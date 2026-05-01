using System.Data;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;

namespace SmartSam.Pages.Inventory.InventoryParameters;

public class InventoryParametersStoreModel : BasePageModel
{
    private const string ExcelVniFontName = "VNI-WIN";
    private const int FunctionId = 146;
    private const int PermissionViewList = 1;
    private const int PermissionAdd = 3;
    private const int PermissionEdit = 4;
    private const int PermissionDelete = 5;

    private readonly PermissionService _permissionService;

    public InventoryParametersStoreModel(IConfiguration config, PermissionService permissionService) : base(config)
    {
        _permissionService = permissionService;
    }

    public PagePermissions PagePerm { get; private set; } = new PagePermissions();
    public int DefaultPageSize => _config.GetValue<int?>("AppSettings:DefaultPageSize") ?? 10;
    public IReadOnlyList<int> PageSizeOptions => GetConfiguredPageSizeOptions();
    public bool CanAdd => PagePerm.HasPermission(PermissionAdd);
    public bool CanEdit => PagePerm.HasPermission(PermissionEdit);
    public bool CanDelete => PagePerm.HasPermission(PermissionDelete);

    [BindProperty(SupportsGet = true)]
    public InventoryParametersStoreFilter Filter { get; set; } = new InventoryParametersStoreFilter();

    [BindProperty]
    public StoreHouseInput StoreInput { get; set; } = new StoreHouseInput();

