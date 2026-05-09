using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;

namespace SmartSam.Pages.Inventory.InventoryTrees;

public class IndexModel : BasePageModel
{
    private const int FunctionId = 65;
    private const int PermissionViewList = 1;
    private const int PermissionAddNode = 3;
    private const int PermissionEditNode = 4;
    private const int PermissionDeleteNode = 5;
    private const int PermissionAddItem = 6;
    private const int PermissionDeleteItem = 7;
    private const int PermissionCheckStock = 8;

    private readonly PermissionService _permissionService;

    public IndexModel(IConfiguration config, PermissionService permissionService) : base(config)
    {
        _permissionService = permissionService;
    }

    [BindProperty(SupportsGet = true)]
    public InventoryTreesFilter Filter { get; set; } = new();

    public PagePermissions PagePerm { get; private set; } = new();
    public bool CanAdd => PagePerm.HasPermission(PermissionAddNode);
    public bool CanEdit => PagePerm.HasPermission(PermissionEditNode);
    public bool CanDeleteNode => PagePerm.HasPermission(PermissionDeleteNode);
    public bool CanAddItem => PagePerm.HasPermission(PermissionAddItem);
    public bool CanDeleteItem => PagePerm.HasPermission(PermissionDeleteItem);
    public bool CanCheckStock => PagePerm.HasPermission(PermissionCheckStock);
    public int DefaultPageSize => _config.GetValue<int?>("AppSettings:DefaultPageSize") ?? 10;
    public IReadOnlyList<int> PageSizeOptions => GetConfiguredPageSizeOptions();

    public List<SelectListItem> GroupList { get; set; } = new();
    public List<TreeNodeVm> TreeRoots { get; set; } = new();
    public List<InventoryTreeItemRow> Items { get; set; } = new();
    public InventoryTreeNodeForm SelectedNodeForm { get; set; } = new();
    public int TotalRecords { get; set; }
    public int TotalPages => Filter.PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)Filter.PageSize));
    public int PageStart => TotalRecords == 0 ? 0 : ((Filter.Page - 1) * Filter.PageSize) + 1;
    public int PageEnd => TotalRecords == 0 ? 0 : Math.Min(Filter.Page * Filter.PageSize, TotalRecords);
    public bool HasPreviousPage => Filter.Page > 1;
    public bool HasNextPage => Filter.Page < TotalPages;

    [TempData] public string? Message { get; set; }
    [TempData] public string MessageType { get; set; } = "info";

    public IActionResult OnGet()
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewList)) return Redirect("/");

        NormalizeFilter();
        LoadPageData();
        return Page();
    }

    public IActionResult OnPostAddNode(int groupId, int selectedCatgId, string nodeCode, string nodeName, string? description)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionAddNode)) return Redirect("/");
        if (string.IsNullOrWhiteSpace(nodeCode) || string.IsNullOrWhiteSpace(nodeName))
        {
            SetMessage("Nh?p Node Code và Node Name.", "warning");
            return RedirectToList(groupId, selectedCatgId, Filter.Page);
        }

        using var conn = OpenConnection();
        var level = GetNodeLevel(conn, groupId, selectedCatgId) + 1;
        if (level > 10)
        {
            SetMessage("Maximum Level is 10!", "warning");
            return RedirectToList(groupId, selectedCatgId, Filter.Page);
        }

        const string sql = @"INSERT INTO dbo.INV_CatgTree (CatgCode, CatgName, ParentID, CatgLevel, KPGroupID, Description)
