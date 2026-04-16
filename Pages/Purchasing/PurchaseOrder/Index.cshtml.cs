using System.Data;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;
using SmartSam.Services.Interfaces;

namespace SmartSam.Pages.Purchasing.PurchaseOrder;

public class IndexModel : BasePageModel
{
    private const int FUNCTION_ID = 73;
    private const int PermissionView = 2;
    private const int PermissionAdd = 3;
    private const int PermissionEdit = 4;
    private const int PermissionBackToProcessing = 6;
    private readonly ILogger<IndexModel> _logger;
    private readonly PermissionService _permissionService;
    private readonly ISecurityService _securityService;

    public IndexModel(IConfiguration config, ILogger<IndexModel> logger, PermissionService permissionService, ISecurityService securityService)
        : base(config)
    {
        _logger = logger;
        _permissionService = permissionService;
        _securityService = securityService;
    }

    public PagePermissions PagePerm { get; private set; } = new PagePermissions();
    public int DefaultPageSize => _config.GetValue<int>("AppSettings:DefaultPageSize", 10);
    public IReadOnlyList<int> PageSizeOptions => GetPageSizeOptions();

    [BindProperty(SupportsGet = true)]
    public PurchaseOrderFilter Filter { get; set; } = new PurchaseOrderFilter();

    [BindProperty(SupportsGet = true)]
    public int? SelectedPoId { get; set; }

    public List<SelectListItem> Statuses { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> AssessLevels { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> Departments { get; set; } = new List<SelectListItem>();

    public IActionResult OnGet()
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionView) && !PagePerm.HasPermission(PermissionAdd))
        {
            return Redirect("/");
        }

