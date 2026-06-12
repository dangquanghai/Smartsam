using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Services;

namespace SmartSam.Pages.Inventory.InventoryIssue;

public class IndexModel : BasePageModel
{
    private const int FunctionId = 66;
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
    [BindProperty(SupportsGet = true)] public InventoryIssueFilter Filter { get; set; } = new();
    public List<SelectListItem> IssueTypes { get; set; } = new();
    public List<SelectListItem> Statuses { get; set; } = new();
    public List<SelectListItem> Stores { get; set; } = new();
    public List<InventoryIssueRow> Rows { get; set; } = new();
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

    public IActionResult OnGetSearchDetail(string? keyword, DateTime? fromDate, DateTime? toDate, int pageNumber = 1, int pageSize = 10)
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

var where = @" WHERE h.FlowType = 1
AND h.KPGroup = @KPGroupId
AND (@FromDate IS NULL OR CONVERT(date,h.FlowDate) >= @FromDate)
AND (@ToDate IS NULL OR CONVERT(date,h.FlowDate) <= @ToDate)
AND (@Keyword IS NULL OR i.ItemCode LIKE '%' + @Keyword + '%' OR i.ItemName LIKE '%' + @Keyword + '%' OR h.FlowNo LIKE '%' + @Keyword + '%') ";

