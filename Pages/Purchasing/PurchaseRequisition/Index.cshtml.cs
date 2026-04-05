using System.Data;
using System.Globalization;
using System.IO;
using System.Text.Json;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;
using SmartSam.Services.Interfaces;

namespace SmartSam.Pages.Purchasing.PurchaseRequisition;

public class IndexModel : BasePageModel
{
    private static readonly HashSet<int> AllowedPageSizes = [10, 20, 50, 100, 200];
    private static readonly string[] AcceptedDateFormats = ["yyyy-MM-dd", "M/d/yyyy", "MM/dd/yyyy", "d/M/yyyy", "dd/MM/yyyy"];
    private const int FUNCTION_ID = 72;
    private const int PermissionViewList = 1;
    private const int PermissionViewDetail = 2;
    private const int PermissionAdd = 3;
    private const int PermissionEdit = 4;
    private const int PermissionDisapproval = 6;
    private const int PermissionChangeStatus = 6;
    private readonly ILogger<IndexModel> _logger;
    private readonly PermissionService _permissionService;
    private readonly ISecurityService _securityService;
    private readonly IWebHostEnvironment _webHostEnvironment;

    // Khởi tạo các service và thành phần cần dùng cho màn hình danh sách phiếu đề nghị mua hàng.
    public IndexModel(IConfiguration config, ILogger<IndexModel> logger, PermissionService permissionService, ISecurityService securityService, IWebHostEnvironment webHostEnvironment) : base(config)
    {
        _logger = logger;
        _permissionService = permissionService;
        _securityService = securityService;
        _webHostEnvironment = webHostEnvironment;
    }

    public PagePermissions PagePerm { get; private set; } = new PagePermissions();
    public int DefaultPageSize => _config.GetValue<int>("AppSettings:DefaultPageSize", 10);

    [BindProperty(SupportsGet = true)]
    public PurchaseRequisitionFilter Filter { get; set; } = new PurchaseRequisitionFilter();

    [BindProperty(SupportsGet = true)]
    public int? SelectedPrId { get; set; }

    [BindProperty]
    public string AddAtDetailsJson { get; set; } = "[]";

    [BindProperty]
    public string AddAtRequestNo { get; set; } = string.Empty;

    [BindProperty]
    public DateTime? AddAtRequestDate { get; set; }

    [BindProperty]
    public string AddAtDescription { get; set; } = string.Empty;

    [BindProperty]
    public byte AddAtCurrencyId { get; set; } = 1;

    [BindProperty]
    public List<IFormFile> AddAtAttachments { get; set; } = new List<IFormFile>();

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public string MessageType { get; set; } = "info";

