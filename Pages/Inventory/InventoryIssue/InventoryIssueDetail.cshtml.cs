using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Helpers;
using SmartSam.Services;

namespace SmartSam.Pages.Inventory.InventoryIssue;

public class InventoryIssueDetailModel : BasePageModel
{
    private const int FunctionId = 66;
    private const int PermissionViewList = 1;
    private const int PermissionViewDetail = 2;
    private const int PermissionAdd = 3;
    private const int PermissionEdit = 4;
    // private const string NotifyCcEmail = "maiquangvinhi4@gmail.com";
    // private const string ReturnMoveToCpnNotifyEmail = "maiquangvinhi4@gmail.com";
    private const string NotifyCcEmail = "hai.dq@saigonskygarden.com.vn";
    private const string ReturnMoveToCpnNotifyEmail = "hai.dq@saigonskygarden.com.vn";
    private readonly PermissionService _permissionService;

    public InventoryIssueDetailModel(IConfiguration config, PermissionService permissionService) : base(config) { _permissionService = permissionService; }

    public PagePermissions PagePerm { get; private set; } = new();
    [BindProperty(SupportsGet = true)] public long? Id { get; set; }
    [BindProperty(SupportsGet = true)] public string Mode { get; set; } = "view";
    [BindProperty] public InventoryIssueHeader Header { get; set; } = new();
    [BindProperty] public string DetailsJson { get; set; } = "[]";
    [BindProperty] public long? SelectedIssueFlowId { get; set; }
    [BindProperty] public string DeletedAttachmentDocIdsJson { get; set; } = "[]";
    [BindProperty] public List<IFormFile> AttachmentUploads { get; set; } = new();
    [BindProperty] public bool ConfirmAfterSave { get; set; }
    [BindProperty] public bool ApplyAllApartment { get; set; }
    public bool HasAdjustedApartmentItems { get; set; }
    public string? Message { get; set; }
    public string MessageType { get; set; } = "info";
    public string AllowedAttachmentExtensionsText => _config.GetValue<string>("FileUploads:AllowedExtensions") ?? ".doc,.docx,.xls,.xlsx,.pdf,.jpg,.jpeg,.png";
    public int MaxAttachmentSizeMb => _config.GetValue<int?>("FileUploads:MaxFileSizeMb") ?? 10;
    public string CurrentEmployeeCode => GetCurrentEmployeeCode();
    public int CurrentKpGroupIdValue => GetCurrentKpGroupId();
    public bool IsIssueHouseLocked { get; private set; }

    public List<SelectListItem> IssueTypes { get; set; } = new();
    public List<SelectListItem> Stores { get; set; } = new();
    public List<SelectListItem> Departments { get; set; } = new();
    public List<SelectListItem> MrDepartments { get; set; } = new();
    public List<SelectListItem> ReceiverBys { get; set; } = new();
    public List<SelectListItem> Locations { get; set; } = new();
    public List<SelectListItem> Statuses { get; set; } = new();
    public List<SelectListItem> ItemOptions { get; set; } = new();
    public List<SelectListItem> MoveToCPNStores { get; set; } = new();
    public List<InventoryIssueItemOption> ItemCatalog { get; set; } = new();
    public List<InventoryIssueDetailRow> Details { get; set; } = new();
    public List<InventoryIssueAttachmentViewModel> AttachmentList { get; set; } = new();

    public bool IsViewMode => string.Equals(Mode, "view", StringComparison.OrdinalIgnoreCase);
    public bool IsEditMode => string.Equals(Mode, "edit", StringComparison.OrdinalIgnoreCase);
    public bool IsApprovedMode => string.Equals(Mode, "approved", StringComparison.OrdinalIgnoreCase);
    public bool IsAddMode => string.Equals(Mode, "add", StringComparison.OrdinalIgnoreCase);
    public bool CanSave => IsAddMode ? PagePerm.HasPermission(PermissionAdd) : IsEditMode && PagePerm.HasPermission(PermissionEdit);
    public bool CanEditVoucherBusiness => EvaluateCanEditVoucherBusiness();
    public bool CanConfirmVoucherBusiness => EvaluateCanConfirmVoucherBusiness();

    public IActionResult OnGet(long? id, string mode = "view", string? message = null, string? messageType = null)
    {
        PagePerm = GetUserPermissions();
        Mode = string.IsNullOrWhiteSpace(mode) ? "view" : mode.Trim().ToLowerInvariant();
        Message = !string.IsNullOrWhiteSpace(message)
            ? message
            : (TempData["Message"] as string);
        MessageType = !string.IsNullOrWhiteSpace(messageType)
            ? messageType
            : ((TempData["MessageType"] as string) ?? "info");
        if (id.HasValue && IsViewMode && !PagePerm.HasPermission(PermissionViewDetail)) return Redirect("/");
        if (!id.HasValue && !PagePerm.HasPermission(PermissionAdd)) return Redirect("/");
        if (IsApprovedMode && !HasIssueApprovalAccess()) return Redirect("/");
        if (IsEditMode && !PagePerm.HasPermission(PermissionEdit)) return Redirect("/");

        if (id.HasValue)
        {
            Id = id;
            LoadHeader(id.Value);
            LoadLookups();
            LoadDetails(id.Value);
            AttachmentList = LoadAttachmentRows(id.Value);
            DetailsJson = JsonSerializer.Serialize(Details);
        }
        else
        {
            LoadLookups();
            Header.FlowDate = DateTime.Today;
            Header.StatusID = 1;
            Header.FlowNo = Header.FlowSubType.HasValue && Header.FlowSubType.Value > 0
                ? GetNextVoucherNo(Header.FlowSubType)
                : string.Empty;
            Header.StatusName = GetStatusName(Header.StatusID, Header.FlowSubType);
            DetailsJson = "[]";
        }
        return Page();
    }

