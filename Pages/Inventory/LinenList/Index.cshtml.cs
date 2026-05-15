using System.Data;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;

namespace SmartSam.Pages.Inventory.LinenList;

public class IndexModel : BasePageModel
{
    private const string ExcelVniFontName = "VNI-WIN";
    private const int FunctionId = 114;
    private const int PermissionView = 1;
    private const int PermissionUpdate = 2;

    private readonly PermissionService _permissionService;

    public IndexModel(IConfiguration config, PermissionService permissionService) : base(config)
    {
        _permissionService = permissionService;
    }

    public PagePermissions PagePerm { get; private set; } = new PagePermissions();
    public int DefaultPageSize => _config.GetValue<int?>("AppSettings:DefaultPageSize") ?? 13;
    public List<LinenListRow> Rows { get; set; } = new List<LinenListRow>();

    [BindProperty(SupportsGet = true)]
    public LinenListFilter Filter { get; set; } = new LinenListFilter();

    public IActionResult OnGet()
    {
        PagePerm = GetUserPermissions();
        if (!HasPageAccess())
        {
            return Redirect("/");
        }

        NormalizeFilter(Filter);
        return Page();
    }

    public IActionResult OnPostSearch([FromBody] LinenListSearchRequest request)
    {
        PagePerm = GetUserPermissions();
        if (!HasPageAccess())
        {
            return new JsonResult(new { success = false, message = "You do not have permission to view Linen List." })
            {
                StatusCode = 401
            };
        }

        NormalizeFilter(request);
        var (rows, totalRecords) = SearchRows(request);
        var canAccess = PagePerm.HasPermission(PermissionView) || PagePerm.HasPermission(PermissionUpdate);
        var canEdit = PagePerm.HasPermission(PermissionUpdate);

        var dataWithActions = rows.Select(row => new
        {
            data = row,
            actions = new
            {
                canAccess,
                accessMode = canEdit ? "edit" : "view",
                canView = canAccess,
                canEdit
            }
        });

        return new JsonResult(new
        {
            success = true,
            data = dataWithActions,
            total = totalRecords,
            page = request.Page,
            pageSize = request.PageSize,
            totalPages = totalRecords == 0 ? 1 : (int)Math.Ceiling((double)totalRecords / request.PageSize)
        });
    }

    public IActionResult OnGetExportExcel([FromQuery] LinenListFilter filter)
    {
        PagePerm = GetUserPermissions();
        if (!HasPageAccess())
        {
            return Redirect("/");
        }

        NormalizeFilter(filter);
        filter.Page = 1;
        filter.PageSize = int.MaxValue;

        var (rows, _) = SearchRows(filter);
        return ExportRows(rows);
    }

    private (List<LinenListRow> rows, int totalRecords) SearchRows(LinenListFilter filter)
    {
        var rows = new List<LinenListRow>();
        var condition = BuildLegacyCondition(filter);
        var page = filter.Page <= 0 ? 1 : filter.Page;
        var pageSize = filter.PageSize <= 0 ? DefaultPageSize : filter.PageSize;
        var offset = pageSize == int.MaxValue ? 0 : (page - 1) * pageSize;

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        using var countCmd = new SqlCommand($@"
SELECT COUNT(1)
FROM dbo.LN_Linnen
WHERE {condition};", conn);
        AddSearchParameters(countCmd, filter);
        var totalRecords = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);

        var pagingSql = pageSize == int.MaxValue
            ? string.Empty
            : "OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        using var cmd = new SqlCommand($@"
SELECT ID, LinnenCode, IsLinen, IsUniform, VNDPriceNew3 AS [EcoWash HCMC], Regular, IsOrder
FROM dbo.LN_Linnen
WHERE {condition}
ORDER BY LinnenCode
{pagingSql};", conn);
        AddSearchParameters(cmd, filter);

        if (pageSize != int.MaxValue)
        {
            cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
            cmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;
        }

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(ReadRow(rd));
        }

