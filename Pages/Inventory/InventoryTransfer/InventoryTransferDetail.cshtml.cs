using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Services;

namespace SmartSam.Pages.Inventory.InventoryTransfer;

public class InventoryTransferDetailModel : BasePageModel
{
    private const int FunctionId = 68;
    private const int PermissionViewDetail = 2;
    private const int PermissionAdd = 3;
    private const int PermissionEdit = 4;
    private readonly PermissionService _permissionService;

    public InventoryTransferDetailModel(IConfiguration config, PermissionService permissionService) : base(config)
    {
        _permissionService = permissionService;
    }

    [BindProperty] public TransferHeader Header { get; set; } = new();
    [BindProperty] public List<TransferDetailRow> Details { get; set; } = new();
    public List<SelectListItem> Stores { get; set; } = new();
    public List<TransferItemLookup> Items { get; set; } = new();
    public PagePermissions PagePerm { get; private set; } = new();
    public string Mode { get; set; } = "add";
    public string Message { get; set; } = string.Empty;
    public string MessageType { get; set; } = "info";
    public long? Id { get; set; }
    public bool IsAddMode => Mode == "add";
    public bool IsViewMode => Mode == "view";

    public IActionResult OnGet(long? id, string? mode)
    {
        PagePerm = GetUserPermissions();
        Id = id;
        Mode = string.IsNullOrWhiteSpace(mode) ? "view" : mode.Trim().ToLowerInvariant();
        if (id.HasValue && Mode == "view" && !PagePerm.HasPermission(PermissionViewDetail)) return Redirect("/");
        if (!id.HasValue && !PagePerm.HasPermission(PermissionAdd)) return Redirect("/");
        if (Mode == "edit" && !PagePerm.HasPermission(PermissionEdit)) return Redirect("/");
        LoadLookups();
        if (id.HasValue && id.Value > 0)
        {
            LoadExisting(id.Value);
        }
        else
        {
            Header.FlowDate = DateTime.Today;
            Header.FlowNo = GetNextVoucherNo();
        }
        return Page();
    }