VALUES (@CatgCode, @CatgName, @ParentID, @CatgLevel, @KPGroupID, @Description);
SELECT CAST(SCOPE_IDENTITY() AS INT);";
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@CatgCode", SqlDbType.NVarChar, 50).Value = nodeCode.Trim();
        cmd.Parameters.Add("@CatgName", SqlDbType.NVarChar, 255).Value = nodeName.Trim();
        cmd.Parameters.Add("@ParentID", SqlDbType.Int).Value = selectedCatgId;
        cmd.Parameters.Add("@CatgLevel", SqlDbType.Int).Value = level;
        cmd.Parameters.Add("@KPGroupID", SqlDbType.Int).Value = groupId;
        cmd.Parameters.Add("@Description", SqlDbType.NVarChar, -1).Value = (object?)description?.Trim() ?? DBNull.Value;
        var newId = Convert.ToInt32(cmd.ExecuteScalar());

        SetMessage("Ðã thêm node.", "success");
        return RedirectToList(groupId, newId, 1);
    }

    public IActionResult OnPostEditNode(int groupId, int selectedCatgId, string nodeCode, string nodeName, string? description)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionEditNode)) return Redirect("/");
        if (selectedCatgId <= 0)
        {
            SetMessage("Không th? s?a root node.", "warning");
            return RedirectToList(groupId, 0, Filter.Page);
        }

        const string sql = @"UPDATE dbo.INV_CatgTree SET CatgCode=@CatgCode, CatgName=@CatgName, Description=@Description WHERE CatgID=@CatgID";
        using var conn = OpenConnection();
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@CatgCode", SqlDbType.NVarChar, 50).Value = nodeCode.Trim();
        cmd.Parameters.Add("@CatgName", SqlDbType.NVarChar, 255).Value = nodeName.Trim();
        cmd.Parameters.Add("@Description", SqlDbType.NVarChar, -1).Value = (object?)description?.Trim() ?? DBNull.Value;
        cmd.Parameters.Add("@CatgID", SqlDbType.Int).Value = selectedCatgId;
        cmd.ExecuteNonQuery();

        SetMessage("Ðã c?p nh?t node.", "success");
        return RedirectToList(groupId, selectedCatgId, Filter.Page);
    }

    public IActionResult OnPostDeleteNode(int groupId, int selectedCatgId)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionDeleteNode)) return Redirect("/");
        if (selectedCatgId <= 0)
        {
            SetMessage("Không th? xóa root node.", "warning");
            return RedirectToList(groupId, 0, Filter.Page);
        }

        using var conn = OpenConnection();
        if (HasChildNode(conn, selectedCatgId))
        {
            SetMessage("Node này dang có node con. Xóa node con tru?c.", "warning");
            return RedirectToList(groupId, selectedCatgId, Filter.Page);
        }

        var parentId = GetParentId(conn, selectedCatgId);
        var sql = groupId == 0
            ? "UPDATE dbo.INV_ItemList SET ItemCatg=0 WHERE ItemCatg=@CatgID; DELETE dbo.INV_CatgTree WHERE CatgID=@CatgID;"
            : "DELETE dbo.INV_KPGroupIndex WHERE CatgID=@CatgID AND KPGroupID=@KPGroupID; DELETE dbo.INV_CatgTree WHERE CatgID=@CatgID;";
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@CatgID", SqlDbType.Int).Value = selectedCatgId;
        if (groupId != 0) cmd.Parameters.Add("@KPGroupID", SqlDbType.Int).Value = groupId;
        cmd.ExecuteNonQuery();

        SetMessage("Ðã xóa node.", "success");
        return RedirectToList(groupId, parentId, 1);
    }

    public IActionResult OnPostRemoveItems(int groupId, int selectedCatgId, List<int>? selectedItemIds)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionDeleteItem)) return Redirect("/");
        if (selectedItemIds == null || selectedItemIds.Count == 0)
        {
            SetMessage("Ch?n item c?n remove kh?i node.", "warning");
            return RedirectToList(groupId, selectedCatgId, Filter.Page);
        }

        using var conn = OpenConnection();
        var placeholders = string.Join(",", selectedItemIds.Select((_, index) => $"@p{index}"));
        var sql = groupId == 0
            ? $"UPDATE dbo.INV_ItemList SET ItemCatg=0 WHERE ItemID IN ({placeholders})"
            : $"DELETE dbo.INV_KPGroupIndex WHERE KPGroupID=@KPGroupID AND CatgID=@CatgID AND ItemID IN ({placeholders})";
        using var cmd = new SqlCommand(sql, conn);
        if (groupId != 0)
        {
            cmd.Parameters.Add("@KPGroupID", SqlDbType.Int).Value = groupId;
            cmd.Parameters.Add("@CatgID", SqlDbType.Int).Value = selectedCatgId;
        }
        for (var i = 0; i < selectedItemIds.Count; i++) cmd.Parameters.Add($"@p{i}", SqlDbType.Int).Value = selectedItemIds[i];
        cmd.ExecuteNonQuery();

        SetMessage("Ðã remove item kh?i node.", "success");
        return RedirectToList(groupId, selectedCatgId, Filter.Page);
    }

    private IActionResult RedirectToList(int groupId, int selectedCatgId, int page)
    {
        return RedirectToPage(new { Filter_GroupId = groupId, Filter_SelectedCatgId = selectedCatgId, Filter_Page = page, Filter_PageSize = Filter.PageSize });
    }

    private void NormalizeFilter()
    {
        if (Filter.Page <= 0) Filter.Page = 1;
        var pageSizes = GetConfiguredPageSizeOptions();
        if (Filter.PageSize <= 0) Filter.PageSize = DefaultPageSize;
        if (!pageSizes.Contains(Filter.PageSize)) Filter.PageSize = DefaultPageSize;
        if (Filter.SelectedCatgId < 0) Filter.SelectedCatgId = 0;
    }

    private IReadOnlyList<int> GetConfiguredPageSizeOptions()
    {
        var configured = _config.GetSection("AppSettings:PageSizeOptions").Get<List<int>>();
        if (configured == null || configured.Count == 0) return new List<int> { 10, 20, 50, 100 };
        return configured.Where(x => x > 0).Distinct().OrderBy(x => x).ToList();
    }

    private void LoadPageData()
    {
        LoadGroups();
        var flatNodes = LoadTreeNodes(Filter.GroupId);
        TreeRoots = BuildTree(flatNodes, Filter.GroupId, Filter.SelectedCatgId);
        (Items, TotalRecords) = LoadItems(Filter.GroupId, Filter.SelectedCatgId);
        if (Filter.Page > TotalPages) Filter.Page = TotalPages;
        (Items, TotalRecords) = LoadItems(Filter.GroupId, Filter.SelectedCatgId);
        SelectedNodeForm = LoadSelectedNodeForm(Filter.GroupId, Filter.SelectedCatgId);
    }

    private void LoadGroups()
    {
        GroupList = new List<SelectListItem> { new() { Value = "0", Text = "--No Group--", Selected = Filter.GroupId == 0 } };
        using var conn = OpenConnection();
        using var cmd = new SqlCommand("SELECT KPGroupID, KPGroupName FROM dbo.INV_KPGroup ORDER BY KPGroupName", conn);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var id = Convert.ToInt32(rd["KPGroupID"]);
            GroupList.Add(new SelectListItem { Value = id.ToString(), Text = Convert.ToString(rd["KPGroupName"]) ?? $"(Group #{id})", Selected = id == Filter.GroupId });
        }
    }

    private List<CatgNodeRaw> LoadTreeNodes(int groupId)
    {
        var result = new List<CatgNodeRaw>();
        using var conn = OpenConnection();
        const string sql = @"SELECT CatgID, ParentID, CatgCode, CatgName, CatgLevel, Description
FROM dbo.INV_CatgTree
WHERE KPGroupID = @KPGroupID
ORDER BY CatgLevel, CatgCode";
        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@KPGroupID", SqlDbType.Int).Value = groupId;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            result.Add(new CatgNodeRaw
            {
                CatgId = Convert.ToInt32(rd["CatgID"]),
                ParentId = Convert.ToInt32(rd["ParentID"]),
                CatgCode = Convert.ToString(rd["CatgCode"]) ?? string.Empty,
                CatgName = Convert.ToString(rd["CatgName"]) ?? string.Empty,
                CatgLevel = Convert.ToInt32(rd["CatgLevel"]),
                Description = rd["Description"] == DBNull.Value ? string.Empty : Convert.ToString(rd["Description"]) ?? string.Empty
            });
        }
        return result;
    }

    private List<TreeNodeVm> BuildTree(List<CatgNodeRaw> flatNodes, int groupId, int selectedCatgId)
    {
        var childMap = flatNodes.GroupBy(x => x.ParentId).ToDictionary(x => x.Key, x => x.Select(n => n.CatgId).ToHashSet());
        var ancestorIds = BuildAncestorSet(flatNodes, selectedCatgId);
        var lookup = flatNodes.ToDictionary(
            x => x.CatgId,
            x => new TreeNodeVm
            {
                CatgId = x.CatgId,
                GroupId = groupId,
                DisplayText = $"{x.CatgCode} ({x.CatgName})",
                IsSelected = x.CatgId == selectedCatgId,
                IsExpanded = ancestorIds.Contains(x.CatgId)
            });

        var roots = new List<TreeNodeVm>();
        foreach (var raw in flatNodes)
        {
            var node = lookup[raw.CatgId];
            node.HasChildren = childMap.ContainsKey(raw.CatgId) && childMap[raw.CatgId].Count > 0;
            if (raw.ParentId == 0) roots.Add(node);
            else if (lookup.TryGetValue(raw.ParentId, out var parent)) parent.Children.Add(node);
            else roots.Add(node);
        }

        return roots;
    }

    private HashSet<int> BuildAncestorSet(List<CatgNodeRaw> flatNodes, int selectedCatgId)
    {
        var parentMap = flatNodes.ToDictionary(x => x.CatgId, x => x.ParentId);
        var result = new HashSet<int>();
        var current = selectedCatgId;
        while (current > 0 && parentMap.TryGetValue(current, out var parentId))
        {
            result.Add(current);
            current = parentId;
        }
        return result;
    }

    private InventoryTreeNodeForm LoadSelectedNodeForm(int groupId, int selectedCatgId)
    {
        if (selectedCatgId <= 0) return new InventoryTreeNodeForm();
        using var conn = OpenConnection();
        using var cmd = new SqlCommand("SELECT CatgCode, CatgName, Description FROM dbo.INV_CatgTree WHERE KPGroupID=@KPGroupID AND CatgID=@CatgID", conn);
        cmd.Parameters.Add("@KPGroupID", SqlDbType.Int).Value = groupId;
        cmd.Parameters.Add("@CatgID", SqlDbType.Int).Value = selectedCatgId;
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return new InventoryTreeNodeForm();
        return new InventoryTreeNodeForm
        {
            NodeCode = Convert.ToString(rd["CatgCode"]) ?? string.Empty,
            NodeName = Convert.ToString(rd["CatgName"]) ?? string.Empty,
            Description = rd["Description"] == DBNull.Value ? string.Empty : Convert.ToString(rd["Description"]) ?? string.Empty
        };
    }

    private (List<InventoryTreeItemRow> Items, int TotalRecords) LoadItems(int groupId, int catgId)
    {
        using var conn = OpenConnection();
        var whereSql = groupId == 0
            ? @"FROM dbo.INV_ItemList item
LEFT JOIN dbo.MS_CurrencyFL cur ON cur.CurrencyID = item.Currency
WHERE item.IsActive = 1 AND item.ItemCatg = @CatgID"
            : @"FROM dbo.INV_KPGroupIndex idx
LEFT JOIN dbo.INV_ItemList item ON item.ItemID = idx.ItemID
LEFT JOIN dbo.MS_CurrencyFL cur ON cur.CurrencyID = item.Currency
WHERE idx.CatgID = @CatgID AND idx.KPGroupID = @KPGroupID";

        var countSql = "SELECT COUNT(1) " + whereSql + ";";
        using var countCmd = new SqlCommand(countSql, conn);
        countCmd.Parameters.Add("@CatgID", SqlDbType.Int).Value = catgId;
        if (groupId != 0) countCmd.Parameters.Add("@KPGroupID", SqlDbType.Int).Value = groupId;
        var total = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);

        var offset = (Filter.Page - 1) * Filter.PageSize;
        var dataSql = @"SELECT item.ItemID, item.ItemCode, item.ItemName, item.Unit, item.UnitPrice, cur.CurrencyName " + whereSql + @"
