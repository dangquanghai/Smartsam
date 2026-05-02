using System.Data;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;

namespace SmartSam.Pages.Inventory.InventoryParameters;

public class IndexModel : BasePageModel
{
    private const string ExcelVniFontName = "VNI-WIN";
    private const int FunctionId = 146;
    private const int PermissionViewList = 1;
    private const int PermissionAdd = 3;
    private const int PermissionEdit = 4;

    private readonly PermissionService _permissionService;

    public IndexModel(IConfiguration config, PermissionService permissionService) : base(config)
    {
        _permissionService = permissionService;
    }

    public PagePermissions PagePerm { get; private set; } = new PagePermissions();
    public int DefaultPageSize => _config.GetValue<int?>("AppSettings:DefaultPageSize") ?? 10;
    public IReadOnlyList<int> PageSizeOptions => GetConfiguredPageSizeOptions();
    public bool CanAdd => PagePerm.HasPermission(PermissionAdd);
    public bool CanEdit => PagePerm.HasPermission(PermissionEdit);

    [BindProperty(SupportsGet = true)]
    public InventoryParametersFilter Filter { get; set; } = new InventoryParametersFilter();

    [BindProperty]
    public InventoryGroupInput GroupInput { get; set; } = new InventoryGroupInput();

    public List<InventoryGroupRow> Rows { get; set; } = new List<InventoryGroupRow>();
    public List<SelectListItem> DepartmentList { get; set; } = new List<SelectListItem>();
    public int TotalRecords { get; set; }
    public int TotalPages => Filter.PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)Filter.PageSize));
    public int PageStart => TotalRecords == 0 ? 0 : ((Filter.Page - 1) * Filter.PageSize) + 1;
    public int PageEnd => TotalRecords == 0 ? 0 : Math.Min(Filter.Page * Filter.PageSize, TotalRecords);
    public bool HasPreviousPage => Filter.Page > 1;
    public bool HasNextPage => Filter.Page < TotalPages;

    public IActionResult OnGet()
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewList))
        {
            return Redirect("/");
        }

        NormalizeQueryInputs();
        NormalizeFilter();
        LoadDepartmentList();
        LoadRows();
        LoadEditGroup();
        return Page();
    }

    public IActionResult OnPostSaveGroup()
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewList))
        {
            return Redirect("/");
        }

        var isEdit = GroupInput.KPGroupID > 0;
        if (!PagePerm.HasPermission(isEdit ? PermissionEdit : PermissionAdd))
        {
            SetMessage("You do not have permission to save inventory group.", "warning");
            return RedirectToPage("./Index", BuildRouteValues());
        }

        GroupInput.KPGroupName = (GroupInput.KPGroupName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(GroupInput.KPGroupName))
        {
            SetMessage("Group Name is required.", "warning");
            return RedirectToPage("./Index", BuildRouteValues());
        }

        if (GroupInput.KPGroupName.Length > 50)
        {
            SetMessage("Group Name cannot exceed 50 characters.", "warning");
            return RedirectToPage("./Index", BuildRouteValues());
        }

        if (!GroupInput.DepID.HasValue || GroupInput.DepID.Value <= 0)
        {
            SetMessage("Department is required.", "warning");
            return RedirectToPage("./Index", BuildRouteValues());
        }

        using var conn = OpenConnection();
        if (!DepartmentExists(conn, GroupInput.DepID.Value))
        {
            SetMessage("Department is invalid.", "warning");
            return RedirectToPage("./Index", BuildRouteValues());
        }

        using var cmd = conn.CreateCommand();
        if (isEdit)
        {
            cmd.CommandText = @"
UPDATE dbo.INV_KPGroup
SET KPGroupName = @KPGroupName,
    IsAdminGroup = @IsAdminGroup,
    DepID = @DepID
WHERE KPGroupID = @KPGroupID;";
            cmd.Parameters.Add("@KPGroupID", SqlDbType.Int).Value = GroupInput.KPGroupID;
        }
        else
        {
            cmd.CommandText = @"
INSERT INTO dbo.INV_KPGroup (KPGroupName, IsAdminGroup, DepID)
VALUES (@KPGroupName, @IsAdminGroup, @DepID);";
        }

        cmd.Parameters.Add("@KPGroupName", SqlDbType.VarChar, 50).Value = GroupInput.KPGroupName;
        cmd.Parameters.Add("@IsAdminGroup", SqlDbType.Bit).Value = GroupInput.IsAdminGroup;
        cmd.Parameters.Add("@DepID", SqlDbType.Int).Value = GroupInput.DepID.Value;
        cmd.ExecuteNonQuery();

        SetMessage(isEdit ? "Inventory group updated." : "Inventory group added.", "success");
        return RedirectToPage("./Index", BuildRouteValues());
    }

    public IActionResult OnGetExportExcel()
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewList))
        {
            return Redirect("/");
        }

        NormalizeQueryInputs();
        NormalizeFilter();

        var exportFilter = new InventoryParametersFilter
        {
            GroupName = Filter.GroupName,
            DepartmentId = Filter.DepartmentId,
            AdminGroup = Filter.AdminGroup,
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

    private (List<InventoryGroupRow> rows, int totalRecords) SearchRows(InventoryParametersFilter filter)
    {
        var rows = new List<InventoryGroupRow>();
        var page = filter.Page <= 0 ? 1 : filter.Page;
        var pageSize = NormalizePageSize(filter.PageSize);
        var offset = (page - 1) * pageSize;

        using var conn = OpenConnection();
        using var countCmd = new SqlCommand(@"
SELECT COUNT(1)
FROM dbo.INV_KPGroup kp
LEFT JOIN dbo.MS_Department dep ON dep.DeptID = kp.DepID
WHERE (@GroupName IS NULL OR kp.KPGroupName LIKE '%' + @GroupName + '%')
  AND (@DepartmentId IS NULL OR kp.DepID = @DepartmentId)
  AND (@AdminGroup IS NULL OR kp.IsAdminGroup = @AdminGroup);", conn);

        BindSearchParams(countCmd, filter);
        var totalRecords = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);

        using var cmd = new SqlCommand(@"
SELECT
    kp.KPGroupID,
    ISNULL(kp.KPGroupName, '') AS KPGroupName,
    ISNULL(kp.IsAdminGroup, 0) AS IsAdminGroup,
    kp.DepID,
    ISNULL(dep.DeptCode, '') AS DeptCode,
    ISNULL(dep.DeptName, '') AS DeptName,
    ISNULL(storeStats.StoreCount, 0) AS StoreCount,
    ISNULL(memberStats.MemberCount, 0) AS MemberCount,
    ISNULL(itemStats.ItemCount, 0) AS ItemCount
FROM dbo.INV_KPGroup kp
LEFT JOIN dbo.MS_Department dep ON dep.DeptID = kp.DepID
OUTER APPLY
(
    SELECT COUNT(1) AS StoreCount
    FROM dbo.INV_StoreList store
    WHERE store.DeptID = kp.KPGroupID
) storeStats
OUTER APPLY
(
    SELECT COUNT(1) AS MemberCount
    FROM dbo.INV_KPGroupMember member
    WHERE member.KPGroupID = kp.KPGroupID
) memberStats
OUTER APPLY
(
    SELECT COUNT(1) AS ItemCount
    FROM dbo.INV_KPGroupIndex item
    WHERE item.KPGroupID = kp.KPGroupID
) itemStats
WHERE (@GroupName IS NULL OR kp.KPGroupName LIKE '%' + @GroupName + '%')
  AND (@DepartmentId IS NULL OR kp.DepID = @DepartmentId)
  AND (@AdminGroup IS NULL OR kp.IsAdminGroup = @AdminGroup)
ORDER BY kp.KPGroupID DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", conn);

        BindSearchParams(cmd, filter);
        cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
        cmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(new InventoryGroupRow
            {
                KPGroupID = Convert.ToInt32(rd["KPGroupID"]),
                KPGroupName = Convert.ToString(rd["KPGroupName"]) ?? string.Empty,
                IsAdminGroup = Convert.ToBoolean(rd["IsAdminGroup"]),
                DepID = rd["DepID"] == DBNull.Value ? null : Convert.ToInt32(rd["DepID"]),
                DeptCode = Convert.ToString(rd["DeptCode"]) ?? string.Empty,
                DeptName = Convert.ToString(rd["DeptName"]) ?? string.Empty,
                StoreCount = Convert.ToInt32(rd["StoreCount"]),
                MemberCount = Convert.ToInt32(rd["MemberCount"]),
                ItemCount = Convert.ToInt32(rd["ItemCount"])
            });
        }

        return (rows, totalRecords);
    }

    private void LoadEditGroup()
    {
        if (!Filter.EditGroupId.HasValue)
        {
            return;
        }

        using var conn = OpenConnection();
        using var cmd = new SqlCommand(@"
SELECT KPGroupID, KPGroupName, IsAdminGroup, DepID
FROM dbo.INV_KPGroup
WHERE KPGroupID = @KPGroupID;", conn);
        cmd.Parameters.Add("@KPGroupID", SqlDbType.Int).Value = Filter.EditGroupId.Value;

        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
        {
            return;
        }

        GroupInput = new InventoryGroupInput
        {
            KPGroupID = Convert.ToInt32(rd["KPGroupID"]),
            KPGroupName = Convert.ToString(rd["KPGroupName"]) ?? string.Empty,
            IsAdminGroup = Convert.ToBoolean(rd["IsAdminGroup"]),
            DepID = rd["DepID"] == DBNull.Value ? null : Convert.ToInt32(rd["DepID"])
        };
    }

    private IActionResult ExportRows(IReadOnlyList<InventoryGroupRow> rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Inventory Groups");
        var headers = new[]
        {
            "STT",
            "Group Name",
            "Admin Group",
            "Department",
            "Stores",
            "Members",
            "Items"
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
            worksheet.Cell(rowIndex, 2).Value = row.KPGroupName;
            worksheet.Cell(rowIndex, 3).Value = row.IsAdminGroup ? "Yes" : "No";
            worksheet.Cell(rowIndex, 4).Value = row.DepartmentText;
            worksheet.Cell(rowIndex, 5).Value = row.StoreCount;
            worksheet.Cell(rowIndex, 6).Value = row.MemberCount;
            worksheet.Cell(rowIndex, 7).Value = row.ItemCount;
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
            $"inventory_parameters_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
    }

    private static void BindSearchParams(SqlCommand cmd, InventoryParametersFilter filter)
    {
        cmd.Parameters.Add("@GroupName", SqlDbType.VarChar, 50).Value =
            string.IsNullOrWhiteSpace(filter.GroupName) ? DBNull.Value : filter.GroupName.Trim();
        cmd.Parameters.Add("@DepartmentId", SqlDbType.Int).Value =
            filter.DepartmentId.HasValue ? filter.DepartmentId.Value : DBNull.Value;
        cmd.Parameters.Add("@AdminGroup", SqlDbType.Bit).Value =
            filter.AdminGroup.HasValue ? filter.AdminGroup.Value : DBNull.Value;
    }

    private void LoadDepartmentList()
    {
        DepartmentList = LoadListFromSql(
            @"SELECT
                    DeptID,
                    CASE
                        WHEN NULLIF(DeptCode, '') IS NOT NULL AND NULLIF(DeptName, '') IS NOT NULL THEN CONCAT(DeptCode, ' - ', DeptName)
                        WHEN NULLIF(DeptCode, '') IS NOT NULL THEN DeptCode
                        ELSE ISNULL(DeptName, '')
                    END AS DepartmentText
              FROM dbo.MS_Department
              ORDER BY DeptCode, DeptName",
            "DeptID",
            "DepartmentText",
            true);
    }

    private bool DepartmentExists(SqlConnection conn, int departmentId)
    {
        using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.MS_Department WHERE DeptID = @DeptID;", conn);
        cmd.Parameters.Add("@DeptID", SqlDbType.Int).Value = departmentId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private void NormalizeFilter()
    {
        Filter.GroupName = string.IsNullOrWhiteSpace(Filter.GroupName) ? null : Filter.GroupName.Trim();
        Filter.PageSize = NormalizePageSize(Filter.PageSize);

        if (Filter.Page <= 0)
        {
            Filter.Page = 1;
        }

        if (Filter.AdminGroup.HasValue && Filter.AdminGroup.Value != 0 && Filter.AdminGroup.Value != 1)
        {
            Filter.AdminGroup = null;
        }

        if (Filter.DepartmentId.HasValue && Filter.DepartmentId.Value <= 0)
        {
            Filter.DepartmentId = null;
        }

        if (Filter.EditGroupId.HasValue && Filter.EditGroupId.Value <= 0)
        {
            Filter.EditGroupId = null;
        }
    }

    private void NormalizeQueryInputs()
    {
        Filter.GroupName = Request.Query[nameof(Filter.GroupName)].ToString();
        Filter.DepartmentId = ParseNullableInt(Request.Query[nameof(Filter.DepartmentId)].ToString());
        Filter.AdminGroup = ParseNullableInt(Request.Query[nameof(Filter.AdminGroup)].ToString());
        Filter.EditGroupId = ParseNullableInt(Request.Query[nameof(Filter.EditGroupId)].ToString());
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

    private SqlConnection OpenConnection()
    {
        var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        return conn;
    }

    private object BuildRouteValues()
    {
        return new
        {
            Filter.GroupName,
            Filter.DepartmentId,
            Filter.AdminGroup,
            PageNumber = Filter.Page,
            PageSize = Filter.PageSize
        };
    }

    private void SetMessage(string message, string type)
    {
        TempData["Message"] = message;
        TempData["MessageType"] = type;
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

public class InventoryParametersFilter
{
    public string? GroupName { get; set; }
    public int? DepartmentId { get; set; }
    public int? AdminGroup { get; set; }
    public int? EditGroupId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class InventoryGroupInput
{
    public int KPGroupID { get; set; }
    public string KPGroupName { get; set; } = string.Empty;
    public bool IsAdminGroup { get; set; }
    public int? DepID { get; set; }
}

public class InventoryGroupRow
{
    public int KPGroupID { get; set; }
    public string KPGroupName { get; set; } = string.Empty;
    public bool IsAdminGroup { get; set; }
    public int? DepID { get; set; }
    public string DeptCode { get; set; } = string.Empty;
    public string DeptName { get; set; } = string.Empty;
    public int StoreCount { get; set; }
    public int MemberCount { get; set; }
    public int ItemCount { get; set; }

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
