using System.Data;
using System.Globalization;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;

namespace SmartSam.Pages.Inventory.InventoryItemList;

public class IndexModel : BasePageModel
{
    private const string ExcelVniFontName = "VNI-WIN";
    private const int FunctionId = 64;
    private const int PermissionViewList = 1;
    private const int PermissionAdd = 3;
    private const int PermissionEdit = 4;
    private const int PermissionDelete = 5;
    private const int PermissionCheckStock = 6;
    private const string SpecialViewNoActionOneYear = "NoActionOneYear";
    private const string SpecialViewNewItems = "NewItems";

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
    public bool CanDelete => PagePerm.HasPermission(PermissionDelete);
    public bool CanCheckStock => PagePerm.HasPermission(PermissionCheckStock);

    [BindProperty(SupportsGet = true)]
    public InventoryItemListFilter Filter { get; set; } = new InventoryItemListFilter();

    [BindProperty]
    public InventoryItemInput ItemInput { get; set; } = new InventoryItemInput();

    [BindProperty]
    public string? RecallItemCode { get; set; }

    public List<InventoryItemRow> Rows { get; set; } = new List<InventoryItemRow>();
    public List<SelectListItem> CategoryList { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> CategoryInputList { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> KpGroupList { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> CurrencyList { get; set; } = new List<SelectListItem>();
    public List<RecallLostItemCandidate> RecallLostCandidates { get; set; } = new List<RecallLostItemCandidate>();
    public InventoryItemStockInfo? StockInfo { get; set; }
    public int TotalRecords { get; set; }
    public int TotalPages => Filter.PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)Filter.PageSize));
    public int PageStart => TotalRecords == 0 ? 0 : ((Filter.Page - 1) * Filter.PageSize) + 1;
    public int PageEnd => TotalRecords == 0 ? 0 : Math.Min(Filter.Page * Filter.PageSize, TotalRecords);
    public bool HasPreviousPage => Filter.Page > 1;
    public bool HasNextPage => Filter.Page < TotalPages;
    public string SpecialViewTitle => Filter.SpecialView switch
    {
        SpecialViewNoActionOneYear => "No action one year",
        SpecialViewNewItems => "New items",
        _ => string.Empty
    };

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
        LoadRows();
        if (CanEdit)
        {
            LoadRecallLostCandidates();
        }
        return Page();
    }

    public IActionResult OnGetStockInfo(int itemId)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewList))
        {
            return new JsonResult(new { success = false, message = "You do not have permission to view inventory item list." })
            {
                StatusCode = 401
            };
        }

        if (!PagePerm.HasPermission(PermissionCheckStock))
        {
            return new JsonResult(new { success = false, message = "You do not have permission to check item stock." })
            {
                StatusCode = 403
            };
        }

        if (itemId <= 0)
        {
            return new JsonResult(new { success = false, message = "Please select an inventory item." })
            {
                StatusCode = 400
            };
        }

        using var conn = OpenConnection();
        var stockInfo = GetStockInfo(conn, itemId);
        if (stockInfo == null)
        {
            return new JsonResult(new { success = false, message = "Inventory item was not found." })
            {
                StatusCode = 404
            };
        }

        return new JsonResult(new
        {
            success = true,
            item = new
            {
                stockInfo.ItemID,
                stockInfo.ItemCode,
                stockInfo.ItemName,
                stockInfo.Unit,
                stockInfo.MainInventory,
                StoreBalances = stockInfo.StoreBalances.Select(balance => new
                {
                    balance.Year,
                    balance.StoreID,
                    balance.StoreName,
                    balance.BGQuantity,
                    balance.BGAmount,
                    balance.CurrencyName
                })
            }
        });
    }

    public IActionResult OnPostSaveItem()
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewList))
        {
            return Redirect("/");
        }

        NormalizeFilter();

        var isEdit = ItemInput.ItemID > 0;
        if (!PagePerm.HasPermission(isEdit ? PermissionEdit : PermissionAdd))
        {
            SetMessage("You do not have permission to save inventory item.", "warning");
            return RedirectToPage("./Index", BuildRouteValues());
        }

        NormalizeItemInput();
        var validationMessage = ValidateItemInput();
        if (!string.IsNullOrWhiteSpace(validationMessage))
        {
            SetMessage(validationMessage, "warning");
            return RedirectToPage("./Index", BuildRouteValues());
        }

        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();

        try
        {
            if (isEdit && !ItemExists(conn, tx, ItemInput.ItemID))
            {
                tx.Rollback();
                SetMessage("Inventory item was not found.", "warning");
                return RedirectToPage("./Index", BuildRouteValues());
            }

            if (ItemCodeExists(conn, tx, ItemInput.ItemCode, ItemInput.ItemID))
            {
                tx.Rollback();
                SetMessage("Item Code already exists.", "warning");
                return RedirectToPage("./Index", BuildRouteValues());
            }

            if (ItemInput.KPGroupItem.HasValue && !KpGroupExists(conn, tx, ItemInput.KPGroupItem.Value))
            {
                tx.Rollback();
                SetMessage("INV Group is invalid.", "warning");
                return RedirectToPage("./Index", BuildRouteValues());
            }

            int? parentItemId = null;
            if (ItemInput.IsSubItem)
            {
                var parent = FindParentItem(conn, tx, ItemInput.ParentItemCode, ItemInput.ItemID);
                if (parent == null)
                {
                    tx.Rollback();
                    SetMessage("Parent Item Code is invalid.", "warning");
                    return RedirectToPage("./Index", BuildRouteValues());
                }

                parentItemId = parent.ItemID;
                ItemInput.ParentItemCode = parent.ItemCode;
            }

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            if (isEdit)
            {
                cmd.CommandText = @"
UPDATE dbo.INV_ItemList
SET ItemCode = @ItemCode,
    ItemName = @ItemName,
    ItemCatg = @ItemCatg,
    Specification = @Specification,
    Unit = @Unit,
    UnitPrice = @UnitPrice,
    Currency = @Currency,
    IsSubItem = @IsSubItem,
    ParentItemID = @ParentItemID,
    KPGroupItem = @KPGroupItem,
    IsApartment = @IsApartment,
    IsStock = @IsStock,
    IsFixAsset = @IsFixAsset,
    IsMaterial = @IsMaterial,
    IsPurchase = @IsPurchase,
    IsActive = @IsActive,
    ReOrderPoint = @ReOrderPoint,
    IsNewItem = @IsNewItem,
    ItemNameNew = CASE WHEN @IsNewItem = 1 THEN @ItemName ELSE ItemNameNew END
WHERE ItemID = @ItemID;";
                cmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = ItemInput.ItemID;
            }
            else
            {
                cmd.CommandText = @"
INSERT INTO dbo.INV_ItemList
(
    ItemCode,
    ItemName,
    ItemCatg,
    Specification,
    Unit,
    UnitPrice,
    Currency,
    IsSubItem,
    ParentItemID,
    KPGroupItem,
    IsApartment,
    IsStock,
    IsFixAsset,
    IsMaterial,
    IsPurchase,
    IsActive,
    ReOrderPoint,
    IsNewItem,
    CreatedDate,
    ItemNameNew
)
VALUES
(
    @ItemCode,
    @ItemName,
    @ItemCatg,
    @Specification,
    @Unit,
    @UnitPrice,
    @Currency,
    @IsSubItem,
    @ParentItemID,
    @KPGroupItem,
    @IsApartment,
    @IsStock,
    @IsFixAsset,
    @IsMaterial,
    @IsPurchase,
    @IsActive,
    @ReOrderPoint,
    @IsNewItem,
    CONVERT(nvarchar(100), GETDATE(), 120),
    CASE WHEN @IsNewItem = 1 THEN @ItemName ELSE NULL END
);";
            }

            BindItemParams(cmd, parentItemId);
            cmd.ExecuteNonQuery();
            tx.Commit();

            SetMessage(isEdit ? "Inventory item updated." : "Inventory item added.", "success");
            return RedirectToPage("./Index", BuildRouteValues());
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public IActionResult OnPostDeleteItem()
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewList))
        {
            return Redirect("/");
        }

        NormalizeFilter();

        if (!PagePerm.HasPermission(PermissionDelete))
        {
            SetMessage("You do not have permission to delete inventory item.", "warning");
            return RedirectToPage("./Index", BuildRouteValues());
        }

        if (ItemInput.ItemID <= 0)
        {
            SetMessage("Please select an inventory item.", "warning");
            return RedirectToPage("./Index", BuildRouteValues());
        }

        using var conn = OpenConnection();
        var item = FindItemForDelete(conn, ItemInput.ItemID);
        if (item == null)
        {
            SetMessage("Inventory item was not found.", "warning");
            return RedirectToPage("./Index", BuildRouteValues());
        }

        if (ItemIsInUse(conn, item))
        {
            SetMessage("Inventory item is in use and cannot be deleted.", "warning");
            return RedirectToPage("./Index", BuildRouteValues());
        }

        using var tx = conn.BeginTransaction();
        try
        {
            using (var auditCmd = conn.CreateCommand())
            {
                auditCmd.Transaction = tx;
                auditCmd.CommandText = @"
INSERT INTO dbo.INV_ItemDel (ItemID, ItemCode, ItemName, Unit, UserID, TheDate)
VALUES (@ItemID, @ItemCode, @ItemName, @Unit, @UserID, GETDATE());";
                auditCmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = item.ItemID;
                auditCmd.Parameters.Add("@ItemCode", SqlDbType.VarChar, 50).Value = ToDbValue(item.ItemCode);
                auditCmd.Parameters.Add("@ItemName", SqlDbType.VarChar, 150).Value = ToDbValue(item.ItemName);
                auditCmd.Parameters.Add("@Unit", SqlDbType.VarChar, 10).Value = ToDbValue(item.Unit);
                auditCmd.Parameters.Add("@UserID", SqlDbType.Int).Value = GetCurrentEmployeeId();
                auditCmd.ExecuteNonQuery();
            }

            using (var deleteCmd = conn.CreateCommand())
            {
                deleteCmd.Transaction = tx;
                deleteCmd.CommandText = "DELETE FROM dbo.INV_ItemList WHERE ItemID = @ItemID;";
                deleteCmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = item.ItemID;
                deleteCmd.ExecuteNonQuery();
            }

            tx.Commit();
            SetMessage("Inventory item deleted.", "success");
            return RedirectToPage("./Index", BuildRouteValues());
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public IActionResult OnPostRecallLostItem()
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewList))
        {
            return Redirect("/");
        }

        NormalizeFilter();

        if (!PagePerm.HasPermission(PermissionEdit))
        {
            SetMessage("You do not have permission to recall lost item.", "warning");
            return RedirectToPage("./Index", BuildRouteValues());
        }

        var itemCode = RecallItemCode?.Trim();
        if (string.IsNullOrWhiteSpace(itemCode))
        {
            SetMessage("Please select a lost item to recall.", "warning");
            return RedirectToPage("./Index", BuildRouteValues());
        }

        using var conn = OpenConnection();
        if (CurrentItemCodeExists(conn, itemCode))
        {
            SetMessage($"Item {itemCode} already exists in inventory item list. No recall is needed.", "warning");
            return RedirectToPage("./Index", BuildRouteValues());
        }

        if (!BackupItemCodeExists(conn, itemCode))
        {
            SetMessage($"Item {itemCode} was not found in backup item list.", "warning");
            return RedirectToPage("./Index", BuildRouteValues());
        }

        using var cmd = new SqlCommand("dbo.UpdateLostItem", conn)
        {
            CommandType = CommandType.StoredProcedure
        };
        cmd.Parameters.Add("@ItemCode", SqlDbType.VarChar, 20).Value = itemCode;
        cmd.ExecuteNonQuery();

        if (CurrentItemCodeExists(conn, itemCode))
        {
            SetCreatedDateForRecalledItem(conn, itemCode);
            SetMessage($"Lost item {itemCode} recalled successfully.", "success");
        }
        else
        {
            SetMessage($"Recall lost item {itemCode} was executed, but the item was not restored.", "warning");
        }

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

        var exportFilter = new InventoryItemListFilter
        {
            ItemCode = Filter.ItemCode,
            ItemName = Filter.ItemName,
            ItemCatg = Filter.ItemCatg,
            KPGroupItem = Filter.KPGroupItem,
            ActiveStatus = Filter.ActiveStatus,
            SpecialView = Filter.SpecialView,
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

    private (List<InventoryItemRow> rows, int totalRecords) SearchRows(InventoryItemListFilter filter)
    {
        var rows = new List<InventoryItemRow>();
        var page = filter.Page <= 0 ? 1 : filter.Page;
        var pageSize = NormalizePageSize(filter.PageSize);
        var offset = (page - 1) * pageSize;

        using var conn = OpenConnection();
        using var countCmd = new SqlCommand(@"
SELECT COUNT(1)
FROM dbo.INV_ItemList item
LEFT JOIN dbo.INV_ItemList parentItem ON parentItem.ItemID = item.ParentItemID
WHERE (@ItemCode IS NULL OR item.ItemCode LIKE '%' + @ItemCode + '%')
  AND (@ItemName IS NULL OR item.ItemName LIKE '%' + @ItemName + '%')
  AND (@ItemCatg IS NULL OR item.ItemCatg = @ItemCatg)
  AND (@KPGroupItem IS NULL OR item.KPGroupItem = @KPGroupItem)
  AND (@ActiveStatus IS NULL OR ISNULL(item.IsActive, 0) = @ActiveStatus)
  AND (@SpecialView <> @SpecialViewNewItems OR ISNULL(item.IsNewItem, 0) = 1)
  AND (
        @SpecialView <> @SpecialViewNoActionOneYear
        OR (
            ISNULL(item.isNotItem, 0) = 0
            AND NOT EXISTS
            (
                SELECT 1
                FROM dbo.INV_ItemFlowDetail flowDetail
                INNER JOIN dbo.INV_ItemFlow flowHeader ON flowHeader.FlowID = flowDetail.FlowID
                WHERE flowDetail.ItemID = item.ItemID
                  AND flowHeader.FlowDate >= DATEADD(day, -365, CAST(GETDATE() AS date))
                  AND flowHeader.FlowDate < DATEADD(day, 1, CAST(GETDATE() AS date))
            )
        )
      );", conn);

        BindSearchParams(countCmd, filter);
        var totalRecords = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);

        using var cmd = new SqlCommand(@"
SELECT
    item.ItemID,
    ISNULL(item.ItemCode, '') AS ItemCode,
    ISNULL(item.ItemName, '') AS ItemName,
    item.ItemCatg,
    ISNULL(catg.CatgCode, '') AS CatgCode,
    ISNULL(catg.CatgName, '') AS CatgName,
    ISNULL(item.Unit, '') AS Unit,
    item.UnitPrice,
    item.Currency,
    ISNULL(curr.CurrencyName, '') AS CurrencyName,
    ISNULL(item.Specification, '') AS Specification,
    item.KPGroupItem,
    ISNULL(kp.KPGroupName, '') AS KPGroupName,
    item.ReOrderPoint,
    ISNULL(item.IsSubItem, 0) AS IsSubItem,
    item.ParentItemID,
    ISNULL(parentItem.ItemCode, '') AS ParentItemCode,
    ISNULL(item.IsApartment, 0) AS IsApartment,
    ISNULL(item.IsStock, 0) AS IsStock,
    ISNULL(item.IsFixAsset, 0) AS IsFixAsset,
    ISNULL(item.IsMaterial, 0) AS IsMaterial,
    ISNULL(item.IsPurchase, 0) AS IsPurchase,
    ISNULL(item.IsActive, 0) AS IsActive,
    ISNULL(item.IsNewItem, 0) AS IsNewItem,
    ISNULL(item.CreatedDate, '') AS CreatedDate
FROM dbo.INV_ItemList item
LEFT JOIN dbo.INV_CatgTree catg ON catg.CatgID = item.ItemCatg
LEFT JOIN dbo.MS_CurrencyFL curr ON curr.CurrencyID = item.Currency
LEFT JOIN dbo.INV_KPGroup kp ON kp.KPGroupID = item.KPGroupItem
LEFT JOIN dbo.INV_ItemList parentItem ON parentItem.ItemID = item.ParentItemID
WHERE (@ItemCode IS NULL OR item.ItemCode LIKE '%' + @ItemCode + '%')
  AND (@ItemName IS NULL OR item.ItemName LIKE '%' + @ItemName + '%')
  AND (@ItemCatg IS NULL OR item.ItemCatg = @ItemCatg)
  AND (@KPGroupItem IS NULL OR item.KPGroupItem = @KPGroupItem)
  AND (@ActiveStatus IS NULL OR ISNULL(item.IsActive, 0) = @ActiveStatus)
  AND (@SpecialView <> @SpecialViewNewItems OR ISNULL(item.IsNewItem, 0) = 1)
  AND (
        @SpecialView <> @SpecialViewNoActionOneYear
        OR (
            ISNULL(item.isNotItem, 0) = 0
            AND NOT EXISTS
            (
                SELECT 1
                FROM dbo.INV_ItemFlowDetail flowDetail
                INNER JOIN dbo.INV_ItemFlow flowHeader ON flowHeader.FlowID = flowDetail.FlowID
                WHERE flowDetail.ItemID = item.ItemID
                  AND flowHeader.FlowDate >= DATEADD(day, -365, CAST(GETDATE() AS date))
                  AND flowHeader.FlowDate < DATEADD(day, 1, CAST(GETDATE() AS date))
            )
        )
      )
ORDER BY
    TRY_CONVERT(datetime2, item.CreatedDate) DESC,
    item.ItemID DESC,
    item.ItemCode
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;", conn);

        BindSearchParams(cmd, filter);
        cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
        cmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(new InventoryItemRow
            {
                ItemID = Convert.ToInt32(rd["ItemID"]),
                ItemCode = Convert.ToString(rd["ItemCode"]) ?? string.Empty,
                ItemName = Convert.ToString(rd["ItemName"]) ?? string.Empty,
                ItemCatg = ReadNullableInt(rd["ItemCatg"]) ?? 0,
                CatgCode = Convert.ToString(rd["CatgCode"]) ?? string.Empty,
                CatgName = Convert.ToString(rd["CatgName"]) ?? string.Empty,
                Unit = Convert.ToString(rd["Unit"]) ?? string.Empty,
                UnitPrice = ReadNullableDouble(rd["UnitPrice"]),
                Currency = ReadNullableInt(rd["Currency"]),
                CurrencyName = Convert.ToString(rd["CurrencyName"]) ?? string.Empty,
                Specification = Convert.ToString(rd["Specification"]) ?? string.Empty,
                KPGroupItem = ReadNullableInt(rd["KPGroupItem"]),
                KPGroupName = Convert.ToString(rd["KPGroupName"]) ?? string.Empty,
                ReOrderPoint = ReadNullableDecimal(rd["ReOrderPoint"]),
                IsSubItem = Convert.ToBoolean(rd["IsSubItem"]),
                ParentItemID = ReadNullableInt(rd["ParentItemID"]),
                ParentItemCode = Convert.ToString(rd["ParentItemCode"]) ?? string.Empty,
                IsApartment = Convert.ToBoolean(rd["IsApartment"]),
                IsStock = Convert.ToBoolean(rd["IsStock"]),
                IsFixAsset = Convert.ToBoolean(rd["IsFixAsset"]),
                IsMaterial = Convert.ToBoolean(rd["IsMaterial"]),
                IsPurchase = Convert.ToBoolean(rd["IsPurchase"]),
                IsActive = Convert.ToBoolean(rd["IsActive"]),
                IsNewItem = Convert.ToBoolean(rd["IsNewItem"]),
                CreatedDate = Convert.ToString(rd["CreatedDate"]) ?? string.Empty
            });
        }

        return (rows, totalRecords);
    }

    private void LoadReferenceData()
    {
        CategoryList = LoadCategoryOptions(includeAll: true, includeEmpty: false);
        CategoryInputList = LoadCategoryOptions(includeAll: false, includeEmpty: true);

        KpGroupList = LoadListFromSql(
            @"SELECT KPGroupID, ISNULL(KPGroupName, CONCAT('(Group #', KPGroupID, ')')) AS KPGroupName
              FROM dbo.INV_KPGroup
              ORDER BY KPGroupName",
            "KPGroupID",
            "KPGroupName",
            true);

        CurrencyList = LoadListFromSql(
            @"SELECT CurrencyID, CurrencyName
              FROM dbo.MS_CurrencyFL
              ORDER BY CurrencyID",
            "CurrencyID",
            "CurrencyName");
    }

    private void LoadRecallLostCandidates()
    {
        RecallLostCandidates.Clear();

        using var conn = OpenConnection();
        using var cmd = new SqlCommand(@"
SELECT TOP (300)
    backupItem.ItemID,
    ISNULL(backupItem.ItemCode, '') AS ItemCode,
    ISNULL(backupItem.ItemName, '') AS ItemName,
    ISNULL(catg.CatgCode, '') AS CatgCode,
    ISNULL(catg.CatgName, '') AS CatgName,
    ISNULL(backupItem.Unit, '') AS Unit,
    backupItem.UnitPrice,
    ISNULL(curr.CurrencyName, '') AS CurrencyName,
    ISNULL(backupItem.Specification, '') AS Specification,
    ISNULL(kp.KPGroupName, '') AS KPGroupName
FROM dbo.INV_ItemListXX backupItem
LEFT JOIN dbo.INV_ItemList currentItem ON currentItem.ItemCode = backupItem.ItemCode
LEFT JOIN dbo.INV_CatgTree catg ON catg.CatgID = backupItem.ItemCatg
LEFT JOIN dbo.MS_CurrencyFL curr ON curr.CurrencyID = backupItem.Currency
LEFT JOIN dbo.INV_KPGroup kp ON kp.KPGroupID = backupItem.KPGroupItem
WHERE currentItem.ItemID IS NULL
ORDER BY backupItem.ItemCode;", conn);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            RecallLostCandidates.Add(new RecallLostItemCandidate
            {
                ItemID = Convert.ToInt32(rd["ItemID"]),
                ItemCode = Convert.ToString(rd["ItemCode"]) ?? string.Empty,
                ItemName = Convert.ToString(rd["ItemName"]) ?? string.Empty,
                CatgCode = Convert.ToString(rd["CatgCode"]) ?? string.Empty,
                CatgName = Convert.ToString(rd["CatgName"]) ?? string.Empty,
                Unit = Convert.ToString(rd["Unit"]) ?? string.Empty,
                UnitPrice = ReadNullableDouble(rd["UnitPrice"]),
                CurrencyName = Convert.ToString(rd["CurrencyName"]) ?? string.Empty,
                Specification = Convert.ToString(rd["Specification"]) ?? string.Empty,
                KPGroupName = Convert.ToString(rd["KPGroupName"]) ?? string.Empty
            });
        }
    }

    private List<SelectListItem> LoadCategoryOptions(bool includeAll, bool includeEmpty)
    {
        var result = new List<SelectListItem>();
        if (includeAll)
        {
            result.Add(new SelectListItem { Value = string.Empty, Text = "--- All ---" });
        }

        if (includeEmpty)
        {
            result.Add(new SelectListItem { Value = "0", Text = "---" });
        }

        var rows = new List<CategoryOptionRow>();
        using var conn = OpenConnection();
        using var cmd = new SqlCommand(@"
SELECT CatgID, CatgCode, CatgName, ParentID, CatgLevel
FROM dbo.INV_CatgTree
WHERE KPGroupID = 0
ORDER BY CatgCode;", conn);

        using (var rd = cmd.ExecuteReader())
        {
            while (rd.Read())
            {
                rows.Add(new CategoryOptionRow
                {
                    CatgID = Convert.ToInt32(rd["CatgID"]),
                    CatgCode = Convert.ToString(rd["CatgCode"]) ?? string.Empty,
                    CatgName = Convert.ToString(rd["CatgName"]) ?? string.Empty,
                    ParentID = Convert.ToInt32(rd["ParentID"]),
                    CatgLevel = Convert.ToInt32(rd["CatgLevel"])
                });
            }
        }

        var byParent = rows
            .GroupBy(item => item.ParentID)
            .ToDictionary(group => group.Key, group => group.OrderBy(item => item.CatgCode).ToList());
        var visited = new HashSet<int>();

        void AddChildren(int parentId, int level)
        {
            if (!byParent.TryGetValue(parentId, out var children))
            {
                return;
            }

            foreach (var child in children)
            {
                if (!visited.Add(child.CatgID))
                {
                    continue;
                }

                result.Add(new SelectListItem
                {
                    Value = child.CatgID.ToString(CultureInfo.InvariantCulture),
                    Text = $"{new string(' ', level * 2)}{child.DisplayText}"
                });
                AddChildren(child.CatgID, level + 1);
            }
        }

        AddChildren(0, 0);

        foreach (var row in rows.Where(item => !visited.Contains(item.CatgID)).OrderBy(item => item.CatgCode))
        {
            result.Add(new SelectListItem
            {
                Value = row.CatgID.ToString(CultureInfo.InvariantCulture),
                Text = $"{new string(' ', Math.Max(0, row.CatgLevel) * 2)}{row.DisplayText}"
            });
        }

        return result;
    }

    private void LoadStockInfoIfRequested()
    {
        if (!Filter.StockItemId.HasValue)
        {
            return;
        }

        if (!PagePerm.HasPermission(PermissionCheckStock))
        {
            SetMessage("You do not have permission to check item stock.", "warning");
            return;
        }

        using var conn = OpenConnection();
        StockInfo = GetStockInfo(conn, Filter.StockItemId.Value);
        if (StockInfo == null)
        {
            SetMessage("Inventory item was not found.", "warning");
        }
    }

    private InventoryItemStockInfo? GetStockInfo(SqlConnection conn, int itemId)
    {
        using var cmd = new SqlCommand(@"
SELECT TOP (1)
    ItemID,
    ISNULL(ItemCode, '') AS ItemCode,
    ISNULL(ItemName, '') AS ItemName,
    ISNULL(Unit, '') AS Unit,
    CAST(ISNULL(dbo.GetItemBalance(ItemID, ISNULL(NULLIF(KPGroupItem, 0), 1), YEAR(GETDATE())), 0) AS decimal(18,2)) AS MainInventory
FROM dbo.INV_ItemList
WHERE ItemID = @ItemID;", conn);
        cmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = itemId;

        InventoryItemStockInfo stockInfo;
        using (var rd = cmd.ExecuteReader())
        {
            if (!rd.Read())
            {
                return null;
            }

            stockInfo = new InventoryItemStockInfo
            {
                ItemID = Convert.ToInt32(rd["ItemID"]),
                ItemCode = Convert.ToString(rd["ItemCode"]) ?? string.Empty,
                ItemName = Convert.ToString(rd["ItemName"]) ?? string.Empty,
                Unit = Convert.ToString(rd["Unit"]) ?? string.Empty,
                MainInventory = ReadNullableDecimal(rd["MainInventory"]) ?? 0m
            };
        }

        using var storeCmd = new SqlCommand(@"
SELECT TOP (200)
    bg.Year,
    bg.StoreID,
    ISNULL(store.StoreName, CONCAT('Store #', bg.StoreID)) AS StoreName,
    CAST(ISNULL(bg.BGQuantity, 0) AS decimal(18,2)) AS BGQuantity,
    CAST(ISNULL(bg.BGAmount, 0) AS decimal(18,2)) AS BGAmount,
    ISNULL(curr.CurrencyName, '') AS CurrencyName
FROM dbo.INV_ItemStoreBG bg
LEFT JOIN dbo.INV_StoreList store ON store.StoreID = bg.StoreID
LEFT JOIN dbo.MS_CurrencyFL curr ON curr.CurrencyID = bg.Currency
WHERE bg.ItemID = @ItemID
ORDER BY bg.Year DESC, store.StoreName;", conn);
        storeCmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = itemId;

        using var storeReader = storeCmd.ExecuteReader();
        while (storeReader.Read())
        {
            stockInfo.StoreBalances.Add(new InventoryItemStoreBalance
            {
                Year = Convert.ToInt32(storeReader["Year"]),
                StoreID = Convert.ToInt32(storeReader["StoreID"]),
                StoreName = Convert.ToString(storeReader["StoreName"]) ?? string.Empty,
                BGQuantity = ReadNullableDecimal(storeReader["BGQuantity"]) ?? 0m,
                BGAmount = ReadNullableDecimal(storeReader["BGAmount"]) ?? 0m,
                CurrencyName = Convert.ToString(storeReader["CurrencyName"]) ?? string.Empty
            });
        }

        return stockInfo;
    }

    private IActionResult ExportRows(IReadOnlyList<InventoryItemRow> rows)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Inventory Items");
        var headers = new[]
        {
            "STT",
            "Item Code",
            "Item Name",
            "Catg",
            "Unit",
            "U.Price",
            "Curr",
            "Specification",
            "INV Group",
            "Re-Order Pnt",
            "IsSub",
            "Parent Item",
            "IsApmt",
            "IsActive",
            "Created Date"
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
            worksheet.Cell(rowIndex, 2).Value = row.ItemCode;
            worksheet.Cell(rowIndex, 3).Value = row.ItemName;
            worksheet.Cell(rowIndex, 4).Value = row.CatgCode;
            worksheet.Cell(rowIndex, 5).Value = row.Unit;
            if (row.UnitPrice.HasValue)
            {
                worksheet.Cell(rowIndex, 6).Value = row.UnitPrice.Value;
            }
            worksheet.Cell(rowIndex, 7).Value = row.CurrencyName;
            worksheet.Cell(rowIndex, 8).Value = row.Specification;
            worksheet.Cell(rowIndex, 9).Value = row.KPGroupName;
            if (row.ReOrderPoint.HasValue)
            {
                worksheet.Cell(rowIndex, 10).Value = row.ReOrderPoint.Value;
            }
            worksheet.Cell(rowIndex, 11).Value = row.IsSubItem ? "Yes" : "No";
            worksheet.Cell(rowIndex, 12).Value = row.ParentItemCode;
            worksheet.Cell(rowIndex, 13).Value = row.IsApartment ? "Yes" : "No";
            worksheet.Cell(rowIndex, 14).Value = row.IsActive ? "Yes" : "No";
            worksheet.Cell(rowIndex, 15).Value = row.CreatedDateDisplay;
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
            $"inventory_items_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
    }

    private void NormalizeItemInput()
    {
        ItemInput.ItemCode = (ItemInput.ItemCode ?? string.Empty).Trim();
        ItemInput.ItemName = (ItemInput.ItemName ?? string.Empty).Trim();
        ItemInput.Specification = string.IsNullOrWhiteSpace(ItemInput.Specification) ? null : ItemInput.Specification.Trim();
        ItemInput.Unit = string.IsNullOrWhiteSpace(ItemInput.Unit) ? null : ItemInput.Unit.Trim();
        ItemInput.ParentItemCode = string.IsNullOrWhiteSpace(ItemInput.ParentItemCode) ? null : ItemInput.ParentItemCode.Trim();

        if (ItemInput.ItemCatg < 0)
        {
            ItemInput.ItemCatg = 0;
        }

        if (ItemInput.KPGroupItem.HasValue && ItemInput.KPGroupItem.Value <= 0)
        {
            ItemInput.KPGroupItem = null;
        }

        if (ItemInput.Currency.HasValue && ItemInput.Currency.Value == 0)
        {
            ItemInput.Currency = null;
        }

        if (!ItemInput.IsSubItem)
        {
            ItemInput.ParentItemCode = null;
        }
    }

    private string? ValidateItemInput()
    {
        if (string.IsNullOrWhiteSpace(ItemInput.ItemCode))
        {
            return "Item Code is required.";
        }

        if (string.IsNullOrWhiteSpace(ItemInput.ItemName))
        {
            return "Item Name is required.";
        }

        if (ItemInput.ItemCode.Length > 20)
        {
            return "Item Code cannot exceed 20 characters.";
        }

        if (ItemInput.ItemName.Length > 150)
        {
            return "Item Name cannot exceed 150 characters.";
        }

        if (ItemInput.Specification?.Length > 50)
        {
            return "Specification cannot exceed 50 characters.";
        }

        if (ItemInput.Unit?.Length > 10)
        {
            return "Unit cannot exceed 10 characters.";
        }

        if (ItemInput.UnitPrice.HasValue && ItemInput.UnitPrice.Value < 0)
        {
            return "Unit Price cannot be negative.";
        }

        if (ItemInput.IsSubItem && string.IsNullOrWhiteSpace(ItemInput.ParentItemCode))
        {
            return "Parent Item Code is required for sub item.";
        }

        return null;
    }

    private void BindItemParams(SqlCommand cmd, int? parentItemId)
    {
        cmd.Parameters.Add("@ItemCode", SqlDbType.VarChar, 20).Value = ItemInput.ItemCode;
        cmd.Parameters.Add("@ItemName", SqlDbType.VarChar, 150).Value = ItemInput.ItemName;
        cmd.Parameters.Add("@ItemCatg", SqlDbType.Int).Value = ItemInput.ItemCatg;
        cmd.Parameters.Add("@Specification", SqlDbType.VarChar, 50).Value = ToDbValue(ItemInput.Specification);
        cmd.Parameters.Add("@Unit", SqlDbType.VarChar, 10).Value = ToDbValue(ItemInput.Unit);
        cmd.Parameters.Add("@UnitPrice", SqlDbType.Float).Value = ItemInput.UnitPrice.HasValue ? ItemInput.UnitPrice.Value : DBNull.Value;
        cmd.Parameters.Add("@Currency", SqlDbType.TinyInt).Value = ItemInput.Currency.HasValue ? ItemInput.Currency.Value : DBNull.Value;
        cmd.Parameters.Add("@IsSubItem", SqlDbType.Bit).Value = ItemInput.IsSubItem;
        cmd.Parameters.Add("@ParentItemID", SqlDbType.Int).Value = parentItemId.HasValue ? parentItemId.Value : DBNull.Value;
        cmd.Parameters.Add("@KPGroupItem", SqlDbType.Int).Value = ItemInput.KPGroupItem.HasValue ? ItemInput.KPGroupItem.Value : DBNull.Value;
        cmd.Parameters.Add("@IsApartment", SqlDbType.Bit).Value = ItemInput.IsApartment;
        cmd.Parameters.Add("@IsStock", SqlDbType.Bit).Value = ItemInput.IsStock;
        cmd.Parameters.Add("@IsFixAsset", SqlDbType.Bit).Value = ItemInput.IsFixAsset;
        cmd.Parameters.Add("@IsMaterial", SqlDbType.Bit).Value = ItemInput.IsMaterial;
        cmd.Parameters.Add("@IsPurchase", SqlDbType.Bit).Value = ItemInput.IsPurchase;
        cmd.Parameters.Add("@IsActive", SqlDbType.Bit).Value = ItemInput.IsActive;
        cmd.Parameters.Add("@IsNewItem", SqlDbType.Bit).Value = ItemInput.IsNewItem;

        var reorderParam = cmd.Parameters.Add("@ReOrderPoint", SqlDbType.Decimal);
        reorderParam.Precision = 18;
        reorderParam.Scale = 2;
        reorderParam.Value = ItemInput.ReOrderPoint.HasValue ? ItemInput.ReOrderPoint.Value : DBNull.Value;
    }

    private static void BindSearchParams(SqlCommand cmd, InventoryItemListFilter filter)
    {
        cmd.Parameters.Add("@ItemCode", SqlDbType.VarChar, 20).Value =
            string.IsNullOrWhiteSpace(filter.ItemCode) ? DBNull.Value : filter.ItemCode.Trim();
        cmd.Parameters.Add("@ItemName", SqlDbType.VarChar, 150).Value =
            string.IsNullOrWhiteSpace(filter.ItemName) ? DBNull.Value : filter.ItemName.Trim();
        cmd.Parameters.Add("@ItemCatg", SqlDbType.Int).Value =
            filter.ItemCatg.HasValue ? filter.ItemCatg.Value : DBNull.Value;
        cmd.Parameters.Add("@KPGroupItem", SqlDbType.Int).Value =
            filter.KPGroupItem.HasValue ? filter.KPGroupItem.Value : DBNull.Value;
        cmd.Parameters.Add("@ActiveStatus", SqlDbType.Bit).Value =
            filter.ActiveStatus.HasValue ? filter.ActiveStatus.Value : DBNull.Value;
        cmd.Parameters.Add("@SpecialView", SqlDbType.VarChar, 30).Value = filter.SpecialView ?? string.Empty;
        cmd.Parameters.Add("@SpecialViewNoActionOneYear", SqlDbType.VarChar, 30).Value = SpecialViewNoActionOneYear;
        cmd.Parameters.Add("@SpecialViewNewItems", SqlDbType.VarChar, 30).Value = SpecialViewNewItems;
    }

    private bool ItemExists(SqlConnection conn, SqlTransaction tx, int itemId)
    {
        using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.INV_ItemList WHERE ItemID = @ItemID;", conn, tx);
        cmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = itemId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private bool ItemCodeExists(SqlConnection conn, SqlTransaction tx, string itemCode, int currentItemId)
    {
        using var cmd = new SqlCommand(@"
SELECT COUNT(1)
FROM dbo.INV_ItemList
WHERE ItemCode = @ItemCode
  AND (@CurrentItemID = 0 OR ItemID <> @CurrentItemID);", conn, tx);
        cmd.Parameters.Add("@ItemCode", SqlDbType.VarChar, 20).Value = itemCode;
        cmd.Parameters.Add("@CurrentItemID", SqlDbType.Int).Value = currentItemId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private bool CurrentItemCodeExists(SqlConnection conn, string itemCode)
    {
        using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.INV_ItemList WHERE ItemCode = @ItemCode;", conn);
        cmd.Parameters.Add("@ItemCode", SqlDbType.VarChar, 20).Value = itemCode;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private void SetCreatedDateForRecalledItem(SqlConnection conn, string itemCode)
    {
        using var cmd = new SqlCommand(@"
UPDATE dbo.INV_ItemList
SET CreatedDate = CONVERT(nvarchar(100), GETDATE(), 120)
WHERE ItemCode = @ItemCode
  AND (CreatedDate IS NULL OR LTRIM(RTRIM(CreatedDate)) = '');", conn);
        cmd.Parameters.Add("@ItemCode", SqlDbType.VarChar, 20).Value = itemCode;
        cmd.ExecuteNonQuery();
    }

    private bool BackupItemCodeExists(SqlConnection conn, string itemCode)
    {
        using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.INV_ItemListXX WHERE ItemCode = @ItemCode;", conn);
        cmd.Parameters.Add("@ItemCode", SqlDbType.VarChar, 20).Value = itemCode;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private bool KpGroupExists(SqlConnection conn, SqlTransaction tx, int groupId)
    {
        using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.INV_KPGroup WHERE KPGroupID = @KPGroupID;", conn, tx);
        cmd.Parameters.Add("@KPGroupID", SqlDbType.Int).Value = groupId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private ParentItemLookup? FindParentItem(SqlConnection conn, SqlTransaction tx, string? parentItemCode, int currentItemId)
    {
        using var cmd = new SqlCommand(@"
SELECT TOP (1) ItemID, ItemCode
FROM dbo.INV_ItemList
WHERE ISNULL(IsSubItem, 0) = 0
  AND (@CurrentItemID = 0 OR ItemID <> @CurrentItemID)
  AND ItemCode LIKE '%' + @ParentItemCode + '%'
ORDER BY ItemCode;", conn, tx);
        cmd.Parameters.Add("@CurrentItemID", SqlDbType.Int).Value = currentItemId;
        cmd.Parameters.Add("@ParentItemCode", SqlDbType.VarChar, 20).Value =
            string.IsNullOrWhiteSpace(parentItemCode) ? DBNull.Value : parentItemCode.Trim();

        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
        {
            return null;
        }

        return new ParentItemLookup
        {
            ItemID = Convert.ToInt32(rd["ItemID"]),
            ItemCode = Convert.ToString(rd["ItemCode"]) ?? string.Empty
        };
    }

    private InventoryItemDeleteInfo? FindItemForDelete(SqlConnection conn, int itemId)
    {
        using var cmd = new SqlCommand(@"
SELECT TOP (1)
    ItemID,
    ISNULL(ItemCode, '') AS ItemCode,
    ISNULL(ItemName, '') AS ItemName,
    ISNULL(Unit, '') AS Unit
FROM dbo.INV_ItemList
WHERE ItemID = @ItemID;", conn);
        cmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = itemId;

        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
        {
            return null;
        }

        return new InventoryItemDeleteInfo
        {
            ItemID = Convert.ToInt32(rd["ItemID"]),
            ItemCode = Convert.ToString(rd["ItemCode"]) ?? string.Empty,
            ItemName = Convert.ToString(rd["ItemName"]) ?? string.Empty,
            Unit = Convert.ToString(rd["Unit"]) ?? string.Empty
        };
    }

    private bool ItemIsInUse(SqlConnection conn, InventoryItemDeleteInfo item)
    {
        return HasTableReference(conn, "dbo", "INV_ItemList", "ParentItemID", item.ItemID, SqlDbType.Int)
            || HasTableReference(conn, "dbo", "INV_ItemFlowDetail", "ItemID", item.ItemID, SqlDbType.Int)
            || HasTableReference(conn, "dbo", "INV_ItemStoreBG", "ItemID", item.ItemID, SqlDbType.Int)
            || HasTableReference(conn, "dbo", "INV_KPGroupIndex", "ItemID", item.ItemID, SqlDbType.Int)
            || HasTableReference(conn, "dbo", "PC_PRDetail", "ItemID", item.ItemID, SqlDbType.Int)
            || HasTableReference(conn, "dbo", "MATERIAL_REQUEST_DETAIL", "ITEMCODE", item.ItemCode, SqlDbType.VarChar);
    }

    private static bool HasTableReference(SqlConnection conn, string schemaName, string tableName, string columnName, object keyValue, SqlDbType keyType)
    {
        if (!ColumnExists(conn, schemaName, tableName, columnName))
        {
            return false;
        }

        var sql = $"SELECT TOP (1) 1 FROM {QuoteSqlIdentifier(schemaName)}.{QuoteSqlIdentifier(tableName)} WHERE {QuoteSqlIdentifier(columnName)} = @KeyValue;";
        using var cmd = new SqlCommand(sql, conn);
        var parameter = cmd.Parameters.Add("@KeyValue", keyType);
        parameter.Value = keyValue;
        return cmd.ExecuteScalar() != null;
    }

    private static bool ColumnExists(SqlConnection conn, string schemaName, string tableName, string columnName)
    {
        using var cmd = new SqlCommand(@"
SELECT COUNT(1)
FROM sys.columns col
INNER JOIN sys.tables tbl ON tbl.object_id = col.object_id
INNER JOIN sys.schemas sch ON sch.schema_id = tbl.schema_id
WHERE sch.name = @SchemaName
  AND tbl.name = @TableName
  AND col.name = @ColumnName;", conn);
        cmd.Parameters.Add("@SchemaName", SqlDbType.NVarChar, 128).Value = schemaName;
        cmd.Parameters.Add("@TableName", SqlDbType.NVarChar, 128).Value = tableName;
        cmd.Parameters.Add("@ColumnName", SqlDbType.NVarChar, 128).Value = columnName;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private static string QuoteSqlIdentifier(string identifier)
    {
        return $"[{identifier.Replace("]", "]]")}]";
    }

    private void NormalizeFilter()
    {
        Filter.ItemCode = string.IsNullOrWhiteSpace(Filter.ItemCode) ? null : Filter.ItemCode.Trim();
        Filter.ItemName = string.IsNullOrWhiteSpace(Filter.ItemName) ? null : Filter.ItemName.Trim();
        Filter.ParentItemCode = null;
        Filter.PageSize = NormalizePageSize(Filter.PageSize);

        if (Filter.Page <= 0)
        {
            Filter.Page = 1;
        }

        if (Filter.ItemCatg.HasValue && Filter.ItemCatg.Value < 0)
        {
            Filter.ItemCatg = null;
        }

        if (Filter.KPGroupItem.HasValue && Filter.KPGroupItem.Value <= 0)
        {
            Filter.KPGroupItem = null;
        }

        if (Filter.ActiveStatus.HasValue && Filter.ActiveStatus.Value != 0 && Filter.ActiveStatus.Value != 1)
        {
            Filter.ActiveStatus = null;
        }

        if (Filter.StockItemId.HasValue && Filter.StockItemId.Value <= 0)
        {
            Filter.StockItemId = null;
        }

        if (!string.Equals(Filter.SpecialView, SpecialViewNoActionOneYear, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Filter.SpecialView, SpecialViewNewItems, StringComparison.OrdinalIgnoreCase))
        {
            Filter.SpecialView = null;
        }
        else
        {
            Filter.SpecialView = string.Equals(Filter.SpecialView, SpecialViewNoActionOneYear, StringComparison.OrdinalIgnoreCase)
                ? SpecialViewNoActionOneYear
                : SpecialViewNewItems;
        }
    }

    private void NormalizeQueryInputs()
    {
        Filter.ItemCode = Request.Query[nameof(Filter.ItemCode)].ToString();
        Filter.ItemName = Request.Query[nameof(Filter.ItemName)].ToString();
        Filter.ParentItemCode = null;
        Filter.ItemCatg = ParseNullableInt(Request.Query[nameof(Filter.ItemCatg)].ToString());
        Filter.KPGroupItem = ParseNullableInt(Request.Query[nameof(Filter.KPGroupItem)].ToString());
        Filter.ActiveStatus = ParseNullableInt(Request.Query[nameof(Filter.ActiveStatus)].ToString());
        Filter.SpecialView = Request.Query[nameof(Filter.SpecialView)].ToString();
        Filter.StockItemId = ParseNullableInt(Request.Query[nameof(Filter.StockItemId)].ToString());
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

    private static int? ReadNullableInt(object value)
    {
        return value == DBNull.Value ? null : Convert.ToInt32(value);
    }

    private static double? ReadNullableDouble(object value)
    {
        return value == DBNull.Value ? null : Convert.ToDouble(value);
    }

    private static decimal? ReadNullableDecimal(object value)
    {
        return value == DBNull.Value ? null : Convert.ToDecimal(value);
    }

    private static object ToDbValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
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
            Filter.ItemCode,
            Filter.ItemName,
            Filter.ItemCatg,
            Filter.KPGroupItem,
            Filter.ActiveStatus,
            Filter.SpecialView,
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

    private int GetCurrentEmployeeId()
    {
        return int.TryParse(User.FindFirst("EmployeeID")?.Value, out var employeeId) ? employeeId : 0;
    }

    private bool IsAdminRole()
    {
        var value = User.FindFirst("IsAdminRole")?.Value;
        return string.Equals(value, "True", StringComparison.OrdinalIgnoreCase) || value == "1";
    }
}

public class InventoryItemListFilter
{
    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }
    public string? ParentItemCode { get; set; }
    public int? ItemCatg { get; set; }
    public int? KPGroupItem { get; set; }
    public int? ActiveStatus { get; set; }
    public string? SpecialView { get; set; }
    public int? StockItemId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class InventoryItemInput
{
    public int ItemID { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int ItemCatg { get; set; }
    public string? Specification { get; set; }
    public string? Unit { get; set; }
    public double? UnitPrice { get; set; }
    public byte? Currency { get; set; }
    public bool IsSubItem { get; set; }
    public string? ParentItemCode { get; set; }
    public int? KPGroupItem { get; set; }
    public bool IsApartment { get; set; }
    public bool IsStock { get; set; } = true;
    public bool IsFixAsset { get; set; }
    public bool IsMaterial { get; set; } = true;
    public bool IsPurchase { get; set; } = true;
    public bool IsActive { get; set; } = true;
    public decimal? ReOrderPoint { get; set; }
    public bool IsNewItem { get; set; }
}

public class RecallLostItemCandidate
{
    public int ItemID { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string CatgCode { get; set; } = string.Empty;
    public string CatgName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public double? UnitPrice { get; set; }
    public string CurrencyName { get; set; } = string.Empty;
    public string Specification { get; set; } = string.Empty;
    public string KPGroupName { get; set; } = string.Empty;

    public string UnitPriceDisplay => UnitPrice.HasValue ? UnitPrice.Value.ToString("#,##0.####", CultureInfo.InvariantCulture) : string.Empty;
}

public class InventoryItemRow
{
    public int ItemID { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int ItemCatg { get; set; }
    public string CatgCode { get; set; } = string.Empty;
    public string CatgName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public double? UnitPrice { get; set; }
    public int? Currency { get; set; }
    public string CurrencyName { get; set; } = string.Empty;
    public string Specification { get; set; } = string.Empty;
    public int? KPGroupItem { get; set; }
    public string KPGroupName { get; set; } = string.Empty;
    public decimal? ReOrderPoint { get; set; }
    public bool IsSubItem { get; set; }
    public int? ParentItemID { get; set; }
    public string ParentItemCode { get; set; } = string.Empty;
    public bool IsApartment { get; set; }
    public bool IsStock { get; set; }
    public bool IsFixAsset { get; set; }
    public bool IsMaterial { get; set; }
    public bool IsPurchase { get; set; }
    public bool IsActive { get; set; }
    public bool IsNewItem { get; set; }
    public string CreatedDate { get; set; } = string.Empty;

    public string UnitPriceInput => UnitPrice.HasValue ? UnitPrice.Value.ToString("0.####", CultureInfo.InvariantCulture) : string.Empty;
    public string ReOrderPointInput => ReOrderPoint.HasValue ? ReOrderPoint.Value.ToString("0.##", CultureInfo.InvariantCulture) : string.Empty;
    public string UnitPriceDisplay => UnitPrice.HasValue ? UnitPrice.Value.ToString("#,##0.####", CultureInfo.InvariantCulture) : string.Empty;
    public string ReOrderPointDisplay => ReOrderPoint.HasValue ? ReOrderPoint.Value.ToString("#,##0.##", CultureInfo.InvariantCulture) : string.Empty;
    public string CreatedDateDisplay
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CreatedDate))
            {
                return string.Empty;
            }

            var formats = new[]
            {
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-dd H:mm:ss",
                "yyyy-MM-ddTHH:mm:ss",
                "M/d/yyyy h:mm:ss tt",
                "M/d/yyyy H:mm:ss"
            };

            if (DateTime.TryParseExact(CreatedDate.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var exactDate)
                || DateTime.TryParse(CreatedDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out exactDate))
            {
                return exactDate.ToString("MMM dd, yyyy HH:mm:ss", CultureInfo.InvariantCulture);
            }

            return CreatedDate;
        }
    }
    public string CategoryText => !string.IsNullOrWhiteSpace(CatgCode) ? CatgCode : (ItemCatg > 0 ? $"#{ItemCatg}" : string.Empty);
}

public class InventoryItemStockInfo
{
    public int ItemID { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal MainInventory { get; set; }
    public List<InventoryItemStoreBalance> StoreBalances { get; set; } = new List<InventoryItemStoreBalance>();
}

public class InventoryItemStoreBalance
{
    public int Year { get; set; }
    public int StoreID { get; set; }
    public string StoreName { get; set; } = string.Empty;
    public decimal BGQuantity { get; set; }
    public decimal BGAmount { get; set; }
    public string CurrencyName { get; set; } = string.Empty;
}

public class InventoryItemDeleteInfo
{
    public int ItemID { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
}

public class ParentItemLookup
{
    public int ItemID { get; set; }
    public string ItemCode { get; set; } = string.Empty;
}

public class CategoryOptionRow
{
    public int CatgID { get; set; }
    public string CatgCode { get; set; } = string.Empty;
    public string CatgName { get; set; } = string.Empty;
    public int ParentID { get; set; }
    public int CatgLevel { get; set; }

    public string DisplayText
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(CatgCode) && !string.IsNullOrWhiteSpace(CatgName))
            {
                return $"{CatgCode} - {CatgName}";
            }

            return !string.IsNullOrWhiteSpace(CatgCode) ? CatgCode : CatgName;
        }
    }
}