    public IActionResult OnPostSave(long? id, string mode)
    {
        PagePerm = GetUserPermissions();
        Mode = string.IsNullOrWhiteSpace(mode) ? "view" : mode.Trim().ToLowerInvariant();
        var isAdjustedItemLocked = id.HasValue && IsApartmentAdjustCreated(id.Value);
        if ((id.HasValue && !PagePerm.HasPermission(PermissionEdit)) || (!id.HasValue && !PagePerm.HasPermission(PermissionAdd))) return Redirect("/");
        if (IsApprovedMode) return Redirect("/");

        if (id.HasValue && !EvaluateCanEditVoucherBusiness())
        {
            TempData["Message"] = "This voucher can no longer be edited by business rule.";
            TempData["MessageType"] = "warning";
            return RedirectToPage(new { id, mode = "edit", message = "This voucher can no longer be edited by business rule.", messageType = "warning" });
        }

        if (isAdjustedItemLocked)
        {
            var existingHeader = GetFlowHeader(id.Value);
            if (!Header.FlowSubType.HasValue || Header.FlowSubType.Value <= 0)
            {
                Header.FlowSubType = existingHeader?.FlowSubType;
            }
            if (existingHeader?.FlowSubType != Header.FlowSubType)
            {
                TempData["Message"] = "Apartment adjust data has already been created. Issue Type can no longer be changed for this voucher.";
                TempData["MessageType"] = "warning";
                return RedirectToPage(new { id, mode = "edit" });
            }
        }

        LoadLookups();
        if (isAdjustedItemLocked && id.HasValue)
        {
            LoadDetails(id.Value);
        }
        else
        {
            Details = ParseDetails(DetailsJson);
        }
        Header.StatusID = 1;
        if (!id.HasValue)
        {
            Header.FlowNo = Header.FlowSubType.HasValue && Header.FlowSubType.Value > 0
                ? GetNextVoucherNo(Header.FlowSubType)
                : string.Empty;
        }
        else if (!Header.FlowSubType.HasValue || Header.FlowSubType.Value <= 0)
        {
            var existingHeaderForBind = GetFlowHeader(id.Value);
            if (existingHeaderForBind?.FlowSubType.HasValue == true)
            {
                Header.FlowSubType = existingHeaderForBind.FlowSubType;
            }
        }
        Header.StatusName = GetStatusName(Header.StatusID, Header.FlowSubType);
        var attachmentValidationMessages = AttachmentUploads
            .Where(file => file != null && file.Length > 0)
            .Select(file => ValidateAttachment(file))
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToList();
        foreach (var message in attachmentValidationMessages)
        {
            ModelState.AddModelError(string.Empty, message!);
        }
        ValidateBusinessRules(id);
        if (!ModelState.IsValid)
        {
            Message = string.Join(" ",
                ModelState
                    .SelectMany(kv => kv.Value.Errors)
                    .Select(e => e.ErrorMessage)
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Distinct());
            if (id.HasValue && id.Value > 0)
            {
                AttachmentList = LoadAttachmentRows(id.Value);
            }
            MessageType = "error";
            return Page();
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var tran = conn.BeginTransaction();
        var savedFilePaths = new List<string>();

        try
        {
            long flowId;
            var affectedRequestNos = new HashSet<long>();
            if (!id.HasValue)
            {
                using var cmd = new SqlCommand(@"INSERT INTO dbo.INV_ItemFlow(FlowType,KPGroup,OperatorID,FlowNo,FlowDate,FromStore,FlowSubType,RecDept,AssetLocation,ReceivedBy,According,Reason,StatusID)
VALUES(1,@KPGroup,@OperatorId,@FlowNo,@FlowDate,@FromStore,@FlowSubType,@RecDept,@AssetLocation,@ReceivedBy,@According,@Reason,@StatusID);
SELECT CAST(SCOPE_IDENTITY() AS bigint);", conn, tran);
                BindHeaderParams(cmd);
                flowId = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);

                using var actionCmd = new SqlCommand(@"INSERT INTO dbo.INV_ItemFlowAction(FlowID,UserID,TheDateTime,ActionTypeID,ComputerName,Des)
VALUES(@FlowID,@UserID,GETDATE(),1,@ComputerName,'Created Voucher')", conn, tran);
                actionCmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
                actionCmd.Parameters.Add("@UserID", SqlDbType.Int).Value = GetCurrentEmployeeId();
                actionCmd.Parameters.Add("@ComputerName", SqlDbType.NVarChar, 100).Value = Environment.MachineName;
                actionCmd.ExecuteNonQuery();
            }
            else
            {
                flowId = id.Value;

                using var cmd = new SqlCommand(@"UPDATE dbo.INV_ItemFlow
SET FlowDate=@FlowDate, FromStore=@FromStore, FlowSubType=@FlowSubType, RecDept=@RecDept, AssetLocation=@AssetLocation, ReceivedBy=@ReceivedBy, According=@According, Reason=@Reason, StatusID=@StatusID
WHERE FlowID=@FlowID", conn, tran);
                BindHeaderParams(cmd);
                cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
                cmd.ExecuteNonQuery();

                if (!isAdjustedItemLocked)
                {
                    var oldDetails = LoadDetailsForTransaction(conn, tran, flowId);
                    affectedRequestNos = GetRequestNosByMrDetails(conn, tran, oldDetails);
                    RollbackMrIssued(conn, tran, oldDetails);

                    using var del = new SqlCommand("DELETE FROM dbo.INV_ItemFlowDetail WHERE FlowID=@FlowID", conn, tran);
                    del.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
                    del.ExecuteNonQuery();
                }
            }

            if (!isAdjustedItemLocked)
            {
                foreach (var d in Details)
                {
                    InsertDetail(conn, tran, flowId, d);
                    ApplyMrIssued(conn, tran, d);
                    AddRequestNoByMrDetail(conn, tran, affectedRequestNos, d.MRDetailID);
                }
                SyncMaterialRequestStatuses(conn, tran, affectedRequestNos);

                if (!id.HasValue && SelectedIssueFlowId.HasValue && SelectedIssueFlowId.Value > 0)
                {
                    IssueFromInventoryIssue(conn, tran, flowId, SelectedIssueFlowId.Value);
                }

                UpdatePriceByDetails(conn, tran, Details);
            }

            foreach (var attachment in AttachmentUploads.Where(file => file != null && file.Length > 0))
            {
                SaveAttachment(conn, tran, flowId, GetCurrentEmployeeId(), Header.FlowNo, Header.FlowDate, attachment, savedFilePaths);
            }

            DeleteAttachmentsInTransaction(conn, tran, flowId, ParseDeletedAttachmentDocIds(DeletedAttachmentDocIdsJson));

            tran.Commit();

            if (ConfirmAfterSave)
            {
                return ExecuteConfirm(flowId, "edit");
            }

            if (!id.HasValue && Header.FlowSubType == 2)
            {
                return RedirectToPage(new
                {
                    id = flowId,
                    mode = "edit",
                    message = "Saved successfully. Please run Adjust Item in Apmt before confirm.",
                    messageType = "info"
                });
            }

            return RedirectToPage("./Index");
        }
        catch
        {
            tran.Rollback();
            RemoveSavedFiles(savedFilePaths);
            throw;
        }
    }

    public IActionResult OnPost(long? id, string mode)
    {
        return OnPostSave(id, mode);
    }

    public IActionResult OnPostSaveAndConfirm(long? id, string mode)
    {
        PagePerm = GetUserPermissions();
        if (!HasIssueApprovalAccess()) return RedirectNoConfirmAccess(id, mode);
        ConfirmAfterSave = true;
        return OnPostSave(id, mode);
    }

    public async Task<IActionResult> OnPostConfirm(long id, string mode)
    {
        PagePerm = GetUserPermissions();
        Mode = string.IsNullOrWhiteSpace(mode) ? "edit" : mode.Trim().ToLowerInvariant();
        if (!HasIssueApprovalAccess()) return RedirectNoConfirmAccess(id, Mode);
        if (!EvaluateCanConfirmVoucherBusiness(id))
        {
            TempData["Message"] = "You have no right to confirm this voucher at current status.";
            TempData["MessageType"] = "warning";
            return RedirectToPage("./Index");
        }

        return await ExecuteConfirmAsync(id, Mode);
    }

    public async Task<IActionResult> OnPostSign(long id, string mode)
    {
        PagePerm = GetUserPermissions();
        Mode = string.IsNullOrWhiteSpace(mode) ? "approved" : mode.Trim().ToLowerInvariant();
        if (!HasIssueApprovalAccess()) return RedirectNoConfirmAccess(id, Mode, "You have no right to sign this voucher.");
        if (!EvaluateCanConfirmVoucherBusiness(id))
        {
            TempData["Message"] = "You have no right to sign this voucher at current status.";
            TempData["MessageType"] = "warning";
            return RedirectToPage("./Index");
        }

        return await ExecuteConfirmAsync(id, Mode);
    }

    public IActionResult OnGetReport(long id, bool inline = false, bool previewSign = false)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewList) && !PagePerm.HasPermission(PermissionViewDetail) && !HasIssueApprovalAccess() && !PagePerm.HasPermission(PermissionEdit))
        {
            return RedirectToPage("./Index");
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        var report = LoadIssueReport(conn, id, previewSign);
        if (report == null)
        {
            TempData["Message"] = "Issue voucher is not found.";
            TempData["MessageType"] = "warning";
            return RedirectToPage("./Index");
        }

        var pdf = InventoryIssuePdfReport.BuildPdf(report);
        if (inline)
        {
            Response.Headers["Content-Disposition"] = "inline";
            return File(pdf, "application/pdf");
        }

        return File(pdf, "application/pdf", $"inventory_issue_{SanitizeFileName(report.FlowNo)}.pdf");
    }

    public IActionResult OnPostAdjustItemInApartment(long id, string mode)
    {
        PagePerm = GetUserPermissions();
        Mode = string.IsNullOrWhiteSpace(mode) ? "edit" : mode.Trim().ToLowerInvariant();
        if (!PagePerm.HasPermission(PermissionEdit) && !HasIssueApprovalAccess()) return Redirect("/");

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        var flow = LoadConfirmFlow(conn, id);
        if (flow == null)
        {
            TempData["Message"] = "Issue voucher not found.";
            TempData["MessageType"] = "warning";
            return RedirectToPage("./Index");
        }

        if (flow.FlowSubType != 2)
        {
            TempData["Message"] = "Cannot create adjust item data for this type of issue.";
            TempData["MessageType"] = "warning";
            return RedirectToPage(new { id, mode = "edit" });
        }

        if (IsAdjustItemAlreadyCreated(conn, id))
        {
            TempData["Message"] = "Adjust item data for apartment was created already.";
            TempData["MessageType"] = "warning";
            return RedirectToPage(new { id, mode = "edit" });
        }
        var postedDetails = ParseDetails(DetailsJson);
        if (postedDetails.Count > 0)
        {
            using var tran = conn.BeginTransaction();
            try
            {
                var oldDetails = LoadDetailsForTransaction(conn, tran, id);
                var affectedRequestNos = GetRequestNosByMrDetails(conn, tran, oldDetails);
                RollbackMrIssued(conn, tran, oldDetails);

                using (var del = new SqlCommand("DELETE FROM dbo.INV_ItemFlowDetail WHERE FlowID=@FlowID", conn, tran))
                {
                    del.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = id;
                    del.ExecuteNonQuery();
                }

                foreach (var d in postedDetails)
                {
                    InsertDetail(conn, tran, id, d);
                    ApplyMrIssued(conn, tran, d);
                    AddRequestNoByMrDetail(conn, tran, affectedRequestNos, d.MRDetailID);
                }

                SyncMaterialRequestStatuses(conn, tran, affectedRequestNos);
                UpdatePriceByDetails(conn, tran, postedDetails);
                tran.Commit();
            }
            catch
            {
                tran.Rollback();
                throw;
            }
        }


        var inserted = 0;
        if (ApplyAllApartment)
        {
            inserted += CreateItemAdjustForAllApartments(conn, id, flow.FlowNo);
        }

        inserted += CreateItemAdjustInApartment(conn, id, flow.FlowNo);
        if (inserted <= 0)
        {
            var hasAnyDetail = HasIssueDetailRows(conn, id);
            var hasPositiveQty = HasIssueDetailPositiveQty(conn, id);
            var hasApartmentItem = HasIssueDetailApartmentItem(conn, id);
            var hasMappableLocation = HasIssueDetailLocationMappedToApartment(conn, id);

            TempData["Message"] = !hasAnyDetail
                ? "No detail item found for this voucher. Please add item before adjusting apartment data."
                : !hasPositiveQty
                    ? "No item has actual quantity greater than zero. Please input Act. Qty before adjusting apartment data."
                    : !hasApartmentItem
                        ? "None of selected items is configured for apartment usage. Please choose apartment items before adjusting."
                        : !hasMappableLocation
                            ? "No item location can be mapped to an apartment. Please check item location before adjusting."
                            : (ApplyAllApartment
                                ? "Cannot apply to all apartments with current data. Please verify item, quantity and location."
                                : "No apartment-adjust item was created. Please verify item, quantity and location.");
            TempData["MessageType"] = "warning";
            return RedirectToPage(new { id, mode = "edit" });
        }

        MarkAdjustItemCreated(conn, id);
        SendAdjustApartmentNotification(conn, flow, ApplyAllApartment, inserted, GetCurrentEmployeeId());
        TempData["Message"] = $"Create adjust item successfully ({inserted} row(s)).";
        TempData["MessageType"] = "success";
        return RedirectToPage(new { id, mode = "edit" });
    }
    private IActionResult ExecuteConfirm(long id, string mode)
        => ExecuteConfirmAsync(id, mode).GetAwaiter().GetResult();

    private async Task<IActionResult> ExecuteConfirmAsync(long id, string mode)
    {
        Mode = string.IsNullOrWhiteSpace(mode) ? "edit" : mode.Trim().ToLowerInvariant();

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        var flow = LoadConfirmFlow(conn, id);
        if (flow == null) return RedirectToPage("./Index");

        if (flow.StatusID > 2)
        {
            return RedirectWithMessage(id, Mode, "This voucher cannot be confirmed at current status.", "warning");
        }

        var issueLevel = GetEmployeeIssueVoucherLevel(conn, GetCurrentEmployeeId());
        var isAdmin = IsCurrentEmployeeAdmin(conn, GetCurrentEmployeeId());
        var nextLevel = flow.StatusID + 1;
        if (!isAdmin && issueLevel != nextLevel)
        {
            return RedirectWithMessage(id, Mode, $"You have no right to confirm this voucher at status: {flow.StatusID}", "warning");
        }

        UpdateFlowStatus(conn, id, nextLevel);
        InsertFlowAction(conn, id, nextLevel, GetCurrentEmployeeId(), "Storeman Confirmed issue");

        string? mailWarning = null;
        if (nextLevel > 1)
        {
            if (flow.ReceivedBy.HasValue && flow.ReceivedBy.Value > 0)
            {
                mailWarning = NotifyReceiverByLegacyRule(conn, flow.ReceivedBy.Value, nextLevel + 1, flow.FlowNo, flow.FlowSubType != 1);
            }
            else
            {
                mailWarning = "Confirm successfully, but notification email was not sent because Receiver By is not selected.";
            }
        }

        TempData["Message"] = mailWarning ?? "Confirm successfully.";
        TempData["MessageType"] = mailWarning == null ? "success" : "warning";
        return RedirectToPage("./Index");
    }

    private ConfirmFlowInfo? LoadConfirmFlow(SqlConnection conn, long flowId)
    {
        using var cmd = new SqlCommand(@"SELECT FlowID, FlowNo, ISNULL(FlowSubType,0) AS FlowSubType, ISNULL(StatusID,1) AS StatusID, ISNULL(KPGroup,0) AS KPGroup, MoveToCPNStore, RetDept, ReceivedBy
FROM dbo.INV_ItemFlow
WHERE FlowID=@FlowID", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;
        return new ConfirmFlowInfo
        {
            FlowID = Convert.ToInt64(rd["FlowID"]),
            FlowNo = Convert.ToString(rd["FlowNo"]) ?? string.Empty,
            FlowSubType = Convert.ToInt32(rd["FlowSubType"]),
            StatusID = Convert.ToInt32(rd["StatusID"]),
            KPGroup = Convert.ToInt32(rd["KPGroup"]),
            MoveToCPNStore = rd["MoveToCPNStore"] == DBNull.Value ? null : Convert.ToInt32(rd["MoveToCPNStore"]),
            ReceivedBy = rd["ReceivedBy"] == DBNull.Value ? null : Convert.ToInt32(rd["ReceivedBy"])
        };
    }

    private int GetEmployeeIssueVoucherLevel(SqlConnection conn, int employeeId)
    {
        using var cmd = new SqlCommand("SELECT ISNULL(IssueVoucher,0) FROM dbo.MS_Employee WHERE EmployeeID=@EmployeeID", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private bool IsCurrentEmployeeAdmin(SqlConnection conn, int employeeId)
    {
        using var cmd = new SqlCommand("SELECT ISNULL(IsAdminUser,0) FROM dbo.MS_Employee WHERE EmployeeID=@EmployeeID", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) == 1;
    }

    private IActionResult RedirectWithMessage(long id, string mode, string message, string type)
    {
        TempData["Message"] = message;
        TempData["MessageType"] = type;
        return RedirectToPage("./Index");
    }

    private IActionResult RedirectNoConfirmAccess(long? id, string? mode, string message = "You have no right to confirm this voucher.")
    {
        TempData["Message"] = message;
        TempData["MessageType"] = "warning";
        if (id.HasValue && id.Value > 0)
        {
            return RedirectToPage(new { id = id.Value, mode = string.IsNullOrWhiteSpace(mode) ? "edit" : mode });
        }

        return RedirectToPage("./Index");
    }

    private void UpdateFlowStatus(SqlConnection conn, long flowId, int statusId)
    {
        using var cmd = new SqlCommand("UPDATE dbo.INV_ItemFlow SET StatusID=@StatusID WHERE FlowID=@FlowID", conn);
        cmd.Parameters.Add("@StatusID", SqlDbType.Int).Value = statusId;
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        cmd.ExecuteNonQuery();
    }

    private void InsertFlowAction(SqlConnection conn, long flowId, int actionTypeId, int userId, string? des)
    {
        using var cmd = new SqlCommand(@"INSERT INTO dbo.INV_ItemFlowAction(FlowID,UserID,TheDateTime,ActionTypeID,ComputerName,Des)
VALUES(@FlowID,@UserID,GETDATE(),@ActionTypeID,@ComputerName,@Des)", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        cmd.Parameters.Add("@UserID", SqlDbType.Int).Value = userId;
        cmd.Parameters.Add("@ActionTypeID", SqlDbType.Int).Value = actionTypeId;
        cmd.Parameters.Add("@ComputerName", SqlDbType.NVarChar, 128).Value = Environment.MachineName;
        cmd.Parameters.Add("@Des", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(des) ? DBNull.Value : des;
        cmd.ExecuteNonQuery();
    }


    private string? NotifyReceiverByLegacyRule(SqlConnection conn, int receivedByEmployeeId, int level, string flowNo, bool includeApprovalLink = true)
    {
        using var cmd = new SqlCommand(@"SELECT TOP 1 ISNULL(TheEmail,'') AS TheEmail,
       ISNULL(EmployeeCode,'') AS EmployeeCode,
       ISNULL(EmployeeName,'') AS EmployeeName,
       ISNULL(Title,'') AS Title,
       ISNULL(IssueVoucher,0) AS IssueVoucher
FROM dbo.MS_Employee
WHERE EmployeeID = @ReceivedByEmployeeID", conn);
        cmd.Parameters.Add("@ReceivedByEmployeeID", SqlDbType.Int).Value = receivedByEmployeeId;
        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
        {
            return $"Confirm successfully, but notification email was not sent because selected Receiver By (EmployeeID {receivedByEmployeeId}) was not found.";
        }

        var email = Convert.ToString(rd["TheEmail"]) ?? string.Empty;
        var code = Convert.ToString(rd["EmployeeCode"]) ?? string.Empty;
        var name = Convert.ToString(rd["EmployeeName"]) ?? string.Empty;
        var title = Convert.ToString(rd["Title"]) ?? string.Empty;
        var issueVoucherLevel = Convert.ToInt32(rd["IssueVoucher"] == DBNull.Value ? 0 : rd["IssueVoucher"]);

        if (issueVoucherLevel != level)
        {
            var receiverName = string.IsNullOrWhiteSpace(name) ? $"EmployeeID {receivedByEmployeeId}" : $"{name} ({code})";
            return $"Confirm successfully, but notification email was not sent because selected Receiver By {receiverName} has IssueVoucher level {issueVoucherLevel}, expected {level}.";
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            var receiverName = string.IsNullOrWhiteSpace(name) ? $"EmployeeID {receivedByEmployeeId}" : $"{name} ({code})";
            return $"Confirm successfully, but notification email was not sent because selected Receiver By {receiverName} has no TheEmail.";
        }

        var recipientLabel = BuildRecipientDisplayName(title, name, code, email);
        var statusName = GetStatusName(level - 1, 1);
        var subject = includeApprovalLink
            ? ApplyMailSubjectPrefix($"[Inventory Item Checking] Please approve voucher {flowNo}")
            : ApplyMailSubjectPrefix($"[Inventory Item Checking] Issue voucher {flowNo} has been confirmed by storeman");
        QueueConfirmMail(email, subject, flowNo, "Issue Voucher", true, recipientLabel, code, statusName, includeApprovalLink);
        return null;
    }

    private void NotifyNextByIssueVoucherLevel(SqlConnection conn, int storeGr, int level, string flowNo)
    {
        using var cmd = new SqlCommand(@"SELECT TheEmail, EmployeeCode, EmployeeName, ISNULL(Title,'') AS Title FROM dbo.MS_Employee
WHERE ISNULL(TheEmail,'')<>'' AND StoreGR=@StoreGR AND IssueVoucher=@Level
ORDER BY ISNULL(IsAdminUser,0) ASC, EmployeeID ASC", conn);
        cmd.Parameters.Add("@StoreGR", SqlDbType.Int).Value = storeGr;
        cmd.Parameters.Add("@Level", SqlDbType.Int).Value = level;
        using var rd = cmd.ExecuteReader();
        var hasRecipient = false;
        while (rd.Read())
        {
            var email = Convert.ToString(rd["TheEmail"]) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(email)) continue;
            hasRecipient = true;
            var code = Convert.ToString(rd["EmployeeCode"]) ?? string.Empty;
            var name = Convert.ToString(rd["EmployeeName"]) ?? string.Empty;
            var title = Convert.ToString(rd["Title"]) ?? string.Empty;
            var recipientLabel = BuildRecipientDisplayName(title, name, code, email);
            var statusName = GetStatusName(level - 1, 1);
            QueueConfirmMail(email, ApplyMailSubjectPrefix($"[Inventory Item Checking] Please approve voucher {flowNo}"), flowNo, "Issue Voucher", true, recipientLabel, code, statusName);
        }
        if (!hasRecipient)
        {
            var fallbackStatusName = GetStatusName(level - 1, 1);
            QueueConfirmMail(NotifyCcEmail, ApplyMailSubjectPrefix($"[Inventory Item Checking] Please approve voucher {flowNo}"), flowNo, "Issue Voucher", false, statusName: fallbackStatusName);
        }
    }

    private void QueueConfirmMail(string toEmail, string subject, string voucherNo, string voucherType, bool addDefaultCc = true, string recipientLabel = "", string employeeCode = "", string statusName = "", bool includeApprovalLink = true)
    {
        var flowId = GetFlowIdByNo(voucherNo);
        var detailUrl = Url.Page("/Inventory/InventoryIssue/InventoryIssueDetail", values: new { id = flowId, mode = "approved" });
        var absoluteUrl = string.IsNullOrWhiteSpace(detailUrl) ? string.Empty : $"{Request.Scheme}://{Request.Host}{detailUrl}";
        var submittedBy = GetCurrentEmployeeDisplayName();

        _ = Task.Run(async () =>
        {
            try
            {
                await SendConfirmMailAsync(toEmail, subject, voucherNo, voucherType, absoluteUrl, submittedBy, addDefaultCc, recipientLabel, employeeCode, statusName, includeApprovalLink);
            }
            catch
            {
            }
        });
    }

    private async Task SendConfirmMailAsync(string toEmail, string subject, string voucherNo, string voucherType, string absoluteUrl, string submittedBy, bool addDefaultCc = true, string recipientLabel = "", string employeeCode = "", string statusName = "", bool includeApprovalLink = true)
    {
        var senderEmail = _config.GetValue<string>("EmailSettings:SenderEmail") ?? string.Empty;
        var password = _config.GetValue<string>("EmailSettings:Password") ?? string.Empty;
        var mailServer = _config.GetValue<string>("EmailSettings:MailServer") ?? string.Empty;
        var mailPort = _config.GetValue<int?>("EmailSettings:MailPort") ?? 0;
        if (string.IsNullOrWhiteSpace(senderEmail) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(mailServer) || mailPort <= 0 || string.IsNullOrWhiteSpace(toEmail))
            return;
        var recipientDisplay = string.IsNullOrWhiteSpace(recipientLabel)
            ? (string.IsNullOrWhiteSpace(employeeCode) ? toEmail : employeeCode)
            : recipientLabel;
        var introText = includeApprovalLink
            ? $"A {WebUtility.HtmlEncode(voucherType)} has been <b>submitted</b> and is waiting for your approval."
            : $"A {WebUtility.HtmlEncode(voucherType)} has been <b>confirmed by storeman</b>. Please create the related receive voucher from this issue voucher.";
        var actionHtml = includeApprovalLink
            ? $"<p><b>Click Here to Approve:</b> <a href='{WebUtility.HtmlEncode(absoluteUrl)}'>Open {WebUtility.HtmlEncode(voucherType)}</a></p>"
            : string.Empty;
        var bodyContent = $@"
<p>Dear {{RECIPIENT_LABEL}},</p>
<p>{introText}</p>
<ul>
    <li>Voucher Type: <b>{WebUtility.HtmlEncode(voucherType)}</b></li>
    <li>Voucher No: <b>{WebUtility.HtmlEncode(voucherNo)}</b></li>
    <li>Status: <b>{WebUtility.HtmlEncode(statusName)}</b></li>
    <li>Submitted by: <b>{WebUtility.HtmlEncode(submittedBy)}</b></li>
    <li>Submit time: <b>{DateTime.Now.ToString("MMM d, yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)}</b></li>
</ul>
{actionHtml}
<p>Best regards,<br/>SmartSam System</p>";
        bodyContent = WrapNotifyMessageBody(bodyContent);
        var mailHeaderTitle = voucherType.Contains("Return", StringComparison.OrdinalIgnoreCase)
            ? "APPROVE RETURN VOUCHER"
            : "APPROVE Inventory Issue";
        var body = EmailTemplateHelper.WrapInNotifyTemplate(mailHeaderTitle, "#17a2b8", DateTime.Now, bodyContent)
            .Replace("{RECIPIENT_LABEL}", WebUtility.HtmlEncode(recipientDisplay));
        using var mail = new MailMessage
        {
            From = new MailAddress(senderEmail, "SmartSam System"),
            Subject = subject,
            Body = body,
            IsBodyHtml = true
        };
        mail.To.Add(toEmail);
        if (addDefaultCc && !string.Equals(toEmail, NotifyCcEmail, StringComparison.OrdinalIgnoreCase))
        {
            mail.CC.Add(NotifyCcEmail);
        }
        using var smtp = new SmtpClient(mailServer, mailPort)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(senderEmail, password)
        };
        await smtp.SendMailAsync(mail);
    }

    private long GetFlowIdByNo(string flowNo)
    {
        if (string.IsNullOrWhiteSpace(flowNo)) return 0;
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand("SELECT TOP 1 FlowID FROM dbo.INV_ItemFlow WHERE FlowNo=@FlowNo ORDER BY FlowID DESC", conn);
        cmd.Parameters.Add("@FlowNo", SqlDbType.VarChar, 30).Value = flowNo.Trim();
        conn.Open();
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
    }

    private static string WrapNotifyMessageBody(string messageBody) => $"<div style='font-family:Segoe UI,Arial,sans-serif;'>{messageBody}</div>";

    private static string BuildRecipientDisplayName(string title, string employeeName, string employeeCode, string email)
    {
        var titleTrim = (title ?? string.Empty).Trim();
        var nameTrim = string.IsNullOrWhiteSpace(employeeName) ? email : employeeName.Trim();
        if (!string.IsNullOrWhiteSpace(titleTrim))
        {
            return string.IsNullOrWhiteSpace(employeeCode)
                ? $"{titleTrim} {nameTrim}"
                : $"{titleTrim} {nameTrim}({employeeCode})";
        }

        return string.IsNullOrWhiteSpace(employeeCode)
            ? nameTrim
            : $"{nameTrim}({employeeCode})";
    }

    private string GetCurrentEmployeeDisplayName()
    {
        var employeeId = GetCurrentEmployeeId();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand("SELECT TOP 1 ISNULL(Title,'') AS Title, ISNULL(EmployeeName,'') AS EmployeeName, ISNULL(EmployeeCode,'') AS EmployeeCode, ISNULL(TheEmail,'') AS TheEmail FROM dbo.MS_Employee WHERE EmployeeID=@EmployeeID", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId;
        conn.Open();
        using var rd = cmd.ExecuteReader();
        if (rd.Read())
        {
            var title = Convert.ToString(rd["Title"]) ?? string.Empty;
            var employeeName = Convert.ToString(rd["EmployeeName"]) ?? string.Empty;
            var employeeCode = Convert.ToString(rd["EmployeeCode"]) ?? string.Empty;
            var email = Convert.ToString(rd["TheEmail"]) ?? string.Empty;
            return BuildRecipientDisplayName(title, employeeName, employeeCode, email);
        }

        var fallbackCode = GetCurrentEmployeeCode();
        if (!string.IsNullOrWhiteSpace(fallbackCode)) return fallbackCode;
        var fallbackName = User?.Identity?.Name ?? string.Empty;
        return string.IsNullOrWhiteSpace(fallbackName) ? "System" : fallbackName;
    }

    private bool EvaluateCanEditVoucherBusiness()
    {
        if (IsAddMode) return PagePerm.HasPermission(PermissionAdd);
        if (!IsEditMode || !Id.HasValue || !PagePerm.HasPermission(PermissionEdit)) return false;
        if (Header.StatusID > 1) return false;
        return true;
    }

    public bool HasIssueApprovalAccess()
    {
        return PagePerm.HasPermission(PermissionViewList) && GetCurrentEmployeeIssueVoucherLevel() > 0;
    }

    private int GetCurrentEmployeeIssueVoucherLevel()
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        return GetEmployeeIssueVoucherLevel(conn, GetCurrentEmployeeId());
    }

    private bool EvaluateCanConfirmVoucherBusiness(long? flowId = null)
    {
        if (!HasIssueApprovalAccess()) return false;
        var targetId = flowId ?? Id;
        if (!targetId.HasValue || targetId.Value <= 0) return false;
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        var flow = LoadConfirmFlow(conn, targetId.Value);
        if (flow == null) return false;
        if (flow.StatusID > 2) return false;
        if (flow.FlowSubType == 1 && flow.StatusID >= 2) return false;

        var currentEmployeeId = GetCurrentEmployeeId();
        var isAdmin = IsCurrentEmployeeAdmin(conn, currentEmployeeId);
        if (isAdmin) return true;

        var nextLevel = flow.StatusID + 1;
        var issueLevel = GetEmployeeIssueVoucherLevel(conn, currentEmployeeId);
        if (issueLevel != nextLevel) return false;

        return true;
    }

    private bool IsAdjustItemAlreadyCreated(SqlConnection conn, long flowId)
    {
        using var cmd = new SqlCommand("SELECT ISNULL(CreatedItemAdjustInApmt,0) FROM dbo.INV_ItemFlow WHERE FlowID=@FlowID", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) == 1;
    }

    private bool IsApartmentAdjustCreated(long flowId)
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        return IsAdjustItemAlreadyCreated(conn, flowId);
    }

    private int CreateItemAdjustInApartment(SqlConnection conn, long flowId, string flowNo)
    {
        using var cmd = new SqlCommand(@"INSERT INTO dbo.AM_ItemAdjust(ApartmentNo, ItemID, AdjustDate, Location, Quantity, Notes)
SELECT a.ApartmentNo, dt.ItemID, CAST(GETDATE() AS date), 1, dt.Act_Qty, @FlowNo
FROM dbo.INV_ItemFlow i
INNER JOIN dbo.INV_ItemFlowDetail dt ON i.FlowID = dt.FlowID
INNER JOIN dbo.INV_ItemList it ON dt.ItemID = it.ItemID
INNER JOIN dbo.AM_Apmt a ON dt.LocationID = a.CoLocationID
WHERE dt.FlowID = @FlowID
  AND ISNULL(it.IsApartment,0) = 1
  AND ISNULL(dt.Act_Qty,0) > 0", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        cmd.Parameters.Add("@FlowNo", SqlDbType.VarChar, 30).Value = string.IsNullOrWhiteSpace(flowNo) ? $"FlowID:{flowId}" : flowNo;
        return cmd.ExecuteNonQuery();
    }

    private int CreateItemAdjustForAllApartments(SqlConnection conn, long flowId, string flowNo)
    {
        using var countCmd = new SqlCommand(@"SELECT COUNT(1)
FROM dbo.INV_ItemFlowDetail dt
INNER JOIN dbo.INV_ItemList it ON dt.ItemID = it.ItemID
WHERE dt.FlowID = @FlowID
  AND ISNULL(it.IsApartment,0) = 1
  AND ISNULL(dt.Act_Qty,0) > 0
  AND dt.LocationID IS NOT NULL", conn);
        countCmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        var rowCount = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);
        if (rowCount != 1) return 0;

        using var cmd = new SqlCommand(@"INSERT INTO dbo.AM_ItemAdjust(ApartmentNo, ItemID, AdjustDate, Location, Quantity, Notes)
SELECT a.ApartmentNo, src.ItemID, CAST(GETDATE() AS date), 1, src.Act_Qty, @FlowNo
FROM (
    SELECT TOP 1 dt.ItemID, dt.Act_Qty, dt.LocationID
    FROM dbo.INV_ItemFlowDetail dt
    INNER JOIN dbo.INV_ItemList it ON dt.ItemID = it.ItemID
    WHERE dt.FlowID = @FlowID
      AND ISNULL(it.IsApartment,0) = 1
      AND ISNULL(dt.Act_Qty,0) > 0
      AND dt.LocationID IS NOT NULL
) src
INNER JOIN dbo.AM_Apmt a ON ISNULL(a.CoLocationID,0) <> ISNULL(src.LocationID,0)
WHERE a.ExistFrom <= GETDATE() AND a.ExistTo >= GETDATE()", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        cmd.Parameters.Add("@FlowNo", SqlDbType.VarChar, 30).Value = string.IsNullOrWhiteSpace(flowNo) ? $"FlowID:{flowId}" : flowNo;
        return cmd.ExecuteNonQuery();
    }

    private bool HasIssueDetailRows(SqlConnection conn, long flowId)
    {
        using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.INV_ItemFlowDetail WHERE FlowID=@FlowID", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private bool HasIssueDetailPositiveQty(SqlConnection conn, long flowId)
    {
        using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.INV_ItemFlowDetail WHERE FlowID=@FlowID AND ISNULL(Act_Qty,0) > 0", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private bool HasIssueDetailApartmentItem(SqlConnection conn, long flowId)
    {
        using var cmd = new SqlCommand(@"SELECT COUNT(1)
FROM dbo.INV_ItemFlowDetail dt
INNER JOIN dbo.INV_ItemList it ON dt.ItemID = it.ItemID
WHERE dt.FlowID=@FlowID
  AND ISNULL(dt.Act_Qty,0) > 0
  AND ISNULL(it.IsApartment,0) = 1", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private bool HasIssueDetailLocationMappedToApartment(SqlConnection conn, long flowId)
    {
        using var cmd = new SqlCommand(@"SELECT COUNT(1)
FROM dbo.INV_ItemFlowDetail dt
INNER JOIN dbo.INV_ItemList it ON dt.ItemID = it.ItemID
INNER JOIN dbo.AM_Apmt a ON dt.LocationID = a.CoLocationID
WHERE dt.FlowID=@FlowID
  AND ISNULL(dt.Act_Qty,0) > 0
  AND ISNULL(it.IsApartment,0) = 1", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private void SendAdjustApartmentNotification(SqlConnection conn, ConfirmFlowInfo flow, bool applyAllApartment, int insertedRows, int adjustedByEmployeeId)
    {
        var toEmail = GetAdjustApartmentNotifyEmail(conn, flow.ReceivedBy);
        if (string.IsNullOrWhiteSpace(toEmail)) return;

        var mailContext = LoadAdjustApartmentMailContext(conn, flow.FlowID, adjustedByEmployeeId);

        var subject = ApplyMailSubjectPrefix("Please arrange item correctly to the location");
        var detailRowsHtml = string.Empty;
        if (mailContext.Items.Count > 0)
        {
            var rows = string.Join(string.Empty, mailContext.Items.Select((item, index) => $"<tr><td style='border:1px solid #dee2e6;padding:4px;text-align:center;'>{index + 1}</td><td style='border:1px solid #dee2e6;padding:4px;'>{WebUtility.HtmlEncode(item.ItemCode)}</td><td style='border:1px solid #dee2e6;padding:4px;font-family: &quot;TCVN3&quot;, &quot;.VnTime&quot;, &quot;.VnArial&quot;, &quot;.VnHelvetica&quot;, sans-serif !important;'>{WebUtility.HtmlEncode(item.ItemName)}</td><td style='border:1px solid #dee2e6;padding:4px;text-align:right;'>{item.ActQty:#,##0}</td><td style='border:1px solid #dee2e6;padding:4px;'>{WebUtility.HtmlEncode(item.LocationName)}</td></tr>"));
            detailRowsHtml = $@"
<table style='border-collapse:collapse;width:100%;margin-top:8px;'>
    <thead>
        <tr style='background:#f8f9fa;'>
            <th style='border:1px solid #dee2e6;padding:4px;text-align:left;'>No.</th>
            <th style='border:1px solid #dee2e6;padding:4px;text-align:left;'>Item Code</th>
            <th style='border:1px solid #dee2e6;padding:4px;text-align:left;'>Item Name</th>
            <th style='border:1px solid #dee2e6;padding:4px;text-align:right;'>Act. Qty</th>
            <th style='border:1px solid #dee2e6;padding:4px;text-align:left;'>Location</th>
        </tr>
    </thead>
    <tbody>{rows}</tbody>
</table>";
        }

        var bodyContent = $@"
<p>Inventory Issue <b>{WebUtility.HtmlEncode(flow.FlowNo)}</b> has created apartment adjust data.</p>
<ul>
    <li>Issue date: <b>{mailContext.FlowDateText}</b></li>
    <li>Issue house: <b>{WebUtility.HtmlEncode(mailContext.StoreName)}</b></li>
    <li>Adjusted by: <b>{WebUtility.HtmlEncode(mailContext.AdjustedByEmployeeName)} ({WebUtility.HtmlEncode(mailContext.AdjustedByEmployeeCode)})</b></li>
    <li>According To: <b>{WebUtility.HtmlEncode(mailContext.According)}</b></li>
    <li>Apply all apartment: <b>{(applyAllApartment ? "Yes" : "No")}</b></li>
    <li>Inserted rows: <b>{insertedRows}</b></li>
</ul>
<p>Please arrange the issued items to the correct apartment/location and review the following details:</p>
{detailRowsHtml}
<div style='height:16px;'></div>
<p style='text-align:left;margin:0;'>Best regards,</p>
<p style='text-align:left;margin-top:4px;'>SmartSam System</p>";
        var body = EmailTemplateHelper.WrapInNotifyTemplate("ADJUST ITEM IN APARTMENT", "#ffc107", DateTime.Now, WrapNotifyMessageBody(bodyContent));

        var senderEmail = _config.GetValue<string>("EmailSettings:SenderEmail") ?? string.Empty;
        var password = _config.GetValue<string>("EmailSettings:Password") ?? string.Empty;
        var mailServer = _config.GetValue<string>("EmailSettings:MailServer") ?? string.Empty;
        var mailPort = _config.GetValue<int?>("EmailSettings:MailPort") ?? 0;
        if (string.IsNullOrWhiteSpace(senderEmail) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(mailServer) || mailPort <= 0) return;

        var mailTo = toEmail;
        var mailCc = !string.Equals(toEmail, NotifyCcEmail, StringComparison.OrdinalIgnoreCase) ? NotifyCcEmail : string.Empty;
        var mailSubject = subject;
        var mailBody = body;

        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                using var mail = new MailMessage { From = new MailAddress(senderEmail, "SmartSam System"), Subject = mailSubject, Body = mailBody, IsBodyHtml = true };
                mail.To.Add(mailTo);
                if (!string.IsNullOrWhiteSpace(mailCc)) mail.CC.Add(mailCc);
                using var smtp = new SmtpClient(mailServer, mailPort) { EnableSsl = true, Credentials = new NetworkCredential(senderEmail, password) };
                smtp.Send(mail);
            }
            catch (Exception ex)
            {
                try
                {
                    var logDir = Path.Combine(Directory.GetCurrentDirectory(), "logs");
                    Directory.CreateDirectory(logDir);
                    var logPath = Path.Combine(logDir, "inventory-issue-mail.log");
                    var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] AdjustMail FAIL Flow={flow.FlowNo} To={mailTo} Error={ex.Message}{Environment.NewLine}";
                    System.IO.File.AppendAllText(logPath, line);
                }
                catch { }
            }
        });
    }

    private AdjustApartmentMailContext LoadAdjustApartmentMailContext(SqlConnection conn, long flowId, int adjustedByEmployeeId)
    {
        var result = new AdjustApartmentMailContext();
        using (var cmd = new SqlCommand(@"SELECT CONVERT(varchar(10), h.FlowDate, 103) AS FlowDateText, ISNULL(s.StoreName,'') AS StoreName,
ISNULL(h.According,'') AS According
FROM dbo.INV_ItemFlow h
LEFT JOIN dbo.INV_StoreList s ON s.StoreID = h.FromStore
WHERE h.FlowID=@FlowID", conn))
        {
            cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
            using var rd = cmd.ExecuteReader();
            if (rd.Read())
            {
                result.FlowDateText = Convert.ToString(rd["FlowDateText"]) ?? string.Empty;
                result.StoreName = Convert.ToString(rd["StoreName"]) ?? string.Empty;
                result.According = Convert.ToString(rd["According"]) ?? string.Empty;
            }
        }

        using (var cmd = new SqlCommand(@"SELECT ISNULL(EmployeeCode,'') AS EmployeeCode, ISNULL(EmployeeName,'') AS EmployeeName
FROM dbo.MS_Employee WHERE EmployeeID=@EmployeeID", conn))
        {
            cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = adjustedByEmployeeId;
            using var rd = cmd.ExecuteReader();
            if (rd.Read())
            {
                result.AdjustedByEmployeeCode = Convert.ToString(rd["EmployeeCode"]) ?? string.Empty;
                result.AdjustedByEmployeeName = Convert.ToString(rd["EmployeeName"]) ?? string.Empty;
            }
        }

        using (var cmd = new SqlCommand(@"SELECT ISNULL(i.ItemCode,'') AS ItemCode, ISNULL(i.ItemName,'') AS ItemName, ISNULL(dt.Act_Qty,0) AS ActQty, ISNULL(l.LocationName,'') AS LocationName
FROM dbo.INV_ItemFlowDetail dt
INNER JOIN dbo.INV_ItemList i ON i.ItemID = dt.ItemID
LEFT JOIN dbo.MS_CoLocation l ON l.LocationID = dt.LocationID
WHERE dt.FlowID=@FlowID
ORDER BY i.ItemCode", conn))
        {
            cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                result.Items.Add(new AdjustApartmentMailItem
                {
                    ItemCode = Convert.ToString(rd["ItemCode"]) ?? string.Empty,
                    ItemName = Convert.ToString(rd["ItemName"]) ?? string.Empty,
                    ActQty = Convert.ToDecimal(rd["ActQty"]),
                    LocationName = Convert.ToString(rd["LocationName"]) ?? string.Empty
                });
            }
        }

        return result;
    }

    private string GetAdjustApartmentNotifyEmail(SqlConnection conn, int? receivedByEmployeeId)
    {
        if (receivedByEmployeeId.HasValue && receivedByEmployeeId.Value > 0)
        {
            using var cmd = new SqlCommand("SELECT TOP 1 ISNULL(TheEmail,'') FROM dbo.MS_Employee WHERE EmployeeID=@EmployeeID", conn);
            cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = receivedByEmployeeId.Value;
            var email = Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(email)) return email;
        }

        return string.IsNullOrWhiteSpace(NotifyCcEmail) ? string.Empty : NotifyCcEmail;
    }
    private void MarkAdjustItemCreated(SqlConnection conn, long flowId)
    {
        using var cmd = new SqlCommand("UPDATE dbo.INV_ItemFlow SET CreatedItemAdjustInApmt = 1 WHERE FlowID=@FlowID", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        cmd.ExecuteNonQuery();
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
        return configuredIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(x => int.TryParse(x, out var parsed) && parsed == FunctionId);
    }

    public IActionResult OnGetDownloadAttachment(long id, int docId)
    {
        var fileName = GetAttachmentFileName(id, docId);
        if (string.IsNullOrWhiteSpace(fileName)) return NotFound();

        var flow = GetFlowHeader(id);
        if (flow == null) return NotFound();
        var fullPath = Path.Combine(ResolveAttachmentUploadFolder(flow.FlowNo, flow.FlowDate, "IssueVoucher"), fileName);
        if (!System.IO.File.Exists(fullPath)) return NotFound();

        return PhysicalFile(fullPath, "application/octet-stream", fileName);
    }

    public IActionResult OnPostDeleteAttachment(long id, int docId)
    {
        return RedirectToPage(new { id, mode = "edit" });
    }

    public JsonResult OnGetIssueContext(int? flowSubType)
    {
        if (!flowSubType.HasValue || flowSubType.Value <= 0)
        {
            return new JsonResult(new
            {
                statusId = 0,
                statusName = string.Empty,
                flowNo = string.Empty
            });
        }

        var statusId = GetDefaultStatusId(flowSubType);
        var statusName = GetStatusName(statusId, flowSubType);
        var flowNo = GetNextVoucherNo(flowSubType);
        return new JsonResult(new
        {
            statusId,
            statusName,
            flowNo
        });
    }

    public JsonResult OnGetReceiverOptions(int? deptId)
    {
        var rows = LoadReceiverOptions(deptId)
            .Select(x => new
            {
                value = x.Value,
                text = x.Text
            })
            .ToList();

        return new JsonResult(rows);
    }


    public JsonResult OnGetMrDepartments()
    {
        var rows = LoadListFromSql("SELECT KPGroupID, KPGroupName FROM dbo.INV_KPGroup WHERE KPGroupID > 1 AND KPGroupID < 15 ORDER BY KPGroupName", "KPGroupID", "KPGroupName")
            .Where(x => !string.IsNullOrWhiteSpace(x.Value))
            .Select(x => new { value = x.Value, text = x.Text })
            .ToList();

        return new JsonResult(rows);
    }

    public JsonResult OnGetMrList(string? requestNo, string? itemCode, string? according, int? deptId, string? fromDate, string? toDate, int? fromStore)
    {
        var from = DateTime.TryParse(fromDate, out var fd) ? fd.Date : DateTime.Today.AddDays(-60);
        var to = DateTime.TryParse(toDate, out var td) ? td.Date : DateTime.Today;
        var rows = new List<object>();
        var rawRows = new List<(long RequestNo, DateTime DateCreate, string According, string DeptName, string MaterialStatusName)>();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand(@"SELECT TOP 200 m.REQUEST_NO, m.DATE_CREATE, ISNULL(m.ACCORDINGTO,'') AS ACCORDINGTO,
       ISNULL(k.KPGroupName,'') AS DeptName,
       ISNULL(s.MaterialStatusName,'') AS MaterialStatusName
FROM dbo.MATERIAL_REQUEST m
INNER JOIN dbo.INV_KPGroup k ON m.STORE_GROUP = k.KPGroupID
INNER JOIN dbo.MaterialStatus s ON m.MATERIALSTATUSID = s.MaterialStatusID
WHERE ((m.MATERIALSTATUSID >= 1) OR (m.MATERIALSTATUSID >= 1 AND ISNULL(m.IS_AUTO,0) = 1))
  AND m.MATERIALSTATUSID < 5
  AND m.DATE_CREATE >= @FromDate AND m.DATE_CREATE < DATEADD(day, 1, @ToDate)
  AND (@RequestNo IS NULL OR CAST(m.REQUEST_NO AS varchar(50)) LIKE '%' + @RequestNo + '%')
  AND (@According IS NULL OR ISNULL(m.ACCORDINGTO,'') LIKE '%' + @According + '%')
  AND (@DeptId IS NULL OR m.STORE_GROUP = @DeptId)
  AND EXISTS (SELECT 1 FROM dbo.MATERIAL_REQUEST_DETAIL d WHERE d.REQUEST_NO=m.REQUEST_NO AND ISNULL(d.NEW_ORDER,0) > ISNULL(d.ISSUED,0)
        AND (@ItemCode IS NULL OR ISNULL(d.ITEMCODE,'') LIKE '%' + @ItemCode + '%'))
ORDER BY m.DATE_CREATE DESC, m.REQUEST_NO DESC", conn);
        cmd.Parameters.Add("@FromDate", SqlDbType.Date).Value = from;
        cmd.Parameters.Add("@ToDate", SqlDbType.Date).Value = to;
        cmd.Parameters.Add("@RequestNo", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(requestNo) ? DBNull.Value : requestNo.Trim();
        cmd.Parameters.Add("@ItemCode", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(itemCode) ? DBNull.Value : itemCode.Trim();
        cmd.Parameters.Add("@According", SqlDbType.NVarChar, 255).Value = string.IsNullOrWhiteSpace(according) ? DBNull.Value : according.Trim();
        cmd.Parameters.Add("@DeptId", SqlDbType.Int).Value = deptId.HasValue && deptId.Value > 0 ? deptId.Value : DBNull.Value;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rawRows.Add((
                Convert.ToInt64(rd["REQUEST_NO"]),
                Convert.ToDateTime(rd["DATE_CREATE"]),
                Convert.ToString(rd["ACCORDINGTO"]) ?? string.Empty,
                Convert.ToString(rd["DeptName"]) ?? string.Empty,
                Convert.ToString(rd["MaterialStatusName"]) ?? string.Empty
            ));
        }
        rd.Dispose();

        foreach (var raw in rawRows)
        {
            rows.Add(new
            {
                requestNo = raw.RequestNo,
                dateCreate = raw.DateCreate.ToString("yyyy-MM-dd"),
                according = raw.According,
                deptName = raw.DeptName,
                materialStatusName = raw.MaterialStatusName,
                allItemsOutOfStock = fromStore.HasValue && fromStore.Value > 0 && AreAllMrItemsOutOfStock(conn, raw.RequestNo, fromStore.Value)
            });
        }
        return new JsonResult(rows);
    }

    public JsonResult OnGetMrDetails(long requestNo, int? fromStore)
    {
        var rows = new List<object>();
        var rawRows = new List<(long Id, long RequestNo, int ItemId, string ItemCode, string ItemName, bool MissingItemName, string Unit, decimal RemainQty)>();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand(@"SELECT m.REQUEST_NO,
       m.DATE_CREATE,
       d.ITEMCODE,
       i.ItemID,
       i.ItemName,
       ISNULL(d.UNIT,'') AS UNIT,
       ISNULL(d.NEW_ORDER,0)-ISNULL(d.ISSUED,0) AS REMAIN_QTY,
       d.ID,
       CASE WHEN i.ItemID IS NULL OR NULLIF(LTRIM(RTRIM(i.ItemName)),'') IS NULL THEN 1 ELSE 0 END AS MissingItemName
FROM dbo.MATERIAL_REQUEST m
INNER JOIN dbo.MATERIAL_REQUEST_DETAIL d ON m.REQUEST_NO = d.REQUEST_NO
INNER JOIN dbo.INV_KPGroup k ON m.STORE_GROUP = k.KPGroupID
INNER JOIN dbo.MaterialStatus s ON m.MATERIALSTATUSID = s.MaterialStatusID
LEFT JOIN dbo.INV_ItemList i ON LTRIM(RTRIM(d.ITEMCODE)) = LTRIM(RTRIM(i.ItemCode))
WHERE m.REQUEST_NO=@RequestNo
  AND ISNULL(d.NEW_ORDER,0) > ISNULL(d.ISSUED,0)
ORDER BY d.ID", conn);
        cmd.Parameters.Add("@RequestNo", SqlDbType.BigInt).Value = requestNo;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rawRows.Add((
                Convert.ToInt64(rd["ID"]),
                Convert.ToInt64(rd["REQUEST_NO"]),
                rd["ItemID"] == DBNull.Value ? 0 : Convert.ToInt32(rd["ItemID"]),
                Convert.ToString(rd["ITEMCODE"]) ?? string.Empty,
                Convert.ToString(rd["ItemName"]) ?? string.Empty,
                Convert.ToInt32(rd["MissingItemName"]) == 1,
                Convert.ToString(rd["UNIT"]) ?? string.Empty,
                Convert.ToDecimal(rd["REMAIN_QTY"])
            ));
        }
        rd.Dispose();

        foreach (var raw in rawRows)
        {
            rows.Add(new
            {
                id = raw.Id,
                requestNo = raw.RequestNo,
                itemId = raw.ItemId,
                itemCode = raw.ItemCode,
                itemName = raw.ItemName,
                missingItemName = raw.MissingItemName,
                unit = raw.Unit,
                remainQty = raw.RemainQty,
                stockQty = raw.ItemId > 0 && fromStore.HasValue && fromStore.Value > 0 ? CheckStore(conn, raw.ItemId, fromStore.Value) : (decimal?)null
            });
        }

        return new JsonResult(rows);
    }

    public JsonResult OnGetCheckStore(int itemId, int fromStore)
    {
        if (itemId <= 0 || fromStore <= 0)
        {
            return new JsonResult(new { ok = true, stockQty = 0m });
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        return new JsonResult(new { ok = true, stockQty = CheckStore(conn, itemId, fromStore) });
    }

    private List<InventoryIssueDetailRow> ParseDetails(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<InventoryIssueDetailRow>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new();
        }
        catch { return new(); }
    }

    private List<SelectListItem> LoadReceiverOptions(int? deptId)
    {
        var result = new List<SelectListItem>();
        if (!deptId.HasValue || deptId.Value <= 0) return result;
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand(@"SELECT DISTINCT e.EmployeeID,
       ISNULL(e.EmployeeName,'') AS EmployeeName,
       ISNULL(e.EmployeeCode,'') AS EmployeeCode
FROM dbo.MS_Employee e
WHERE ISNULL(e.IsActive,0)=1
  AND ISNULL(e.IssueVoucher,0)=3
  AND e.DeptID=@DeptID
ORDER BY EmployeeName", conn);
        cmd.Parameters.Add("@DeptID", SqlDbType.Int).Value = deptId.Value;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var employeeName = Convert.ToString(rd["EmployeeName"]) ?? string.Empty;
            var employeeCode = Convert.ToString(rd["EmployeeCode"]) ?? string.Empty;
            var displayText = string.IsNullOrWhiteSpace(employeeCode)
                ? employeeName
                : $"{employeeName} ({employeeCode})";

            result.Add(new SelectListItem
            {
                Value = Convert.ToString(rd["EmployeeID"]) ?? string.Empty,
                Text = displayText
            });
        }

        return result;
    }

    private void LoadLookups()
    {
        var isKpAdmin = IsCurrentIssueTypeKpAdmin();
        IssueTypes = LoadListFromSql($@"SELECT FlowSubTypeID, FlowSubTypeName
FROM dbo.INV_FlowSubType
WHERE FlowTypeID=1 AND {(isKpAdmin ? "ISNULL(isKPAdmin,0)=1" : "ISNULL(isKPOther,0)=1")}
ORDER BY FlowSubTypeID", "FlowSubTypeID", "FlowSubTypeName");
        Departments = LoadListFromSql("SELECT DeptID, DeptName FROM dbo.MS_Department ORDER BY DeptName", "DeptID", "DeptName");
        MrDepartments = LoadListFromSql("SELECT KPGroupID, KPGroupName FROM dbo.INV_KPGroup WHERE KPGroupID > 1 AND KPGroupID < 15 ORDER BY KPGroupName", "KPGroupID", "KPGroupName");
        Locations = LoadListFromSql("SELECT LocationID, LocationName FROM dbo.MS_CoLocation ORDER BY LocationName", "LocationID", "LocationName");
        LoadItemCatalog();
        ItemOptions = ItemCatalog.Select(x => new SelectListItem($"{x.ItemCode} - {x.ItemName}", x.ItemId.ToString())).ToList();
        Stores = BuildStoreTreeOptions();
        if (IsIssueHouseLocked && (!Header.FromStore.HasValue || Header.FromStore.Value <= 0))
        {
            var firstStore = Stores.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x.Value));
            if (firstStore != null && int.TryParse(firstStore.Value, out var storeId) && storeId > 0)
            {
                Header.FromStore = storeId;
            }
        }
        Statuses = LoadVoucherStatuses(Header.FlowSubType);
                Header.StatusName = GetStatusName(Header.StatusID, Header.FlowSubType);

        ReceiverBys = LoadReceiverOptions(Header.RecDept);

        IssueTypes.Insert(0, new SelectListItem("--- Select ---", ""));
        Departments.Insert(0, new SelectListItem("--- Select ---", ""));
        MrDepartments.Insert(0, new SelectListItem("--- Select ---", ""));
        ReceiverBys.Insert(0, new SelectListItem("--- Select ---", ""));
        Locations.Insert(0, new SelectListItem("--- Select ---", ""));
        if (Statuses.Count == 0)
        {
            Statuses.Add(new SelectListItem("--- Select ---", ""));
            Statuses.Add(new SelectListItem("1", "1"));
        }
        else
        {
            Statuses.Insert(0, new SelectListItem("--- Select ---", ""));
        }
        ItemOptions.Insert(0, new SelectListItem("--- Select Item ---", ""));
    }

    private bool IsCurrentIssueTypeKpAdmin()
    {
        return GetCurrentKpGroupId() == 1;
    }
    private List<SelectListItem> LoadVoucherStatuses(int? flowSubType)
    {
        return LoadListFromSql("SELECT StatusID, StatusName FROM dbo.INV_ItemFlowIssueStatus ORDER BY StatusID", "StatusID", "StatusName");
    }

    private void LoadItemCatalog()
    {
        ItemCatalog = LoadAllItems();
    }

    private List<InventoryIssueItemOption> LoadAllItems()
    {
        var results = new List<InventoryIssueItemOption>();
        var kpGroupId = GetCurrentKpGroupId();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand(@"SELECT a1.ItemID,
       COALESCE(NULLIF(LTRIM(RTRIM(a2.ItemCode)),''), NULLIF(LTRIM(RTRIM(a1.ItemCode)),'')) AS ItemCode,
       COALESCE(NULLIF(LTRIM(RTRIM(a2.ItemName)),''), NULLIF(LTRIM(RTRIM(a1.ItemName)),'')) AS ItemName,
       ISNULL(a1.Unit,'') AS Unit
FROM dbo.INV_ItemList a1
LEFT JOIN dbo.INV_KPGroupIndex a2 ON a1.ItemID = a2.ItemID AND a2.KPGroupID = @KPGroupID
WHERE ISNULL(a1.IsActive,0)=1
ORDER BY COALESCE(NULLIF(LTRIM(RTRIM(a2.ItemCode)),''), NULLIF(LTRIM(RTRIM(a1.ItemCode)),''))", conn);
        cmd.Parameters.Add("@KPGroupID", SqlDbType.Int).Value = kpGroupId;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            results.Add(new InventoryIssueItemOption
            {
                ItemId = Convert.ToInt32(rd["ItemID"]),
                ItemCode = Convert.ToString(rd["ItemCode"]) ?? string.Empty,
                ItemName = Convert.ToString(rd["ItemName"]) ?? string.Empty,
                Unit = Convert.ToString(rd["Unit"]) ?? string.Empty
            });
        }

        return results;
    }

    /* removed PO-based issue source per business rule */
    private List<SelectListItem> BuildStoreTreeOptions()
    {
        var results = new List<SelectListItem>();
        var currentKpGroupId = GetCurrentKpGroupId();
        var isKpAdmin = IsCurrentIssueTypeKpAdmin();
        IsIssueHouseLocked = !isKpAdmin && currentKpGroupId > 0;
        var isAdminUser = IsAdminRole();

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand(@"SELECT g.KPGroupID, g.KPGroupName, s.StoreID, s.StoreName
FROM dbo.INV_KPGroup g
INNER JOIN dbo.INV_StoreList s ON s.DeptID = g.KPGroupID
WHERE @IsAdminUser = 1 OR s.DeptID = @KPGroupID
ORDER BY g.KPGroupName, s.StoreName", conn);
        cmd.Parameters.Add("@IsAdminUser", SqlDbType.Bit).Value = isAdminUser;
        cmd.Parameters.Add("@KPGroupID", SqlDbType.Int).Value = currentKpGroupId > 0 ? currentKpGroupId : -1;
        using var rd = cmd.ExecuteReader();
        var groupMap = new Dictionary<int, SelectListGroup>();
        while (rd.Read())
        {
            var groupId = Convert.ToInt32(rd["KPGroupID"]);
            if (!groupMap.ContainsKey(groupId)) groupMap[groupId] = new SelectListGroup { Name = Convert.ToString(rd["KPGroupName"]) ?? "" };
            results.Add(new SelectListItem
            {
                Value = Convert.ToString(rd["StoreID"]),
                Text = Convert.ToString(rd["StoreName"]) ?? "",
                Group = groupMap[groupId]
            });
        }
        results.Insert(0, new SelectListItem("--- Select ---", ""));
        return results;
    }

    private void LoadHeader(long id)
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand("SELECT FlowID,FlowNo,FlowDate,FromStore,FlowSubType,RecDept,AssetLocation,ReceivedBy,According,Reason,StatusID,CreatedItemAdjustInApmt FROM dbo.INV_ItemFlow WHERE FlowID=@FlowID", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = id;
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return;
        Header = new InventoryIssueHeader
        {
            FlowID = Convert.ToInt64(rd["FlowID"]),
            FlowNo = Convert.ToString(rd["FlowNo"]) ?? string.Empty,
            FlowDate = Convert.ToDateTime(rd["FlowDate"]),
            FromStore = rd["FromStore"] == DBNull.Value ? null : Convert.ToInt32(rd["FromStore"]),
            FlowSubType = rd["FlowSubType"] == DBNull.Value ? null : Convert.ToInt32(rd["FlowSubType"]),
            RecDept = rd["RecDept"] == DBNull.Value ? null : Convert.ToInt32(rd["RecDept"]),
            AssetLocation = rd["AssetLocation"] == DBNull.Value ? null : Convert.ToInt32(rd["AssetLocation"]),
            ReceivedBy = rd["ReceivedBy"] == DBNull.Value ? null : Convert.ToInt32(rd["ReceivedBy"]),
            According = Convert.ToString(rd["According"]) ?? string.Empty,
            Reason = Convert.ToString(rd["Reason"]) ?? string.Empty,
            StatusID = rd["StatusID"] == DBNull.Value ? 1 : Convert.ToInt32(rd["StatusID"])
        };
        Header.StatusName = GetStatusName(Header.StatusID, Header.FlowSubType);
        HasAdjustedApartmentItems = Convert.ToInt32(rd["CreatedItemAdjustInApmt"] == DBNull.Value ? 0 : rd["CreatedItemAdjustInApmt"]) == 1;
    }

    private string GetStatusName(int statusId, int? flowSubType)
    {
        if (statusId <= 0) return string.Empty;
        var statuses = LoadVoucherStatuses(flowSubType);
        return statuses.FirstOrDefault(x => x.Value == statusId.ToString())?.Text ?? string.Empty;
    }

    private int GetDefaultStatusId(int? flowSubType)
    {
        return 1;
    }

    private void LoadDetails(long id)
    {
        Details.Clear();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand(@"SELECT d.ItemID,d.RequestNo,d.ChekingID,d.ChekingDTID,d.MRDetailID,i.ItemCode,i.ItemName,d.Unit,d.Doc_Qty,d.Act_Qty,d.UnitPrice,d.Amount,d.LocationID,l.LocationName
FROM dbo.INV_ItemFlowDetail d
INNER JOIN dbo.INV_ItemList i ON i.ItemID=d.ItemID
LEFT JOIN dbo.MS_CoLocation l ON l.LocationID=d.LocationID
WHERE d.FlowID=@FlowID
ORDER BY i.ItemCode", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = id;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            Details.Add(new InventoryIssueDetailRow
            {
                ItemId = Convert.ToInt32(rd["ItemID"]),
                RequestNo = rd["RequestNo"] == DBNull.Value ? null : Convert.ToInt64(rd["RequestNo"]),
                ChekingID = rd["ChekingID"] == DBNull.Value ? null : Convert.ToInt64(rd["ChekingID"]),
                ChekingDTID = rd["ChekingDTID"] == DBNull.Value ? null : Convert.ToInt64(rd["ChekingDTID"]),
                MRDetailID = rd["MRDetailID"] == DBNull.Value ? null : Convert.ToInt64(rd["MRDetailID"]),
                ItemCode = Convert.ToString(rd["ItemCode"]) ?? "",
                ItemName = Convert.ToString(rd["ItemName"]) ?? "",
                Unit = Convert.ToString(rd["Unit"]) ?? "",
                DocQty = Convert.ToDecimal(rd["Doc_Qty"] == DBNull.Value ? 0 : rd["Doc_Qty"]),
                ActQty = Convert.ToDecimal(rd["Act_Qty"] == DBNull.Value ? 0 : rd["Act_Qty"]),
                UnitPrice = Convert.ToDecimal(rd["UnitPrice"] == DBNull.Value ? 0 : rd["UnitPrice"]),
                Amount = Convert.ToDecimal(rd["Amount"] == DBNull.Value ? 0 : rd["Amount"]),
                LocationId = rd["LocationID"] == DBNull.Value ? null : Convert.ToInt32(rd["LocationID"]),
                LocationName = Convert.ToString(rd["LocationName"]) ?? ""
            });
        }
    }

    private bool LocationExists(int locationId)
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand("SELECT COUNT(1) FROM dbo.MS_CoLocation WHERE LocationID=@LocationID", conn);
        cmd.Parameters.Add("@LocationID", SqlDbType.Int).Value = locationId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
    }

    private void ValidateBusinessRules(long? flowId)
    {
        if (string.IsNullOrWhiteSpace(Header.FlowNo))
        {
            ModelState.AddModelError("Header.FlowNo", "Doc No. is required.");
        }

        if (!Header.FlowSubType.HasValue || Header.FlowSubType.Value <= 0)
        {
            ModelState.AddModelError("Header.FlowSubType", "Issue Type is required.");
        }

        if (!Header.FromStore.HasValue || Header.FromStore.Value <= 0)
        {
            ModelState.AddModelError("Header.FromStore", "Issue House is required.");
        }

        if (Header.AssetLocation.HasValue && Header.AssetLocation.Value > 0 && !LocationExists(Header.AssetLocation.Value))
        {
            ModelState.AddModelError("Header.AssetLocation", "Invalid Location.");
        }


        if (Details == null || Details.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Please add at least one detail item.");
            return;
        }

        var sourceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var detail in Details)
        {
            var key = detail.ItemId > 0 ? $"item:{detail.ItemId}" : detail.ItemCode;
            if (!sourceKeys.Add(key))
            {
                ModelState.AddModelError(string.Empty, $"Item '{detail.ItemCode}' is duplicated.");
            }
            if (detail.ActQty <= 0)
            {
                ModelState.AddModelError(string.Empty, $"Item '{detail.ItemCode}' must have Act. Qty > 0.");
            }
        }

        if (Header.FromStore.HasValue && Header.FromStore.Value > 0)
        {
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            conn.Open();
            foreach (var group in Details.Where(x => x.ItemId > 0).GroupBy(x => x.ItemId))
            {
                var totalActQty = group.Sum(x => x.ActQty);
                var stockQty = CheckStore(conn, group.Key, Header.FromStore.Value);
                if (totalActQty > stockQty)
                {
                    var itemName = GetItemDisplayName(group.Key);
                    ModelState.AddModelError(string.Empty, $"Item '{itemName}' stock quantity is {stockQty:N0}.");
                }
            }
        }
    }

    private decimal CheckStore(SqlConnection conn, int itemId, int storeId)
    {
        if (itemId <= 0 || storeId <= 0) return 0m;
        using var cmd = new SqlCommand("EXEC dbo.HaiCheckItemInStore @ItemID, @StoreID, NULL", conn);
        cmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = itemId;
        cmd.Parameters.Add("@StoreID", SqlDbType.Int).Value = storeId;
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return 0m;
        var value = rd["EndQty"];
        return value == DBNull.Value ? 0m : Convert.ToDecimal(value);
    }

    private bool AreAllMrItemsOutOfStock(SqlConnection conn, long requestNo, int storeId)
    {
        if (requestNo <= 0 || storeId <= 0) return false;
        var itemIds = new List<int>();
        using (var cmd = new SqlCommand(@"SELECT DISTINCT i.ItemID
FROM dbo.MATERIAL_REQUEST_DETAIL d
INNER JOIN dbo.INV_ItemList i ON LTRIM(RTRIM(d.ITEMCODE)) = LTRIM(RTRIM(i.ItemCode))
WHERE d.REQUEST_NO=@RequestNo
  AND ISNULL(d.NEW_ORDER,0) > ISNULL(d.ISSUED,0)
  AND i.ItemID IS NOT NULL", conn))
        {
            cmd.Parameters.Add("@RequestNo", SqlDbType.BigInt).Value = requestNo;
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                itemIds.Add(Convert.ToInt32(rd["ItemID"]));
            }
        }

        if (itemIds.Count == 0) return false;
        return itemIds.All(itemId => CheckStore(conn, itemId, storeId) <= 0m);
    }

    private string GetItemDisplayName(int itemId)
    {
        if (itemId <= 0) return itemId.ToString();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand("SELECT TOP 1 ISNULL(ItemCode,'') + CASE WHEN ISNULL(ItemName,'')='' THEN '' ELSE ' - ' + ISNULL(ItemName,'') END FROM dbo.INV_ItemList WHERE ItemID=@ItemID", conn);
        cmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = itemId;
        return Convert.ToString(cmd.ExecuteScalar()) ?? itemId.ToString();
    }

    private List<InventoryIssueDetailRow> LoadDetailsForTransaction(SqlConnection conn, SqlTransaction tran, long flowId)
    {
        var results = new List<InventoryIssueDetailRow>();
        using var cmd = new SqlCommand(@"SELECT ItemID, RequestNo, ChekingID, ChekingDTID, MRDetailID, Doc_Qty, Act_Qty, UnitPrice, Amount, LocationID
FROM dbo.INV_ItemFlowDetail
WHERE FlowID = @FlowID", conn, tran);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            results.Add(new InventoryIssueDetailRow
            {
                ItemId = Convert.ToInt32(rd["ItemID"]),
                RequestNo = rd["RequestNo"] == DBNull.Value ? null : Convert.ToInt64(rd["RequestNo"]),
                ChekingID = rd["ChekingID"] == DBNull.Value ? null : Convert.ToInt64(rd["ChekingID"]),
                ChekingDTID = rd["ChekingDTID"] == DBNull.Value ? null : Convert.ToInt64(rd["ChekingDTID"]),
                MRDetailID = rd["MRDetailID"] == DBNull.Value ? null : Convert.ToInt64(rd["MRDetailID"]),
                DocQty = Convert.ToDecimal(rd["Doc_Qty"] == DBNull.Value ? 0 : rd["Doc_Qty"]),
                ActQty = Convert.ToDecimal(rd["Act_Qty"] == DBNull.Value ? 0 : rd["Act_Qty"]),
                UnitPrice = Convert.ToDecimal(rd["UnitPrice"] == DBNull.Value ? 0 : rd["UnitPrice"]),
                Amount = Convert.ToDecimal(rd["Amount"] == DBNull.Value ? 0 : rd["Amount"]),
                LocationId = rd["LocationID"] == DBNull.Value ? null : Convert.ToInt32(rd["LocationID"])
            });
        }
        return results;
    }

    private void InsertDetail(SqlConnection conn, SqlTransaction tran, long flowId, InventoryIssueDetailRow d)
    {
        using var ins = new SqlCommand(@"INSERT INTO dbo.INV_ItemFlowDetail(ItemID,Unit,Doc_Qty,Act_Qty,UnitPrice,Amount,FlowID,LocationID,ChekingID,ChekingDTID,MRDetailID,RequestNo)
SELECT @ItemID, ISNULL(i.Unit,''), @DocQty, @ActQty, @UnitPrice, @Amount, @FlowID, @LocationID, @ChekingID, @ChekingDTID, @MRDetailID, @RequestNo
FROM dbo.INV_ItemList i WHERE i.ItemID=@ItemID", conn, tran);
        ins.Parameters.Add("@ItemID", SqlDbType.Int).Value = d.ItemId;
        ins.Parameters.Add("@DocQty", SqlDbType.Decimal).Value = d.DocQty;
        ins.Parameters.Add("@ActQty", SqlDbType.Decimal).Value = d.ActQty;
        ins.Parameters.Add("@UnitPrice", SqlDbType.Decimal).Value = d.UnitPrice;
        ins.Parameters.Add("@Amount", SqlDbType.Decimal).Value = d.ActQty * d.UnitPrice;
        ins.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        ins.Parameters.Add("@LocationID", SqlDbType.Int).Value = d.LocationId > 0 ? d.LocationId.Value : DBNull.Value;
        ins.Parameters.Add("@ChekingID", SqlDbType.BigInt).Value = d.ChekingID.HasValue ? d.ChekingID.Value : DBNull.Value;
        ins.Parameters.Add("@ChekingDTID", SqlDbType.BigInt).Value = d.ChekingDTID.HasValue ? d.ChekingDTID.Value : DBNull.Value;
        ins.Parameters.Add("@MRDetailID", SqlDbType.BigInt).Value = d.MRDetailID.HasValue ? d.MRDetailID.Value : DBNull.Value;
        ins.Parameters.Add("@RequestNo", SqlDbType.BigInt).Value = d.RequestNo.HasValue ? d.RequestNo.Value : DBNull.Value;
        ins.ExecuteNonQuery();
    }
    private void ApplyMrIssued(SqlConnection conn, SqlTransaction tran, InventoryIssueDetailRow d)
    {
        if (!d.MRDetailID.HasValue || d.MRDetailID.Value <= 0 || d.ActQty <= 0) return;
        using var cmd = new SqlCommand(@"UPDATE dbo.MATERIAL_REQUEST_DETAIL
SET ISSUED = ISNULL(ISSUED,0) + @Qty
WHERE ID = @Id", conn, tran);
        cmd.Parameters.Add("@Qty", SqlDbType.Decimal).Value = d.ActQty;
        cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = d.MRDetailID.Value;
        cmd.ExecuteNonQuery();
    }
    private void RollbackMrIssued(SqlConnection conn, SqlTransaction tran, List<InventoryIssueDetailRow> details)
    {
        foreach (var d in details)
        {
            if (!d.MRDetailID.HasValue || d.MRDetailID.Value <= 0 || d.ActQty <= 0) continue;
            using var cmd = new SqlCommand(@"UPDATE dbo.MATERIAL_REQUEST_DETAIL
SET ISSUED = CASE WHEN ISNULL(ISSUED,0) - @Qty < 0 THEN 0 ELSE ISNULL(ISSUED,0) - @Qty END
WHERE ID = @Id", conn, tran);
            cmd.Parameters.Add("@Qty", SqlDbType.Decimal).Value = d.ActQty;
            cmd.Parameters.Add("@Id", SqlDbType.BigInt).Value = d.MRDetailID.Value;
            cmd.ExecuteNonQuery();
        }
    }
    private HashSet<long> GetRequestNosByMrDetails(SqlConnection conn, SqlTransaction tran, List<InventoryIssueDetailRow> details)
    {
        var result = new HashSet<long>();
        foreach (var d in details)
        {
            if (d.RequestNo.HasValue && d.RequestNo.Value > 0)
            {
                result.Add(d.RequestNo.Value);
                continue;
            }

            AddRequestNoByMrDetail(conn, tran, result, d.MRDetailID);
        }
        return result;
    }
    private void AddRequestNoByMrDetail(SqlConnection conn, SqlTransaction tran, HashSet<long> requestNos, long? mrDetailId)
    {
        if (!mrDetailId.HasValue || mrDetailId.Value <= 0) return;
        using var cmd = new SqlCommand("SELECT REQUEST_NO FROM dbo.MATERIAL_REQUEST_DETAIL WHERE ID=@ID", conn, tran);
        cmd.Parameters.Add("@ID", SqlDbType.BigInt).Value = mrDetailId.Value;
        var value = cmd.ExecuteScalar();
        if (value != null && value != DBNull.Value)
        {
            requestNos.Add(Convert.ToInt64(value));
        }
    }

    private void SyncMaterialRequestStatuses(SqlConnection conn, SqlTransaction tran, HashSet<long> requestNos)
    {
        foreach (var requestNo in requestNos)
        {
            using var cmd = new SqlCommand(@"SELECT SUM(ISNULL(ISSUED,0)) - SUM(ISNULL(NEW_ORDER,0))
FROM dbo.MATERIAL_REQUEST_DETAIL
WHERE REQUEST_NO=@RequestNo", conn, tran);
            cmd.Parameters.Add("@RequestNo", SqlDbType.BigInt).Value = requestNo;
        cmd.Parameters.Add("@KPGroupID", SqlDbType.Int).Value = GetCurrentKpGroupId();
            var diff = Convert.ToDecimal(cmd.ExecuteScalar() ?? 0);
            using var update = new SqlCommand("UPDATE dbo.MATERIAL_REQUEST SET MATERIALSTATUSID=@StatusID WHERE REQUEST_NO=@RequestNo", conn, tran);
            update.Parameters.Add("@RequestNo", SqlDbType.BigInt).Value = requestNo;
            update.Parameters.Add("@StatusID", SqlDbType.Int).Value = diff >= 0 ? 6 : 2;
            update.ExecuteNonQuery();
        }
    }


    private void IssueFromInventoryIssue(SqlConnection conn, SqlTransaction tran, long flowId, long sourceIssueFlowId)
    {
        using (var lockIssue = new SqlCommand("SELECT ISNULL(DeptRecDocID,0) FROM dbo.INV_ItemFlow WHERE FlowID=@FlowID", conn, tran))
        {
            lockIssue.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
            var linkedId = Convert.ToInt64(lockIssue.ExecuteScalar() ?? 0L);
            if (linkedId > 0)
            {
                throw new InvalidOperationException("Selected Inventory Issue has already been Issue.");
            }
        }

        using (var cmd = new SqlCommand("UPDATE dbo.INV_ItemFlow SET DeptRecDocID = @FlowID, StatusID = 3 WHERE FlowID = @FlowID", conn, tran))
        {
            cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
            cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
            cmd.ExecuteNonQuery();
        }

        using (var cmd = new SqlCommand(@"INSERT INTO dbo.INV_ItemFlowAction(FlowID,UserID,TheDateTime,ActionTypeID,ComputerName,Des)
VALUES(@FlowID,@UserID,GETDATE(),3,@ComputerName,'Issuer Confirmed')", conn, tran))
        {
            cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
            cmd.Parameters.Add("@UserID", SqlDbType.Int).Value = GetCurrentEmployeeId();
            cmd.Parameters.Add("@ComputerName", SqlDbType.NVarChar, 128).Value = Environment.MachineName;
            cmd.ExecuteNonQuery();
        }
    }

    private void BindHeaderParams(SqlCommand cmd)
    {
        cmd.Parameters.Add("@KPGroup", SqlDbType.Int).Value = GetCurrentKpGroupId();
        cmd.Parameters.Add("@OperatorId", SqlDbType.Int).Value = GetCurrentEmployeeId();
        cmd.Parameters.Add("@FlowNo", SqlDbType.NVarChar, 30).Value = Header.FlowNo;
        cmd.Parameters.Add("@FlowDate", SqlDbType.Date).Value = Header.FlowDate.Date;
        cmd.Parameters.Add("@FromStore", SqlDbType.Int).Value = Header.FromStore.HasValue && Header.FromStore > 0 ? Header.FromStore.Value : DBNull.Value;
        cmd.Parameters.Add("@FlowSubType", SqlDbType.Int).Value = Header.FlowSubType.HasValue && Header.FlowSubType > 0 ? Header.FlowSubType.Value : DBNull.Value;
        cmd.Parameters.Add("@RecDept", SqlDbType.Int).Value = Header.RecDept.HasValue && Header.RecDept > 0 ? Header.RecDept.Value : DBNull.Value;
        cmd.Parameters.Add("@AssetLocation", SqlDbType.Int).Value = Header.AssetLocation.HasValue && Header.AssetLocation > 0 ? Header.AssetLocation.Value : DBNull.Value;
        cmd.Parameters.Add("@ReceivedBy", SqlDbType.Int).Value = Header.ReceivedBy.HasValue && Header.ReceivedBy > 0 ? Header.ReceivedBy.Value : DBNull.Value;
        cmd.Parameters.Add("@According", SqlDbType.NVarChar, 250).Value = string.IsNullOrWhiteSpace(Header.According) ? DBNull.Value : Header.According.Trim();
        cmd.Parameters.Add("@Reason", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(Header.Reason) ? DBNull.Value : Header.Reason.Trim();
        cmd.Parameters.Add("@StatusID", SqlDbType.Int).Value = Header.StatusID > 0 ? Header.StatusID : 1;
    }

    private string GetNextVoucherNo(int? flowSubType)
    {
        var now = DateTime.Now;
        var IssueFrom = GetIssueFrom(flowSubType, GetCurrentKpGroupId());
        var prefix = BuildVoucherPrefix(now, GetCurrentKpGroupId(), IssueFrom);
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand("SELECT ISNULL(MAX(RIGHT(FlowNo,3)),'000') FROM dbo.INV_ItemFlow WHERE MONTH(FlowDate)=@Month AND YEAR(FlowDate)=@Year AND LEFT(FlowNo,8)=@Prefix", conn);
        cmd.Parameters.Add("@Month", SqlDbType.Int).Value = now.Month;
        cmd.Parameters.Add("@Year", SqlDbType.Int).Value = now.Year;
        cmd.Parameters.Add("@Prefix", SqlDbType.NVarChar, 8).Value = prefix;
        var seq = Convert.ToInt32(cmd.ExecuteScalar() ?? 0) + 1;
        return $"{prefix}-{seq:000}";
    }

    private static string GetIssueFrom(int? flowSubType, int kpGroupId)
    {
        if (!flowSubType.HasValue) return string.Empty;
        return flowSubType.Value switch
        {
            1 => "SUPPLIER",
            6 when kpGroupId != 1 => "MAIN_STORE",
            _ => string.Empty
        };
    }

    private static string BuildVoucherPrefix(DateTime now, int kpGroupId, string IssueFrom)
    {
        var basePrefix = now.ToString("yyMM");
        if (kpGroupId == 1)
        {
            return IssueFrom == "SUPPLIER" ? $"{basePrefix}-XRC" : $"{basePrefix}-XRT";
        }

        var groupCode = kpGroupId switch
        {
            2 => "T",
            4 => "H",
            6 => "A",
            12 => "C",
            13 => "F",
            14 => "M",
            16 => "S",
            _ => "T"
        };
        var flowCode = IssueFrom == "MAIN_STORE" ? "RC" : "RT";
        return $"{basePrefix}-{groupCode}{flowCode}";
    }

    private string? ValidateAttachment(IFormFile file)
    {
        var allowedExtensions = AllowedAttachmentExtensionsText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.StartsWith('.') ? x.ToLowerInvariant() : $".{x.ToLowerInvariant()}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var extension = Path.GetExtension(file.FileName) ?? string.Empty;
        if (!allowedExtensions.Contains(extension))
        {
            return $"Attached file extension is invalid. Allowed: {AllowedAttachmentExtensionsText}";
        }

        var maxBytes = MaxAttachmentSizeMb * 1024L * 1024L;
        if (file.Length > maxBytes)
        {
            return $"Attached file size must not exceed {MaxAttachmentSizeMb} MB.";
        }

        return null;
    }

    private void SaveAttachment(SqlConnection conn, SqlTransaction tran, long flowId, int? userId, string voucherNo, DateTime voucherDate, IFormFile file, List<string> savedFilePaths)
    {
        var uploadFolder = ResolveAttachmentUploadFolder(voucherNo, voucherDate, "IssueVoucher");
        Directory.CreateDirectory(uploadFolder);

        var savedFileName = BuildAttachmentFileName(file.FileName);
        var fullPath = Path.Combine(uploadFolder, savedFileName);
        using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            file.CopyTo(stream);
        }

        savedFilePaths.Add(fullPath);

        using var cmd = new SqlCommand(@"
INSERT INTO dbo.INV_ItemFlow_Doc
(
    FlowID,
    FilePath,
    UploadDate,
    UserID
)
VALUES
(
    @FlowID,
    @FilePath,
    GETDATE(),
    @UserID
)", conn, tran);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        cmd.Parameters.Add("@FilePath", SqlDbType.NVarChar, 1000).Value = savedFileName;
        cmd.Parameters.Add("@UserID", SqlDbType.Int).Value = userId.HasValue ? userId.Value : DBNull.Value;
        cmd.ExecuteNonQuery();
    }

    private string ResolveAttachmentUploadFolder(string voucherNo, DateTime voucherDate, string voucherFolder)
    {
        var basePath = _config.GetValue<string>("FileUploads:BasePath");
        if (string.IsNullOrWhiteSpace(basePath))
        {
            throw new InvalidOperationException("FileUploads:BasePath is missing in appsettings.json.");
        }

        var rootPath = Path.IsPathRooted(basePath)
            ? basePath
            : Path.Combine(Directory.GetCurrentDirectory(), basePath);
        var configuredFunctionPath = _config.GetValue<string>($"FileUploads:Funtions:{FunctionId}");
        if (string.IsNullOrWhiteSpace(configuredFunctionPath))
        {
            throw new InvalidOperationException($"FileUploads:Funtions:{FunctionId} is missing in appsettings.json.");
        }

        var relativeSegments = configuredFunctionPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var voucherNoClean = (voucherNo ?? string.Empty).Trim().Replace("/", string.Empty);
        return Path.Combine([rootPath, .. relativeSegments, voucherFolder, voucherDate.Year.ToString(), voucherNoClean]);
    }

    private static string BuildAttachmentFileName(string originalFileName)
    {
        var sourceName = Path.GetFileName(originalFileName);
        var nameOnly = Path.GetFileNameWithoutExtension(sourceName);
        var extension = Path.GetExtension(sourceName);
        var safeName = string.Concat(nameOnly.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "attachment";
        }

        return $"{safeName}_{DateTime.UtcNow.Ticks}{extension}";
    }

    private static void RemoveSavedFiles(IEnumerable<string> savedFilePaths)
    {
        foreach (var path in savedFilePaths)
        {
            try
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
            }
            catch
            {
            }
        }
    }

    private List<InventoryIssueAttachmentViewModel> LoadAttachmentRows(long flowId)
    {
        var rows = new List<InventoryIssueAttachmentViewModel>();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"SELECT d.DocID,d.FlowID,d.FilePath,d.UploadDate,d.UserID,ISNULL(e.EmployeeCode,'') AS EmployeeCode
FROM dbo.INV_ItemFlow_Doc d
LEFT JOIN dbo.MS_Employee e ON d.UserID = e.EmployeeID
WHERE d.FlowID = @FlowID
ORDER BY d.UploadDate DESC, d.DocID DESC", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        conn.Open();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(new InventoryIssueAttachmentViewModel
            {
                DocId = Convert.ToInt32(rd["DocID"]),
                FlowId = Convert.ToInt64(rd["FlowID"]),
                FilePath = Convert.ToString(rd["FilePath"]) ?? string.Empty,
                UploadDate = rd["UploadDate"] == DBNull.Value ? null : Convert.ToDateTime(rd["UploadDate"]),
                UserId = rd["UserID"] == DBNull.Value ? null : Convert.ToInt32(rd["UserID"]),
                EmployeeCode = Convert.ToString(rd["EmployeeCode"]) ?? string.Empty
            });
        }
        return rows;
    }

    private string? GetAttachmentFileName(long flowId, int docId)
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand("SELECT FilePath FROM dbo.INV_ItemFlow_Doc WHERE DocID=@DocID AND FlowID=@FlowID", conn);
        cmd.Parameters.Add("@DocID", SqlDbType.Int).Value = docId;
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        conn.Open();
        return Convert.ToString(cmd.ExecuteScalar());
    }

    private List<int> ParseDeletedAttachmentDocIds(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<int>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private void DeleteAttachmentsInTransaction(SqlConnection conn, SqlTransaction tran, long flowId, List<int> docIds)
    {
        if (docIds.Count == 0) return;
        var flow = GetFlowHeader(flowId);
        if (flow == null) return;
        var folder = ResolveAttachmentUploadFolder(flow.FlowNo, flow.FlowDate, "IssueVoucher");

        foreach (var docId in docIds.Distinct())
        {
            string? fileName;
            using (var load = new SqlCommand("SELECT FilePath FROM dbo.INV_ItemFlow_Doc WHERE DocID=@DocID AND FlowID=@FlowID", conn, tran))
            {
                load.Parameters.Add("@DocID", SqlDbType.Int).Value = docId;
                load.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
                fileName = Convert.ToString(load.ExecuteScalar());
            }

            using (var del = new SqlCommand("DELETE FROM dbo.INV_ItemFlow_Doc WHERE DocID=@DocID AND FlowID=@FlowID", conn, tran))
            {
                del.Parameters.Add("@DocID", SqlDbType.Int).Value = docId;
                del.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
                del.ExecuteNonQuery();
            }

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                var fullPath = Path.Combine(folder, fileName);
                if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
            }
        }
    }

    private InventoryIssueHeader? GetFlowHeader(long flowId)
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand("SELECT FlowNo, FlowDate, FlowSubType FROM dbo.INV_ItemFlow WHERE FlowID=@FlowID", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        conn.Open();
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;
        return new InventoryIssueHeader
        {
            FlowID = flowId,
            FlowNo = Convert.ToString(rd["FlowNo"]) ?? string.Empty,
            FlowDate = Convert.ToDateTime(rd["FlowDate"]),
            FlowSubType = rd["FlowSubType"] == DBNull.Value ? null : Convert.ToInt32(rd["FlowSubType"])
        };
    }

    private void UpdatePriceByDetails(SqlConnection conn, SqlTransaction tran, List<InventoryIssueDetailRow> details)
    {
        foreach (var detail in details.Where(x => x.ItemId > 0 && x.UnitPrice > 0).GroupBy(x => x.ItemId).Select(x => x.First()))
        {
            using var getCmd = new SqlCommand("SELECT ISNULL(UnitPrice,0) FROM dbo.INV_ItemList WHERE ItemID = @ItemID", conn, tran);
            getCmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = detail.ItemId;
            var oldPrice = Convert.ToDecimal(getCmd.ExecuteScalar() ?? 0m);
            if (oldPrice == detail.UnitPrice) continue;

            using var updateCmd = new SqlCommand("UPDATE dbo.INV_ItemList SET UnitPrice = @NewPrice WHERE ItemID = @ItemID AND ISNULL(IsIrregular,0) = 0", conn, tran);
            updateCmd.Parameters.Add("@NewPrice", SqlDbType.Decimal).Value = detail.UnitPrice;
            updateCmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = detail.ItemId;
            var affected = updateCmd.ExecuteNonQuery();
            if (affected <= 0) continue;

            using var logCmd = new SqlCommand(@"INSERT INTO dbo.INV_UpdatePriceOfItemHis(ItemID, UserID, TheDateTime, OldPrice, NewPrice)
VALUES(@ItemID, @UserID, GETDATE(), @OldPrice, @NewPrice)", conn, tran);
            logCmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = detail.ItemId;
            logCmd.Parameters.Add("@UserID", SqlDbType.Int).Value = GetCurrentEmployeeId();
            logCmd.Parameters.Add("@OldPrice", SqlDbType.Decimal).Value = oldPrice;
            logCmd.Parameters.Add("@NewPrice", SqlDbType.Decimal).Value = detail.UnitPrice;
            logCmd.ExecuteNonQuery();
        }
    }

    private int GetCurrentRoleId() => int.Parse(User.FindFirst("RoleID")?.Value ?? "0");
    private bool IsAdminRole() => User.FindFirst("IsAdminRole")?.Value == "True";
    private int GetCurrentKpGroupId()
    {
        return int.TryParse(User.FindFirst("StoreGR")?.Value, out var kpGroupFromClaim) && kpGroupFromClaim > 0 ? kpGroupFromClaim : 0;
    }
    private int GetCurrentEmployeeId() => int.Parse(User.FindFirst("EmployeeID")?.Value ?? User.FindFirst("EmpID")?.Value ?? "0");
    private PagePermissions GetUserPermissions() => IsAdminRole() ? new PagePermissions { AllowedNos = Enumerable.Range(1, 20).ToList() } : new PagePermissions { AllowedNos = _permissionService.GetPermissionsForPage(GetCurrentRoleId(), FunctionId) };

    private string GetCurrentEmployeeCode()
    {
        var claimCode = User.FindFirst("EmployeeCode")?.Value;
        if (!string.IsNullOrWhiteSpace(claimCode)) return claimCode;

        var employeeId = GetCurrentEmployeeId();
        if (employeeId <= 0) return string.Empty;
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand("SELECT EmployeeCode FROM dbo.MS_Employee WHERE EmployeeID=@EmployeeID", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId;
        conn.Open();
        return Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
    }

    private InventoryIssueReportModel? LoadIssueReport(SqlConnection conn, long flowId, bool previewSign = false)
    {
        using var cmd = new SqlCommand(@"SELECT h.FlowID,h.FlowNo,h.FlowDate,ISNULL(h.According,'') AS According,ISNULL(h.Reason,'') AS Reason,
       ISNULL(s.StoreName,'') AS StoreName,ISNULL(d.DeptName,'') AS DeptName,ISNULL(l.LocationName,'') AS LocationName,
       h.OperatorID,h.ReceivedBy,ISNULL(h.StatusID,1) AS StatusID,ISNULL(op.EmployeeName,'') AS StoremanName,ISNULL(rc.EmployeeName,'') AS ReceiverName
FROM dbo.INV_ItemFlow h
LEFT JOIN dbo.INV_StoreList s ON s.StoreID=h.FromStore
LEFT JOIN dbo.MS_Department d ON d.DeptID=h.RecDept
LEFT JOIN dbo.MS_CoLocation l ON l.LocationID=h.AssetLocation
LEFT JOIN dbo.MS_Employee op ON op.EmployeeID=h.OperatorID
LEFT JOIN dbo.MS_Employee rc ON rc.EmployeeID=h.ReceivedBy
WHERE h.FlowID=@FlowID", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;
        var operatorId = rd["OperatorID"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["OperatorID"]);
        var receiverId = rd["ReceivedBy"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["ReceivedBy"]);
        var statusId = Convert.ToInt32(rd["StatusID"] == DBNull.Value ? 1 : rd["StatusID"]);
        var report = new InventoryIssueReportModel
        {
            FlowNo = Convert.ToString(rd["FlowNo"]) ?? string.Empty,
            FlowDateText = Convert.ToDateTime(rd["FlowDate"]).ToString("dd/MM/yyyy"),
            According = Convert.ToString(rd["According"]) ?? string.Empty,
            Reason = Convert.ToString(rd["Reason"]) ?? string.Empty,
            StoreName = Convert.ToString(rd["StoreName"]) ?? string.Empty,
            DepartmentName = Convert.ToString(rd["DeptName"]) ?? string.Empty,
            LocationName = Convert.ToString(rd["LocationName"]) ?? string.Empty,
            StoremanName = Convert.ToString(rd["StoremanName"]) ?? string.Empty,
            ReceiverName = Convert.ToString(rd["ReceiverName"]) ?? string.Empty
        };
        rd.Close();

        if (statusId >= 2)
        {
            report.StoremanSignature = LoadEmployeeSignature(conn, operatorId);
        }

        if (statusId >= 3 || previewSign)
        {
            report.ReceiverSignature = LoadEmployeeSignature(conn, receiverId);
        }

        using var detailCmd = new SqlCommand(@"SELECT ISNULL(i.ItemCode,'') AS ItemCode,ISNULL(i.ItemName,'') AS ItemName,ISNULL(dt.Unit,'') AS Unit,
       ISNULL(dt.Doc_Qty,0) AS DocQty,ISNULL(dt.Act_Qty,0) AS ActQty,ISNULL(dt.Amount,0) AS Amount,ISNULL(l.LocationName,'') AS LocationName
FROM dbo.INV_ItemFlowDetail dt
INNER JOIN dbo.INV_ItemList i ON i.ItemID=dt.ItemID
LEFT JOIN dbo.MS_CoLocation l ON l.LocationID=dt.LocationID
WHERE dt.FlowID=@FlowID
ORDER BY i.ItemCode", conn);
        detailCmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        using var detailRd = detailCmd.ExecuteReader();
        while (detailRd.Read())
        {
            report.Items.Add(new InventoryIssueReportItem
            {
                ItemCode = Convert.ToString(detailRd["ItemCode"]) ?? string.Empty,
                ItemName = Convert.ToString(detailRd["ItemName"]) ?? string.Empty,
                Unit = Convert.ToString(detailRd["Unit"]) ?? string.Empty,
                DocQty = Convert.ToDecimal(detailRd["DocQty"]),
                ActQty = Convert.ToDecimal(detailRd["ActQty"]),
                Amount = Convert.ToDecimal(detailRd["Amount"]),
                LocationName = Convert.ToString(detailRd["LocationName"]) ?? string.Empty
            });
        }

        return report;
    }

    private byte[]? LoadEmployeeSignature(SqlConnection conn, int? employeeId)
    {
        if (!employeeId.HasValue || employeeId.Value < 0) return null;
        using var cmd = new SqlCommand("SELECT TOP 1 ISNULL(UrlNomalSign,'') FROM dbo.MS_Employee WHERE EmployeeID=@EmployeeID", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId.Value;
        var fileName = Convert.ToString(cmd.ExecuteScalar())?.Trim();
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var path = ResolveEmployeeSignaturePath(fileName);
        return string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path) ? null : System.IO.File.ReadAllBytes(path);
    }

    private string ResolveEmployeeSignaturePath(string fileName)
    {
        var cleanedFileName = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(cleanedFileName))
        {
            return string.Empty;
        }

        var basePath = _config.GetValue<string>("FileUploads:BasePath");
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return string.Empty;
        }

        var rootPath = Path.IsPathRooted(basePath)
            ? basePath
            : Path.Combine(Directory.GetCurrentDirectory(), basePath);

        var functionPath = _config.GetValue<string>("FileUploads:Funtions:18");
        if (!string.IsNullOrWhiteSpace(functionPath))
        {
            var relativeSegments = functionPath
                .Replace('\\', '/')
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (relativeSegments.Length > 0)
            {
                rootPath = Path.Combine([rootPath, .. relativeSegments]);
            }
        }

        return Path.Combine(rootPath, cleanedFileName);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string((value ?? string.Empty).Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
    }
}

public class InventoryIssueHeader
{
    public long FlowID { get; set; }
    [Required] public string FlowNo { get; set; } = string.Empty;
    [DataType(DataType.Date)] public DateTime FlowDate { get; set; } = DateTime.Today;
    public int? FromStore { get; set; }
    public int? FlowSubType { get; set; }
    public int? RecDept { get; set; }
    public int? AssetLocation { get; set; }
    public int? ReceivedBy { get; set; }
    public int? POID { get; set; }
    public int? MoveToCPNStore { get; set; }
    public string? According { get; set; }
    public string? Reason { get; set; }
    public int StatusID { get; set; } = 1;
    public string StatusName { get; set; } = string.Empty;
}

public class InventoryIssueAttachmentViewModel
{
    public int DocId { get; set; }
    public long FlowId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public DateTime? UploadDate { get; set; }
    public int? UserId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
}

public class InventoryIssueDetailRow
{
    public int ItemId { get; set; }
    public long? RequestNo { get; set; }
    public long? ChekingID { get; set; }
    public long? ChekingDTID { get; set; }
    public long? MRDetailID { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal DocQty { get; set; }
    public decimal ActQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public int? LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
}

public class InventoryIssueItemOption
{
    public int ItemId { get; set; }
    public long? ChekingID { get; set; }
    public long? ChekingDTID { get; set; }
    public long? MRDetailID { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal RemainingQty { get; set; }
    public decimal Price { get; set; }
}

public class ConfirmFlowInfo
{
    public long FlowID { get; set; }
    public string FlowNo { get; set; } = string.Empty;
    public int FlowSubType { get; set; }
    public int StatusID { get; set; }
    public int KPGroup { get; set; }
    public int? MoveToCPNStore { get; set; }
    public int? RecDept { get; set; }
    public int? ReceivedBy { get; set; }
}

public class AdjustApartmentMailContext
{
    public string FlowDateText { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
    public string AdjustedByEmployeeCode { get; set; } = string.Empty;
    public string AdjustedByEmployeeName { get; set; } = string.Empty;
    public string According { get; set; } = string.Empty;
    public List<AdjustApartmentMailItem> Items { get; set; } = new();
}

public class AdjustApartmentMailItem
{
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public decimal ActQty { get; set; }
    public string LocationName { get; set; } = string.Empty;
}
