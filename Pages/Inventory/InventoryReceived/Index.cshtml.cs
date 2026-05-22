using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Services;

namespace SmartSam.Pages.Inventory.InventoryReceived;

public class IndexModel : BasePageModel
{
    private const int FunctionId = 67;
    private const int PermissionViewList = 1;
    private const int PermissionViewDetail = 2;
    private const int PermissionAdd = 3;
    private const int PermissionEdit = 4;
    private readonly PermissionService _permissionService;

    public IndexModel(IConfiguration config, PermissionService permissionService) : base(config)
    {
        _permissionService = permissionService;
    }

    public PagePermissions PagePerm { get; private set; } = new();
    [BindProperty(SupportsGet = true)] public InventoryReceivedFilter Filter { get; set; } = new();
    public List<SelectListItem> ReceiveTypes { get; set; } = new();
    public List<SelectListItem> Statuses { get; set; } = new();
    public List<SelectListItem> Stores { get; set; } = new();
    public List<InventoryReceivedRow> Rows { get; set; } = new();
    public int TotalRecords { get; set; }
    public int TotalPages => Filter.PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)Filter.PageSize));
    public int PageStart => TotalRecords == 0 ? 0 : ((Filter.Page - 1) * Filter.PageSize) + 1;
    public int PageEnd => TotalRecords == 0 ? 0 : Math.Min(Filter.Page * Filter.PageSize, TotalRecords);
    public bool HasPreviousPage => Filter.Page > 1;
    public bool HasNextPage => Filter.Page < TotalPages;
    public IReadOnlyList<int> PageSizeOptions => [10, 20, 50, 100, 200];

    public IActionResult OnGet()
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewList)) return Redirect("/");
        NormalizeFilter();
        LoadLookups();
        LoadRows();
        return Page();
    }

    public IActionResult OnGetSearchDetail(string? keyword, int pageNumber = 1, int pageSize = 10)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewList))
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return new JsonResult(new { message = "No permission." });
        }

        pageNumber = pageNumber <= 0 ? 1 : pageNumber;
        pageSize = pageSize is 10 or 20 or 50 or 100 or 200 ? pageSize : 10;

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        var where = @" WHERE h.FlowType = 2
AND (@KPGroupId IS NULL OR h.KPGroup = @KPGroupId)
AND (@Keyword IS NULL OR i.ItemCode LIKE '%' + @Keyword + '%' OR i.ItemName LIKE '%' + @Keyword + '%' OR h.FlowNo LIKE '%' + @Keyword + '%') ";

        using var countCmd = new SqlCommand("SELECT COUNT(1) FROM dbo.INV_ItemFlowDetail d INNER JOIN dbo.INV_ItemFlow h ON h.FlowID=d.FlowID INNER JOIN dbo.INV_ItemList i ON i.ItemID=d.ItemID " + where, conn);
        var kpGroupId = GetCurrentKpGroupId();
        countCmd.Parameters.Add("@KPGroupId", SqlDbType.Int).Value = kpGroupId > 0 ? kpGroupId : DBNull.Value;
        countCmd.Parameters.Add("@Keyword", SqlDbType.NVarChar, 150).Value = string.IsNullOrWhiteSpace(keyword) ? DBNull.Value : keyword.Trim();
        var total = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);

        using var cmd = new SqlCommand(@"SELECT h.FlowID,h.FlowNo,CONVERT(varchar(10),h.FlowDate,103) AS FlowDateText,i.ItemCode,i.ItemName,d.Act_Qty,d.UnitPrice,d.Amount
FROM dbo.INV_ItemFlowDetail d
INNER JOIN dbo.INV_ItemFlow h ON h.FlowID=d.FlowID
INNER JOIN dbo.INV_ItemList i ON i.ItemID=d.ItemID " + where + @"
ORDER BY h.FlowDate DESC, h.FlowID DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", conn);
        cmd.Parameters.Add("@KPGroupId", SqlDbType.Int).Value = kpGroupId > 0 ? kpGroupId : DBNull.Value;
        cmd.Parameters.Add("@Keyword", SqlDbType.NVarChar, 150).Value = string.IsNullOrWhiteSpace(keyword) ? DBNull.Value : keyword.Trim();
        cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = (pageNumber - 1) * pageSize;
        cmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;

        var items = new List<object>();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            items.Add(new
            {
                flowId = Convert.ToInt64(rd["FlowID"]),
                flowNo = Convert.ToString(rd["FlowNo"]) ?? string.Empty,
                flowDate = Convert.ToString(rd["FlowDateText"]) ?? string.Empty,
                itemCode = Convert.ToString(rd["ItemCode"]) ?? string.Empty,
                itemName = Convert.ToString(rd["ItemName"]) ?? string.Empty,
                actQty = Convert.ToDecimal(rd["Act_Qty"] == DBNull.Value ? 0 : rd["Act_Qty"]),
                unitPrice = Convert.ToDecimal(rd["UnitPrice"] == DBNull.Value ? 0 : rd["UnitPrice"]),
                amount = Convert.ToDecimal(rd["Amount"] == DBNull.Value ? 0 : rd["Amount"])
            });
        }

        var totalPages = pageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(total / (double)pageSize));
        return new JsonResult(new { rows = items, totalRecords = total, pageNumber, pageSize, totalPages });
    }

    public IActionResult OnPostDelete(long id)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionEdit)) return Redirect("/");

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var tran = conn.BeginTransaction();

        try
        {
            int flowStatusId;
            using (var getStatusCmd = new SqlCommand("SELECT ISNULL(StatusID,0) FROM dbo.INV_ItemFlow WHERE FlowID=@FlowID", conn, tran))
            {
                getStatusCmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = id;
                flowStatusId = Convert.ToInt32(getStatusCmd.ExecuteScalar() ?? 0);
            }

            if (flowStatusId > 1)
            {
                tran.Rollback();
                TempData["Message"] = "Confirmed vouchers cannot be deleted.";
                TempData["MessageType"] = "warning";
                return RedirectToPage("./Index", new
                {
                    FlowNo = Filter.FlowNo,
                    PONo = Filter.PONo,
                    ToStoreId = Filter.ToStoreId,
                    RecTypeId = Filter.RecTypeId,
                    StatusId = Filter.StatusId,
                    FromDate = Filter.FromDate?.ToString("yyyy-MM-dd"),
                    ToDate = Filter.ToDate?.ToString("yyyy-MM-dd"),
                    Page = Filter.Page,
                    PageSize = Filter.PageSize
                });
            }

            int? poId;
            using (var getPoCmd = new SqlCommand("SELECT POID FROM dbo.INV_ItemFlow WHERE FlowID=@FlowID", conn, tran))
            {
                getPoCmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = id;
                var poObj = getPoCmd.ExecuteScalar();
                poId = poObj == null || poObj == DBNull.Value ? null : Convert.ToInt32(poObj);
            }

            var oldDetails = new List<(int ItemId, long? ChekingDTID, long? MRDetailID, decimal ActQty, decimal UnitPrice)>();
            using (var loadDetails = new SqlCommand(@"SELECT ItemID, ChekingDTID, MRDetailID, ISNULL(Act_Qty,0) AS ActQty, ISNULL(UnitPrice,0) AS UnitPrice
FROM dbo.INV_ItemFlowDetail
WHERE FlowID=@FlowID", conn, tran))
            {
                loadDetails.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = id;
                using var rd = loadDetails.ExecuteReader();
                while (rd.Read())
                {
                    oldDetails.Add((
                        Convert.ToInt32(rd["ItemID"]),
                        rd["ChekingDTID"] == DBNull.Value ? null : Convert.ToInt64(rd["ChekingDTID"]),
                        rd["MRDetailID"] == DBNull.Value ? null : Convert.ToInt64(rd["MRDetailID"]),
                        Convert.ToDecimal(rd["ActQty"] == DBNull.Value ? 0 : rd["ActQty"]),
                        Convert.ToDecimal(rd["UnitPrice"] == DBNull.Value ? 0 : rd["UnitPrice"]) 
                    ));
                }
            }

            foreach (var d in oldDetails)
            {
                if (d.ChekingDTID.HasValue && d.ChekingDTID.Value > 0)
                {
                    using var updateChecking = new SqlCommand(@"UPDATE dbo.INV_ReceivingChekingVoucherDT
SET QuantityRec = CASE WHEN ISNULL(QuantityRec,0) - @RecQty < 0 THEN 0 ELSE ISNULL(QuantityRec,0) - @RecQty END
WHERE ChekingDTID = @ChekingDTID", conn, tran);
                    updateChecking.Parameters.Add("@RecQty", SqlDbType.Decimal).Value = d.ActQty;
                    updateChecking.Parameters.Add("@ChekingDTID", SqlDbType.BigInt).Value = d.ChekingDTID.Value;
                    updateChecking.ExecuteNonQuery();
                }

                if (poId.HasValue && poId.Value > 0 && d.MRDetailID.HasValue && d.MRDetailID.Value > 0)
                {
                    using var updatePoDetail = new SqlCommand(@"UPDATE dbo.PC_PODetail
SET RecQty = CASE WHEN ISNULL(RecQty,0) - @RecQty < 0 THEN 0 ELSE ISNULL(RecQty,0) - @RecQty END,
    RecAmount = CASE WHEN ISNULL(RecAmount,0) - @RecAmount < 0 THEN 0 ELSE ISNULL(RecAmount,0) - @RecAmount END
WHERE POID = @POID AND ItemID = @ItemID AND MRDetailID = @MRDetailID", conn, tran);
                    updatePoDetail.Parameters.Add("@RecQty", SqlDbType.Decimal).Value = d.ActQty;
                    updatePoDetail.Parameters.Add("@RecAmount", SqlDbType.Decimal).Value = d.ActQty * d.UnitPrice;
                    updatePoDetail.Parameters.Add("@POID", SqlDbType.Int).Value = poId.Value;
                    updatePoDetail.Parameters.Add("@ItemID", SqlDbType.Int).Value = d.ItemId;
                    updatePoDetail.Parameters.Add("@MRDetailID", SqlDbType.BigInt).Value = d.MRDetailID.Value;
                    updatePoDetail.ExecuteNonQuery();

                    using var syncReceived = new SqlCommand(@"UPDATE dbo.PC_PODetail
SET IsReceived = CASE WHEN ISNULL(RecQty,0) > 0 THEN 1 ELSE 0 END
WHERE POID = @POID AND ItemID = @ItemID AND MRDetailID = @MRDetailID", conn, tran);
                    syncReceived.Parameters.Add("@POID", SqlDbType.Int).Value = poId.Value;
                    syncReceived.Parameters.Add("@ItemID", SqlDbType.Int).Value = d.ItemId;
                    syncReceived.Parameters.Add("@MRDetailID", SqlDbType.BigInt).Value = d.MRDetailID.Value;
                    syncReceived.ExecuteNonQuery();
                }
            }

            using (var deleteDetail = new SqlCommand("DELETE FROM dbo.INV_ItemFlowDetail WHERE FlowID=@FlowID", conn, tran))
            {
                deleteDetail.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = id;
                deleteDetail.ExecuteNonQuery();
            }

            using (var deleteDoc = new SqlCommand("DELETE FROM dbo.INV_ItemFlow_Doc WHERE FlowID=@FlowID", conn, tran))
            {
                deleteDoc.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = id;
                deleteDoc.ExecuteNonQuery();
            }

            using (var deleteHeader = new SqlCommand("DELETE FROM dbo.INV_ItemFlow WHERE FlowID=@FlowID", conn, tran))
            {
                deleteHeader.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = id;
                deleteHeader.ExecuteNonQuery();
            }

            if (poId.HasValue && poId.Value > 0)
            {
                decimal totalQty;
                decimal recQty;
                using (var statusCmd = new SqlCommand(@"SELECT ISNULL(SUM(ISNULL(Quantity,0)),0), ISNULL(SUM(ISNULL(RecQty,0)),0)
FROM dbo.PC_PODetail
WHERE POID = @POID", conn, tran))
                {
                    statusCmd.Parameters.Add("@POID", SqlDbType.Int).Value = poId.Value;
                    using var rd = statusCmd.ExecuteReader();
                    rd.Read();
                    totalQty = rd.IsDBNull(0) ? 0 : Convert.ToDecimal(rd.GetValue(0));
                    recQty = rd.IsDBNull(1) ? 0 : Convert.ToDecimal(rd.GetValue(1));
                }

                var statusId = recQty >= totalQty && totalQty > 0 ? 7 : recQty > 0 ? 6 : 5;
                using var updateStatus = new SqlCommand("UPDATE dbo.PC_PO SET StatusID = @StatusID WHERE POID = @POID", conn, tran);
                updateStatus.Parameters.Add("@StatusID", SqlDbType.Int).Value = statusId;
                updateStatus.Parameters.Add("@POID", SqlDbType.Int).Value = poId.Value;
                updateStatus.ExecuteNonQuery();
            }

            tran.Commit();
        }
        catch
        {
            tran.Rollback();
            throw;
        }

        return RedirectToPage("./Index", new
        {
            FlowNo = Filter.FlowNo,
            PONo = Filter.PONo,
            ToStoreId = Filter.ToStoreId,
            RecTypeId = Filter.RecTypeId,
            StatusId = Filter.StatusId,
            FromDate = Filter.FromDate?.ToString("yyyy-MM-dd"),
            ToDate = Filter.ToDate?.ToString("yyyy-MM-dd"),
            Page = Filter.Page,
            PageSize = Filter.PageSize
        });
    }

    private void LoadLookups()
    {
        ReceiveTypes = LoadListFromSql("SELECT FlowSubTypeID, FlowSubTypeName FROM dbo.INV_FlowSubType WHERE FlowTypeID=2 ORDER BY FlowSubTypeName", "FlowSubTypeID", "FlowSubTypeName", true);
        Statuses = LoadListFromSql(@"SELECT CAST(StatusID AS varchar(20)) AS Value, StatusName AS Text FROM dbo.INV_ItemFlowReceiveStatus
UNION
SELECT CAST(ID AS varchar(20)) AS Value, Name AS Text FROM dbo.INV_ItemReturnStatus
ORDER BY Text", "Value", "Text", true);
        Stores = BuildStoreTreeOptions();
    }

    private List<SelectListItem> BuildStoreTreeOptions()
    {
        var results = new List<SelectListItem> { new("--- Select ---", "") };
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand(@"SELECT g.KPGroupID, g.KPGroupName, s.StoreID, s.StoreName
FROM dbo.INV_KPGroup g
INNER JOIN dbo.INV_StoreList s ON s.DeptID = g.KPGroupID
ORDER BY g.KPGroupName, s.StoreName", conn);
        using var rd = cmd.ExecuteReader();
        var groupMap = new Dictionary<int, SelectListGroup>();
        while (rd.Read())
        {
            var groupId = Convert.ToInt32(rd["KPGroupID"]);
            if (!groupMap.ContainsKey(groupId))
            {
                groupMap[groupId] = new SelectListGroup { Name = Convert.ToString(rd["KPGroupName"]) ?? string.Empty };
            }

            results.Add(new SelectListItem
            {
                Value = Convert.ToString(rd["StoreID"]),
                Text = Convert.ToString(rd["StoreName"]) ?? string.Empty,
                Group = groupMap[groupId]
            });
        }

        return results;
    }

    private void LoadRows()
    {
        Rows.Clear();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        using (var countCmd = new SqlCommand(@"SELECT COUNT(1)
FROM dbo.INV_ItemFlow h
LEFT JOIN dbo.PC_PO po ON po.POID = h.POID
WHERE h.FlowType = 2
AND (@KPGroupId IS NULL OR h.KPGroup = @KPGroupId)
AND h.FlowDate >= @FromDate AND h.FlowDate < DATEADD(DAY,1,@ToDate)
AND (@FlowNo IS NULL OR h.FlowNo LIKE '%' + @FlowNo + '%')
AND (@PoNo IS NULL OR po.PONo LIKE '%' + @PoNo + '%')
AND (@ToStore IS NULL OR h.ToStore = @ToStore)
AND (@RecType IS NULL OR h.FlowSubType = @RecType)
AND (@StatusId IS NULL OR h.StatusID = @StatusId)", conn))
        {
            BindFilterParams(countCmd);
            TotalRecords = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);
        }

        using var cmd = new SqlCommand(@"SELECT h.FlowID,h.FlowNo,h.FlowDate,h.According,h.StatusID,
       COALESCE(rs.StatusName, rts.Name, CONVERT(varchar(20), h.StatusID)) AS StatusName,
       s.StoreName,po.PONo, fst.FlowSubTypeName AS RecTypeName
FROM dbo.INV_ItemFlow h
LEFT JOIN dbo.INV_StoreList s ON s.StoreID = h.ToStore
LEFT JOIN dbo.PC_PO po ON po.POID = h.POID
LEFT JOIN dbo.INV_FlowSubType fst ON fst.FlowSubTypeID = h.FlowSubType AND fst.FlowTypeID = 2
LEFT JOIN dbo.INV_ItemFlowReceiveStatus rs ON rs.StatusID = h.StatusID AND (h.FlowSubType = 1 OR h.FlowSubType = 6)
LEFT JOIN dbo.INV_ItemReturnStatus rts ON rts.ID = h.StatusID AND ISNULL(h.FlowSubType,0) NOT IN (1,6)
WHERE h.FlowType = 2
AND (@KPGroupId IS NULL OR h.KPGroup = @KPGroupId)
AND h.FlowDate >= @FromDate AND h.FlowDate < DATEADD(DAY,1,@ToDate)
AND (@FlowNo IS NULL OR h.FlowNo LIKE '%' + @FlowNo + '%')
AND (@PoNo IS NULL OR po.PONo LIKE '%' + @PoNo + '%')
AND (@ToStore IS NULL OR h.ToStore = @ToStore)
AND (@RecType IS NULL OR h.FlowSubType = @RecType)
AND (@StatusId IS NULL OR h.StatusID = @StatusId)
ORDER BY h.FlowDate DESC, h.FlowID DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", conn);
        BindFilterParams(cmd);
        cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = (Filter.Page - 1) * Filter.PageSize;
        cmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = Filter.PageSize;

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            Rows.Add(new InventoryReceivedRow
            {
                FlowID = Convert.ToInt64(rd["FlowID"]),
                FlowNo = Convert.ToString(rd["FlowNo"]) ?? string.Empty,
                FlowDate = Convert.ToDateTime(rd["FlowDate"]),
                StatusID = Convert.ToInt32(rd["StatusID"] == DBNull.Value ? 0 : rd["StatusID"]),
                According = Convert.ToString(rd["According"]) ?? string.Empty,
                StatusName = Convert.ToString(rd["StatusName"]) ?? Convert.ToString(rd["StatusID"]) ?? string.Empty,
                StoreName = Convert.ToString(rd["StoreName"]) ?? string.Empty,
                PONo = Convert.ToString(rd["PONo"]) ?? string.Empty,
                SupplierName = Convert.ToString(rd["RecTypeName"]) ?? string.Empty
            });
        }
    }

    private void BindFilterParams(SqlCommand cmd)
    {
        var kpGroupId = GetCurrentKpGroupId();
        cmd.Parameters.Add("@KPGroupId", SqlDbType.Int).Value = kpGroupId > 0 ? kpGroupId : DBNull.Value;
        cmd.Parameters.Add("@FromDate", SqlDbType.Date).Value = Filter.FromDate!.Value.Date;
        cmd.Parameters.Add("@ToDate", SqlDbType.Date).Value = Filter.ToDate!.Value.Date;
        cmd.Parameters.Add("@FlowNo", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(Filter.FlowNo) ? DBNull.Value : Filter.FlowNo.Trim();
        cmd.Parameters.Add("@PoNo", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(Filter.PONo) ? DBNull.Value : Filter.PONo.Trim();
        cmd.Parameters.Add("@ToStore", SqlDbType.Int).Value = Filter.ToStoreId.HasValue && Filter.ToStoreId > 0 ? Filter.ToStoreId.Value : DBNull.Value;
        cmd.Parameters.Add("@RecType", SqlDbType.Int).Value = Filter.RecTypeId.HasValue && Filter.RecTypeId > 0 ? Filter.RecTypeId.Value : DBNull.Value;
        cmd.Parameters.Add("@StatusId", SqlDbType.Int).Value = Filter.StatusId.HasValue && Filter.StatusId > 0 ? Filter.StatusId.Value : DBNull.Value;
    }

    private void NormalizeFilter()
    {
        if (!Filter.FromDate.HasValue) Filter.FromDate = DateTime.Today.AddDays(-2);
        if (!Filter.ToDate.HasValue) Filter.ToDate = DateTime.Today;
        if (Filter.Page <= 0) Filter.Page = 1;
        if (Filter.PageSize is not (10 or 20 or 50 or 100 or 200)) Filter.PageSize = 10;
    }

    private int GetCurrentRoleId() => int.Parse(User.FindFirst("RoleID")?.Value ?? "0");
    private bool IsAdminRole() => User.FindFirst("IsAdminRole")?.Value == "True";
    private int GetCurrentKpGroupId() => int.Parse(User.FindFirst("KPGroupID")?.Value ?? "0");
    private PagePermissions GetUserPermissions() => IsAdminRole() ? new PagePermissions { AllowedNos = Enumerable.Range(1, 20).ToList() } : new PagePermissions { AllowedNos = _permissionService.GetPermissionsForPage(GetCurrentRoleId(), FunctionId) };
}

public class InventoryReceivedFilter
{
    public string? FlowNo { get; set; }
    public string? PONo { get; set; }
    public int? ToStoreId { get; set; }
    public int? RecTypeId { get; set; }
    public int? StatusId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class InventoryReceivedRow
{
    public long FlowID { get; set; }
    public string FlowNo { get; set; } = string.Empty;
    public DateTime FlowDate { get; set; }
    public int StatusID { get; set; }
    public string According { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
    public string PONo { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
}
