using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;

namespace SmartSam.Pages.Inventory.LinnenNoteDaily;

public class IndexModel : BasePageModel
{
    private const int FunctionId = 115;
    private const int PermissionView = 1;
    private const int PermissionUpdate = 2;
    private readonly PermissionService _permissionService;

    public IndexModel(IConfiguration config, PermissionService permissionService) : base(config)
    {
        _permissionService = permissionService;
    }

    public PagePermissions PagePerm { get; private set; } = new PagePermissions();
    public int DefaultPageSize => _config.GetValue<int?>("AppSettings:DefaultPageSize") ?? 13;
    public bool CanAdd => PagePerm.HasPermission(PermissionUpdate);

    [BindProperty(SupportsGet = true)]
    public LinnenNoteDailyFilter Filter { get; set; } = new LinnenNoteDailyFilter();

    public List<LinnenNoteDailyRow> Rows { get; set; } = new List<LinnenNoteDailyRow>();

    public IActionResult OnGet()
    {
        PagePerm = GetUserPermissions();
        if (!HasPageAccess())
        {
            return Redirect("/");
        }

        NormalizeFilter();
        ViewData["DefaultPageSize"] = DefaultPageSize;
        return Page();
    }

    public IActionResult OnPostSearch([FromBody] LinnenNoteDailySearchRequest request)
    {
        try
        {
            request ??= new LinnenNoteDailySearchRequest();
            PagePerm = GetUserPermissions();
            if (!HasPageAccess())
            {
                return new JsonResult(new { success = false, message = "Forbidden" })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }

            NormalizeSearchRequest(request);
            var (rows, totalRecords) = SearchRows(request);
            var totalPages = request.PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(totalRecords / (double)request.PageSize));

            var effectivePerms = PagePerm.AllowedNos;
            var dataWithActions = rows.Select(row => new
            {
                data = row,
                actions = new
                {
                    canAccess = effectivePerms.Contains(PermissionView) || effectivePerms.Contains(PermissionUpdate),
                    accessMode = effectivePerms.Contains(PermissionUpdate) && !row.IsClose ? "edit" : "view",
                    canView = effectivePerms.Contains(PermissionView) || effectivePerms.Contains(PermissionUpdate),
                    canEdit = effectivePerms.Contains(PermissionUpdate) && !row.IsClose
                }
            });

            return new JsonResult(new
            {
                success = true,
                data = dataWithActions,
                total = totalRecords,
                page = request.Page,
                pageSize = request.PageSize,
                totalPages
            });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, message = ex.Message });
        }
    }

    private (List<LinnenNoteDailyRow> rows, int totalRecords) SearchRows(LinnenNoteDailySearchRequest request)
    {
        var rows = new List<LinnenNoteDailyRow>();
        var where = new List<string> { "1 = 1" };
        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = NormalizePageSize(request.PageSize);
        var offset = (page - 1) * pageSize;
        var totalRecords = 0;

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            where.Add("mt.Des LIKE '%' + @Description + '%'");
        }
        if (request.FromDate.HasValue)
        {
            where.Add("CAST(mt.DateCreate AS date) >= @FromDate");
        }
        if (request.ToDate.HasValue)
        {
            where.Add("CAST(mt.DateCreate AS date) <= @ToDate");
        }

        var whereSql = string.Join(" AND ", where);

        using var conn = OpenConnection();
        using (var countCmd = new SqlCommand($@"
SELECT COUNT(1)
FROM dbo.LN_DeAndReMT mt
WHERE {whereSql};", conn))
        {
            BindFilterParams(countCmd, request);
            totalRecords = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);
        }

        using (var cmd = new SqlCommand($@"
SELECT mt.ID,
       ISNULL(mt.Des, '') AS Des,
       mt.DateCreate,
       ISNULL(mt.IsClose, 0) AS IsClose,
       ISNULL(mt.Start, 0) AS Start,
       ISNULL(mt.IsRent, 0) AS IsRent,
       COUNT(dt.ID) AS DetailCount
FROM dbo.LN_DeAndReMT mt
LEFT JOIN dbo.LN_DeAndRe_DT dt ON mt.ID = dt.IDDeAndRe
WHERE {whereSql}
GROUP BY mt.ID, mt.Des, mt.DateCreate, mt.IsClose, mt.Start, mt.IsRent
ORDER BY mt.DateCreate DESC, mt.ID DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", conn))
        {
            BindFilterParams(cmd, request);
            cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
            cmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                rows.Add(new LinnenNoteDailyRow
                {
                    Id = Convert.ToInt32(rd["ID"]),
                    Description = Convert.ToString(rd["Des"]) ?? string.Empty,
                    DateCreate = rd["DateCreate"] == DBNull.Value ? null : Convert.ToDateTime(rd["DateCreate"]),
                    IsClose = ToBool(rd["IsClose"]),
                    Start = ToBool(rd["Start"]),
                    IsRent = ToBool(rd["IsRent"]),
                    DetailCount = Convert.ToInt32(rd["DetailCount"])
                });
            }
        }

        return (rows, totalRecords);
    }

    private static void BindFilterParams(SqlCommand cmd, LinnenNoteDailySearchRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            cmd.Parameters.Add("@Description", SqlDbType.VarChar, 100).Value = request.Description.Trim();
        }
        if (request.FromDate.HasValue)
        {
            cmd.Parameters.Add("@FromDate", SqlDbType.Date).Value = request.FromDate.Value.Date;
        }
        if (request.ToDate.HasValue)
        {
            cmd.Parameters.Add("@ToDate", SqlDbType.Date).Value = request.ToDate.Value.Date;
        }
    }

    private void NormalizeFilter()
    {
        Filter.Description = (Filter.Description ?? string.Empty).Trim();

        if (!Filter.FromDate.HasValue)
        {
            Filter.FromDate = DateTime.Today;
        }

        if (!Filter.ToDate.HasValue)
        {
            Filter.ToDate = DateTime.Today;
        }
    }

    private void NormalizeSearchRequest(LinnenNoteDailySearchRequest request)
    {
        request.Description = (request.Description ?? string.Empty).Trim();

        if (!request.FromDate.HasValue)
        {
            request.FromDate = DateTime.Today;
        }

        if (!request.ToDate.HasValue)
        {
            request.ToDate = DateTime.Today;
        }

        request.Page = request.Page <= 0 ? 1 : request.Page;
        request.PageSize = NormalizePageSize(request.PageSize);
    }

    private int NormalizePageSize(int pageSize)
    {
        return pageSize <= 0 ? DefaultPageSize : pageSize;
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

    private bool HasPageAccess() => PagePerm.HasPermission(PermissionView) || PagePerm.HasPermission(PermissionUpdate);

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
}

public class LinnenNoteDailyFilter
{
    public string? Description { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}

public class LinnenNoteDailySearchRequest
{
    public string? Description { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 13;
}

public class LinnenNoteDailyRow
{
    public int Id { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime? DateCreate { get; set; }
    public bool IsClose { get; set; }
    public bool Start { get; set; }
    public bool IsRent { get; set; }
    public int DetailCount { get; set; }
    public string DateCreateText => DateCreate.HasValue ? DateCreate.Value.ToString("dd/MM/yyyy") : string.Empty;
}
