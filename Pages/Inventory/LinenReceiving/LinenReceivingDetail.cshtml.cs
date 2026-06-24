using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;

namespace SmartSam.Pages.Inventory.LinenReceiving;

public class LinenReceivingDetailModel : BasePageModel
{
    private const int FunctionId = 117;
    private const int PermissionView = 1;
    private const int PermissionUpdate = 2;

    private readonly PermissionService _permissionService;

    public LinenReceivingDetailModel(IConfiguration config, PermissionService permissionService) : base(config)
    {
        _permissionService = permissionService;
    }

    [BindProperty(SupportsGet = true)]
    public string Mode { get; set; } = "view";

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    public LinenReceivingHeader Header { get; set; } = new LinenReceivingHeader();

    [BindProperty]
    public string DetailsJson { get; set; } = "[]";

    public PagePermissions PagePerm { get; private set; } = new PagePermissions();
    public bool CanSave { get; private set; }
    public bool IsViewMode => string.Equals(Mode, "view", StringComparison.OrdinalIgnoreCase) || Header.IsLocked;
    public bool IsDeliveryLocked => IsViewMode || Details.Count > 0;
    public List<LinenReceivingDetailRow> Details { get; set; } = new List<LinenReceivingDetailRow>();
    public List<SelectListItem> DeliveryOptions { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> LocationOptions { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> LinenOptions { get; set; } = new List<SelectListItem>();
    public string DeliveryOptionsJson { get; set; } = "[]";
    public string LocationOptionsJson { get; set; } = "[]";
    public string LinenOptionsJson { get; set; } = "[]";
    public string BackUrl => IsLocalReturnUrl(ReturnUrl) ? ReturnUrl! : Url.Page("./Index")!;

    public IActionResult OnGet(int? id, string mode = "view")
    {
        PagePerm = GetUserPermissions();
        Mode = NormalizeMode(mode);

        if (Mode == "add")
        {
            if (!PagePerm.HasPermission(PermissionUpdate))
            {
                return RedirectToPage("./Index");
            }

            using var conn = OpenConnection();
            using var trans = conn.BeginTransaction();
            Header = CreateMaster(conn, trans);
            trans.Commit();
            return RedirectToPage("./LinenReceivingDetail", new { id = Header.ReceiveID, mode = "edit", returnUrl = ReturnUrl });
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

        if (Mode == "edit" && Header.IsLocked)
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
            ModelState.AddModelError(string.Empty, "You do not have permission to update linen receiving.");
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
            var currentHeader = GetCurrentHeaderState(conn, trans, Header.ReceiveID);
            if (currentHeader == null)
            {
                trans.Rollback();
                return NotFound();
            }

            if (currentHeader.IsLocked)
            {
                trans.Rollback();
                ModelState.AddModelError(string.Empty, "Linen receiving is locked and cannot be edited.");
                ReloadCurrentPage(Header.ReceiveID, viewMode: true);
                return Page();
            }

            var existingDetailCount = GetDetailCount(conn, trans, Header.ReceiveID);
            if (existingDetailCount > 0 && currentHeader.SendID != Header.SendID)
            {
                ModelState.AddModelError(string.Empty, "Delivery cannot be changed after detail rows exist.");
            }

            if (IsDeliveryUsedByAnotherReceive(conn, trans))
            {
                ModelState.AddModelError(string.Empty, "Pls select another Delivery");
            }

            if (!ModelState.IsValid)
            {
                trans.Rollback();
                LoadLookupData();
                CanSave = true;
                return Page();
            }

            UpdateMaster(conn, trans);

            if (existingDetailCount == 0 && Header.SendID.HasValue && Details.Count == 0)
            {
                using var createCmd = new SqlCommand("exec LN_CreateReceiveDT_NEW @ReceiveID, @DeliveryID", conn, trans);
                createCmd.Parameters.Add("@ReceiveID", SqlDbType.Int).Value = Header.ReceiveID;
                createCmd.Parameters.Add("@DeliveryID", SqlDbType.Int).Value = Header.SendID.Value;
                createCmd.ExecuteNonQuery();
                BackfillReceiveDetailDisplayFields(conn, trans, Header.ReceiveID);
            }
            else
            {
                SaveDetailRows(conn, trans);
            }

            trans.Commit();
        }
        catch
        {
            trans.Rollback();
            throw;
        }

        TempData["SuccessMessage"] = "Linen receiving saved.";
        return RedirectToPage("./LinenReceivingDetail", new { id = Header.ReceiveID, mode = "edit", returnUrl = ReturnUrl });
    }

    public JsonResult OnGetRowDeliveryOptions(int? currentDeliveryId, string? term)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionUpdate))
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return new JsonResult(new { success = false, message = "Forbidden" });
        }

        using var conn = OpenConnection();
        var options = LoadAvailableDeliveryOptions(conn, currentDeliveryId, term)
            .Select(x => new { value = x.Value, text = x.Text })
            .ToList();

        return new JsonResult(new { success = true, options });
    }

    public JsonResult OnGetDeliveryInfo(int id)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionView) && !PagePerm.HasPermission(PermissionUpdate))
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return new JsonResult(new { success = false, message = "Forbidden" });
        }

        using var conn = OpenConnection();
        using var cmd = new SqlCommand("SELECT ISNULL(IsRent, 0) AS IsRent FROM dbo.LN_DeliveryMT WHERE DeliveryID = @DeliveryID;", conn);
        cmd.Parameters.Add("@DeliveryID", SqlDbType.Int).Value = id;
        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return new JsonResult(new { success = false, message = "Delivery not found." });
        }

        return new JsonResult(new { success = true, isRent = ToBool(rd["IsRent"]) });
    }

    private LinenReceivingHeader CreateMaster(SqlConnection conn, SqlTransaction trans)
    {
        var header = new LinenReceivingHeader
        {
            ReceiveDate = DateTime.Today,
            Description = GetGeneratedDescription(conn, trans)
        };

        using var cmd = new SqlCommand(@"
INSERT INTO dbo.LN_ReceiveMT (ReceiveDate, Des)
VALUES (@ReceiveDate, @Des);
SELECT CONVERT(int, SCOPE_IDENTITY());", conn, trans);
        cmd.Parameters.Add("@ReceiveDate", SqlDbType.DateTime).Value = header.ReceiveDate.Date;
        cmd.Parameters.Add("@Des", SqlDbType.VarChar, 100).Value = header.Description;
        header.ReceiveID = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        return header;
    }

    private string GetGeneratedDescription(SqlConnection conn, SqlTransaction trans)
    {
        using var cmd = new SqlCommand("exec LN_GenNo 3, null", conn, trans);
        using var rd = cmd.ExecuteReader();
        if (rd.Read())
        {
            return Convert.ToString(rd["strResult"]) ?? string.Empty;
        }

        return string.Empty;
    }

    private bool LoadHeader(int receiveId)
    {
        using var conn = OpenConnection();
        using var cmd = new SqlCommand(@"
SELECT ReceiveID,
       SendID,
       ISNULL(Des, '') AS Des,
       ReceiveDate,
       ISNULL([Lock], 0) AS IsLocked,
       SupplierID,
       ISNULL(IsRent, 0) AS IsRent
FROM dbo.LN_ReceiveMT
WHERE ReceiveID = @ReceiveID;", conn);
        cmd.Parameters.Add("@ReceiveID", SqlDbType.Int).Value = receiveId;

        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
        {
            return false;
        }

        Header = new LinenReceivingHeader
        {
            ReceiveID = Convert.ToInt32(rd["ReceiveID"]),
            SendID = rd["SendID"] == DBNull.Value ? null : Convert.ToInt32(rd["SendID"]),
            Description = Convert.ToString(rd["Des"]) ?? string.Empty,
            ReceiveDate = rd["ReceiveDate"] == DBNull.Value ? DateTime.Today : Convert.ToDateTime(rd["ReceiveDate"]),
            IsLocked = ToBool(rd["IsLocked"]),
            SupplierID = rd["SupplierID"] == DBNull.Value ? null : Convert.ToInt32(rd["SupplierID"]),
            IsRent = ToBool(rd["IsRent"])
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
        Details = new List<LinenReceivingDetailRow>();
        using var cmd = new SqlCommand(@"
SELECT dt.ID,
       dt.ReceiveID,
       dt.SendID,
       ISNULL(de.Des, '') AS DeliveryDescription,
       ISNULL(dt.Location, '') AS Location,
       ISNULL(CASE WHEN ISNULL(dt.LinnenCode, '') <> '' THEN dt.LinnenCode ELSE ln.LinnenCode END, '') AS LinenCode,
       dt.LocationID,
       dt.LinnenID,
       ISNULL(dt.Express, 0) AS Express,
       ISNULL(dt.IsChild, 0) AS IsChild,
       ISNULL(dt.Quantity, 0) AS Quantity,
       ISNULL(dt.Price, 0) AS Price,
       ISNULL(dt.Amount, 0) AS Amount,
       ISNULL(dt.Note, '') AS Note
FROM dbo.LN_ReceiveDT dt
LEFT JOIN dbo.LN_DeliveryMT de ON de.DeliveryID = dt.SendID
LEFT JOIN dbo.LN_Linnen ln ON ln.ID = dt.LinnenID
WHERE dt.ReceiveID = @ReceiveID
ORDER BY ISNULL(dt.Location, ''), ISNULL(CASE WHEN ISNULL(dt.LinnenCode, '') <> '' THEN dt.LinnenCode ELSE ln.LinnenCode END, ''), dt.ID;", conn, trans);
        cmd.Parameters.Add("@ReceiveID", SqlDbType.Int).Value = Header.ReceiveID;

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            Details.Add(new LinenReceivingDetailRow
            {
                ID = Convert.ToInt32(rd["ID"]),
                ReceiveID = Convert.ToInt32(rd["ReceiveID"]),
                SendID = rd["SendID"] == DBNull.Value ? null : Convert.ToInt32(rd["SendID"]),
                DeliveryDescription = Convert.ToString(rd["DeliveryDescription"]) ?? string.Empty,
                Location = Convert.ToString(rd["Location"]) ?? string.Empty,
                LinenCode = Convert.ToString(rd["LinenCode"]) ?? string.Empty,
                LocationID = rd["LocationID"] == DBNull.Value ? null : Convert.ToInt32(rd["LocationID"]),
                LinnenID = rd["LinnenID"] == DBNull.Value ? null : Convert.ToInt32(rd["LinnenID"]),
                Express = ToBool(rd["Express"]),
                IsChild = ToBool(rd["IsChild"]),
                Quantity = ToDecimal(rd["Quantity"]),
                Price = ToDecimal(rd["Price"]),
                Amount = ToDecimal(rd["Amount"]),
                Note = Convert.ToString(rd["Note"]) ?? string.Empty
            });
        }
    }

    private void LoadLookupData()
    {
        using var conn = OpenConnection();
        DeliveryOptions = LoadDeliveryOptions(conn);
        LocationOptions = LoadLocations(conn);
        LinenOptions = LoadLinenOptions(conn);
        DeliveryOptionsJson = JsonSerializer.Serialize(DeliveryOptions.Select(x => new { value = x.Value, text = x.Text }));
        LocationOptionsJson = JsonSerializer.Serialize(LocationOptions.Select(x => new { value = x.Value, text = x.Text }));
        LinenOptionsJson = JsonSerializer.Serialize(LinenOptions.Select(x => new { value = x.Value, text = x.Text, price = x.Group?.Name ?? string.Empty }));
    }

    private List<SelectListItem> LoadDeliveryOptions(SqlConnection conn)
    {
        var items = new List<SelectListItem>
        {
            new SelectListItem { Value = string.Empty, Text = "-- Select --" }
        };

        var selectedDeliveryIds = Details
            .Where(x => x.SendID.HasValue)
            .Select(x => x.SendID!.Value)
            .ToList();

        if (Header.SendID.HasValue)
        {
            selectedDeliveryIds.Add(Header.SendID.Value);
        }

        selectedDeliveryIds = selectedDeliveryIds.Distinct().ToList();
        if (Details.Count > 0 && selectedDeliveryIds.Count > 0)
        {
            var parameterNames = selectedDeliveryIds.Select((_, index) => $"@DeliveryID{index}").ToList();
            using var selectedCmd = new SqlCommand($@"
SELECT DeliveryID, Des
FROM dbo.LN_DeliveryMT
WHERE DeliveryID IN ({string.Join(", ", parameterNames)})
ORDER BY DeliveryID DESC;", conn);

            for (var i = 0; i < selectedDeliveryIds.Count; i++)
            {
                selectedCmd.Parameters.Add(parameterNames[i], SqlDbType.Int).Value = selectedDeliveryIds[i];
            }

            using var selectedRd = selectedCmd.ExecuteReader();
            while (selectedRd.Read())
            {
                items.Add(new SelectListItem
                {
                    Value = Convert.ToInt32(selectedRd["DeliveryID"]).ToString(),
                    Text = Convert.ToString(selectedRd["Des"]) ?? string.Empty
                });
            }

            return items;
        }

        return LoadAvailableDeliveryOptions(conn, Header.SendID, null);
    }

    private List<SelectListItem> LoadAvailableDeliveryOptions(SqlConnection conn, int? currentDeliveryId, string? term)
    {
        var items = new List<SelectListItem>
        {
            new SelectListItem { Value = string.Empty, Text = "-- Select --" }
        };

        var keyword = (term ?? string.Empty).Trim();
        var sql = @"
WITH ReceivedDelivery AS
(
    SELECT dt.SendID
    FROM dbo.LN_ReceiveDT dt
    WHERE dt.SendID IS NOT NULL
    GROUP BY dt.SendID
)
SELECT TOP (100) de.DeliveryID, de.Des
FROM dbo.LN_DeliveryMT de
LEFT JOIN ReceivedDelivery rd ON rd.SendID = de.DeliveryID
WHERE (@CurrentDeliveryID IS NOT NULL AND de.DeliveryID = @CurrentDeliveryID)
   OR (
       rd.SendID IS NULL
       AND (@SearchText = '' OR de.Des LIKE @SearchPattern OR CONVERT(varchar(20), de.DeliveryID) LIKE @SearchPattern)
   )
ORDER BY CASE WHEN de.DeliveryID = @CurrentDeliveryID THEN 0 ELSE 1 END,
         de.DeliveryID DESC;";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@CurrentDeliveryID", SqlDbType.Int).Value = currentDeliveryId.HasValue ? currentDeliveryId.Value : (object)DBNull.Value;
        cmd.Parameters.Add("@SearchText", SqlDbType.VarChar, 100).Value = keyword;
        cmd.Parameters.Add("@SearchPattern", SqlDbType.VarChar, 110).Value = $"%{keyword}%";
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            items.Add(new SelectListItem
            {
                Value = Convert.ToInt32(rd["DeliveryID"]).ToString(),
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

        using var cmd = new SqlCommand(@"
WITH LocationSource AS
(
    SELECT ApmtID AS LocationID,
           ApartmentNo AS LocationName
    FROM dbo.AM_Apmt
    WHERE (ExistFrom IS NULL OR ExistFrom < DATEADD(DAY, 1, CONVERT(date, GETDATE())))
      AND (ExistTo IS NULL OR ExistTo >= CONVERT(date, GETDATE()))

    UNION

    SELECT LocationID,
           Location AS LocationName
    FROM dbo.LN_ReceiveDT
    WHERE ReceiveID = @ReceiveID
      AND LocationID IS NOT NULL
      AND ISNULL(Location, '') <> ''
)
SELECT LocationID,
       MAX(LocationName) AS LocationName
FROM LocationSource
GROUP BY LocationID
ORDER BY MAX(LocationName);", conn);
        cmd.Parameters.Add("@ReceiveID", SqlDbType.Int).Value = Header.ReceiveID;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            items.Add(new SelectListItem
            {
                Value = Convert.ToInt32(rd["LocationID"]).ToString(),
                Text = Convert.ToString(rd["LocationName"]) ?? string.Empty
            });
        }

        return items;
    }

    private static List<SelectListItem> LoadLinenOptions(SqlConnection conn)
    {
        var items = new List<SelectListItem>
        {
            new SelectListItem { Value = string.Empty, Text = "-- Select --" }
        };

        using var cmd = new SqlCommand(@"
SELECT ID, LinnenCode, TRY_CONVERT(decimal(18,2), VNDPriceNew3) AS DisplayPrice
FROM dbo.LN_Linnen
ORDER BY LinnenCode;", conn);
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

    private LinenReceivingHeader? GetCurrentHeaderState(SqlConnection conn, SqlTransaction? trans, int receiveId)
    {
        using var cmd = new SqlCommand(@"
SELECT ReceiveID,
       SendID,
       ISNULL([Lock], 0) AS IsLocked
FROM dbo.LN_ReceiveMT
WHERE ReceiveID = @ReceiveID;", conn, trans);
        cmd.Parameters.Add("@ReceiveID", SqlDbType.Int).Value = receiveId;
        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
        {
            return null;
        }

        return new LinenReceivingHeader
        {
            ReceiveID = Convert.ToInt32(rd["ReceiveID"]),
            SendID = rd["SendID"] == DBNull.Value ? null : Convert.ToInt32(rd["SendID"]),
            IsLocked = ToBool(rd["IsLocked"])
        };
    }

    private void ReloadCurrentPage(int receiveId, bool viewMode)
    {
        LoadHeader(receiveId);
        LoadDetails();
        LoadLookupData();
        DetailsJson = JsonSerializer.Serialize(Details);
        Mode = viewMode ? "view" : Mode;
        CanSave = !IsViewMode && PagePerm.HasPermission(PermissionUpdate);
    }

    private int GetDetailCount(SqlConnection conn, SqlTransaction trans, int receiveId)
    {
        using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.LN_ReceiveDT WHERE ReceiveID = @ReceiveID;", conn, trans);
        cmd.Parameters.Add("@ReceiveID", SqlDbType.Int).Value = receiveId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private bool IsDeliveryUsedByAnotherReceive(SqlConnection conn, SqlTransaction trans)
    {
        if (!Header.SendID.HasValue)
        {
            return false;
        }

        using var cmd = new SqlCommand(@"
SELECT COUNT(1)
FROM dbo.LN_ReceiveMT mt
WHERE mt.SendID = @SendID
  AND mt.ReceiveID <> @ReceiveID
  AND EXISTS (SELECT 1 FROM dbo.LN_ReceiveDT dt WHERE dt.ReceiveID = mt.ReceiveID);", conn, trans);
        cmd.Parameters.Add("@SendID", SqlDbType.Int).Value = Header.SendID.Value;
        cmd.Parameters.Add("@ReceiveID", SqlDbType.Int).Value = Header.ReceiveID;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private void UpdateMaster(SqlConnection conn, SqlTransaction trans)
    {
        var supplierId = GetDeliverySupplierId(conn, trans, Header.SendID);
        using var cmd = new SqlCommand(@"
UPDATE dbo.LN_ReceiveMT
SET SendID = @SendID,
    Des = @Des,
    ReceiveDate = @ReceiveDate,
    [Lock] = @Lock,
    SupplierID = @SupplierID,
    IsRent = @IsRent
WHERE ReceiveID = @ReceiveID;", conn, trans);
        cmd.Parameters.Add("@ReceiveID", SqlDbType.Int).Value = Header.ReceiveID;
        cmd.Parameters.Add("@SendID", SqlDbType.Int).Value = Header.SendID.HasValue ? Header.SendID.Value : (object)DBNull.Value;
        cmd.Parameters.Add("@Des", SqlDbType.VarChar, 100).Value = Header.Description;
        cmd.Parameters.Add("@ReceiveDate", SqlDbType.DateTime).Value = Header.ReceiveDate.Date;
        cmd.Parameters.Add("@Lock", SqlDbType.Bit).Value = Header.IsLocked;
        cmd.Parameters.Add("@SupplierID", SqlDbType.Int).Value = supplierId.HasValue ? supplierId.Value : (object)DBNull.Value;
        cmd.Parameters.Add("@IsRent", SqlDbType.Bit).Value = Header.IsRent;
        cmd.ExecuteNonQuery();
    }

    private static int? GetDeliverySupplierId(SqlConnection conn, SqlTransaction trans, int? deliveryId)
    {
        if (!deliveryId.HasValue)
        {
            return null;
        }

        using var cmd = new SqlCommand("SELECT SupplierID FROM dbo.LN_DeliveryMT WHERE DeliveryID = @DeliveryID;", conn, trans);
        cmd.Parameters.Add("@DeliveryID", SqlDbType.Int).Value = deliveryId.Value;
        var value = cmd.ExecuteScalar();
        return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
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

            row.Location = locationMap.TryGetValue(row.LocationID.Value, out var locationName) ? locationName : string.Empty;
            row.LinenCode = linenMap.TryGetValue(row.LinnenID.Value, out var linenCode) ? linenCode : string.Empty;
            row.SendID ??= Header.SendID;
            row.Amount = Math.Round(row.Quantity * row.Price, 2);
        }

        var keptIds = Details.Where(x => x.ID > 0).Select(x => x.ID).ToList();
        var deleteSql = keptIds.Count == 0
            ? "DELETE FROM dbo.LN_ReceiveDT WHERE ReceiveID = @ReceiveID;"
            : $"DELETE FROM dbo.LN_ReceiveDT WHERE ReceiveID = @ReceiveID AND ID NOT IN ({string.Join(", ", keptIds)});";

        using (var deleteCmd = new SqlCommand(deleteSql, conn, trans))
        {
            deleteCmd.Parameters.Add("@ReceiveID", SqlDbType.Int).Value = Header.ReceiveID;
            deleteCmd.ExecuteNonQuery();
        }

        foreach (var row in Details.Where(x => x.LocationID.HasValue && x.LinnenID.HasValue))
        {
            if (row.ID > 0)
            {
                using var updateCmd = new SqlCommand(@"
UPDATE dbo.LN_ReceiveDT
SET SendID = @SendID,
    Location = @Location,
    LinnenCode = @LinnenCode,
    LocationID = @LocationID,
    LinnenID = @LinnenID,
    Express = @Express,
    IsChild = @IsChild,
    Quantity = @Quantity,
    Price = @Price,
    Amount = @Amount,
    Note = @Note
WHERE ID = @ID AND ReceiveID = @ReceiveID;", conn, trans);
                BindDetailParams(updateCmd, row);
                updateCmd.Parameters.Add("@ID", SqlDbType.Int).Value = row.ID;
                updateCmd.ExecuteNonQuery();
            }
            else
            {
                using var insertCmd = new SqlCommand(@"
INSERT INTO dbo.LN_ReceiveDT
    (ReceiveID, SendID, Location, LinnenCode, LocationID, LinnenID, Express, IsChild, Quantity, Price, Amount, Note)
VALUES
    (@ReceiveID, @SendID, @Location, @LinnenCode, @LocationID, @LinnenID, @Express, @IsChild, @Quantity, @Price, @Amount, @Note);", conn, trans);
                BindDetailParams(insertCmd, row);
                insertCmd.ExecuteNonQuery();
            }
        }
    }

    private static void BackfillReceiveDetailDisplayFields(SqlConnection conn, SqlTransaction trans, int receiveId)
    {
        using var cmd = new SqlCommand(@"
UPDATE dt
SET Location = ISNULL(loc.Location, dt.Location),
    LinnenCode = ISNULL(ln.LinnenCode, dt.LinnenCode)
FROM dbo.LN_ReceiveDT dt
LEFT JOIN dbo.View_LN_Location loc ON loc.locationID = dt.LocationID
LEFT JOIN dbo.LN_Linnen ln ON ln.ID = dt.LinnenID
WHERE dt.ReceiveID = @ReceiveID;", conn, trans);
        cmd.Parameters.Add("@ReceiveID", SqlDbType.Int).Value = receiveId;
        cmd.ExecuteNonQuery();
    }

    private void BindDetailParams(SqlCommand cmd, LinenReceivingDetailRow row)
    {
        cmd.Parameters.Add("@ReceiveID", SqlDbType.Int).Value = Header.ReceiveID;
        cmd.Parameters.Add("@SendID", SqlDbType.Int).Value = row.SendID.HasValue ? (object)row.SendID.Value : (Header.SendID.HasValue ? Header.SendID.Value : (object)DBNull.Value);
        cmd.Parameters.Add("@Location", SqlDbType.VarChar, 50).Value = row.Location;
        cmd.Parameters.Add("@LinnenCode", SqlDbType.VarChar, 50).Value = row.LinenCode;
        cmd.Parameters.Add("@LocationID", SqlDbType.Int).Value = row.LocationID.HasValue ? row.LocationID.Value : (object)DBNull.Value;
        cmd.Parameters.Add("@LinnenID", SqlDbType.Int).Value = row.LinnenID.HasValue ? row.LinnenID.Value : (object)DBNull.Value;
        cmd.Parameters.Add("@Express", SqlDbType.Bit).Value = row.Express;
        cmd.Parameters.Add("@IsChild", SqlDbType.Bit).Value = row.IsChild;
        AddDecimalParameter(cmd, "@Quantity", row.Quantity);
        AddDecimalParameter(cmd, "@Price", row.Price);
        AddDecimalParameter(cmd, "@Amount", row.Amount);
        cmd.Parameters.Add("@Note", SqlDbType.VarChar, 100).Value = row.Note ?? string.Empty;
    }

    private static void AddDecimalParameter(SqlCommand cmd, string name, decimal value)
    {
        var parameter = cmd.Parameters.Add(name, SqlDbType.Decimal);
        parameter.Precision = 18;
        parameter.Scale = 2;
        parameter.Value = value;
    }

    private Dictionary<int, string> GetLocationMap(SqlConnection conn, SqlTransaction trans)
    {
        var map = new Dictionary<int, string>();
        using var cmd = new SqlCommand(@"
WITH LocationSource AS
(
    SELECT ApmtID AS LocationID,
           ApartmentNo AS LocationName
    FROM dbo.AM_Apmt
    WHERE (ExistFrom IS NULL OR ExistFrom < DATEADD(DAY, 1, CONVERT(date, GETDATE())))
      AND (ExistTo IS NULL OR ExistTo >= CONVERT(date, GETDATE()))

    UNION

    SELECT LocationID,
           Location AS LocationName
    FROM dbo.LN_ReceiveDT
    WHERE ReceiveID = @ReceiveID
      AND LocationID IS NOT NULL
      AND ISNULL(Location, '') <> ''
)
SELECT LocationID,
       MAX(LocationName) AS LocationName
FROM LocationSource
GROUP BY LocationID;", conn, trans);
        cmd.Parameters.Add("@ReceiveID", SqlDbType.Int).Value = Header.ReceiveID;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            map[Convert.ToInt32(rd["LocationID"])] = Convert.ToString(rd["LocationName"]) ?? string.Empty;
        }

        return map;
    }

    private static Dictionary<int, string> GetLinenMap(SqlConnection conn, SqlTransaction trans)
    {
        var map = new Dictionary<int, string>();
        using var cmd = new SqlCommand("SELECT ID, LinnenCode FROM dbo.LN_Linnen;", conn, trans);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            map[Convert.ToInt32(rd["ID"])] = Convert.ToString(rd["LinnenCode"]) ?? string.Empty;
        }

        return map;
    }

    private void NormalizeInput()
    {
        Header.Description = (Header.Description ?? string.Empty).Trim();
    }

    private void ValidateInput()
    {
        if (Header.ReceiveID <= 0)
        {
            ModelState.AddModelError(string.Empty, "Receive ID is invalid.");
        }

        if (!Header.SendID.HasValue || Header.SendID <= 0)
        {
            ModelState.AddModelError(string.Empty, "Delivery is required.");
        }

        if (string.IsNullOrWhiteSpace(Header.Description))
        {
            ModelState.AddModelError(string.Empty, "Description is required.");
        }

        for (var i = 0; i < Details.Count; i++)
        {
            var row = Details[i];
            row.SendID ??= Header.SendID;
            if (!row.SendID.HasValue)
            {
                ModelState.AddModelError(string.Empty, $"Delivery is required at row {i + 1}.");
            }

            if (!row.LocationID.HasValue)
            {
                ModelState.AddModelError(string.Empty, $"Location is required at row {i + 1}.");
            }

            if (!row.LinnenID.HasValue)
            {
                ModelState.AddModelError(string.Empty, $"Linen is required at row {i + 1}.");
            }
        }
    }

    private List<LinenReceivingDetailRow> ParseDetailsJson()
    {
        if (string.IsNullOrWhiteSpace(DetailsJson))
        {
            return new List<LinenReceivingDetailRow>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<LinenReceivingDetailRow>>(DetailsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<LinenReceivingDetailRow>();
        }
        catch
        {
            return new List<LinenReceivingDetailRow>();
        }
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
        var employeeId = int.Parse(User.FindFirst("EmployeeID")?.Value ?? "0");
        var perms = new PagePermissions();
        perms.AllowedNos = isAdmin
            ? Enumerable.Range(1, 20).ToList()
            : _permissionService.GetPermissionsForPage(roleId, FunctionId);

        if (!isAdmin && employeeId > 0)
        {
            perms.AllowedNos = perms.AllowedNos
                .Union(GetEmployeeRolePermissions(employeeId))
                .Distinct()
                .ToList();
        }

        return perms;
    }

    private List<int> GetEmployeeRolePermissions(int employeeId)
    {
        var result = new List<int>();
        using var conn = OpenConnection();
        using var cmd = new SqlCommand(@"
SELECT rp.Permission
FROM dbo.SYS_RoleMember rm
INNER JOIN dbo.SYS_RolePermission rp ON rp.RoleID = rm.RoleID
WHERE rm.Operator = @EmployeeID
  AND rp.FunctionID = @FunctionID
  AND ISNULL(rp.IsActive, 1) = 1;", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId;
        cmd.Parameters.Add("@FunctionID", SqlDbType.Int).Value = FunctionId;

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var permissionText = Convert.ToString(rd["Permission"]) ?? string.Empty;
            foreach (var permission in permissionText.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(permission.Trim(), out var permissionNo))
                {
                    result.Add(permissionNo);
                }
            }
        }

        return result;
    }

    private static string NormalizeMode(string? mode)
    {
        var value = (mode ?? string.Empty).Trim().ToLowerInvariant();
        return value is "add" or "edit" or "view" ? value : "view";
    }

    private static bool IsLocalReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return false;
        }

        return Uri.TryCreate(returnUrl, UriKind.Relative, out _)
            && returnUrl.StartsWith("/", StringComparison.Ordinal)
            && !returnUrl.StartsWith("//", StringComparison.Ordinal)
            && !returnUrl.StartsWith("/\\", StringComparison.Ordinal);
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

public class LinenReceivingHeader
{
    public int ReceiveID { get; set; }
    public int? SendID { get; set; }
    public string Description { get; set; } = string.Empty;
    [DataType(DataType.Date)]
    public DateTime ReceiveDate { get; set; } = DateTime.Today;
    public bool IsLocked { get; set; }
    public int? SupplierID { get; set; }
    public bool IsRent { get; set; }
}

public class LinenReceivingDetailRow
{
    public int ID { get; set; }
    public int ReceiveID { get; set; }
    public int? SendID { get; set; }
    public string DeliveryDescription { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string LinenCode { get; set; } = string.Empty;
    public int? LocationID { get; set; }
    public int? LinnenID { get; set; }
    public bool Express { get; set; }
    public bool IsChild { get; set; }
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal Amount { get; set; }
    public string Note { get; set; } = string.Empty;
}
