using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;

namespace SmartSam.Pages.Inventory.LinenReport;

public class IndexModel : BasePageModel
{
    private const int FunctionId = 118;
    private const int PermissionView = 1;
    private const int LaundryBalanceSupplierId = 1636;

    private readonly PermissionService _permissionService;
    private readonly IWebHostEnvironment _webHostEnvironment;

    public IndexModel(IConfiguration config, PermissionService permissionService, IWebHostEnvironment webHostEnvironment) : base(config)
    {
        _permissionService = permissionService;
        _webHostEnvironment = webHostEnvironment;
    }

    [BindProperty(SupportsGet = true)]
    public LinenReportFilter Filter { get; set; } = new LinenReportFilter();

    [BindProperty(SupportsGet = true)]
    public string LockedReportType { get; set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public bool Popup { get; set; }

    public PagePermissions PagePerm { get; private set; } = new PagePermissions();
    public List<SelectListItem> DescriptionOptions { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> LinenOptions { get; set; } = new List<SelectListItem>();
    public string DescriptionLabel { get; private set; } = "Des";
    public bool DescriptionEnabled { get; private set; }
    public bool LinenEnabled { get; private set; }
    public bool FromEnabled { get; private set; }
    public bool ToEnabled { get; private set; }
    public bool ChartEnabled { get; private set; }
    public bool IsTypeLocked { get; private set; }
    public bool IsPopup => Popup;

    public IActionResult OnGet()
    {
        PagePerm = GetUserPermissions();
        if (!HasPageAccess())
        {
            return Redirect("/");
        }

        NormalizeFilter();

        using var conn = OpenConnection();
        LinenOptions = LoadLinenOptions(conn);
        var modeState = BuildModeState(conn, Filter.ReportType, Filter.DescriptionId);
        ApplyModeState(modeState);
        Filter.DescriptionId = modeState.SelectedDescriptionId;

        return Page();
    }

    public JsonResult OnGetModeOptions(string? reportType, int? descriptionId)
    {
        PagePerm = GetUserPermissions();
        if (!HasPageAccess())
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return new JsonResult(new { success = false, message = "Forbidden" });
        }

        try
        {
            using var conn = OpenConnection();
            var modeState = BuildModeState(conn, ResolveRequestedReportType(reportType), descriptionId);
            return new JsonResult(new
            {
                success = true,
                reportType = modeState.ReportType,
                labelText = modeState.DescriptionLabel,
                descriptionEnabled = modeState.DescriptionEnabled,
                linenEnabled = modeState.LinenEnabled,
                fromEnabled = modeState.FromEnabled,
                toEnabled = modeState.ToEnabled,
                chartEnabled = modeState.ChartEnabled,
                selectedDescriptionId = modeState.SelectedDescriptionId,
                descriptions = modeState.DescriptionOptions.Select(x => new
                {
                    value = x.Value,
                    text = x.Text
                })
            });
        }
        catch (Exception ex)
        {
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            return new JsonResult(new { success = false, message = ex.Message });
        }
    }

    public JsonResult OnGetPreview(string? reportType, int? descriptionId, string? linenCode, DateTime? fromDate, DateTime? toDate)
    {
        PagePerm = GetUserPermissions();
        if (!HasPageAccess())
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return new JsonResult(new { success = false, message = "Forbidden" });
        }

        var normalizedType = ResolveRequestedReportType(reportType);
        var normalizedLinenCode = (linenCode ?? string.Empty).Trim();
        var normalizedFromDate = (fromDate ?? DateTime.Today).Date;
        var normalizedToDate = (toDate ?? DateTime.Today).Date;

        if (normalizedFromDate > normalizedToDate)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return new JsonResult(new { success = false, message = "From Date must be less than or equal to To Date." });
        }