        Filter.PageSize = NormalizePageSize(Filter.PageSize);
        NormalizeFilter(GetDefaultStatusIdsForCurrentUser());
        LoadStatuses();
        LoadAssessLevels();
        LoadDepartments();
        return Page();
    }

    public IActionResult OnPostSearch([FromBody] PurchaseOrderSearchRequest request)
    {
        try
        {
            PagePerm = GetUserPermissions();
            if (!PagePerm.HasPermission(PermissionView) && !PagePerm.HasPermission(PermissionAdd))
            {
                return new JsonResult(new { success = false, message = "You have no permission to access purchase order." });
            }

            var filter = BuildSearchFilter(request);
            var (rows, total) = SearchPurchaseOrders(filter);
            var roleId = GetCurrentRoleId();
            var userInfo = LoadWorkflowUser();

            var data = rows.Select(row =>
            {
                var effectivePermissions = GetEffectivePermissionsByStatus(roleId, row.StatusId);
                return new
                {
                    data = row,
                    actions = new
                    {
                        canAccess = effectivePermissions.Contains(PermissionView),
                        accessMode = row.StatusId == 1 && effectivePermissions.Contains(PermissionEdit) ? "edit" : "view",
                        canEdit = row.StatusId == 1 && effectivePermissions.Contains(PermissionEdit),
                        canView = effectivePermissions.Contains(PermissionView),
                        canBackToProcessing = effectivePermissions.Contains(PermissionBackToProcessing)
                            && row.StatusId == 2
                            && CanHandleWaitingForApproval(userInfo)
                    }
                };
            });

            return new JsonResult(new
            {
                success = true,
                data,
                total,
                page = filter.Page,
                pageSize = filter.PageSize,
                totalPages = filter.PageSize <= 0 ? 1 : (int)Math.Ceiling(total / (double)filter.PageSize)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while searching purchase orders.");
            return new JsonResult(new { success = false, message = ex.Message });
        }
    }

    public IActionResult OnPostSearchDetail([FromBody] PurchaseOrderViewDetailSearchRequest request)
    {
        try
        {
            PagePerm = GetUserPermissions();
            if (!PagePerm.HasPermission(PermissionView) && !PagePerm.HasPermission(PermissionAdd))
            {
                return new JsonResult(new { success = false, message = "You have no permission to access purchase order." });
            }

            var filter = BuildViewDetailFilter(request);
            var (rows, total) = SearchPurchaseOrderDetails(filter);
            return new JsonResult(new
            {
                success = true,
                data = rows,
                total,
                page = filter.Page,
                pageSize = filter.PageSize,
                totalPages = filter.PageSize <= 0 ? 1 : (int)Math.Ceiling(total / (double)filter.PageSize)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while searching purchase order details.");
            return new JsonResult(new { success = false, message = ex.Message });
        }
    }

    public IActionResult OnGetExportExcel()
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionView))
        {
            return Redirect("/");
        }

        NormalizeFilter(GetDefaultStatusIdsForCurrentUser());
        var exportFilter = new PurchaseOrderSearchRequest
        {
            PONo = Filter.PONo,
            RequestNo = Filter.RequestNo,
            StatusId = Filter.StatusId,
            StatusIds = Filter.StatusIds,
            SupplierKeyword = Filter.SupplierKeyword,
            AssessLevelId = Filter.AssessLevelId,
            Remark = Filter.Remark,
            UseDateRange = Filter.UseDateRange,
            FromDate = Filter.FromDate,
            ToDate = Filter.ToDate,
            Page = 1,
            PageSize = int.MaxValue
        };

        var (rows, _) = SearchPurchaseOrders(exportFilter);
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Purchase Order");
        var headers = new[]
        {
            "No.",
            "Date",
            "Request No.",
            "Supplier",
            "Status",
            "Purchaser",
            "Chief.A",
            "G.Director",
            "Remark"
        };

        for (var i = 0; i < headers.Length; i++)
        {
            worksheet.Cell(1, i + 1).Value = headers[i];
        }

        var rowIndex = 2;
        foreach (var row in rows)
        {
            worksheet.Cell(rowIndex, 1).Value = row.PONo;
            worksheet.Cell(rowIndex, 2).Value = row.PODateDisplay;
            worksheet.Cell(rowIndex, 3).Value = row.RequestNo;
            worksheet.Cell(rowIndex, 4).Value = row.Supplier;
            worksheet.Cell(rowIndex, 5).Value = row.StatusName;
            worksheet.Cell(rowIndex, 6).Value = row.PurchaserCode;
            worksheet.Cell(rowIndex, 7).Value = row.ChiefACode;
            worksheet.Cell(rowIndex, 8).Value = row.GDirectorCode;
            worksheet.Cell(rowIndex, 9).Value = row.Remark;
            rowIndex++;
        }

        var usedRange = worksheet.Range(1, 1, Math.Max(1, rowIndex - 1), headers.Length);
        usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        usedRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        worksheet.Row(1).Style.Font.Bold = true;
        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"purchase_order_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
    }

    public IActionResult OnGetExportDetailExcel([FromQuery] PurchaseOrderViewDetailSearchRequest request)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionView))
        {
            return Redirect("/");
        }

        var filter = BuildViewDetailFilter(request);
        var (rows, _) = SearchPurchaseOrderDetails(filter);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Purchase Order Detail");
        var headers = new[]
        {
            "Item Code",
            "Item Name",
            "Quantity",
            "U.Price",
            "PO Amount",
            "For Department",
            "Note",
            "Rec Qty",
            "Rec Amount",
            "Rec Date"
        };

        for (var i = 0; i < headers.Length; i++)
        {
            worksheet.Cell(1, i + 1).Value = headers[i];
        }

        var rowIndex = 2;
        foreach (var row in rows)
        {
            worksheet.Cell(rowIndex, 1).Value = row.ItemCode;
            worksheet.Cell(rowIndex, 2).Value = row.ItemName;
            worksheet.Cell(rowIndex, 3).Value = row.Quantity;
            worksheet.Cell(rowIndex, 4).Value = row.UnitPrice;
            worksheet.Cell(rowIndex, 5).Value = row.POAmount;
            worksheet.Cell(rowIndex, 6).Value = row.ForDepartment;
            worksheet.Cell(rowIndex, 7).Value = row.Note;
            worksheet.Cell(rowIndex, 8).Value = row.RecQty;
            worksheet.Cell(rowIndex, 9).Value = row.RecAmount;
            worksheet.Cell(rowIndex, 10).Value = row.RecDateDisplay;
            rowIndex++;
        }

        var usedRange = worksheet.Range(1, 1, Math.Max(1, rowIndex - 1), headers.Length);
        usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        usedRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        worksheet.Row(1).Style.Font.Bold = true;
        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"purchase_order_detail_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
    }

    public IActionResult OnGetSupplierLookup(string? term)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionView) && !PagePerm.HasPermission(PermissionAdd))
        {
            return new JsonResult(Array.Empty<object>());
        }

        return new JsonResult(LoadSupplierLookup(term));
    }

    public IActionResult OnGetEstimateView(int poId)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionView))
        {
            return new JsonResult(new { success = false, message = "You have no permission to view purchase order estimate." });
        }

        if (poId <= 0)
        {
            return new JsonResult(new { success = false, message = "Purchase order is not selected." });
        }

        try
        {
            var rows = LoadPurchaseOrderEstimates(poId);
            return new JsonResult(new { success = true, data = rows });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error while loading purchase order estimate view.");
            return new JsonResult(new { success = false, message = ex.Message });
        }
    }

    public IActionResult OnGetReportDetailQuestPdf([FromQuery] PurchaseOrderViewDetailSearchRequest request)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionView) && !PagePerm.HasPermission(PermissionAdd))
        {
            return Redirect("/");
        }

        var filter = BuildViewDetailFilter(request);
        var rows = SearchPurchaseOrderDetailsForReport(filter);
        var pdf = PurchaseOrderViewDetailQuestPdfReport.BuildPdf(rows);
        return File(pdf, "application/pdf", "PurchaseOrder_Report.pdf");
    }

    private void NormalizeFilter()
    {
        NormalizeFilter(GetDefaultStatusIdsForCurrentUser());
    }

    private List<object> LoadSupplierLookup(string? term)
    {
        var rows = new List<object>();
        var searchTerm = term?.Trim();
        if (string.IsNullOrWhiteSpace(searchTerm) || searchTerm.Length < 3)
        {
            return rows;
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
        SELECT TOP 20
            SupplierID,
            ISNULL(SupplierCode, '') AS SupplierCode,
            ISNULL(SupplierName, '') AS SupplierName
        FROM dbo.PC_Suppliers
        WHERE Status >= 0
          AND Status < 5
          AND (
                SupplierCode LIKE '%' + @term + '%'
                OR SupplierName LIKE '%' + @term + '%'
          )
        ORDER BY SupplierCode", conn);
        cmd.Parameters.Add("@term", SqlDbType.NVarChar, 100).Value = searchTerm;
        conn.Open();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var code = Convert.ToString(reader["SupplierCode"]) ?? string.Empty;
            var name = Convert.ToString(reader["SupplierName"]) ?? string.Empty;
            rows.Add(new
            {
                id = Convert.ToString(reader["SupplierID"]) ?? string.Empty,
                text = string.IsNullOrWhiteSpace(name) ? code : $"{code} ({name})"
            });
        }

        return rows.Cast<object>().ToList();
    }

    private List<object> LoadPurchaseOrderEstimates(int poId)
    {
        var rows = new List<object>();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
        SELECT
            d.PO_IndexDetailID,
            ISNULL(i.PO_IndexDetailName, '') AS PO_IndexDetailName,
            ISNULL(d.Point, 0) AS Point,
            d.TheDate,
            ISNULL(e.EmployeeName, '') AS EmployeeName
        FROM dbo.PO_Estimate d
        LEFT JOIN dbo.PO_IndexDetail i ON i.PO_IndexDetailID = d.PO_IndexDetailID
        LEFT JOIN dbo.MS_Employee e ON e.EmployeeCode = d.UserCode
        LEFT JOIN dbo.PC_PO po ON po.POID = d.POID
        WHERE d.POID = @POID
        ORDER BY d.TheDate DESC, d.PO_IndexDetailID", conn);
        cmd.Parameters.Add("@POID", SqlDbType.Int).Value = poId;
        conn.Open();

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new
            {
                poIndexDetailId = reader.IsDBNull(reader.GetOrdinal("PO_IndexDetailID")) ? 0 : Convert.ToInt32(reader["PO_IndexDetailID"]),
                poIndexDetailName = Convert.ToString(reader["PO_IndexDetailName"]) ?? string.Empty,
                point = reader.IsDBNull(reader.GetOrdinal("Point")) ? 0 : Convert.ToInt32(reader["Point"]),
                theDate = reader.IsDBNull(reader.GetOrdinal("TheDate")) ? string.Empty : Convert.ToDateTime(reader["TheDate"]).ToString("dd/MM/yyyy HH:mm"),
                employeeName = Convert.ToString(reader["EmployeeName"]) ?? string.Empty
            });
        }

        return rows;
    }

    private void NormalizeFilter(IReadOnlyCollection<int> defaultStatusIds)
    {
        Filter.Page = Filter.Page <= 0 ? 1 : Filter.Page;
        Filter.PageSize = NormalizePageSize(Filter.PageSize);

        Filter.StatusIds ??= new List<int>();
        if (Filter.StatusIds.Count == 0 && Filter.StatusId.HasValue)
        {
            Filter.StatusIds = new List<int> { Filter.StatusId.Value };
        }

        if (Filter.StatusIds.Count == 0)
        {
            Filter.StatusIds = defaultStatusIds.Where(id => id > 0).Distinct().ToList();
        }
        else
        {
            Filter.StatusIds = Filter.StatusIds.Where(id => id > 0).Distinct().ToList();
        }

        if (Filter.StatusIds.Count > 0)
        {
            Filter.StatusId = Filter.StatusIds[0];
        }
        else
        {
            Filter.StatusId = null;
        }

        if (!Filter.UseDateRange)
        {
            Filter.FromDate = null;
            Filter.ToDate = null;
            return;
        }

        Filter.FromDate ??= DateTime.Today.AddDays(-30);
        Filter.ToDate ??= DateTime.Today;
        if (Filter.FromDate > Filter.ToDate)
        {
            Filter.ToDate = Filter.FromDate;
        }
    }

    private IReadOnlyList<int> GetDefaultStatusIdsForCurrentUser()
    {
        var workflowUser = LoadWorkflowUser();
        return workflowUser.IsPurchaser ? new[] { 1, 2 } : new[] { 2 };
    }

    private PurchaseOrderSearchRequest BuildSearchFilter(PurchaseOrderSearchRequest request)
    {
        return new PurchaseOrderSearchRequest
        {
            PONo = request.PONo?.Trim(),
            RequestNo = request.RequestNo?.Trim(),
            StatusId = request.StatusId,
            StatusIds = request.StatusIds?.Distinct().ToList(),
            SupplierKeyword = request.SupplierKeyword?.Trim(),
            AssessLevelId = request.AssessLevelId,
            Remark = request.Remark?.Trim(),
            UseDateRange = request.UseDateRange,
            FromDate = request.UseDateRange ? request.FromDate : null,
            ToDate = request.UseDateRange ? request.ToDate : null,
            Page = request.Page <= 0 ? 1 : request.Page,
            PageSize = NormalizePageSize(request.PageSize)
        };
    }

    private (List<PurchaseOrderRow> Rows, int Total) SearchPurchaseOrders(PurchaseOrderSearchRequest filter)
    {
        var rows = new List<PurchaseOrderRow>();
        var whereParts = new List<string>();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = conn.CreateCommand();

        if (!string.IsNullOrWhiteSpace(filter.PONo))
        {
            whereParts.Add("p.PONo LIKE @PONo");
            cmd.Parameters.Add("@PONo", SqlDbType.NVarChar, 50).Value = $"%{filter.PONo}%";
        }

        if (!string.IsNullOrWhiteSpace(filter.RequestNo))
        {
            whereParts.Add("pr.RequestNo LIKE @RequestNo");
            cmd.Parameters.Add("@RequestNo", SqlDbType.NVarChar, 50).Value = $"%{filter.RequestNo}%";
        }

        if (!string.IsNullOrWhiteSpace(filter.Remark))
        {
            whereParts.Add("p.Remark LIKE @Remark");
            cmd.Parameters.Add("@Remark", SqlDbType.NVarChar, 300).Value = $"%{filter.Remark}%";
        }

        if (!string.IsNullOrWhiteSpace(filter.SupplierKeyword))
        {
            whereParts.Add("(s.SupplierCode LIKE @SupplierKeyword OR s.SupplierName LIKE @SupplierKeyword)");
            cmd.Parameters.Add("@SupplierKeyword", SqlDbType.NVarChar, 150).Value = $"%{filter.SupplierKeyword}%";
        }

        var effectiveStatusIds = GetEffectiveStatusIds(filter);
        if (effectiveStatusIds.Count > 0)
        {
            var statusParams = new List<string>();
            for (var i = 0; i < effectiveStatusIds.Count; i++)
            {
                var parameterName = $"@StatusId{i}";
                statusParams.Add(parameterName);
                cmd.Parameters.Add(parameterName, SqlDbType.Int).Value = effectiveStatusIds[i];
            }

            whereParts.Add($"p.StatusID IN ({string.Join(", ", statusParams)})");
        }

        if (filter.AssessLevelId.HasValue)
        {
            whereParts.Add("p.AssessLevel = @AssessLevelId");
            cmd.Parameters.Add("@AssessLevelId", SqlDbType.Int).Value = filter.AssessLevelId.Value;
        }

        if (filter.UseDateRange && filter.FromDate.HasValue)
        {
            whereParts.Add("CAST(p.PODate AS date) >= @FromDate");
            cmd.Parameters.Add("@FromDate", SqlDbType.Date).Value = filter.FromDate.Value.Date;
        }

        if (filter.UseDateRange && filter.ToDate.HasValue)
        {
            whereParts.Add("CAST(p.PODate AS date) <= @ToDate");
            cmd.Parameters.Add("@ToDate", SqlDbType.Date).Value = filter.ToDate.Value.Date;
        }

        var whereSql = whereParts.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", whereParts)}";

        cmd.CommandText = $@"
        SELECT COUNT(1)
        FROM dbo.PC_PO p
        LEFT JOIN dbo.PC_PR pr ON pr.PRID = p.PRID
        LEFT JOIN dbo.PC_Suppliers s ON s.SupplierID = p.SupplierID
        {whereSql};

        SELECT
            p.POID,
            ISNULL(p.PONo, '') AS PONo,
            p.PODate,
            ISNULL(pr.RequestNo, '') AS RequestNo,
            ISNULL(s.SupplierCode, '') + CASE WHEN ISNULL(s.SupplierName, '') = '' THEN '' ELSE ' / ' + ISNULL(s.SupplierName, '') END AS Supplier,
            ISNULL(st.POStatusName, '') AS StatusName,
            ISNULL(p.StatusID, 0) AS StatusId,
            ISNULL(ep.EmployeeCode, '') AS PurchaserCode,
            ISNULL(ec.EmployeeCode, '') AS ChiefACode,
            ISNULL(eg.EmployeeCode, '') AS GDirectorCode,
            ISNULL(p.Remark, '') AS Remark,
            ISNULL(p.PerVAT, 0) AS PerVAT,
            ISNULL(p.VAT, 0) AS VAT,
            ISNULL(p.AfterVAT, 0) AS AfterVAT
        FROM dbo.PC_PO p
        LEFT JOIN dbo.PC_PR pr ON pr.PRID = p.PRID
        LEFT JOIN dbo.PC_POStatus st ON st.POStatusID = p.StatusID
        LEFT JOIN dbo.PC_Suppliers s ON s.SupplierID = p.SupplierID
        LEFT JOIN dbo.MS_Employee ep ON ep.EmployeeID = p.PurId
        LEFT JOIN dbo.MS_Employee ec ON ec.EmployeeID = p.CAId
        LEFT JOIN dbo.MS_Employee eg ON eg.EmployeeID = p.GDId
        {whereSql}
        ORDER BY p.POID DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

        cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = (filter.Page - 1) * filter.PageSize;
        cmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = filter.PageSize;

        conn.Open();
        using var reader = cmd.ExecuteReader();
        var total = 0;
        if (reader.Read() && !reader.IsDBNull(0))
        {
            total = reader.GetInt32(0);
        }

        if (reader.NextResult())
        {
            while (reader.Read())
            {
                rows.Add(new PurchaseOrderRow
                {
                    Id = Convert.ToInt32(reader["POID"]),
                    PONo = Convert.ToString(reader["PONo"]) ?? string.Empty,
                    PODate = reader.IsDBNull(reader.GetOrdinal("PODate")) ? null : Convert.ToDateTime(reader["PODate"]),
                    RequestNo = Convert.ToString(reader["RequestNo"]) ?? string.Empty,
                    Supplier = Convert.ToString(reader["Supplier"]) ?? string.Empty,
                    StatusName = Convert.ToString(reader["StatusName"]) ?? string.Empty,
                    StatusId = Convert.ToInt32(reader["StatusId"]),
                    PurchaserCode = Convert.ToString(reader["PurchaserCode"]) ?? string.Empty,
                    ChiefACode = Convert.ToString(reader["ChiefACode"]) ?? string.Empty,
                    GDirectorCode = Convert.ToString(reader["GDirectorCode"]) ?? string.Empty,
                    Remark = Convert.ToString(reader["Remark"]) ?? string.Empty,
                    PerVAT = reader.IsDBNull(reader.GetOrdinal("PerVAT")) ? 0 : Convert.ToDecimal(reader["PerVAT"]),
                    VAT = reader.IsDBNull(reader.GetOrdinal("VAT")) ? 0 : Convert.ToDecimal(reader["VAT"]),
                    AfterVAT = reader.IsDBNull(reader.GetOrdinal("AfterVAT")) ? 0 : Convert.ToDecimal(reader["AfterVAT"])
                });
            }
        }

        return (rows, total);
    }

    private static List<int> GetEffectiveStatusIds(PurchaseOrderSearchRequest filter)
    {
        var statusIds = new List<int>();

        if (filter.StatusIds is { Count: > 0 })
        {
            statusIds.AddRange(filter.StatusIds);
        }
        else if (filter.StatusId.HasValue)
        {
            statusIds.Add(filter.StatusId.Value);
        }

        return statusIds.Where(id => id > 0).Distinct().ToList();
    }

    private PurchaseOrderViewDetailSearchRequest BuildViewDetailFilter(PurchaseOrderViewDetailSearchRequest? request)
    {
        var filter = request ?? new PurchaseOrderViewDetailSearchRequest();
        return new PurchaseOrderViewDetailSearchRequest
        {
            ItemCode = filter.ItemCode?.Trim(),
            RecQtyOperator = NormalizeComparisonOperator(filter.RecQtyOperator),
            RecQtyValue = filter.RecQtyValue,
            Renovation = filter.Renovation,
            General = filter.General,
            ForDeptId = filter.ForDeptId,
            ItemNotInclude = filter.ItemNotInclude?.Trim(),
            SupplierName = filter.SupplierName?.Trim(),
            UsePoDateRange = filter.UsePoDateRange,
            PoFromDate = filter.UsePoDateRange ? filter.PoFromDate : null,
            PoToDate = filter.UsePoDateRange ? filter.PoToDate : null,
            UseRecDateRange = filter.UseRecDateRange,
            RecFromDate = filter.UseRecDateRange ? filter.RecFromDate : null,
            RecToDate = filter.UseRecDateRange ? filter.RecToDate : null,
            Page = filter.Page <= 0 ? 1 : filter.Page,
            PageSize = NormalizePageSize(filter.PageSize)
        };
    }

    private IReadOnlyList<int> GetPageSizeOptions()
    {
        var configured = _config.GetSection("AppSettings:PageSizeOptions").Get<int[]>() ?? Array.Empty<int>();
        var options = configured.Where(value => value > 0).Distinct().ToList();
        if (options.Count == 0)
        {
            options = new List<int> { DefaultPageSize, 20, 25, 30, 35 }
                .Distinct()
                .ToList();
        }

        if (!options.Contains(DefaultPageSize))
        {
            options.Insert(0, DefaultPageSize);
        }

        return options;
    }

    private int NormalizePageSize(int pageSize)
    {
        if (pageSize <= 0)
        {
            return DefaultPageSize;
        }

        return PageSizeOptions.Contains(pageSize) ? pageSize : DefaultPageSize;
    }

    private (List<PurchaseOrderViewDetailRow> Rows, int Total) SearchPurchaseOrderDetails(PurchaseOrderViewDetailSearchRequest filter)
    {
        var rows = new List<PurchaseOrderViewDetailRow>();
        var whereParts = new List<string>();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = conn.CreateCommand();

        ApplyViewDetailFilters(filter, whereParts, cmd);

        var whereSql = whereParts.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", whereParts)}";

        cmd.CommandText = $@"
        SELECT COUNT(1)
        FROM dbo.PC_PO p
        INNER JOIN dbo.PC_PODetail d ON d.POID = p.POID
        LEFT JOIN dbo.PC_PR pr ON pr.PRID = p.PRID
        LEFT JOIN dbo.PC_Suppliers s ON s.SupplierID = p.SupplierID
        LEFT JOIN dbo.MS_CurrencyFL c ON c.CurrencyID = p.Currency
        LEFT JOIN dbo.INV_ItemList i ON i.ItemID = d.ItemID
        LEFT JOIN dbo.MS_Department dep ON dep.DeptID = d.RecDept
        {whereSql};

        SELECT
            p.POID,
            ISNULL(p.PONo, '') AS PONo,
            p.PODate,
            ISNULL(pr.RequestNo, '') AS RequestNo,
            ISNULL(s.SupplierCode, '') + CASE WHEN ISNULL(s.SupplierName, '') = '' THEN '' ELSE ' / ' + ISNULL(s.SupplierName, '') END AS Supplier,
            ISNULL(p.Currency, 0) AS CurrencyId,
            ISNULL(c.CurrencyName, '') AS CurrencyName,
            ISNULL(p.ExRate, 0) AS ExRate,
            ISNULL(i.ItemCode, '') AS ItemCode,
            ISNULL(i.ItemName, '') AS ItemName,
            ISNULL(i.Unit, '') AS Unit,
            ISNULL(d.Quantity, 0) AS Quantity,
            ISNULL(d.UnitPrice, 0) AS UnitPrice,
            ISNULL(d.POAmount, 0) AS POAmount,
            ISNULL(dep.DeptName, '') AS ForDepartment,
            ISNULL(d.Note, '') AS Note,
            ISNULL(d.RecQty, 0) AS RecQty,
            ISNULL(d.RecAmount, 0) AS RecAmount,
            d.RecDate
        FROM dbo.PC_PO p
        INNER JOIN dbo.PC_PODetail d ON d.POID = p.POID
        LEFT JOIN dbo.PC_PR pr ON pr.PRID = p.PRID
        LEFT JOIN dbo.PC_Suppliers s ON s.SupplierID = p.SupplierID
        LEFT JOIN dbo.MS_CurrencyFL c ON c.CurrencyID = p.Currency
        LEFT JOIN dbo.INV_ItemList i ON i.ItemID = d.ItemID
        LEFT JOIN dbo.MS_Department dep ON dep.DeptID = d.RecDept
        {whereSql}
        ORDER BY p.PODate DESC, p.POID DESC, d.ItemID, d.RecDate
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

        cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = (filter.Page - 1) * filter.PageSize;
        cmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = filter.PageSize;

        conn.Open();
        using var reader = cmd.ExecuteReader();
        var total = 0;
        if (reader.Read() && !reader.IsDBNull(0))
        {
            total = reader.GetInt32(0);
        }

        if (reader.NextResult())
        {
            while (reader.Read())
            {
                rows.Add(MapViewDetailRow(reader));
            }
        }

        return (rows, total);
    }

    private List<PurchaseOrderViewDetailRow> SearchPurchaseOrderDetailsForReport(PurchaseOrderViewDetailSearchRequest filter)
    {
        var rows = new List<PurchaseOrderViewDetailRow>();
        var whereParts = new List<string>();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = conn.CreateCommand();

        ApplyViewDetailFilters(filter, whereParts, cmd);

        var whereSql = whereParts.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", whereParts)}";

        cmd.CommandText = $@"
        SELECT
            p.POID,
            ISNULL(p.PONo, '') AS PONo,
            p.PODate,
            ISNULL(pr.RequestNo, '') AS RequestNo,
            ISNULL(s.SupplierCode, '') + CASE WHEN ISNULL(s.SupplierName, '') = '' THEN '' ELSE ' / ' + ISNULL(s.SupplierName, '') END AS Supplier,
            ISNULL(p.Currency, 0) AS CurrencyId,
            ISNULL(c.CurrencyName, '') AS CurrencyName,
            ISNULL(p.ExRate, 0) AS ExRate,
            ISNULL(i.ItemCode, '') AS ItemCode,
            ISNULL(i.ItemName, '') AS ItemName,
            ISNULL(i.Unit, '') AS Unit,
            ISNULL(d.Quantity, 0) AS Quantity,
            ISNULL(d.UnitPrice, 0) AS UnitPrice,
            ISNULL(d.POAmount, 0) AS POAmount,
            ISNULL(dep.DeptName, '') AS ForDepartment,
            ISNULL(d.Note, '') AS Note,
            ISNULL(d.RecQty, 0) AS RecQty,
            ISNULL(d.RecAmount, 0) AS RecAmount,
            d.RecDate
        FROM dbo.PC_PO p
        INNER JOIN dbo.PC_PODetail d ON d.POID = p.POID
        LEFT JOIN dbo.PC_PR pr ON pr.PRID = p.PRID
        LEFT JOIN dbo.PC_Suppliers s ON s.SupplierID = p.SupplierID
        LEFT JOIN dbo.MS_CurrencyFL c ON c.CurrencyID = p.Currency
        LEFT JOIN dbo.INV_ItemList i ON i.ItemID = d.ItemID
        LEFT JOIN dbo.MS_Department dep ON dep.DeptID = d.RecDept
        {whereSql}
        ORDER BY p.PODate DESC, p.POID DESC, d.ItemID, d.RecDate;";

        conn.Open();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(MapViewDetailRow(reader));
        }

        return rows;
    }

    private static PurchaseOrderViewDetailRow MapViewDetailRow(SqlDataReader reader)
    {
        return new PurchaseOrderViewDetailRow
        {
            POID = Convert.ToInt32(reader["POID"]),
            PONo = Convert.ToString(reader["PONo"]) ?? string.Empty,
            PODate = reader.IsDBNull(reader.GetOrdinal("PODate")) ? null : Convert.ToDateTime(reader["PODate"]),
            RequestNo = Convert.ToString(reader["RequestNo"]) ?? string.Empty,
            Supplier = Convert.ToString(reader["Supplier"]) ?? string.Empty,
            CurrencyId = reader.IsDBNull(reader.GetOrdinal("CurrencyId")) ? 0 : Convert.ToInt32(reader["CurrencyId"]),
            CurrencyName = Convert.ToString(reader["CurrencyName"]) ?? string.Empty,
            ExRate = reader.IsDBNull(reader.GetOrdinal("ExRate")) ? 0 : Convert.ToDecimal(reader["ExRate"]),
            ItemCode = Convert.ToString(reader["ItemCode"]) ?? string.Empty,
            ItemName = Convert.ToString(reader["ItemName"]) ?? string.Empty,
            Unit = Convert.ToString(reader["Unit"]) ?? string.Empty,
            Quantity = reader.IsDBNull(reader.GetOrdinal("Quantity")) ? 0 : Convert.ToDecimal(reader["Quantity"]),
            UnitPrice = reader.IsDBNull(reader.GetOrdinal("UnitPrice")) ? 0 : Convert.ToDecimal(reader["UnitPrice"]),
            POAmount = reader.IsDBNull(reader.GetOrdinal("POAmount")) ? 0 : Convert.ToDecimal(reader["POAmount"]),
            ForDepartment = Convert.ToString(reader["ForDepartment"]) ?? string.Empty,
            Note = Convert.ToString(reader["Note"]) ?? string.Empty,
            RecQty = reader.IsDBNull(reader.GetOrdinal("RecQty")) ? 0 : Convert.ToDecimal(reader["RecQty"]),
            RecAmount = reader.IsDBNull(reader.GetOrdinal("RecAmount")) ? 0 : Convert.ToDecimal(reader["RecAmount"]),
            RecDate = reader.IsDBNull(reader.GetOrdinal("RecDate")) ? null : Convert.ToDateTime(reader["RecDate"])
        };
    }

    private void ApplyViewDetailFilters(PurchaseOrderViewDetailSearchRequest filter, List<string> whereParts, SqlCommand cmd)
    {
        if (!string.IsNullOrWhiteSpace(filter.ItemCode))
        {
            whereParts.Add("(ISNULL(i.ItemCode, '') LIKE @ItemCode OR ISNULL(i.ItemName, '') LIKE @ItemCode)");
            cmd.Parameters.Add("@ItemCode", SqlDbType.NVarChar, 100).Value = $"%{filter.ItemCode}%";
        }

        if (!string.IsNullOrWhiteSpace(filter.ItemNotInclude))
        {
            whereParts.Add("(ISNULL(i.ItemCode, '') NOT LIKE @ItemNotInclude AND ISNULL(i.ItemName, '') NOT LIKE @ItemNotInclude)");
            cmd.Parameters.Add("@ItemNotInclude", SqlDbType.NVarChar, 100).Value = $"%{filter.ItemNotInclude}%";
        }

        if (!string.IsNullOrWhiteSpace(filter.SupplierName))
        {
            whereParts.Add("(ISNULL(s.SupplierCode, '') LIKE @SupplierName OR ISNULL(s.SupplierName, '') LIKE @SupplierName)");
            cmd.Parameters.Add("@SupplierName", SqlDbType.NVarChar, 150).Value = $"%{filter.SupplierName}%";
        }

        if (filter.ForDeptId.HasValue)
        {
            whereParts.Add("ISNULL(d.RecDept, 0) = @ForDeptId");
            cmd.Parameters.Add("@ForDeptId", SqlDbType.Int).Value = filter.ForDeptId.Value;
        }

        if (filter.Renovation)
        {
            whereParts.Add("ISNULL(d.Renovation, 0) = 1");
        }

        if (filter.General)
        {
            whereParts.Add("ISNULL(d.General, 0) = 1");
        }

        if (filter.RecQtyValue.HasValue)
        {
            var recQtyOperator = NormalizeComparisonOperator(filter.RecQtyOperator);
            whereParts.Add($"ISNULL(d.RecQty, 0) {recQtyOperator} @RecQtyValue");
            var recQtyParam = cmd.Parameters.Add("@RecQtyValue", SqlDbType.Decimal);
            recQtyParam.Precision = 18;
            recQtyParam.Scale = 2;
            recQtyParam.Value = filter.RecQtyValue.Value;
        }

        if (filter.UsePoDateRange && filter.PoFromDate.HasValue)
        {
            whereParts.Add("CAST(p.PODate AS date) >= @FromDate");
            cmd.Parameters.Add("@FromDate", SqlDbType.Date).Value = filter.PoFromDate.Value.Date;
        }

        if (filter.UsePoDateRange && filter.PoToDate.HasValue)
        {
            whereParts.Add("CAST(p.PODate AS date) <= @ToDate");
            cmd.Parameters.Add("@ToDate", SqlDbType.Date).Value = filter.PoToDate.Value.Date;
        }

        if (filter.UseRecDateRange && filter.RecFromDate.HasValue)
        {
            whereParts.Add("CAST(d.RecDate AS date) >= @RecFromDate");
            cmd.Parameters.Add("@RecFromDate", SqlDbType.Date).Value = filter.RecFromDate.Value.Date;
        }

        if (filter.UseRecDateRange && filter.RecToDate.HasValue)
        {
            whereParts.Add("CAST(d.RecDate AS date) <= @RecToDate");
            cmd.Parameters.Add("@RecToDate", SqlDbType.Date).Value = filter.RecToDate.Value.Date;
        }
    }

    private string NormalizeComparisonOperator(string? comparisonOperator)
    {
        return comparisonOperator switch
        {
            ">" => ">",
            ">=" => ">=",
            "<" => "<",
            "<=" => "<=",
            _ => "="
        };
    }

    private void LoadStatuses()
    {
        Statuses = LoadListFromSql("SELECT POStatusID, POStatusName FROM dbo.PC_POStatus ORDER BY POStatusID", "POStatusID", "POStatusName", true);
    }

    private void LoadAssessLevels()
    {
        AssessLevels = LoadListFromSql("SELECT AssessLevelID, AssessLevelName FROM dbo.PC_AssessLevel ORDER BY AssessLevelID", "AssessLevelID", "AssessLevelName", true);
    }

    private void LoadDepartments()
    {
        Departments = LoadListFromSql("SELECT DeptID, DeptCode + ' / ' + DeptName AS DeptText FROM dbo.MS_Department ORDER BY DeptCode", "DeptID", "DeptText", true);
        if (Departments.Count > 0 && string.IsNullOrWhiteSpace(Departments[0].Value))
        {
            Departments[0].Text = "(All)";
        }
    }

    private PagePermissions GetUserPermissions()
    {
        var permsObj = new PagePermissions();
        if (IsAdminRole())
        {
            permsObj.AllowedNos = Enumerable.Range(1, 20).ToList();
        }
        else
        {
            permsObj.AllowedNos = _permissionService.GetPermissionsForPage(GetCurrentRoleId(), FUNCTION_ID);
        }

        return permsObj;
    }

    private List<int> GetEffectivePermissionsByStatus(int roleId, int status)
    {
        if (IsAdminRole())
        {
            return Enumerable.Range(1, 20).ToList();
        }

        return _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, status);
    }

    private PurchaseOrderWorkflowUser LoadWorkflowUser()
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
        SELECT TOP 1
            EmployeeID,
            ISNULL(EmployeeCode, '') AS EmployeeCode,
            ISNULL(IsPurchaser, 0) AS IsPurchaser,
            ISNULL(IsCFO, 0) AS IsCFO,
            ISNULL(IsBOD, 0) AS IsBOD
        FROM dbo.MS_Employee
        WHERE EmployeeID = @EmployeeID", conn);

        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = GetCurrentEmployeeId();
        conn.Open();
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return new PurchaseOrderWorkflowUser();
        }

        return new PurchaseOrderWorkflowUser
        {
            EmployeeId = Convert.ToInt32(reader["EmployeeID"]),
            EmployeeCode = Convert.ToString(reader["EmployeeCode"]) ?? string.Empty,
            IsPurchaser = Convert.ToBoolean(reader["IsPurchaser"]),
            IsCFO = Convert.ToBoolean(reader["IsCFO"]),
            IsBOD = Convert.ToBoolean(reader["IsBOD"])
        };
    }

    private bool CanHandleWaitingForApproval(PurchaseOrderWorkflowUser user)
    {
        return user.IsPurchaser || user.IsCFO || user.IsBOD || IsAdminRole();
    }

    private int GetCurrentRoleId()
    {
        return int.Parse(User.FindFirst("RoleID")?.Value ?? "0");
    }

    private int GetCurrentEmployeeId()
    {
        return int.Parse(User.FindFirst("EmployeeID")?.Value ?? "0");
    }

    private bool IsAdminRole()
    {
        return User.FindFirst("IsAdminRole")?.Value == "True";
    }
}

