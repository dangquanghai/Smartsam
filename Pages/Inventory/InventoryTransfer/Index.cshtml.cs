using ClosedXML.Excel;
using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Services;

namespace SmartSam.Pages.Inventory.InventoryTransfer;

public class IndexModel : BasePageModel
{
    private const string ExcelTcvn3FontName = ".VnTime";
    private const int FunctionId = 68;
    private const int PermissionViewList = 1;
    private const int PermissionViewDetail = 2;
    private const int PermissionAdd = 3;
    private const int PermissionEdit = 4;
    private const int PermissionDelete = 5;
    private readonly PermissionService _permissionService;

    public IndexModel(IConfiguration config, PermissionService permissionService) : base(config)
    {
        _permissionService = permissionService;
    }

    public PagePermissions PagePerm { get; private set; } = new();
    [BindProperty(SupportsGet = true)] public InventoryTransferFilter Filter { get; set; } = new();
    public List<SelectListItem> Stores { get; set; } = new();
    public List<InventoryTransferRow> Rows { get; set; } = new();
    public int TotalRecords { get; set; }
    public IReadOnlyList<int> PageSizeOptions => [10, 20, 50, 100, 200];
    public int TotalPages => Filter.PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)Filter.PageSize));
    public int PageStart => TotalRecords == 0 ? 0 : ((Filter.Page - 1) * Filter.PageSize) + 1;
    public int PageEnd => TotalRecords == 0 ? 0 : Math.Min(Filter.Page * Filter.PageSize, TotalRecords);
    public bool HasPreviousPage => Filter.Page > 1;
    public bool HasNextPage => Filter.Page < TotalPages;

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
        fromDate ??= DateTime.Today.AddDays(-2);
        toDate ??= DateTime.Today;

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        var where = @" WHERE h.FlowType = 3
AND (
    @AllowAllStores = 1
    OR (
        @KPGroupId IS NOT NULL
        AND fs.DeptID = @KPGroupId
        AND ts.DeptID = @KPGroupId
    )
)
AND h.FlowDate >= @FromDate AND h.FlowDate < DATEADD(DAY, 1, @ToDate)
AND (@Keyword IS NULL OR COALESCE(k.ItemCode, i.ItemCode) LIKE '%' + @Keyword + '%' OR COALESCE(k.ItemName, i.ItemName) LIKE '%' + @Keyword + '%' OR h.FlowNo LIKE '%' + @Keyword + '%') ";

        var kpGroupId = GetCurrentKpGroupId();
        var allowAllStores = IsAdminRole() || kpGroupId == 1;
        var searchJoin = @" FROM dbo.INV_ItemFlowDetail d
