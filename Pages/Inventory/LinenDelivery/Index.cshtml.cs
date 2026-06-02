using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;

namespace SmartSam.Pages.Inventory.LinenDelivery;

public class IndexModel : BasePageModel
{
    private const int FunctionId = 116;
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
    public LinenDeliveryFilter Filter { get; set; } = new LinenDeliveryFilter();

    public List<SelectListItem> DeliveryTypeOptions { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> SupplierOptions { get; set; } = new List<SelectListItem>();

    public IActionResult OnGet()
    {
        PagePerm = GetUserPermissions();
        if (!HasPageAccess())
        {
            return Redirect("/");
        }

        NormalizeFilter(Filter);
        PrepareLegacyListData();
        LoadDropdowns();
        ViewData["DefaultPageSize"] = DefaultPageSize;
        return Page();
    }

    public IActionResult OnPostSearch([FromBody] LinenDeliverySearchRequest request)
    {
        request ??= new LinenDeliverySearchRequest();
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

        var dataWithActions = rows.Select(row => new
        {
            data = row,
            actions = new
            {
                canAccess,
                accessMode = canEdit && !row.Closed ? "edit" : "view",
                canView = canAccess,
                canEdit = canEdit && !row.Closed
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

    private void PrepareLegacyListData()
    {
        using var conn = OpenConnection();

        using (var deleteCmd = new SqlCommand(@"
DELETE FROM dbo.LN_DeliveryMT
WHERE DeliveryID NOT IN (
    SELECT DeliveryID
    FROM dbo.LN_DeliveryDT
);", conn))
        {
            deleteCmd.ExecuteNonQuery();
        }

        using var serviceCmd = new SqlCommand("exec LN_SendServiceToLinen", conn);
        serviceCmd.ExecuteNonQuery();
    }

    private (List<LinenDeliveryRow> rows, int totalRecords) SearchRows(LinenDeliverySearchRequest request)
    {
        var rows = new List<LinenDeliveryRow>();
        var conditions = new List<string>
        {
            "mt.DeliveryDate >= @FromDate",
            "mt.DeliveryDate <= @ToDate",
            "ISNULL(mt.Closed, 0) = @Closed",
            "ISNULL(mt.IsRent, 0) = @IsRent"
        };

        if (request.DeliveryId.HasValue)
        {
            conditions.Add("mt.DeliveryID = @DeliveryID");
        }

        if (request.DeliveryTypeId.HasValue && request.DeliveryTypeId.Value > 0)
        {
            conditions.Add("mt.DeliveryType = @DeliveryTypeID");
        }

        if (request.SupplierId.HasValue && request.SupplierId.Value > 0)
        {
            conditions.Add("mt.SupplierID = @SupplierID");
        }

        var whereSql = string.Join(" AND ", conditions);
        var page = request.Page <= 0 ? 1 : request.Page;
        var pageSize = NormalizePageSize(request.PageSize);
        var offset = (page - 1) * pageSize;
        var totalRecords = 0;

        using var conn = OpenConnection();

        using (var countCmd = new SqlCommand($@"
SELECT COUNT(1)
FROM dbo.LN_DeliveryMT mt
INNER JOIN dbo.PC_Suppliers s ON mt.SupplierID = s.SupplierID
LEFT JOIN dbo.LN_LaudryType t ON mt.DeliveryType = t.LaundryTypeID
WHERE {whereSql};", conn))
        {
            BindSearchParams(countCmd, request);
            totalRecords = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);
        }

        using (var cmd = new SqlCommand($@"
SELECT mt.DeliveryID,
       mt.DeliveryDate,
       ISNULL(mt.Des, '') AS Des,
       ISNULL(mt.Closed, 0) AS Closed,
       ISNULL(mt.DeliveryType, 0) AS DeliveryType,
       ISNULL(mt.NoteID, 0) AS NoteID,
       ISNULL(mt.SupplierID, 0) AS SupplierID,
       ISNULL(s.SupplierName, '') AS SupplierName,
       ISNULL(t.LaundryTypeName, '') AS LaundryTypeName,
       ISNULL(mt.IsRent, 0) AS IsRent
FROM dbo.LN_DeliveryMT mt
INNER JOIN dbo.PC_Suppliers s ON mt.SupplierID = s.SupplierID
LEFT JOIN dbo.LN_LaudryType t ON mt.DeliveryType = t.LaundryTypeID
WHERE {whereSql}
ORDER BY mt.DeliveryDate DESC, mt.DeliveryID DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", conn))
        {
            BindSearchParams(cmd, request);
            cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
            cmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                rows.Add(new LinenDeliveryRow
                {
                    DeliveryID = Convert.ToInt32(rd["DeliveryID"]),
                    DeliveryDate = rd["DeliveryDate"] == DBNull.Value ? null : Convert.ToDateTime(rd["DeliveryDate"]),
                    Description = Convert.ToString(rd["Des"]) ?? string.Empty,
                    Closed = ToBool(rd["Closed"]),
                    DeliveryType = Convert.ToInt32(rd["DeliveryType"]),
                    NoteID = Convert.ToInt32(rd["NoteID"]),
                    SupplierID = Convert.ToInt32(rd["SupplierID"]),
                    SupplierName = Convert.ToString(rd["SupplierName"]) ?? string.Empty,
                    DeliveryTypeName = Convert.ToString(rd["LaundryTypeName"]) ?? string.Empty,
                    IsRent = ToBool(rd["IsRent"])
                });
            }
        }

        return (rows, totalRecords);
    }

    private void LoadDropdowns()
    {
        DeliveryTypeOptions = new List<SelectListItem>
        {
            new SelectListItem { Value = string.Empty, Text = "-- All --" }
        };
        SupplierOptions = new List<SelectListItem>
        {
            new SelectListItem { Value = string.Empty, Text = "-- All --" }
        };

        using var conn = OpenConnection();

        using (var typeCmd = new SqlCommand(@"
SELECT LaundryTypeID, LaundryTypeName
FROM dbo.LN_LaudryType
WHERE ISNULL(IsActive, 0) = 1
ORDER BY LaundryTypeID;", conn))
        using (var rd = typeCmd.ExecuteReader())
        {
            while (rd.Read())
            {
                DeliveryTypeOptions.Add(new SelectListItem
                {
                    Value = Convert.ToInt32(rd["LaundryTypeID"]).ToString(),
                    Text = Convert.ToString(rd["LaundryTypeName"]) ?? string.Empty
                });
            }
        }

        using (var supplierCmd = new SqlCommand(@"
SELECT SupplierID, SupplierName
FROM dbo.PC_Suppliers
WHERE ISNULL(IsLinen, 0) = 1
ORDER BY SupplierID;", conn))
        using (var rd = supplierCmd.ExecuteReader())
        {
            while (rd.Read())
            {
                SupplierOptions.Add(new SelectListItem
                {
                    Value = Convert.ToInt32(rd["SupplierID"]).ToString(),
                    Text = Convert.ToString(rd["SupplierName"]) ?? string.Empty
                });
            }
        }
    }

    private static void BindSearchParams(SqlCommand cmd, LinenDeliverySearchRequest request)
    {
        cmd.Parameters.Add("@FromDate", SqlDbType.DateTime).Value = request.FromDate!.Value.Date;
        cmd.Parameters.Add("@ToDate", SqlDbType.DateTime).Value = request.ToDate!.Value.Date.AddDays(1).AddTicks(-1);
        cmd.Parameters.Add("@Closed", SqlDbType.Bit).Value = request.Closed;
        cmd.Parameters.Add("@IsRent", SqlDbType.Bit).Value = request.IsRent;

        if (request.DeliveryId.HasValue)
        {
            cmd.Parameters.Add("@DeliveryID", SqlDbType.Int).Value = request.DeliveryId.Value;
        }

        if (request.DeliveryTypeId.HasValue && request.DeliveryTypeId.Value > 0)
        {
            cmd.Parameters.Add("@DeliveryTypeID", SqlDbType.Int).Value = request.DeliveryTypeId.Value;
        }

        if (request.SupplierId.HasValue && request.SupplierId.Value > 0)
        {
            cmd.Parameters.Add("@SupplierID", SqlDbType.Int).Value = request.SupplierId.Value;
        }
    }

    private void NormalizeFilter(LinenDeliveryFilter filter)
    {
        filter.FromDate ??= DateTime.Today.AddDays(-10);
        filter.ToDate ??= DateTime.Today;
    }

    private void NormalizeSearchRequest(LinenDeliverySearchRequest request)
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
        var perms = new PagePermissions();
        perms.AllowedNos = isAdmin
            ? Enumerable.Range(1, 20).ToList()
            : _permissionService.GetPermissionsForPage(roleId, FunctionId);
        return perms;
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

public class LinenDeliveryFilter
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int? DeliveryId { get; set; }
    public int? DeliveryTypeId { get; set; }
    public int? SupplierId { get; set; }
    public bool Closed { get; set; }
    public bool IsRent { get; set; }
}

public class LinenDeliverySearchRequest : LinenDeliveryFilter
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 13;
}

public class LinenDeliveryRow
{
    public int DeliveryID { get; set; }
    public DateTime? DeliveryDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool Closed { get; set; }
    public int DeliveryType { get; set; }
    public int NoteID { get; set; }
    public int SupplierID { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public string DeliveryTypeName { get; set; } = string.Empty;
    public bool IsRent { get; set; }
    public string DeliveryDateText => DeliveryDate.HasValue ? DeliveryDate.Value.ToString("dd/MM/yyyy") : string.Empty;
}