    public List<SelectListItem> StoreGroupList { get; set; } = new List<SelectListItem>();
    public List<StoreHouseRow> Rows { get; set; } = new List<StoreHouseRow>();
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
        LoadEditStore();
        return Page();
    }

    public IActionResult OnPostSaveStore()
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewList))
        {
            return Redirect("/");
        }

        var isEdit = StoreInput.StoreID > 0;
        if (!PagePerm.HasPermission(isEdit ? PermissionEdit : PermissionAdd))
        {
            SetMessage("You do not have permission to save store-house.", "warning");
            return RedirectToPage("./InventoryParametersStore", BuildRouteValues());
        }

        StoreInput.StoreName = (StoreInput.StoreName ?? string.Empty).Trim();
        StoreInput.Address = string.IsNullOrWhiteSpace(StoreInput.Address) ? null : StoreInput.Address.Trim();

        if (!isEdit && Filter.GroupId.HasValue)
        {
            StoreInput.KPGroupID = Filter.GroupId.Value;
        }

        if (string.IsNullOrWhiteSpace(StoreInput.StoreName) || (!isEdit && StoreInput.KPGroupID <= 0))
        {
            SetMessage(isEdit ? "Store Name is required." : "Store Group and Store Name are required.", "warning");
            return RedirectToPage("./InventoryParametersStore", BuildRouteValues());
        }

        if (StoreInput.StoreName.Length > 50)
        {
            SetMessage("Store Name cannot exceed 50 characters.", "warning");
            return RedirectToPage("./InventoryParametersStore", BuildRouteValues());
        }

        if (StoreInput.Address?.Length > 100)
        {
            SetMessage("Address cannot exceed 100 characters.", "warning");
            return RedirectToPage("./InventoryParametersStore", BuildRouteValues());
        }

        using var conn = OpenConnection();
        if (isEdit)
        {
            var currentGroupId = FindStoreGroupId(conn, StoreInput.StoreID);
            if (!currentGroupId.HasValue)
            {
                SetMessage("Store-house is invalid.", "warning");
                return RedirectToPage("./InventoryParametersStore", BuildRouteValues());
            }

            StoreInput.KPGroupID = currentGroupId.Value;
        }

        if (!GroupExists(conn, StoreInput.KPGroupID))
        {
            SetMessage("Inventory group is invalid.", "warning");
            return RedirectToPage("/Inventory/InventoryParameters/Index");
        }

        using var cmd = conn.CreateCommand();
        if (isEdit)
        {
            cmd.CommandText = @"
UPDATE dbo.INV_StoreList
SET StoreName = @StoreName,
    Address = @Address
WHERE StoreID = @StoreID;";
            cmd.Parameters.Add("@StoreID", SqlDbType.Int).Value = StoreInput.StoreID;
        }
        else
        {
            cmd.CommandText = @"
INSERT INTO dbo.INV_StoreList (StoreName, Address, DeptID, IsCoStore)
VALUES (@StoreName, @Address, @KPGroupID, @IsCoStore);";
        }

        cmd.Parameters.Add("@StoreName", SqlDbType.VarChar, 50).Value = StoreInput.StoreName;
        cmd.Parameters.Add("@Address", SqlDbType.VarChar, 100).Value =
            string.IsNullOrWhiteSpace(StoreInput.Address) ? DBNull.Value : StoreInput.Address;
        if (!isEdit)
        {
            cmd.Parameters.Add("@KPGroupID", SqlDbType.Int).Value = StoreInput.KPGroupID;
            cmd.Parameters.Add("@IsCoStore", SqlDbType.Bit).Value = StoreInput.KPGroupID == 1;
        }
        cmd.ExecuteNonQuery();

        SetMessage(isEdit ? "Store-house updated." : "Store-house added.", "success");
        return RedirectToPage("./InventoryParametersStore", BuildRouteValuesForMutation(StoreInput.KPGroupID));
    }

    public IActionResult OnPostDeleteStore()
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewList))
        {
            return Redirect("/");
        }

        if (!PagePerm.HasPermission(PermissionDelete))
        {
            SetMessage("You do not have permission to delete store-house.", "warning");
            return RedirectToPage("./InventoryParametersStore", BuildRouteValues());
        }

        if (StoreInput.StoreID <= 0)
        {
            SetMessage("Please select a store-house.", "warning");
            return RedirectToPage("./InventoryParametersStore", BuildRouteValues());
        }

        using var conn = OpenConnection();
        if (StoreIsInUse(conn, StoreInput.StoreID))
        {
            SetMessage("Store-house is in use and cannot be deleted.", "warning");
            return RedirectToPage("./InventoryParametersStore", BuildRouteValues());
        }

        using var cmd = new SqlCommand("DELETE FROM dbo.INV_StoreList WHERE StoreID = @StoreID;", conn);
        cmd.Parameters.Add("@StoreID", SqlDbType.Int).Value = StoreInput.StoreID;
        var affectedRows = cmd.ExecuteNonQuery();

        SetMessage(affectedRows > 0 ? "Store-house deleted." : "Store-house was not found.", affectedRows > 0 ? "success" : "warning");
        return RedirectToPage("./InventoryParametersStore", BuildRouteValues());
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

        var exportFilter = new InventoryParametersStoreFilter
        {
            GroupId = Filter.GroupId,
            StoreName = Filter.StoreName,
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

    private (List<StoreHouseRow> rows, int totalRecords) SearchRows(InventoryParametersStoreFilter filter)
    {
        var rows = new List<StoreHouseRow>();
        var page = filter.Page <= 0 ? 1 : filter.Page;
        var pageSize = NormalizePageSize(filter.PageSize);
        var offset = (page - 1) * pageSize;

        using var conn = OpenConnection();
        using var countCmd = new SqlCommand(@"
SELECT COUNT(1)
FROM dbo.INV_StoreList store
WHERE (@GroupId IS NULL OR store.DeptID = @GroupId)
  AND (@StoreName IS NULL OR store.StoreName LIKE '%' + @StoreName + '%');", conn);

        BindSearchParams(countCmd, filter);
        var totalRecords = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);

        using var cmd = new SqlCommand(@"
SELECT
    store.StoreID,
    ISNULL(store.StoreName, '') AS StoreName,
    ISNULL(store.Address, '') AS Address,
    store.DeptID AS KPGroupID,
    ISNULL(kp.KPGroupName, '') AS KPGroupName,
    ISNULL(store.IsCoStore, 0) AS IsCoStore
FROM dbo.INV_StoreList store
LEFT JOIN dbo.INV_KPGroup kp ON kp.KPGroupID = store.DeptID
WHERE (@GroupId IS NULL OR store.DeptID = @GroupId)
  AND (@StoreName IS NULL OR store.StoreName LIKE '%' + @StoreName + '%')
ORDER BY store.StoreID DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", conn);

        BindSearchParams(cmd, filter);
        cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
        cmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(new StoreHouseRow
            {
                StoreID = Convert.ToInt32(rd["StoreID"]),
                StoreName = Convert.ToString(rd["StoreName"]) ?? string.Empty,
                Address = Convert.ToString(rd["Address"]) ?? string.Empty,
                KPGroupID = Convert.ToInt32(rd["KPGroupID"]),
                KPGroupName = Convert.ToString(rd["KPGroupName"]) ?? string.Empty,
                IsCoStore = Convert.ToBoolean(rd["IsCoStore"])
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
    }

    private void SetGroupContext()
    {
        SelectedGroupName = StoreGroupList.FirstOrDefault(item => item.Value == Filter.GroupId?.ToString())?.Text ?? string.Empty;
        StoreInput.KPGroupID = Filter.GroupId ?? 0;
    }

    private void LoadEditStore()
    {
        if (!Filter.EditStoreId.HasValue)
        {
            return;
        }

        var row = Rows.FirstOrDefault(item => item.StoreID == Filter.EditStoreId.Value);
        if (row == null)
        {
            return;
        }

        StoreInput = new StoreHouseInput
        {
            StoreID = row.StoreID,
            StoreName = row.StoreName,
            Address = row.Address,
            KPGroupID = row.KPGroupID
        };
    }

    private IActionResult ExportRows(IReadOnlyList<StoreHouseRow> rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Inventory Stores");
        var headers = new[] { "STT", "Store Name", "Address", "INV Group", "Co Store" };

        for (var col = 0; col < headers.Length; col++)
        {
            worksheet.Cell(1, col + 1).Value = headers[col];
        }

        var rowIndex = 2;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            worksheet.Cell(rowIndex, 1).Value = i + 1;
            worksheet.Cell(rowIndex, 2).Value = row.StoreName;
            worksheet.Cell(rowIndex, 3).Value = row.Address;
            worksheet.Cell(rowIndex, 4).Value = row.KPGroupName;
            worksheet.Cell(rowIndex, 5).Value = row.IsCoStore ? "Yes" : "No";
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
            $"inventory_group_stores_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
    }

    private static void BindSearchParams(SqlCommand cmd, InventoryParametersStoreFilter filter)
    {
        cmd.Parameters.Add("@GroupId", SqlDbType.Int).Value = filter.GroupId.HasValue ? filter.GroupId.Value : DBNull.Value;
        cmd.Parameters.Add("@StoreName", SqlDbType.VarChar, 50).Value =
            string.IsNullOrWhiteSpace(filter.StoreName) ? DBNull.Value : filter.StoreName.Trim();
    }

    private bool GroupExists(SqlConnection conn, int groupId)
    {
        using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.INV_KPGroup WHERE KPGroupID = @GroupId;", conn);
        cmd.Parameters.Add("@GroupId", SqlDbType.Int).Value = groupId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private int? FindStoreGroupId(SqlConnection conn, int storeId)
    {
        using var cmd = new SqlCommand("SELECT DeptID FROM dbo.INV_StoreList WHERE StoreID = @StoreID;", conn);
        cmd.Parameters.Add("@StoreID", SqlDbType.Int).Value = storeId;

        var value = cmd.ExecuteScalar();
        return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
    }

    private bool StoreIsInUse(SqlConnection conn, int storeId)
    {
        return HasTableReference(conn, "dbo", "INV_ItemStoreBG", "StoreID", storeId)
            || HasForeignKeyReferences(conn, "dbo", "INV_StoreList", "StoreID", storeId);
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

    private void NormalizeFilter()
    {
        Filter.StoreName = string.IsNullOrWhiteSpace(Filter.StoreName) ? null : Filter.StoreName.Trim();
        Filter.PageSize = NormalizePageSize(Filter.PageSize);

        if (Filter.Page <= 0)
        {
            Filter.Page = 1;
        }

        if (Filter.GroupId.HasValue && Filter.GroupId.Value <= 0)
        {
            Filter.GroupId = null;
        }

        if (Filter.EditStoreId.HasValue && Filter.EditStoreId.Value <= 0)
        {
            Filter.EditStoreId = null;
        }
    }

    private void NormalizeQueryInputs()
    {
        Filter.GroupId = ParseNullableInt(Request.Query[nameof(Filter.GroupId)].ToString());
        Filter.StoreName = Request.Query[nameof(Filter.StoreName)].ToString();
        Filter.EditStoreId = ParseNullableInt(Request.Query[nameof(Filter.EditStoreId)].ToString());
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
            Filter.StoreName,
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

public class InventoryParametersStoreFilter
{
    public int? GroupId { get; set; }
    public string? StoreName { get; set; }
    public int? EditStoreId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class StoreHouseInput
{
    public int StoreID { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public string? Address { get; set; }
    public int KPGroupID { get; set; }
}

public class StoreHouseRow
{
    public int StoreID { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public int KPGroupID { get; set; }
    public string KPGroupName { get; set; } = string.Empty;
    public bool IsCoStore { get; set; }
}