INNER JOIN dbo.INV_ItemFlow h ON h.FlowID=d.FlowID
INNER JOIN dbo.INV_ItemList i ON i.ItemID=d.ItemID
LEFT JOIN dbo.INV_KPGroupIndex k ON k.ItemID=d.ItemID AND k.KPGroupID=@ItemKpGroupId
LEFT JOIN dbo.INV_StoreList fs ON fs.StoreID=h.FromStore
LEFT JOIN dbo.INV_StoreList ts ON ts.StoreID=h.ToStore ";

        using var countCmd = new SqlCommand("SELECT COUNT(1)" + searchJoin + where, conn);
        BindSearchDetailParams(countCmd, kpGroupId, allowAllStores, keyword, fromDate, toDate);
        var total = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);

        using var cmd = new SqlCommand(@"SELECT h.FlowID,h.FlowNo,CONVERT(varchar(10),h.FlowDate,103) AS FlowDateText,
       COALESCE(k.ItemCode, i.ItemCode) AS ItemCode,
       COALESCE(k.ItemName, i.ItemName) AS ItemName,
       d.Act_Qty,d.UnitPrice,d.Amount" + searchJoin + where + @"
ORDER BY h.FlowDate DESC, h.FlowID DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", conn);
        BindSearchDetailParams(cmd, kpGroupId, allowAllStores, keyword, fromDate, toDate);
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

        return new JsonResult(new
        {
            rows = items,
            pageNumber,
            pageSize,
            totalRecords = total,
            totalPages = pageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(total / (double)pageSize))
        });
    }


    public IActionResult OnGetExport(long id)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewDetail)) return Redirect("/");
        if (id <= 0) return NotFound();

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var headerCmd = new SqlCommand(@"SELECT h.FlowNo, h.FlowDate,
       ISNULL(h.According,'') AS According,
       ISNULL(fs.StoreName,'') AS FromStoreName,
       ISNULL(ts.StoreName,'') AS ToStoreName
FROM dbo.INV_ItemFlow h
LEFT JOIN dbo.INV_StoreList fs ON fs.StoreID = h.FromStore
LEFT JOIN dbo.INV_StoreList ts ON ts.StoreID = h.ToStore
WHERE h.FlowID = @FlowID AND h.FlowType = 3
AND (@KPGroupId IS NULL OR h.KPGroup = @KPGroupId)", conn);
        headerCmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = id;
        headerCmd.Parameters.Add("@KPGroupId", SqlDbType.Int).Value = IsAdminRole() ? DBNull.Value : GetCurrentKpGroupId();
        using var headerReader = headerCmd.ExecuteReader();
        if (!headerReader.Read()) return NotFound();

        var flowNo = Convert.ToString(headerReader["FlowNo"]) ?? string.Empty;
        var flowDate = Convert.ToDateTime(headerReader["FlowDate"]);
        var according = Convert.ToString(headerReader["According"]) ?? string.Empty;
        var fromStoreName = Convert.ToString(headerReader["FromStoreName"]) ?? string.Empty;
        var toStoreName = Convert.ToString(headerReader["ToStoreName"]) ?? string.Empty;
        headerReader.Close();

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Transfer Voucher");
        ws.Cell(1, 1).Value = "ITEM TRANSFER VOUCHER";
        ws.Range(1, 1, 1, 8).Merge().Style.Font.SetBold().Font.SetFontSize(14).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

        ws.Cell(3, 1).Value = "Doc No.";
        ws.Cell(3, 2).Value = flowNo;
        ws.Cell(3, 4).Value = "Transfer Date";
        ws.Cell(3, 5).Value = flowDate;
        ws.Cell(3, 5).Style.DateFormat.Format = "dd/MM/yyyy";
        ws.Cell(4, 1).Value = "Issue at St.House";
        ws.Cell(4, 2).Value = fromStoreName;
        ws.Cell(4, 4).Value = "Enter to St.House";
        ws.Cell(4, 5).Value = toStoreName;
        ws.Cell(5, 1).Value = "According";
        ws.Cell(5, 2).Value = according;
        ws.Range(5, 2, 5, 8).Merge();
        ws.Range(3, 1, 5, 1).Style.Font.SetBold();
        ws.Range(3, 4, 4, 4).Style.Font.SetBold();

        var headerRow = 7;
        ws.Cell(headerRow, 1).Value = "No.";
        ws.Cell(headerRow, 2).Value = "Item Code";
        ws.Cell(headerRow, 3).Value = "Item Name";
        ws.Cell(headerRow, 4).Value = "Unit";
        ws.Cell(headerRow, 5).Value = "Doc Qty";
        ws.Cell(headerRow, 6).Value = "Act Qty";
        ws.Cell(headerRow, 7).Value = "Price";
        ws.Cell(headerRow, 8).Value = "Amount";
        ws.Range(headerRow, 1, headerRow, 8).Style.Font.SetBold();
        ws.Range(headerRow, 1, headerRow, 8).Style.Fill.BackgroundColor = XLColor.LightGray;

        using var detailCmd = new SqlCommand(@"SELECT COALESCE(k.ItemCode,i.ItemCode) AS ItemCode,
       COALESCE(k.ItemName,i.ItemName) AS ItemName,
       ISNULL(d.Unit,'') AS Unit,
       ISNULL(d.Doc_Qty,0) AS DocQty,
       ISNULL(d.Act_Qty,0) AS ActQty,
       ISNULL(d.UnitPrice,0) AS UnitPrice,
       ISNULL(d.Amount,0) AS Amount
FROM dbo.INV_ItemFlowDetail d
INNER JOIN dbo.INV_ItemList i ON i.ItemID = d.ItemID
LEFT JOIN dbo.INV_KPGroupIndex k ON k.ItemID = i.ItemID AND k.KPGroupID = @KPGroupId
WHERE d.FlowID = @FlowID
ORDER BY d.DetailID", conn);
        detailCmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = id;
        detailCmd.Parameters.Add("@KPGroupId", SqlDbType.Int).Value = GetCurrentKpGroupId();
        using var detailReader = detailCmd.ExecuteReader();
        var row = headerRow + 1;
        var no = 1;
        while (detailReader.Read())
        {
            ws.Cell(row, 1).Value = no++;
            ws.Cell(row, 2).Value = Convert.ToString(detailReader["ItemCode"]) ?? string.Empty;
            ws.Cell(row, 3).Value = Convert.ToString(detailReader["ItemName"]) ?? string.Empty;
            ws.Cell(row, 4).Value = Convert.ToString(detailReader["Unit"]) ?? string.Empty;
            ws.Cell(row, 5).Value = Convert.ToDecimal(detailReader["DocQty"]);
            ws.Cell(row, 6).Value = Convert.ToDecimal(detailReader["ActQty"]);
            ws.Cell(row, 7).Value = Convert.ToDecimal(detailReader["UnitPrice"]);
            ws.Cell(row, 8).Value = Convert.ToDecimal(detailReader["Amount"]);
            row++;
        }
        ws.Range(headerRow, 1, Math.Max(headerRow, row - 1), 8).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Range(headerRow, 1, Math.Max(headerRow, row - 1), 8).Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        var lastTableRow = Math.Max(headerRow, row - 1);
        ws.Range(headerRow, 1, lastTableRow, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        ws.Range(headerRow, 1, lastTableRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        ws.Range(headerRow, 2, lastTableRow, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        ws.Range(headerRow, 5, lastTableRow, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Range(headerRow, 3, lastTableRow, 3).Style.Font.FontName = ExcelTcvn3FontName;
        ws.Range(headerRow + 1, 5, Math.Max(headerRow + 1, row - 1), 8).Style.NumberFormat.Format = "#,##0.00";
        ws.Columns().AdjustToContents();
        using var ms = new System.IO.MemoryStream();
        wb.SaveAs(ms);
        var safeFlowNo = string.Join("_", flowNo.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"inventory_transfer_{safeFlowNo}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
    }
    public IActionResult OnPostDelete(long id)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionDelete)) return Forbid();
        NormalizeFilter();

        try
        {
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            conn.Open();
            using var tran = conn.BeginTransaction();
            using (var deleteDetail = new SqlCommand("DELETE FROM dbo.INV_ItemFlowDetail WHERE FlowID=@FlowID", conn, tran))
            {
                deleteDetail.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = id;
                deleteDetail.ExecuteNonQuery();
            }
            using (var deleteDoc = new SqlCommand("IF OBJECT_ID('dbo.INV_ItemFlow_Doc','U') IS NOT NULL DELETE FROM dbo.INV_ItemFlow_Doc WHERE FlowID=@FlowID", conn, tran))
            {
                deleteDoc.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = id;
                deleteDoc.ExecuteNonQuery();
            }
            using (var deleteHeader = new SqlCommand("DELETE FROM dbo.INV_ItemFlow WHERE FlowID=@FlowID AND FlowType=3", conn, tran))
            {
                deleteHeader.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = id;
                deleteHeader.ExecuteNonQuery();
            }
            tran.Commit();
            TempData["Message"] = "Transfer voucher deleted successfully.";
            TempData["MessageType"] = "success";
        }
        catch (Exception ex)
        {
            TempData["Message"] = "Cannot delete Transfer voucher: " + ex.Message;
            TempData["MessageType"] = "danger";
        }

        return RedirectToPage(new
        {
            Filter.FlowNo,
            Filter.According,
            Filter.FromStoreId,
            Filter.ToStoreId,
            FromDate = Filter.FromDate?.ToString("yyyy-MM-dd"),
            ToDate = Filter.ToDate?.ToString("yyyy-MM-dd")
        });
    }

    private void LoadLookups() => Stores = BuildStoreOptions();

    private List<SelectListItem> BuildStoreOptions()
    {
        var results = new List<SelectListItem> { new("All", "") };
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        var kpGroupId = GetStoreFilterKpGroupId();
        using var cmd = new SqlCommand(@"SELECT g.KPGroupID, g.KPGroupName, s.StoreID, s.StoreName
FROM dbo.INV_KPGroup g
INNER JOIN dbo.INV_StoreList s ON s.DeptID = g.KPGroupID
WHERE s.StoreID <> 0 AND (@KPGroupId IS NULL OR s.DeptID = @KPGroupId)
ORDER BY g.KPGroupName, s.StoreName", conn);
        cmd.Parameters.Add("@KPGroupId", SqlDbType.Int).Value = kpGroupId.HasValue ? kpGroupId.Value : DBNull.Value;
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
        using var countCmd = new SqlCommand(@"SELECT COUNT(1)
FROM dbo.INV_ItemFlow h
LEFT JOIN dbo.INV_StoreList fs ON fs.StoreID = h.FromStore
LEFT JOIN dbo.INV_StoreList ts ON ts.StoreID = h.ToStore
WHERE h.FlowType = 3
AND (
    @AllowAllStores = 1
    OR (
        @KPGroupId IS NOT NULL
        AND fs.DeptID = @KPGroupId
        AND ts.DeptID = @KPGroupId
    )
)
AND h.FlowDate >= @FromDate AND h.FlowDate < DATEADD(DAY, 1, @ToDate)
AND (@FlowNo IS NULL OR h.FlowNo LIKE '%' + @FlowNo + '%')
AND (@According IS NULL OR h.According LIKE '%' + @According + '%')
AND (@FromStoreId IS NULL OR h.FromStore = @FromStoreId)
AND (@ToStoreId IS NULL OR h.ToStore = @ToStoreId)", conn);
        BindFilterParams(countCmd);
        TotalRecords = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);

        using var cmd = new SqlCommand(@"SELECT h.FlowID, h.FlowNo, h.FlowDate, h.According,
       fs.StoreName AS FromStoreName, ts.StoreName AS ToStoreName
FROM dbo.INV_ItemFlow h
LEFT JOIN dbo.INV_StoreList fs ON fs.StoreID = h.FromStore
LEFT JOIN dbo.INV_StoreList ts ON ts.StoreID = h.ToStore
WHERE h.FlowType = 3
AND (
    @AllowAllStores = 1
    OR (
        @KPGroupId IS NOT NULL
        AND fs.DeptID = @KPGroupId
        AND ts.DeptID = @KPGroupId
    )
)
AND h.FlowDate >= @FromDate AND h.FlowDate < DATEADD(DAY, 1, @ToDate)
AND (@FlowNo IS NULL OR h.FlowNo LIKE '%' + @FlowNo + '%')
AND (@According IS NULL OR h.According LIKE '%' + @According + '%')
AND (@FromStoreId IS NULL OR h.FromStore = @FromStoreId)
AND (@ToStoreId IS NULL OR h.ToStore = @ToStoreId)
ORDER BY h.FlowDate DESC, h.FlowID DESC", conn);
        BindFilterParams(cmd);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            Rows.Add(new InventoryTransferRow
            {
                FlowID = Convert.ToInt64(rd["FlowID"]),
                FlowNo = Convert.ToString(rd["FlowNo"]) ?? string.Empty,
                FlowDate = Convert.ToDateTime(rd["FlowDate"]),
                According = Convert.ToString(rd["According"]) ?? string.Empty,
                FromStoreName = Convert.ToString(rd["FromStoreName"]) ?? string.Empty,
                ToStoreName = Convert.ToString(rd["ToStoreName"]) ?? string.Empty
            });
        }
    }

    private void BindFilterParams(SqlCommand cmd)
    {
        var kpGroupId = GetCurrentKpGroupId();
        var allowAllStores = IsAdminRole() || kpGroupId == 1;
        cmd.Parameters.Add("@AllowAllStores", SqlDbType.Bit).Value = allowAllStores;
        cmd.Parameters.Add("@KPGroupId", SqlDbType.Int).Value = !allowAllStores && kpGroupId > 0 ? kpGroupId : DBNull.Value;
        cmd.Parameters.Add("@FromDate", SqlDbType.Date).Value = Filter.FromDate!.Value.Date;
        cmd.Parameters.Add("@ToDate", SqlDbType.Date).Value = Filter.ToDate!.Value.Date;
        cmd.Parameters.Add("@FlowNo", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(Filter.FlowNo) ? DBNull.Value : Filter.FlowNo.Trim();
        cmd.Parameters.Add("@According", SqlDbType.NVarChar, 250).Value = string.IsNullOrWhiteSpace(Filter.According) ? DBNull.Value : Filter.According.Trim();
        cmd.Parameters.Add("@FromStoreId", SqlDbType.Int).Value = Filter.FromStoreId.HasValue && Filter.FromStoreId > 0 ? Filter.FromStoreId.Value : DBNull.Value;
        cmd.Parameters.Add("@ToStoreId", SqlDbType.Int).Value = Filter.ToStoreId.HasValue && Filter.ToStoreId > 0 ? Filter.ToStoreId.Value : DBNull.Value;
    }

    private static void BindSearchDetailParams(SqlCommand cmd, int kpGroupId, bool allowAllStores, string? keyword, DateTime? fromDate, DateTime? toDate)
    {
        cmd.Parameters.Add("@AllowAllStores", SqlDbType.Bit).Value = allowAllStores;
        cmd.Parameters.Add("@KPGroupId", SqlDbType.Int).Value = !allowAllStores && kpGroupId > 0 ? kpGroupId : DBNull.Value;
        cmd.Parameters.Add("@ItemKpGroupId", SqlDbType.Int).Value = kpGroupId > 0 ? kpGroupId : DBNull.Value;
        cmd.Parameters.Add("@FromDate", SqlDbType.Date).Value = fromDate!.Value.Date;
        cmd.Parameters.Add("@ToDate", SqlDbType.Date).Value = toDate!.Value.Date;
        cmd.Parameters.Add("@Keyword", SqlDbType.NVarChar, 150).Value = string.IsNullOrWhiteSpace(keyword) ? DBNull.Value : keyword.Trim();
    }

    private void NormalizeFilter()
    {
        if (!Filter.FromDate.HasValue) Filter.FromDate = DateTime.Today.AddDays(-2);
        if (!Filter.ToDate.HasValue) Filter.ToDate = DateTime.Today;
        Filter.Page = Filter.Page <= 0 ? 1 : Filter.Page;
        Filter.PageSize = PageSizeOptions.Contains(Filter.PageSize) ? Filter.PageSize : 10;
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
    private int? GetStoreFilterKpGroupId()
    {
        if (IsAdminRole()) return null;
        var kpGroupId = GetCurrentKpGroupId();
        return kpGroupId == 1 ? null : kpGroupId;
    }
    private PagePermissions GetUserPermissions() => IsAdminRole() ? new PagePermissions { AllowedNos = Enumerable.Range(1, 20).ToList() } : new PagePermissions { AllowedNos = _permissionService.GetPermissionsForPage(GetCurrentRoleId(), FunctionId) };
}

public class InventoryTransferFilter
{
    public string? FlowNo { get; set; }
    public string? According { get; set; }
    public int? FromStoreId { get; set; }
    public int? ToStoreId { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class InventoryTransferRow
{
    public long FlowID { get; set; }
    public string FlowNo { get; set; } = string.Empty;
    public DateTime FlowDate { get; set; }
    public string According { get; set; } = string.Empty;
    public string FromStoreName { get; set; } = string.Empty;
    public string ToStoreName { get; set; } = string.Empty;
}








