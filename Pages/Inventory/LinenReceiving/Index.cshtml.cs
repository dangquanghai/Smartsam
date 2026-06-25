using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;

namespace SmartSam.Pages.Inventory.LinenReceiving;

public class IndexModel : BasePageModel
{
    private const int FunctionId = 117;
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
    public LinenReceivingFilter Filter { get; set; } = new LinenReceivingFilter();

    public IActionResult OnGet()
    {
        PagePerm = GetUserPermissions();
        if (!HasPageAccess())
        {
            return Redirect("/");
        }

        NormalizeFilter(Filter);
        ViewData["DefaultPageSize"] = DefaultPageSize;
        return Page();
    }

    public IActionResult OnPostSearch([FromBody] LinenReceivingSearchRequest request)
    {
        request ??= new LinenReceivingSearchRequest();
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
        var canAccess = PagePerm.HasPermission(PermissionView) || PagePerm.HasPermission(PermissionUpdate);
        var canEdit = PagePerm.HasPermission(PermissionUpdate);
        var totalPages = request.PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(totalRecords / (double)request.PageSize));

        return new JsonResult(new
        {
            success = true,
            data = rows.Select(row => new
            {
                data = row,
                actions = new
                {
                    canAccess,
                    accessMode = canEdit && !row.IsLocked ? "edit" : "view",
                    canView = canAccess,
                    canEdit = canEdit && !row.IsLocked
                }
            }),
            total = totalRecords,
            page = request.Page,
            pageSize = request.PageSize,
            totalPages
        });
    }

    public IActionResult OnPostDelete([FromBody] LinenReceivingDeleteRequest request)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionUpdate))
        {
            return new JsonResult(new { success = false, message = "Forbidden" })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }

        if (request.ReceiveId <= 0)
        {
            return new JsonResult(new { success = false, message = "Receive ID is invalid." })
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
        }

        using var conn = OpenConnection();
        using var trans = conn.BeginTransaction();
        try
        {
            DateTime? receiveDate = null;
            int? deliveryId = null;

            using (var headerCmd = new SqlCommand(@"
SELECT ReceiveDate,
       SendID,
       ISNULL([Lock], 0) AS IsLocked
FROM dbo.LN_ReceiveMT
WHERE ReceiveID = @ReceiveID;", conn, trans))
            {
                headerCmd.Parameters.Add("@ReceiveID", SqlDbType.Int).Value = request.ReceiveId;
                using var rd = headerCmd.ExecuteReader();
                if (!rd.Read())
                {
                    trans.Rollback();
                    return new JsonResult(new { success = false, message = "Linen receiving not found." })
                    {
                        StatusCode = StatusCodes.Status404NotFound
                    };
                }

                if (ToBool(rd["IsLocked"]))
                {
                    trans.Rollback();
                    return new JsonResult(new { success = false, message = "Linen receiving is locked and cannot be deleted." })
                    {
                        StatusCode = StatusCodes.Status400BadRequest
                    };
                }

                receiveDate = rd["ReceiveDate"] == DBNull.Value ? null : Convert.ToDateTime(rd["ReceiveDate"]);
                deliveryId = rd["SendID"] == DBNull.Value ? null : Convert.ToInt32(rd["SendID"]);
            }

            using (var detailCmd = new SqlCommand("DELETE FROM dbo.LN_ReceiveDT WHERE ReceiveID = @ReceiveID;", conn, trans))
            {
                detailCmd.Parameters.Add("@ReceiveID", SqlDbType.Int).Value = request.ReceiveId;
                detailCmd.ExecuteNonQuery();
            }

            using (var masterCmd = new SqlCommand("DELETE FROM dbo.LN_ReceiveMT WHERE ReceiveID = @ReceiveID;", conn, trans))
            {
                masterCmd.Parameters.Add("@ReceiveID", SqlDbType.Int).Value = request.ReceiveId;
                masterCmd.ExecuteNonQuery();
            }

            if (deliveryId.HasValue)
            {
                using var markCmd = new SqlCommand("exec LN_MarkFullReceiveOnDelevery @DeliveryID", conn, trans);
                markCmd.Parameters.Add("@DeliveryID", SqlDbType.Int).Value = deliveryId.Value;
                markCmd.ExecuteNonQuery();
            }

            if (receiveDate.HasValue)
            {
                RefreshLaundryRecordForBusinessDate(conn, trans, receiveDate.Value);
            }

            trans.Commit();
        }
        catch
        {
            trans.Rollback();
            throw;
        }

        return new JsonResult(new { success = true });
    }
    private (List<LinenReceivingRow> rows, int totalRecords) SearchRows(LinenReceivingSearchRequest request)
    {
        var rows = new List<LinenReceivingRow>();
        var conditions = new List<string>
        {
            "mt.ReceiveDate >= @FromDate",
            "mt.ReceiveDate <= @ToDate",
            "ISNULL(mt.[Lock], 0) = @Lock"
        };

        if (request.ReceiveId.HasValue)
        {
            conditions.Add("CAST(mt.ReceiveID AS varchar(30)) LIKE @ReceiveID");
        }

        var whereSql = string.Join(" AND ", conditions);
        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = NormalizePageSize(request.PageSize);
        var offset = (page - 1) * pageSize;
        using var conn = OpenConnection();

        int totalRecords;
        using (var countCmd = new SqlCommand($@"
SELECT COUNT(1)
FROM dbo.LN_ReceiveMT mt
INNER JOIN (
    SELECT ReceiveID
    FROM dbo.LN_ReceiveDT
    GROUP BY ReceiveID
) dt ON dt.ReceiveID = mt.ReceiveID
INNER JOIN dbo.LN_DeliveryMT de ON mt.SendID = de.DeliveryID
WHERE {whereSql};", conn))
        {
            BindSearchParams(countCmd, request);
            totalRecords = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);
        }

        using (var cmd = new SqlCommand($@"
SELECT mt.ReceiveID,
       mt.ReceiveDate,
       ISNULL(mt.Des, '') AS Des,
       ISNULL(mt.[Lock], 0) AS IsLocked,
       ISNULL(de.Des, '') AS DeliveryInfor
FROM dbo.LN_ReceiveMT mt
INNER JOIN (
    SELECT ReceiveID
    FROM dbo.LN_ReceiveDT
    GROUP BY ReceiveID
) dt ON dt.ReceiveID = mt.ReceiveID
INNER JOIN dbo.LN_DeliveryMT de ON mt.SendID = de.DeliveryID
WHERE {whereSql}
ORDER BY mt.ReceiveDate DESC, mt.ReceiveID DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", conn))
        {
            BindSearchParams(cmd, request);
            cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
            cmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                rows.Add(new LinenReceivingRow
                {
                    ReceiveID = Convert.ToInt32(rd["ReceiveID"]),
                    ReceiveDate = rd["ReceiveDate"] == DBNull.Value ? null : Convert.ToDateTime(rd["ReceiveDate"]),
                    Description = Convert.ToString(rd["Des"]) ?? string.Empty,
                    IsLocked = ToBool(rd["IsLocked"]),
                    DeliveryInfor = Convert.ToString(rd["DeliveryInfor"]) ?? string.Empty
                });
            }
        }

        return (rows, totalRecords);
    }

    private static void BindSearchParams(SqlCommand cmd, LinenReceivingSearchRequest request)
    {
        cmd.Parameters.Add("@FromDate", SqlDbType.DateTime).Value = request.FromDate!.Value.Date;
        cmd.Parameters.Add("@ToDate", SqlDbType.DateTime).Value = request.ToDate!.Value.Date.AddDays(1).AddTicks(-1);
        cmd.Parameters.Add("@Lock", SqlDbType.Bit).Value = request.IsLocked;

        if (request.ReceiveId.HasValue)
        {
            cmd.Parameters.Add("@ReceiveID", SqlDbType.VarChar, 30).Value = "%" + request.ReceiveId.Value + "%";
        }
    }

    private void RefreshLaundryRecordForBusinessDate(SqlConnection conn, SqlTransaction trans, DateTime businessDate)
    {
        var dayStart = businessDate.Date.AddSeconds(1);
        var dayEnd = businessDate.Date.AddDays(1).AddSeconds(-1);
        var userCode = User.Identity?.Name ?? "SYSTEM";

        using var cmd = new SqlCommand("dbo.LN_LaundryRecordRPT", conn, trans);
        cmd.CommandType = CommandType.StoredProcedure;
        cmd.Parameters.Add("@Month", SqlDbType.Int).Value = businessDate.Month;
        cmd.Parameters.Add("@Year", SqlDbType.Int).Value = businessDate.Year;
        cmd.Parameters.Add("@FromDate", SqlDbType.VarChar, 50).Value = dayStart.ToString("MM/dd/yyyy hh:mm:ss tt");
        cmd.Parameters.Add("@ToDate", SqlDbType.VarChar, 50).Value = dayEnd.ToString("MM/dd/yyyy hh:mm:ss tt");
        cmd.Parameters.Add("@UserCode", SqlDbType.VarChar, 15).Value = userCode;
        cmd.ExecuteNonQuery();
    }
    private void NormalizeFilter(LinenReceivingFilter filter)
    {
        filter.FromDate ??= DateTime.Today.AddDays(-10);
        filter.ToDate ??= DateTime.Today;
    }

    private void NormalizeSearchRequest(LinenReceivingSearchRequest request)
    {
        request.FromDate ??= DateTime.Today.AddDays(-10);
        request.ToDate ??= DateTime.Today;
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

    private bool HasPageAccess()
    {
        return PagePerm.HasPermission(PermissionView) || PagePerm.HasPermission(PermissionUpdate);
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
}

public class LinenReceivingFilter
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int? ReceiveId { get; set; }
    public bool IsLocked { get; set; }
}

public class LinenReceivingSearchRequest : LinenReceivingFilter
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 13;
}

public class LinenReceivingDeleteRequest
{
    public int ReceiveId { get; set; }
}

public class LinenReceivingRow
{
    public int ReceiveID { get; set; }
    public DateTime? ReceiveDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool IsLocked { get; set; }
    public string DeliveryInfor { get; set; } = string.Empty;
    public string ReceiveDateText => ReceiveDate.HasValue ? ReceiveDate.Value.ToString("dd/MM/yyyy") : string.Empty;
}