    public IActionResult OnGetCheckStock(int itemId, int fromStoreId, string? flowDate)
    {
        if (itemId <= 0 || fromStoreId <= 0)
        {
            return new JsonResult(new { ok = true, stockQty = 0m });
        }

        var dateValue = DateTime.Today;
        if (!string.IsNullOrWhiteSpace(flowDate) && DateTime.TryParse(flowDate, out var parsed))
        {
            dateValue = parsed.Date;
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand("EXEC dbo.sp_CheckStockItem @FlowDate, @FromStore, @ItemID", conn);
        cmd.Parameters.Add("@FlowDate", SqlDbType.Date).Value = dateValue;
        cmd.Parameters.Add("@FromStore", SqlDbType.Int).Value = fromStoreId;
        cmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = itemId;
        var stockObj = cmd.ExecuteScalar();
        var stockQty = Convert.ToDecimal(stockObj == null || stockObj == DBNull.Value ? 0m : stockObj);
        return new JsonResult(new { ok = true, stockQty });
    }


    public IActionResult OnGetReport(long id, bool inline = false)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewDetail)) return Redirect("/");
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewDetail)) return Redirect("/");
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        var report = LoadTransferReport(conn, id);
        if (report == null) return NotFound();

        var pdf = InventoryTransferPdfReport.BuildPdf(report);
        if (inline)
        {
            Response.Headers["Content-Disposition"] = $"inline; filename={report.FlowNo}.pdf";
            return File(pdf, "application/pdf");
        }

        return File(pdf, "application/pdf", $"{report.FlowNo}.pdf");
    }
    public IActionResult OnPost(long? id, string? mode)
    {
        PagePerm = GetUserPermissions();
        Mode = string.IsNullOrWhiteSpace(mode) ? "add" : mode.Trim().ToLowerInvariant();
        if ((id.HasValue && !PagePerm.HasPermission(PermissionEdit)) || (!id.HasValue && !PagePerm.HasPermission(PermissionAdd))) return Redirect("/");
        LoadLookups();
        Details = Details.Where(x => x.ItemId > 0).ToList();

        if (string.IsNullOrWhiteSpace(Header.FlowNo)) ModelState.AddModelError("Header.FlowNo", "Please enter doc no.");
        if (!Details.Any()) ModelState.AddModelError(string.Empty, "Please add at least one item.");
        if (!Header.FromStoreId.HasValue || Header.FromStoreId.Value <= 0) ModelState.AddModelError("Header.FromStoreId", "Issue at St.House is required.");
        if (!Header.ToStoreId.HasValue || Header.ToStoreId.Value <= 0) ModelState.AddModelError("Header.ToStoreId", "Enter to St.House is required.");
        if (Header.FromStoreId.HasValue && Header.ToStoreId.HasValue && Header.FromStoreId.Value == Header.ToStoreId.Value && Header.FromStoreId.Value > 0) ModelState.AddModelError("Header.ToStoreId", "Please select again. Issue at St.House have to difference enter to St.House.");

        ValidateStorePermissions();
        ValidateDetailBusinessRules();
        if (!ModelState.IsValid)
        {
            Message = string.Join(Environment.NewLine,
                ModelState
                    .Where(x => string.IsNullOrEmpty(x.Key))
                    .SelectMany(x => x.Value?.Errors ?? Enumerable.Empty<Microsoft.AspNetCore.Mvc.ModelBinding.ModelError>())
                    .Select(e => e.ErrorMessage)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct());
            MessageType = "error";
            return Page();
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var tran = conn.BeginTransaction();
        try
        {
            long flowId;
            Header.FlowNo = Header.FlowNo?.Trim() ?? string.Empty;
            if (!id.HasValue || id.Value <= 0)
            {
                if (string.IsNullOrWhiteSpace(Header.FlowNo)) Header.FlowNo = GenerateNextVoucherNo(conn, tran);
                if (FlowNoExists(conn, tran, Header.FlowNo, null))
                {
                    ModelState.AddModelError("Header.FlowNo", $"Doc No. '{Header.FlowNo}' already exists.");
                    tran.Rollback();
                    Message = $"Doc No. '{Header.FlowNo}' already exists.";
                    MessageType = "error";
                    return Page();
                }

                using var cmd = new SqlCommand(@"INSERT INTO dbo.INV_ItemFlow(FlowType,KPGroup,OperatorID,FlowNo,FlowDate,FromStore,ToStore,According,Reason)
VALUES(3,@KPGroup,@OperatorId,@FlowNo,@FlowDate,@FromStore,@ToStore,@According,@Reason);
SELECT CAST(SCOPE_IDENTITY() AS bigint);", conn, tran);
                BindHeader(cmd);
                flowId = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
                InsertFlowAction(conn, tran, flowId, GetCurrentEmployeeId(), 1, "Created Transfer Voucher");
            }
            else
            {
                flowId = id.Value;
                if (FlowNoExists(conn, tran, Header.FlowNo, flowId))
                {
                    ModelState.AddModelError("Header.FlowNo", $"Doc No. '{Header.FlowNo}' already exists.");
                    tran.Rollback();
                    Message = $"Doc No. '{Header.FlowNo}' already exists.";
                    MessageType = "error";
                    return Page();
                }

                using var upd = new SqlCommand(@"UPDATE dbo.INV_ItemFlow
SET FlowNo=@FlowNo, FlowDate=@FlowDate, FromStore=@FromStore, ToStore=@ToStore, According=@According, Reason=@Reason
WHERE FlowID=@FlowID AND FlowType=3", conn, tran);
                BindHeader(upd);
                upd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
                upd.ExecuteNonQuery();
                InsertFlowAction(conn, tran, flowId, GetCurrentEmployeeId(), 4, "Updated Transfer Voucher");

                using var del = new SqlCommand("DELETE FROM dbo.INV_ItemFlowDetail WHERE FlowID=@FlowID", conn, tran);
                del.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
                del.ExecuteNonQuery();
            }

            foreach (var d in Details)
            {
                using var ins = new SqlCommand(@"INSERT INTO dbo.INV_ItemFlowDetail(ItemID,Unit,Doc_Qty,Act_Qty,UnitPrice,Amount,FlowID)
SELECT @ItemID, ISNULL(i.Unit,''), @DocQty, @ActQty, @UnitPrice, @Amount, @FlowID
FROM dbo.INV_ItemList i WHERE i.ItemID=@ItemID", conn, tran);
                ins.Parameters.Add("@ItemID", SqlDbType.Int).Value = d.ItemId;
                ins.Parameters.Add("@DocQty", SqlDbType.Decimal).Value = d.DocQty;
                ins.Parameters.Add("@ActQty", SqlDbType.Decimal).Value = d.ActQty;
                ins.Parameters.Add("@UnitPrice", SqlDbType.Decimal).Value = d.UnitPrice;
                ins.Parameters.Add("@Amount", SqlDbType.Decimal).Value = d.Amount;
                ins.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
                ins.ExecuteNonQuery();
            }

            tran.Commit();
            TempData["Message"] = "Transfer voucher saved successfully.";
            TempData["MessageType"] = "success";
            return RedirectToPage("./Index");
        }
        catch (Exception ex)
        {
            tran.Rollback();
            Message = ex.Message;
            MessageType = "error";
            return Page();
        }
    }

    private void LoadLookups()
    {
        Stores = new List<SelectListItem> { new("--- Select ---", "") };
        Items = new List<TransferItemLookup>();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        var kpGroupId = GetCurrentKpGroupId();
        var storeFilterKpGroupId = GetStoreFilterKpGroupId();
        var isAdminUser = IsAdminRole();

        using (var cmd = new SqlCommand(@"SELECT g.KPGroupID, g.KPGroupName, s.StoreID, s.StoreName
FROM dbo.INV_KPGroup g
INNER JOIN dbo.INV_StoreList s ON s.DeptID = g.KPGroupID
WHERE s.StoreID <> 0 AND (@IsAdminUser = 1 OR s.DeptID = @KPGroupId)
ORDER BY g.KPGroupName, s.StoreName", conn))
        {
            cmd.Parameters.Add("@IsAdminUser", SqlDbType.Bit).Value = isAdminUser;
            cmd.Parameters.Add("@KPGroupId", SqlDbType.Int).Value = storeFilterKpGroupId.HasValue ? storeFilterKpGroupId.Value : -1;
            using var rd = cmd.ExecuteReader();
            var groupMap = new Dictionary<int, SelectListGroup>();
            while (rd.Read())
            {
                var groupId = Convert.ToInt32(rd["KPGroupID"]);
                if (!groupMap.ContainsKey(groupId))
                {
                    groupMap[groupId] = new SelectListGroup { Name = Convert.ToString(rd["KPGroupName"]) ?? string.Empty };
                }

                Stores.Add(new SelectListItem
                {
                    Value = Convert.ToString(rd["StoreID"]),
                    Text = Convert.ToString(rd["StoreName"]) ?? string.Empty,
                    Group = groupMap[groupId]
                });
            }
        }

        using (var cmd = new SqlCommand(@"SELECT i.ItemID, COALESCE(k.ItemCode,i.ItemCode) AS ItemCode, COALESCE(k.ItemName,i.ItemName) AS ItemName, ISNULL(i.Unit,'') AS Unit, ISNULL(i.UnitPrice,0) AS UnitPrice
FROM dbo.INV_ItemList i
LEFT JOIN dbo.INV_KPGroupIndex k ON k.ItemID=i.ItemID AND k.KPGroupID=@KPGroupId
ORDER BY COALESCE(k.ItemCode,i.ItemCode)", conn))
        {
            cmd.Parameters.Add("@KPGroupId", SqlDbType.Int).Value = kpGroupId > 0 ? kpGroupId : DBNull.Value;
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                Items.Add(new TransferItemLookup
                {
                    ItemId = Convert.ToInt32(rd["ItemID"]),
                    ItemCode = Convert.ToString(rd["ItemCode"]) ?? string.Empty,
                    ItemName = Convert.ToString(rd["ItemName"]) ?? string.Empty,
                    Unit = Convert.ToString(rd["Unit"]) ?? string.Empty,
                    UnitPrice = Convert.ToDecimal(rd["UnitPrice"] == DBNull.Value ? 0 : rd["UnitPrice"])
                });
            }
        }
    }

    private void LoadExisting(long id)
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using (var cmd = new SqlCommand("SELECT FlowNo, FlowDate, FromStore, ToStore, According, Reason FROM dbo.INV_ItemFlow WHERE FlowID=@FlowID AND FlowType=3", conn))
        {
            cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = id;
            using var rd = cmd.ExecuteReader();
            if (rd.Read())
            {
                Header.FlowNo = Convert.ToString(rd["FlowNo"]) ?? string.Empty;
                Header.FlowDate = Convert.ToDateTime(rd["FlowDate"]);
                Header.FromStoreId = Convert.ToInt32(rd["FromStore"] == DBNull.Value ? 0 : rd["FromStore"]);
                Header.ToStoreId = Convert.ToInt32(rd["ToStore"] == DBNull.Value ? 0 : rd["ToStore"]);
                Header.According = Convert.ToString(rd["According"]) ?? string.Empty;
                Header.Reason = Convert.ToString(rd["Reason"]) ?? string.Empty;
            }
        }

        Details = new List<TransferDetailRow>();
        using (var cmd = new SqlCommand("SELECT ItemID, Unit, ISNULL(Doc_Qty,0) AS DocQty, ISNULL(Act_Qty,0) AS ActQty, ISNULL(UnitPrice,0) AS UnitPrice, ISNULL(Amount,0) AS Amount FROM dbo.INV_ItemFlowDetail WHERE FlowID=@FlowID", conn))
        {
            cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = id;
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                Details.Add(new TransferDetailRow
                {
                    ItemId = Convert.ToInt32(rd["ItemID"]),
                    Unit = Convert.ToString(rd["Unit"]) ?? string.Empty,
                    DocQty = Convert.ToDecimal(rd["DocQty"]),
                    ActQty = Convert.ToDecimal(rd["ActQty"]),
                    UnitPrice = Convert.ToDecimal(rd["UnitPrice"]),
                    Amount = Convert.ToDecimal(rd["Amount"])
                });
            }
        }


    }

    private void BindHeader(SqlCommand cmd)
    {
        cmd.Parameters.Add("@KPGroup", SqlDbType.Int).Value = GetCurrentKpGroupId();
        cmd.Parameters.Add("@OperatorId", SqlDbType.Int).Value = GetCurrentEmployeeId();
        cmd.Parameters.Add("@FlowNo", SqlDbType.NVarChar, 50).Value = Header.FlowNo ?? string.Empty;
        cmd.Parameters.Add("@FlowDate", SqlDbType.Date).Value = Header.FlowDate.Date;
        cmd.Parameters.Add("@FromStore", SqlDbType.Int).Value = (object?)Header.FromStoreId ?? DBNull.Value;
        cmd.Parameters.Add("@ToStore", SqlDbType.Int).Value = (object?)Header.ToStoreId ?? DBNull.Value;
        cmd.Parameters.Add("@According", SqlDbType.NVarChar, 250).Value = string.IsNullOrWhiteSpace(Header.According) ? DBNull.Value : Header.According.Trim();
        cmd.Parameters.Add("@Reason", SqlDbType.NVarChar, 250).Value = string.IsNullOrWhiteSpace(Header.Reason) ? DBNull.Value : Header.Reason.Trim();
    }

    private string GetNextVoucherNo()
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var tran = conn.BeginTransaction(IsolationLevel.Serializable);
        var flowNo = GenerateNextVoucherNo(conn, tran);
        tran.Rollback();
        return flowNo;
    }

    private string GenerateNextVoucherNo(SqlConnection conn, SqlTransaction tran)
    {
        var now = DateTime.Today;
        var yyMM = now.ToString("yyMM");
        var suffix = GetTransferSuffixByKpGroup(GetCurrentKpGroupId());
        var prefix = $"{yyMM}-{suffix}";

        using var cmd = new SqlCommand(@"SELECT MAX(RIGHT(FlowNo,3))
FROM dbo.INV_ItemFlow WITH (UPDLOCK, HOLDLOCK)
WHERE FlowType = 3
  AND MONTH(FlowDate) = @Month
  AND YEAR(FlowDate) = @Year
  AND LEFT(FlowNo, @PrefixLen) = @Prefix", conn, tran);
        cmd.Parameters.Add("@Month", SqlDbType.Int).Value = now.Month;
        cmd.Parameters.Add("@Year", SqlDbType.Int).Value = now.Year;
        cmd.Parameters.Add("@PrefixLen", SqlDbType.Int).Value = prefix.Length;
        cmd.Parameters.Add("@Prefix", SqlDbType.NVarChar, 20).Value = prefix;

        var maxObj = cmd.ExecuteScalar();
        var maxNo = Convert.ToString(maxObj);
        var next = 1;
        if (!string.IsNullOrWhiteSpace(maxNo) && int.TryParse(maxNo, out var n)) next = n + 1;
        return $"{prefix}-{next:000}";
    }

    private static bool FlowNoExists(SqlConnection conn, SqlTransaction tran, string flowNo, long? excludeFlowId)
    {
        using var cmd = new SqlCommand(@"SELECT COUNT(1)
FROM dbo.INV_ItemFlow WITH (UPDLOCK, HOLDLOCK)
WHERE FlowType = 3
  AND FlowNo = @FlowNo
  AND (@ExcludeFlowId IS NULL OR FlowID <> @ExcludeFlowId)", conn, tran);
        cmd.Parameters.Add("@FlowNo", SqlDbType.NVarChar, 50).Value = flowNo.Trim();
        cmd.Parameters.Add("@ExcludeFlowId", SqlDbType.BigInt).Value = excludeFlowId.HasValue ? excludeFlowId.Value : DBNull.Value;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private static string GetTransferSuffixByKpGroup(int kpGroupId)
    {
        return kpGroupId switch
        {
            1 => "XTR",
            2 => "TTR",
            4 => "HTR",
            12 => "CTR",
            16 => "STR",
            _ => "XTR"
        };
    }


    private InventoryTransferReportModel? LoadTransferReport(SqlConnection conn, long flowId)
    {
        using var cmd = new SqlCommand(@"SELECT h.FlowNo,h.FlowDate,ISNULL(h.According,'') AS According,
       ISNULL(fs.StoreName,'') AS FromStoreName,ISNULL(ts.StoreName,'') AS ToStoreName,
       ISNULL(e.EmployeeName,'') AS OperatorName,h.OperatorID
FROM dbo.INV_ItemFlow h
LEFT JOIN dbo.INV_StoreList fs ON fs.StoreID=h.FromStore
LEFT JOIN dbo.INV_StoreList ts ON ts.StoreID=h.ToStore
LEFT JOIN dbo.MS_Employee e ON e.EmployeeID=h.OperatorID
WHERE h.FlowID=@FlowID AND h.FlowType=3", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;

        var report = new InventoryTransferReportModel
        {
            FlowNo = Convert.ToString(rd["FlowNo"]) ?? string.Empty,
            FlowDateText = Convert.ToDateTime(rd["FlowDate"]).ToString("dd/MM/yyyy"),
            According = Convert.ToString(rd["According"]) ?? string.Empty,
            FromStoreName = Convert.ToString(rd["FromStoreName"]) ?? string.Empty,
            ToStoreName = Convert.ToString(rd["ToStoreName"]) ?? string.Empty,
            OperatorName = Convert.ToString(rd["OperatorName"]) ?? string.Empty
        };
        rd.Close();

        using var detailCmd = new SqlCommand(@"SELECT ISNULL(i.ItemCode,'') AS ItemCode,ISNULL(i.ItemName,'') AS ItemName,ISNULL(dt.Unit,'') AS Unit,
       ISNULL(dt.Doc_Qty,0) AS DocQty,ISNULL(dt.Act_Qty,0) AS ActQty,ISNULL(dt.UnitPrice,0) AS UnitPrice,ISNULL(dt.Amount,0) AS Amount
FROM dbo.INV_ItemFlowDetail dt
INNER JOIN dbo.INV_ItemList i ON i.ItemID=dt.ItemID
WHERE dt.FlowID=@FlowID
ORDER BY i.ItemCode", conn);
        detailCmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        using var detailRd = detailCmd.ExecuteReader();
        while (detailRd.Read())
        {
            report.Items.Add(new InventoryTransferReportItem
            {
                ItemCode = Convert.ToString(detailRd["ItemCode"]) ?? string.Empty,
                ItemName = Convert.ToString(detailRd["ItemName"]) ?? string.Empty,
                Unit = Convert.ToString(detailRd["Unit"]) ?? string.Empty,
                DocQty = Convert.ToDecimal(detailRd["DocQty"]),
                ActQty = Convert.ToDecimal(detailRd["ActQty"]),
                UnitPrice = Convert.ToDecimal(detailRd["UnitPrice"]),
                Amount = Convert.ToDecimal(detailRd["Amount"])
            });
        }

        return report;
    }

    private void ValidateStorePermissions()
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        if (Header.FromStoreId.HasValue && !StoreBelongsToCurrentKpGroup(conn, Header.FromStoreId.Value))
            ModelState.AddModelError("Header.FromStoreId", "Issue at St.House is not valid for current user group.");
        if (Header.ToStoreId.HasValue && !StoreBelongsToCurrentKpGroup(conn, Header.ToStoreId.Value))
            ModelState.AddModelError("Header.ToStoreId", "Enter to St.House is not valid for current user group.");
    }

    private bool StoreBelongsToCurrentKpGroup(SqlConnection conn, int storeId)
    {
        if (storeId <= 0) return false;
        if (IsAdminRole()) return true;
        using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.INV_StoreList WHERE StoreID=@StoreID AND DeptID=@KPGroupID", conn);
        cmd.Parameters.Add("@StoreID", SqlDbType.Int).Value = storeId;
        cmd.Parameters.Add("@KPGroupID", SqlDbType.Int).Value = GetCurrentKpGroupId();
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private void ValidateDetailBusinessRules()
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        var groupedDetails = Details.Where(x => x.ItemId > 0).GroupBy(x => x.ItemId).ToList();

        foreach (var detail in Details)
        {
            if (detail.ItemId <= 0) continue;

            if (!IsItemAllowedForCurrentKpGroup(conn, detail.ItemId))
            {
                ModelState.AddModelError(string.Empty, $"Item '{GetItemDisplayName(detail.ItemId)}' is not valid for current KPGroup.");
                continue;
            }

            if (detail.DocQty < 0 || detail.ActQty < 0)
            {
                ModelState.AddModelError(string.Empty, "Quantity cannot be negative.");
                continue;
            }

            using (var unitCmd = new SqlCommand("SELECT ISNULL(Unit,'') FROM dbo.INV_ItemList WHERE ItemID=@ItemID", conn))
            {
                unitCmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = detail.ItemId;
                detail.Unit = Convert.ToString(unitCmd.ExecuteScalar()) ?? string.Empty;
            }

            if (detail.ActQty > 0)
            {
                using (var stockCmd = new SqlCommand("EXEC dbo.sp_CheckStockItem @FlowDate, @FromStore, @ItemID", conn))
                {
                    stockCmd.Parameters.Add("@FlowDate", SqlDbType.Date).Value = Header.FlowDate.Date;
                    stockCmd.Parameters.Add("@FromStore", SqlDbType.Int).Value = Header.FromStoreId ?? 0;
                    stockCmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = detail.ItemId;
                    var stockObj = stockCmd.ExecuteScalar();
                    var stockQty = Convert.ToDecimal(stockObj == null || stockObj == DBNull.Value ? 0m : stockObj);
                    var totalActQty = Details.Where(x => x.ItemId == detail.ItemId).Sum(x => x.ActQty);
                    if (totalActQty > stockQty)
                    {
                        ModelState.AddModelError(string.Empty, $"Item '{GetItemDisplayName(detail.ItemId)}' total transfer quantity ({totalActQty:N0}) exceeds stock ({stockQty:N0}).");
                    }
                }
            }

            detail.Amount = detail.ActQty * detail.UnitPrice;
        }
    }


    private void InsertFlowAction(SqlConnection conn, SqlTransaction tran, long flowId, int userId, int actionTypeId, string description)
    {
        using var cmd = new SqlCommand(@"INSERT INTO dbo.INV_ItemFlowAction(FlowID,UserID,TheDateTime,ActionTypeID,ComputerName,Des)
VALUES(@FlowID,@UserID,GETDATE(),@ActionTypeID,@ComputerName,@Des)", conn, tran);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        cmd.Parameters.Add("@UserID", SqlDbType.Int).Value = userId;
        cmd.Parameters.Add("@ActionTypeID", SqlDbType.Int).Value = actionTypeId;
        cmd.Parameters.Add("@ComputerName", SqlDbType.NVarChar, 100).Value = Environment.MachineName;
        cmd.Parameters.Add("@Des", SqlDbType.NVarChar, 255).Value = description;
        cmd.ExecuteNonQuery();
    }



    private bool IsItemAllowedForCurrentKpGroup(SqlConnection conn, int itemId)
    {
        if (itemId <= 0) return false;
        using var cmd = new SqlCommand(@"SELECT COUNT(1)
FROM dbo.INV_ItemList i
LEFT JOIN dbo.INV_KPGroupIndex k ON k.ItemID = i.ItemID AND k.KPGroupID = @KPGroupID
WHERE i.ItemID = @ItemID", conn);
        cmd.Parameters.Add("@KPGroupID", SqlDbType.Int).Value = GetCurrentKpGroupId();
        cmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = itemId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private string GetItemDisplayName(int itemId)
    {
        var lookupItem = Items.FirstOrDefault(x => x.ItemId == itemId);
        if (lookupItem != null)
        {
            var label = $"{lookupItem.ItemCode} - {lookupItem.ItemName}".Trim();
            return string.IsNullOrWhiteSpace(label) ? itemId.ToString() : label;
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand("SELECT TOP 1 ISNULL(ItemCode,''), ISNULL(ItemName,'') FROM dbo.INV_ItemList WHERE ItemID=@ItemID", conn);
        cmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = itemId;
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            var code = reader.GetString(0);
            var name = reader.GetString(1);
            var label = $"{code} - {name}".Trim();
            return string.IsNullOrWhiteSpace(label) ? itemId.ToString() : label;
        }

        return itemId.ToString();
    }

    private int GetCurrentKpGroupId()
    {
        return int.TryParse(User.FindFirst("StoreGR")?.Value, out var kpGroupFromClaim) && kpGroupFromClaim > 0
            ? kpGroupFromClaim
            : 0;
    }
    private int GetCurrentEmployeeId() => int.Parse(User.FindFirst("EmployeeID")?.Value ?? "0");
    private int GetCurrentRoleId() => int.Parse(User.FindFirst("RoleID")?.Value ?? "0");
    private bool IsAdminRole() => User.FindFirst("IsAdminRole")?.Value == "True";
    private int? GetStoreFilterKpGroupId()
    {
        if (IsAdminRole()) return null;
        var kpGroupId = GetCurrentKpGroupId();
        return kpGroupId > 0 ? kpGroupId : null;
    }
    private PagePermissions GetUserPermissions() => IsAdminRole() ? new PagePermissions { AllowedNos = Enumerable.Range(1, 20).ToList() } : new PagePermissions { AllowedNos = _permissionService.GetPermissionsForPage(GetCurrentRoleId(), FunctionId) };
}

public class TransferHeader
{
    [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "Please enter doc no.")]
    public string FlowNo { get; set; } = string.Empty;
    public DateTime FlowDate { get; set; } = DateTime.Today;
    [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue, ErrorMessage = "Please select issue at St.House.")]
    public int? FromStoreId { get; set; }
    [System.ComponentModel.DataAnnotations.Range(1, int.MaxValue, ErrorMessage = "Please select enter to St.House.")]
    public int? ToStoreId { get; set; }
    public string? According { get; set; }
    public string? Reason { get; set; }
}

public class TransferDetailRow
{
    public int ItemId { get; set; }
    public string Unit { get; set; } = string.Empty;
    public decimal DocQty { get; set; }
    public decimal ActQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
}

public class TransferItemLookup
{
    public int ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
}
