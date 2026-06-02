using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;

namespace SmartSam.Pages.Inventory.LinenDelivery;

public class LinenDeliveryDetailModel : BasePageModel
{
    private const int FunctionId = 116;
    private const int PermissionView = 1;
    private const int PermissionUpdate = 2;

    private readonly PermissionService _permissionService;

    public LinenDeliveryDetailModel(IConfiguration config, PermissionService permissionService) : base(config)
    {
        _permissionService = permissionService;
    }

    [BindProperty(SupportsGet = true)]
    public string Mode { get; set; } = "view";

    [BindProperty(SupportsGet = true)]
    public bool Popup { get; set; }

    [BindProperty]
    public LinenDeliveryHeader Header { get; set; } = new LinenDeliveryHeader();

    [BindProperty]
    public string DetailsJson { get; set; } = "[]";

    [BindProperty]
    public string? RedirectUrl { get; set; }

    public PagePermissions PagePerm { get; private set; } = new PagePermissions();
    public bool CanSave { get; private set; }
    public bool IsViewMode => string.Equals(Mode, "view", StringComparison.OrdinalIgnoreCase) || Header.Closed;
    public bool IsTypeLocked => IsViewMode || !string.Equals(Mode, "edit", StringComparison.OrdinalIgnoreCase) || Header.DeliveryType.HasValue;
    public bool IsSpecialLocked => IsViewMode || Header.DeliveryType.HasValue;
    public bool IsPantryNoteLocked { get; private set; }
    public List<LinenDeliveryDetailRow> Details { get; set; } = new List<LinenDeliveryDetailRow>();
    public List<SelectListItem> DeliveryTypeOptions { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> SupplierOptions { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> PantryNoteOptions { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> LocationOptions { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> LinenOptions { get; set; } = new List<SelectListItem>();
    public string LocationOptionsJson { get; set; } = "[]";
    public string LinenOptionsJson { get; set; } = "[]";
    public bool CanToBill => Header.DeliveryID > 0 && CanHeaderCreateBill(Header);

    public IActionResult OnGet(int? id, string mode = "view", string? returnUrl = null)
    {
        PagePerm = GetUserPermissions();
        Mode = NormalizeMode(mode);
        if (Popup)
        {
            Mode = "view";
        }

        RedirectUrl = NormalizeReturnUrl(returnUrl);

        if (Mode == "add")
        {
            if (!PagePerm.HasPermission(PermissionUpdate))
            {
                return RedirectToPage("./Index");
            }

            using var conn = OpenConnection();
            using var trans = conn.BeginTransaction();
            try
            {
                Header = CreateMaster(conn, trans);
                trans.Commit();
            }
            catch
            {
                trans.Rollback();
                throw;
            }

            return RedirectToPage("./LinenDeliveryDetail", new { id = Header.DeliveryID, mode = "edit", returnUrl = RedirectUrl });
        }

        if (!id.HasValue || id.Value <= 0)
        {
            return RedirectToPage("./Index");
        }

        if (!LoadHeader(id.Value))
        {
            return NotFound();
        }

        if (!PagePerm.HasPermission(PermissionView) && !PagePerm.HasPermission(PermissionUpdate))
        {
            return RedirectToPage("./Index");
        }

        if (Mode == "edit" && !PagePerm.HasPermission(PermissionUpdate))
        {
            Mode = "view";
        }

        if (Mode == "edit" && Header.Closed)
        {
            Mode = "view";
        }

        LoadDetails();
        LoadLookupData();
        DetailsJson = JsonSerializer.Serialize(Details);
        CanSave = !IsViewMode && PagePerm.HasPermission(PermissionUpdate);
        return Page();
    }

    public IActionResult OnPost()
    {
        PagePerm = GetUserPermissions();
        Mode = NormalizeMode(Mode);

        if (!PagePerm.HasPermission(PermissionUpdate))
        {
            ModelState.AddModelError(string.Empty, "You do not have permission to update linen delivery.");
        }

        NormalizeInput();
        Details = ParseDetailsJson();
        ValidateInput();

        if (!ModelState.IsValid)
        {
            LoadLookupData();
            CanSave = true;
            return Page();
        }

        using var conn = OpenConnection();
        using var trans = conn.BeginTransaction();

        try
        {
            var currentHeader = GetCurrentHeaderState(conn, trans, Header.DeliveryID);
            if (currentHeader == null)
            {
                trans.Rollback();
                return NotFound();
            }

            if (currentHeader.Closed)
            {
                trans.Rollback();
                ModelState.AddModelError(string.Empty, "Linen delivery is locked and cannot be edited.");
                ReloadCurrentPage(Header.DeliveryID, viewMode: true);
                return Page();
            }

            var shouldCreatePantryDetails = Header.DeliveryType == 1
                && Header.NoteID.HasValue
                && currentHeader.NoteID != Header.NoteID;

            if (!ModelState.IsValid)
            {
                trans.Rollback();
                LoadLookupData();
                CanSave = true;
                return Page();
            }

            UpdateMaster(conn, trans);

            if (shouldCreatePantryDetails)
            {
                using var createCmd = new SqlCommand("exec LN_CreateDeliveryDT_NEW @DeliveryID, @NoteID, @SupplierID", conn, trans);
                createCmd.Parameters.Add("@DeliveryID", SqlDbType.Int).Value = Header.DeliveryID;
                createCmd.Parameters.Add("@NoteID", SqlDbType.Int).Value = Header.NoteID!.Value;
                createCmd.Parameters.Add("@SupplierID", SqlDbType.Int).Value = Header.SupplierID ?? 0;
                createCmd.ExecuteNonQuery();
                LoadDetails(conn, trans);
            }
            else
            {
                SaveDetailRows(conn, trans);
            }

            using (var markCmd = new SqlCommand("exec LN_MarkFullReceiveOnDelevery @DeliveryID", conn, trans))
            {
                markCmd.Parameters.Add("@DeliveryID", SqlDbType.Int).Value = Header.DeliveryID;
                markCmd.ExecuteNonQuery();
            }

            trans.Commit();
        }
        catch (SqlException ex) when (ex.Message.Contains("FK_SV_Bill_SV_SalePoint", StringComparison.OrdinalIgnoreCase))
        {
            trans.Rollback();
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return new JsonResult(new
            {
                success = false,
                message = "Cannot create bill because Sale Point master data is missing."
            });
        }
        catch (InvalidOperationException ex)
        {
            trans.Rollback();
            ModelState.AddModelError(string.Empty, ex.Message);
            LoadLookupData();
            CanSave = true;
            return Page();
        }
        catch
        {
            trans.Rollback();
            throw;
        }

        TempData["SuccessMessage"] = "Linen delivery saved.";
        if (!string.IsNullOrWhiteSpace(RedirectUrl) && Url.IsLocalUrl(RedirectUrl))
        {
            return LocalRedirect(RedirectUrl);
        }

        return RedirectToPage("./LinenDeliveryDetail", new { id = Header.DeliveryID, mode = "edit" });
    }

    public JsonResult OnGetLookupOptions(int? deliveryType, int? supplierId, bool isSpecialLaundry)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionView) && !PagePerm.HasPermission(PermissionUpdate))
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return new JsonResult(new { success = false, message = "Forbidden" });
        }

        Header.DeliveryType = deliveryType;
        Header.SupplierID = supplierId;
        Header.IsSpecialLaundry = isSpecialLaundry;

        using var conn = OpenConnection();
        var locations = LoadLocations(conn);
        var linens = LoadLinenOptions(conn);

        return new JsonResult(new
        {
            success = true,
            locations = locations.Select(x => new
            {
                value = x.Value,
                text = x.Text
            }),
            linens = linens.Select(x => new
            {
                value = x.Value,
                text = x.Text,
                price = x.Group?.Name ?? string.Empty
            })
        });
    }