        try
        {
            using var conn = OpenConnection();
            switch (normalizedType)
            {
                case LinenReportTypes.Pantry:
                    return BuildPantryPreview(conn, descriptionId, normalizedLinenCode);
                case LinenReportTypes.Delivery:
                    return BuildDeliveryPreview(conn, descriptionId, normalizedLinenCode);
                case LinenReportTypes.Receive:
                    return BuildReceivePreview(conn, descriptionId, normalizedLinenCode);
                case LinenReportTypes.LaundryRecord:
                    return BuildLaundryRecordPreview(conn, normalizedFromDate, normalizedToDate, normalizedLinenCode);
                case LinenReportTypes.NotReceive:
                    return BuildNotReceivePreview(conn, normalizedLinenCode);
                case LinenReportTypes.LaundryBalance:
                    return BuildLaundryBalancePreview(conn, normalizedFromDate, normalizedToDate);
                case LinenReportTypes.ApmtBalance:
                    return BuildApartmentBalancePreview(conn, descriptionId, normalizedFromDate, normalizedToDate);
                default:
                    Response.StatusCode = StatusCodes.Status400BadRequest;
                    return new JsonResult(new { success = false, message = "Invalid report type." });
            }
        }
        catch (InvalidOperationException ex)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return new JsonResult(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            return new JsonResult(new { success = false, message = ex.Message });
        }
    }

    public IActionResult OnGetPdf(string? reportType, int? descriptionId, string? linenCode, DateTime? fromDate, DateTime? toDate)
    {
        PagePerm = GetUserPermissions();
        if (!HasPageAccess())
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return new JsonResult(new { success = false, message = "Forbidden" });
        }

        var normalizedType = ResolveRequestedReportType(reportType);
        var normalizedLinenCode = (linenCode ?? string.Empty).Trim();
        var normalizedFromDate = (fromDate ?? DateTime.Today).Date;
        var normalizedToDate = (toDate ?? DateTime.Today).Date;

        if (normalizedFromDate > normalizedToDate)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return new JsonResult(new { success = false, message = "From Date must be less than or equal to To Date." });
        }

        try
        {
            using var conn = OpenConnection();
            var preview = normalizedType switch
            {
                LinenReportTypes.Pantry => BuildPantryPreview(conn, descriptionId, normalizedLinenCode),
                LinenReportTypes.Delivery => BuildDeliveryPreview(conn, descriptionId, normalizedLinenCode),
                LinenReportTypes.Receive => BuildReceivePreview(conn, descriptionId, normalizedLinenCode),
                LinenReportTypes.LaundryRecord => BuildLaundryRecordPreview(conn, normalizedFromDate, normalizedToDate, normalizedLinenCode),
                LinenReportTypes.NotReceive => BuildNotReceivePreview(conn, normalizedLinenCode),
                LinenReportTypes.LaundryBalance => BuildLaundryBalancePreview(conn, normalizedFromDate, normalizedToDate),
                LinenReportTypes.ApmtBalance => BuildApartmentBalancePreview(conn, descriptionId, normalizedFromDate, normalizedToDate),
                _ => throw new InvalidOperationException("Invalid report type.")
            };

            var pdf = LinenReportQuestPdfReport.BuildPdf(preview.Value ?? new { reportType = normalizedType }, LoadCompanyLogoBytes(conn));
            return File(pdf, "application/pdf");
        }
        catch (InvalidOperationException ex)
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return new JsonResult(new { success = false, message = ex.Message });
        }
        catch (Exception ex)
        {
            Response.StatusCode = StatusCodes.Status500InternalServerError;
            return new JsonResult(new { success = false, message = ex.Message });
        }
    }

    private JsonResult BuildPantryPreview(SqlConnection conn, int? descriptionId, string linenCode)
    {
        if (!descriptionId.HasValue || descriptionId.Value <= 0)
        {
            throw new InvalidOperationException("Pantry-Linen description is required.");
        }

        var header = LoadPantryHeader(conn, descriptionId.Value);
        if (header == null)
        {
            throw new InvalidOperationException("Pantry-Linen note not found.");
        }

        using (var procCmd = new SqlCommand("dbo.LN_MakeLinenRPT", conn))
        {
            procCmd.CommandType = CommandType.StoredProcedure;
            procCmd.Parameters.Add("@PickupID", SqlDbType.Int).Value = descriptionId.Value;
            procCmd.Parameters.Add("@UserCode", SqlDbType.VarChar, 15).Value = GetCurrentUserCode();
            procCmd.ExecuteNonQuery();
        }

        var rows = new List<LinenPantryPreviewRow>();
        using (var cmd = new SqlCommand(@"
SELECT ISNULL(IsOrder, 0) AS IsOrder,
       ISNULL(PentryName, '') AS PentryName,
       ISNULL(TimeSection, 0) AS TimeSection,
       ISNULL(BathBe, 0) AS BathBe,
       ISNULL(BathDe, 0) AS BathDe,
       ISNULL(BathRe, 0) AS BathRe,
       ISNULL(HandBe, 0) AS HandBe,
       ISNULL(HandDe, 0) AS HandDe,
       ISNULL(HandRe, 0) AS HandRe,
       ISNULL(FaceBe, 0) AS FaceBe,
       ISNULL(FaceDe, 0) AS FaceDe,
       ISNULL(FaceRe, 0) AS FaceRe,
       ISNULL(BathMBe, 0) AS BathMBe,
       ISNULL(BathMDe, 0) AS BathMDe,
       ISNULL(BathMRe, 0) AS BathMRe,
       ISNULL(PillowBe, 0) AS PillowBe,
       ISNULL(PillowDe, 0) AS PillowDe,
       ISNULL(PillowRe, 0) AS PillowRe,
       ISNULL(SheetSBe, 0) AS SheetSBe,
       ISNULL(SheetSDe, 0) AS SheetSDe,
       ISNULL(SheetSRe, 0) AS SheetSRe,
       ISNULL(DCoverSBe, 0) AS DCoverSBe,
       ISNULL(DCoverSDe, 0) AS DCoverSDe,
       ISNULL(DCoverSRe, 0) AS DCoverSRe,
       ISNULL(SheetKBe, 0) AS SheetKBe,
       ISNULL(SheetKDe, 0) AS SheetKDe,
       ISNULL(SheetKRe, 0) AS SheetKRe,
       ISNULL(DCoverKBe, 0) AS DCoverKBe,
       ISNULL(DCoverKDe, 0) AS DCoverKDe,
       ISNULL(DCoverKRe, 0) AS DCoverKRe,
       ISNULL(KClothBe, 0) AS KClothBe,
       ISNULL(KClothDe, 0) AS KClothDe,
       ISNULL(KClothRe, 0) AS KClothRe,
       ISNULL(DClothBe, 0) AS DClothBe,
       ISNULL(DClothDe, 0) AS DClothDe,
       ISNULL(DClothRe, 0) AS DClothRe
FROM dbo.ViewPentryLinen
WHERE ID = @PickupID
  AND UserCode = @UserCode
ORDER BY IsOrder, TimeSection;", conn))
        {
            cmd.Parameters.Add("@UserCode", SqlDbType.VarChar, 50).Value = GetCurrentUserCode();
            cmd.Parameters.Add("@PickupID", SqlDbType.Int).Value = descriptionId.Value;

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                rows.Add(new LinenPantryPreviewRow
                {
                    Pentry = ToInt(rd["IsOrder"]),
                    PentryName = Convert.ToString(rd["PentryName"]) ?? string.Empty,
                    TimeSection = ToInt(rd["TimeSection"]),
                    BathBe = ToInt(rd["BathBe"]),
                    BathDe = ToInt(rd["BathDe"]),
                    BathRe = ToInt(rd["BathRe"]),
                    HandBe = ToInt(rd["HandBe"]),
                    HandDe = ToInt(rd["HandDe"]),
                    HandRe = ToInt(rd["HandRe"]),
                    FaceBe = ToInt(rd["FaceBe"]),
                    FaceDe = ToInt(rd["FaceDe"]),
                    FaceRe = ToInt(rd["FaceRe"]),
                    BathMBe = ToInt(rd["BathMBe"]),
                    BathMDe = ToInt(rd["BathMDe"]),
                    BathMRe = ToInt(rd["BathMRe"]),
                    PillowBe = ToInt(rd["PillowBe"]),
                    PillowDe = ToInt(rd["PillowDe"]),
                    PillowRe = ToInt(rd["PillowRe"]),
                    SheetSBe = ToInt(rd["SheetSBe"]),
                    SheetSDe = ToInt(rd["SheetSDe"]),
                    SheetSRe = ToInt(rd["SheetSRe"]),
                    DCoverSBe = ToInt(rd["DCoverSBe"]),
                    DCoverSDe = ToInt(rd["DCoverSDe"]),
                    DCoverSRe = ToInt(rd["DCoverSRe"]),
                    SheetKBe = ToInt(rd["SheetKBe"]),
                    SheetKDe = ToInt(rd["SheetKDe"]),
                    SheetKRe = ToInt(rd["SheetKRe"]),
                    DCoverKBe = ToInt(rd["DCoverKBe"]),
                    DCoverKDe = ToInt(rd["DCoverKDe"]),
                    DCoverKRe = ToInt(rd["DCoverKRe"]),
                    KClothBe = ToInt(rd["KClothBe"]),
                    KClothDe = ToInt(rd["KClothDe"]),
                    KClothRe = ToInt(rd["KClothRe"]),
                    DClothBe = ToInt(rd["DClothBe"]),
                    DClothDe = ToInt(rd["DClothDe"]),
                    DClothRe = ToInt(rd["DClothRe"])
                });
            }
        }

        return new JsonResult(new
        {
            success = true,
            reportType = LinenReportTypes.Pantry,
            descriptionId = header.Id,
            description = header.Description,
            dateText = header.DateCreate.ToString("yyyy-MM-dd"),
            columns = GetPantryColumns(linenCode),
            rows
        });
    }

    private JsonResult BuildDeliveryPreview(SqlConnection conn, int? descriptionId, string linenCode)
    {
        if (!descriptionId.HasValue || descriptionId.Value <= 0)
        {
            throw new InvalidOperationException("Delivery description is required.");
        }

        var preview = LoadDeliveryPreviewHeader(conn, descriptionId.Value);
        if (preview == null)
        {
            throw new InvalidOperationException("Delivery not found.");
        }

        var rows = new List<LinenDeliveryPreviewRow>();
        using var cmd = new SqlCommand(@"
SELECT Location,
       LinnenCode AS LinenCode,
       Quantity,
       Price,
       Amount,
       Note
FROM dbo.ViewLinenDelivery
WHERE DeliveryID = @DeliveryID
  AND (@LinenCode = '' OR ISNULL(LinnenCode, '') = @LinenCode);", conn);
        cmd.Parameters.Add("@DeliveryID", SqlDbType.Int).Value = descriptionId.Value;
        cmd.Parameters.Add("@LinenCode", SqlDbType.VarChar, 50).Value = linenCode;

        using (var rd = cmd.ExecuteReader())
        {
            while (rd.Read())
            {
                rows.Add(new LinenDeliveryPreviewRow
                {
                    Location = Convert.ToString(rd["Location"]) ?? string.Empty,
                    LinenCode = Convert.ToString(rd["LinenCode"]) ?? string.Empty,
                    Quantity = ToDecimal(rd["Quantity"]),
                    Price = ToDecimal(rd["Price"]),
                    Amount = ToDecimal(rd["Amount"]),
                    Note = Convert.ToString(rd["Note"]) ?? string.Empty
                });
            }
        }

        return new JsonResult(new
        {
            success = true,
            reportType = LinenReportTypes.Delivery,
            deliveryId = preview.DeliveryId,
            description = preview.Description,
            dateText = preview.DeliveryDate.ToString("yyyy-MM-dd"),
            supplierName = preview.SupplierName,
            deliveryTypeName = preview.DeliveryTypeName,
            rows
        });
    }

    private LinenDeliveryPreviewHeader? LoadDeliveryPreviewHeader(SqlConnection conn, int deliveryId)
    {
        using var cmd = new SqlCommand(@"
SELECT mt.DeliveryID,
       mt.DeliveryDate,
       ISNULL(mt.Des, '') AS Des,
       ISNULL(tp.LaundryTypeName, '') AS LaundryTypeName,
       ISNULL(sp.SupplierName, '') AS SupplierName
FROM dbo.LN_DeliveryMT mt
LEFT JOIN dbo.LN_LaudryType tp ON tp.LaundryTypeID = mt.DeliveryType
LEFT JOIN dbo.PC_Suppliers sp ON sp.SupplierID = mt.SupplierID
WHERE mt.DeliveryID = @DeliveryID;", conn);
        cmd.Parameters.Add("@DeliveryID", SqlDbType.Int).Value = deliveryId;

        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
        {
            return null;
        }

        return new LinenDeliveryPreviewHeader
        {
            DeliveryId = ToInt(rd["DeliveryID"]),
            DeliveryDate = rd["DeliveryDate"] == DBNull.Value ? DateTime.Today : Convert.ToDateTime(rd["DeliveryDate"]),
            Description = Convert.ToString(rd["Des"]) ?? string.Empty,
            SupplierName = Convert.ToString(rd["SupplierName"]) ?? string.Empty,
            DeliveryTypeName = Convert.ToString(rd["LaundryTypeName"]) ?? string.Empty
        };
    }

    private JsonResult BuildReceivePreview(SqlConnection conn, int? descriptionId, string linenCode)
    {
        if (!descriptionId.HasValue || descriptionId.Value <= 0)
        {
            throw new InvalidOperationException("Receive description is required.");
        }

        var rows = new List<LinenReceivePreviewRow>();
        var preview = new LinenReceivePreviewHeader();

        using var cmd = new SqlCommand(@"
SELECT ReceiveID,
       Des,
       DesOfDe AS RefDeliveryDes,
       SupplierName,
       ReceiveDate,
       Location,
       LinnenCode AS LinenCode,
       Quantity,
       Price,
       Amount,
       Note
FROM dbo.ViewLinenReceive
WHERE ReceiveID = @ReceiveID
  AND (@LinenCode = '' OR ISNULL(LinnenCode, '') = @LinenCode);", conn);
        cmd.Parameters.Add("@ReceiveID", SqlDbType.Int).Value = descriptionId.Value;
        cmd.Parameters.Add("@LinenCode", SqlDbType.VarChar, 50).Value = linenCode;

        using (var rd = cmd.ExecuteReader())
        {
            while (rd.Read())
            {
                if (preview.ReceiveId <= 0)
                {
                    preview.ReceiveId = ToInt(rd["ReceiveID"]);
                    preview.ReceiveDate = rd["ReceiveDate"] == DBNull.Value ? DateTime.Today : Convert.ToDateTime(rd["ReceiveDate"]);
                    preview.Description = Convert.ToString(rd["Des"]) ?? string.Empty;
                    preview.RefDeliveryDescription = Convert.ToString(rd["RefDeliveryDes"]) ?? string.Empty;
                    preview.SupplierName = Convert.ToString(rd["SupplierName"]) ?? string.Empty;
                }

                rows.Add(new LinenReceivePreviewRow
                {
                    Location = Convert.ToString(rd["Location"]) ?? string.Empty,
                    LinenCode = Convert.ToString(rd["LinenCode"]) ?? string.Empty,
                    Quantity = ToDecimal(rd["Quantity"]),
                    Price = ToDecimal(rd["Price"]),
                    Amount = ToDecimal(rd["Amount"]),
                    Note = Convert.ToString(rd["Note"]) ?? string.Empty
                });
            }
        }

        if (preview.ReceiveId <= 0)
        {
            throw new InvalidOperationException("Receive not found.");
        }

        return new JsonResult(new
        {
            success = true,
            reportType = LinenReportTypes.Receive,
            receiveId = preview.ReceiveId,
            description = preview.Description,
            dateText = preview.ReceiveDate.ToString("yyyy-MM-dd"),
            supplierName = preview.SupplierName,
            refDeliveryDescription = preview.RefDeliveryDescription,
            rows
        });
    }

    private JsonResult BuildLaundryRecordPreview(SqlConnection conn, DateTime fromDate, DateTime toDate, string linenCode)
    {
        if (fromDate.Month != toDate.Month || fromDate.Year != toDate.Year)
        {
            throw new InvalidOperationException("From Date and To Date must be in a month");
        }

        var normalizedFromDate = fromDate.Date.AddSeconds(1);
        var normalizedToDate = toDate.Date.AddDays(1).AddSeconds(-1);

        using (var procCmd = new SqlCommand("dbo.LN_LaundryRecordRPT", conn))
        {
            procCmd.CommandType = CommandType.StoredProcedure;
            procCmd.Parameters.Add("@Month", SqlDbType.Int).Value = fromDate.Month;
            procCmd.Parameters.Add("@Year", SqlDbType.Int).Value = fromDate.Year;
            procCmd.Parameters.Add("@FromDate", SqlDbType.VarChar, 50).Value = normalizedFromDate.ToString("yyyy-MM-dd HH:mm:ss");
            procCmd.Parameters.Add("@ToDate", SqlDbType.VarChar, 50).Value = normalizedToDate.ToString("yyyy-MM-dd HH:mm:ss");
            procCmd.Parameters.Add("@UserCode", SqlDbType.VarChar, 15).Value = GetCurrentUserCode();
            procCmd.ExecuteNonQuery();
        }

        var rows = new List<LaundryRecordPreviewRow>();
        using (var cmd = new SqlCommand(@"
SELECT View_LNLinenRecord.SupplierID,
       View_LNLinenRecord.LinenCode,
       View_LNLinenRecord.Price,
       View_LNLinenRecord.GroupID,
       View_LNLinenRecord.MyMonth,
       View_LNLinenRecord.MyYear,
       PC_Suppliers.SupplierName,
       SUM(View_LNLinenRecord.D01) AS D01,
       SUM(View_LNLinenRecord.D02) AS D02,
       SUM(View_LNLinenRecord.D03) AS D03,
       SUM(View_LNLinenRecord.D04) AS D04,
       SUM(View_LNLinenRecord.D05) AS D05,
       SUM(View_LNLinenRecord.D06) AS D06,
       SUM(View_LNLinenRecord.D07) AS D07,
       SUM(View_LNLinenRecord.D08) AS D08,
       SUM(View_LNLinenRecord.D09) AS D09,
       SUM(View_LNLinenRecord.D10) AS D10,
       SUM(View_LNLinenRecord.D11) AS D11,
       SUM(View_LNLinenRecord.D12) AS D12,
       SUM(View_LNLinenRecord.D13) AS D13,
       SUM(View_LNLinenRecord.D14) AS D14,
       SUM(View_LNLinenRecord.D15) AS D15,
       SUM(View_LNLinenRecord.D16) AS D16,
       SUM(View_LNLinenRecord.D17) AS D17,
       SUM(View_LNLinenRecord.D18) AS D18,
       SUM(View_LNLinenRecord.D19) AS D19,
       SUM(View_LNLinenRecord.D20) AS D20,
       SUM(View_LNLinenRecord.D21) AS D21,
       SUM(View_LNLinenRecord.D22) AS D22,
       SUM(View_LNLinenRecord.D23) AS D23,
       SUM(View_LNLinenRecord.D24) AS D24,
       SUM(View_LNLinenRecord.D25) AS D25,
       SUM(View_LNLinenRecord.D26) AS D26,
       SUM(View_LNLinenRecord.D27) AS D27,
       SUM(View_LNLinenRecord.D28) AS D28,
       SUM(View_LNLinenRecord.D29) AS D29,
       SUM(View_LNLinenRecord.D30) AS D30,
       SUM(View_LNLinenRecord.D31) AS D31
FROM dbo.View_LNLinenRecord
INNER JOIN dbo.PC_Suppliers ON View_LNLinenRecord.SupplierID = dbo.PC_Suppliers.SupplierID
WHERE View_LNLinenRecord.UserCode = @UserCode
  AND View_LNLinenRecord.MyMonth = @MyMonth
  AND View_LNLinenRecord.MyYear = @MyYear
  AND (@LinenCode = '' OR View_LNLinenRecord.LinenCode = @LinenCode)
GROUP BY View_LNLinenRecord.SupplierID,
         View_LNLinenRecord.LinenCode,
         View_LNLinenRecord.Price,
         View_LNLinenRecord.GroupID,
         View_LNLinenRecord.MyMonth,
         View_LNLinenRecord.MyYear,
         dbo.PC_Suppliers.SupplierName
ORDER BY View_LNLinenRecord.SupplierID ASC,
         View_LNLinenRecord.GroupID,
         View_LNLinenRecord.LinenCode ASC;", conn))
        {
            cmd.Parameters.Add("@UserCode", SqlDbType.VarChar, 15).Value = GetCurrentUserCode();
            cmd.Parameters.Add("@MyMonth", SqlDbType.Int).Value = fromDate.Month;
            cmd.Parameters.Add("@MyYear", SqlDbType.Int).Value = fromDate.Year;
            cmd.Parameters.Add("@LinenCode", SqlDbType.VarChar, 50).Value = linenCode;

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var dayValues = new List<decimal>(31);
                for (var day = 1; day <= 31; day++)
                {
                    dayValues.Add(ToDecimal(rd[$"D{day:00}"]));
                }

                rows.Add(new LaundryRecordPreviewRow
                {
                    SupplierId = ToInt(rd["SupplierID"]),
                    GroupId = ToInt(rd["GroupID"]),
                    SupplierName = Convert.ToString(rd["SupplierName"]) ?? string.Empty,
                    LinenCode = Convert.ToString(rd["LinenCode"]) ?? string.Empty,
                    Price = ToDecimal(rd["Price"]),
                    DayValues = dayValues
                });
            }
        }

        var groups = rows
            .GroupBy(x => new { x.SupplierId, x.SupplierName, x.GroupId })
            .Select(x => new
            {
                supplierId = x.Key.SupplierId,
                supplierName = x.Key.SupplierName,
                groupId = x.Key.GroupId,
                groupName = GetLaundryRecordGroupName(x.Key.GroupId),
                rows = x.Select(r => new
                {
                    linenCode = r.LinenCode,
                    price = r.Price.ToString("0.##"),
                    days = r.DayValues.Select(d => d.ToString("0.##")).ToList(),
                    total = r.DayValues.Sum().ToString("0.##")
                }).ToList()
            })
            .ToList();

        return new JsonResult(new
        {
            success = true,
            reportType = LinenReportTypes.LaundryRecord,
            fromDate = fromDate.ToString("yyyy-MM-dd"),
            toDate = toDate.ToString("yyyy-MM-dd"),
            groups
        });
    }

    private JsonResult BuildNotReceivePreview(SqlConnection conn, string linenCode)
    {
        using (var procCmd = new SqlCommand("dbo.LN_MarkFullReceiveOnAllDelevery", conn))
        {
            procCmd.CommandType = CommandType.StoredProcedure;
            procCmd.ExecuteNonQuery();
        }

        var rows = new List<NotReceivePreviewRow>();
        using (var cmd = new SqlCommand(@"
SELECT ISNULL(DeliveryID, 0) AS DeliveryID,
       DeliveryDate,
       ISNULL(Des, '') AS Des,
       ISNULL(SupplierName, '') AS SupplierName,
       ISNULL(LinenCode, '') AS LinenCode,
       ISNULL(QuantityDe, 0) AS QuantityDe,
       ISNULL(QuantityRe, 0) AS QuantityRe
FROM dbo.ViewNotReceive
WHERE (QuantityDe > QuantityRe OR (QuantityRe IS NULL AND QuantityDe > 0))
  AND (@LinenCode = '' OR LinenCode = @LinenCode)
ORDER BY DeliveryDate DESC, DeliveryID DESC, LinenCode ASC;", conn))
        {
            cmd.Parameters.Add("@LinenCode", SqlDbType.VarChar, 50).Value = linenCode;
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var quantityDe = ToDecimal(rd["QuantityDe"]);
                var quantityRe = ToDecimal(rd["QuantityRe"]);
                rows.Add(new NotReceivePreviewRow
                {
                    DeliveryId = ToInt(rd["DeliveryID"]),
                    DeliveryDate = rd["DeliveryDate"] == DBNull.Value ? null : Convert.ToDateTime(rd["DeliveryDate"]),
                    Description = Convert.ToString(rd["Des"]) ?? string.Empty,
                    SupplierName = Convert.ToString(rd["SupplierName"]) ?? string.Empty,
                    LinenCode = Convert.ToString(rd["LinenCode"]) ?? string.Empty,
                    QuantityDe = quantityDe,
                    QuantityRe = quantityRe,
                    Remain = quantityDe - quantityRe
                });
            }
        }

        return new JsonResult(new
        {
            success = true,
            reportType = LinenReportTypes.NotReceive,
            rows = rows.Select(x => new
            {
                deliveryId = x.DeliveryId,
                deliveryDate = x.DeliveryDate?.ToString("yyyy-MM-dd") ?? string.Empty,
                description = x.Description,
                supplierName = x.SupplierName,
                linenCode = x.LinenCode,
                quantityDe = x.QuantityDe.ToString("0.##"),
                quantityRe = x.QuantityRe.ToString("0.##"),
                remain = x.Remain.ToString("0.##")
            })
        });
    }

    private JsonResult BuildLaundryBalancePreview(SqlConnection conn, DateTime fromDate, DateTime toDate)
    {
        var rows = new List<LaundryBalancePreviewRow>();
        using (var cmd = new SqlCommand("dbo.LN_LaundryRoomBalance", conn))
        {
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.Add("@Fromdate", SqlDbType.DateTime).Value = fromDate.Date;
            cmd.Parameters.Add("@ToDate", SqlDbType.DateTime).Value = toDate.Date;
            cmd.Parameters.Add("@SupplierID", SqlDbType.Int).Value = LaundryBalanceSupplierId;

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var begin = ToDecimal(rd["TonDauNam"]) + ToDecimal(rd["Nhan1"]) + ToDecimal(rd["Nhan2"]) - ToDecimal(rd["Giao1"]) - ToDecimal(rd["Giao2"]);
                var receiveApartment = ToDecimal(rd["NhanTuCanHo"]);
                var receiveSupplier = ToDecimal(rd["NhanTuNCC"]);
                var deliveryApartment = ToDecimal(rd["GiaoLenCanHo"]);
                var deliverySupplier = ToDecimal(rd["GiaoNCC"]);
                var end = begin + receiveApartment + receiveSupplier - deliveryApartment - deliverySupplier;

                rows.Add(new LaundryBalancePreviewRow
                {
                    LinenCode = Convert.ToString(rd["LinnenCode"]) ?? string.Empty,
                    Begin = begin,
                    ReceiveApartment = receiveApartment,
                    ReceiveSupplier = receiveSupplier,
                    DeliveryApartment = deliveryApartment,
                    DeliverySupplier = deliverySupplier,
                    End = end
                });
            }
        }

        return new JsonResult(new
        {
            success = true,
            reportType = LinenReportTypes.LaundryBalance,
            fromDate = fromDate.ToString("yyyy-MM-dd"),
            toDate = toDate.ToString("yyyy-MM-dd"),
            rows = rows.Select(x => new
            {
                linenCode = x.LinenCode,
                begin = x.Begin.ToString("0.##"),
                receiveApartment = x.ReceiveApartment.ToString("0.##"),
                receiveSupplier = x.ReceiveSupplier.ToString("0.##"),
                deliveryApartment = x.DeliveryApartment.ToString("0.##"),
                deliverySupplier = x.DeliverySupplier.ToString("0.##"),
                end = x.End.ToString("0.##")
            })
        });
    }

    private JsonResult BuildApartmentBalancePreview(SqlConnection conn, int? descriptionId, DateTime fromDate, DateTime toDate)
    {
        if (!descriptionId.HasValue || descriptionId.Value <= 0)
        {
            throw new InvalidOperationException("Apartment is required.");
        }

        var apartmentNo = GetApartmentNo(conn, descriptionId.Value);
        if (string.IsNullOrWhiteSpace(apartmentNo))
        {
            throw new InvalidOperationException("Apartment not found.");
        }

        var rows = new List<ApartmentBalancePreviewRow>();
        using (var cmd = new SqlCommand("dbo.LN_ApmtLaundryBalance", conn))
        {
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.Add("@ApmtID", SqlDbType.Int).Value = descriptionId.Value;
            cmd.Parameters.Add("@Fromdate", SqlDbType.DateTime).Value = fromDate.Date;
            cmd.Parameters.Add("@ToDate", SqlDbType.DateTime).Value = toDate.Date;

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var begin = ToDecimal(rd["TonDauNam"]) + ToDecimal(rd["Nhan1"]) - ToDecimal(rd["Giao1"]);
                var receiveApartment = ToDecimal(rd["NhapVaoCanHo"]);
                var deliveryApartment = ToDecimal(rd["XuatRaTuCanHo"]);
                var end = begin + receiveApartment - deliveryApartment;

                rows.Add(new ApartmentBalancePreviewRow
                {
                    LinenCode = Convert.ToString(rd["LinnenCode"]) ?? string.Empty,
                    Begin = begin,
                    ReceiveApartment = receiveApartment,
                    DeliveryApartment = deliveryApartment,
                    End = end
                });
            }
        }

        return new JsonResult(new
        {
            success = true,
            reportType = LinenReportTypes.ApmtBalance,
            apartmentId = descriptionId.Value,
            apartmentNo,
            fromDate = fromDate.ToString("yyyy-MM-dd"),
            toDate = toDate.ToString("yyyy-MM-dd"),
            rows = rows.Select(x => new
            {
                linenCode = x.LinenCode,
                begin = x.Begin.ToString("0.##"),
                receiveApartment = x.ReceiveApartment.ToString("0.##"),
                deliveryApartment = x.DeliveryApartment.ToString("0.##"),
                end = x.End.ToString("0.##")
            })
        });
    }

    private LinenReportModeState BuildModeState(SqlConnection conn, string reportType, int? requestedDescriptionId)
    {
        var state = new LinenReportModeState
        {
            ReportType = reportType
        };

        if (reportType == LinenReportTypes.Pantry)
        {
            state.DescriptionEnabled = true;
            state.LinenEnabled = true;
            state.DescriptionLabel = "Des";
            state.DescriptionOptions = LoadDescriptionOptions(conn, @"
SELECT ID, Des
FROM dbo.LN_DeAndReMT
ORDER BY ID DESC;", requestedDescriptionId);
        }
        else if (reportType == LinenReportTypes.Delivery)
        {
            state.DescriptionEnabled = true;
            state.LinenEnabled = true;
            state.DescriptionLabel = "Des";
            state.DescriptionOptions = LoadDescriptionOptions(conn, @"
SELECT DeliveryID AS ID, Des
FROM dbo.LN_DeliveryMT
ORDER BY DeliveryID DESC;", requestedDescriptionId);
        }
        else if (reportType == LinenReportTypes.Receive)
        {
            state.DescriptionEnabled = true;
            state.LinenEnabled = true;
            state.DescriptionLabel = "Des";
            state.DescriptionOptions = LoadDescriptionOptions(conn, @"
SELECT ReceiveID AS ID, Des
FROM dbo.LN_ReceiveMT
ORDER BY ReceiveID DESC;", requestedDescriptionId);
        }
        else if (reportType == LinenReportTypes.LaundryRecord)
        {
            state.LinenEnabled = true;
            state.FromEnabled = true;
            state.ToEnabled = true;
            state.ChartEnabled = false;
        }
        else if (reportType == LinenReportTypes.NotReceive)
        {
            state.LinenEnabled = true;
        }
        else if (reportType == LinenReportTypes.LaundryBalance)
        {
            state.FromEnabled = true;
            state.ToEnabled = true;
            state.ChartEnabled = false;
        }
        else if (reportType == LinenReportTypes.ApmtBalance)
        {
            state.DescriptionEnabled = true;
            state.DescriptionLabel = "APMT";
            state.FromEnabled = true;
            state.ToEnabled = true;
            state.ChartEnabled = false;
            state.DescriptionOptions = LoadApartmentOptions(conn, requestedDescriptionId);
        }

        if (state.DescriptionOptions.Count > 0)
        {
            state.SelectedDescriptionId = GetSelectedValue(state.DescriptionOptions, requestedDescriptionId);
        }

        return state;
    }

    private static int? GetSelectedValue(List<SelectListItem> items, int? requestedValue)
    {
        if (requestedValue.HasValue && items.Any(x => x.Value == requestedValue.Value.ToString()))
        {
            return requestedValue.Value;
        }

        var first = items.FirstOrDefault();
        if (first == null || string.IsNullOrWhiteSpace(first.Value))
        {
            return null;
        }

        return Convert.ToInt32(first.Value);
    }

    private void ApplyModeState(LinenReportModeState state)
    {
        Filter.ReportType = state.ReportType;
        DescriptionLabel = state.DescriptionLabel;
        DescriptionEnabled = state.DescriptionEnabled;
        LinenEnabled = state.LinenEnabled;
        FromEnabled = state.FromEnabled;
        ToEnabled = state.ToEnabled;
        ChartEnabled = state.ChartEnabled;
        DescriptionOptions = state.DescriptionOptions;
    }

    private static List<SelectListItem> LoadDescriptionOptions(SqlConnection conn, string sql, int? requestedValue)
    {
        var items = new List<SelectListItem>();
        using var cmd = new SqlCommand(sql, conn);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            items.Add(new SelectListItem
            {
                Value = Convert.ToInt32(rd["ID"]).ToString(),
                Text = Convert.ToString(rd["Des"]) ?? string.Empty,
                Selected = requestedValue.HasValue && requestedValue.Value == Convert.ToInt32(rd["ID"])
            });
        }

        return items;
    }

    private static List<SelectListItem> LoadApartmentOptions(SqlConnection conn, int? requestedValue)
    {
        var items = new List<SelectListItem>();
        using var cmd = new SqlCommand(@"
SELECT ApmtID AS ID, ApartmentNo AS Des
FROM dbo.AM_Apmt
WHERE GETDATE() >= CAST(ExistFrom AS datetime)
  AND GETDATE() <= DATEADD(second, 86399, CAST(ExistTo AS datetime))
ORDER BY ApartmentNo;", conn);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            items.Add(new SelectListItem
            {
                Value = Convert.ToInt32(rd["ID"]).ToString(),
                Text = Convert.ToString(rd["Des"]) ?? string.Empty,
                Selected = requestedValue.HasValue && requestedValue.Value == Convert.ToInt32(rd["ID"])
            });
        }

        return items;
    }

    private static List<SelectListItem> LoadLinenOptions(SqlConnection conn)
    {
        var items = new List<SelectListItem>
        {
            new SelectListItem { Value = string.Empty, Text = "ALL" }
        };

        using var cmd = new SqlCommand(@"
SELECT DISTINCT LinnenCode
FROM dbo.LN_Linnen
WHERE ISNULL(LinnenCode, '') <> ''
ORDER BY LinnenCode;", conn);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var linenCode = Convert.ToString(rd["LinnenCode"]) ?? string.Empty;
            items.Add(new SelectListItem
            {
                Value = linenCode,
                Text = linenCode
            });
        }

        return items;
    }

    private PantryHeaderInfo? LoadPantryHeader(SqlConnection conn, int id)
    {
        using var cmd = new SqlCommand(@"
SELECT ID, ISNULL(Des, '') AS Des, DateCreate
FROM dbo.LN_DeAndReMT
WHERE ID = @ID;", conn);
        cmd.Parameters.Add("@ID", SqlDbType.Int).Value = id;

        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
        {
            return null;
        }

        return new PantryHeaderInfo
        {
            Id = ToInt(rd["ID"]),
            Description = Convert.ToString(rd["Des"]) ?? string.Empty,
            DateCreate = rd["DateCreate"] == DBNull.Value ? DateTime.Today : Convert.ToDateTime(rd["DateCreate"])
        };
    }

    private string GetApartmentNo(SqlConnection conn, int apartmentId)
    {
        using var cmd = new SqlCommand("SELECT ApartmentNo FROM dbo.AM_Apmt WHERE ApmtID = @ApmtID;", conn);
        cmd.Parameters.Add("@ApmtID", SqlDbType.Int).Value = apartmentId;
        return Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
    }

    private byte[]? LoadCompanyLogoBytes(SqlConnection conn)
    {
        using var cmd = new SqlCommand(@"
SELECT TOP 1 ISNULL(CoLogo, '') AS CoLogo
FROM dbo.MS_Parameters;", conn);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var logoPath = ResolveCompanyLogoPathValue(Convert.ToString(reader["CoLogo"]));
        if (string.IsNullOrWhiteSpace(logoPath) || !System.IO.File.Exists(logoPath))
        {
            return null;
        }

        return System.IO.File.ReadAllBytes(logoPath);
    }

    private string ResolveCompanyLogoPathValue(string? logoFileName)
    {
        if (string.IsNullOrWhiteSpace(logoFileName))
        {
            return string.Empty;
        }

        var logoReference = logoFileName.Trim();
        if (System.IO.File.Exists(logoReference))
        {
            return logoReference;
        }

        var logoSegments = logoReference
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (logoSegments.Length == 0 || logoSegments.Any(x => x == "." || x == ".."))
        {
            return string.Empty;
        }

        var contentRootCandidate = Path.Combine([_webHostEnvironment.ContentRootPath, .. logoSegments]);
        if (System.IO.File.Exists(contentRootCandidate))
        {
            return contentRootCandidate;
        }

        var basePath = _config.GetValue<string>("FileUploads:BasePath");
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return string.Empty;
        }

        var rootPath = Path.IsPathRooted(basePath)
            ? basePath
            : Path.Combine(_webHostEnvironment.ContentRootPath, basePath);

        var uploadRootCandidate = Path.Combine([rootPath, .. logoSegments]);
        return System.IO.File.Exists(uploadRootCandidate) ? uploadRootCandidate : string.Empty;
    }

    private string GetCurrentUserCode()
    {
        return User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "SYSTEM";
    }

    private void NormalizeFilter()
    {
        Filter.ReportType = NormalizeReportType(Filter.ReportType);
        var lockedType = NormalizeLockedReportType(LockedReportType);
        if (!string.IsNullOrWhiteSpace(lockedType))
        {
            LockedReportType = lockedType;
            Filter.ReportType = lockedType;
            IsTypeLocked = true;
        }
        else
        {
            LockedReportType = string.Empty;
            IsTypeLocked = false;
        }

        Filter.LinenCode = (Filter.LinenCode ?? string.Empty).Trim();
        Filter.FromDate ??= DateTime.Today;
        Filter.ToDate ??= DateTime.Today;
        if (Filter.FromDate > Filter.ToDate)
        {
            Filter.ToDate = Filter.FromDate;
        }
    }

    private static string NormalizeReportType(string? reportType)
    {
        var value = (reportType ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            LinenReportTypes.Pantry => LinenReportTypes.Pantry,
            LinenReportTypes.Delivery => LinenReportTypes.Delivery,
            LinenReportTypes.Receive => LinenReportTypes.Receive,
            LinenReportTypes.LaundryRecord => LinenReportTypes.LaundryRecord,
            LinenReportTypes.NotReceive => LinenReportTypes.NotReceive,
            LinenReportTypes.LaundryBalance => LinenReportTypes.LaundryBalance,
            LinenReportTypes.ApmtBalance => LinenReportTypes.ApmtBalance,
            _ => LinenReportTypes.LaundryRecord
        };
    }

    private string ResolveRequestedReportType(string? reportType)
    {
        if (!string.IsNullOrWhiteSpace(LockedReportType))
        {
            return LockedReportType;
        }

        return NormalizeReportType(reportType);
    }

    private static string NormalizeLockedReportType(string? reportType)
    {
        var value = (reportType ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            LinenReportTypes.Pantry => LinenReportTypes.Pantry,
            LinenReportTypes.Delivery => LinenReportTypes.Delivery,
            LinenReportTypes.Receive => LinenReportTypes.Receive,
            LinenReportTypes.LaundryRecord => LinenReportTypes.LaundryRecord,
            LinenReportTypes.NotReceive => LinenReportTypes.NotReceive,
            LinenReportTypes.LaundryBalance => LinenReportTypes.LaundryBalance,
            LinenReportTypes.ApmtBalance => LinenReportTypes.ApmtBalance,
            _ => string.Empty
        };
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

    private bool HasPageAccess()
    {
        return PagePerm.HasPermission(PermissionView);
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

    private static int ToInt(object value)
    {
        return value == DBNull.Value ? 0 : Convert.ToInt32(value);
    }

    private static decimal ToDecimal(object value)
    {
        return value == DBNull.Value ? 0 : Convert.ToDecimal(value);
    }

    private static List<LinenPantryPreviewColumn> GetPantryColumns(string linenCode)
    {
        var columns = new List<LinenPantryPreviewColumn>
        {
            new LinenPantryPreviewColumn("BATH", "Bath", "BathBe", "BathDe", "BathRe"),
            new LinenPantryPreviewColumn("HAND", "Hand", "HandBe", "HandDe", "HandRe"),
            new LinenPantryPreviewColumn("FACE", "Face", "FaceBe", "FaceDe", "FaceRe"),
            new LinenPantryPreviewColumn("BATH-MAT", "Bath mat", "BathMBe", "BathMDe", "BathMRe"),
            new LinenPantryPreviewColumn("PILLOW-CASE", "Pillow case", "PillowBe", "PillowDe", "PillowRe"),
            new LinenPantryPreviewColumn("SHEET-S", "Sheet S", "SheetSBe", "SheetSDe", "SheetSRe"),
            new LinenPantryPreviewColumn("D-COVER-S", "D Cover S", "DCoverSBe", "DCoverSDe", "DCoverSRe"),
            new LinenPantryPreviewColumn("SHEET-K", "Sheet K", "SheetKBe", "SheetKDe", "SheetKRe"),
            new LinenPantryPreviewColumn("D-COVER-K", "D Cover K", "DCoverKBe", "DCoverKDe", "DCoverKRe"),
            new LinenPantryPreviewColumn("K-CLOTH", "K Cloth", "KClothBe", "KClothDe", "KClothRe"),
            new LinenPantryPreviewColumn("D-CLOTH", "D Cloth", "DClothBe", "DClothDe", "DClothRe")
        };

        var code = (linenCode ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
        {
            return columns;
        }

        return columns.Where(x => x.Code == code).ToList();
    }

    private static string GetLaundryRecordGroupName(int groupId)
    {
        return groupId switch
        {
            1 => "Pantry-linen",
            2 => "Uniform",
            3 => "Guest",
            4 => "Other",
            5 => "Apartment",
            _ => "Group " + groupId
        };
    }
}

public class LinenReportFilter
{
    public string ReportType { get; set; } = LinenReportTypes.LaundryRecord;
    public int? DescriptionId { get; set; }
    public string LinenCode { get; set; } = string.Empty;
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

public static class LinenReportTypes
{
    public const string Pantry = "pantry";
    public const string Delivery = "delivery";
    public const string Receive = "receive";
    public const string LaundryRecord = "laundry-record";
    public const string NotReceive = "not-receive";
    public const string LaundryBalance = "laundry-balance";
    public const string ApmtBalance = "apmt-balance";
}

public class LinenReportModeState
{
    public string ReportType { get; set; } = LinenReportTypes.LaundryRecord;
    public string DescriptionLabel { get; set; } = "Des";
    public bool DescriptionEnabled { get; set; }
    public bool LinenEnabled { get; set; }
    public bool FromEnabled { get; set; }
    public bool ToEnabled { get; set; }
    public bool ChartEnabled { get; set; }
    public int? SelectedDescriptionId { get; set; }
    public List<SelectListItem> DescriptionOptions { get; set; } = new List<SelectListItem>();
}

public class PantryHeaderInfo
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime DateCreate { get; set; }
}

public class LinenPantryPreviewColumn
{
    public LinenPantryPreviewColumn(string code, string title, string beField, string deField, string reField)
    {
        Code = code;
        Title = title;
        BeField = beField;
        DeField = deField;
        ReField = reField;
    }

    public string Code { get; set; }
    public string Title { get; set; }
    public string BeField { get; set; }
    public string DeField { get; set; }
    public string ReField { get; set; }
}

public class LinenPantryPreviewRow
{
    public int Pentry { get; set; }
    public string PentryName { get; set; } = string.Empty;
    public int TimeSection { get; set; }
    public int BathBe { get; set; }
    public int BathDe { get; set; }
    public int BathRe { get; set; }
    public int HandBe { get; set; }
    public int HandDe { get; set; }
    public int HandRe { get; set; }
    public int FaceBe { get; set; }
    public int FaceDe { get; set; }
    public int FaceRe { get; set; }
    public int BathMBe { get; set; }
    public int BathMDe { get; set; }
    public int BathMRe { get; set; }
    public int PillowBe { get; set; }
    public int PillowDe { get; set; }
    public int PillowRe { get; set; }
    public int SheetSBe { get; set; }
    public int SheetSDe { get; set; }
    public int SheetSRe { get; set; }
    public int DCoverSBe { get; set; }
    public int DCoverSDe { get; set; }
    public int DCoverSRe { get; set; }
    public int SheetKBe { get; set; }
    public int SheetKDe { get; set; }
    public int SheetKRe { get; set; }
    public int DCoverKBe { get; set; }
    public int DCoverKDe { get; set; }
    public int DCoverKRe { get; set; }
    public int KClothBe { get; set; }
    public int KClothDe { get; set; }
    public int KClothRe { get; set; }
    public int DClothBe { get; set; }
    public int DClothDe { get; set; }
    public int DClothRe { get; set; }
}

public class LinenDeliveryPreviewHeader
{
    public int DeliveryId { get; set; }
    public DateTime DeliveryDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string DeliveryTypeName { get; set; } = string.Empty;
}

public class LinenDeliveryPreviewRow
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

public class LinenReceivePreviewHeader
{
    public int ReceiveId { get; set; }
    public DateTime ReceiveDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string RefDeliveryDescription { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
}

public class LinenReceivePreviewRow
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

public class LaundryRecordPreviewRow
{
    public int SupplierId { get; set; }
    public int GroupId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string LinenCode { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public List<decimal> DayValues { get; set; } = new List<decimal>();
}

public class NotReceivePreviewRow
{
    public int DeliveryId { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string LinenCode { get; set; } = string.Empty;
    public decimal QuantityDe { get; set; }
    public decimal QuantityRe { get; set; }
    public decimal Remain { get; set; }
}

public class LaundryBalancePreviewRow
{
    public string LinenCode { get; set; } = string.Empty;
    public decimal Begin { get; set; }
    public decimal ReceiveApartment { get; set; }
    public decimal ReceiveSupplier { get; set; }
    public decimal DeliveryApartment { get; set; }
    public decimal DeliverySupplier { get; set; }
    public decimal End { get; set; }
}

public class ApartmentBalancePreviewRow
{
    public string LinenCode { get; set; } = string.Empty;
    public decimal Begin { get; set; }
    public decimal ReceiveApartment { get; set; }
    public decimal DeliveryApartment { get; set; }
    public decimal End { get; set; }
}

