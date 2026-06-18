using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Text.Json;
using SmartSam.Helpers;
using SmartSam.Pages;
using SmartSam.Services;

namespace SmartSam.Pages.Inventory.ItemChecking;

public class ItemCheckingDetailModel : BasePageModel
{
    private const int FunctionId = 151;
    // private const string NotifyCcEmail = "maiquangvinhi4@gmail.com";
    private const string NotifyCcEmail = "hai.dq@saigonskygarden.com.vn";
    private const string NotifyFontFamily = "'VNI-WIN', 'VNI-Times', 'VNI-Helve', sans-serif";
    private readonly PermissionService _permissionService;
    private static readonly JsonSerializerOptions JsonCaseInsensitiveOptions = new() { PropertyNameCaseInsensitive = true };
    public ItemCheckingDetailModel(IConfiguration config, PermissionService permissionService) : base(config) { _permissionService = permissionService; }

    public PagePermissions PagePerm { get; private set; } = new();
    [BindProperty(SupportsGet = true)] public int Id { get; set; }
    [BindProperty(SupportsGet = true)] public string Mode { get; set; } = "view";
    [BindProperty] public ItemCheckingHeaderVm Header { get; set; } = new();
    [BindProperty] public string StagedItemsJson { get; set; } = "[]";
    [BindProperty] public bool SendCreateCheckMail { get; set; }
    public int PreviewCheckingId { get; set; }
    public string Message { get; set; } = string.Empty;
    public string MessageType { get; set; } = "info";
    [BindProperty] public List<ItemCheckingDetailRowVm> DetailRows { get; set; } = new();
    public List<SelectListItem> Departments { get; set; } = new();
    public List<SelectListItem> Statuses { get; set; } = new();
    public List<SelectListItem> Pos { get; set; } = new();
    public List<SelectListItem> Employees { get; set; } = new();
    public List<SelectListItem> CheckByEmployees { get; set; } = new();
    public bool LockDeptCheck { get; set; }
    public bool IsApprovedMode => string.Equals(Mode, "approved", StringComparison.OrdinalIgnoreCase);
    public bool IsReadOnly => string.Equals(Mode, "view", StringComparison.OrdinalIgnoreCase) || IsApprovedMode;
    public string ActionCaption { get; set; } = "Save";
    public bool CanDisapprove { get; set; }
    public bool CanAction { get; set; }
    public bool CanSaveDraft { get; set; }
    public bool CanCheckAction { get; set; }
    public bool CanApproveAction { get; set; }
    public bool CanEditCheckingFields { get; set; }

