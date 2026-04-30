using System.Data;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;

namespace SmartSam.Pages.Inventory.GroupOfInventoryUser;

public class IndexModel : BasePageModel
{
    private const string ExcelVniFontName = "VNI-WIN";
    private const int FunctionId = 61;
    private const int PermissionViewList = 1;

    private readonly PermissionService _permissionService;

    public IndexModel(IConfiguration config, PermissionService permissionService) : base(config)
    {
        _permissionService = permissionService;
    }

    public PagePermissions PagePerm { get; private set; } = new PagePermissions();
    public int DefaultPageSize => _config.GetValue<int?>("AppSettings:DefaultPageSize") ?? 10;
    public IReadOnlyList<int> PageSizeOptions => GetConfiguredPageSizeOptions();

    [BindProperty(SupportsGet = true)]
    public GroupOfInventoryUserFilter Filter { get; set; } = new GroupOfInventoryUserFilter();

    public List<SelectListItem> StoreGroupList { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> DepartmentList { get; set; } = new List<SelectListItem>();
    public List<GroupOfInventoryUserRow> Rows { get; set; } = new List<GroupOfInventoryUserRow>();
    public int TotalRecords { get; set; }
    public int TotalPages => Filter.PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)Filter.PageSize));
    public int PageStart => TotalRecords == 0 ? 0 : ((Filter.Page - 1) * Filter.PageSize) + 1;
    public int PageEnd => TotalRecords == 0 ? 0 : Math.Min(Filter.Page * Filter.PageSize, TotalRecords);
    public bool HasPreviousPage => Filter.Page > 1;
    public bool HasNextPage => Filter.Page < TotalPages;

    public IActionResult OnGet()
    {
        PagePerm = GetUserPermissions();
        if (!HasPermission(PermissionViewList))
        {
            return Redirect("/");
        }

        NormalizeQueryInputs();
        NormalizeFilter();
        LoadStoreGroupList();
        LoadDepartmentList();
        LoadRows();
        return Page();
    }

    public IActionResult OnGetExportExcel()
    {
        PagePerm = GetUserPermissions();
        if (!HasPermission(PermissionViewList))
        {
            return Redirect("/");
        }

        NormalizeQueryInputs();
        NormalizeFilter();

        var exportFilter = new GroupOfInventoryUserFilter
        {
            StoreGroupId = Filter.StoreGroupId,
            DepartmentId = Filter.DepartmentId,
            EmployeeKeyword = Filter.EmployeeKeyword,
            ActiveStatus = Filter.ActiveStatus,
            Page = 1,
            PageSize = int.MaxValue
        };

        var (rows, _) = SearchRows(exportFilter);
        return ExportRows(rows);
    }

    private void LoadRows()
    {
        var (rows, totalRecords) = SearchRows(Filter);
        Rows = rows;
        TotalRecords = totalRecords;

        if (TotalRecords > 0 && Filter.Page > TotalPages)
        {
            Filter.Page = TotalPages;
            (rows, totalRecords) = SearchRows(Filter);
            Rows = rows;
            TotalRecords = totalRecords;
        }
    }

    private (List<GroupOfInventoryUserRow> rows, int totalRecords) SearchRows(GroupOfInventoryUserFilter filter)
    {
        var rows = new List<GroupOfInventoryUserRow>();
        var page = filter.Page <= 0 ? 1 : filter.Page;
        var pageSize = NormalizePageSize(filter.PageSize);
        var offset = (page - 1) * pageSize;

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        using var countCmd = new SqlCommand(@"
SELECT COUNT(1)
FROM dbo.MS_Employee emp
LEFT JOIN dbo.INV_KPGroup kp ON kp.KPGroupID = emp.StoreGR
LEFT JOIN dbo.MS_Department dep ON dep.DeptID = emp.DeptID
WHERE emp.StoreGR IS NOT NULL
  AND (@StoreGroupId IS NULL OR emp.StoreGR = @StoreGroupId)
  AND (@DepartmentId IS NULL OR emp.DeptID = @DepartmentId)
  AND (
        @EmployeeKeyword IS NULL
        OR emp.EmployeeCode LIKE '%' + @EmployeeKeyword + '%'
        OR emp.EmployeeName LIKE '%' + @EmployeeKeyword + '%'
      )
  AND (@ActiveStatus IS NULL OR emp.IsActive = @ActiveStatus);", conn);

        BindSearchParams(countCmd, filter);
        var totalRecords = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);

        using var cmd = new SqlCommand(@"
SELECT
    ISNULL(emp.StoreGR, 0) AS KPGroupID,
    CASE
        WHEN kp.KPGroupName IS NOT NULL THEN kp.KPGroupName
        ELSE CONCAT('(Missing group #', emp.StoreGR, ')')
    END AS KPGroupName,
    emp.EmployeeID,
    ISNULL(emp.EmployeeCode, '') AS EmployeeCode,
    ISNULL(emp.EmployeeName, '') AS EmployeeName,
    ISNULL(dep.DeptCode, '') AS DeptCode,
    ISNULL(dep.DeptName, '') AS DeptName,
    ISNULL(emp.Position, '') AS Position,
    ISNULL(emp.IsActive, 0) AS IsActive,
    ISNULL(emp.IsInventoryControlInDep, 0) AS IsInventoryControlInDep,
    ISNULL(emp.SeeDataAllDept, 0) AS SeeDataAllDept
FROM dbo.MS_Employee emp
LEFT JOIN dbo.INV_KPGroup kp ON kp.KPGroupID = emp.StoreGR
LEFT JOIN dbo.MS_Department dep ON dep.DeptID = emp.DeptID
WHERE emp.StoreGR IS NOT NULL
  AND (@StoreGroupId IS NULL OR emp.StoreGR = @StoreGroupId)
  AND (@DepartmentId IS NULL OR emp.DeptID = @DepartmentId)
  AND (
        @EmployeeKeyword IS NULL
        OR emp.EmployeeCode LIKE '%' + @EmployeeKeyword + '%'
        OR emp.EmployeeName LIKE '%' + @EmployeeKeyword + '%'
      )
  AND (@ActiveStatus IS NULL OR emp.IsActive = @ActiveStatus)
ORDER BY emp.StoreGR, emp.EmployeeCode
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", conn);

        BindSearchParams(cmd, filter);
        cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
        cmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(new GroupOfInventoryUserRow
            {
                StoreGroupId = Convert.ToInt32(rd["KPGroupID"]),
                StoreGroupName = Convert.ToString(rd["KPGroupName"]) ?? string.Empty,
                EmployeeId = Convert.ToInt32(rd["EmployeeID"]),
                EmployeeCode = Convert.ToString(rd["EmployeeCode"]) ?? string.Empty,
                EmployeeName = Convert.ToString(rd["EmployeeName"]) ?? string.Empty,
                DeptCode = Convert.ToString(rd["DeptCode"]) ?? string.Empty,
                DeptName = Convert.ToString(rd["DeptName"]) ?? string.Empty,
                Position = Convert.ToString(rd["Position"]) ?? string.Empty,
                IsActive = Convert.ToBoolean(rd["IsActive"]),
                IsInventoryControlInDep = Convert.ToBoolean(rd["IsInventoryControlInDep"]),
                SeeDataAllDept = Convert.ToBoolean(rd["SeeDataAllDept"])
            });
        }

        return (rows, totalRecords);
    }

    private IActionResult ExportRows(IReadOnlyList<GroupOfInventoryUserRow> rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Inventory Users");
        var headers = new[]
        {
            "STT",
            "Store Group",
            "Employee Code",
            "Employee Name",
            "Department",
            "Position",
            "Active",
            "Inventory Control",
            "See All Dept"
        };

        for (var col = 0; col < headers.Length; col++)
        {
            worksheet.Cell(1, col + 1).Value = headers[col];
        }

        var rowIndex = 2;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            worksheet.Cell(rowIndex, 1).Value = i + 1;
            worksheet.Cell(rowIndex, 2).Value = row.StoreGroupName;
            worksheet.Cell(rowIndex, 3).Value = row.EmployeeCode;
            worksheet.Cell(rowIndex, 4).Value = row.EmployeeName;
            worksheet.Cell(rowIndex, 5).Value = row.DepartmentText;
            worksheet.Cell(rowIndex, 6).Value = row.Position;
            worksheet.Cell(rowIndex, 7).Value = row.IsActive ? "Yes" : "No";
            worksheet.Cell(rowIndex, 8).Value = row.IsInventoryControlInDep ? "Yes" : "No";
            worksheet.Cell(rowIndex, 9).Value = row.SeeDataAllDept ? "Yes" : "No";
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

        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"inventory_group_users_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
    }

    private void LoadStoreGroupList()
    {
        StoreGroupList = LoadListFromSql(
            @"SELECT
                    kp.KPGroupID AS StoreGroupID,
                    ISNULL(kp.KPGroupName, CONCAT('(Group #', kp.KPGroupID, ')')) AS StoreGroupName
              FROM dbo.INV_KPGroup kp
              ORDER BY kp.KPGroupID",
            "StoreGroupID",
            "StoreGroupName",
            true);
    }

    private void LoadDepartmentList()
    {
        DepartmentList = LoadListFromSql(
            @"SELECT
                    dep.DeptID,
                    CASE
                        WHEN NULLIF(dep.DeptCode, '') IS NOT NULL AND NULLIF(dep.DeptName, '') IS NOT NULL THEN CONCAT(dep.DeptCode, ' - ', dep.DeptName)
                        WHEN NULLIF(dep.DeptCode, '') IS NOT NULL THEN dep.DeptCode
                        ELSE ISNULL(dep.DeptName, '')
                    END AS DepartmentText
              FROM dbo.MS_Department dep
              WHERE EXISTS
              (
                  SELECT 1
                  FROM dbo.MS_Employee emp
                  WHERE emp.StoreGR IS NOT NULL
                    AND emp.DeptID = dep.DeptID
              )
              ORDER BY dep.DeptCode, dep.DeptName",
            "DeptID",
            "DepartmentText",
            true);
    }

    private static void BindSearchParams(SqlCommand cmd, GroupOfInventoryUserFilter filter)
    {
        cmd.Parameters.Add("@StoreGroupId", SqlDbType.Int).Value =
            filter.StoreGroupId.HasValue ? filter.StoreGroupId.Value : DBNull.Value;
        cmd.Parameters.Add("@DepartmentId", SqlDbType.Int).Value =
            filter.DepartmentId.HasValue ? filter.DepartmentId.Value : DBNull.Value;
        cmd.Parameters.Add("@EmployeeKeyword", SqlDbType.VarChar, 100).Value =
            string.IsNullOrWhiteSpace(filter.EmployeeKeyword) ? DBNull.Value : filter.EmployeeKeyword.Trim();
        cmd.Parameters.Add("@ActiveStatus", SqlDbType.Bit).Value =
            filter.ActiveStatus.HasValue ? filter.ActiveStatus.Value : DBNull.Value;
    }

    private void NormalizeFilter()
    {
        Filter.EmployeeKeyword = string.IsNullOrWhiteSpace(Filter.EmployeeKeyword) ? null : Filter.EmployeeKeyword.Trim();
        Filter.PageSize = NormalizePageSize(Filter.PageSize);

        if (Filter.Page <= 0)
        {
            Filter.Page = 1;
        }

        if (Filter.StoreGroupId.HasValue && Filter.StoreGroupId.Value <= 0)
        {
            Filter.StoreGroupId = null;
        }

        if (Filter.DepartmentId.HasValue && Filter.DepartmentId.Value <= 0)
        {
            Filter.DepartmentId = null;
        }

        if (Filter.ActiveStatus.HasValue && Filter.ActiveStatus.Value != 0 && Filter.ActiveStatus.Value != 1)
        {
            Filter.ActiveStatus = null;
        }
    }

    private void NormalizeQueryInputs()
    {
        Filter.StoreGroupId = ParseNullableInt(Request.Query[nameof(Filter.StoreGroupId)].ToString());
        Filter.DepartmentId = ParseNullableInt(Request.Query[nameof(Filter.DepartmentId)].ToString());
        Filter.EmployeeKeyword = Request.Query[nameof(Filter.EmployeeKeyword)].ToString();
        Filter.ActiveStatus = ParseNullableInt(Request.Query[nameof(Filter.ActiveStatus)].ToString());
        Filter.Page = ParseInt(Request.Query["PageNumber"].ToString(), 1);
        Filter.PageSize = ParseInt(Request.Query["PageSize"].ToString(), DefaultPageSize);

        ModelState.Remove("Page");
        ModelState.Remove("page");
        ModelState.Remove("Filter.Page");
        ModelState.Remove("Filter.PageSize");
    }

    private IReadOnlyList<int> GetConfiguredPageSizeOptions()
    {
        var configured = _config.GetSection("AppSettings:PageSizeOptions").Get<int[]>() ?? Array.Empty<int>();
        var options = configured
            .Where(value => value > 0)
            .Distinct()
            .OrderBy(value => value)
            .ToList();

        if (options.Count == 0)
        {
            options = new List<int> { DefaultPageSize, 20, 50, 100, 200 }
                .Where(value => value > 0)
                .Distinct()
                .OrderBy(value => value)
                .ToList();
        }

        if (!options.Contains(DefaultPageSize))
        {
            options.Add(DefaultPageSize);
            options = options
                .Where(value => value > 0)
                .Distinct()
                .OrderBy(value => value)
                .ToList();
        }

        return options;
    }

    private int NormalizePageSize(int pageSize)
    {
        if (pageSize <= 0)
        {
            return DefaultPageSize;
        }

        if (pageSize == int.MaxValue)
        {
            return pageSize;
        }

        return PageSizeOptions.Contains(pageSize) ? pageSize : DefaultPageSize;
    }

    private static int ParseInt(string? raw, int defaultValue)
    {
        return int.TryParse(raw, out var parsed) ? parsed : defaultValue;
    }

    private static int? ParseNullableInt(string? raw)
    {
        return int.TryParse(raw, out var parsed) ? parsed : null;
    }

    private PagePermissions GetUserPermissions()
    {
        var perms = new PagePermissions();
        if (IsAdminRole())
        {
            perms.AllowedNos = Enumerable.Range(1, 20).ToList();
            return perms;
        }

        perms.AllowedNos = _permissionService.GetPermissionsForPage(GetCurrentRoleId(), FunctionId);
        return perms;
    }

    private bool HasPermission(int permissionNo) => PagePerm.HasPermission(permissionNo);

    private int GetCurrentRoleId()
    {
        return int.TryParse(User.FindFirst("RoleID")?.Value, out var roleId) ? roleId : 0;
    }

    private bool IsAdminRole()
    {
        var value = User.FindFirst("IsAdminRole")?.Value;
        return string.Equals(value, "True", StringComparison.OrdinalIgnoreCase) || value == "1";
    }
}

public class GroupOfInventoryUserFilter
{
    public int? StoreGroupId { get; set; }
    public int? DepartmentId { get; set; }
    public string? EmployeeKeyword { get; set; }
    public int? ActiveStatus { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class GroupOfInventoryUserRow
{
    public int StoreGroupId { get; set; }
    public string StoreGroupName { get; set; } = string.Empty;
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string DeptCode { get; set; } = string.Empty;
    public string DeptName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsInventoryControlInDep { get; set; }
    public bool SeeDataAllDept { get; set; }

    public string DepartmentText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DeptCode) && !string.IsNullOrWhiteSpace(DeptName))
            {
                return $"{DeptCode} - {DeptName}";
            }

            return !string.IsNullOrWhiteSpace(DeptCode) ? DeptCode : DeptName;
        }
    }
}