public class PurchaseOrderFilter
{
    public string? PONo { get; set; }
    public string? RequestNo { get; set; }
    public int? StatusId { get; set; }
    public List<int> StatusIds { get; set; } = new List<int>();
    public string? SupplierKeyword { get; set; }
    public int? AssessLevelId { get; set; }
    public string? Remark { get; set; }
    public bool UseDateRange { get; set; } = true;
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; }
}

public class PurchaseOrderSearchRequest
{
    public string? PONo { get; set; }
    public string? RequestNo { get; set; }
    public int? StatusId { get; set; }
    public List<int>? StatusIds { get; set; }
    public string? SupplierKeyword { get; set; }
    public int? AssessLevelId { get; set; }
    public string? Remark { get; set; }
    public bool UseDateRange { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; }
}

public class PurchaseOrderViewDetailSearchRequest
{
    public string? ItemCode { get; set; }
    public string? RecQtyOperator { get; set; } = "=";
    public decimal? RecQtyValue { get; set; }
    public bool Renovation { get; set; }
    public bool General { get; set; }
    public int? ForDeptId { get; set; }
    public string? ItemNotInclude { get; set; }
    public string? SupplierName { get; set; }
    public bool UsePoDateRange { get; set; }
    public DateTime? PoFromDate { get; set; }
    public DateTime? PoToDate { get; set; }
    public bool UseRecDateRange { get; set; }
    public DateTime? RecFromDate { get; set; }
    public DateTime? RecToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; }
}

