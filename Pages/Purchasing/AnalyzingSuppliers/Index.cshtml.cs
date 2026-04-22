using System.Data;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;

namespace SmartSam.Pages.Purchasing.AnalyzingSuppliers;

public class IndexModel : BasePageModel
{
    private const string ExcelVniFontName = "VNI-WIN";

    private const int FUNCTION_ID = 75;
    private const int PermissionViewList = 1;
    private readonly PermissionService _permissionService;

    private static readonly IReadOnlyDictionary<string, string> SortColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["SupplierCode"] = "s.SupplierCode",
        ["SupplierName"] = "s.SupplierName",
        ["PONo"] = "p.PONo",
        ["PODate"] = "p.PODate",
        ["PRNo"] = "pr.RequestNo",
        ["Remark"] = "p.Remark",
        ["Status"] = "st.POStatusName",
        ["AssessLevel"] = "al.AssessLevelName",
        ["Comment"] = "p.Comment"
    };

    public IndexModel(IConfiguration config, PermissionService permissionService) : base(config)
    {
        _permissionService = permissionService;
    }

    public PagePermissions PagePerm { get; private set; } = new();
    public int DefaultPageSize => _config.GetValue<int?>("AppSettings:DefaultPageSize") ?? 10;
    public IReadOnlyList<int> PageSizeOptions => GetConfiguredPageSizeOptions();

    [BindProperty(SupportsGet = true)]
    public AnalyzingSuppliersFilter Filter { get; set; } = new();

    public List<SelectListItem> SupplierCodeOptions { get; private set; } = new();
    public List<SelectListItem> SupplierNameOptions { get; private set; } = new();
    public List<AnalyzingSuppliersRow> Rows { get; private set; } = new();
    public int TotalRecords { get; private set; }
    public int TotalPages => Filter.PageNumber <= 0 || Filter.PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)Filter.PageSize));
    public int PageStart => TotalRecords == 0 ? 0 : ((Filter.PageNumber - 1) * Filter.PageSize) + 1;
    public int PageEnd => TotalRecords == 0 ? 0 : Math.Min(Filter.PageNumber * Filter.PageSize, TotalRecords);
    public bool HasPreviousPage => Filter.PageNumber > 1;
    public bool HasNextPage => Filter.PageNumber < TotalPages;
    public bool CanPrintReport => Filter.SupplierId.HasValue && Filter.SupplierId.Value > 0;
    public string SelectedSupplierCode { get; private set; } = string.Empty;
    public string SelectedSupplierName { get; private set; } = string.Empty;

    public IActionResult OnGet()
    {
        PagePerm = GetUserPermissions();
        if (!HasPermission(PermissionViewList))
        {
            return Redirect("/");
        }

        NormalizeFilter();
        LoadSupplierOptions();
        LoadRows();
        LoadSelectedSupplierInfo();
        return Page();
    }

    public IActionResult OnGetExportExcel()
    {
        PagePerm = GetUserPermissions();
        if (!HasPermission(PermissionViewList))
        {
            return Redirect("/");
        }

        NormalizeFilter();
        LoadSupplierOptions();

        var exportFilter = CloneFilterForExport();
        var (rows, _) = SearchRows(exportFilter);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Analyzing Suppliers");
        var headers = new[] { "Supplier Code", "Supplier Name", "PO No.", "PO Date", "PR No.", "Remark", "Status", "Assess Level", "Comment" };

        for (var i = 0; i < headers.Length; i++)
        {
            worksheet.Cell(1, i + 1).Value = headers[i];
        }

        var rowIndex = 2;
        foreach (var row in rows)
        {
            worksheet.Cell(rowIndex, 1).Value = row.SupplierCode;
            worksheet.Cell(rowIndex, 2).Value = row.SupplierName;
            worksheet.Cell(rowIndex, 3).Value = row.PONo;
            worksheet.Cell(rowIndex, 4).Value = row.PODateText;
            worksheet.Cell(rowIndex, 5).Value = row.PRNo;
            worksheet.Cell(rowIndex, 6).Value = row.Remark;
            worksheet.Cell(rowIndex, 7).Value = row.StatusName;
            worksheet.Cell(rowIndex, 8).Value = row.AssessLevelName;
            worksheet.Cell(rowIndex, 9).Value = row.Comment;
            rowIndex++;
        }

        var usedRange = worksheet.Range(1, 1, Math.Max(1, rowIndex - 1), headers.Length);
        usedRange.Style.Font.FontName = ExcelVniFontName;
        usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        usedRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        worksheet.Row(1).Style.Font.Bold = true;
        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"analyzing_suppliers_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
    }

    public IActionResult OnGetReport()
    {
        PagePerm = GetUserPermissions();
        if (!HasPermission(PermissionViewList))
        {
            return Redirect("/");
        }

        NormalizeFilter();
        LoadSupplierOptions();
        LoadSelectedSupplierInfo();

        if (!CanPrintReport)
        {
            return RedirectToPage("./Index", BuildRouteValues());
        }

        var reportFilter = CloneFilterForExport();
        var (rows, _) = SearchRows(reportFilter);
        var model = BuildReportModel(rows);
        var pdfBytes = AnalyzingSuppliersPdfReport.BuildPdf(model);
        var fileName = $"analyzing_suppliers_{SelectedSupplierCode}_{DateTime.Now:yyyyMMddHHmmss}.pdf";
        return File(pdfBytes, "application/pdf", fileName);
    }

    public string GetNextSortDirection(string column)
    {
        if (string.Equals(Filter.SortBy, column, StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(Filter.SortDirection, "ASC", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
        }

        return "ASC";
    }

    public string GetSortIcon(string column)
    {
        if (!string.Equals(Filter.SortBy, column, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        return string.Equals(Filter.SortDirection, "ASC", StringComparison.OrdinalIgnoreCase)
            ? "<i class=\"fas fa-sort-up\"></i>"
            : "<i class=\"fas fa-sort-down\"></i>";
    }

    private void LoadRows()
    {
        var (rows, totalRecords) = SearchRows(Filter);
        Rows = rows;
        TotalRecords = totalRecords;

        if (TotalRecords > 0 && Filter.PageNumber > TotalPages)
        {
            Filter.PageNumber = TotalPages;
            (rows, totalRecords) = SearchRows(Filter);
            Rows = rows;
            TotalRecords = totalRecords;
        }
    }

    private (List<AnalyzingSuppliersRow> rows, int totalRecords) SearchRows(AnalyzingSuppliersFilter filter)
    {
        var rows = new List<AnalyzingSuppliersRow>();
        var whereParts = new List<string>();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = conn.CreateCommand();

        BuildWhereClause(filter, whereParts, cmd);
        var whereSql = whereParts.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", whereParts)}";
        var orderBySql = BuildOrderBySql(filter);

        cmd.CommandText = $@"
SELECT COUNT(1) AS TotalRecords
FROM dbo.PC_PO p
LEFT JOIN dbo.PC_Suppliers s ON s.SupplierID = p.SupplierID
LEFT JOIN dbo.PC_PR pr ON pr.PRID = p.PRID
LEFT JOIN dbo.PC_POStatus st ON st.POStatusID = p.StatusID
LEFT JOIN dbo.PC_AssessLevel al ON al.AssessLevelID = p.AssessLevel
{whereSql};

SELECT
    p.POID,
    ISNULL(s.SupplierCode, '') AS SupplierCode,
    ISNULL(s.SupplierName, '') AS SupplierName,
    ISNULL(p.PONo, '') AS PONo,
    p.PODate,
    ISNULL(pr.RequestNo, '') AS PRNo,
    ISNULL(p.Remark, '') AS Remark,
    ISNULL(st.POStatusName, '') AS StatusName,
    ISNULL(al.AssessLevelName, '') AS AssessLevelName,
    ISNULL(p.Comment, '') AS Comment
FROM dbo.PC_PO p
LEFT JOIN dbo.PC_Suppliers s ON s.SupplierID = p.SupplierID
LEFT JOIN dbo.PC_PR pr ON pr.PRID = p.PRID
LEFT JOIN dbo.PC_POStatus st ON st.POStatusID = p.StatusID
LEFT JOIN dbo.PC_AssessLevel al ON al.AssessLevelID = p.AssessLevel
{whereSql}
ORDER BY {orderBySql}
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

        cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = (filter.PageNumber - 1) * filter.PageSize;
        cmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = filter.PageSize;

        conn.Open();
        using var reader = cmd.ExecuteReader();

        var totalRecords = 0;
        if (reader.Read())
        {
            totalRecords = reader.IsDBNull(reader.GetOrdinal("TotalRecords")) ? 0 : Convert.ToInt32(reader["TotalRecords"]);
        }

        if (reader.NextResult())
        {
            while (reader.Read())
            {
                rows.Add(new AnalyzingSuppliersRow
                {
                    POID = Convert.ToInt32(reader["POID"]),
                    SupplierCode = Convert.ToString(reader["SupplierCode"]) ?? string.Empty,
                    SupplierName = Convert.ToString(reader["SupplierName"]) ?? string.Empty,
                    PONo = Convert.ToString(reader["PONo"]) ?? string.Empty,
                    PODate = reader.IsDBNull(reader.GetOrdinal("PODate")) ? null : Convert.ToDateTime(reader["PODate"]),
                    PRNo = Convert.ToString(reader["PRNo"]) ?? string.Empty,
                    Remark = Convert.ToString(reader["Remark"]) ?? string.Empty,
                    StatusName = Convert.ToString(reader["StatusName"]) ?? string.Empty,
                    AssessLevelName = Convert.ToString(reader["AssessLevelName"]) ?? string.Empty,
                    Comment = Convert.ToString(reader["Comment"]) ?? string.Empty
                });
            }
        }

        return (rows, totalRecords);
    }

    private void BuildWhereClause(AnalyzingSuppliersFilter filter, List<string> whereParts, SqlCommand cmd)
    {
        if (filter.SupplierId.HasValue && filter.SupplierId.Value > 0)
        {
            whereParts.Add("p.SupplierID = @SupplierID");
            cmd.Parameters.Add("@SupplierID", SqlDbType.Int).Value = filter.SupplierId.Value;
        }
        else
        {
            whereParts.Add("p.SupplierID IS NOT NULL");
        }
    }

    private string BuildOrderBySql(AnalyzingSuppliersFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.SortBy) && SortColumns.TryGetValue(filter.SortBy, out var columnSql))
        {
            var direction = string.Equals(filter.SortDirection, "DESC", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
            return $"{columnSql} {direction}, p.POID DESC";
        }

        return "p.PODate ASC, p.POID DESC";
    }

    private void NormalizeFilter()
    {
        Filter.PageNumber = Filter.PageNumber <= 0 ? 1 : Filter.PageNumber;
        Filter.PageSize = NormalizePageSize(Filter.PageSize);
        if (!SortColumns.ContainsKey(Filter.SortBy ?? string.Empty))
        {
            Filter.SortBy = "PODate";
        }

        Filter.SortDirection = string.Equals(Filter.SortDirection, "DESC", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";
        if (Filter.SupplierId.HasValue && Filter.SupplierId.Value <= 0)
        {
            Filter.SupplierId = null;
        }
    }

    private void LoadSupplierOptions()
    {
        SupplierCodeOptions = new List<SelectListItem> { new() { Value = string.Empty, Text = "--- All ---" } };
        SupplierNameOptions = new List<SelectListItem> { new() { Value = string.Empty, Text = "--- All ---" } };

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
SELECT SupplierID, ISNULL(SupplierCode, '') AS SupplierCode, ISNULL(SupplierName, '') AS SupplierName
FROM dbo.PC_Suppliers
ORDER BY SupplierName, SupplierCode, SupplierID", conn);

        conn.Open();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var supplierId = Convert.ToInt32(reader["SupplierID"]).ToString();
            var supplierCode = Convert.ToString(reader["SupplierCode"]) ?? string.Empty;
            var supplierName = Convert.ToString(reader["SupplierName"]) ?? string.Empty;

            SupplierCodeOptions.Add(new SelectListItem { Value = supplierId, Text = string.IsNullOrWhiteSpace(supplierCode) ? "(No code)" : supplierCode });
            SupplierNameOptions.Add(new SelectListItem { Value = supplierId, Text = string.IsNullOrWhiteSpace(supplierName) ? "(No name)" : supplierName });
        }

        if (Filter.SupplierId.HasValue)
        {
            var selectedValue = Filter.SupplierId.Value.ToString();
            if (!SupplierCodeOptions.Any(x => x.Value == selectedValue))
            {
                Filter.SupplierId = null;
            }
        }
    }

    private void LoadSelectedSupplierInfo()
    {
        if (!Filter.SupplierId.HasValue || Filter.SupplierId.Value <= 0)
        {
            SelectedSupplierCode = "--- All ---";
            SelectedSupplierName = "--- All ---";
            return;
        }

        var selectedValue = Filter.SupplierId.Value.ToString();
        SelectedSupplierCode = SupplierCodeOptions.FirstOrDefault(x => x.Value == selectedValue)?.Text ?? string.Empty;
        SelectedSupplierName = SupplierNameOptions.FirstOrDefault(x => x.Value == selectedValue)?.Text ?? string.Empty;
    }

    private AnalyzingSuppliersFilter CloneFilterForExport()
    {
        return new AnalyzingSuppliersFilter
        {
            SupplierId = Filter.SupplierId,
            SortBy = Filter.SortBy,
            SortDirection = Filter.SortDirection,
            PageNumber = 1,
            PageSize = int.MaxValue
        };
    }

    private object BuildRouteValues()
    {
        return new
        {
            SupplierId = Filter.SupplierId,
            SortBy = Filter.SortBy,
            SortDirection = Filter.SortDirection,
            PageNumber = Filter.PageNumber,
            PageSize = Filter.PageSize
        };
    }

    private AnalyzingSuppliersReportModel BuildReportModel(IReadOnlyList<AnalyzingSuppliersRow> rows)
    {
        var goodCount = rows.Count(row => !IsNotGoodRow(row));
        var notGoodCount = rows.Count - goodCount;

        return new AnalyzingSuppliersReportModel
        {
            SupplierCode = SelectedSupplierCode,
            SupplierName = SelectedSupplierName,
            GeneratedDate = DateTime.Today,
            GoodCount = goodCount,
            NotGoodCount = notGoodCount,
            Rows = rows.Select(row => new AnalyzingSuppliersReportRow
            {
                PONo = row.PONo,
                PRNo = row.PRNo,
                PODate = row.PODate,
                Remark = row.Remark,
                StatusName = row.StatusName,
                AssessLevelName = row.AssessLevelName,
                Comment = row.Comment
            }).ToList()
        };
    }

    private static bool IsNotGoodRow(AnalyzingSuppliersRow row)
    {
        var assessLevel = row.AssessLevelName.Trim();
        var comment = row.Comment.Trim();
        return assessLevel.Contains("not good", StringComparison.OrdinalIgnoreCase)
            || comment.Contains("not good", StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<int> GetConfiguredPageSizeOptions()
    {
        var configured = _config.GetSection("AppSettings:PageSizeOptions").Get<int[]>() ?? Array.Empty<int>();
        var options = configured.Where(value => value > 0).Distinct().ToList();
        if (options.Count == 0)
        {
            options = new List<int> { DefaultPageSize, 20, 25, 30, 35 }.Distinct().ToList();
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

    private bool HasPermission(int permissionNo) => PagePerm.HasPermission(permissionNo);

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

    private int GetCurrentRoleId()
    {
        return int.Parse(User.FindFirst("RoleID")?.Value ?? "0");
    }

    private bool IsAdminRole()
    {
        return User.FindFirst("IsAdminRole")?.Value == "True";
    }
}

public class AnalyzingSuppliersFilter
{
    public int? SupplierId { get; set; }
    public string? SortBy { get; set; } = "PODate";
    public string? SortDirection { get; set; } = "ASC";
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class AnalyzingSuppliersRow
{
    public int POID { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string PONo { get; set; } = string.Empty;
    public DateTime? PODate { get; set; }
    public string PODateText => PODate?.ToString("dd/MM/yyyy") ?? string.Empty;
    public string PRNo { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
    public string AssessLevelName { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
}