    public JsonResult OnGetPantryNoteInfo(int id)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionView) && !PagePerm.HasPermission(PermissionUpdate))
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return new JsonResult(new { success = false, message = "Forbidden" });
        }

        if (id <= 0)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return new JsonResult(new { success = false, message = "Invalid pantry linen." });
        }

        using var conn = OpenConnection();
        using var cmd = new SqlCommand("SELECT Des, ISNULL(IsRent, 0) AS IsRent FROM dbo.LN_DeAndReMT WHERE ID = @ID;", conn);
        cmd.Parameters.Add("@ID", SqlDbType.Int).Value = id;
        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return new JsonResult(new { success = false, message = "Pantry linen not found." });
        }

        return new JsonResult(new
        {
            success = true,
            description = Convert.ToString(rd["Des"]) ?? string.Empty,
            isRent = ToBool(rd["IsRent"])
        });
    }

    public JsonResult OnGetBills(int id)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionView) && !PagePerm.HasPermission(PermissionUpdate))
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return new JsonResult(new { success = false, message = "Forbidden" });
        }

        using var conn = OpenConnection();
        var header = GetCurrentHeaderState(conn, null, id);
        if (header == null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return new JsonResult(new { success = false, message = "Delivery not found." });
        }

        if (!CanHeaderCreateBill(header))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return new JsonResult(new { success = false, message = "To Bill is available only for Laundry and In-House Laundry." });
        }

        var bills = LoadBills(conn, null, id);
        return new JsonResult(new
        {
            success = true,
            toBill = header.ToBill,
            bills = bills.Select(x => new
            {
                billId = x.BillID,
                billDate = x.BillDateText,
                apartmentNo = x.ApartmentNo,
                customer = x.Customer,
                vndAmountBefVat = x.VNDAmountBefVAT.ToString("0.##"),
                pctTax = x.PctTax.ToString("0.##"),
                vndAmountVat = x.VNDAmountVAT.ToString("0.##"),
                vndAmount = x.VNDAmount.ToString("0.##"),
                billStatus = x.BillStatus,
                billStatusText = x.BillStatusText
            })
        });
    }

    public JsonResult OnGetPrintPreview(int id, string? linenCode = null)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionView) && !PagePerm.HasPermission(PermissionUpdate))
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return new JsonResult(new { success = false, message = "Forbidden" });
        }

        if (id <= 0)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return new JsonResult(new { success = false, message = "Invalid delivery." });
        }

        if (!LoadHeader(id))
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return new JsonResult(new { success = false, message = "Delivery not found." });
        }

        try
        {
            using var conn = OpenConnection();
            var preview = LoadDeliveryPrintPreview(conn, id, linenCode);

            return new JsonResult(new
            {
                success = true,
                deliveryId = Header.DeliveryID,
                description = Header.Description,
                deliveryDate = Header.DeliveryDate.ToString("yyyy-MM-dd"),
                supplierName = preview.SupplierName,
                deliveryTypeName = preview.DeliveryTypeName,
                rows = preview.Rows.Select(x => new
                {
                    location = x.Location,
                    linenCode = x.LinenCode,
                    isChild = x.IsChild,
                    express = x.Express,
                    quantity = x.Quantity.ToString("0.00"),
                    price = x.Price.ToString("0.##"),
                    amount = x.Amount.ToString("0.##"),
                    note = x.Note
                })
            });
        }
        catch (Exception ex)
        {
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            return new JsonResult(new { success = false, message = ex.Message });
        }
    }

    public JsonResult OnPostCreateBill(int deliveryId, DateTime? billDate, string? detailsJson)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionUpdate))
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return new JsonResult(new { success = false, message = "Forbidden" });
        }

        if (deliveryId <= 0)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return new JsonResult(new { success = false, message = "Invalid delivery." });
        }

        if (!billDate.HasValue)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return new JsonResult(new { success = false, message = "Bill date is required." });
        }

        if (!TryGetCurrentEmployeeId(out var employeeId))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return new JsonResult(new { success = false, message = "Current user is not linked to employee data." });
        }

        using var conn = OpenConnection();
        using var trans = conn.BeginTransaction();
        try
        {
            var header = GetCurrentHeaderState(conn, trans, deliveryId);
            if (header == null)
            {
                trans.Rollback();
                Response.StatusCode = StatusCodes.Status404NotFound;
                return new JsonResult(new { success = false, message = "Delivery not found." });
            }

            Header = header;
            Header.DeliveryID = deliveryId;
            DetailsJson = string.IsNullOrWhiteSpace(detailsJson) ? "[]" : detailsJson;
            Details = ParseDetailsJson();

            var detailError = ValidateDetailRowsForSave();
            if (!string.IsNullOrEmpty(detailError))
            {
                trans.Rollback();
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return new JsonResult(new { success = false, message = detailError });
            }

            if (!CanHeaderCreateBill(header))
            {
                trans.Rollback();
                Response.StatusCode = StatusCodes.Status400BadRequest;
                return new JsonResult(new { success = false, message = "To Bill is available only for Laundry and In-House Laundry." });
            }

            if (header.ToBill)
            {
                var existingBills = LoadBills(conn, trans, deliveryId);
                trans.Commit();
                return new JsonResult(new
                {
                    success = true,
                    bills = existingBills.Select(x => new
                    {
                        billId = x.BillID,
                        billDate = x.BillDateText,
                        apartmentNo = x.ApartmentNo,
                        customer = x.Customer,
                        vndAmountBefVat = x.VNDAmountBefVAT.ToString("0.##"),
                        pctTax = x.PctTax.ToString("0.##"),
                        vndAmountVat = x.VNDAmountVAT.ToString("0.##"),
                        vndAmount = x.VNDAmount.ToString("0.##"),
                        billStatus = x.BillStatus,
                        billStatusText = x.BillStatusText
                    })
                });
            }

            SaveDetailRows(conn, trans);

            using (var createCmd = new SqlCommand("exec LN_CreateBill @DeliveryID, @BillDate, @OperatorID", conn, trans))
            {
                createCmd.Parameters.Add("@DeliveryID", SqlDbType.Int).Value = deliveryId;
                createCmd.Parameters.Add("@BillDate", SqlDbType.SmallDateTime).Value = billDate.Value.Date;
                createCmd.Parameters.Add("@OperatorID", SqlDbType.Int).Value = employeeId;
                createCmd.ExecuteNonQuery();
            }

            using (var markCmd = new SqlCommand("UPDATE dbo.LN_DeliveryMT SET ToBill = 1 WHERE DeliveryID = @DeliveryID;", conn, trans))
            {
                markCmd.Parameters.Add("@DeliveryID", SqlDbType.Int).Value = deliveryId;
                markCmd.ExecuteNonQuery();
            }

            var bills = LoadBills(conn, trans, deliveryId);
            trans.Commit();

            return new JsonResult(new
            {
                success = true,
                bills = bills.Select(x => new
                {
                    billId = x.BillID,
                    billDate = x.BillDateText,
                    apartmentNo = x.ApartmentNo,
                    customer = x.Customer,
                    vndAmountBefVat = x.VNDAmountBefVAT.ToString("0.##"),
                    pctTax = x.PctTax.ToString("0.##"),
                    vndAmountVat = x.VNDAmountVAT.ToString("0.##"),
                    vndAmount = x.VNDAmount.ToString("0.##"),
                    billStatus = x.BillStatus,
                    billStatusText = x.BillStatusText
                })
            });
        }
        catch
        {
            trans.Rollback();
            throw;
        }
    }

    public JsonResult OnPostMarkNoNeedBill(int deliveryId, int detailId)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionUpdate))
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return new JsonResult(new { success = false, message = "Forbidden" });
        }

        if (deliveryId <= 0 || detailId <= 0)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return new JsonResult(new { success = false, message = "Invalid detail row." });
        }

        using var conn = OpenConnection();
        var header = GetCurrentHeaderState(conn, null, deliveryId);
        if (header == null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return new JsonResult(new { success = false, message = "Delivery not found." });
        }

        if (!CanHeaderMarkNoNeedBill(header))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return new JsonResult(new { success = false, message = "No Need Bill is available only for non-special Laundry and In-House Laundry." });
        }

        using var cmd = new SqlCommand(@"
UPDATE dbo.LN_DeliveryDT
SET NoNeedBill = 1
WHERE ID = @ID
  AND DeliveryID = @DeliveryID;", conn);
        cmd.Parameters.Add("@ID", SqlDbType.Int).Value = detailId;
        cmd.Parameters.Add("@DeliveryID", SqlDbType.Int).Value = deliveryId;
        var affected = cmd.ExecuteNonQuery();
        if (affected <= 0)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return new JsonResult(new { success = false, message = "Detail row not found." });
        }

        return new JsonResult(new { success = true, detailId = detailId });
    }

    private LinenDeliveryHeader CreateMaster(SqlConnection conn, SqlTransaction trans)
    {
        var header = new LinenDeliveryHeader
        {
            DeliveryDate = DateTime.Today,
            Description = GetGeneratedDescription(conn, trans)
        };

        using var cmd = new SqlCommand(@"
INSERT INTO dbo.LN_DeliveryMT (DeliveryDate, Des)
VALUES (@DeliveryDate, @Des);
SELECT CONVERT(int, SCOPE_IDENTITY());", conn, trans);
        cmd.Parameters.Add("@DeliveryDate", SqlDbType.DateTime).Value = DateTime.Today;
        cmd.Parameters.Add("@Des", SqlDbType.VarChar, 100).Value = header.Description;
        header.DeliveryID = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        return header;
    }

    private string GetGeneratedDescription(SqlConnection conn, SqlTransaction trans)
    {
        using var cmd = new SqlCommand("exec LN_GenNo 2, null", conn, trans);
        using var rd = cmd.ExecuteReader();
        if (rd.Read())
        {
            return Convert.ToString(rd["strResult"]) ?? string.Empty;
        }

        return string.Empty;
    }

    private bool LoadHeader(int deliveryId)
    {
        using var conn = OpenConnection();
        using var cmd = new SqlCommand(@"
SELECT DeliveryID,
       DeliveryDate,
       ISNULL(Des, '') AS Des,
       ISNULL(Closed, 0) AS Closed,
       DeliveryType,
       NoteID,
       SupplierID,
       ISNULL(ToBill, 0) AS ToBill,
       ISNULL(FullReceive, 0) AS FullReceive,
       ISNULL(IsRent, 0) AS IsRent,
       ISNULL(IsSpecialLaundry, 0) AS IsSpecialLaundry
FROM dbo.LN_DeliveryMT
WHERE DeliveryID = @DeliveryID;", conn);
        cmd.Parameters.Add("@DeliveryID", SqlDbType.Int).Value = deliveryId;

        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
        {
            return false;
        }

        Header = new LinenDeliveryHeader
        {
            DeliveryID = Convert.ToInt32(rd["DeliveryID"]),
            DeliveryDate = rd["DeliveryDate"] == DBNull.Value ? DateTime.Today : Convert.ToDateTime(rd["DeliveryDate"]),
            Description = Convert.ToString(rd["Des"]) ?? string.Empty,
            Closed = ToBool(rd["Closed"]),
            DeliveryType = rd["DeliveryType"] == DBNull.Value ? null : Convert.ToInt32(rd["DeliveryType"]),
            NoteID = rd["NoteID"] == DBNull.Value ? null : Convert.ToInt32(rd["NoteID"]),
            SupplierID = rd["SupplierID"] == DBNull.Value ? null : Convert.ToInt32(rd["SupplierID"]),
            ToBill = ToBool(rd["ToBill"]),
            FullReceive = ToBool(rd["FullReceive"]),
            IsRent = ToBool(rd["IsRent"]),
            IsSpecialLaundry = ToBool(rd["IsSpecialLaundry"])
        };

        return true;
    }

    private void LoadDetails()
    {
        using var conn = OpenConnection();
        LoadDetails(conn, null);
    }

    private void LoadDetails(SqlConnection conn, SqlTransaction? trans)
    {
        Details = new List<LinenDeliveryDetailRow>();
        using var cmd = new SqlCommand(@"
SELECT dt.ID,
       dt.DeliveryID,
       ISNULL(dt.Location, '') AS Location,
       ISNULL(dt.LinenCode, '') AS LinenCode,
       ISNULL(dt.Express, 0) AS Express,
       ISNULL(dt.IsChild, 0) AS IsChild,
       ISNULL(dt.Quantity, 0) AS Quantity,
       ISNULL(dt.Price, 0) AS Price,
       ISNULL(dt.Amount, 0) AS Amount,
       ISNULL(dt.Note, '') AS Note,
       ISNULL(dt.QuantityRe, 0) AS QuantityRe,
       ISNULL(dt.NoNeedBill, 0) AS NoNeedBill,
       dt.LocationID,
       dt.LinnenID,
       ISNULL(dt.OutOfOffer, 0) AS OutOfOffer,
       ISNULL(dt.CollectToBill, 0) AS CollectToBill
FROM dbo.LN_DeliveryDT dt
WHERE dt.DeliveryID = @DeliveryID
ORDER BY dt.ID;", conn, trans);
        cmd.Parameters.Add("@DeliveryID", SqlDbType.Int).Value = Header.DeliveryID;

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            Details.Add(new LinenDeliveryDetailRow
            {
                ID = Convert.ToInt32(rd["ID"]),
                DeliveryID = Convert.ToInt32(rd["DeliveryID"]),
                Location = Convert.ToString(rd["Location"]) ?? string.Empty,
                LinenCode = Convert.ToString(rd["LinenCode"]) ?? string.Empty,
                Express = ToBool(rd["Express"]),
                IsChild = ToBool(rd["IsChild"]),
                Quantity = ToDecimal(rd["Quantity"]),
                Price = ToDecimal(rd["Price"]),
                Amount = ToDecimal(rd["Amount"]),
                Note = Convert.ToString(rd["Note"]) ?? string.Empty,
                QuantityRe = ToDecimal(rd["QuantityRe"]),
                NoNeedBill = ToBool(rd["NoNeedBill"]),
                LocationID = rd["LocationID"] == DBNull.Value ? null : Convert.ToInt32(rd["LocationID"]),
                LinnenID = rd["LinnenID"] == DBNull.Value ? null : Convert.ToInt32(rd["LinnenID"]),
                OutOfOffer = ToBool(rd["OutOfOffer"]),
                CollectToBill = ToBool(rd["CollectToBill"])
            });
        }

        IsPantryNoteLocked = Details.Count > 0;
    }

    private void LoadLookupData()
    {
        using var conn = OpenConnection();
        DeliveryTypeOptions = LoadDeliveryTypes(conn);
        SupplierOptions = LoadSuppliers(conn);
        PantryNoteOptions = LoadPantryNotes(conn);
        LocationOptions = LoadLocations(conn);
        LinenOptions = LoadLinenOptions(conn);
        LocationOptionsJson = JsonSerializer.Serialize(LocationOptions.Select(x => new { value = x.Value, text = x.Text }));
        LinenOptionsJson = JsonSerializer.Serialize(LinenOptions.Select(x => new { value = x.Value, text = x.Text, price = x.Group?.Name ?? string.Empty }));
    }

    private List<SelectListItem> LoadDeliveryTypes(SqlConnection conn)
    {
        var items = new List<SelectListItem>
        {
            new SelectListItem { Value = string.Empty, Text = "-- Select --" }
        };

        using var cmd = new SqlCommand(@"
SELECT LaundryTypeID, LaundryTypeName
FROM dbo.LN_LaudryType
WHERE ISNULL(IsActive, 0) = 1
ORDER BY LaundryTypeID;", conn);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            items.Add(new SelectListItem
            {
                Value = Convert.ToInt32(rd["LaundryTypeID"]).ToString(),
                Text = Convert.ToString(rd["LaundryTypeName"]) ?? string.Empty
            });
        }

        return items;
    }

    private List<SelectListItem> LoadSuppliers(SqlConnection conn)
    {
        var items = new List<SelectListItem>
        {
            new SelectListItem { Value = string.Empty, Text = "-- Select --" }
        };

        using var cmd = new SqlCommand(@"
SELECT SupplierID, SupplierName
FROM dbo.PC_Suppliers
WHERE ISNULL(IsLinen, 0) = 1
  AND ISNULL(Status, 0) >= 0
  AND ISNULL(Status, 0) < 5
ORDER BY SupplierID;", conn);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            items.Add(new SelectListItem
            {
                Value = Convert.ToInt32(rd["SupplierID"]).ToString(),
                Text = Convert.ToString(rd["SupplierName"]) ?? string.Empty
            });
        }

        return items;
    }

    private List<SelectListItem> LoadPantryNotes(SqlConnection conn)
    {
        var items = new List<SelectListItem>
        {
            new SelectListItem { Value = string.Empty, Text = "-- Select --" }
        };

        var sql = Header.NoteID.HasValue
            ? "SELECT ID, Des FROM dbo.LN_DeAndReMT WHERE 1 = 1 ORDER BY ID DESC;"
            : @"
SELECT mt.ID, mt.Des
FROM dbo.LN_DeAndReMT mt
WHERE mt.ID NOT IN (
    SELECT NoteID
    FROM dbo.LN_DeliveryMT
    WHERE NoteID IS NOT NULL
)
ORDER BY mt.ID DESC;";

        using var cmd = new SqlCommand(sql, conn);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            items.Add(new SelectListItem
            {
                Value = Convert.ToInt32(rd["ID"]).ToString(),
                Text = Convert.ToString(rd["Des"]) ?? string.Empty
            });
        }

        return items;
    }

    private List<SelectListItem> LoadLocations(SqlConnection conn)
    {
        var items = new List<SelectListItem>
        {
            new SelectListItem { Value = string.Empty, Text = "-- Select --" }
        };

        var sql = BuildLocationSql();
        using var cmd = new SqlCommand(sql, conn);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            items.Add(new SelectListItem
            {
                Value = Convert.ToInt32(rd["locationID"]).ToString(),
                Text = Convert.ToString(rd["Location"]) ?? string.Empty
            });
        }

        return items;
    }

    private string BuildLocationSql()
    {
        if (Header.DeliveryType == 1)
        {
            return "SELECT locationID, Location FROM dbo.ViewLNLocation_New WHERE locationID = 1000;";
        }

        if (Header.DeliveryType == 2)
        {
            return "SELECT locationID, Location FROM dbo.ViewLNLocation_New WHERE locationID = 1001 ORDER BY Location;";
        }

        if (Header.DeliveryType == 3)
        {
            if (Header.IsSpecialLaundry)
            {
                return @"
SELECT dbo.AM_Apmt.ApmtID AS locationID, dbo.CM_Contract.CurrentApartmentNo AS Location
FROM dbo.AM_Apmt
INNER JOIN dbo.CM_Contract ON dbo.AM_Apmt.ApartmentNo = dbo.CM_Contract.CurrentApartmentNo
INNER JOIN dbo.CM_ContractService ON dbo.CM_Contract.ContractID = dbo.CM_ContractService.ContractID
WHERE dbo.CM_ContractService.ServiceID IN (1217, 1219)
  AND dbo.CM_Contract.ContractStatus = 2
  AND GETDATE() >= dbo.CM_ContractService.ServiceFromDate
  AND GETDATE() <= DATEADD(second, 86399, CAST(dbo.CM_ContractService.ServiceToDate AS datetime))
ORDER BY dbo.CM_Contract.CurrentApartmentNo;";
            }

            return "SELECT locationID, Location FROM dbo.ViewLNLocation_New WHERE locationID < 1000 ORDER BY Location;";
        }

        return "SELECT locationID, Location FROM dbo.ViewLNLocation_New WHERE Location <> 'Uniform' ORDER BY Location;";
    }

    private List<SelectListItem> LoadLinenOptions(SqlConnection conn)
    {
        var items = new List<SelectListItem>
        {
            new SelectListItem { Value = string.Empty, Text = "-- Select --" }
        };

        var sql = BuildLinenSql();
        using var cmd = new SqlCommand(sql, conn);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            items.Add(new SelectListItem
            {
                Value = Convert.ToInt32(rd["ID"]).ToString(),
                Text = Convert.ToString(rd["LinnenCode"]) ?? string.Empty,
                Group = new SelectListGroup { Name = Convert.ToString(rd["DisplayPrice"]) ?? "0" }
            });
        }

        return items;
    }

    private string BuildLinenSql()
    {
        var priceColumn = GetPriceColumn(Header.SupplierID);

        if (Header.DeliveryType == 1)
        {
            return $@"
SELECT ID, LinnenCode, ISNULL({priceColumn}, 0) AS DisplayPrice
FROM dbo.LN_Linnen
WHERE ISNULL(IsLinen, 0) = 1
  AND ISNULL({priceColumn}, 0) > 0
ORDER BY LinnenCode;";
        }

        if (Header.DeliveryType == 2)
        {
            return $@"
SELECT ID, LinnenCode, ISNULL({priceColumn}, 0) AS DisplayPrice
FROM dbo.LN_Linnen
WHERE ISNULL({priceColumn}, 0) > 0
  AND ISNULL(IsUniform, 0) = 1
ORDER BY LinnenCode;";
        }

        if (Header.DeliveryType == 3)
        {
            if (Header.IsSpecialLaundry)
            {
                return @"
SELECT ID, LinnenCode, TRY_CONVERT(decimal(18,2), VNDPriceNew3) AS DisplayPrice
FROM dbo.LN_Linnen
WHERE (ISNULL(IsUniform, 0) = 0)
  AND (ISNULL(IsLinen, 0) = 0)
ORDER BY LinnenCode;";
            }

            return $@"
SELECT ID, LinnenCode, ISNULL({priceColumn}, 0) AS DisplayPrice
FROM dbo.LN_Linnen
WHERE ISNULL({priceColumn}, 0) > 0
  AND (ISNULL(IsUniform, 0) = 0)
  AND (ISNULL(IsLinen, 0) = 0)
ORDER BY LinnenCode;";
        }

        return @"
SELECT ID, LinnenCode, TRY_CONVERT(decimal(18,2), VNDPriceNew3) AS DisplayPrice
FROM dbo.LN_Linnen
WHERE TRY_CONVERT(decimal(18,2), VNDPriceNew3) > 0
  AND (ISNULL(IsUniform, 0) = 0)
ORDER BY LinnenCode;";
    }

    private static string GetPriceColumn(int? supplierId)
    {
        return supplierId switch
        {
            1635 => "VNDPrice",
            1636 => "VNDPriceNew1",
            1672 => "TRY_CONVERT(decimal(18,2), VNDPriceNew2)",
            1845 => "TRY_CONVERT(decimal(18,2), VNDPriceNew3)",
            2043 => "TRY_CONVERT(decimal(18,2), VNDPriceNew3)",
            _ => "0"
        };
    }

    private LinenDeliveryHeader? GetCurrentHeaderState(SqlConnection conn, SqlTransaction? trans, int deliveryId)
    {
        using var cmd = new SqlCommand(@"
SELECT DeliveryID,
       DeliveryType,
       NoteID,
       SupplierID,
       ISNULL(IsSpecialLaundry, 0) AS IsSpecialLaundry,
       ISNULL(ToBill, 0) AS ToBill,
       ISNULL(Closed, 0) AS Closed
FROM dbo.LN_DeliveryMT
WHERE DeliveryID = @DeliveryID;", conn, trans);
        cmd.Parameters.Add("@DeliveryID", SqlDbType.Int).Value = deliveryId;
        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
        {
            return null;
        }

        return new LinenDeliveryHeader
        {
            DeliveryID = Convert.ToInt32(rd["DeliveryID"]),
            DeliveryType = rd["DeliveryType"] == DBNull.Value ? null : Convert.ToInt32(rd["DeliveryType"]),
            NoteID = rd["NoteID"] == DBNull.Value ? null : Convert.ToInt32(rd["NoteID"]),
            SupplierID = rd["SupplierID"] == DBNull.Value ? null : Convert.ToInt32(rd["SupplierID"]),
            IsSpecialLaundry = ToBool(rd["IsSpecialLaundry"]),
            ToBill = ToBool(rd["ToBill"]),
            Closed = ToBool(rd["Closed"])
        };
    }

    private void ReloadCurrentPage(int deliveryId, bool viewMode)
    {
        LoadHeader(deliveryId);
        LoadDetails();
        LoadLookupData();
        DetailsJson = JsonSerializer.Serialize(Details);
        Mode = viewMode ? "view" : Mode;
        CanSave = !IsViewMode && PagePerm.HasPermission(PermissionUpdate);
    }

    private List<LinenDeliveryBillRow> LoadBills(SqlConnection conn, SqlTransaction? trans, int deliveryId)
    {
        var rows = new List<LinenDeliveryBillRow>();
        using var cmd = new SqlCommand(@"
SELECT dbo.SV_Bill.BillID,
       dbo.SV_Bill.BillDate,
       ISNULL(dbo.SV_Bill.ApartmentNo, '') AS ApartmentNo,
       ISNULL(dbo.CM_Customer.CustomerName, '') + '(' + CAST(dbo.CM_Customer.CustomerID AS varchar(10)) + ')' AS Customer,
       ISNULL(dbo.SV_Bill.VNDAmountBefVAT, 0) AS VNDAmountBefVAT,
       ISNULL(dbo.SV_Bill.PctTax, 0) AS PctTax,
       ISNULL(dbo.SV_Bill.VNDAmountVAT, 0) AS VNDAmountVAT,
       ISNULL(dbo.SV_Bill.VNDAmount, 0) AS VNDAmount,
       ISNULL(dbo.SV_Bill.BillStatus, 0) AS BillStatus,
       ISNULL(dbo.SV_BillStatus.StatusName, '') AS BillStatusName
FROM dbo.SV_Bill
INNER JOIN dbo.CM_Customer ON dbo.SV_Bill.CustomerID = dbo.CM_Customer.CustomerID
LEFT JOIN dbo.SV_BillStatus ON dbo.SV_BillStatus.StatusID = dbo.SV_Bill.BillStatus
WHERE dbo.SV_Bill.LinenDeliveryID = @DeliveryID
ORDER BY dbo.SV_Bill.BillDate DESC, dbo.SV_Bill.BillID DESC;", conn, trans);
        cmd.Parameters.Add("@DeliveryID", SqlDbType.Int).Value = deliveryId;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(new LinenDeliveryBillRow
            {
                BillID = Convert.ToInt32(rd["BillID"]),
                BillDate = rd["BillDate"] == DBNull.Value ? null : Convert.ToDateTime(rd["BillDate"]),
                ApartmentNo = Convert.ToString(rd["ApartmentNo"]) ?? string.Empty,
                Customer = Convert.ToString(rd["Customer"]) ?? string.Empty,
                VNDAmountBefVAT = ToDecimal(rd["VNDAmountBefVAT"]),
                PctTax = ToDecimal(rd["PctTax"]),
                VNDAmountVAT = ToDecimal(rd["VNDAmountVAT"]),
                VNDAmount = ToDecimal(rd["VNDAmount"]),
                BillStatus = Convert.ToInt32(rd["BillStatus"]),
                BillStatusName = Convert.ToString(rd["BillStatusName"]) ?? string.Empty
            });
        }

        return rows;
    }

    private LinenDeliveryPrintPreviewResult LoadDeliveryPrintPreview(SqlConnection conn, int deliveryId, string? linenCode)
    {
        var result = new LinenDeliveryPrintPreviewResult();
        using var cmd = new SqlCommand(@"
SELECT mt.DeliveryID,
       mt.DeliveryDate,
       ISNULL(mt.Des, '') AS Des,
       ISNULL(tp.LaundryTypeName, '') AS LaundryTypeName,
       ISNULL(sp.SupplierName, '') AS SupplierName,
       ISNULL(dt.Location, '') AS Location,
       ISNULL(CASE WHEN ISNULL(dt.LinenCode, '') <> '' THEN dt.LinenCode ELSE ln.LinnenCode END, '') AS LinenCode,
       ISNULL(dt.IsChild, 0) AS IsChild,
       ISNULL(dt.Express, 0) AS Express,
       ISNULL(dt.Quantity, 0) AS Quantity,
       ISNULL(dt.Price, 0) AS Price,
       ISNULL(dt.Amount, 0) AS Amount,
       ISNULL(dt.Note, '') AS Note
FROM dbo.LN_DeliveryMT mt
INNER JOIN dbo.LN_DeliveryDT dt ON mt.DeliveryID = dt.DeliveryID
LEFT JOIN dbo.LN_LaudryType tp ON tp.LaundryTypeID = mt.DeliveryType
LEFT JOIN dbo.PC_Suppliers sp ON sp.SupplierID = mt.SupplierID
LEFT JOIN dbo.LN_Linnen ln ON ln.ID = dt.LinnenID
WHERE mt.DeliveryID = @DeliveryID
  AND (@LinenCode = '' OR ISNULL(CASE WHEN ISNULL(dt.LinenCode, '') <> '' THEN dt.LinenCode ELSE ln.LinnenCode END, '') = @LinenCode)
ORDER BY dt.ID;", conn);
        cmd.Parameters.Add("@DeliveryID", SqlDbType.Int).Value = deliveryId;
        cmd.Parameters.Add("@LinenCode", SqlDbType.VarChar, 50).Value = (linenCode ?? string.Empty).Trim();

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            if (string.IsNullOrWhiteSpace(result.SupplierName))
            {
                result.SupplierName = Convert.ToString(rd["SupplierName"]) ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(result.DeliveryTypeName))
            {
                result.DeliveryTypeName = Convert.ToString(rd["LaundryTypeName"]) ?? string.Empty;
            }

            result.Rows.Add(new LinenDeliveryPrintPreviewRow
            {
                Location = Convert.ToString(rd["Location"]) ?? string.Empty,
                LinenCode = Convert.ToString(rd["LinenCode"]) ?? string.Empty,
                IsChild = ToBool(rd["IsChild"]),
                Express = ToBool(rd["Express"]),
                Quantity = ToDecimal(rd["Quantity"]),
                Price = ToDecimal(rd["Price"]),
                Amount = ToDecimal(rd["Amount"]),
                Note = Convert.ToString(rd["Note"]) ?? string.Empty
            });
        }

        return result;
    }

    public static string GetBillStatusText(int billStatus, string billStatusName)
    {
        if (!string.IsNullOrWhiteSpace(billStatusName))
        {
            return billStatusName.Trim();
        }

        if (billStatus == 1)
        {
            return "Open";
        }

        if (billStatus == 5)
        {
            return "Cancelled";
        }

        return "Unknown";
    }

    private int GetDetailCount(SqlConnection conn, SqlTransaction trans, int deliveryId)
    {
        using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.LN_DeliveryDT WHERE DeliveryID = @DeliveryID;", conn, trans);
        cmd.Parameters.Add("@DeliveryID", SqlDbType.Int).Value = deliveryId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private void UpdateMaster(SqlConnection conn, SqlTransaction trans)
    {
        if (Header.DeliveryType == 1)
        {
            using (var duplicateCmd = new SqlCommand(@"
SELECT COUNT(1)
FROM dbo.LN_DeliveryMT
WHERE NoteID = @NoteID
  AND DeliveryID <> @DeliveryID;", conn, trans))
            {
                duplicateCmd.Parameters.Add("@NoteID", SqlDbType.Int).Value = Header.NoteID ?? 0;
                duplicateCmd.Parameters.Add("@DeliveryID", SqlDbType.Int).Value = Header.DeliveryID;
                var duplicateCount = Convert.ToInt32(duplicateCmd.ExecuteScalar() ?? 0);
                if (duplicateCount > 0)
                {
                    throw new InvalidOperationException("PLS select another Pantry-Linen");
                }
            }
        }

        using var cmd = new SqlCommand(@"
UPDATE dbo.LN_DeliveryMT
SET DeliveryDate = @DeliveryDate,
    Des = @Des,
    Closed = @Closed,
    IsRent = @IsRent,
    DeliveryType = @DeliveryType,
    NoteID = @NoteID,
    SupplierID = @SupplierID,
    IsSpecialLaundry = @IsSpecialLaundry
WHERE DeliveryID = @DeliveryID;", conn, trans);
        cmd.Parameters.Add("@DeliveryID", SqlDbType.Int).Value = Header.DeliveryID;
        cmd.Parameters.Add("@DeliveryDate", SqlDbType.DateTime).Value = Header.DeliveryDate.Date;
        cmd.Parameters.Add("@Des", SqlDbType.VarChar, 100).Value = Header.Description;
        cmd.Parameters.Add("@Closed", SqlDbType.Bit).Value = Header.Closed;
        cmd.Parameters.Add("@IsRent", SqlDbType.Bit).Value = Header.IsRent;
        cmd.Parameters.Add("@DeliveryType", SqlDbType.Int).Value = Header.DeliveryType ?? (object)DBNull.Value;
        cmd.Parameters.Add("@NoteID", SqlDbType.Int).Value = Header.DeliveryType == 1 && Header.NoteID.HasValue ? Header.NoteID.Value : (object)DBNull.Value;
        cmd.Parameters.Add("@SupplierID", SqlDbType.Int).Value = (Header.DeliveryType == 1 || Header.DeliveryType == 2 || Header.DeliveryType == 3) && Header.SupplierID.HasValue
            ? Header.SupplierID.Value
            : (object)DBNull.Value;
        cmd.Parameters.Add("@IsSpecialLaundry", SqlDbType.Bit).Value = (Header.DeliveryType == 2 || Header.DeliveryType == 3 || Header.DeliveryType == 5) && Header.IsSpecialLaundry;
        cmd.ExecuteNonQuery();
    }

    private void SaveDetailRows(SqlConnection conn, SqlTransaction trans)
    {
        var locationMap = GetLocationMap(conn, trans);
        var linenMap = GetLinenMap(conn, trans);

        foreach (var row in Details)
        {
            if (!row.LocationID.HasValue || !row.LinnenID.HasValue)
            {
                continue;
            }

            if (!locationMap.TryGetValue(row.LocationID.Value, out var locationName))
            {
                continue;
            }

            if (!linenMap.TryGetValue(row.LinnenID.Value, out var linenCode))
            {
                continue;
            }

            row.Location = locationName;
            row.LinenCode = linenCode;
            row.Amount = Math.Round(row.Quantity * row.Price * (row.Express ? 2 : 1), 2);
        }

        var keptIds = Details.Where(x => x.ID > 0).Select(x => x.ID).ToList();
        var deleteSql = keptIds.Count == 0
            ? "DELETE FROM dbo.LN_DeliveryDT WHERE DeliveryID = @DeliveryID;"
            : $"DELETE FROM dbo.LN_DeliveryDT WHERE DeliveryID = @DeliveryID AND ID NOT IN ({string.Join(", ", keptIds)});";

        using (var deleteCmd = new SqlCommand(deleteSql, conn, trans))
        {
            deleteCmd.Parameters.Add("@DeliveryID", SqlDbType.Int).Value = Header.DeliveryID;
            deleteCmd.ExecuteNonQuery();
        }

        foreach (var row in Details.Where(x => x.LocationID.HasValue && x.LinnenID.HasValue))
        {
            if (row.ID > 0)
            {
                using var updateCmd = new SqlCommand(@"
UPDATE dbo.LN_DeliveryDT
SET Location = @Location,
    LinenCode = @LinenCode,
    LocationID = @LocationID,
    LinnenID = @LinnenID,
    Express = @Express,
    IsChild = @IsChild,
    Quantity = @Quantity,
    Price = @Price,
    Amount = @Amount,
    Note = @Note
WHERE ID = @ID
  AND DeliveryID = @DeliveryID;", conn, trans);
                BindDetailParams(updateCmd, row);
                updateCmd.Parameters.Add("@ID", SqlDbType.Int).Value = row.ID;
                updateCmd.ExecuteNonQuery();
            }
            else
            {
                using var insertCmd = new SqlCommand(@"
INSERT INTO dbo.LN_DeliveryDT
    (DeliveryID, Location, LinenCode, IsChild, Express, Quantity, Price, Amount, Note, LocationID, LinnenID)
VALUES
    (@DeliveryID, @Location, @LinenCode, @IsChild, @Express, @Quantity, @Price, @Amount, @Note, @LocationID, @LinnenID);", conn, trans);
                BindDetailParams(insertCmd, row);
                insertCmd.ExecuteNonQuery();
            }
        }
    }

    private static void BindDetailParams(SqlCommand cmd, LinenDeliveryDetailRow row)
    {
        cmd.Parameters.Add("@DeliveryID", SqlDbType.Int).Value = row.DeliveryID;
        cmd.Parameters.Add("@Location", SqlDbType.VarChar, 50).Value = row.Location ?? string.Empty;
        cmd.Parameters.Add("@LinenCode", SqlDbType.VarChar, 50).Value = row.LinenCode ?? string.Empty;
        cmd.Parameters.Add("@IsChild", SqlDbType.Bit).Value = row.IsChild;
        cmd.Parameters.Add("@Express", SqlDbType.Bit).Value = row.Express;
        cmd.Parameters.Add("@Quantity", SqlDbType.Decimal).Value = row.Quantity;
        cmd.Parameters["@Quantity"].Precision = 10;
        cmd.Parameters["@Quantity"].Scale = 2;
        cmd.Parameters.Add("@Price", SqlDbType.Decimal).Value = row.Price;
        cmd.Parameters["@Price"].Precision = 18;
        cmd.Parameters["@Price"].Scale = 2;
        cmd.Parameters.Add("@Amount", SqlDbType.Decimal).Value = row.Amount;
        cmd.Parameters["@Amount"].Precision = 18;
        cmd.Parameters["@Amount"].Scale = 2;
        cmd.Parameters.Add("@Note", SqlDbType.VarChar, 100).Value = row.Note ?? string.Empty;
        cmd.Parameters.Add("@LocationID", SqlDbType.Int).Value = row.LocationID ?? (object)DBNull.Value;
        cmd.Parameters.Add("@LinnenID", SqlDbType.Int).Value = row.LinnenID ?? (object)DBNull.Value;
    }

    private Dictionary<int, string> GetLocationMap(SqlConnection conn, SqlTransaction trans)
    {
        var result = new Dictionary<int, string>();
        using var cmd = new SqlCommand("SELECT locationID, Location FROM dbo.ViewLNLocation_New;", conn, trans);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            result[Convert.ToInt32(rd["locationID"])] = Convert.ToString(rd["Location"]) ?? string.Empty;
        }

        return result;
    }

    private Dictionary<int, string> GetLinenMap(SqlConnection conn, SqlTransaction trans)
    {
        var result = new Dictionary<int, string>();
        using var cmd = new SqlCommand("SELECT ID, LinnenCode FROM dbo.LN_Linnen;", conn, trans);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            result[Convert.ToInt32(rd["ID"])] = Convert.ToString(rd["LinnenCode"]) ?? string.Empty;
        }

        return result;
    }

    private void NormalizeInput()
    {
        Header.Description = (Header.Description ?? string.Empty).Trim();
        DetailsJson = string.IsNullOrWhiteSpace(DetailsJson) ? "[]" : DetailsJson;
    }

    private void ValidateInput()
    {
        if (Header.DeliveryDate == default)
        {
            ModelState.AddModelError(string.Empty, "Delivery date is required.");
        }

        if (string.IsNullOrWhiteSpace(Header.Description))
        {
            ModelState.AddModelError(string.Empty, "Description is required.");
        }

        if (!Header.DeliveryType.HasValue)
        {
            ModelState.AddModelError(string.Empty, "Type is required.");
            return;
        }

        if (Header.DeliveryType == 1)
        {
            if (!Header.NoteID.HasValue || Header.NoteID.Value <= 0)
            {
                ModelState.AddModelError(string.Empty, "Pantry Linen is required.");
            }

            if (!Header.SupplierID.HasValue || Header.SupplierID.Value <= 0)
            {
                ModelState.AddModelError(string.Empty, "Supplier is required.");
            }
        }
        else if (Header.DeliveryType == 2 || Header.DeliveryType == 3)
        {
            if (!Header.SupplierID.HasValue || Header.SupplierID.Value <= 0)
            {
                ModelState.AddModelError(string.Empty, "Supplier is required.");
            }
        }

        foreach (var row in Details)
        {
            if (!row.LocationID.HasValue || row.LocationID.Value <= 0)
            {
                ModelState.AddModelError(string.Empty, "Location is required for every detail row.");
                break;
            }

            if (!row.LinnenID.HasValue || row.LinnenID.Value <= 0)
            {
                ModelState.AddModelError(string.Empty, "Linen is required for every detail row.");
                break;
            }
        }
    }

    private string ValidateDetailRowsForSave()
    {
        foreach (var row in Details)
        {
            if (!row.LocationID.HasValue || row.LocationID.Value <= 0)
            {
                return "Location is required for every detail row.";
            }

            if (!row.LinnenID.HasValue || row.LinnenID.Value <= 0)
            {
                return "Linen is required for every detail row.";
            }
        }

        return string.Empty;
    }

    private List<LinenDeliveryDetailRow> ParseDetailsJson()
    {
        try
        {
            var rows = JsonSerializer.Deserialize<List<LinenDeliveryDetailRow>>(DetailsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            rows ??= new List<LinenDeliveryDetailRow>();
            foreach (var row in rows)
            {
                row.DeliveryID = Header.DeliveryID;
                row.Note = (row.Note ?? string.Empty).Trim();
            }

            return rows;
        }
        catch
        {
            ModelState.AddModelError(string.Empty, "Detail data is invalid.");
            return new List<LinenDeliveryDetailRow>();
        }
    }

    private string NormalizeMode(string? mode)
    {
        return string.IsNullOrWhiteSpace(mode) ? "view" : mode.Trim().ToLowerInvariant();
    }

    private string? NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return null;
        }

        if (!Url.IsLocalUrl(returnUrl))
        {
            return null;
        }

        return returnUrl;
    }

    private bool TryGetCurrentEmployeeId(out int employeeId)
    {
        return int.TryParse(User.FindFirst("EmployeeID")?.Value, out employeeId);
    }

    private SqlConnection OpenConnection()
    {
        var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        return conn;
    }

    private PagePermissions GetUserPermissions()
    {
        var isAdmin = User.FindFirst("IsAdminRole")?.Value == "True";
        var roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "0");
        var perms = new PagePermissions();
        perms.AllowedNos = isAdmin
            ? Enumerable.Range(1, 20).ToList()
            : _permissionService.GetPermissionsForPage(roleId, FunctionId);
        return perms;
    }

    private static bool IsLaundryType(int? deliveryType)
    {
        return deliveryType == 3;
    }

    private static bool CanHeaderCreateBill(LinenDeliveryHeader header)
    {
        if (header.DeliveryType == 5)
        {
            return true;
        }

        if (header.DeliveryType == 3)
        {
            return true;
        }

        return false;
    }

    private static bool CanHeaderMarkNoNeedBill(LinenDeliveryHeader header)
    {
        if (header.DeliveryType == 5)
        {
            return true;
        }

        if (header.DeliveryType == 3 && !header.IsSpecialLaundry)
        {
            return true;
        }

        return false;
    }

    private static bool ToBool(object value)
    {
        if (value == DBNull.Value)
        {
            return false;
        }

        if (value is bool boolValue)
        {
            return boolValue;
        }

        return Convert.ToInt32(value) != 0;
    }

    private static decimal ToDecimal(object value)
    {
        if (value == DBNull.Value)
        {
            return 0;
        }

        return Convert.ToDecimal(value);
    }
}

public class LinenDeliveryHeader
{
    public int DeliveryID { get; set; }
    [DataType(DataType.Date)]
    public DateTime DeliveryDate { get; set; } = DateTime.Today;
    public string Description { get; set; } = string.Empty;
    public bool Closed { get; set; }
    public int? DeliveryType { get; set; }
    public int? NoteID { get; set; }
    public int? SupplierID { get; set; }
    public bool ToBill { get; set; }
    public bool FullReceive { get; set; }
    public bool IsRent { get; set; }
    public bool IsSpecialLaundry { get; set; }
}

public class LinenDeliveryDetailRow
{
    public int ID { get; set; }
    public int DeliveryID { get; set; }
    public string Location { get; set; } = string.Empty;
    public string LinenCode { get; set; } = string.Empty;
    public bool Express { get; set; }
    public bool IsChild { get; set; }
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal Amount { get; set; }
    public string Note { get; set; } = string.Empty;
    public decimal QuantityRe { get; set; }
    public bool NoNeedBill { get; set; }
    public int? LocationID { get; set; }
    public int? LinnenID { get; set; }
    public bool OutOfOffer { get; set; }
    public bool CollectToBill { get; set; }
}

public class LinenDeliveryBillRow
{
    public int BillID { get; set; }
    public DateTime? BillDate { get; set; }
    public string ApartmentNo { get; set; } = string.Empty;
    public string Customer { get; set; } = string.Empty;
    public decimal VNDAmountBefVAT { get; set; }
    public decimal PctTax { get; set; }
    public decimal VNDAmountVAT { get; set; }
    public decimal VNDAmount { get; set; }
    public int BillStatus { get; set; }
    public string BillStatusName { get; set; } = string.Empty;
    public string BillDateText => BillDate.HasValue ? BillDate.Value.ToString("dd/MM/yyyy") : string.Empty;
    public string BillStatusText => LinenDeliveryDetailModel.GetBillStatusText(BillStatus, BillStatusName);
}

public class LinenDeliveryPrintPreviewResult
{
    public string SupplierName { get; set; } = string.Empty;
    public string DeliveryTypeName { get; set; } = string.Empty;
    public List<LinenDeliveryPrintPreviewRow> Rows { get; set; } = new List<LinenDeliveryPrintPreviewRow>();
}

public class LinenDeliveryPrintPreviewRow
{
    public string Location { get; set; } = string.Empty;
    public string LinenCode { get; set; } = string.Empty;
    public bool IsChild { get; set; }
    public bool Express { get; set; }
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal Amount { get; set; }
    public string Note { get; set; } = string.Empty;
}