        return (rows, totalRecords);
    }

    private static LinenListRow ReadRow(SqlDataReader rd)
    {
        return new LinenListRow
        {
            ID = Convert.ToInt32(rd["ID"]),
            LinnenCode = Convert.ToString(rd["LinnenCode"]) ?? string.Empty,
            IsLinen = ToBool(rd["IsLinen"]),
            IsUniform = ToBool(rd["IsUniform"]),
            EcoWashHcmc = Convert.ToString(rd["EcoWash HCMC"])?.Trim() ?? string.Empty,
            Regular = ToBool(rd["Regular"]),
            IsOrder = ToBool(rd["IsOrder"])
        };
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

    private static string BuildLegacyCondition(LinenListFilter filter)
    {
        var conditions = new List<string> { "(1 = 1)" };

        if (filter.IsLinen)
        {
            conditions.Add("IsLinen = 1");
        }
        else
        {
            conditions.Add("(IsLinen = 0 OR IsLinen IS NULL)");
        }

        if (filter.IsUniform)
        {
            conditions.Add("IsUniform = 1");
        }
        else
        {
            conditions.Add("IsUniform = 0");
        }

        if (filter.IsRegular)
        {
            conditions.Add("Regular = 1");
        }
        else
        {
            conditions.Add("Regular = 0");
        }

        if (filter.IsService)
        {
            conditions.Add("Regular = 0 AND IsLinen = 0");
        }

        if (!string.IsNullOrWhiteSpace(filter.LinenCode))
        {
            conditions.Add("ISNULL(LinnenCode, '') LIKE @LinenCode");
        }

        return string.Join(" AND ", conditions);
    }

    private static void AddSearchParameters(SqlCommand cmd, LinenListFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.LinenCode))
        {
            cmd.Parameters.Add("@LinenCode", SqlDbType.VarChar, 50).Value = "%" + filter.LinenCode.Trim() + "%";
        }
    }

    private IActionResult ExportRows(IReadOnlyList<LinenListRow> rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Linen List");
        var headers = new[]
        {
            "LinnenCode",
            "IsLinen",
            "IsUniform",
            "EcoWash HCMC",
            "Regular",
            "IsOrder"
        };

        for (var col = 0; col < headers.Length; col++)
        {
            worksheet.Cell(1, col + 1).Value = headers[col];
        }

        var rowIndex = 2;
        foreach (var row in rows)
        {
            worksheet.Cell(rowIndex, 1).Value = row.LinnenCode;
            worksheet.Cell(rowIndex, 2).Value = row.IsLinen;
            worksheet.Cell(rowIndex, 3).Value = row.IsUniform;
            worksheet.Cell(rowIndex, 4).Value = row.EcoWashHcmc;
            worksheet.Cell(rowIndex, 5).Value = row.Regular;
            worksheet.Cell(rowIndex, 6).Value = row.IsOrder;
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
            $"linen_list_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
    }

    private PagePermissions GetUserPermissions()
    {
        var isAdmin = User.FindFirst("IsAdminRole")?.Value == "True";
        var roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "0");
        var permsObj = new PagePermissions();

        if (isAdmin)
        {
            permsObj.AllowedNos = Enumerable.Range(1, 20).ToList();
        }
        else
        {
            permsObj.AllowedNos = _permissionService.GetPermissionsForPage(roleId, FunctionId);
        }

        return permsObj;
    }

    private bool HasPageAccess()
    {
        return PagePerm.HasPermission(PermissionView) || PagePerm.HasPermission(PermissionUpdate);
    }

    private void NormalizeFilter(LinenListFilter filter)
    {
        filter.LinenCode = (filter.LinenCode ?? string.Empty).Trim();

        if (filter.Page <= 0)
        {
            filter.Page = 1;
        }

        if (filter.PageSize <= 0)
        {
            filter.PageSize = DefaultPageSize;
        }
    }
}

public class LinenListFilter
{
    public string LinenCode { get; set; } = string.Empty;
    public bool IsLinen { get; set; } = true;
    public bool IsUniform { get; set; }
    public bool IsRegular { get; set; }
    public bool IsService { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 13;
}

public class LinenListSearchRequest : LinenListFilter
{
}

public class LinenListRow
{
    public int ID { get; set; }
    public string LinnenCode { get; set; } = string.Empty;
    public bool IsLinen { get; set; }
    public bool IsUniform { get; set; }
    public string EcoWashHcmc { get; set; } = string.Empty;
    public bool Regular { get; set; }
    public bool IsOrder { get; set; }
}