        using var countCmd = new SqlCommand("SELECT COUNT(1) FROM dbo.INV_ItemFlowDetail d INNER JOIN dbo.INV_ItemFlow h ON h.FlowID=d.FlowID INNER JOIN dbo.INV_ItemList i ON i.ItemID=d.ItemID " + where, conn);
        var kpGroupId = GetCurrentKpGroupId();
        countCmd.Parameters.Add("@KPGroupId", SqlDbType.Int).Value = kpGroupId;
        countCmd.Parameters.Add("@FromDate", SqlDbType.Date).Value = fromDate.HasValue ? fromDate.Value.Date : DBNull.Value;
        countCmd.Parameters.Add("@ToDate", SqlDbType.Date).Value = toDate.HasValue ? toDate.Value.Date : DBNull.Value;
        countCmd.Parameters.Add("@Keyword", SqlDbType.NVarChar, 150).Value = string.IsNullOrWhiteSpace(keyword) ? DBNull.Value : keyword.Trim();
        var total = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);

        using var cmd = new SqlCommand(@"SELECT h.FlowID,h.FlowNo,CONVERT(varchar(10),h.FlowDate,103) AS FlowDateText,i.ItemCode,i.ItemName,d.Act_Qty,d.UnitPrice,d.Amount
FROM dbo.INV_ItemFlowDetail d
INNER JOIN dbo.INV_ItemFlow h ON h.FlowID=d.FlowID
INNER JOIN dbo.INV_ItemList i ON i.ItemID=d.ItemID " + where + @"
ORDER BY h.FlowDate DESC, h.FlowID DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", conn);
        cmd.Parameters.Add("@KPGroupId", SqlDbType.Int).Value = kpGroupId;
        cmd.Parameters.Add("@FromDate", SqlDbType.Date).Value = fromDate.HasValue ? fromDate.Value.Date : DBNull.Value;
        cmd.Parameters.Add("@ToDate", SqlDbType.Date).Value = toDate.HasValue ? toDate.Value.Date : DBNull.Value;
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
            var affectedRequestNos = new HashSet<long>();
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
                if (d.MRDetailID.HasValue && d.MRDetailID.Value > 0 && d.ActQty > 0)
                {
                    using var rollbackMrIssued = new SqlCommand(@"UPDATE dbo.MATERIAL_REQUEST_DETAIL
SET ISSUED = CASE WHEN ISNULL(ISSUED,0) - @Qty < 0 THEN 0 ELSE ISNULL(ISSUED,0) - @Qty END
WHERE ID = @Id", conn, tran);
                    rollbackMrIssued.Parameters.Add("@Qty", SqlDbType.Decimal).Value = d.ActQty;
                    rollbackMrIssued.Parameters.Add("@Id", SqlDbType.BigInt).Value = d.MRDetailID.Value;
                    rollbackMrIssued.ExecuteNonQuery();

                    using var getRequestNo = new SqlCommand("SELECT REQUEST_NO FROM dbo.MATERIAL_REQUEST_DETAIL WHERE ID=@ID", conn, tran);
                    getRequestNo.Parameters.Add("@ID", SqlDbType.BigInt).Value = d.MRDetailID.Value;
                    var requestNoObj = getRequestNo.ExecuteScalar();
                    if (requestNoObj != null && requestNoObj != DBNull.Value)
                    {
                        affectedRequestNos.Add(Convert.ToInt64(requestNoObj));
                    }
                }

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

                    using var syncIssue = new SqlCommand(@"UPDATE dbo.PC_PODetail
SET IsIssue = CASE WHEN ISNULL(RecQty,0) > 0 THEN 1 ELSE 0 END
WHERE POID = @POID AND ItemID = @ItemID AND MRDetailID = @MRDetailID", conn, tran);
                    syncIssue.Parameters.Add("@POID", SqlDbType.Int).Value = poId.Value;
                    syncIssue.Parameters.Add("@ItemID", SqlDbType.Int).Value = d.ItemId;
                    syncIssue.Parameters.Add("@MRDetailID", SqlDbType.BigInt).Value = d.MRDetailID.Value;
                    syncIssue.ExecuteNonQuery();
                }
            }

            foreach (var requestNo in affectedRequestNos)
            {
                using var calcMrStatus = new SqlCommand(@"SELECT SUM(ISNULL(ISSUED,0)) - SUM(ISNULL(NEW_ORDER,0))
FROM dbo.MATERIAL_REQUEST_DETAIL
WHERE REQUEST_NO=@RequestNo", conn, tran);
                calcMrStatus.Parameters.Add("@RequestNo", SqlDbType.BigInt).Value = requestNo;
                var diff = Convert.ToDecimal(calcMrStatus.ExecuteScalar() ?? 0m);

                using var updateMrStatus = new SqlCommand("UPDATE dbo.MATERIAL_REQUEST SET MATERIALSTATUSID=@StatusID WHERE REQUEST_NO=@RequestNo", conn, tran);
                updateMrStatus.Parameters.Add("@RequestNo", SqlDbType.BigInt).Value = requestNo;
                updateMrStatus.Parameters.Add("@StatusID", SqlDbType.Int).Value = diff >= 0 ? 6 : 2;
                updateMrStatus.ExecuteNonQuery();
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
        IssueTypes = LoadListFromSql("SELECT FlowSubTypeID, FlowSubTypeName FROM dbo.INV_FlowSubType WHERE FlowTypeID=1 ORDER BY FlowSubTypeName", "FlowSubTypeID", "FlowSubTypeName", true);
        Statuses = LoadStatusFilterOptions();
        Stores = BuildStoreTreeOptions();
    }

    private List<SelectListItem> LoadStatusFilterOptions()
    {
        var issueGroup = new SelectListGroup { Name = "Issue Status" };
        var returnGroup = new SelectListGroup { Name = "Return Status" };
        var results = new List<SelectListItem> { new("--- All ---", string.Empty) };

        results.AddRange(LoadListFromSql("SELECT CAST(StatusID AS varchar(20)) AS Value, StatusName AS Text FROM dbo.INV_ItemFlowIssueStatus ORDER BY StatusID", "Value", "Text").Select(x =>
        {
            x.Group = issueGroup;
            return x;
        }));
        results.AddRange(LoadListFromSql("SELECT CAST(ID AS varchar(20)) AS Value, Name AS Text FROM dbo.INV_ItemReturnStatus ORDER BY ID", "Value", "Text").Select(x =>
        {
            x.Group = returnGroup;
            return x;
        }));
        return results;
    }

    private List<SelectListItem> BuildStoreTreeOptions()
    {
        var results = new List<SelectListItem> { new("--- Select ---", "") };
        var currentKpGroupId = GetCurrentKpGroupId();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand(@"SELECT g.KPGroupID, g.KPGroupName, s.StoreID, s.StoreName
FROM dbo.INV_KPGroup g
INNER JOIN dbo.INV_StoreList s ON s.DeptID = g.KPGroupID
WHERE s.DeptID = @CurrentKpGroupID
ORDER BY g.KPGroupName, s.StoreName", conn);
        cmd.Parameters.Add("@CurrentKpGroupID", SqlDbType.Int).Value = currentKpGroupId > 0 ? currentKpGroupId : DBNull.Value;
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
WHERE h.FlowType = 1
AND h.KPGroup = @KPGroupId
AND h.FlowDate >= @FromDate AND h.FlowDate < DATEADD(DAY,1,@ToDate)
AND (@FlowNo IS NULL OR h.FlowNo LIKE '%' + @FlowNo + '%')
AND (@PoNo IS NULL OR po.PONo LIKE '%' + @PoNo + '%')
AND (@ToStore IS NULL OR h.FromStore = @ToStore)
AND (@RecType IS NULL OR h.FlowSubType = @RecType)
AND (@StatusId IS NULL OR h.StatusID = @StatusId)", conn))
        {
            BindFilterParams(countCmd);
            TotalRecords = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);
        }

        using var cmd = new SqlCommand(@"SELECT h.FlowID,h.FlowNo,h.FlowDate,h.According,h.StatusID,
       COALESCE(rs.StatusName, CONVERT(varchar(20), h.StatusID)) AS StatusName,
       s.StoreName,po.PONo, fst.FlowSubTypeName AS RecTypeName
FROM dbo.INV_ItemFlow h
LEFT JOIN dbo.INV_StoreList s ON s.StoreID = h.FromStore
LEFT JOIN dbo.PC_PO po ON po.POID = h.POID
LEFT JOIN dbo.INV_FlowSubType fst ON fst.FlowSubTypeID = h.FlowSubType AND fst.FlowTypeID = 1
LEFT JOIN dbo.INV_ItemFlowIssueStatus rs ON rs.StatusID = h.StatusID
WHERE h.FlowType = 1
AND h.KPGroup = @KPGroupId
AND h.FlowDate >= @FromDate AND h.FlowDate < DATEADD(DAY,1,@ToDate)
AND (@FlowNo IS NULL OR h.FlowNo LIKE '%' + @FlowNo + '%')
AND (@PoNo IS NULL OR po.PONo LIKE '%' + @PoNo + '%')
AND (@ToStore IS NULL OR h.FromStore = @ToStore)
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
            Rows.Add(new InventoryIssueRow
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
        cmd.Parameters.Add("@KPGroupId", SqlDbType.Int).Value = kpGroupId;
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
    private int GetCurrentKpGroupId()
    {
        if (int.TryParse(User.FindFirst("KPGroupID")?.Value, out var kpGroupFromClaim) && kpGroupFromClaim > 0)
        {
            return kpGroupFromClaim;
        }

        var employeeId = int.Parse(User.FindFirst("EmployeeID")?.Value ?? User.FindFirst("EmpID")?.Value ?? "0");
        using var connEmployee = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmdEmployee = new SqlCommand("SELECT TOP 1 StoreGR FROM dbo.MS_Employee WHERE EmployeeID=@EmployeeID", connEmployee);
        cmdEmployee.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId;
        connEmployee.Open();
        var kpGroupFromEmployee = Convert.ToInt32(cmdEmployee.ExecuteScalar() ?? 0);
        if (kpGroupFromEmployee > 0) return kpGroupFromEmployee;

        var employeeCode = User.FindFirst("EmployeeCode")?.Value;
        if (!string.IsNullOrWhiteSpace(employeeCode))
        {
            using var cmdEmployeeCode = new SqlCommand("SELECT TOP 1 StoreGR FROM dbo.MS_Employee WHERE EmployeeCode=@EmployeeCode", connEmployee);
            cmdEmployeeCode.Parameters.Add("@EmployeeCode", SqlDbType.VarChar, 50).Value = employeeCode.Trim();
            var kpGroupFromCode = Convert.ToInt32(cmdEmployeeCode.ExecuteScalar() ?? 0);
            if (kpGroupFromCode > 0) return kpGroupFromCode;
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand("SELECT TOP 1 KPGroupID FROM dbo.INV_KPGroupMember WHERE EmployeeID=@EmployeeID ORDER BY KPGroupID", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId;
        conn.Open();
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }
    private PagePermissions GetUserPermissions() => IsAdminRole() ? new PagePermissions { AllowedNos = Enumerable.Range(1, 20).ToList() } : new PagePermissions { AllowedNos = _permissionService.GetPermissionsForPage(GetCurrentRoleId(), FunctionId) };
}

public class InventoryIssueFilter
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

public class InventoryIssueRow
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



