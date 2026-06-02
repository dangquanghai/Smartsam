using System.Data;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;

namespace SmartSam.Pages.Inventory.ItemChecking;

public class IndexModel : BasePageModel
{
    private const int FunctionId = 151;
    private const int PermissionViewList = 1;
    private const int PermissionViewDetail = 2;
    private const int PermissionAdd = 3;
    private const int PermissionEdit = 4;
    private readonly PermissionService _permissionService;
    private EmployeeItemCheckingScope _userScope = new();

    public IndexModel(IConfiguration config, PermissionService permissionService) : base(config)
    {
        _permissionService = permissionService;
    }

    public PagePermissions PagePerm { get; private set; } = new();
    [BindProperty(SupportsGet = true)] public ItemCheckingFilter Filter { get; set; } = new();
    public List<SelectListItem> Departments { get; set; } = new();
    public List<SelectListItem> Statuses { get; set; } = new();
    public List<ItemCheckingRow> Rows { get; set; } = new();
    public int DefaultPageSize => _config.GetValue<int?>("AppSettings:DefaultPageSize") ?? 13;
    public IReadOnlyList<int> PageSizeOptions => GetConfiguredPageSizeOptions();
    public int TotalRecords { get; set; }
    public int TotalPages => Filter.PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)Filter.PageSize));
    public int PageStart => TotalRecords == 0 ? 0 : ((Filter.Page - 1) * Filter.PageSize) + 1;
    public int PageEnd => TotalRecords == 0 ? 0 : Math.Min(Filter.Page * Filter.PageSize, TotalRecords);
    public bool HasPreviousPage => Filter.Page > 1;
    public bool HasNextPage => Filter.Page < TotalPages;

    public IActionResult OnGet()
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewList)) return Redirect("/");
        _userScope = LoadUserScope();
        NormalizeFilter();
        LoadLookups();
        LoadRows();
        return Page();
    }

    public IActionResult OnGetExportExcel()
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewList)) return Redirect("/");
        NormalizeFilter();
        LoadRows();
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("ItemChecking");
        ws.Cell(1, 1).Value = "ID"; ws.Cell(1, 2).Value = "PO No"; ws.Cell(1, 3).Value = "Status"; ws.Cell(1, 4).Value = "Checked By";
        ws.Cell(1, 5).Value = "Checked Date"; ws.Cell(1, 6).Value = "Approved By"; ws.Cell(1, 7).Value = "Approved Date";
        ws.Cell(1, 8).Value = "Receiving No";
        for (int i = 0; i < Rows.Count; i++)
        {
            var r = Rows[i];
            ws.Cell(i + 2, 1).Value = r.CheckingId;
            ws.Cell(i + 2, 2).Value = r.PONo;
            ws.Cell(i + 2, 3).Value = r.StatusName;
            ws.Cell(i + 2, 4).Value = r.CheckedBy;
            ws.Cell(i + 2, 5).Value = r.CheckedDate?.ToString("dd/MM/yyyy") ?? "";
            ws.Cell(i + 2, 6).Value = r.ApprovedBy;
            ws.Cell(i + 2, 7).Value = r.ApprovedDate?.ToString("dd/MM/yyyy") ?? "";
            ws.Cell(i + 2, 8).Value = r.ReceiveNo;
        }
        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream(); wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "item_checking.xlsx");
    }

    private void LoadLookups()
    {
        using var conn = OpenConnection();
        using var depCmd = new SqlCommand("SELECT DeptID, DeptName FROM dbo.MS_Department ORDER BY DeptName", conn);
        using var depRd = depCmd.ExecuteReader();
        Departments.Add(new SelectListItem("--- All ---", ""));
        while (depRd.Read()) Departments.Add(new SelectListItem(Convert.ToString(depRd["DeptName"]) ?? "", Convert.ToString(depRd["DeptID"]) ?? ""));
        depRd.Close();
        using var stCmd = new SqlCommand("SELECT CheckingVoucherStatusID, CheckingVoucherStatusName FROM dbo.INV_CheckingVoucherStatus ORDER BY CheckingVoucherStatusID", conn);
        using var stRd = stCmd.ExecuteReader();
        Statuses.Add(new SelectListItem("--- All ---", ""));
        while (stRd.Read()) Statuses.Add(new SelectListItem(Convert.ToString(stRd["CheckingVoucherStatusName"]) ?? "", Convert.ToString(stRd["CheckingVoucherStatusID"]) ?? ""));
    }

    private void LoadRows()
    {
        Rows.Clear();
        using var conn = OpenConnection();
        using (var countCmd = new SqlCommand(@"SELECT COUNT(1)
FROM dbo.INV_RecevingChekingVoucher c
WHERE 1=1
AND (@DeptId IS NULL OR c.DeptChecked = @DeptId)
AND (@StatusId IS NULL OR c.StatusID = @StatusId)
AND (@UseChecked = 0 OR (c.CheckedDate >= @ChkFrom AND c.CheckedDate < DATEADD(DAY,1,@ChkTo)))
AND (@UseApproved = 0 OR (c.ApprovedDate >= @AprFrom AND c.ApprovedDate < DATEADD(DAY,1,@AprTo)))
AND (@Late = 0 OR ((c.ApprovedDate IS NOT NULL AND DATEDIFF(DAY,c.ExpectDate,c.ApprovedDate) > 0)
 OR (c.ApprovedDate IS NULL AND DATEDIFF(DAY,c.ExpectDate,GETDATE()) > 0)))", conn))
        {
            BindFilterParams(countCmd);
            TotalRecords = Convert.ToInt32(countCmd.ExecuteScalar());
        }

        if (TotalRecords > 0 && Filter.Page > TotalPages) Filter.Page = TotalPages;

        using var cmd = new SqlCommand(@"SELECT c.CheckingID, p.PONo, s.CheckingVoucherStatusName,
e2.EmployeeName AS CheckBy, c.CheckedDate, e1.EmployeeName AS ApprovedBy, c.ApprovedDate,
dbo.CollectReceivingFromChecking(c.CheckingID) AS ReceiveNo, c.StatusID
FROM dbo.INV_RecevingChekingVoucher c
LEFT JOIN dbo.PC_PO p ON p.POID = c.POID
INNER JOIN dbo.INV_CheckingVoucherStatus s ON c.StatusID = s.CheckingVoucherStatusID
LEFT JOIN dbo.MS_Employee e1 ON c.ApprovedBy = e1.EmployeeID
LEFT JOIN dbo.MS_Employee e2 ON c.CheckedBy = e2.EmployeeID
WHERE 1=1
AND (@DeptId IS NULL OR c.DeptChecked = @DeptId)
AND (@StatusId IS NULL OR c.StatusID = @StatusId)
AND (@UseChecked = 0 OR (c.CheckedDate >= @ChkFrom AND c.CheckedDate < DATEADD(DAY,1,@ChkTo)))
AND (@UseApproved = 0 OR (c.ApprovedDate >= @AprFrom AND c.ApprovedDate < DATEADD(DAY,1,@AprTo)))
AND (@Late = 0 OR ((c.ApprovedDate IS NOT NULL AND DATEDIFF(DAY,c.ExpectDate,c.ApprovedDate) > 0)
 OR (c.ApprovedDate IS NULL AND DATEDIFF(DAY,c.ExpectDate,GETDATE()) > 0)))
ORDER BY c.CheckingID DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", conn);
        BindFilterParams(cmd);
        cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = (Filter.Page - 1) * Filter.PageSize;
        cmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = Filter.PageSize;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            Rows.Add(new ItemCheckingRow
            {
                CheckingId = Convert.ToInt32(rd["CheckingID"]),
                PONo = rd["PONo"] == DBNull.Value ? "" : Convert.ToString(rd["PONo"]) ?? "",
                StatusName = Convert.ToString(rd["CheckingVoucherStatusName"]) ?? "",
                CheckedBy = rd["CheckBy"] == DBNull.Value ? "" : Convert.ToString(rd["CheckBy"]) ?? "",
                CheckedDate = rd["CheckedDate"] == DBNull.Value ? null : Convert.ToDateTime(rd["CheckedDate"]),
                ApprovedBy = rd["ApprovedBy"] == DBNull.Value ? "" : Convert.ToString(rd["ApprovedBy"]) ?? "",
                ApprovedDate = rd["ApprovedDate"] == DBNull.Value ? null : Convert.ToDateTime(rd["ApprovedDate"]),
                ReceiveNo = rd["ReceiveNo"] == DBNull.Value ? "" : Convert.ToString(rd["ReceiveNo"]) ?? "",
                StatusId = rd["StatusID"] == DBNull.Value ? 0 : Convert.ToInt32(rd["StatusID"])
            });
        }
    }

    private void NormalizeFilter()
    {
        Filter.Page = Filter.Page <= 0 ? 1 : Filter.Page;
        Filter.PageSize = NormalizePageSize(Filter.PageSize);
        Filter.CheckedFrom ??= DateTime.Today.AddDays(-30);
        Filter.CheckedTo ??= DateTime.Today;
        Filter.ApprovedFrom ??= DateTime.Today.AddDays(-30);
        Filter.ApprovedTo ??= DateTime.Today;
    }
    private void BindFilterParams(SqlCommand cmd)
    {
        cmd.Parameters.Add("@DeptId", SqlDbType.Int).Value = Filter.DeptId ?? (object)DBNull.Value;
        cmd.Parameters.Add("@StatusId", SqlDbType.Int).Value = Filter.StatusId ?? (object)DBNull.Value;
        cmd.Parameters.Add("@UseChecked", SqlDbType.Bit).Value = Filter.UseCheckedDate;
        cmd.Parameters.Add("@ChkFrom", SqlDbType.DateTime).Value = Filter.CheckedFrom ?? DateTime.Today.AddDays(-30);
        cmd.Parameters.Add("@ChkTo", SqlDbType.DateTime).Value = Filter.CheckedTo ?? DateTime.Today;
        cmd.Parameters.Add("@UseApproved", SqlDbType.Bit).Value = Filter.UseApprovedDate;
        cmd.Parameters.Add("@AprFrom", SqlDbType.DateTime).Value = Filter.ApprovedFrom ?? DateTime.Today.AddDays(-30);
        cmd.Parameters.Add("@AprTo", SqlDbType.DateTime).Value = Filter.ApprovedTo ?? DateTime.Today;
        cmd.Parameters.Add("@Late", SqlDbType.Bit).Value = Filter.LateChecking;
    }
    private IReadOnlyList<int> GetConfiguredPageSizeOptions()
    {
        var configured = _config.GetSection("AppSettings:PageSizeOptions").Get<int[]>() ?? Array.Empty<int>();
        var normalized = configured.Where(x => x > 0).Distinct().OrderBy(x => x).ToList();
        if (!normalized.Contains(DefaultPageSize)) normalized.Add(DefaultPageSize);
        normalized.Sort();
        return normalized;
    }
    private int NormalizePageSize(int pageSize) => PageSizeOptions.Contains(pageSize) ? pageSize : DefaultPageSize;
    private SqlConnection OpenConnection() { var c = new SqlConnection(_config.GetConnectionString("DefaultConnection")); c.Open(); return c; }
    private PagePermissions GetUserPermissions() => IsAdminRole() ? new PagePermissions { AllowedNos = Enumerable.Range(1, 20).ToList() } : new PagePermissions { AllowedNos = _permissionService.GetPermissionsForPage(GetCurrentRoleId(), FunctionId) };
    private int GetCurrentRoleId() => int.TryParse(User.FindFirst("RoleID")?.Value, out var roleId) ? roleId : 0;
    private bool IsAdminRole() => User.FindFirst("IsAdminRole")?.Value == "True";
    //public bool CanAdd => IsAdminRole() && !_userScope.IsHeadDept && PagePerm.HasPermission(PermissionAdd);
    public bool CanAdd => IsAdminRole() || PagePerm.HasPermission(PermissionAdd);
    public bool CanEdit => PagePerm.HasPermission(PermissionEdit);
    public bool CanView => PagePerm.HasPermission(PermissionViewDetail);
    public bool CanEditRow(int statusId)
    {
        if (!PagePerm.HasPermission(PermissionEdit)) return false;
        return statusId switch
        {
            1 => true,
            2 => _userScope.IsHeadDept,
            _ => false
        };
    }
    private EmployeeItemCheckingScope LoadUserScope()
    {
        var scope = new EmployeeItemCheckingScope();
        var employeeCode = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(employeeCode)) return scope;
        using var conn = OpenConnection();
        using var cmd = new SqlCommand(@"SELECT TOP 1 ISNULL(HeadDept,0) AS IsHeadDept, ISNULL(IsAdminUser,0) AS IsAdminUser, DeptID FROM dbo.MS_Employee WHERE EmployeeCode=@EmployeeCode", conn);
        cmd.Parameters.Add("@EmployeeCode", SqlDbType.VarChar, 10).Value = employeeCode.Trim();
        using var rd = cmd.ExecuteReader();
        if (rd.Read())
        {
            scope.IsHeadDept = !rd.IsDBNull(0) && Convert.ToInt32(rd[0]) == 1;
            scope.IsAdminUser = !rd.IsDBNull(1) && Convert.ToBoolean(rd[1]);
            scope.DeptId = rd.IsDBNull(2) ? null : Convert.ToInt32(rd[2]);
        }
        return scope;
    }
}

public class ItemCheckingFilter
{
    public int? DeptId { get; set; }
    public int? StatusId { get; set; }
    public bool UseCheckedDate { get; set; } = false;
    public DateTime? CheckedFrom { get; set; }
    public DateTime? CheckedTo { get; set; }
    public bool UseApprovedDate { get; set; } = false;
    public DateTime? ApprovedFrom { get; set; }
    public DateTime? ApprovedTo { get; set; }
    public bool LateChecking { get; set; } = false;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 13;
}

public class ItemCheckingRow
{
    public int CheckingId { get; set; }
    public string PONo { get; set; } = "";
    public string StatusName { get; set; } = "";
    public string CheckedBy { get; set; } = "";
    public DateTime? CheckedDate { get; set; }
    public string ApprovedBy { get; set; } = "";
    public DateTime? ApprovedDate { get; set; }
    public string ReceiveNo { get; set; } = "";
    public int StatusId { get; set; }
}

public class EmployeeItemCheckingScope
{
    public bool IsHeadDept { get; set; }
    public bool IsAdminUser { get; set; }
    public int? DeptId { get; set; }
}