    public IActionResult OnGet()
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(2) && !PagePerm.HasPermission(3) && !PagePerm.HasPermission(4)) return Redirect("/");
        if (Id > 0)
        {
            LoadMaster(Id);
            ApplyDeptCheckScope();
            LoadLookups();
            if (!CanAccessVoucherDepartment(Header.DeptChecked))
            {
                TempData["AlertMessage"] = "You do not have permission to access this checking voucher.";
                TempData["AlertType"] = "warning";
                return RedirectToPage("./Index");
            }
            LoadDetail(Id);
        }
        else
        {
            InitData();
            ApplyDeptCheckScope();
            LoadLookups();
        }
        ComputeActionUi();
        return Page();
    }

    public IActionResult OnGetPoItems(int poid)
    {
        if (poid <= 0) return new JsonResult(Array.Empty<object>());
        using var conn = OpenConnection();
        using var cmd = new SqlCommand(@"SELECT a1.ItemID, a2.ItemCode + '/' + a2.ItemName AS Item,
dbo.GetQuantityFromPOMinusInAllChecking(@POID,a1.ItemID,a1.MRDetailID) AS Quantity,
a1.UnitPrice, a1.Note, a1.MRDetailID
FROM dbo.PC_PODetail a1
INNER JOIN dbo.INV_ItemList a2 ON a1.ItemID = a2.ItemID
WHERE a1.POID = @POID
  AND dbo.GetQuantityFromPOMinusInAllChecking(@POID,a1.ItemID,a1.MRDetailID) > 0", conn);
        cmd.Parameters.Add("@POID", SqlDbType.Int).Value = poid;
        using var rd = cmd.ExecuteReader();
        var list = new List<object>();
        while (rd.Read())
        {
            list.Add(new
            {
                itemId = Convert.ToInt32(rd[0]),
                itemText = Convert.ToString(rd[1]) ?? "",
                quantity = rd.IsDBNull(2) ? 0m : Convert.ToDecimal(rd[2]),
                price = rd.IsDBNull(3) ? 0m : Convert.ToDecimal(rd[3]),
                note = rd.IsDBNull(4) ? "" : Convert.ToString(rd[4]) ?? "",
                mrDetailId = rd.IsDBNull(5) ? 0 : Convert.ToInt32(rd[5])
            });
        }
        return new JsonResult(list);
    }

    public IActionResult OnGetCheckUsers(int? deptId)
    {
        ApplyDeptCheckScope();
        if (LockDeptCheck)
        {
            deptId = Header.DeptChecked;
        }
        if (!CanAccessVoucherDepartment(deptId)) return new JsonResult(Array.Empty<object>());
        var items = LoadCheckByEmployees(deptId)
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => new { value = x.Value, text = x.Text })
            .ToList();
        return new JsonResult(items);
    }

    public IActionResult OnPostSaveSelectedItems([FromBody] SaveSelectedItemsRequest request)
    {
        if (request == null || request.Poid <= 0 || request.SelectedItems == null || request.SelectedItems.Count == 0)
            return new JsonResult(new { success = false, message = "No selected items." });
        try
        {
            using var conn = OpenConnection();
            var validated = new List<object>();
            foreach (var it in request.SelectedItems)
            {
                if (it.ItemId <= 0) return new JsonResult(new { success = false, message = "Item is invalid." });
                if (it.QuantityPassed <= 0) return new JsonResult(new { success = false, message = "Quantity Passed must be greater than 0." });

                using var cmd = new SqlCommand("SELECT dbo.GetQuantityFromPOMinusInAllChecking(@POID,@ItemID,@MRDetailID)", conn);
                cmd.Parameters.Add("@POID", SqlDbType.Int).Value = request.Poid;
                cmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = it.ItemId;
                cmd.Parameters.Add("@MRDetailID", SqlDbType.Int).Value = it.MrDetailId <= 0 ? DBNull.Value : it.MrDetailId;
                var remainObj = cmd.ExecuteScalar();
                var remainQty = remainObj == null || remainObj == DBNull.Value ? 0m : Convert.ToDecimal(remainObj);
                if (it.QuantityPassed > remainQty)
                {
                    return new JsonResult(new { success = false, message = $"Quantity Passed cannot exceed PO remain ({remainQty})." });
                }

                validated.Add(new
                {
                    itemId = it.ItemId,
                    itemText = it.ItemText,
                    quantity = it.Quantity,
                    quantityPassed = it.QuantityPassed,
                    price = it.Price,
                    note = it.Note,
                    mrDetailId = it.MrDetailId
                });
            }
            return new JsonResult(new { success = true, rows = validated });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, message = ex.Message });
        }
    }

    public IActionResult OnPostAction()
    {
        PagePerm = GetUserPermissions();
        ApplyDeptCheckScope();
        var createdInThisAction = false;
        var currentStatus = Header.StatusId;
        if (Header.CheckingId > 0)
        {
            if (!CanAccessCheckingVoucher(Header.CheckingId)) return RedirectNoVoucherAccess();
            using var statusConn = OpenConnection();
            currentStatus = GetCurrentStatus(statusConn, Header.CheckingId);
        }
        var scope = LoadCurrentEmployeeScope();
        var isStoreman = IsAdminRole() || (scope.IsStoreKeeper && !scope.IsHeadDept);
        var isDepartmentUser = !scope.IsStoreKeeper && !scope.IsHeadDept;
        var isDepartmentHead = scope.IsHeadDept;
        if ((currentStatus == 1 && Header.CheckingId <= 0 && !isStoreman) || (currentStatus == 1 && Header.CheckingId > 0 && !isDepartmentUser) || (currentStatus == 2 && !isDepartmentHead)) return RedirectNoVoucherAccess();
        if (!Header.POID.HasValue || Header.POID.Value <= 0)
        {
            LoadLookups();
            EnsurePreviewCheckingId();
            ComputeActionUi();
            Message = "Please select a PO.";
            MessageType = "error";
            return Page();
        }
        if (Header.CheckingId <= 0)
        {
            if (!TryPersistStagedItems(out var createdCheckingId, out var errMsg))
            {
                LoadLookups();
                Header.CreatedDate = Header.CreatedDate == default ? DateTime.Now : Header.CreatedDate;
                Header.CreatedBy ??= GetCurrentEmployeeId();
                Header.StatusId = Header.StatusId == 0 ? 1 : Header.StatusId;
                EnsurePreviewCheckingId();
                ComputeActionUi();
                Message = errMsg;
                MessageType = "error";
                return Page();
            }
            Header.CheckingId = createdCheckingId;
            createdInThisAction = true;
            StagedItemsJson = "[]";
            if (SendCreateCheckMail)
            {
                _ = TryQueueNotifyCheckedUserAsync(createdCheckingId, Header.DeptChecked, Header.NotifyCheckedBy);
            }
        }

        if (currentStatus == 1 && !createdInThisAction && HasStagedItems())
        {
            if (!TryPersistStagedItemsForExisting(Header.CheckingId, out var stageErr))
            {
                LoadLookups();
                LoadDetail(Header.CheckingId);
                ComputeActionUi();
                Message = stageErr;
                MessageType = "error";
                return Page();
            }
        }

        if (currentStatus == 1 && !createdInThisAction && Header.CheckingId > 0 && DetailRows.Count > 0 && isStoreman)
        {
            if (!TryUpdateExistingDetailRows(Header.CheckingId, out var updateErr))
            {
                LoadLookups();
                LoadMaster(Header.CheckingId);
                LoadDetail(Header.CheckingId);
                ComputeActionUi();
                Message = updateErr;
                MessageType = "error";
                return Page();
            }
        }
        using var conn = OpenConnection();
        if (currentStatus == 1)
        {
            var sql = @"UPDATE dbo.INV_RecevingChekingVoucher
SET CheckedBy=ISNULL(CheckedBy,@EmpId), CheckedDate=GETDATE(), StatusID=2,
    ExpectDate=@ExpectDate, MRInfor=@MRInfor, CheckingMethod=@CheckingMethod, Result=@Result
WHERE CheckingID=@CheckingID";
            using var cmd = new SqlCommand(sql, conn);
            BindActionParams(cmd, 2);
            cmd.ExecuteNonQuery();
            UpdatePoStatusAfterChecking(conn, Header.POID);
            _ = TryQueueNotifyHeadDeptAsync(Header.CheckingId, Header.DeptChecked, "checked");
        }
        else if (currentStatus == 2 && IsHeadDept())
        {
            using var cmd = new SqlCommand("UPDATE dbo.INV_RecevingChekingVoucher SET ApprovedBy=@EmpId, ApprovedDate=GETDATE(), StatusID=3 WHERE CheckingID=@CheckingID", conn);
            cmd.Parameters.Add("@EmpId", SqlDbType.Int).Value = GetCurrentEmployeeId();
            cmd.Parameters.Add("@CheckingID", SqlDbType.Int).Value = Header.CheckingId;
            cmd.ExecuteNonQuery();
            UpdatePoStatusAfterApproval(conn, Header.POID, Header.CheckingId);
        }
        return RedirectToPage("./Index");
    }

    public IActionResult OnPostSave()
    {
        PagePerm = GetUserPermissions();
        ApplyDeptCheckScope();
        if (Header.CheckingId > 0 && !CanAccessCheckingVoucher(Header.CheckingId)) return RedirectNoVoucherAccess();

        var scope = LoadCurrentEmployeeScope();
        var isStoreman = IsAdminRole() || (scope.IsStoreKeeper && !scope.IsHeadDept);
        if (!isStoreman) return RedirectNoVoucherAccess();

        if (Header.StatusId >= 2)
        {
            LoadLookups();
            if (Header.CheckingId > 0)
            {
                LoadMaster(Header.CheckingId);
                LoadDetail(Header.CheckingId);
            }
            ComputeActionUi();
            Message = "Checked or approved voucher cannot be updated.";
            MessageType = "warning";
            return Page();
        }
        if (!Header.POID.HasValue || Header.POID.Value <= 0)
        {
            LoadLookups();
            EnsurePreviewCheckingId();
            ComputeActionUi();
            Message = "Please select a PO.";
            MessageType = "error";
            return Page();
        }

        if (Header.CheckingId <= 0)
        {
            if (!TryPersistStagedItems(out var createdCheckingId, out var errMsg))
            {
                LoadLookups();
                Header.CreatedDate = Header.CreatedDate == default ? DateTime.Now : Header.CreatedDate;
                Header.CreatedBy ??= GetCurrentEmployeeId();
                Header.StatusId = Header.StatusId == 0 ? 1 : Header.StatusId;
                EnsurePreviewCheckingId();
                ComputeActionUi();
                Message = errMsg;
                MessageType = "error";
                return Page();
            }
            if (SendCreateCheckMail)
            {
                var notifyResult = TryQueueNotifyCheckedUserAsync(createdCheckingId, Header.DeptChecked, Header.NotifyCheckedBy).GetAwaiter().GetResult();
                if (!string.IsNullOrWhiteSpace(notifyResult.Message))
                {
                    TempData["AlertMessage"] = notifyResult.Message;
                    TempData["AlertType"] = notifyResult.AlertType;
                }
            }
            return RedirectToPage("./Index");
        }

        if (HasStagedItems())
        {
            if (!TryPersistStagedItemsForExisting(Header.CheckingId, out var stageErr))
            {
                LoadLookups();
                LoadMaster(Header.CheckingId);
                LoadDetail(Header.CheckingId);
                ComputeActionUi();
                Message = stageErr;
                MessageType = "error";
                return Page();
            }
        }

        if (Header.CheckingId > 0 && DetailRows.Count > 0)
        {
            if (!TryUpdateExistingDetailRows(Header.CheckingId, out var updateErr))
            {
                LoadLookups();
                LoadMaster(Header.CheckingId);
                LoadDetail(Header.CheckingId);
                ComputeActionUi();
                Message = updateErr;
                MessageType = "error";
                return Page();
            }
        }

        using var conn = OpenConnection();
        using var cmd = new SqlCommand(@"UPDATE dbo.INV_RecevingChekingVoucher
SET ExpectDate=@ExpectDate, MRInfor=@MRInfor, CheckingMethod=@CheckingMethod, Result=@Result, CheckedBy=@CheckedBy
WHERE CheckingID=@CheckingID", conn);
        cmd.Parameters.Add("@ExpectDate", SqlDbType.DateTime).Value = Header.ExpectDate;
        cmd.Parameters.Add("@MRInfor", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(Header.MRInfor) ? string.Empty : Header.MRInfor.Trim();
        cmd.Parameters.Add("@CheckingMethod", SqlDbType.NVarChar, 254).Value = string.IsNullOrWhiteSpace(Header.CheckingMethod) ? string.Empty : Header.CheckingMethod.Trim();
        cmd.Parameters.Add("@Result", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(Header.Result) ? string.Empty : Header.Result.Trim();
        cmd.Parameters.Add("@CheckedBy", SqlDbType.Int).Value = Header.NotifyCheckedBy ?? (object)DBNull.Value;
        cmd.Parameters.Add("@CheckingID", SqlDbType.Int).Value = Header.CheckingId;
        cmd.ExecuteNonQuery();
        if (SendCreateCheckMail)
        {
            var notifyResult = TryQueueNotifyCheckedUserAsync(Header.CheckingId, Header.DeptChecked, Header.NotifyCheckedBy).GetAwaiter().GetResult();
            if (!string.IsNullOrWhiteSpace(notifyResult.Message))
            {
                TempData["AlertMessage"] = notifyResult.Message;
                TempData["AlertType"] = notifyResult.AlertType;
            }
        }
        return RedirectToPage("./Index");
    }

    private bool HasStagedItems() => !string.IsNullOrWhiteSpace(StagedItemsJson) && StagedItemsJson.Trim() != "[]";

    private bool TryUpdateExistingDetailRows(int checkingId, out string errorMessage)
    {
        errorMessage = string.Empty;
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            foreach (var row in DetailRows.Where(x => x.ChekingDTID > 0))
            {
                if (row.QuantityPassed <= 0) throw new Exception($"Quantity Passed must be greater than 0 for item {row.ItemName}.");
                if (row.QuantityPassed > row.QuantityCheck) throw new Exception($"Quantity Passed cannot exceed Qlt ({Math.Round(row.QuantityCheck, 0)}) for item {row.ItemName}.");
                if (row.Price < 0) throw new Exception($"Price cannot be negative for item {row.ItemName}.");
                using var cmd = new SqlCommand(@"UPDATE dbo.INV_ReceivingChekingVoucherDT
SET QuantityPassed=@QuantityPassed,
    Price=@Price,
    Amount=@Amount,
    Notes=@Notes
WHERE ChekingDTID=@ChekingDTID AND CheckingID=@CheckingID", conn, tx);
                cmd.Parameters.Add("@QuantityPassed", SqlDbType.Decimal).Value = row.QuantityPassed;
                cmd.Parameters.Add("@Price", SqlDbType.Decimal).Value = row.Price;
                cmd.Parameters.Add("@Amount", SqlDbType.Decimal).Value = row.QuantityPassed * row.Price;
                cmd.Parameters.Add("@Notes", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(row.Notes) ? "" : row.Notes.Trim();
                cmd.Parameters.Add("@ChekingDTID", SqlDbType.Int).Value = row.ChekingDTID;
                cmd.Parameters.Add("@CheckingID", SqlDbType.Int).Value = checkingId;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return true;
        }
        catch (Exception ex)
        {
            tx.Rollback();
            errorMessage = ex.Message;
            return false;
        }
    }

    private bool TryPersistStagedItemsForExisting(int checkingId, out string errorMessage)
    {
        errorMessage = string.Empty;
        List<SaveSelectedItemVm> stagedItems;
        try
        {
            stagedItems = JsonSerializer.Deserialize<List<SaveSelectedItemVm>>(StagedItemsJson ?? "[]", JsonCaseInsensitiveOptions) ?? new();
        }
        catch
        {
            errorMessage = "Invalid item staging data.";
            return false;
        }
        if (stagedItems.Count == 0) return true;

        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            foreach (var it in stagedItems)
            {
                using var remainCmd = new SqlCommand("SELECT dbo.GetQuantityFromPOMinusInAllChecking(@POID,@ItemID,@MRDetailID)", conn, tx);
                remainCmd.Parameters.Add("@POID", SqlDbType.Int).Value = Header.POID!.Value;
                remainCmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = it.ItemId;
                remainCmd.Parameters.Add("@MRDetailID", SqlDbType.Int).Value = it.MrDetailId <= 0 ? DBNull.Value : it.MrDetailId;
                var remainObj = remainCmd.ExecuteScalar();
                var remainQty = remainObj == null || remainObj == DBNull.Value ? 0m : Convert.ToDecimal(remainObj);
                if (it.QuantityPassed <= 0 || it.QuantityPassed > remainQty)
                {
                    throw new Exception($"Invalid quantity for item {it.ItemId}. Remain: {remainQty}");
                }
                using var cmd = new SqlCommand(@"INSERT INTO dbo.INV_ReceivingChekingVoucherDT(CheckingID,ItemID,QuantityCheck,QuantityPassed,Price,Amount,Notes,MRDetailID)
VALUES(@CheckingID,@ItemID,@QuantityCheck,@QuantityPassed,@Price,@Amount,@Notes,@MRDetailID)", conn, tx);
                cmd.Parameters.Add("@CheckingID", SqlDbType.Int).Value = checkingId;
                cmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = it.ItemId;
                cmd.Parameters.Add("@QuantityCheck", SqlDbType.Decimal).Value = it.QuantityPassed;
                cmd.Parameters.Add("@QuantityPassed", SqlDbType.Decimal).Value = it.QuantityPassed;
                cmd.Parameters.Add("@Price", SqlDbType.Decimal).Value = it.Price;
                cmd.Parameters.Add("@Amount", SqlDbType.Decimal).Value = it.QuantityPassed * it.Price;
                cmd.Parameters.Add("@Notes", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(it.Note) ? string.Empty : it.Note.Trim();
                cmd.Parameters.Add("@MRDetailID", SqlDbType.Int).Value = it.MrDetailId <= 0 ? DBNull.Value : it.MrDetailId;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            StagedItemsJson = "[]";
            return true;
        }
        catch (Exception ex)
        {
            tx.Rollback();
            errorMessage = ex.Message;
            return false;
        }
    }

    private bool TryPersistStagedItems(out int checkingId, out string errorMessage)
    {
        checkingId = 0;
        errorMessage = string.Empty;
        List<SaveSelectedItemVm> stagedItems;
        try
        {
            stagedItems = JsonSerializer.Deserialize<List<SaveSelectedItemVm>>(StagedItemsJson ?? "[]", JsonCaseInsensitiveOptions) ?? new();
        }
        catch
        {
            errorMessage = "Invalid item staging data.";
            return false;
        }

        var scope = LoadCurrentEmployeeScope();




        if (!Header.POID.HasValue || Header.POID.Value <= 0) { errorMessage = "Please select a PO."; return false; }
        if (!Header.DeptChecked.HasValue || Header.DeptChecked.Value <= 0) { errorMessage = "Please select Department to check item."; return false; }
        if (stagedItems.Count == 0) { errorMessage = "Please add at least one item from PO."; return false; }

        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            checkingId = InsertMaster(Header.POID.Value, conn, tx);
            foreach (var it in stagedItems)
            {
                using var remainCmd = new SqlCommand("SELECT dbo.GetQuantityFromPOMinusInAllChecking(@POID,@ItemID,@MRDetailID)", conn, tx);
                remainCmd.Parameters.Add("@POID", SqlDbType.Int).Value = Header.POID.Value;
                remainCmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = it.ItemId;
                remainCmd.Parameters.Add("@MRDetailID", SqlDbType.Int).Value = it.MrDetailId <= 0 ? DBNull.Value : it.MrDetailId;
                var remainObj = remainCmd.ExecuteScalar();
                var remainQty = remainObj == null || remainObj == DBNull.Value ? 0m : Convert.ToDecimal(remainObj);
                if (it.QuantityPassed <= 0 || it.QuantityPassed > remainQty)
                {
                    throw new Exception($"Invalid quantity for item {it.ItemId}. Remain: {remainQty}");
                }
                using var cmd = new SqlCommand(@"INSERT INTO dbo.INV_ReceivingChekingVoucherDT(CheckingID,ItemID,QuantityCheck,QuantityPassed,Price,Amount,Notes,MRDetailID)
VALUES(@CheckingID,@ItemID,@QuantityCheck,@QuantityPassed,@Price,@Amount,@Notes,@MRDetailID)", conn, tx);
                cmd.Parameters.Add("@CheckingID", SqlDbType.Int).Value = checkingId;
                cmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = it.ItemId;
                cmd.Parameters.Add("@QuantityCheck", SqlDbType.Decimal).Value = it.QuantityPassed;
                cmd.Parameters.Add("@QuantityPassed", SqlDbType.Decimal).Value = it.QuantityPassed;
                cmd.Parameters.Add("@Price", SqlDbType.Decimal).Value = it.Price;
                cmd.Parameters.Add("@Amount", SqlDbType.Decimal).Value = it.QuantityPassed * it.Price;
                cmd.Parameters.Add("@Notes", SqlDbType.NVarChar, 200).Value = string.IsNullOrWhiteSpace(it.Note) ? string.Empty : it.Note.Trim();
                cmd.Parameters.Add("@MRDetailID", SqlDbType.Int).Value = it.MrDetailId <= 0 ? DBNull.Value : it.MrDetailId;
                cmd.ExecuteNonQuery();
            }
            tx.Commit();
            return true;
        }
        catch (Exception ex)
        {
            tx.Rollback();
            errorMessage = ex.Message;
            return false;
        }
    }

    public IActionResult OnPostDisapprove()
    {
        PagePerm = GetUserPermissions();
        if (Header.CheckingId > 0 && !CanAccessCheckingVoucher(Header.CheckingId)) return RedirectNoVoucherAccess();
        var scope = LoadCurrentEmployeeScope();
        if (!scope.IsHeadDept && !IsAdminRole()) return RedirectNoVoucherAccess();
        if (Header.CheckingId > 0)
        {
            using var conn = OpenConnection();
            using var cmd = new SqlCommand("UPDATE dbo.INV_RecevingChekingVoucher SET StatusID = 5 WHERE CheckingID = @CheckingID", conn);
            cmd.Parameters.Add("@CheckingID", SqlDbType.Int).Value = Header.CheckingId;
            cmd.ExecuteNonQuery();
            if (Header.POID.HasValue)
            {
                using var poCmd = new SqlCommand("UPDATE dbo.PC_PO SET StatusID = 4 WHERE POID=@POID", conn);
                poCmd.Parameters.Add("@POID", SqlDbType.Int).Value = Header.POID.Value;
                poCmd.ExecuteNonQuery();
            }
        }
        return RedirectToPage("./Index");
    }
    private void ComputeActionUi()
    {
        var scope = LoadCurrentEmployeeScope();
        var isStoreman = IsAdminRole() || (scope.IsStoreKeeper && !scope.IsHeadDept);
        var isDepartmentUser = !scope.IsStoreKeeper && !scope.IsHeadDept;
        var isDepartmentHead = scope.IsHeadDept;
        CanAction = !string.Equals(Mode, "view", StringComparison.OrdinalIgnoreCase) && (Mode == "add" || Mode == "edit" || IsApprovedMode);
        CanSaveDraft = isStoreman && !IsReadOnly && (Mode == "add" || Mode == "edit") && Header.StatusId == 1;
        CanCheckAction = false;
        CanApproveAction = false;
        CanDisapprove = false;
        CanEditCheckingFields = false;
        if (Header.StatusId == 1)
        {
            ActionCaption = Mode == "add" ? "Save" : "Check";
            CanCheckAction = isDepartmentUser && (string.Equals(Mode, "edit", StringComparison.OrdinalIgnoreCase) || IsApprovedMode);
            CanEditCheckingFields = CanCheckAction;
        }
        else if (Header.StatusId == 2)
        {
            ActionCaption = "Approve";
            CanApproveAction = isDepartmentHead;
            CanDisapprove = isDepartmentHead;
        }
        else if (Header.StatusId == 3 || Header.StatusId == 4 || Header.StatusId == 5)
        {
            CanAction = false;
            CanSaveDraft = false;
        }
    }

    private void LoadLookups()
    {
        var scope = LoadCurrentEmployeeScope();
        Departments = LoadDepartmentLookup(IsAdminRole() || scope.StoreGroupId == 1 ? null : scope.DeptId);
        if ((!Header.DeptChecked.HasValue || Header.DeptChecked.Value <= 0) && Departments.Count > 0 && int.TryParse(Departments[0].Value, out var firstDeptId))
        {
            Header.DeptChecked = firstDeptId;
        }
        Statuses = LoadListFromSql("SELECT CheckingVoucherStatusID, CheckingVoucherStatusName FROM dbo.INV_CheckingVoucherStatus ORDER BY CheckingVoucherStatusID", "CheckingVoucherStatusID", "CheckingVoucherStatusName");
        Employees = LoadListFromSql("SELECT EmployeeID, EmployeeName FROM dbo.MS_Employee ORDER BY EmployeeName", "EmployeeID", "EmployeeName");
        CheckByEmployees = LoadCheckByEmployees(Header.DeptChecked);
        Pos = LoadPoList();
    }

    private List<SelectListItem> LoadDepartmentLookup(int? deptId)
    {
        var items = new List<SelectListItem>();
        using var conn = OpenConnection();
        using var cmd = new SqlCommand(deptId.HasValue
            ? "SELECT DeptID, DeptName FROM dbo.MS_Department WHERE DeptID=@DeptID ORDER BY DeptName"
            : "SELECT DeptID, DeptName FROM dbo.MS_Department ORDER BY DeptName", conn);
        if (deptId.HasValue) cmd.Parameters.Add("@DeptID", SqlDbType.Int).Value = deptId.Value;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            items.Add(new SelectListItem(Convert.ToString(rd["DeptName"]) ?? string.Empty, Convert.ToString(rd["DeptID"]) ?? string.Empty));
        }
        return items;
    }

    private List<SelectListItem> LoadCheckByEmployees(int? deptId)
    {
        var items = new List<SelectListItem> { new("--- Select ---", string.Empty) };
        if (!deptId.HasValue || deptId.Value <= 0) return items;
        using var conn = OpenConnection();
        using var cmd = new SqlCommand(@"SELECT EmployeeID, EmployeeName
FROM dbo.MS_Employee
WHERE DeptID=@DeptID
  AND ISNULL(IsActive,0)=1
  AND ISNULL(IsStoreKeeper,0)<>1
  AND ISNULL(HeadDept,0)<>1
  AND EmployeeCode NOT LIKE '%X'
ORDER BY EmployeeName", conn);
        cmd.Parameters.Add("@DeptID", SqlDbType.Int).Value = deptId.Value;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            items.Add(new SelectListItem(Convert.ToString(rd["EmployeeName"]) ?? string.Empty, Convert.ToString(rd["EmployeeID"]) ?? string.Empty));
        }
        return items;
    }

    private void ApplyDeptCheckScope()
    {
        var scope = LoadCurrentEmployeeScope();
        LockDeptCheck = !IsAdminRole() && scope.StoreGroupId != 1;
        if (LockDeptCheck && scope.DeptId.HasValue)
        {
            Header.DeptChecked = scope.DeptId.Value;
        }
    }

    private List<SelectListItem> LoadPoList()
    {
        var items = new List<SelectListItem>();
        using var conn = OpenConnection();
        using var cmd = new SqlCommand(@"SELECT p.POID, p.PONo,
       CASE WHEN p.POID=@CurrentPOID THEN 1 ELSE 0 END AS IsCurrentPO
FROM dbo.PC_PO p
WHERE (
        p.StatusID IN (3,4,6)
        AND EXISTS (
            SELECT 1
            FROM dbo.PC_PODetail d
            WHERE d.POID = p.POID
              AND dbo.GetQuantityFromPOMinusInAllChecking(p.POID, d.ItemID, d.MRDetailID) > 0
        )
      )
   OR p.POID=@CurrentPOID
ORDER BY p.PODate DESC", conn);
        cmd.Parameters.Add("@CurrentPOID", SqlDbType.Int).Value = Header.POID.HasValue && Header.POID > 0 ? Header.POID.Value : DBNull.Value;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            items.Add(new SelectListItem
            {
                Value = Convert.ToString(rd["POID"]) ?? string.Empty,
                Text = Convert.ToString(rd["PONo"]) ?? string.Empty,
                Disabled = false
            });
        }
        return items;
    }
    private void InitData()
    {
        Header.CreatedDate = DateTime.Now;
        Header.ExpectDate = DateTime.Now.AddDays(2);
        Header.CreatedBy = GetCurrentEmployeeId();
        Header.StatusId = 1;
        PreviewCheckingId = GetNextCheckingIdPreview();
    }
    private void LoadMaster(int checkingId)
    {
        using var conn = OpenConnection();
        using var cmd = new SqlCommand("SELECT CheckingID, CreateDate, CreatedBy, POID, ExpectDate, DeptChecked, StatusID, MRInfor, CheckingMethod, Result, CheckedBy, CheckedDate, ApprovedBy, ApprovedDate FROM dbo.INV_RecevingChekingVoucher WHERE CheckingID=@CheckingID", conn);
        cmd.Parameters.Add("@CheckingID", SqlDbType.Int).Value = checkingId;
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return;
        Header.CheckingId = checkingId;
        Header.CreatedDate = rd.IsDBNull(1) ? DateTime.Now : Convert.ToDateTime(rd[1]);
        Header.CreatedBy = ResolveEmployeeId(rd.IsDBNull(2) ? null : rd[2]);
        Header.POID = rd.IsDBNull(3) ? null : Convert.ToInt32(rd[3]);
        Header.ExpectDate = rd.IsDBNull(4) ? DateTime.Now : Convert.ToDateTime(rd[4]);
        Header.DeptChecked = rd.IsDBNull(5) ? null : Convert.ToInt32(rd[5]);
        Header.StatusId = rd.IsDBNull(6) ? 1 : Convert.ToInt32(rd[6]);
        Header.MRInfor = NormalizeLegacyText(rd.IsDBNull(7) ? "" : Convert.ToString(rd[7]) ?? "");
        Header.CheckingMethod = NormalizeLegacyText(rd.IsDBNull(8) ? "" : Convert.ToString(rd[8]) ?? "");
        Header.Result = NormalizeLegacyText(rd.IsDBNull(9) ? "" : Convert.ToString(rd[9]) ?? "");
        Header.CheckedBy = rd.IsDBNull(10) ? null : Convert.ToInt32(rd[10]);
        Header.CheckedDate = rd.IsDBNull(11) ? null : Convert.ToDateTime(rd[11]);
        Header.ApprovedBy = rd.IsDBNull(12) ? null : Convert.ToInt32(rd[12]);
        Header.ApprovedDate = rd.IsDBNull(13) ? null : Convert.ToDateTime(rd[13]);
        Header.NotifyCheckedBy = Header.CheckedBy;
    }
    private int? ResolveEmployeeId(object? value)
    {
        if (value == null || value == DBNull.Value) return null;
        if (int.TryParse(Convert.ToString(value)?.Trim(), out var employeeId)) return employeeId;

        var employeeCode = Convert.ToString(value)?.Trim();
        if (string.IsNullOrWhiteSpace(employeeCode)) return null;

        using var conn = OpenConnection();
        using var cmd = new SqlCommand("SELECT TOP 1 EmployeeID FROM dbo.MS_Employee WHERE LTRIM(RTRIM(EmployeeCode))=@EmployeeCode", conn);
        cmd.Parameters.Add("@EmployeeCode", SqlDbType.VarChar, 50).Value = employeeCode;
        var result = cmd.ExecuteScalar();
        return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }
    private void LoadDetail(int checkingId)
    {
        DetailRows.Clear();
        using var conn = OpenConnection();
        using var cmd = new SqlCommand(@"SELECT d.ChekingDTID, d.ItemID, i.ItemCode + '-' + i.ItemName AS ItemName, d.QuantityCheck, d.QuantityPassed, d.Price, d.Amount, d.Notes, d.MRDetailID
FROM dbo.INV_ReceivingChekingVoucherDT d
LEFT JOIN dbo.INV_ItemList i ON i.ItemID=d.ItemID
WHERE d.CheckingID=@CheckingID", conn);
        cmd.Parameters.Add("@CheckingID", SqlDbType.Int).Value = checkingId;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            DetailRows.Add(new ItemCheckingDetailRowVm
            {
                ChekingDTID = rd.IsDBNull(0) ? 0 : Convert.ToInt32(rd[0]),
                ItemID = rd.IsDBNull(1) ? 0 : Convert.ToInt32(rd[1]),
                ItemName = rd.IsDBNull(2) ? "" : Convert.ToString(rd[2]) ?? "",
                QuantityCheck = rd.IsDBNull(3) ? 0 : Convert.ToDecimal(rd[3]),
                QuantityPassed = rd.IsDBNull(4) ? 0 : Convert.ToDecimal(rd[4]),
                Price = rd.IsDBNull(5) ? 0 : Convert.ToDecimal(rd[5]),
                Amount = rd.IsDBNull(6) ? 0 : Convert.ToDecimal(rd[6]),
                Notes = NormalizeLegacyText(rd.IsDBNull(7) ? "" : Convert.ToString(rd[7]) ?? ""),
                MRDetailID = rd.IsDBNull(8) ? null : Convert.ToInt32(rd[8])
            });
        }
    }
    private static string NormalizeLegacyText(string value)
    {
        var text = (value ?? string.Empty).Trim();
        return text == "-" ? string.Empty : text;
    }
    private int InsertMaster(int poid)
    {
        using var conn = OpenConnection();
        return InsertMaster(poid, conn, null);
    }
    private int InsertMaster(int poid, SqlConnection conn, SqlTransaction? tx)
    {
        using var cmd = new SqlCommand(@"INSERT INTO dbo.INV_RecevingChekingVoucher(CreateDate,CreatedBy,POID,ExpectDate,DeptChecked,StatusID,MRInfor,CheckingMethod,Result,CheckedBy)
VALUES(GETDATE(),@CreatedBy,@POID,@ExpectDate,@DeptChecked,1,@MRInfor,@CheckingMethod,@Result,@CheckedBy);
SELECT CAST(SCOPE_IDENTITY() AS INT);", conn, tx);
        cmd.Parameters.Add("@CreatedBy", SqlDbType.Int).Value = GetCurrentEmployeeId();
        cmd.Parameters.Add("@POID", SqlDbType.Int).Value = poid;
        cmd.Parameters.Add("@ExpectDate", SqlDbType.DateTime).Value = Header.ExpectDate;
        cmd.Parameters.Add("@DeptChecked", SqlDbType.Int).Value = Header.DeptChecked ?? (object)DBNull.Value;
        cmd.Parameters.Add("@MRInfor", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(Header.MRInfor) ? string.Empty : Header.MRInfor.Trim();
        cmd.Parameters.Add("@CheckingMethod", SqlDbType.NVarChar, 254).Value = string.IsNullOrWhiteSpace(Header.CheckingMethod) ? string.Empty : Header.CheckingMethod.Trim();
        cmd.Parameters.Add("@Result", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(Header.Result) ? string.Empty : Header.Result.Trim();
        cmd.Parameters.Add("@CheckedBy", SqlDbType.Int).Value = Header.NotifyCheckedBy ?? (object)DBNull.Value;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
    private void EnsurePreviewCheckingId()
    {
        if (Header.CheckingId <= 0 && PreviewCheckingId <= 0)
        {
            PreviewCheckingId = GetNextCheckingIdPreview();
        }
    }
    private int GetNextCheckingIdPreview()
    {
        using var conn = OpenConnection();
        using var cmd = new SqlCommand("SELECT ISNULL(MAX(CheckingID), 0) + 1 FROM dbo.INV_RecevingChekingVoucher", conn);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 1);
    }
    private int GetCurrentStatus(SqlConnection conn, int checkingId)
    {
        using var cmd = new SqlCommand("SELECT ISNULL(StatusID,1) FROM dbo.INV_RecevingChekingVoucher WHERE CheckingID=@CheckingID", conn);
        cmd.Parameters.Add("@CheckingID", SqlDbType.Int).Value = checkingId;
        var val = cmd.ExecuteScalar();
        return val == null || val == DBNull.Value ? 1 : Convert.ToInt32(val);
    }
    private void BindActionParams(SqlCommand cmd, int toStatus)
    {
        cmd.Parameters.Add("@EmpId", SqlDbType.Int).Value = GetCurrentEmployeeId();
        cmd.Parameters.Add("@ExpectDate", SqlDbType.DateTime).Value = Header.ExpectDate;
        cmd.Parameters.Add("@MRInfor", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(Header.MRInfor) ? string.Empty : Header.MRInfor.Trim();
        cmd.Parameters.Add("@CheckingMethod", SqlDbType.NVarChar, 254).Value = string.IsNullOrWhiteSpace(Header.CheckingMethod) ? string.Empty : Header.CheckingMethod.Trim();
        cmd.Parameters.Add("@Result", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(Header.Result) ? string.Empty : Header.Result.Trim();
        cmd.Parameters.Add("@CheckingID", SqlDbType.Int).Value = Header.CheckingId;
    }
    private bool IsHeadDept()
    {
        var employeeCode = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(employeeCode)) return false;
        using var conn = OpenConnection();
        using var cmd = new SqlCommand("SELECT TOP 1 ISNULL(HeadDept,0) FROM dbo.MS_Employee WHERE EmployeeCode=@EmployeeCode", conn);
        cmd.Parameters.Add("@EmployeeCode", SqlDbType.VarChar, 10).Value = employeeCode.Trim();
        var value = cmd.ExecuteScalar();
        return value != null && value != DBNull.Value && Convert.ToInt32(value) == 1;
    }
    private int GetCurrentEmployeeId() => int.TryParse(User.FindFirst("EmployeeID")?.Value, out var employeeId) ? employeeId : 0;
    private void UpdatePoStatusAfterChecking(SqlConnection conn, int? poid)
    {
        if (!poid.HasValue || poid.Value <= 0) return;
        using var cmd = new SqlCommand("UPDATE dbo.PC_PO SET StatusID = 4 WHERE POID=@POID", conn);
        cmd.Parameters.Add("@POID", SqlDbType.Int).Value = poid.Value;
        cmd.ExecuteNonQuery();
    }
    private void UpdatePoStatusAfterApproval(SqlConnection conn, int? poid, int checkingId)
    {
        if (!poid.HasValue || poid.Value <= 0) return;
        var allChecked = AreAllPoItemsCovered(conn, poid.Value, checkingId);
        using var cmd = new SqlCommand("UPDATE dbo.PC_PO SET StatusID = @StatusID WHERE POID=@POID", conn);
        cmd.Parameters.Add("@StatusID", SqlDbType.Int).Value = allChecked ? 5 : 4;
        cmd.Parameters.Add("@POID", SqlDbType.Int).Value = poid.Value;
        cmd.ExecuteNonQuery();
    }
    private bool AreAllPoItemsCovered(SqlConnection conn, int poid, int checkingId)
    {
        using var cmd = new SqlCommand(@"SELECT COUNT(1)
FROM dbo.PC_PODetail d
WHERE d.POID=@POID
  AND dbo.GetQuantityFromPOMinusInAllChecking(@POID, d.ItemID, d.MRDetailID) > 0", conn);
        cmd.Parameters.Add("@POID", SqlDbType.Int).Value = poid;
        var remaining = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        return remaining == 0;
    }
    private async Task<string?> TryQueueNotifyHeadDeptAsync(int checkingId, int? deptId, string action)
    {
        if (!deptId.HasValue || deptId.Value <= 0) return "Department not found.";
        var recipients = GetHeadDeptRecipients(deptId.Value);
        if (recipients.Count == 0) return "No head dept email recipients found.";
        var senderEmail = _config.GetValue<string>("EmailSettings:SenderEmail") ?? string.Empty;
        var password = _config.GetValue<string>("EmailSettings:Password") ?? string.Empty;
        var mailServer = _config.GetValue<string>("EmailSettings:MailServer") ?? string.Empty;
        var mailPort = _config.GetValue<int?>("EmailSettings:MailPort") ?? 0;
        if (string.IsNullOrWhiteSpace(senderEmail) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(mailServer) || mailPort <= 0)
            return "Email settings missing.";

        var detailUrl = Url.Page("/Inventory/InventoryItemChecking/ItemCheckingDetail", values: new { id = checkingId, mode = "approved" });
        var absoluteUrl = string.IsNullOrWhiteSpace(detailUrl) ? string.Empty : $"{Request.Scheme}://{Request.Host}{detailUrl}";
        var deptName = GetDepartmentName(deptId.Value);
        var subject = ApplyMailSubjectPrefix($"[Inventory Item Checking] Please approve checking ID {checkingId}");
        var body = $@"
<p>Dear {{RECIPIENT_LABEL}},</p>
<p>A receiving checking voucher has been {WebUtility.HtmlEncode(action)} and is waiting for your approval.</p>
<ul>
    <li>Checking ID: <b>{checkingId}</b></li>
    <li>Department: <b>{WebUtility.HtmlEncode(deptName)}</b></li>
    <li>Date: <b>{DateTime.Now.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}</b></li>
</ul>
<p><b>Click Here to Approve:</b> <a href='{WebUtility.HtmlEncode(absoluteUrl)}'>Open checking voucher</a></p>
<p>Best regards,<br/>SmartSam System</p>";
        body = WrapNotifyMessageBody(body);
        var htmlBody = EmailTemplateHelper.WrapInNotifyTemplate("INVENTORY ITEM CHECKING", "#007bff", DateTime.Now, body);
        var request = new ItemCheckingNotifyRequest
        {
            SenderEmail = senderEmail,
            Password = password,
            MailServer = mailServer,
            MailPort = mailPort,
            Subject = subject,
            HtmlBody = htmlBody,
            RecipientDetails = recipients,
            DefaultRecipientLabel = string.Join(", ", recipients.Select(BuildRecipientDisplayName)),
            SendIndividually = true
        };
        await SendNotifyEmailAsync(request);
        return null;
    }
    private List<ItemCheckingNotifyRecipient> GetHeadDeptRecipients(int deptId)
    {
        var rows = new List<ItemCheckingNotifyRecipient>();
        using var conn = OpenConnection();
        using var cmd = new SqlCommand(@"SELECT DISTINCT LTRIM(RTRIM(TheEmail)) AS Email, LTRIM(RTRIM(EmployeeCode)) AS EmployeeCode, LTRIM(RTRIM(EmployeeName)) AS EmployeeName, LTRIM(RTRIM(ISNULL(Title, ''))) AS Title
FROM dbo.MS_Employee
WHERE HeadDept = 1 AND DeptID = @DeptID AND ISNULL(IsActive,0)=1 AND ISNULL(LTRIM(RTRIM(TheEmail)), '') <> '' AND EmployeeCode NOT LIKE '%X'", conn);
        cmd.Parameters.Add("@DeptID", SqlDbType.Int).Value = deptId;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(new ItemCheckingNotifyRecipient
            {
                Email = Convert.ToString(rd["Email"])?.Trim() ?? string.Empty,
                EmployeeCode = Convert.ToString(rd["EmployeeCode"])?.Trim() ?? string.Empty,
                EmployeeName = Convert.ToString(rd["EmployeeName"])?.Trim() ?? string.Empty,
                Title = Convert.ToString(rd["Title"])?.Trim() ?? string.Empty
            });
        }
        return rows;
    }

    private async Task<ItemCheckingNotifyOutcome> TryQueueNotifyCheckedUserAsync(int checkingId, int? deptId, int? checkedBy)
    {
        if (!deptId.HasValue || deptId.Value <= 0) return new(false, "Department not found.", "warning");
        var resolveResult = ResolveCheckedUserNotification(deptId.Value, checkedBy);
        if (resolveResult.Recipients.Count == 0) return new(false, resolveResult.Message, "warning");
        var senderEmail = _config.GetValue<string>("EmailSettings:SenderEmail") ?? string.Empty;
        var password = _config.GetValue<string>("EmailSettings:Password") ?? string.Empty;
        var mailServer = _config.GetValue<string>("EmailSettings:MailServer") ?? string.Empty;
        var mailPort = _config.GetValue<int?>("EmailSettings:MailPort") ?? 0;
        if (string.IsNullOrWhiteSpace(senderEmail) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(mailServer) || mailPort <= 0)
            return new(false, "Email settings missing.", "warning");

        var detailUrl = Url.Page("/Inventory/InventoryItemChecking/ItemCheckingDetail", values: new { id = checkingId, mode = "approved" });
        var absoluteUrl = string.IsNullOrWhiteSpace(detailUrl) ? string.Empty : $"{Request.Scheme}://{Request.Host}{detailUrl}";
        var deptName = GetDepartmentName(deptId.Value);
        var subject = ApplyMailSubjectPrefix($"[Inventory Item Checking] Please check checking ID {checkingId}");
        var body = $@"
<p>Dear {{RECIPIENT_LABEL}},</p>
<p>A receiving checking voucher is waiting for your checking.</p>
<ul>
    <li>Checking ID: <b>{checkingId}</b></li>
    <li>Department: <b>{WebUtility.HtmlEncode(deptName)}</b></li>
    <li>Date: <b>{DateTime.Now.ToString("MMM d, yyyy", CultureInfo.InvariantCulture)}</b></li>
</ul>
<p><b>Click Here to Check:</b> <a href='{WebUtility.HtmlEncode(absoluteUrl)}'>Open checking voucher</a></p>
<p>Best regards,<br/>SmartSam System</p>";
        body = WrapNotifyMessageBody(body);
        var htmlBody = EmailTemplateHelper.WrapInNotifyTemplate("INVENTORY ITEM CHECKING", "#007bff", DateTime.Now, body);
        try
        {
            await SendNotifyEmailAsync(new ItemCheckingNotifyRequest
            {
                SenderEmail = senderEmail,
                Password = password,
                MailServer = mailServer,
                MailPort = mailPort,
                Subject = subject,
                HtmlBody = htmlBody,
                RecipientDetails = resolveResult.Recipients,
                DefaultRecipientLabel = string.Join(", ", resolveResult.Recipients.Select(BuildRecipientDisplayName)),
                SendIndividually = true
            });
            return new(true, resolveResult.Message, resolveResult.AlertType);
        }
        catch (Exception ex)
        {
            return new(false, $"Cannot send email notification. {ex.Message}", "warning");
        }
    }

    private ItemCheckingNotifyResolveResult ResolveCheckedUserNotification(int deptId, int? checkedBy)
    {
        if (checkedBy.HasValue && checkedBy.Value > 0)
        {
            var checkedUser = GetEmployeeNotifyLookup(checkedBy.Value, deptId);
            if (checkedUser.Exists && checkedUser.Recipient != null)
            {
                return new(new List<ItemCheckingNotifyRecipient> { checkedUser.Recipient }, "Notification email sent to Checked By.", "success");
            }

            var manager = GetInventoryControlRecipientLookup(deptId);
            if (manager.Recipients.Count > 0)
            {
                return new(manager.Recipients, "Cannot find Checked By email, notification email was sent to inventory controller.", "warning");
            }

            var checkedUserMessage = checkedUser.Exists
                ? "Cannot find Checked By email."
                : "Cannot find Checked By user."
                ;
            return new(new List<ItemCheckingNotifyRecipient>(), $"{checkedUserMessage} {manager.Message}", "warning");
        }

        var inventoryController = GetInventoryControlRecipientLookup(deptId);
        if (inventoryController.Recipients.Count > 0)
        {
            return new(inventoryController.Recipients, "Checked By is empty, notification email was sent to inventory controller.", "warning");
        }

        return new(new List<ItemCheckingNotifyRecipient>(), $"Checked By is empty. {inventoryController.Message}", "warning");
    }

    private List<ItemCheckingNotifyRecipient> GetCheckedUserRecipientOrInventoryControl(int deptId, int employeeId)
    {
        var recipient = GetEmployeeMailRecipient(employeeId, deptId);
        return recipient == null ? GetInventoryControlRecipients(deptId) : new List<ItemCheckingNotifyRecipient> { recipient };
    }

    private ItemCheckingNotifyRecipient? GetEmployeeMailRecipient(int employeeId, int deptId)
    {
        using var conn = OpenConnection();
        using var cmd = new SqlCommand(@"SELECT TOP 1 LTRIM(RTRIM(TheEmail)) AS Email, LTRIM(RTRIM(EmployeeCode)) AS EmployeeCode, LTRIM(RTRIM(EmployeeName)) AS EmployeeName, LTRIM(RTRIM(ISNULL(Title, ''))) AS Title
FROM dbo.MS_Employee
WHERE EmployeeID=@EmployeeID AND DeptID=@DeptID AND ISNULL(IsActive,0)=1 AND ISNULL(LTRIM(RTRIM(TheEmail)), '') <> '' AND EmployeeCode NOT LIKE '%X'", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId;
        cmd.Parameters.Add("@DeptID", SqlDbType.Int).Value = deptId;
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;
        return new ItemCheckingNotifyRecipient
        {
            Email = Convert.ToString(rd["Email"])?.Trim() ?? string.Empty,
            EmployeeCode = Convert.ToString(rd["EmployeeCode"])?.Trim() ?? string.Empty,
            EmployeeName = Convert.ToString(rd["EmployeeName"])?.Trim() ?? string.Empty,
            Title = Convert.ToString(rd["Title"])?.Trim() ?? string.Empty
        };
    }

    private (bool Exists, ItemCheckingNotifyRecipient? Recipient) GetEmployeeNotifyLookup(int employeeId, int deptId)
    {
        using var conn = OpenConnection();
        using var cmd = new SqlCommand(@"SELECT TOP 1 LTRIM(RTRIM(ISNULL(TheEmail,''))) AS Email, LTRIM(RTRIM(EmployeeCode)) AS EmployeeCode, LTRIM(RTRIM(EmployeeName)) AS EmployeeName, LTRIM(RTRIM(ISNULL(Title, ''))) AS Title
FROM dbo.MS_Employee
WHERE EmployeeID=@EmployeeID AND DeptID=@DeptID AND ISNULL(IsActive,0)=1 AND EmployeeCode NOT LIKE '%X'", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId;
        cmd.Parameters.Add("@DeptID", SqlDbType.Int).Value = deptId;
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return (false, null);
        var email = Convert.ToString(rd["Email"])?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(email)) return (true, null);
        return (true, new ItemCheckingNotifyRecipient
        {
            Email = email,
            EmployeeCode = Convert.ToString(rd["EmployeeCode"])?.Trim() ?? string.Empty,
            EmployeeName = Convert.ToString(rd["EmployeeName"])?.Trim() ?? string.Empty,
            Title = Convert.ToString(rd["Title"])?.Trim() ?? string.Empty
        });
    }

    private ItemCheckingNotifyResolveResult GetInventoryControlRecipientLookup(int deptId)
    {
        var rows = new List<ItemCheckingNotifyRecipient>();
        var userCount = 0;
        using var conn = OpenConnection();
        using var cmd = new SqlCommand(@"SELECT DISTINCT LTRIM(RTRIM(ISNULL(TheEmail,''))) AS Email, LTRIM(RTRIM(EmployeeCode)) AS EmployeeCode, LTRIM(RTRIM(EmployeeName)) AS EmployeeName, LTRIM(RTRIM(ISNULL(Title, ''))) AS Title
FROM dbo.MS_Employee
WHERE DeptID=@DeptID AND ISNULL(IsInventoryControlInDep,0)=1 AND ISNULL(IsActive,0)=1 AND EmployeeCode NOT LIKE '%X'", conn);
        cmd.Parameters.Add("@DeptID", SqlDbType.Int).Value = deptId;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            userCount++;
            var email = Convert.ToString(rd["Email"])?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(email)) continue;
            rows.Add(new ItemCheckingNotifyRecipient
            {
                Email = email,
                EmployeeCode = Convert.ToString(rd["EmployeeCode"])?.Trim() ?? string.Empty,
                EmployeeName = Convert.ToString(rd["EmployeeName"])?.Trim() ?? string.Empty,
                Title = Convert.ToString(rd["Title"])?.Trim() ?? string.Empty
            });
        }

        if (rows.Count > 0) return new(rows, "Notification email sent to inventory controller.", "success");
        if (userCount == 0) return new(rows, "There is no inventory controller user in this department.", "warning");
        return new(rows, "There are inventory controller users in this department, but none has an email address.", "warning");
    }

    private List<ItemCheckingNotifyRecipient> GetInventoryControlRecipients(int deptId)
    {
        var rows = new List<ItemCheckingNotifyRecipient>();
        using var conn = OpenConnection();
        using var cmd = new SqlCommand(@"SELECT DISTINCT LTRIM(RTRIM(TheEmail)) AS Email, LTRIM(RTRIM(EmployeeCode)) AS EmployeeCode, LTRIM(RTRIM(EmployeeName)) AS EmployeeName, LTRIM(RTRIM(ISNULL(Title, ''))) AS Title
FROM dbo.MS_Employee
WHERE DeptID=@DeptID AND ISNULL(IsInventoryControlInDep,0)=1 AND ISNULL(IsActive,0)=1 AND ISNULL(LTRIM(RTRIM(TheEmail)), '') <> '' AND EmployeeCode NOT LIKE '%X'", conn);
        cmd.Parameters.Add("@DeptID", SqlDbType.Int).Value = deptId;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(new ItemCheckingNotifyRecipient
            {
                Email = Convert.ToString(rd["Email"])?.Trim() ?? string.Empty,
                EmployeeCode = Convert.ToString(rd["EmployeeCode"])?.Trim() ?? string.Empty,
                EmployeeName = Convert.ToString(rd["EmployeeName"])?.Trim() ?? string.Empty,
                Title = Convert.ToString(rd["Title"])?.Trim() ?? string.Empty
            });
        }
        return rows;
    }
    private string GetDepartmentName(int deptId)
    {
        using var conn = OpenConnection();
        using var cmd = new SqlCommand("SELECT TOP 1 DeptName FROM dbo.MS_Department WHERE DeptID=@DeptID", conn);
        cmd.Parameters.Add("@DeptID", SqlDbType.Int).Value = deptId;
        var obj = cmd.ExecuteScalar();
        var name = obj == null || obj == DBNull.Value ? string.Empty : Convert.ToString(obj)?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(name) ? deptId.ToString() : name;
    }
    private async Task SendNotifyEmailAsync(ItemCheckingNotifyRequest request)
    {
        if (request.SendIndividually && request.RecipientDetails.Count > 0)
        {
            foreach (var recipient in request.RecipientDetails)
            {
                using var mail = new MailMessage
                {
                    From = new MailAddress(request.SenderEmail, "SmartSam System"),
                    Subject = request.Subject,
                    Body = request.HtmlBody.Replace("{RECIPIENT_LABEL}", WebUtility.HtmlEncode(BuildRecipientDisplayName(recipient))),
                    IsBodyHtml = true
                };
                mail.To.Add(recipient.Email);
                mail.CC.Add(NotifyCcEmail);
                using var smtp = new SmtpClient(request.MailServer, request.MailPort)
                {
                    EnableSsl = true,
                    Credentials = new NetworkCredential(request.SenderEmail, request.Password)
                };
                await smtp.SendMailAsync(mail);
            }
        }
    }
    private string ApplyMailSubjectPrefix(string subject)
    {
        var prefix = _config.GetValue<string>("EmailSettings:PrefixSubject")?.Trim();
        if (string.IsNullOrWhiteSpace(prefix) || !ShouldApplyTestSubjectPrefix()) return subject;
        return $"{prefix} - {subject}";
    }
    private bool ShouldApplyTestSubjectPrefix()
    {
        var configuredIds = _config.GetValue<string>("EmailSettings:TestFunctionIDs");
        if (string.IsNullOrWhiteSpace(configuredIds)) return false;
        return configuredIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(x => int.TryParse(x, out var id) && id == FunctionId);
    }
    private static string WrapNotifyMessageBody(string messageBody) => $"<div style='font-family:{NotifyFontFamily};'>{messageBody}</div>";
    private static string BuildRecipientDisplayName(ItemCheckingNotifyRecipient recipient)
    {
        var title = (recipient.Title ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(title))
        {
            var titledEmployeeName = string.IsNullOrWhiteSpace(recipient.EmployeeName) ? recipient.Email : recipient.EmployeeName;
            return string.IsNullOrWhiteSpace(recipient.EmployeeCode)
                ? $"{title} {titledEmployeeName}"
                : $"{title} {titledEmployeeName}({recipient.EmployeeCode})";
        }

        var employeeName = string.IsNullOrWhiteSpace(recipient.EmployeeName) ? recipient.Email : recipient.EmployeeName;
        return string.IsNullOrWhiteSpace(recipient.EmployeeCode)
            ? employeeName
            : $"{employeeName} ({recipient.EmployeeCode})";
    }
    private IActionResult RedirectNoVoucherAccess()
    {
        TempData["AlertMessage"] = "You do not have permission to access this checking voucher.";
        TempData["AlertType"] = "warning";
        return RedirectToPage("./Index");
    }
    private bool CanAccessCheckingVoucher(int checkingId)
    {
        if (checkingId <= 0 || IsAdminRole()) return true;
        using var conn = OpenConnection();
        using var cmd = new SqlCommand("SELECT TOP 1 DeptChecked FROM dbo.INV_RecevingChekingVoucher WHERE CheckingID=@CheckingID", conn);
        cmd.Parameters.Add("@CheckingID", SqlDbType.Int).Value = checkingId;
        var result = cmd.ExecuteScalar();
        var deptChecked = result == null || result == DBNull.Value ? (int?)null : Convert.ToInt32(result);
        return CanAccessVoucherDepartment(deptChecked);
    }
    private bool CanAccessVoucherDepartment(int? deptChecked)
    {
        if (IsAdminRole()) return true;
        var scope = LoadCurrentEmployeeScope();
        if (scope.StoreGroupId == 1) return true;
        return scope.DeptId.HasValue && deptChecked.HasValue && scope.DeptId.Value == deptChecked.Value;
    }
    private ItemCheckingEmployeeScope LoadCurrentEmployeeScope()
    {
        var scope = new ItemCheckingEmployeeScope();
        var employeeCode = User.Identity?.Name;
        if (string.IsNullOrWhiteSpace(employeeCode)) return scope;
        using var conn = OpenConnection();
        using var cmd = new SqlCommand("SELECT TOP 1 DeptID, StoreGR, ISNULL(IsStoreKeeper,0) AS IsStoreKeeper, ISNULL(HeadDept,0) AS HeadDept FROM dbo.MS_Employee WHERE EmployeeCode=@EmployeeCode", conn);
        cmd.Parameters.Add("@EmployeeCode", SqlDbType.VarChar, 10).Value = employeeCode.Trim();
        using var rd = cmd.ExecuteReader();
        if (rd.Read())
        {
            scope.DeptId = rd.IsDBNull(0) ? null : Convert.ToInt32(rd[0]);
            scope.StoreGroupId = rd.IsDBNull(1) ? null : Convert.ToInt32(rd[1]);
            scope.IsStoreKeeper = !rd.IsDBNull(2) && Convert.ToBoolean(rd[2]);
            scope.IsHeadDept = !rd.IsDBNull(3) && Convert.ToInt32(rd[3]) == 1;
        }
        return scope;
    }
    private SqlConnection OpenConnection() { var c = new SqlConnection(_config.GetConnectionString("DefaultConnection")); c.Open(); return c; }
    private PagePermissions GetUserPermissions() => IsAdminRole() ? new PagePermissions { AllowedNos = Enumerable.Range(1, 20).ToList() } : new PagePermissions { AllowedNos = _permissionService.GetPermissionsForPage(GetCurrentRoleId(), FunctionId) };
    private int GetCurrentRoleId() => int.TryParse(User.FindFirst("RoleID")?.Value, out var roleId) ? roleId : 0;
    private bool IsAdminRole() => User.FindFirst("IsAdminRole")?.Value == "True";
}

public class ItemCheckingEmployeeScope
{
    public int? DeptId { get; set; }
    public int? StoreGroupId { get; set; }
    public bool IsStoreKeeper { get; set; }
    public bool IsHeadDept { get; set; }
}

public class ItemCheckingHeaderVm
{
    public int CheckingId { get; set; }
    public DateTime CreatedDate { get; set; }
    public int? CreatedBy { get; set; }
    public int? POID { get; set; }
    public DateTime ExpectDate { get; set; }
    public int? DeptChecked { get; set; }
    public int StatusId { get; set; } = 1;
    public string MRInfor { get; set; } = string.Empty;
    public string CheckingMethod { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public int? NotifyCheckedBy { get; set; }
    public int? CheckedBy { get; set; }
    public DateTime? CheckedDate { get; set; }
    public int? ApprovedBy { get; set; }
    public DateTime? ApprovedDate { get; set; }
}

public class ItemCheckingDetailRowVm
{
    public int ChekingDTID { get; set; }
    public int ItemID { get; set; }
    public string ItemName { get; set; } = string.Empty;
    public decimal QuantityCheck { get; set; }
    public decimal QuantityPassed { get; set; }
    public decimal Price { get; set; }
    public decimal Amount { get; set; }
    public string Notes { get; set; } = string.Empty;
    public int? MRDetailID { get; set; }
}

public class SaveSelectedItemsRequest
{
    public int Poid { get; set; }
    public int? DeptChecked { get; set; }
    public DateTime? ExpectDate { get; set; }
    public string? MRInfor { get; set; }
    public string? CheckingMethod { get; set; }
    public string? Result { get; set; }
    public List<SaveSelectedItemVm> SelectedItems { get; set; } = new();
}
public class SaveSelectedItemVm
{
    public int ItemId { get; set; }
    public string ItemText { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal QuantityPassed { get; set; }
    public decimal Price { get; set; }
    public string Note { get; set; } = string.Empty;
    public int MrDetailId { get; set; }
}

public class ItemCheckingNotifyRequest
{
    public string SenderEmail { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MailServer { get; set; } = string.Empty;
    public int MailPort { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
    public List<ItemCheckingNotifyRecipient> RecipientDetails { get; set; } = new();
    public string DefaultRecipientLabel { get; set; } = string.Empty;
    public bool SendIndividually { get; set; }
}

public class ItemCheckingNotifyRecipient
{
    public string Email { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}

public record ItemCheckingNotifyOutcome(bool Success, string Message, string AlertType);

public record ItemCheckingNotifyResolveResult(List<ItemCheckingNotifyRecipient> Recipients, string Message, string AlertType);

