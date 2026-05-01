using System.Data;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;

namespace SmartSam.Pages.Inventory.InventoryParameters;

public class InventoryParametersMemberModel : BasePageModel
{
    private const string ExcelVniFontName = "VNI-WIN";
    private const int FunctionId = 146;
    private const int PermissionViewList = 1;
    private const int PermissionAdd = 3;
    private const int PermissionDelete = 5;

    private readonly PermissionService _permissionService;

    public InventoryParametersMemberModel(IConfiguration config, PermissionService permissionService) : base(config)
    {
        _permissionService = permissionService;
    }

    public PagePermissions PagePerm { get; private set; } = new PagePermissions();
    public int DefaultPageSize => _config.GetValue<int?>("AppSettings:DefaultPageSize") ?? 10;
    public IReadOnlyList<int> PageSizeOptions => GetConfiguredPageSizeOptions();
    public bool CanAdd => PagePerm.HasPermission(PermissionAdd);
    public bool CanDelete => PagePerm.HasPermission(PermissionDelete);

    [BindProperty(SupportsGet = true)]
    public InventoryParametersMemberFilter Filter { get; set; } = new InventoryParametersMemberFilter();

    [BindProperty]
    public GroupMemberInput MemberInput { get; set; } = new GroupMemberInput();

    public List<SelectListItem> StoreGroupList { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> DepartmentList { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> EmployeeList { get; set; } = new List<SelectListItem>();
    public List<GroupMemberRow> Rows { get; set; } = new List<GroupMemberRow>();
    public string SelectedGroupName { get; set; } = string.Empty;
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
        LoadReferenceData();
        SetGroupContext();
        LoadRows();
        EmployeeList = LoadEmployeeList();
        return Page();
    }

    public IActionResult OnPostSaveMember()
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewList))
        {
            return Redirect("/");
        }

        if (MemberInput.DetailID > 0)
        {
            SetMessage("Edit group member is disabled.", "warning");
            return RedirectToPage("./InventoryParametersMember", BuildRouteValues());
        }

        if (!PagePerm.HasPermission(PermissionAdd))
        {
            SetMessage("You do not have permission to save group member.", "warning");
            return RedirectToPage("./InventoryParametersMember", BuildRouteValues());
        }

        if (Filter.GroupId.HasValue)
        {
            MemberInput.KPGroupID = Filter.GroupId.Value;
        }

        if (MemberInput.KPGroupID <= 0 || MemberInput.EmployeeID <= 0)
        {
            SetMessage("Group and Employee are required.", "warning");
            return RedirectToPage("./InventoryParametersMember", BuildRouteValues());
        }

        using var conn = OpenConnection();
        if (!GroupExists(conn, MemberInput.KPGroupID))
        {
            SetMessage("Inventory group is invalid.", "warning");
            return RedirectToPage("/Inventory/InventoryParameters/Index");
        }

        if (!EmployeeExists(conn, MemberInput.EmployeeID))
        {
            SetMessage("Employee is invalid.", "warning");
            return RedirectToPage("./InventoryParametersMember", BuildRouteValuesForMutation(MemberInput.KPGroupID));
        }

        var existingMember = FindMemberByEmployee(conn, MemberInput.EmployeeID, 0);
        if (existingMember != null)
        {
            SetMessage($"This employee is already assigned to group {existingMember.GroupName}.", "warning");
            return RedirectToPage("./InventoryParametersMember", BuildRouteValuesForMutation(MemberInput.KPGroupID));
        }

        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