ORDER BY item.ItemCode
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";
        using var cmd = new SqlCommand(dataSql, conn);
        cmd.Parameters.Add("@CatgID", SqlDbType.Int).Value = catgId;
        cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
        cmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = Filter.PageSize;
        if (groupId != 0) cmd.Parameters.Add("@KPGroupID", SqlDbType.Int).Value = groupId;

        var list = new List<InventoryTreeItemRow>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var unitPrice = rd["UnitPrice"] == DBNull.Value ? (decimal?)null : Convert.ToDecimal(rd["UnitPrice"]);
            list.Add(new InventoryTreeItemRow
            {
                ItemID = Convert.ToInt32(rd["ItemID"]),
                ItemCode = Convert.ToString(rd["ItemCode"]) ?? string.Empty,
                ItemName = Convert.ToString(rd["ItemName"]) ?? string.Empty,
                Unit = Convert.ToString(rd["Unit"]) ?? string.Empty,
                UnitPrice = unitPrice,
                CurrencyName = Convert.ToString(rd["CurrencyName"]) ?? string.Empty
            });
        }
        return (list, total);
    }

    private int GetNodeLevel(SqlConnection conn, int groupId, int catgId)
    {
        if (catgId == 0) return 0;
        using var cmd = new SqlCommand("SELECT ISNULL(CatgLevel, 0) FROM dbo.INV_CatgTree WHERE KPGroupID=@KPGroupID AND CatgID=@CatgID", conn);
        cmd.Parameters.Add("@KPGroupID", SqlDbType.Int).Value = groupId;
        cmd.Parameters.Add("@CatgID", SqlDbType.Int).Value = catgId;
        var value = cmd.ExecuteScalar();
        return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
    }

    private bool HasChildNode(SqlConnection conn, int catgId)
    {
        using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.INV_CatgTree WHERE ParentID=@CatgID", conn);
        cmd.Parameters.Add("@CatgID", SqlDbType.Int).Value = catgId;
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private int GetParentId(SqlConnection conn, int catgId)
    {
        using var cmd = new SqlCommand("SELECT ISNULL(ParentID,0) FROM dbo.INV_CatgTree WHERE CatgID=@CatgID", conn);
        cmd.Parameters.Add("@CatgID", SqlDbType.Int).Value = catgId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private SqlConnection OpenConnection()
    {
        var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        return conn;
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

    private int GetCurrentRoleId() => int.TryParse(User.FindFirst("RoleID")?.Value, out var roleId) ? roleId : 0;

    private bool IsAdminRole()
    {
        var value = User.FindFirst("IsAdminRole")?.Value;
        return string.Equals(value, "True", StringComparison.OrdinalIgnoreCase) || value == "1";
    }
}

public class InventoryTreesFilter
{
    public int GroupId { get; set; }
    public int SelectedCatgId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class TreeNodeVm
{
    public int CatgId { get; set; }
    public int GroupId { get; set; }
    public string DisplayText { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
    public bool IsExpanded { get; set; }
    public bool HasChildren { get; set; }
    public List<TreeNodeVm> Children { get; set; } = new();
}

public class CatgNodeRaw
{
    public int CatgId { get; set; }
    public int ParentId { get; set; }
    public string CatgCode { get; set; } = string.Empty;
    public string CatgName { get; set; } = string.Empty;
    public int CatgLevel { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class InventoryTreeItemRow
{
    public int ItemID { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal? UnitPrice { get; set; }
    public string CurrencyName { get; set; } = string.Empty;
    public string UnitPriceText => UnitPrice.HasValue ? UnitPrice.Value.ToString("N2") : string.Empty;
}

public class InventoryTreeNodeForm
{
    public string NodeCode { get; set; } = string.Empty;
    public string NodeName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}