    public List<PurchaseRequisitionRow> Rows { get; set; } = new List<PurchaseRequisitionRow>();
    public List<SelectListItem> StatusList { get; set; } = new List<SelectListItem>();
    public List<PurchaseRequisitionItemLookup> ItemList { get; set; } = new List<PurchaseRequisitionItemLookup>();
    public List<PurchaseRequisitionSupplierLookup> SupplierList { get; set; } = new List<PurchaseRequisitionSupplierLookup>();
    public bool CanAddNew { get; set; }
    public bool CanAddAt { get; set; }
    public bool CanEditRequisition { get; set; }
    public bool CanViewDetailRequisition { get; set; }
    public int TotalRecords { get; set; }
    public string AllowedAttachmentExtensionsText => _config.GetValue<string>("AllowedExtensions") ?? ".doc,.docx,.xls,.xlsx,.pdf,.jpg,.jpeg,.png";
    public int MaxAttachmentSizeMb => _config.GetValue<int?>("MaxFileSizeMb") ?? 10;
    public int TotalPages => Filter.PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)Filter.PageSize));
    public int PageStart => TotalRecords == 0 ? 0 : ((Filter.Page - 1) * Filter.PageSize) + 1;
    public int PageEnd => TotalRecords == 0 ? 0 : Math.Min(Filter.Page * Filter.PageSize, TotalRecords);
    public bool HasPreviousPage => Filter.Page > 1;
    public bool HasNextPage => Filter.Page < TotalPages;

    private PurchaseRequisitionWorkflowUserInfo _workflowUser = new PurchaseRequisitionWorkflowUserInfo();

    // Xử lý tải dữ liệu ban đầu của màn hình.
    public IActionResult OnGet()
    {
        PagePerm = GetUserPermissions();
        LoadCurrentWorkflowUser();
        LoadPageActions();
        if (!HasPermission(PermissionViewList))
        {
            return Redirect("/");
        }

        // 2. Chuẩn hóa filter trước khi nạp dữ liệu để danh sách và phân trang luôn đồng bộ.
        NormalizeQueryInputs();
        NormalizeFilter();
        LoadStatusList();
        LoadLookups();
        LoadPurchaseRequisitionRows();
        return Page();
    }

    // Xử lý yêu cầu tìm kiếm danh sách theo điều kiện người dùng nhập.
    public IActionResult OnPostSearch([FromBody] PurchaseRequisitionSearchRequest request)
    {
        try
        {
            // 1. Lấy quyền thực tế của role đang đăng nhập
            PagePerm = GetUserPermissions();
            LoadCurrentWorkflowUser();
            LoadPageActions();
            if (!HasPermission(PermissionViewList))
            {
                return new JsonResult(new { success = false, message = "You have no permission to access purchase requisitions." });
            }

            // 2. Build filter tìm kiếm và lấy danh sách dữ liệu theo đúng điều kiện người dùng chọn.
            var filter = BuildSearchFilter(request);
            var (rows, totalRecords) = SearchPurchaseRequisitionRows(filter);

            var data = rows.Select(row => new
            {
                data = row,
                actions = BuildRowActions(row)
            });

            return new JsonResult(new
            {
                success = true,
                data,
                total = totalRecords,
                page = filter.Page,
                pageSize = filter.PageSize,
                totalPages = filter.PageSize <= 0 ? 1 : (int)Math.Ceiling((double)totalRecords / filter.PageSize)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in PurchaseRequisition search.");
            return new JsonResult(new { success = false, message = ex.Message });
        }
    }

    // Xử lý thao tác Add AT để tạo nhanh chứng từ và chi tiết.
    public IActionResult OnPostAddAt()
    {
        PagePerm = GetUserPermissions();
        LoadCurrentWorkflowUser();
        LoadPageActions();
        if (!CanAddAt)
        {
            return Redirect("/");
        }

        // 2. Chuẩn hóa filter sau post để quay lại đúng danh sách người dùng đang xem.
        NormalizePostInputs();
        NormalizeFilter();

        var details = ParseAddAtSourceRows();

        if (string.IsNullOrWhiteSpace(AddAtDescription))
        {
            ModelState.AddModelError(string.Empty, "Description is required.");
        }

        if (details.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Please select at least one MR row.");
        }

        for (var i = 0; i < details.Count; i++)
        {
            ValidateAddAtSourceRow(details[i], i + 1);
        }

        ValidateAddAtAttachment();

        if (!ModelState.IsValid)
        {
            Message = GetModelStateErrorMessage();
            MessageType = "error";
            return RedirectToPage("./Index", BuildRouteValues());
        }

        try
        {
            var newPrId = CreatePurchaseRequisitionFromAddAt(details);
            return RedirectToPage("./PurchaseRequisitionDetail", new { id = newPrId, mode = "edit" });
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
            MessageType = "error";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot save Add AT details.");
            Message = "Cannot save Add AT details. Please try again.";
            MessageType = "error";
        }

        return RedirectToPage("./Index", BuildRouteValues());
    }

    // Xử lý nạp dữ liệu nguồn cho popup Add AT theo flow Gen PR từ MR.
    public IActionResult OnGetAddAtSource()
    {
        PagePerm = GetUserPermissions();
        LoadCurrentWorkflowUser();
        LoadPageActions();
        if (!CanAddAt)
        {
            return new JsonResult(new { success = false, message = "You have no permission to use Add AT." });
        }

        try
        {
            var rows = LoadAddAtSourceRows();
            var currencies = LoadCurrencyOptions();
            var requestNo = GetSuggestedAddAtRequestNo();
            var requestDate = DateTime.Today.ToString("yyyy-MM-dd");
            var defaultCurrencyId = currencies.FirstOrDefault()?.Id ?? (byte)1;

            return new JsonResult(new
            {
                success = true,
                requestNo,
                requestDate,
                currencyId = defaultCurrencyId,
                currencies,
                rows
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot load Add AT source rows.");
            return new JsonResult(new { success = false, message = "Cannot load Add AT source data." });
        }
    }

    // Xử lý xuất dữ liệu ra file Excel.
    public IActionResult OnGetExportExcel()
    {
        PagePerm = GetUserPermissions();
        LoadCurrentWorkflowUser();
        if (!HasPermission(PermissionViewList))
        {
            return Redirect("/");
        }

        // 2. Export phải bám đúng điều kiện filter hiện tại của màn hình danh sách.
        NormalizeQueryInputs();
        NormalizeFilter();

        if (SelectedPrId.HasValue && SelectedPrId.Value > 0)
        {
            var requisition = LoadPurchaseRequisitionForExport(SelectedPrId.Value);
            if (requisition == null)
            {
                return RedirectToPage("./Index", BuildRouteValues());
            }

            if (!CanViewDetailRow(requisition.StatusId))
            {
                return Redirect("/");
            }

            return ExportPurchaseRequisitionDetailExcel(requisition);
        }

        var exportFilter = new PurchaseRequisitionFilter
        {
            RequestNo = Filter.RequestNo,
            StatusId = Filter.StatusId,
            Description = Filter.Description,
            UseDateRange = Filter.UseDateRange,
            FromDate = Filter.FromDate,
            ToDate = Filter.ToDate,
            Page = 1,
            PageSize = int.MaxValue
        };

        var (rows, _) = SearchPurchaseRequisitionRows(exportFilter);
        return ExportPurchaseRequisitionListExcel(rows);
    }

    // Thực hiện xử lý cho hàm ExportPurchaseRequisitionListExcel theo nghiệp vụ của màn hình.
    private IActionResult ExportPurchaseRequisitionListExcel(List<PurchaseRequisitionRow> rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Purchase Requisition");

        // 1. File export danh sách hiển thị đúng các cột đang dùng ở màn hình list.
        var headers = new[]
        {
            "STT",
            "No.",
            "Date",
            "Description",
            "Status",
            "Purchaser",
            "C of A",
            "G Director"
        };

        for (var col = 0; col < headers.Length; col++)
        {
            worksheet.Cell(1, col + 1).Value = headers[col];
        }

        var rowIndex = 2;
        var orderNo = 1;
        foreach (var row in rows)
        {
            worksheet.Cell(rowIndex, 1).Value = orderNo;
            worksheet.Cell(rowIndex, 2).Value = row.RequestNo;
            worksheet.Cell(rowIndex, 3).Value = row.RequestDate == DateTime.MinValue ? string.Empty : row.RequestDate.ToString("dd/MM/yyyy");
            worksheet.Cell(rowIndex, 4).Value = row.Description;
            worksheet.Cell(rowIndex, 5).Value = row.StatusName;
            worksheet.Cell(rowIndex, 6).Value = row.PurchaserCode;
            worksheet.Cell(rowIndex, 7).Value = row.ChiefACode;
            worksheet.Cell(rowIndex, 8).Value = row.GDirectorCode;
            rowIndex++;
            orderNo++;
        }

        FormatWorksheetAsTable(worksheet, 1, Math.Max(1, rowIndex - 1), headers.Length);
        return BuildExcelFileResult(workbook, "purchase_requisition");
    }

    // Thực hiện xử lý cho hàm ExportPurchaseRequisitionDetailExcel theo nghiệp vụ của màn hình.
    private IActionResult ExportPurchaseRequisitionDetailExcel(PurchaseRequisitionExportHeader requisition)
    {
        using var workbook = new XLWorkbook();
        var headerSheet = workbook.Worksheets.Add("Purchase Requisition");
        var detailSheet = workbook.Worksheets.Add("PR Items");

        // 1. Sheet đầu tiên chứa thông tin header của phiếu đang được chọn.
        var headerRows = new (string Label, string Value)[]
        {
            ("No.", requisition.RequestNo),
            ("Date", requisition.RequestDate == DateTime.MinValue ? string.Empty : requisition.RequestDate.ToString("dd/MM/yyyy")),
            ("Description", requisition.Description),
            ("Status", requisition.StatusName),
            ("Purchaser", requisition.PurchaserCode),
            ("C of A", requisition.ChiefACode),
            ("G Director", requisition.GDirectorCode)
        };

        for (var i = 0; i < headerRows.Length; i++)
        {
            headerSheet.Cell(i + 1, 1).Value = headerRows[i].Label;
            headerSheet.Cell(i + 1, 2).Value = headerRows[i].Value;
        }

        FormatWorksheetAsTable(headerSheet, 1, headerRows.Length, 2);

        // 2. Sheet thứ hai chứa toàn bộ item detail của phiếu đang chọn.
        var detailHeaders = new[]
        {
            "STT",
            "Item Code",
            "Item Name",
            "Unit",
            "QtyFromM",
            "QtyPur",
            "U.Price",
            "Amount",
            "Remark",
            "Supplier"
        };

        for (var col = 0; col < detailHeaders.Length; col++)
        {
            detailSheet.Cell(1, col + 1).Value = detailHeaders[col];
        }

        var rowIndex = 2;
        var orderNo = 1;
        foreach (var detail in requisition.Details)
        {
            detailSheet.Cell(rowIndex, 1).Value = orderNo;
            detailSheet.Cell(rowIndex, 2).Value = detail.ItemCode;
            detailSheet.Cell(rowIndex, 3).Value = detail.ItemName;
            detailSheet.Cell(rowIndex, 4).Value = detail.Unit;
            detailSheet.Cell(rowIndex, 5).Value = detail.QtyFromM;
            detailSheet.Cell(rowIndex, 6).Value = detail.QtyPur;
            detailSheet.Cell(rowIndex, 7).Value = detail.UnitPrice;
            detailSheet.Cell(rowIndex, 8).Value = detail.QtyPur * detail.UnitPrice;
            detailSheet.Cell(rowIndex, 9).Value = detail.Remark ?? string.Empty;
            detailSheet.Cell(rowIndex, 10).Value = detail.SupplierText;
            rowIndex++;
            orderNo++;
        }

        FormatWorksheetAsTable(detailSheet, 1, Math.Max(1, rowIndex - 1), detailHeaders.Length);
        detailSheet.Column(7).Style.NumberFormat.Format = "#,##0.00";
        detailSheet.Column(8).Style.NumberFormat.Format = "#,##0.00";
        return BuildExcelFileResult(workbook, $"purchase_requisition_{requisition.RequestNo}");
    }

    // Thực hiện xử lý cho hàm LoadPurchaseRequisitionForExport theo nghiệp vụ của màn hình.
    private PurchaseRequisitionExportHeader? LoadPurchaseRequisitionForExport(int prId)
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        using var cmd = new SqlCommand(@"
SELECT
    p.PRID,
    ISNULL(p.RequestNo, '') AS RequestNo,
    p.RequestDate,
    ISNULL(p.[Description], '') AS [Description],
    ISNULL(p.[Status], 1) AS [Status],
    ISNULL(NULLIF(st.PRStatusName, ''), CASE p.[Status]
        WHEN 1 THEN 'New'
        WHEN 2 THEN 'Waiting For Approve'
        WHEN 3 THEN 'Pending'
        WHEN 4 THEN 'Done'
        ELSE 'New'
    END) AS StatusName,
    ISNULL(ep.EmployeeCode, '') AS PurchaserCode,
    ISNULL(ec.EmployeeCode, '') AS ChiefACode,
    ISNULL(eg.EmployeeCode, '') AS GDirectorCode
FROM dbo.PC_PR p
LEFT JOIN dbo.PC_PRStatus st ON p.Status = st.PRStatusID
LEFT JOIN dbo.MS_Employee ep ON p.PurId = ep.EmployeeID
LEFT JOIN dbo.MS_Employee ec ON p.CAId = ec.EmployeeID
LEFT JOIN dbo.MS_Employee eg ON p.GDId = eg.EmployeeID
WHERE p.PRID = @PRID", conn);

        cmd.Parameters.Add("@PRID", SqlDbType.Int).Value = prId;
        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
        {
            return null;
        }

        var requisition = new PurchaseRequisitionExportHeader
        {
            Id = Convert.ToInt32(rd["PRID"]),
            RequestNo = Convert.ToString(rd["RequestNo"]) ?? string.Empty,
            RequestDate = rd.IsDBNull(rd.GetOrdinal("RequestDate")) ? DateTime.MinValue : Convert.ToDateTime(rd["RequestDate"]),
            Description = Convert.ToString(rd["Description"]) ?? string.Empty,
            StatusId = Convert.ToByte(rd["Status"]),
            StatusName = Convert.ToString(rd["StatusName"]) ?? string.Empty,
            PurchaserCode = Convert.ToString(rd["PurchaserCode"]) ?? string.Empty,
            ChiefACode = Convert.ToString(rd["ChiefACode"]) ?? string.Empty,
            GDirectorCode = Convert.ToString(rd["GDirectorCode"]) ?? string.Empty
        };
        rd.Close();

        requisition.Details = LoadPurchaseRequisitionDetailRows(conn, prId);
        return requisition;
    }

    // Thực hiện xử lý cho hàm LoadPurchaseRequisitionDetailRows theo nghiệp vụ của màn hình.
    private List<PurchaseRequisitionDetailInput> LoadPurchaseRequisitionDetailRows(SqlConnection conn, int prId)
    {
        var rows = new List<PurchaseRequisitionDetailInput>();

        using var cmd = new SqlCommand(@"
SELECT d.RecordID,
       d.ItemID,
       ISNULL(i.ItemCode, '') AS ItemCode,
       ISNULL(i.ItemName, '') AS ItemName,
       ISNULL(i.Unit, '') AS Unit,
       ISNULL(d.SugQty, 0) AS QtyFromM,
       ISNULL(d.Quantity, 0) AS QtyPur,
       ISNULL(d.UnitPrice, 0) AS UnitPrice,
       ISNULL(d.Remark, '') AS Remark,
       d.SupplierID,
       CASE WHEN s.SupplierID IS NULL THEN '' ELSE ISNULL(s.SupplierCode, '') + ' - ' + ISNULL(s.SupplierName, '') END AS SupplierText
FROM dbo.PC_PRDetail d
LEFT JOIN dbo.INV_ItemList i ON d.ItemID = i.ItemID
LEFT JOIN dbo.PC_Suppliers s ON d.SupplierID = s.SupplierID
WHERE d.PRID = @PRID
ORDER BY d.RecordID", conn);

        cmd.Parameters.Add("@PRID", SqlDbType.Int).Value = prId;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(new PurchaseRequisitionDetailInput
            {
                DetailId = rd.IsDBNull(rd.GetOrdinal("RecordID")) ? 0 : Convert.ToInt64(rd["RecordID"]),
                ItemId = Convert.ToInt32(rd["ItemID"]),
                ItemCode = Convert.ToString(rd["ItemCode"]) ?? string.Empty,
                ItemName = Convert.ToString(rd["ItemName"]) ?? string.Empty,
                Unit = Convert.ToString(rd["Unit"]) ?? string.Empty,
                QtyFromM = Convert.ToDecimal(rd["QtyFromM"]),
                QtyPur = Convert.ToDecimal(rd["QtyPur"]),
                UnitPrice = Convert.ToDecimal(rd["UnitPrice"]),
                Remark = Convert.ToString(rd["Remark"]),
                SupplierId = rd.IsDBNull(rd.GetOrdinal("SupplierID")) ? null : Convert.ToInt32(rd["SupplierID"]),
                SupplierText = Convert.ToString(rd["SupplierText"]) ?? string.Empty
            });
        }

        return rows;
    }

    // Thực hiện xử lý cho hàm FormatWorksheetAsTable theo nghiệp vụ của màn hình.
    private void FormatWorksheetAsTable(IXLWorksheet worksheet, int fromRow, int toRow, int totalColumns)
    {
        var range = worksheet.Range(fromRow, 1, toRow, totalColumns);
        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        range.Style.Alignment.WrapText = true;
        range.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;

        worksheet.Row(fromRow).Style.Font.Bold = true;
        worksheet.Row(fromRow).Style.Fill.BackgroundColor = XLColor.FromHtml("#D9E2F3");
        worksheet.Columns().AdjustToContents();

        for (var col = 1; col <= totalColumns; col++)
        {
            if (worksheet.Column(col).Width > 40)
            {
                worksheet.Column(col).Width = 40;
            }
        }
    }

    // Thực hiện xử lý cho hàm BuildExcelFileResult theo nghiệp vụ của màn hình.
    private FileContentResult BuildExcelFileResult(XLWorkbook workbook, string filePrefix)
    {
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);

        var fileName = $"{filePrefix}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    // Thực hiện xử lý cho hàm LoadPurchaseRequisitionRows theo nghiệp vụ của màn hình.
    private void LoadPurchaseRequisitionRows()
    {
        var (rows, totalRecords) = SearchPurchaseRequisitionRows(Filter);
        Rows = rows;
        TotalRecords = totalRecords;

        if (TotalRecords > 0 && Filter.Page > TotalPages)
        {
            Filter.Page = TotalPages;
            (rows, totalRecords) = SearchPurchaseRequisitionRows(Filter);
            Rows = rows;
            TotalRecords = totalRecords;
        }
    }

    // Thực hiện xử lý cho hàm SearchPurchaseRequisitionRows theo nghiệp vụ của màn hình.
    private (List<PurchaseRequisitionRow> rows, int totalRecords) SearchPurchaseRequisitionRows(PurchaseRequisitionFilter filter)
    {
        var rows = new List<PurchaseRequisitionRow>();
        var totalRecords = 0;
        var page = filter.Page <= 0 ? 1 : filter.Page;
        var pageSize = AllowedPageSizes.Contains(filter.PageSize) ? filter.PageSize : DefaultPageSize;
        var offset = (page - 1) * pageSize;

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        // 1. Đếm tổng số bản ghi trước để phục vụ phân trang.
        using (var countCmd = new SqlCommand(@"
        SELECT COUNT(1)
        FROM dbo.PC_PR p
        LEFT JOIN dbo.PC_PRStatus st ON p.Status = st.PRStatusID
        LEFT JOIN dbo.MS_Employee ep ON p.PurId = ep.EmployeeID
        LEFT JOIN dbo.MS_Employee ec ON p.CAId = ec.EmployeeID
        LEFT JOIN dbo.MS_Employee eg ON p.GDId = eg.EmployeeID
        WHERE (@RequestNo IS NULL OR p.RequestNo LIKE '%' + @RequestNo + '%')
        AND (@StatusID IS NULL OR p.[Status] = @StatusID)
        AND (@Description IS NULL OR p.[Description] LIKE '%' + @Description + '%')
        AND (
                @UseDateRange = 0
                OR (@FromDate IS NULL OR p.RequestDate >= @FromDate)
                AND (@ToDate IS NULL OR p.RequestDate < DATEADD(DAY, 1, @ToDate))
            )", conn))
        {
            BindSearchParams(countCmd, filter);
            totalRecords = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);
        }

        // 2. Lấy danh sách dữ liệu theo đúng trang đang xem.
        using var cmd = new SqlCommand(@"
        SELECT
            p.PRID,
            ISNULL(p.RequestNo, '') AS RequestNo,
            p.RequestDate,
            ISNULL(p.[Description], '') AS [Description],
            p.[Status],
            ISNULL(NULLIF(st.PRStatusName, ''), CASE p.[Status]
                WHEN 1 THEN 'New'
                WHEN 2 THEN 'Waiting For Approve'
                WHEN 3 THEN 'Pending'
                WHEN 4 THEN 'Done'
                ELSE 'New'
            END) AS StatusName,
            ISNULL(ep.EmployeeCode, '') AS PurchaserCode,
            ISNULL(ec.EmployeeCode, '') AS ChiefACode,
            ISNULL(eg.EmployeeCode, '') AS GDirectorCode
        FROM dbo.PC_PR p
        LEFT JOIN dbo.PC_PRStatus st ON p.Status = st.PRStatusID
        LEFT JOIN dbo.MS_Employee ep ON p.PurId = ep.EmployeeID
        LEFT JOIN dbo.MS_Employee ec ON p.CAId = ec.EmployeeID
        LEFT JOIN dbo.MS_Employee eg ON p.GDId = eg.EmployeeID
        WHERE (@RequestNo IS NULL OR p.RequestNo LIKE '%' + @RequestNo + '%')
        AND (@StatusID IS NULL OR p.[Status] = @StatusID)
        AND (@Description IS NULL OR p.[Description] LIKE '%' + @Description + '%')
        AND (
                @UseDateRange = 0
                OR (@FromDate IS NULL OR p.RequestDate >= @FromDate)
                AND (@ToDate IS NULL OR p.RequestDate < DATEADD(DAY, 1, @ToDate))
            )
        ORDER BY p.RequestDate DESC, p.PRID DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", conn);

        BindSearchParams(cmd, filter);
        cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
        cmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(new PurchaseRequisitionRow
            {
                Id = Convert.ToInt32(rd["PRID"]),
                RequestNo = Convert.ToString(rd["RequestNo"]) ?? string.Empty,
                RequestDate = rd.IsDBNull(rd.GetOrdinal("RequestDate")) ? DateTime.MinValue : Convert.ToDateTime(rd["RequestDate"]),
                Description = Convert.ToString(rd["Description"]) ?? string.Empty,
                StatusId = rd.IsDBNull(rd.GetOrdinal("Status")) ? null : Convert.ToByte(rd["Status"]),
                StatusName = Convert.ToString(rd["StatusName"]) ?? string.Empty,
                PurchaserCode = Convert.ToString(rd["PurchaserCode"]) ?? string.Empty,
                ChiefACode = Convert.ToString(rd["ChiefACode"]) ?? string.Empty,
                GDirectorCode = Convert.ToString(rd["GDirectorCode"]) ?? string.Empty
            });
        }

        return (rows, totalRecords);
    }

    private object BuildRowActions(PurchaseRequisitionRow row) => new
    {
        canEdit = CanEditRow(row.StatusId),
        canAddAt = CanAddAt,
        canViewDetail = CanViewDetailRow(row.StatusId),
        canDisapproval = HasPermission(PermissionDisapproval),
        accessMode = CanEditRow(row.StatusId) ? "edit" : "view"
    };

    // Thực hiện xử lý cho hàm LoadStatusList theo nghiệp vụ của màn hình.
    private void LoadStatusList()
    {
        StatusList = new List<SelectListItem>
        {
            new SelectListItem
            {
                Value = string.Empty,
                Text = "--- All ---"
            }
        };

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
SELECT PRStatusID, ISNULL(PRStatusName, '') AS PRStatusName
FROM dbo.PC_PRStatus
ORDER BY PRStatusID", conn);

        conn.Open();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            StatusList.Add(new SelectListItem
            {
                Value = Convert.ToString(rd["PRStatusID"]),
                Text = Convert.ToString(rd["PRStatusName"])
            });
        }
    }

    // Thực hiện xử lý cho hàm LoadLookups theo nghiệp vụ của màn hình.
    private void LoadLookups()
    {
        LoadItemList();
        LoadSupplierList();
    }

    // Thực hiện xử lý cho hàm LoadItemList theo nghiệp vụ của màn hình.
    private void LoadItemList()
    {
        ItemList = new List<PurchaseRequisitionItemLookup>();

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
SELECT TOP 200
    ItemID,
    ISNULL(ItemCode, '') AS ItemCode,
    ISNULL(ItemName, '') AS ItemName,
    ISNULL(Unit, '') AS Unit
FROM dbo.INV_ItemList
WHERE ISNULL(IsPurchase, 0) = 1
ORDER BY ItemCode", conn);

        conn.Open();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            ItemList.Add(new PurchaseRequisitionItemLookup
            {
                Id = Convert.ToInt32(rd["ItemID"]),
                ItemCode = Convert.ToString(rd["ItemCode"]) ?? string.Empty,
                ItemName = Convert.ToString(rd["ItemName"]) ?? string.Empty,
                Unit = Convert.ToString(rd["Unit"]) ?? string.Empty
            });
        }
    }

    // Thực hiện xử lý cho hàm LoadSupplierList theo nghiệp vụ của màn hình.
    private void LoadSupplierList()
    {
        SupplierList = new List<PurchaseRequisitionSupplierLookup>();

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
SELECT TOP 200
    SupplierID,
    ISNULL(SupplierCode, '') AS SupplierCode,
    ISNULL(SupplierName, '') AS SupplierName
FROM dbo.PC_Suppliers
ORDER BY SupplierCode", conn);

        conn.Open();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            SupplierList.Add(new PurchaseRequisitionSupplierLookup
            {
                Id = Convert.ToInt32(rd["SupplierID"]),
                SupplierCode = Convert.ToString(rd["SupplierCode"]) ?? string.Empty,
                SupplierName = Convert.ToString(rd["SupplierName"]) ?? string.Empty
            });
        }
    }

    // Nạp danh sách dòng MR đủ điều kiện để popup Add AT tạo PR mới.
    private List<PurchaseRequisitionAddAtSourceRow> LoadAddAtSourceRows()
    {
        var rows = new List<PurchaseRequisitionAddAtSourceRow>();

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        using var cmd = new SqlCommand(@"
SELECT
    d.REQUEST_NO,
    i.ItemID,
    ISNULL(i.ItemCode, '') AS ItemCode,
    ISNULL(i.ItemName, '') AS ItemName,
    ISNULL(i.Unit, '') AS Unit,
    ISNULL(d.BUY, 0) AS BUY,
    ISNULL(i.UnitPrice, 0) AS UnitPrice,
    ISNULL(i.Specification, '') AS Specification,
    ISNULL(d.NOTE, '') AS Note,
    d.ID AS MRDetailID
FROM dbo.MATERIAL_REQUEST m
INNER JOIN dbo.MATERIAL_REQUEST_DETAIL d ON m.REQUEST_NO = d.REQUEST_NO
INNER JOIN dbo.INV_ItemList i ON d.ITEMCODE = i.ItemCode
WHERE ISNULL(d.BUY, 0) > 0
  AND (d.PostedPR = 0 OR d.PostedPR IS NULL)
  AND
  (
      (m.MATERIALSTATUSID = 3 AND ISNULL(d.BUY, 0) > 0)
      OR
      (m.MATERIALSTATUSID = 2 AND ISNULL(m.IS_AUTO, 0) = 1)
  )
ORDER BY d.REQUEST_NO, i.ItemCode", conn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var buy = Convert.ToDecimal(rd["BUY"]);
            rows.Add(new PurchaseRequisitionAddAtSourceRow
            {
                RequestNo = rd.IsDBNull(rd.GetOrdinal("REQUEST_NO")) ? 0 : Convert.ToDecimal(rd["REQUEST_NO"]),
                ItemId = Convert.ToInt32(rd["ItemID"]),
                ItemCode = Convert.ToString(rd["ItemCode"]) ?? string.Empty,
                ItemName = Convert.ToString(rd["ItemName"]) ?? string.Empty,
                Unit = Convert.ToString(rd["Unit"]) ?? string.Empty,
                Buy = buy,
                SugBuy = buy,
                UnitPrice = Convert.ToDecimal(rd["UnitPrice"]),
                Specification = Convert.ToString(rd["Specification"]) ?? string.Empty,
                Note = Convert.ToString(rd["Note"]) ?? string.Empty,
                MrDetailId = Convert.ToInt32(rd["MRDetailID"])
            });
        }

        return rows;
    }

    // Nạp danh sách tiền tệ cho popup Add AT, nếu bảng rỗng thì dùng giá trị mặc định của hệ thống.
    private List<PurchaseRequisitionCurrencyOption> LoadCurrencyOptions()
    {
        var currencies = new List<PurchaseRequisitionCurrencyOption>();

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        using var cmd = new SqlCommand("SELECT CurrencyID, CurrencyName FROM dbo.MS_CurrencyFL ORDER BY CurrencyID", conn);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            currencies.Add(new PurchaseRequisitionCurrencyOption
            {
                Id = Convert.ToByte(rd["CurrencyID"]),
                Name = Convert.ToString(rd["CurrencyName"]) ?? string.Empty
            });
        }

        if (currencies.Count == 0)
        {
            currencies.Add(new PurchaseRequisitionCurrencyOption
            {
                Id = 1,
                Name = "VND"
            });
        }

        return currencies;
    }

    // Sinh số PR cho popup Add AT theo stored procedure cũ của hệ thống.
    private string GetSuggestedAddAtRequestNo()
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        return GetSuggestedAddAtRequestNo(conn, null);
    }

    // Sinh số PR trong cùng transaction để tránh lệch dữ liệu khi đang tạo mới chứng từ.
    private static string GetSuggestedAddAtRequestNo(SqlConnection conn, SqlTransaction? trans)
    {
        using var cmd = new SqlCommand("EXEC dbo.HaiAutoNumPR NULL", conn, trans);
        var result = Convert.ToString(cmd.ExecuteScalar());
        return string.IsNullOrWhiteSpace(result) ? $"PR{DateTime.Now:ddMMyy}" : result.Trim();
    }

    // Xác định EmployeeID của người đang đăng nhập để gán làm người lập PR.
    private int? ResolveCurrentEmployeeId(SqlConnection conn, SqlTransaction trans)
    {
        var employeeCode = User?.Identity?.Name;
        if (string.IsNullOrWhiteSpace(employeeCode))
        {
            return null;
        }

        using var cmd = new SqlCommand("SELECT TOP 1 EmployeeID FROM dbo.MS_Employee WHERE EmployeeCode = @EmployeeCode", conn, trans);
        cmd.Parameters.Add("@EmployeeCode", SqlDbType.VarChar, 20).Value = employeeCode.Trim();

        var result = cmd.ExecuteScalar();
        return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }

    // Tạo mới một PR từ các dòng MR được chọn trong popup Add AT.
    private int CreatePurchaseRequisitionFromAddAt(IReadOnlyList<PurchaseRequisitionAddAtSourceRow> details)
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var trans = conn.BeginTransaction();
        var savedFilePaths = new List<string>();

        try
        {
            var requestDate = AddAtRequestDate?.Date ?? DateTime.Today;
            var requestNo = string.IsNullOrWhiteSpace(AddAtRequestNo) ? GetSuggestedAddAtRequestNo(conn, trans) : AddAtRequestNo.Trim();
            var purchaserId = ResolveCurrentEmployeeId(conn, trans);

            using var headerCmd = new SqlCommand(@"
INSERT INTO dbo.PC_PR
(
    RequestNo,
    RequestDate,
    [Description],
    Currency,
    [Status],
    PurId,
    IsAuto,
    PostPO
)
OUTPUT INSERTED.PRID
VALUES
(
    @RequestNo,
    @RequestDate,
    @Description,
    @Currency,
    1,
    @PurId,
    1,
    0
)", conn, trans);

            headerCmd.Parameters.Add("@RequestNo", SqlDbType.VarChar, 50).Value = requestNo;
            headerCmd.Parameters.Add("@RequestDate", SqlDbType.DateTime).Value = requestDate;
            headerCmd.Parameters.Add("@Description", SqlDbType.NVarChar, 500).Value = AddAtDescription.Trim();
            headerCmd.Parameters.Add("@Currency", SqlDbType.TinyInt).Value = AddAtCurrencyId <= 0 ? (byte)1 : AddAtCurrencyId;
            headerCmd.Parameters.Add("@PurId", SqlDbType.Int).Value = purchaserId.HasValue ? purchaserId.Value : DBNull.Value;

            var newPrId = Convert.ToInt32(headerCmd.ExecuteScalar() ?? 0);
            if (newPrId <= 0)
            {
                throw new InvalidOperationException("Cannot create purchase requisition from Add AT.");
            }

            foreach (var detail in details)
            {
                InsertAddAtDetail(conn, trans, newPrId, detail);
                UpdateMaterialRequestAfterAddAt(conn, trans, detail);
            }

            foreach (var attachment in AddAtAttachments.Where(file => file != null && file.Length > 0))
            {
                SaveAddAtAttachment(conn, trans, newPrId, purchaserId, attachment, savedFilePaths);
            }

            trans.Commit();
            return newPrId;
        }
        catch
        {
            trans.Rollback();
            RemoveSavedFiles(savedFilePaths);
            throw;
        }
    }

    // Lưu file đính kèm của Add AT vào thư mục cấu hình và ghi tên file vào bảng PC_PR_Doc.
    private void SaveAddAtAttachment(SqlConnection conn, SqlTransaction trans, int prId, int? userId, IFormFile attachment, List<string> savedFilePaths)
    {
        if (attachment == null || attachment.Length <= 0)
        {
            return;
        }

        var uploadFolder = ResolveAddAtUploadFolder();
        Directory.CreateDirectory(uploadFolder);

        var savedFileName = BuildAttachmentFileName(attachment.FileName);
        var fullPath = Path.Combine(uploadFolder, savedFileName);

        using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            attachment.CopyTo(stream);
        }

        savedFilePaths.Add(fullPath);

        using var docCmd = new SqlCommand(@"
INSERT INTO dbo.PC_PR_Doc
(
    PRID,
    FilePath,
    UploadDate,
    UserID
)
VALUES
(
    @PRID,
    @FilePath,
    GETDATE(),
    @UserID
)", conn, trans);

        docCmd.Parameters.Add("@PRID", SqlDbType.Int).Value = prId;
        docCmd.Parameters.Add("@FilePath", SqlDbType.NVarChar, 1000).Value = savedFileName;
        docCmd.Parameters.Add("@UserID", SqlDbType.Int).Value = userId.HasValue ? userId.Value : DBNull.Value;
        docCmd.ExecuteNonQuery();
    }

    // Xác định thư mục lưu file theo cấu hình FilePath trong appsettings.json.
    private string ResolveAddAtUploadFolder()
    {
        var configuredPath = _config.GetValue<string>("FilePath");
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            throw new InvalidOperationException("FilePath is missing in appsettings.json.");
        }

        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(_webHostEnvironment.ContentRootPath, configuredPath);
    }

    // Sinh tên file mới có gắn timestamp để tránh trùng tên khi upload nhiều lần.
    private static string BuildAttachmentFileName(string originalFileName)
    {
        var sourceName = Path.GetFileName(originalFileName);
        var nameOnly = Path.GetFileNameWithoutExtension(sourceName);
        var extension = Path.GetExtension(sourceName);
        var safeName = string.Concat(nameOnly.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "attachment";
        }

        var timeLong = DateTime.UtcNow.Ticks;
        return $"{safeName}_{timeLong}{extension}";
    }

    // Xóa các file đã ghi ra đĩa nếu transaction lưu PR bị lỗi và phải rollback.
    private static void RemoveSavedFiles(IEnumerable<string> savedFilePaths)
    {
        foreach (var path in savedFilePaths)
        {
            try
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
            }
            catch
            {
                // Không chặn flow rollback nếu việc dọn file tạm bị lỗi.
            }
        }
    }

    // Ghi từng dòng MR đã chọn vào PC_PRDetail theo flow Gen PR cũ.
    private static void InsertAddAtDetail(SqlConnection conn, SqlTransaction trans, int prId, PurchaseRequisitionAddAtSourceRow detail)
    {
        using var detailCmd = new SqlCommand(@"
INSERT INTO dbo.PC_PRDetail
(
    PRID,
    ItemID,
    Quantity,
    UnitPrice,
    Remark,
    RecQty,
    OrdAmount,
    RecAmount,
    POed,
    MRRequestNO,
    SugQty,
    SupplierID,
    PoQuantity,
    PoQuantitySug,
    MRDetailID
)
VALUES
(
    @PRID,
    @ItemID,
    @Quantity,
    @UnitPrice,
    @Remark,
    0,
    @OrdAmount,
    0,
    0,
    @MRRequestNO,
    @SugQty,
    NULL,
    0,
    @PoQuantitySug,
    @MRDetailID
)", conn, trans);

        detailCmd.Parameters.Add("@PRID", SqlDbType.Int).Value = prId;
        detailCmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = detail.ItemId;
        detailCmd.Parameters.Add("@Quantity", SqlDbType.Decimal).Value = detail.SugBuy;
        detailCmd.Parameters.Add("@UnitPrice", SqlDbType.Decimal).Value = detail.UnitPrice;
        detailCmd.Parameters.Add("@Remark", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(detail.Specification) ? DBNull.Value : detail.Specification.Trim();
        detailCmd.Parameters.Add("@OrdAmount", SqlDbType.Decimal).Value = detail.SugBuy * detail.UnitPrice;
        detailCmd.Parameters.Add("@MRRequestNO", SqlDbType.Decimal).Value = detail.RequestNo;
        detailCmd.Parameters.Add("@SugQty", SqlDbType.Decimal).Value = detail.Buy;
        detailCmd.Parameters.Add("@PoQuantitySug", SqlDbType.Decimal).Value = detail.SugBuy;
        detailCmd.Parameters.Add("@MRDetailID", SqlDbType.Int).Value = detail.MrDetailId;
        detailCmd.ExecuteNonQuery();
    }

    // Ghi một dòng chi tiết PR theo dữ liệu đang nhập ở màn hình chi tiết.
    internal static void InsertDetail(SqlConnection conn, SqlTransaction trans, int prId, PurchaseRequisitionDetailInput detail)
    {
        using var detailCmd = new SqlCommand(@"
INSERT INTO dbo.PC_PRDetail
(
    PRID,
    ItemID,
    Quantity,
    UnitPrice,
    Remark,
    RecQty,
    OrdAmount,
    RecAmount,
    POed,
    MRRequestNO,
    SugQty,
    SupplierID,
    PoQuantity,
    PoQuantitySug,
    MRDetailID
)
VALUES
(
    @PRID,
    @ItemID,
    @Quantity,
    @UnitPrice,
    @Remark,
    0,
    @OrdAmount,
    0,
    0,
    @MRRequestNO,
    @SugQty,
    @SupplierID,
    0,
    @PoQuantitySug,
    @MRDetailID
)", conn, trans);

        detailCmd.Parameters.Add("@PRID", SqlDbType.Int).Value = prId;
        detailCmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = detail.ItemId;
        detailCmd.Parameters.Add("@Quantity", SqlDbType.Decimal).Value = detail.QtyPur;
        detailCmd.Parameters.Add("@UnitPrice", SqlDbType.Decimal).Value = detail.UnitPrice;
        detailCmd.Parameters.Add("@Remark", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(detail.Remark) ? DBNull.Value : detail.Remark.Trim();
        detailCmd.Parameters.Add("@OrdAmount", SqlDbType.Decimal).Value = detail.QtyPur * detail.UnitPrice;
        detailCmd.Parameters.Add("@MRRequestNO", SqlDbType.VarChar, 50).Value = string.IsNullOrWhiteSpace(detail.MrRequestNo) ? DBNull.Value : detail.MrRequestNo.Trim();
        detailCmd.Parameters.Add("@SugQty", SqlDbType.Decimal).Value = detail.QtyFromM;
        detailCmd.Parameters.Add("@SupplierID", SqlDbType.Int).Value = detail.SupplierId.HasValue ? detail.SupplierId.Value : DBNull.Value;
        detailCmd.Parameters.Add("@PoQuantitySug", SqlDbType.Decimal).Value = detail.QtyPur;
        detailCmd.Parameters.Add("@MRDetailID", SqlDbType.Int).Value = detail.MrDetailId.HasValue ? detail.MrDetailId.Value : DBNull.Value;
        detailCmd.ExecuteNonQuery();
    }

    // Cập nhật ngược lại dữ liệu MR sau khi một dòng đã được gom vào PR.
    private static void UpdateMaterialRequestAfterAddAt(SqlConnection conn, SqlTransaction trans, PurchaseRequisitionAddAtSourceRow detail)
    {
        var remainBuy = detail.Buy - detail.SugBuy;
        if (detail.SugBuy < detail.Buy)
        {
            using var updateCmd = new SqlCommand(@"
UPDATE dbo.MATERIAL_REQUEST_DETAIL
SET BUY = @RemainBuy
WHERE ID = @MRDetailID", conn, trans);

            updateCmd.Parameters.Add("@RemainBuy", SqlDbType.Decimal).Value = remainBuy < 0 ? 0 : remainBuy;
            updateCmd.Parameters.Add("@MRDetailID", SqlDbType.Int).Value = detail.MrDetailId;
            updateCmd.ExecuteNonQuery();
            return;
        }

        using var postedCmd = new SqlCommand(@"
UPDATE dbo.MATERIAL_REQUEST_DETAIL
SET PostedPR = 1
WHERE ID = @MRDetailID", conn, trans);

        postedCmd.Parameters.Add("@MRDetailID", SqlDbType.Int).Value = detail.MrDetailId;
        postedCmd.ExecuteNonQuery();
    }

    internal static void BindSearchParams(SqlCommand cmd, PurchaseRequisitionFilter filter)
    {
        // 1. Bộ tham số tìm kiếm dùng chung cho cả câu đếm và câu lấy danh sách.
        cmd.Parameters.Add("@RequestNo", SqlDbType.VarChar, 20).Value = string.IsNullOrWhiteSpace(filter.RequestNo) ? DBNull.Value : filter.RequestNo.Trim();
        cmd.Parameters.Add("@StatusID", SqlDbType.Int).Value = filter.StatusId.HasValue ? filter.StatusId.Value : DBNull.Value;
        cmd.Parameters.Add("@Description", SqlDbType.VarChar, 500).Value = string.IsNullOrWhiteSpace(filter.Description) ? DBNull.Value : filter.Description.Trim();
        cmd.Parameters.Add("@UseDateRange", SqlDbType.Bit).Value = filter.UseDateRange;
        cmd.Parameters.Add("@FromDate", SqlDbType.DateTime).Value = filter.FromDate.HasValue ? filter.FromDate.Value.Date : DBNull.Value;
        cmd.Parameters.Add("@ToDate", SqlDbType.DateTime).Value = filter.ToDate.HasValue ? filter.ToDate.Value.Date : DBNull.Value;
    }

    // Thực hiện xử lý cho hàm NormalizeFilter theo nghiệp vụ của màn hình.
    private void NormalizeFilter()
    {
        if (!AllowedPageSizes.Contains(Filter.PageSize))
        {
            Filter.PageSize = DefaultPageSize;
        }

        if (Filter.Page <= 0)
        {
            Filter.Page = 1;
        }

        if (!Filter.UseDateRange)
        {
            Filter.FromDate = null;
            Filter.ToDate = null;
        }
        else if (!Filter.FromDate.HasValue)
        {
            Filter.ToDate = null;
        }

        if (Filter.UseDateRange && Filter.FromDate.HasValue && Filter.ToDate.HasValue && Filter.FromDate.Value.Date > Filter.ToDate.Value.Date)
        {
            ModelState.AddModelError(string.Empty, "From Date must be less than or equal to To Date.");
            Filter.ToDate = null;
        }
    }

    // Thực hiện xử lý cho hàm BuildSearchFilter theo nghiệp vụ của màn hình.
    private PurchaseRequisitionFilter BuildSearchFilter(PurchaseRequisitionSearchRequest request)
    {
        return new PurchaseRequisitionFilter
        {
            RequestNo = request.RequestNo,
            StatusId = request.StatusId,
            Description = request.Description,
            UseDateRange = request.UseDateRange,
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            Page = request.Page <= 0 ? 1 : request.Page,
            PageSize = AllowedPageSizes.Contains(request.PageSize) ? request.PageSize : DefaultPageSize
        };
    }

    private object BuildRouteValues() => new
    {
        RequestNo = Filter.RequestNo,
        StatusId = Filter.StatusId,
        Description = Filter.Description,
        UseDateRange = Filter.UseDateRange,
        FromDate = Filter.UseDateRange ? Filter.FromDate?.ToString("yyyy-MM-dd") : null,
        ToDate = Filter.UseDateRange && Filter.FromDate.HasValue ? Filter.ToDate?.ToString("yyyy-MM-dd") : null,
        PageNumber = Filter.Page,
        PageSize = Filter.PageSize
    };

    // Thực hiện xử lý cho hàm NormalizeQueryInputs theo nghiệp vụ của màn hình.
    private void NormalizeQueryInputs()
    {
        Filter.RequestNo = Request.Query[nameof(Filter.RequestNo)].ToString();
        NormalizeNullableIntQuery(nameof(Filter.StatusId), value => Filter.StatusId = value);
        Filter.Description = Request.Query[nameof(Filter.Description)].ToString();
        NormalizeBoolQuery(nameof(Filter.UseDateRange), value => Filter.UseDateRange = value);
        NormalizeIntQuery("PageNumber", value => Filter.Page = value, 1);
        NormalizeIntQuery("PageSize", value => Filter.PageSize = value, DefaultPageSize);
        NormalizeDateQuery(nameof(Filter.FromDate), value => Filter.FromDate = value);
        NormalizeDateQuery(nameof(Filter.ToDate), value => Filter.ToDate = value);
        ClearPaginationModelState();
    }

    // Thực hiện xử lý cho hàm NormalizePostInputs theo nghiệp vụ của màn hình.
    private void NormalizePostInputs()
    {
        Filter.RequestNo = Request.Form[nameof(Filter.RequestNo)].ToString();
        NormalizeNullableIntForm(nameof(Filter.StatusId), value => Filter.StatusId = value);
        Filter.Description = Request.Form[nameof(Filter.Description)].ToString();
        NormalizeBoolForm(nameof(Filter.UseDateRange), value => Filter.UseDateRange = value);
        NormalizeIntForm("PageNumber", value => Filter.Page = value, 1);
        NormalizeIntForm("PageSize", value => Filter.PageSize = value, DefaultPageSize);
        NormalizeDateForm(nameof(Filter.FromDate), value => Filter.FromDate = value);
        NormalizeDateForm(nameof(Filter.ToDate), value => Filter.ToDate = value);
        ClearPaginationModelState();

        if (Request.HasFormContentType && Request.Form.ContainsKey(nameof(SelectedPrId)))
        {
            var raw = Request.Form[nameof(SelectedPrId)].ToString();
            SelectedPrId = int.TryParse(raw, out var parsed) ? parsed : null;
            ModelState.Remove(nameof(SelectedPrId));
        }
    }

    // Thực hiện xử lý cho hàm ClearPaginationModelState theo nghiệp vụ của màn hình.
    private void ClearPaginationModelState()
    {
        ModelState.Remove("Page");
        ModelState.Remove("page");
        ModelState.Remove("Filter.Page");
        ModelState.Remove("Filter.PageSize");
    }

    // Thực hiện xử lý cho hàm NormalizeDateQuery theo nghiệp vụ của màn hình.
    private void NormalizeDateQuery(string key, Action<DateTime?> assign)
    {
        if (!Request.Query.ContainsKey(key)) return;
        ParseDate(Request.Query[key].ToString(), assign, key);
    }

    // Thực hiện xử lý cho hàm NormalizeDateForm theo nghiệp vụ của màn hình.
    private void NormalizeDateForm(string key, Action<DateTime?> assign)
    {
        if (!Request.HasFormContentType || !Request.Form.ContainsKey(key)) return;
        ParseDate(Request.Form[key].ToString(), assign, key);
    }

    // Thực hiện xử lý cho hàm ParseDate theo nghiệp vụ của màn hình.
    private void ParseDate(string raw, Action<DateTime?> assign, string key)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            assign(null);
            ModelState.Remove(key);
            return;
        }

        if (DateTime.TryParseExact(raw, AcceptedDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedExact)
            || DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsedExact)
            || DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedExact))
        {
            assign(parsedExact.Date);
            ModelState.Remove(key);
            return;
        }

        assign(null);
        ModelState.Remove(key);
    }

    // Thực hiện xử lý cho hàm NormalizeBoolQuery theo nghiệp vụ của màn hình.
    private void NormalizeBoolQuery(string key, Action<bool> assign)
    {
        if (!Request.Query.ContainsKey(key)) return;
        assign(ParseBoolValues(Request.Query[key]));
        ModelState.Remove(key);
    }

    // Thực hiện xử lý cho hàm NormalizeBoolForm theo nghiệp vụ của màn hình.
    private void NormalizeBoolForm(string key, Action<bool> assign)
    {
        if (!Request.HasFormContentType || !Request.Form.ContainsKey(key)) return;
        assign(ParseBoolValues(Request.Form[key]));
        ModelState.Remove(key);
    }

    // Thực hiện xử lý cho hàm ParseBoolValues theo nghiệp vụ của màn hình.
    private static bool ParseBoolValues(IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (value == "1" || value.Equals("on", StringComparison.OrdinalIgnoreCase)) return true;
            if (value != "0" && bool.TryParse(value, out var parsed) && parsed) return true;
        }
        return false;
    }

    // Thực hiện xử lý cho hàm NormalizeIntQuery theo nghiệp vụ của màn hình.
    private void NormalizeIntQuery(string key, Action<int> assign, int defaultValue)
    {
        if (!Request.Query.ContainsKey(key)) return;
        assign(int.TryParse(Request.Query[key].ToString(), out var parsed) ? parsed : defaultValue);
        ModelState.Remove(key);
    }

    // Thực hiện xử lý cho hàm NormalizeIntForm theo nghiệp vụ của màn hình.
    private void NormalizeIntForm(string key, Action<int> assign, int defaultValue)
    {
        if (!Request.HasFormContentType || !Request.Form.ContainsKey(key)) return;
        assign(int.TryParse(Request.Form[key].ToString(), out var parsed) ? parsed : defaultValue);
        ModelState.Remove(key);
    }

    // Thực hiện xử lý cho hàm NormalizeNullableIntQuery theo nghiệp vụ của màn hình.
    private void NormalizeNullableIntQuery(string key, Action<int?> assign)
    {
        if (!Request.Query.ContainsKey(key))
        {
            return;
        }

        var raw = Request.Query[key].ToString();
        assign(int.TryParse(raw, out var parsed) ? parsed : null);
        ModelState.Remove(key);
    }

    // Thực hiện xử lý cho hàm NormalizeNullableIntForm theo nghiệp vụ của màn hình.
    private void NormalizeNullableIntForm(string key, Action<int?> assign)
    {
        if (!Request.HasFormContentType || !Request.Form.ContainsKey(key))
        {
            return;
        }

        var raw = Request.Form[key].ToString();
        assign(int.TryParse(raw, out var parsed) ? parsed : null);
        ModelState.Remove(key);
    }

    // Phân tích danh sách dòng MR người dùng đã chọn trong popup Add AT.
    private List<PurchaseRequisitionAddAtSourceRow> ParseAddAtSourceRows()
    {
        if (string.IsNullOrWhiteSpace(AddAtDetailsJson)) return new List<PurchaseRequisitionAddAtSourceRow>();

        try
        {
            var details = JsonSerializer.Deserialize<List<PurchaseRequisitionAddAtSourceRow>>(AddAtDetailsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return details ?? new List<PurchaseRequisitionAddAtSourceRow>();
        }
        catch
        {
            ModelState.AddModelError(string.Empty, "Detail data format is invalid.");
            return new List<PurchaseRequisitionAddAtSourceRow>();
        }
    }

    // Kiểm tra từng dòng MR trước khi tạo PR từ popup Add AT.
    private void ValidateAddAtSourceRow(PurchaseRequisitionAddAtSourceRow detail, int rowNo)
    {
        if (detail.ItemId <= 0)
        {
            ModelState.AddModelError(string.Empty, $"Row {rowNo}: Item is required.");
        }

        if (detail.SugBuy <= 0)
        {
            ModelState.AddModelError(string.Empty, $"Row {rowNo}: SugBuy must be greater than 0.");
        }

        if (detail.Buy < 0 || detail.UnitPrice < 0)
        {
            ModelState.AddModelError(string.Empty, $"Row {rowNo}: BUY and UnitPrice must be valid numbers.");
        }

        if (detail.MrDetailId <= 0)
        {
            ModelState.AddModelError(string.Empty, $"Row {rowNo}: MR detail is invalid.");
        }
    }

    // Kiểm tra loại file và dung lượng file đính kèm theo cấu hình trong appsettings.json.
    private void ValidateAddAtAttachment()
    {
        if (AddAtAttachments == null || AddAtAttachments.Count == 0)
        {
            return;
        }

        var allowedExtensions = (AllowedAttachmentExtensionsText ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.StartsWith('.') ? x.ToLowerInvariant() : $".{x.ToLowerInvariant()}")
            .ToHashSet();

        var maxFileSizeBytes = MaxAttachmentSizeMb * 1024 * 1024L;
        foreach (var attachment in AddAtAttachments.Where(file => file != null && file.Length > 0))
        {
            var fileExtension = Path.GetExtension(attachment.FileName)?.ToLowerInvariant() ?? string.Empty;
            if (allowedExtensions.Count > 0 && !allowedExtensions.Contains(fileExtension))
            {
                ModelState.AddModelError(string.Empty, $"Attachment file type is invalid for '{attachment.FileName}'. Allowed types: {string.Join(", ", allowedExtensions)}.");
            }

            if (maxFileSizeBytes > 0 && attachment.Length > maxFileSizeBytes)
            {
                ModelState.AddModelError(string.Empty, $"Attachment '{attachment.FileName}' size cannot exceed {MaxAttachmentSizeMb} MB.");
            }
        }
    }

    // Thực hiện xử lý cho hàm GetModelStateErrorMessage theo nghiệp vụ của màn hình.
    private string GetModelStateErrorMessage()
    {
        var errors = ModelState.Values
            .SelectMany(x => x.Errors)
            .Select(x => x.ErrorMessage)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        return errors.Count > 0
            ? string.Join(" ", errors)
            : "Cannot save Add AT details. Please review data and try again.";
    }

    // Lấy tập quyền thực tế của người dùng trên chức năng hiện tại.
    private PagePermissions GetUserPermissions()
    {
        bool isAdmin = IsAdminRole();
        int roleId = GetCurrentRoleId();

        // 1. Khởi tạo đối tượng PagePermissions mới
        var permsObj = new PagePermissions();

        if (isAdmin)
        {
            // 2. Admin được cấp danh sách quyền đầy đủ
            permsObj.AllowedNos = Enumerable.Range(1, 20).ToList();
        }
        else
        {
            // 3. User thường lấy danh sách quyền theo RoleID và FunctionID
            permsObj.AllowedNos = _permissionService.GetPermissionsForPage(roleId, FUNCTION_ID);
        }

        // 4. Trả về object chứa tập quyền của người dùng
        return permsObj;
    }

    // Thực hiện xử lý cho hàm LoadPageActions theo nghiệp vụ của màn hình.
    private void LoadPageActions()
    {
        var newStatusPermissions = GetEffectivePermissionsByStatus(1);

        CanAddNew = newStatusPermissions.Contains(PermissionAdd);
        CanAddAt = newStatusPermissions.Contains(PermissionAdd);
        CanEditRequisition = newStatusPermissions.Contains(PermissionEdit);
        CanViewDetailRequisition = newStatusPermissions.Contains(PermissionViewDetail);
    }

    // Thực hiện xử lý cho hàm GetEffectivePermissionsByStatus theo nghiệp vụ của màn hình.
    private List<int> GetEffectivePermissionsByStatus(int status)
    {
        bool isAdmin = IsAdminRole();
        int roleId = GetCurrentRoleId();

        if (isAdmin)
        {
            return Enumerable.Range(1, 20).ToList();
        }

        return _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, status);
    }

    // Thực hiện xử lý cho hàm CanEditRow theo nghiệp vụ của màn hình.
    public bool CanEditRow(byte? statusId)
    {
        if (!statusId.HasValue)
        {
            return false;
        }

        var effectivePermissions = GetEffectivePermissionsByStatus(statusId.Value);
        return statusId.Value switch
        {
            1 => effectivePermissions.Contains(PermissionEdit),
            3 => effectivePermissions.Contains(PermissionChangeStatus) && IsWorkflowActor(),
            _ => false
        };
    }

    // Thực hiện xử lý cho hàm CanViewDetailRow theo nghiệp vụ của màn hình.
    public bool CanViewDetailRow(byte? statusId)
    {
        return statusId.HasValue && GetEffectivePermissionsByStatus(statusId.Value).Contains(PermissionViewDetail);
    }

    private bool HasPermission(int permissionNo) => PagePerm.HasPermission(permissionNo);

    // Nạp vai trò workflow hiện tại để danh sách cũng xác định đúng row nào được mở mode=edit.
    private void LoadCurrentWorkflowUser()
    {
        _workflowUser = new PurchaseRequisitionWorkflowUserInfo();
        var employeeCode = User.Identity?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(employeeCode))
        {
            return;
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
SELECT TOP 1 EmployeeID,
       ISNULL(IsPurchaser, 0) AS IsPurchaser,
       ISNULL(IsCFO, 0) AS IsCFO,
       ISNULL(IsBOD, 0) AS IsBOD
FROM dbo.MS_Employee
WHERE EmployeeCode = @EmployeeCode", conn);

        cmd.Parameters.Add("@EmployeeCode", SqlDbType.VarChar, 50).Value = employeeCode;
        conn.Open();

        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
        {
            return;
        }

        _workflowUser = new PurchaseRequisitionWorkflowUserInfo
        {
            EmployeeId = rd.IsDBNull(rd.GetOrdinal("EmployeeID")) ? 0 : Convert.ToInt32(rd["EmployeeID"]),
            EmployeeCode = employeeCode,
            IsPurchaser = Convert.ToBoolean(rd["IsPurchaser"]),
            IsCFO = Convert.ToBoolean(rd["IsCFO"]),
            IsBOD = Convert.ToBoolean(rd["IsBOD"])
        };
    }

    // Chỉ PU/CFO/BOD/Admin mới được mở Pending ở mode=edit để xử lý CST New.
    private bool IsWorkflowActor()
    {
        return _workflowUser.IsPurchaser || _workflowUser.IsCFO || _workflowUser.IsBOD || IsAdminRole();
    }

    // Thực hiện xử lý cho hàm NeedCollapseDescription theo nghiệp vụ của màn hình.
    public bool NeedCollapseDescription(string? description)
    {
        return !string.IsNullOrWhiteSpace(description) && description.Trim().Length > 80;
    }

    // Thực hiện xử lý cho hàm GetDescriptionPreview theo nghiệp vụ của màn hình.
    public string GetDescriptionPreview(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return string.Empty;
        }

        var source = description.Trim();
        return source.Length <= 80 ? source : $"{source[..80]}...";
    }

    // Thực hiện xử lý cho hàm GetCurrentRoleId theo nghiệp vụ của màn hình.
    private int GetCurrentRoleId()
    {
        return int.Parse(User.FindFirst("RoleID")?.Value ?? "0");
    }

    // Thực hiện xử lý cho hàm IsAdminRole theo nghiệp vụ của màn hình.
    private bool IsAdminRole()
    {
        return User.FindFirst("IsAdminRole")?.Value == "True";
    }
}