INSERT INTO dbo.INV_KPGroupMember (KPGroupID, EmployeeID)
VALUES (@KPGroupID, @EmployeeID);";
        cmd.Parameters.Add("@KPGroupID", SqlDbType.Int).Value = MemberInput.KPGroupID;
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = MemberInput.EmployeeID;
        cmd.ExecuteNonQuery();

        SetMessage("Group member added.", "success");
        return RedirectToPage("./InventoryParametersMember", BuildRouteValuesForMutation(MemberInput.KPGroupID));
    }

    public IActionResult OnPostDeleteMember()
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewList))
        {
            return Redirect("/");
        }

        if (!PagePerm.HasPermission(PermissionDelete))
        {
            SetMessage("You do not have permission to delete group member.", "warning");
            return RedirectToPage("./InventoryParametersMember", BuildRouteValues());
        }

        if (MemberInput.DetailID <= 0)
        {
            SetMessage("Please select a group member.", "warning");
            return RedirectToPage("./InventoryParametersMember", BuildRouteValues());
        }

        using var conn = OpenConnection();
        if (MemberIsInUse(conn, MemberInput.DetailID))
        {
            SetMessage("Group member is in use and cannot be deleted.", "warning");
            return RedirectToPage("./InventoryParametersMember", BuildRouteValues());
        }

        using var cmd = new SqlCommand("DELETE FROM dbo.INV_KPGroupMember WHERE DetailID = @DetailID;", conn);
        cmd.Parameters.Add("@DetailID", SqlDbType.Int).Value = MemberInput.DetailID;
        var affectedRows = cmd.ExecuteNonQuery();

        SetMessage(affectedRows > 0 ? "Group member deleted." : "Group member was not found.", affectedRows > 0 ? "success" : "warning");
        return RedirectToPage("./InventoryParametersMember", BuildRouteValues());
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
        LoadReferenceData();
        SetGroupContext();

        var exportFilter = new InventoryParametersMemberFilter
        {
            GroupId = Filter.GroupId,
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

    private (List<GroupMemberRow> rows, int totalRecords) SearchRows(InventoryParametersMemberFilter filter)
    {
        var rows = new List<GroupMemberRow>();
        var page = filter.Page <= 0 ? 1 : filter.Page;
        var pageSize = NormalizePageSize(filter.PageSize);
        var offset = (page - 1) * pageSize;

        using var conn = OpenConnection();
        using var countCmd = new SqlCommand(@"
SELECT COUNT(1)
FROM dbo.INV_KPGroupMember member
LEFT JOIN dbo.MS_Employee emp ON emp.EmployeeID = member.EmployeeID
LEFT JOIN dbo.MS_Department dep ON dep.DeptID = emp.DeptID
WHERE (@GroupId IS NULL OR member.KPGroupID = @GroupId)
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
    member.DetailID,
    member.KPGroupID,
    ISNULL(kp.KPGroupName, '') AS KPGroupName,
    member.EmployeeID,
    ISNULL(emp.EmployeeCode, '') AS EmployeeCode,
    ISNULL(emp.EmployeeName, '') AS EmployeeName,
    ISNULL(dep.DeptCode, '') AS DeptCode,
    ISNULL(dep.DeptName, '') AS DeptName,
    ISNULL(emp.Position, '') AS Position,
    ISNULL(emp.IsActive, 0) AS IsActive
FROM dbo.INV_KPGroupMember member
LEFT JOIN dbo.INV_KPGroup kp ON kp.KPGroupID = member.KPGroupID
LEFT JOIN dbo.MS_Employee emp ON emp.EmployeeID = member.EmployeeID
LEFT JOIN dbo.MS_Department dep ON dep.DeptID = emp.DeptID
WHERE (@GroupId IS NULL OR member.KPGroupID = @GroupId)
  AND (@DepartmentId IS NULL OR emp.DeptID = @DepartmentId)
  AND (
        @EmployeeKeyword IS NULL
        OR emp.EmployeeCode LIKE '%' + @EmployeeKeyword + '%'
        OR emp.EmployeeName LIKE '%' + @EmployeeKeyword + '%'
      )
  AND (@ActiveStatus IS NULL OR emp.IsActive = @ActiveStatus)
ORDER BY kp.KPGroupName, emp.EmployeeCode, emp.EmployeeName
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", conn);

        BindSearchParams(cmd, filter);
        cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
        cmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(new GroupMemberRow
            {
                DetailID = Convert.ToInt32(rd["DetailID"]),
                StoreGroupId = Convert.ToInt32(rd["KPGroupID"]),
                StoreGroupName = Convert.ToString(rd["KPGroupName"]) ?? string.Empty,
                EmployeeId = Convert.ToInt32(rd["EmployeeID"]),
                EmployeeCode = Convert.ToString(rd["EmployeeCode"]) ?? string.Empty,
                EmployeeName = Convert.ToString(rd["EmployeeName"]) ?? string.Empty,
                DeptCode = Convert.ToString(rd["DeptCode"]) ?? string.Empty,
                DeptName = Convert.ToString(rd["DeptName"]) ?? string.Empty,
                Position = Convert.ToString(rd["Position"]) ?? string.Empty,
                IsActive = Convert.ToBoolean(rd["IsActive"])
            });
        }

        return (rows, totalRecords);
    }

    private void LoadReferenceData()
    {
        StoreGroupList = LoadListFromSql(
            @"SELECT KPGroupID AS StoreGroupID, ISNULL(KPGroupName, CONCAT('(Group #', KPGroupID, ')')) AS StoreGroupName
              FROM dbo.INV_KPGroup
              ORDER BY KPGroupName",
            "StoreGroupID",
            "StoreGroupName",
            false);

        DepartmentList = LoadListFromSql(
            @"SELECT
                    dep.DeptID,
                    CASE
                        WHEN NULLIF(dep.DeptCode, '') IS NOT NULL AND NULLIF(dep.DeptName, '') IS NOT NULL THEN CONCAT(dep.DeptCode, ' - ', dep.DeptName)
                        WHEN NULLIF(dep.DeptCode, '') IS NOT NULL THEN dep.DeptCode
                        ELSE ISNULL(dep.DeptName, '')
                    END AS DepartmentText
              FROM dbo.MS_Department dep
              ORDER BY dep.DeptCode, dep.DeptName",
            "DeptID",
            "DepartmentText",
            true);

    }

    private List<SelectListItem> LoadEmployeeList(int currentEmployeeId = 0)
    {
        var rows = new List<SelectListItem>();

        using var conn = OpenConnection();
        using var cmd = new SqlCommand(@"
SELECT emp.EmployeeID, emp.EmployeeCode, emp.EmployeeName
FROM dbo.MS_Employee emp
WHERE (@CurrentEmployeeID > 0 AND emp.EmployeeID = @CurrentEmployeeID)
   OR NOT EXISTS (
        SELECT 1
        FROM dbo.INV_KPGroupMember member
        WHERE member.EmployeeID = emp.EmployeeID
   )
ORDER BY emp.EmployeeCode;", conn);

        cmd.Parameters.Add("@CurrentEmployeeID", SqlDbType.Int).Value = currentEmployeeId;

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var employeeCode = Convert.ToString(rd["EmployeeCode"]) ?? string.Empty;
            var employeeName = Convert.ToString(rd["EmployeeName"]) ?? string.Empty;
            rows.Add(new SelectListItem
            {
                Value = Convert.ToString(rd["EmployeeID"]),
                Text = string.IsNullOrWhiteSpace(employeeCode) ? employeeName : $"{employeeCode} - {employeeName}"
            });
        }

        return rows;
    }

    private void SetGroupContext()
    {
        SelectedGroupName = StoreGroupList.FirstOrDefault(item => item.Value == Filter.GroupId?.ToString())?.Text ?? string.Empty;
        MemberInput.KPGroupID = Filter.GroupId ?? 0;
    }

    private IActionResult ExportRows(IReadOnlyList<GroupMemberRow> rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Inventory Group Members");
        var headers = new[] { "STT", "Store Group", "Employee Code", "Employee Name", "Department", "Position", "Active" };

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
            $"inventory_group_members_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
    }

    private static void BindSearchParams(SqlCommand cmd, InventoryParametersMemberFilter filter)
    {
        cmd.Parameters.Add("@GroupId", SqlDbType.Int).Value = filter.GroupId.HasValue ? filter.GroupId.Value : DBNull.Value;
        cmd.Parameters.Add("@DepartmentId", SqlDbType.Int).Value =
            filter.DepartmentId.HasValue ? filter.DepartmentId.Value : DBNull.Value;
        cmd.Parameters.Add("@EmployeeKeyword", SqlDbType.VarChar, 100).Value =
            string.IsNullOrWhiteSpace(filter.EmployeeKeyword) ? DBNull.Value : filter.EmployeeKeyword.Trim();
        cmd.Parameters.Add("@ActiveStatus", SqlDbType.Bit).Value =
            filter.ActiveStatus.HasValue ? filter.ActiveStatus.Value : DBNull.Value;
    }

    private bool GroupExists(SqlConnection conn, int groupId)
    {
        using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.INV_KPGroup WHERE KPGroupID = @GroupId;", conn);
        cmd.Parameters.Add("@GroupId", SqlDbType.Int).Value = groupId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private bool EmployeeExists(SqlConnection conn, int employeeId)
    {
        using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.MS_Employee WHERE EmployeeID = @EmployeeID;", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private bool MemberIsInUse(SqlConnection conn, int detailId)
    {
        return HasForeignKeyReferences(conn, "dbo", "INV_KPGroupMember", "DetailID", detailId);
    }

    private static bool HasForeignKeyReferences(SqlConnection conn, string schemaName, string tableName, string columnName, int keyValue)
    {
        var references = new List<(string SchemaName, string TableName, string ColumnName)>();
        using (var cmd = new SqlCommand(@"
SELECT
    parentSchema.name AS SchemaName,
    parentTable.name AS TableName,
    parentColumn.name AS ColumnName
FROM sys.foreign_key_columns fkc
INNER JOIN sys.tables parentTable ON parentTable.object_id = fkc.parent_object_id
INNER JOIN sys.schemas parentSchema ON parentSchema.schema_id = parentTable.schema_id
INNER JOIN sys.columns parentColumn ON parentColumn.object_id = fkc.parent_object_id
    AND parentColumn.column_id = fkc.parent_column_id
INNER JOIN sys.tables referencedTable ON referencedTable.object_id = fkc.referenced_object_id
INNER JOIN sys.schemas referencedSchema ON referencedSchema.schema_id = referencedTable.schema_id
INNER JOIN sys.columns referencedColumn ON referencedColumn.object_id = fkc.referenced_object_id
    AND referencedColumn.column_id = fkc.referenced_column_id
WHERE referencedSchema.name = @SchemaName
  AND referencedTable.name = @TableName
  AND referencedColumn.name = @ColumnName;", conn))
        {
            cmd.Parameters.Add("@SchemaName", SqlDbType.NVarChar, 128).Value = schemaName;
            cmd.Parameters.Add("@TableName", SqlDbType.NVarChar, 128).Value = tableName;
            cmd.Parameters.Add("@ColumnName", SqlDbType.NVarChar, 128).Value = columnName;

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                references.Add((
                    Convert.ToString(rd["SchemaName"]) ?? string.Empty,
                    Convert.ToString(rd["TableName"]) ?? string.Empty,
                    Convert.ToString(rd["ColumnName"]) ?? string.Empty));
            }
        }

        foreach (var reference in references)
        {
            if (HasTableReference(conn, reference.SchemaName, reference.TableName, reference.ColumnName, keyValue))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasTableReference(SqlConnection conn, string schemaName, string tableName, string columnName, int keyValue)
    {
        var sql = $"SELECT TOP (1) 1 FROM {QuoteSqlIdentifier(schemaName)}.{QuoteSqlIdentifier(tableName)} WHERE {QuoteSqlIdentifier(columnName)} = @KeyValue;";
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@KeyValue", SqlDbType.Int).Value = keyValue;
        return cmd.ExecuteScalar() != null;
    }

    private static string QuoteSqlIdentifier(string identifier)
    {
        return $"[{identifier.Replace("]", "]]")}]";
    }

    private ExistingMemberInfo? FindMemberByEmployee(SqlConnection conn, int employeeId, int currentDetailId)
    {
        using var cmd = new SqlCommand(@"
SELECT TOP (1)
    member.DetailID,
    member.KPGroupID,
    ISNULL(kp.KPGroupName, '') AS KPGroupName
FROM dbo.INV_KPGroupMember member
LEFT JOIN dbo.INV_KPGroup kp ON kp.KPGroupID = member.KPGroupID
WHERE member.EmployeeID = @EmployeeID
  AND (@CurrentDetailID = 0 OR member.DetailID <> @CurrentDetailID);", conn);

        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId;
        cmd.Parameters.Add("@CurrentDetailID", SqlDbType.Int).Value = currentDetailId;

        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
        {
            return null;
        }

        return new ExistingMemberInfo
        {
            DetailID = Convert.ToInt32(rd["DetailID"]),
            KPGroupID = Convert.ToInt32(rd["KPGroupID"]),
            GroupName = Convert.ToString(rd["KPGroupName"]) ?? string.Empty
        };
    }

    private void NormalizeFilter()
    {
        Filter.EmployeeKeyword = string.IsNullOrWhiteSpace(Filter.EmployeeKeyword) ? null : Filter.EmployeeKeyword.Trim();
        Filter.PageSize = NormalizePageSize(Filter.PageSize);

        if (Filter.Page <= 0)
        {
            Filter.Page = 1;
        }

        if (Filter.GroupId.HasValue && Filter.GroupId.Value <= 0)
        {
            Filter.GroupId = null;
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
        Filter.GroupId = ParseNullableInt(Request.Query[nameof(Filter.GroupId)].ToString());
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

    private SqlConnection OpenConnection()
    {
        var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        return conn;
    }

    private object BuildRouteValues(int? groupId = null)
    {
        return new
        {
            GroupId = groupId ?? Filter.GroupId,
            Filter.DepartmentId,
            Filter.EmployeeKeyword,
            Filter.ActiveStatus,
            PageNumber = Filter.Page,
            PageSize = Filter.PageSize
        };
    }

    private object BuildRouteValuesForMutation(int groupId)
    {
        return BuildRouteValues(Filter.GroupId.HasValue ? groupId : null);
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

public class InventoryParametersMemberFilter
{
    public int? GroupId { get; set; }
    public int? DepartmentId { get; set; }
    public string? EmployeeKeyword { get; set; }
    public int? ActiveStatus { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class GroupMemberInput
{
    public int DetailID { get; set; }
    public int KPGroupID { get; set; }
    public int EmployeeID { get; set; }
}

public class GroupMemberRow
{
    public int DetailID { get; set; }
    public int StoreGroupId { get; set; }
    public string StoreGroupName { get; set; } = string.Empty;
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string DeptCode { get; set; } = string.Empty;
    public string DeptName { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public bool IsActive { get; set; }

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

public class ExistingMemberInfo
{
    public int DetailID { get; set; }
    public int KPGroupID { get; set; }
    public string GroupName { get; set; } = string.Empty;
}