public class PurchaseOrderRow
{
    public int Id { get; set; }
    public string PONo { get; set; } = string.Empty;
    public DateTime? PODate { get; set; }
    public string RequestNo { get; set; } = string.Empty;
    public string Supplier { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
    public int StatusId { get; set; }
    public string PurchaserCode { get; set; } = string.Empty;
    public string ChiefACode { get; set; } = string.Empty;
    public string GDirectorCode { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public decimal PerVAT { get; set; }
    public decimal VAT { get; set; }
    public decimal AfterVAT { get; set; }
    public string PODateDisplay => PODate?.ToString("dd/MM/yyyy") ?? string.Empty;
}

public class PurchaseOrderViewDetailRow
{
    public int POID { get; set; }
    public string PONo { get; set; } = string.Empty;
    public DateTime? PODate { get; set; }
    public string RequestNo { get; set; } = string.Empty;
    public string Supplier { get; set; } = string.Empty;
    public int CurrencyId { get; set; }
    public string CurrencyName { get; set; } = string.Empty;
    public decimal ExRate { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal POAmount { get; set; }
    public string ForDepartment { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public decimal RecQty { get; set; }
    public decimal RecAmount { get; set; }
    public DateTime? RecDate { get; set; }
    public string PODateDisplay => PODate?.ToString("dd/MM/yyyy") ?? string.Empty;
    public string RecDateDisplay => RecDate?.ToString("dd/MM/yyyy") ?? string.Empty;
}

public class PurchaseOrderWorkflowUser
{
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public bool IsPurchaser { get; set; }
    public bool IsCFO { get; set; }
    public bool IsBOD { get; set; }
}