public class PurchaseRequisitionSearchRequest
{
    public string? RequestNo { get; set; }
    public int? StatusId { get; set; }
    public string? Description { get; set; }
    public bool UseDateRange { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class PurchaseRequisitionFilter
{
    public string? RequestNo { get; set; }
    public int? StatusId { get; set; }
    public string? Description { get; set; }
    public bool UseDateRange { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class PurchaseRequisitionRow
{
    public int Id { get; set; }
    public string RequestNo { get; set; } = string.Empty;
    public DateTime RequestDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public byte? StatusId { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public string PurchaserCode { get; set; } = string.Empty;
    public string ChiefACode { get; set; } = string.Empty;
    public string GDirectorCode { get; set; } = string.Empty;
}

public class PurchaseRequisitionExportHeader
{
    public int Id { get; set; }
    public string RequestNo { get; set; } = string.Empty;
    public DateTime RequestDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public byte StatusId { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public string PurchaserCode { get; set; } = string.Empty;
    public string ChiefACode { get; set; } = string.Empty;
    public string GDirectorCode { get; set; } = string.Empty;
    public List<PurchaseRequisitionDetailInput> Details { get; set; } = new List<PurchaseRequisitionDetailInput>();
}

public class PurchaseRequisitionItemLookup
{
    public int Id { get; set; }
    public string RequestNo { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Buy { get; set; }
    public decimal UnitPrice { get; set; }
    public string Specification { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public int? MrDetailId { get; set; }
}

public class PurchaseRequisitionSupplierLookup
{
    public int Id { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
}

public class PurchaseRequisitionCurrencyOption
{
    public byte Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class PurchaseRequisitionDetailInput
{
    public long DetailId { get; set; }
    public int ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal QtyFromM { get; set; }
    public decimal QtyPur { get; set; }
    public decimal UnitPrice { get; set; }
    public string? Remark { get; set; }
    public int? SupplierId { get; set; }
    public string SupplierText { get; set; } = string.Empty;
    public string MrRequestNo { get; set; } = string.Empty;
    public int? MrDetailId { get; set; }
}

public class PurchaseRequisitionAddAtSourceRow
{
    public decimal RequestNo { get; set; }
    public int ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Buy { get; set; }
    public decimal SugBuy { get; set; }
    public decimal UnitPrice { get; set; }
    public string Specification { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public int MrDetailId { get; set; }
}







