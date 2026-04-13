using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Helpers;
using SmartSam.Pages;
using SmartSam.Services;
using SmartSam.Services.Interfaces;
using System.Data;

namespace SmartSam.Pages.Purchasing.MaterialRequest;

public class MaterialRequestDetailModel : BasePageModel
{
    private readonly MaterialRequestService _materialRequestService;
    private readonly ISecurityService _securityService;
    private readonly PermissionService _permissionService;
    private readonly ILogger<MaterialRequestDetailModel> _logger;

    private const int MaterialRequestFunctionId = 104;
    private const int PermissionCreate = 1;
    private const int PermissionCreateAuto = 2;
    private const int PermissionEdit = 3;
    private const int PermissionShowAll = 4;
    private const int NoScopeStoreGroup = -1;
    private const int StatusJustCreated = -1;
    private const int StatusSubmittedToHead = 0;
    private const int StatusHeadDeptApproved = 1;
    private const int StatusPurchaserChecked = 2;
    private const int StatusCfoApproved = 3;
    private const int StatusCollectedToPr = 4;
    private const int StatusRejected = 5;
    private const int StatusIssued = 6;
    private const string TestCcEmail = "hai.dq@saigonskygarden.com.vn";

    private EmployeeMaterialScopeDto _dataScope = new EmployeeMaterialScopeDto();
    private bool _isAdminRole;
    private string? _draftCreatorEmployeeCode;
    private bool _draftCreatorResolved;

        public MaterialRequestDetailModel(
        ISecurityService securityService,
        IConfiguration configuration,
        PermissionService permissionService,
        ILogger<MaterialRequestDetailModel> logger) : base(configuration)
    {
        _materialRequestService = new MaterialRequestService(configuration);
        _securityService = securityService;
        _permissionService = permissionService;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    public MaterialRequestDetailDto Input { get; set; } = new MaterialRequestDetailDto();

    [BindProperty]
    public string? LinesJson { get; set; }

    [BindProperty]
    public string? RejectItemLineIdsJson { get; set; }

    [BindProperty]
    public string? RejectItemReason { get; set; }

    [BindProperty]
    public List<MaterialRequestLineDto> Lines { get; set; } = new List<MaterialRequestLineDto>();

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? WarningMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    [BindProperty]
    public PagePermissions PagePerm { get; set; } = new PagePermissions();
    public List<SelectListItem> StoreGroups { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> Statuses { get; set; } = new List<SelectListItem>();

    public bool IsEdit
    {
        get { return Id.HasValue && Id.Value > 0; }
    }

    public bool CanAccessAllStoreGroups
    {
        get
        {
            return IsAdminUser()
                || _dataScope.StoreGroup == 1
                || _dataScope.IsPurchaser
                || _dataScope.IsCFO
                || _dataScope.IsBOD
                || _dataScope.ApprovalLevel >= 2;
        }
    }

    public bool CanSave
    {
        get
        {
            if (IsEdit)
            {
                return CanUpdateDraftMaterialRequest() && CurrentStatusId == StatusJustCreated;
            }

            return CanCreateDraftMaterialRequest();
        }
    }

    public bool StoreGroupLocked
    {
        get { return !CanAccessAllStoreGroups; }
    }

    public int CurrentStatusId
    {
        get { return Input.MaterialStatusId ?? StatusJustCreated; }
    }

    public bool CanSubmit
    {
        get { return IsEdit && CanUpdateDraftMaterialRequest() && CurrentStatusId == StatusJustCreated; }
    }

    public bool CanApprove
    {
        get { return IsEdit && CanApproveStatus(CurrentStatusId, Input.IsAuto); }
    }

    public bool CanIssue
    {
        get { return false; }
    }

    public bool ShowIssueButton
    {
        get { return IsEdit && CurrentStatusId == StatusCollectedToPr; }
    }

    public bool CanCalculate
    {
        get { return IsEdit && (IsAdminUser() || _dataScope.IsPurchaser) && CurrentStatusId <= StatusPurchaserChecked; }
    }

    public bool CanReject
    {
        get { return IsEdit && CanRejectStatus(CurrentStatusId, Input.IsAuto); }
    }

    public bool HideZeroBuyLines
    {
        get
        {
            return _dataScope.IsCFO || _dataScope.ApprovalLevel >= 3;
        }
    }

    public bool ShowAdvancedDetailColumns
    {
        get
        {
            return _isAdminRole
                || _dataScope.IsPurchaser
                || _dataScope.IsCFO
                || _dataScope.IsBOD
                || _dataScope.ApprovalLevel >= 2;
        }
    }

    public string BackToListUrl
    {
        get { return ResolveBackToListUrl(); }
    }

    // ==========================================
    // 1. PAGE LOAD AND INITIALIZATION
    // ==========================================
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");
        PagePerm = new PagePermissions();
        PagePerm = GetUserPermissions();
        await LoadUserScopeAsync(cancellationToken);
        if (!CanAccessPage())
        {
            return Forbid();
        }

        LoadDropdowns();
        ApplyTempDataMessagesToModelState();

        if (!IsEdit)
        {
            if (!CanCreateDraftMaterialRequest())
            {
                return Forbid();
            }

            if (StoreGroupLocked)
            {
                Input.StoreGroup = _dataScope.StoreGroup ?? NoScopeStoreGroup;
            }

            Input.DateCreate = DateTime.Today;
            Input.FromDate = DateTime.Today.AddMonths(-3);
            Input.ToDate = DateTime.Today;
            Input.MaterialStatusId ??= StatusJustCreated;
            return Page();
        }

        var detail = await _materialRequestService.GetDetailAsync(Id!.Value, cancellationToken);
        if (detail is null)
        {
            ErrorMessage = "Material Request not found.";
            return RedirectToPage("./Index");
        }

        if (StoreGroupLocked && _dataScope.StoreGroup.HasValue && detail.StoreGroup != _dataScope.StoreGroup)
        {
            return Forbid();
        }

        Input = detail;
        Lines = (await _materialRequestService.GetLinesAsync(Id.Value, cancellationToken)).ToList();
        if (HideZeroBuyLines)
        {
            Lines = Lines
                .Where(line => (line.Buy ?? 0m) > 0m)
                .ToList();
        }
        await LoadDraftCreatorAsync(Id.Value, cancellationToken);
        PagePerm = GetUserPermissions();
        return Page();
    }

    // ==========================================
    // 2. SAVE AND WORKFLOW ACTIONS
    // ==========================================
    public async Task<IActionResult> OnPostSave(CancellationToken cancellationToken)
    {
        int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");
        PagePerm = new PagePermissions();
        PagePerm = GetUserPermissions();
        await LoadUserScopeAsync(cancellationToken);
        if (!CanAccessPage())
        {
            return Forbid();
        }

        if (StoreGroupLocked && !_dataScope.StoreGroup.HasValue)
        {
            ModelState.AddModelError(string.Empty, "Store Group scope is required.");
            LoadDropdowns();
            Lines = ResolvePostedLines();
            return Page();
        }

        LoadDropdowns();

        if (StoreGroupLocked)
        {
            Input.StoreGroup = _dataScope.StoreGroup ?? NoScopeStoreGroup;
        }

        MaterialRequestDetailDto? existing = null;
        if (IsEdit)
        {
            existing = await _materialRequestService.GetDetailAsync(Id!.Value, cancellationToken);
            if (existing is null)
            {
                ErrorMessage = "Material Request not found.";
                return RedirectToPage("./Index");
            }

            var currentStatus = existing.MaterialStatusId ?? StatusJustCreated;
            PagePerm = GetUserPermissions();
            await LoadDraftCreatorAsync(Id.Value, cancellationToken);
            if (currentStatus != StatusJustCreated || !CanUpdateDraftMaterialRequest())
            {
                WarningMessage = "You cannot edit this Material Request at current status.";
                return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(Id));
            }

            Input.MaterialStatusId = existing.MaterialStatusId;
            Input.Approval = existing.Approval;
            Input.ApprovalEnd = existing.ApprovalEnd;
            Input.PostPr = existing.PostPr;
            Input.FromDate ??= existing.FromDate;
            Input.ToDate ??= existing.ToDate;
        }
        else if (User.Identity?.IsAuthenticated != true)
        {
            return Forbid();
        }

        var isAutoRequest = IsEdit && existing?.IsAuto == true;
        var lines = ResolvePostedLines();
        await ApplyReadonlyLineRulesAsync(
            lines,
            Id,
            cancellationToken,
            preserveReadonlyOrder: isAutoRequest,
            preserveEditableNote: ShouldPreserveEditableNote(CurrentStatusId));

        if (!ModelState.IsValid)
        {
            Lines = lines;
            return Page();
        }

        try
        {
            var actionMode = Request.Form["workflowActionModeInput"].ToString();
            var draftSaveAction = Request.Form["DraftSaveAction"].ToString();
            if (IsEdit && existing is not null)
            {
                Input.IsAuto = existing.IsAuto;
            }
            else
            {
                Input.IsAuto = false;
            }

            var savedNo = await _materialRequestService.SaveAsync(IsEdit ? Id : null, Input, lines, cancellationToken);
            if (string.Equals(actionMode, "draft-save", StringComparison.OrdinalIgnoreCase))
            {
                var draftStatus = existing?.MaterialStatusId ?? Input.MaterialStatusId ?? StatusJustCreated;
                await WriteDraftSaveHistoryAsync(savedNo, draftStatus, cancellationToken);
            }
            var isDraftSave = string.Equals(actionMode, "draft-save", StringComparison.OrdinalIgnoreCase);
            var successMessage = isDraftSave
                ? GetDraftSaveSuccessMessage(draftSaveAction)
                : (IsEdit ? "Saved successfully." : "Material Request created successfully.");

            if (isDraftSave && IsAjaxRequest())
            {
                return new JsonResult(new
                {
                    success = true,
                    requestNo = savedNo,
                    message = successMessage
                });
            }

            SuccessMessage = successMessage;
            return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(savedNo));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MaterialRequest][Save] {ex}");
            Lines = lines;
            if (IsAjaxRequest() && string.Equals(Request.Form["workflowActionModeInput"].ToString(), "draft-save", StringComparison.OrdinalIgnoreCase))
            {
                return new JsonResult(new
                {
                    success = false,
                    message = ex is InvalidOperationException ? ex.Message : $"Cannot save Material Request. {ex.Message}"
                });
            }
            ModelState.AddModelError(string.Empty, ex is InvalidOperationException ? ex.Message : $"Cannot save Material Request. {ex.Message}");
            return Page();
        }
    }


    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        return await OnPostSave(cancellationToken);
    }

    public async Task<IActionResult> OnPostSubmit(CancellationToken cancellationToken)
    {
        return await HandleWorkflowActionAsync(MaterialRequestWorkflowAction.Submit, cancellationToken);
    }

    public async Task<IActionResult> OnPostApprove(CancellationToken cancellationToken)
    {
        return await HandleWorkflowActionAsync(MaterialRequestWorkflowAction.Approve, cancellationToken);
    }

    public async Task<IActionResult> OnPostCalculate(CancellationToken cancellationToken)
    {
        return await HandleCalculateAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSavePurchaserLines(CancellationToken cancellationToken)
    {
        int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");
        PagePerm = new PagePermissions();
        PagePerm = GetUserPermissions();
        await LoadUserScopeAsync(cancellationToken);
        if (!CanAccessPage())
        {
            return IsAjaxRequest()
                ? new JsonResult(new { success = false, message = "Access denied." }) { StatusCode = StatusCodes.Status403Forbidden }
                : Forbid();
        }

        if (!IsEdit)
        {
            return new JsonResult(new { success = false, message = "Please save Material Request first." });
        }

        var existing = await _materialRequestService.GetDetailAsync(Id!.Value, cancellationToken);
        if (existing is null)
        {
            return new JsonResult(new { success = false, message = "Material Request not found." }) { StatusCode = StatusCodes.Status404NotFound };
        }

        var currentStatus = existing.MaterialStatusId ?? StatusJustCreated;
        if (!ShouldPreserveEditableBuy(currentStatus))
        {
            return new JsonResult(new { success = false, message = "You cannot edit Buy/Note at current status." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }

        var lines = ResolvePostedLines();
        if (lines.Count == 0)
        {
            lines = (await _materialRequestService.GetLinesAsync(Id!.Value, cancellationToken)).ToList();
        }

        await ApplyReadonlyLineRulesAsync(lines, Id, cancellationToken, preserveEditableBuy: true, preserveReadonlyOrder: existing.IsAuto, preserveEditableNote: true);

        try
        {
            var savedNo = await _materialRequestService.SaveDetailLinesAsync(Id.Value, lines, cancellationToken);
            return new JsonResult(new
            {
                success = true,
                requestNo = savedNo,
                message = "Buy/Note changes saved."
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MaterialRequest][SavePurchaserLines] {ex}");
            return new JsonResult(new
            {
                success = false,
                message = ex is InvalidOperationException ? ex.Message : $"Cannot save Buy/Note changes. {ex.Message}"
            })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
        }
    }

    public Task<IActionResult> OnPostIssue(CancellationToken cancellationToken)
    {
        WarningMessage = "Issue is handled in another business flow and is not available in Material Request.";
        return Task.FromResult<IActionResult>(RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(Id)));
    }

    public async Task<IActionResult> OnPostReject(CancellationToken cancellationToken)
    {
        if (ParseRejectItemLineIds().Count > 0)
        {
            return await HandleRejectItemAsync(cancellationToken);
        }

        return await HandleWorkflowActionAsync(MaterialRequestWorkflowAction.Reject, cancellationToken);
    }

    public async Task<IActionResult> OnPostRejectItem(CancellationToken cancellationToken)
    {
        return await HandleRejectItemAsync(cancellationToken);
    }

    // ==========================================
    // 3. ITEM LOOKUP AND QUICK ITEM ACTIONS
    // ==========================================
    private async Task<IActionResult> HandleWorkflowActionAsync(MaterialRequestWorkflowAction action, CancellationToken cancellationToken)
    {
        int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");
        PagePerm = new PagePermissions();
        PagePerm = GetUserPermissions();
        await LoadUserScopeAsync(cancellationToken);
        if (!CanAccessPage())
        {
            return Forbid();
        }

        if (!IsEdit)
        {
            WarningMessage = "Please save Material Request first.";
            return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(null));
        }

        LoadDropdowns();

        if (StoreGroupLocked)
        {
            Input.StoreGroup = _dataScope.StoreGroup ?? NoScopeStoreGroup;
        }

        var existing = await _materialRequestService.GetDetailAsync(Id!.Value, cancellationToken);
        if (existing is null)
        {
            ErrorMessage = "Material Request not found.";
            return RedirectToPage("./Index");
        }

        await LoadDraftCreatorAsync(Id.Value, cancellationToken);

        var currentStatus = existing.MaterialStatusId ?? StatusJustCreated;
        PagePerm = GetUserPermissions();
        var isAuto = existing.IsAuto;
        if (action == MaterialRequestWorkflowAction.Submit && !CanUpdateDraftMaterialRequest())
        {
            WarningMessage = "You do not have permission to submit this draft.";
            return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(Id));
        }
        var transition = ResolveTransition(action, currentStatus, isAuto, _dataScope.IsHeadDept);
        if (transition is null)
        {
            WarningMessage = "Invalid workflow action for current status.";
            return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(Id));
        }

        if (!CanExecuteTransition(action, currentStatus, isAuto))
        {
            WarningMessage = "You do not have permission for this workflow action.";
            return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(Id));
        }

        try
        {
            if (action == MaterialRequestWorkflowAction.Submit)
            {
                await _materialRequestService.UpdateWorkflowHeaderAsync(
                    Id!.Value,
                    transition.NextStatusId,
                    transition.Approval,
                    transition.ApprovalEnd,
                    transition.PostPr,
                    cancellationToken);

                await WriteWorkflowHistoryAsync(action, currentStatus, transition, Id.Value, cancellationToken);
                TryQueueWorkflowNotifyEmail(action, currentStatus, existing, isAuto, Array.Empty<MaterialRequestLineDto>(), _dataScope.IsHeadDept);
                SuccessMessage = transition.SuccessMessage;
                return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(Id));
            }

            IReadOnlyList<MaterialRequestLineDto> purchaserLines = Array.Empty<MaterialRequestLineDto>();
            if (action == MaterialRequestWorkflowAction.Approve && currentStatus == StatusHeadDeptApproved)
            {
                purchaserLines = await _materialRequestService.GetLinesAsync(Id.Value, cancellationToken);
                if (isAuto || !HasPositiveBuyLines(purchaserLines))
                {
                    transition.ApprovalEnd = true;
                    transition.PostPr = false;
                }
            }

            await _materialRequestService.UpdateWorkflowHeaderAsync(
                Id!.Value,
                transition.NextStatusId,
                transition.Approval,
                transition.ApprovalEnd,
                transition.PostPr,
                cancellationToken);

            await WriteWorkflowHistoryAsync(action, currentStatus, transition, Id.Value, cancellationToken);
            TryQueueWorkflowNotifyEmail(action, currentStatus, existing, isAuto, purchaserLines, false);
            SuccessMessage = transition.SuccessMessage;
            return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(Id));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MaterialRequest][WorkflowAction:{action}] {ex}");
            ErrorMessage = ex is InvalidOperationException
                ? ex.Message
                : $"Cannot process workflow action. {ex.Message}";
            return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(Id));
        }
    }

    private async Task<IActionResult> HandleRejectItemAsync(CancellationToken cancellationToken)
    {
        int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");
        PagePerm = new PagePermissions();
        PagePerm = GetUserPermissions();
        await LoadUserScopeAsync(cancellationToken);
        if (!CanAccessPage())
        {
            return Forbid();
        }

        if (!IsEdit)
        {
            WarningMessage = "Please save Material Request first.";
            return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(null));
        }

        LoadDropdowns();

        if (StoreGroupLocked)
        {
            Input.StoreGroup = _dataScope.StoreGroup ?? NoScopeStoreGroup;
        }

        var existing = await _materialRequestService.GetDetailAsync(Id!.Value, cancellationToken);
        if (existing is null)
        {
            ErrorMessage = "Material Request not found.";
            return RedirectToPage("./Index");
        }

        var currentStatus = existing.MaterialStatusId ?? StatusJustCreated;
        var isAuto = existing.IsAuto;
        if (!CanRejectStatus(currentStatus, isAuto))
        {
            WarningMessage = "You do not have permission for this workflow action.";
            return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(Id));
        }

        var rejectedLineIds = ParseRejectItemLineIds();
        if (rejectedLineIds.Count == 0)
        {
            WarningMessage = "Please select item row(s) to reject.";
            return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(Id));
        }

        var lines = ResolvePostedLines();
        if (lines.Count == 0)
        {
            lines = (await _materialRequestService.GetLinesAsync(Id!.Value, cancellationToken)).ToList();
        }

        await ApplyReadonlyLineRulesAsync(lines, Id, cancellationToken, preserveReadonlyOrder: isAuto);

        try
        {
            var savedNo = await _materialRequestService.ProcessLegacyRejectAsync(
                existing,
                lines,
                rejectedLineIds,
                RejectItemReason,
                User.Identity?.Name ?? string.Empty,
                cancellationToken);
            SuccessMessage = "Selected item(s) rejected.";
            return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(savedNo));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MaterialRequest][RejectItem] {ex}");
            ErrorMessage = ex is InvalidOperationException
                ? ex.Message
                : $"Cannot reject item(s). {ex.Message}";
            return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(Id));
        }
    }

    private async Task<IActionResult> HandleCalculateAsync(CancellationToken cancellationToken)
    {
        int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");
        PagePerm = new PagePermissions();
        PagePerm = GetUserPermissions();
        await LoadUserScopeAsync(cancellationToken);
        if (!CanAccessPage())
        {
            return Forbid();
        }

        if (!IsEdit)
        {
            WarningMessage = "Please save Material Request first.";
            return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(null));
        }

        LoadDropdowns();

        if (StoreGroupLocked)
        {
            Input.StoreGroup = _dataScope.StoreGroup ?? NoScopeStoreGroup;
        }

        var existing = await _materialRequestService.GetDetailAsync(Id!.Value, cancellationToken);
        if (existing is null)
        {
            ErrorMessage = "Material Request not found.";
            return RedirectToPage("./Index");
        }

        var currentStatus = existing.MaterialStatusId ?? StatusJustCreated;
        if (!CanCalculateStatus(currentStatus))
        {
            WarningMessage = "You do not have permission for this workflow action.";
            return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(Id));
        }

        var lines = ResolvePostedLines();
        if (lines.Count == 0)
        {
            lines = (await _materialRequestService.GetLinesAsync(Id!.Value, cancellationToken)).ToList();
        }

        await ApplyReadonlyLineRulesAsync(
            lines,
            Id,
            cancellationToken,
            preserveEditableBuy: ShouldPreserveEditableBuy(currentStatus),
            preserveReadonlyOrder: existing.IsAuto,
            preserveEditableNote: ShouldPreserveEditableNote(currentStatus));

        var storeGroup = existing.StoreGroup ?? Input.StoreGroup ?? _dataScope.StoreGroup;
        if (!storeGroup.HasValue)
        {
            WarningMessage = "Store Group scope is required.";
            return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(Id));
        }

        await _materialRequestService.CalculatePurchaserLinesAsync(Id.Value, storeGroup.Value, lines, cancellationToken);

        try
        {
            var savedNo = await _materialRequestService.SaveDetailLinesAsync(Id.Value, lines, cancellationToken);
            SuccessMessage = "Calculate completed.";
            return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(savedNo));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MaterialRequest][Calculate] {ex}");
            ErrorMessage = ex is InvalidOperationException
                ? ex.Message
                : $"Cannot calculate Material Request. {ex.Message}";
            return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(Id));
        }
    }

    private List<int> ParseRejectItemLineIds()
    {
        if (string.IsNullOrWhiteSpace(RejectItemLineIdsJson))
        {
            return new List<int>();
        }

        try
        {
            var lineIds = JsonSerializer.Deserialize<List<int?>>(RejectItemLineIdsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return lineIds?
                .Where(x => x.HasValue && x.Value > 0)
                .Select(x => x!.Value)
                .Distinct()
                .ToList()
                ?? new List<int>();
        }
        catch
        {
            return new List<int>();
        }
    }

    public async Task<IActionResult> OnGetSearchItems(string? keyword, bool checkBalanceInStore = false, CancellationToken cancellationToken = default)
    {
        int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");
        PagePerm = new PagePermissions();
        PagePerm = GetUserPermissions();
        await LoadUserScopeAsync(cancellationToken);
        if (!CanAccessPage())
        {
            return new JsonResult(new { success = false, message = "Access denied." });
        }

        var rows = await _materialRequestService.SearchItemsAsync(keyword, checkBalanceInStore, cancellationToken);
        return new JsonResult(new { success = true, data = rows });
    }

    public async Task<IActionResult> OnPostCreateItem([FromForm] string? itemName, [FromForm] string? unit, CancellationToken cancellationToken)
    {
        int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");
        PagePerm = new PagePermissions();
        PagePerm = GetUserPermissions();
        await LoadUserScopeAsync(cancellationToken);
        if (!CanAccessPage())
        {
            return new JsonResult(new { success = false, message = "Access denied." });
        }

        if (IsEdit)
        {
            var existing = await _materialRequestService.GetDetailAsync(Id!.Value, cancellationToken);
            if (existing is null)
            {
                return new JsonResult(new { success = false, message = "Material Request not found." });
            }

            var currentStatus = existing.MaterialStatusId ?? StatusJustCreated;
            if (!CanUpdateDraftMaterialRequest() || currentStatus != StatusJustCreated)
            {
                return new JsonResult(new { success = false, message = "You cannot add item at current status." });
            }
        }
        else if (!CanCreateDraftMaterialRequest())
        {
            return new JsonResult(new { success = false, message = "Access denied." });
        }

        try
        {
            var created = await _materialRequestService.CreateQuickItemAsync(itemName ?? string.Empty, unit, cancellationToken);
            return new JsonResult(new { success = true, data = created });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, message = ex.Message });
        }
    }

    // ==========================================
    // 4. DROPDOWN LOADING
    // ==========================================
    private void LoadDropdowns()
    {
        var stores = LoadListFromSql(
            @"SELECT
                    kp.KPGroupID,
                    CASE
                        WHEN kp.KPGroupName IS NOT NULL AND dep.DeptCode IS NOT NULL THEN CONCAT(kp.KPGroupName, ' (', dep.DeptCode, ')')
                        ELSE kp.KPGroupName
                    END AS KPGroupName
              FROM dbo.INV_KPGroup kp
              INNER JOIN dbo.MS_Department dep ON dep.DeptID = kp.DepID
              ORDER BY kp.KPGroupID",
            "KPGroupID",
            "KPGroupName");

        StoreGroups = stores;

        var statuses = LoadListFromSql(
            @"SELECT MaterialStatusID, MaterialStatusName
              FROM dbo.MaterialStatus
              ORDER BY MaterialStatusID",
            "MaterialStatusID",
            "MaterialStatusName");

        Statuses = statuses;

        if (StoreGroupLocked)
        {
            if (_dataScope.StoreGroup.HasValue)
            {
                var selectedStore = stores.FirstOrDefault(x => string.Equals(x.Value, _dataScope.StoreGroup.Value.ToString(), StringComparison.OrdinalIgnoreCase));
                StoreGroups = new List<SelectListItem>
                {
                    new SelectListItem
                    {
                        Value = _dataScope.StoreGroup.Value.ToString(),
                        Text = selectedStore?.Text ?? string.Empty
                    }
                };
            }
            else
            {
                StoreGroups = new List<SelectListItem>
                {
                    new SelectListItem
                    {
                        Value = string.Empty,
                        Text = "(No Store Group Scope)"
                    }
                };
            }
        }
    }

    // ==========================================
    // 5. INTERNAL HELPERS
    // ==========================================
    private static List<MaterialRequestLineDto> ParseLinesFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<MaterialRequestLineDto>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<MaterialRequestLineDto>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (parsed == null)
            {
                return new List<MaterialRequestLineDto>();
            }

            return parsed;
        }
        catch
        {
            return new List<MaterialRequestLineDto>();
        }
    }

    /// <summary>
    /// Lay danh sach line da post tu form / hidden field.
    /// Co the tra ve list rong neu client khong gui data line.
    /// </summary>
    private List<MaterialRequestLineDto> ResolvePostedLines()
    {
        var linesFromJson = ParseLinesFromJson(LinesJson);
        if (linesFromJson.Count > 0)
        {
            return NormalizeLines(linesFromJson);
        }

        return NormalizeLines(Lines);
    }

    /// <summary>
    /// Chuan hoa line va ap dung rule readonly tren server truoc khi luu.
    /// </summary>
    private async Task ApplyReadonlyLineRulesAsync(
        List<MaterialRequestLineDto> lines,
        long? requestNo,
        CancellationToken cancellationToken,
        bool preserveEditableBuy = false,
        bool preserveReadonlyOrder = false,
        bool preserveEditableNote = false)
    {
        if (lines.Count == 0)
        {
            return;
        }

        var itemCodes = lines
            .Select(x => (x.ItemCode ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var itemUnits = await _materialRequestService.GetItemUnitsAsync(itemCodes, cancellationToken);
        var existingSnapshots = requestNo.HasValue
            ? await _materialRequestService.GetLineReadonlySnapshotsAsync(requestNo.Value, cancellationToken)
            : new Dictionary<string, MaterialRequestLineReadonlySnapshotDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var itemCode = (line.ItemCode ?? string.Empty).Trim();
            line.ItemCode = itemCode;
            line.ItemName = (line.ItemName ?? string.Empty).Trim();
            line.Note = (line.Note ?? string.Empty).Trim();
            line.Selected = true;

            if (string.IsNullOrWhiteSpace(itemCode))
            {
                continue;
            }

            if (existingSnapshots.TryGetValue(itemCode, out var snapshot))
            {
                if (preserveReadonlyOrder)
                {
                    line.OrderQty = snapshot.OrderQty;
                }
                line.NotReceipt = snapshot.NotReceipt;
                line.InStock = snapshot.InStock;
                line.AccIn = snapshot.AccIn;
                if (!preserveEditableBuy)
                {
                    line.Buy = snapshot.Buy;
                }
                if (!preserveEditableNote)
                {
                    line.Note = snapshot.Note;
                }
                line.Unit = snapshot.Unit;
            }
            else
            {
                line.NotReceipt = 0m;
                line.InStock = 0m;
                line.AccIn = 0m;
                if (!preserveEditableBuy)
                {
                    line.Buy = 0m;
                }
                if (!preserveEditableNote)
                {
                    line.Note = string.Empty;
                }
            }

            if (itemUnits.TryGetValue(itemCode, out var unitFromMaster) && !string.IsNullOrWhiteSpace(unitFromMaster))
            {
                line.Unit = unitFromMaster;
            }

            line.Unit = (line.Unit ?? string.Empty).Trim();
        }
    }

    /// <summary>
    /// Lam sach danh sach line truoc khi luu reject-pool / auto flow.
    /// Trim text, bo dong rong, va chuan hoa cac gia tri null ve 0.
    /// </summary>
    private static List<MaterialRequestLineDto> NormalizeLines(IEnumerable<MaterialRequestLineDto> lines)
    {
        var result = new List<MaterialRequestLineDto>();
        foreach (var line in lines)
        {
            var itemCode = (line.ItemCode ?? string.Empty).Trim();
            var itemName = (line.ItemName ?? string.Empty).Trim();
            var unit = (line.Unit ?? string.Empty).Trim();
            var note = (line.Note ?? string.Empty).Trim();

            // Bo qua dong rong thuc su: khong co text va toan so deu = 0/null.
            var isBlank = string.IsNullOrWhiteSpace(itemCode)
                && string.IsNullOrWhiteSpace(itemName)
                && string.IsNullOrWhiteSpace(unit)
                && string.IsNullOrWhiteSpace(note)
                && !(line.OrderQty.HasValue && line.OrderQty.Value != 0)
                && !(line.NotReceipt.HasValue && line.NotReceipt.Value != 0)
                && !(line.InStock.HasValue && line.InStock.Value != 0)
                && !(line.AccIn.HasValue && line.AccIn.Value != 0)
                && !(line.Buy.HasValue && line.Buy.Value != 0)
                && !(line.NormQty.HasValue && line.NormQty.Value != 0)
                && !(line.Issued.HasValue && line.Issued.Value != 0)
                && !(line.TempStore.HasValue && line.TempStore.Value != 0)
                && !(line.Price.HasValue && line.Price.Value != 0);

            if (isBlank)
            {
                continue;
            }

            result.Add(new MaterialRequestLineDto
            {
                Id = line.Id,
                ItemCode = itemCode,
                ItemName = itemName,
                Unit = unit,
                OrderQty = line.OrderQty ?? 0,
                NotReceipt = line.NotReceipt ?? 0,
                InStock = line.InStock ?? 0,
                AccIn = line.AccIn ?? 0,
                Buy = line.Buy ?? 0,
                NormMain = line.NormMain ?? 0,
                Price = line.Price ?? 0,
                Note = note,
                NewItem = line.NewItem,
                Selected = line.Selected,
                ManualCheck = line.ManualCheck,
                TempStore = line.TempStore ?? 0,
                Issued = line.Issued ?? 0
            });
        }

        return result;
    }

    private static string GetDraftSaveSuccessMessage(string? draftSaveAction)
    {
        return (draftSaveAction ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "add-detail" => "Item added and saved.",
            "remove-item" => "Item removed and saved.",
            "create-new-item" => "New item created and saved.",
            _ => "Line changes saved."
        };
    }

    /// <summary>
    /// Xac dinh khi nao nguoi dung duoc giu/quyentao tac Buy o line.
    /// </summary>
    private bool ShouldPreserveEditableBuy(int currentStatus)
    {
        return currentStatus == StatusHeadDeptApproved && (IsAdminUser() || _dataScope.IsPurchaser);
    }

    /// <summary>
    /// Xac dinh khi nao nguoi dung duoc giu/quyentao tac Note o line.
    /// </summary>
    private bool ShouldPreserveEditableNote(int currentStatus)
    {
        if (currentStatus == StatusJustCreated)
        {
            return CanUpdateDraftMaterialRequest();
        }

        return ShouldPreserveEditableBuy(currentStatus);
    }

    /// <summary>
    /// Ghi 1 dong lich su workflow theo action va trang thai moi.
    /// </summary>
    private async Task WriteWorkflowHistoryAsync(
        MaterialRequestWorkflowAction action,
        int currentStatus,
        MaterialRequestWorkflowTransition transition,
        long requestNo,
        CancellationToken cancellationToken)
    {
        var note = ResolveWorkflowHistoryNote(action, currentStatus);
        if (string.IsNullOrWhiteSpace(note))
        {
            return;
        }

        var typeEffective = transition.NextStatusId;
        await _materialRequestService.InsertSuperRequestAsync(
            requestNo,
            note,
            typeEffective,
            User.Identity?.Name ?? string.Empty,
            cancellationToken);
    }

    /// <summary>
    /// Ghi lich su cho luong draft save / update MR.
    /// </summary>
    private async Task WriteDraftSaveHistoryAsync(long requestNo, int currentStatus, CancellationToken cancellationToken)
    {
        await _materialRequestService.InsertSuperRequestAsync(
            requestNo,
            "Update MR",
            currentStatus,
            User.Identity?.Name ?? string.Empty,
            cancellationToken);
    }

    /// <summary>
    /// Map action + trang thai hien tai sang noi dung note trong workflow history.
    /// </summary>
    private static string? ResolveWorkflowHistoryNote(MaterialRequestWorkflowAction action, int currentStatus)
    {
        if (action == MaterialRequestWorkflowAction.Submit)
        {
            return "Submit";
        }

        if (action == MaterialRequestWorkflowAction.Approve)
        {
            return currentStatus switch
            {
                StatusSubmittedToHead => "Head approve",
                StatusHeadDeptApproved => "Purchaser approve",
                StatusPurchaserChecked => "CFO approve",
                StatusCfoApproved => "Collected to PR",
                _ => null
            };
        }

        if (action == MaterialRequestWorkflowAction.Reject)
        {
            return "Reject request";
        }

        return null;
    }

    /// <summary>
    /// Kiem tra request co phai Ajax hay khong.
    /// </summary>
    private bool IsAjaxRequest()
    {
        return string.Equals(Request.Headers["X-Requested-With"].ToString(), "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Nap scope/nghia vu cua user hien tai truoc khi check quyen MR.
    /// </summary>
    private async Task LoadUserScopeAsync(CancellationToken cancellationToken)
    {
        _isAdminRole = IsAdminUser();
        if (_isAdminRole)
        {
            _dataScope = new EmployeeMaterialScopeDto();
            return;
        }

        _dataScope = await _materialRequestService.GetEmployeeScopeAsync(User.Identity?.Name, cancellationToken);
    }

    /// <summary>
    /// Lay danh sach quyen trang va record pham vi cua user hien tai.
    /// </summary>
    private PagePermissions GetUserPermissions()
    {
        bool isAdmin = IsAdminUser();
        int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");

        var permsObj = new PagePermissions();

        if (isAdmin)
        {
            permsObj.AllowedNos = Enumerable.Range(1, 20).ToList();
        }
        else
        {
            permsObj.AllowedNos = _permissionService.GetPermissionsForPage(roleId, MaterialRequestFunctionId);
        }

        return permsObj;
    }

    /// <summary>
    /// Kiem tra user co la admin hay khong.
    /// Duy tri o page nay vi BasePageModel chua co helper admin chung; Index/Detail
    /// dang dung chung cung 1 rule nen tam thoi phai lap lai dung pattern nay.
    /// </summary>
    private bool IsAdminUser()
    {
        // Uu tien claim dang co san trong auth ticket neu he thong da set.
        var adminClaim = (User.FindFirst("IsAdminRole")?.Value ?? string.Empty).Trim();
        if (string.Equals(adminClaim, "True", StringComparison.OrdinalIgnoreCase)
            || string.Equals(adminClaim, "1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Neu claim chua co thi fallback ve DB, theo thu tu:
        // 1) MS_Employee.IsAdminUser
        // 2) SYS_RoleMember/SYS_Role.IsAdminRole
        var employeeCode = User.Identity?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(employeeCode))
        {
            return false;
        }

        try
        {
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand(@"
                SELECT TOP 1 ISNULL(IsAdminUser, 0)
                FROM dbo.MS_Employee
                WHERE EmployeeCode = @EmployeeCode", conn);
            cmd.Parameters.Add("@EmployeeCode", SqlDbType.VarChar, 50).Value = employeeCode;
            conn.Open();
            var value = cmd.ExecuteScalar();
            if (value == null || value == DBNull.Value)
            {
                using var roleCmd = new SqlCommand(@"
                    SELECT TOP 1 ISNULL(r.IsAdminRole, 0)
                    FROM dbo.SYS_RoleMember rm
                    INNER JOIN dbo.SYS_Role r ON r.RoleID = rm.RoleID
                    INNER JOIN dbo.MS_Employee e ON e.EmployeeID = rm.Operator
                    WHERE e.EmployeeCode = @EmployeeCode
                    ORDER BY r.IsAdminRole DESC, rm.RoleID ASC", conn);
                roleCmd.Parameters.Add("@EmployeeCode", SqlDbType.VarChar, 50).Value = employeeCode;
                var roleValue = roleCmd.ExecuteScalar();
                if (roleValue == null || roleValue == DBNull.Value)
                {
                    return false;
                }

                return Convert.ToBoolean(roleValue);
            }

            return Convert.ToBoolean(value);
        }
        catch
        {
            return false;
        }
    }

    private bool CanAccessPage()
    {
        return User.Identity?.IsAuthenticated == true
            || IsAdminUser()
            || HasPermission(PermissionCreate)
            || HasPermission(PermissionCreateAuto)
            || HasPermission(PermissionEdit)
            || HasPermission(PermissionShowAll)
            || HasPermission(5)
            || HasPermission(6)
            || HasPermission(7);
    }

    /// <summary>
    /// Check if the user can create a new MR draft.
    /// Any authenticated user can start a draft.
    /// </summary>
    /// <returns>True if draft MR creation is allowed.</returns>
    private bool CanCreateDraftMaterialRequest()
    {
        return User.Identity?.IsAuthenticated == true;
    }

    /// <summary>
    /// Check if the user can update an existing Just Created MR draft.
    /// Only the creator, admin, or Edit-permission users may update the draft.
    /// </summary>
    /// <returns>True if updating the draft MR is allowed.</returns>
    private bool CanUpdateDraftMaterialRequest()
    {
        if (IsAdminUser())
        {
            return true;
        }

        if (IsEdit && CurrentStatusId == StatusJustCreated && !string.IsNullOrWhiteSpace(_draftCreatorEmployeeCode))
        {
            return string.Equals(
                User.Identity?.Name?.Trim(),
                _draftCreatorEmployeeCode,
                StringComparison.OrdinalIgnoreCase);
        }

        return HasPermission(PermissionEdit);
    }

    private async Task LoadDraftCreatorAsync(long requestNo, CancellationToken cancellationToken)
    {
        if (_draftCreatorResolved)
        {
            return;
        }

        _draftCreatorResolved = true;
        _draftCreatorEmployeeCode = null;

        const string sql = @"
            SELECT TOP 1 LTRIM(RTRIM(e.EmployeeCode)) AS EmployeeCode
            FROM dbo.SUPER_REQUEST sr
            INNER JOIN dbo.MS_Employee e ON e.EmployeeID = sr.EmployeeID
            WHERE sr.RequestNo = @RequestNo
              AND sr.TypeEffective = @TypeEffective
            ORDER BY sr.AutoNum ASC";

        await using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        await conn.OpenAsync(cancellationToken);
        await using var cmd = new SqlCommand(sql, conn);
        Helper.AddParameter(cmd, "@RequestNo", requestNo, SqlDbType.Decimal);
        Helper.AddParameter(cmd, "@TypeEffective", StatusJustCreated, SqlDbType.Int);

        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        _draftCreatorEmployeeCode = value == null || value == DBNull.Value ? null : Convert.ToString(value);
    }

    private bool HasPermission(int permissionNo)
    {
        return PagePerm.HasPermission(permissionNo);
    }

    private static MaterialRequestDetailDto MergeEditableInput(MaterialRequestDetailDto existing, MaterialRequestDetailDto posted)
    {
        return new MaterialRequestDetailDto
        {
            RequestNo = existing.RequestNo,
            StoreGroup = posted.StoreGroup ?? existing.StoreGroup,
            DateCreate = posted.DateCreate ?? existing.DateCreate,
            AccordingTo = string.IsNullOrWhiteSpace(posted.AccordingTo) ? existing.AccordingTo : posted.AccordingTo,
            Approval = existing.Approval,
            ApprovalEnd = existing.ApprovalEnd,
            PostPr = existing.PostPr,
            IsAuto = posted.IsAuto,
            FromDate = posted.FromDate ?? existing.FromDate,
            ToDate = posted.ToDate ?? existing.ToDate,
            MaterialStatusId = existing.MaterialStatusId,
            PrNo = string.IsNullOrWhiteSpace(posted.PrNo) ? existing.PrNo : posted.PrNo,
            NoIssue = posted.NoIssue
        };
    }

    /// <summary>
    /// Kiem tra MR da o trang thai ket thuc chua.
    /// </summary>
    private static bool IsTerminalWorkflowStatus(int statusId)
    {
        return statusId == StatusRejected || statusId == StatusIssued;
    }

    /// <summary>
    /// Xac dinh user hien tai co duoc approve MR o trang thai nay khong.
    /// </summary>
    private bool CanApproveStatus(int statusId, bool isAuto)
    {
        if (IsTerminalWorkflowStatus(statusId))
        {
            return false;
        }

        if (_isAdminRole)
        {
            return true;
        }

        return statusId switch
        {
            StatusSubmittedToHead => _dataScope.IsHeadDept,
            StatusHeadDeptApproved => _dataScope.IsPurchaser,
            StatusPurchaserChecked => !isAuto && _dataScope.IsCFO,
            _ => false
        };
    }

    private bool CanRejectStatus(int statusId, bool isAuto)
    {
        if (IsTerminalWorkflowStatus(statusId))
        {
            return false;
        }

        if (_isAdminRole)
        {
            return true;
        }

        return statusId switch
        {
            StatusSubmittedToHead => _dataScope.IsHeadDept,
            StatusHeadDeptApproved => _dataScope.IsPurchaser,
            StatusPurchaserChecked => !isAuto && _dataScope.IsCFO,
            _ => false
        };
    }

    /// <summary>
    /// Xac dinh user hien tai co duoc calculate MR o trang thai nay khong.
    /// </summary>
    private bool CanCalculateStatus(int statusId)
    {
        if (_isAdminRole)
        {
            return true;
        }

        if (!_dataScope.IsPurchaser)
        {
            return false;
        }

        return statusId <= StatusPurchaserChecked;
    }

    private bool CanExecuteTransition(MaterialRequestWorkflowAction action, int currentStatus, bool isAuto)
    {
        if (action == MaterialRequestWorkflowAction.Submit)
        {
            return CanUpdateDraftMaterialRequest() && currentStatus == StatusJustCreated;
        }

        if (action == MaterialRequestWorkflowAction.Approve)
        {
            return CanApproveStatus(currentStatus, isAuto);
        }

        if (action == MaterialRequestWorkflowAction.Issue)
        {
            return currentStatus == StatusCollectedToPr;
        }

        if (action == MaterialRequestWorkflowAction.Reject)
        {
            return CanRejectStatus(currentStatus, isAuto);
        }

        return false;
    }

    /// <summary>
    /// Tao va dua email workflow vao hang doi gui neu action hien tai can notify.
    /// </summary>
    private void TryQueueWorkflowNotifyEmail(
        MaterialRequestWorkflowAction action,
        int currentStatus,
        MaterialRequestDetailDto header,
        bool isAuto,
        IReadOnlyList<MaterialRequestLineDto> lines,
        bool submitAsHeadDept)
    {
        var notifyRequest = BuildWorkflowNotifyRequest(action, currentStatus, header, isAuto, lines, submitAsHeadDept);
        if (notifyRequest is null)
        {
            return;
        }

        if (notifyRequest.Recipients.Count == 0 ||
            string.IsNullOrWhiteSpace(notifyRequest.SenderEmail) ||
            string.IsNullOrWhiteSpace(notifyRequest.Password) ||
            string.IsNullOrWhiteSpace(notifyRequest.MailServer) ||
            notifyRequest.MailPort <= 0)
        {
            return;
        }

        _ = SendNotifyEmailAsync(notifyRequest, header.RequestNo);
    }

    /// <summary>
    /// Xay dung payload email workflow cho submit/approve/reject cua MR.
    /// </summary>
    private MaterialRequestWorkflowNotifyRequestViewModel? BuildWorkflowNotifyRequest(
        MaterialRequestWorkflowAction action,
        int currentStatus,
        MaterialRequestDetailDto header,
        bool isAuto,
        IReadOnlyList<MaterialRequestLineDto> lines,
        bool submitAsHeadDept)
    {
        if (action == MaterialRequestWorkflowAction.Submit && currentStatus == StatusJustCreated)
        {
            var recipients = submitAsHeadDept
                ? GetRecipientsByEmployeeFlag("IsPurchaser")
                : GetHeadDeptRecipientsByStoreGroup(header.StoreGroup);
            if (recipients.Count == 0)
            {
                return null;
            }

            return CreateWorkflowNotifyRequest(
                header,
                recipients,
                submitAsHeadDept
                    ? "[Material Request] Waiting for Purchaser check"
                    : "[Material Request] Waiting for Head Dept approval",
                "MATERIAL REQUEST",
                "#17a2b8",
                submitAsHeadDept
                    ? "has been submitted and approved by Head Dept and is waiting for your check."
                    : "has been submitted and is waiting for your approval.",
                submitAsHeadDept ? "Head approval" : "Submit");
        }

        if (action == MaterialRequestWorkflowAction.Approve && currentStatus == StatusSubmittedToHead)
        {
            var recipients = GetRecipientsByEmployeeFlag("IsPurchaser");
            if (recipients.Count == 0)
            {
                return null;
            }

            return CreateWorkflowNotifyRequest(
                header,
                recipients,
                "[Material Request] Waiting for Purchaser check",
                "MATERIAL REQUEST",
                "#007bff",
                "has been approved by Head Dept and is waiting for your check.",
                "Head approval");
        }

        if (action == MaterialRequestWorkflowAction.Approve && currentStatus == StatusHeadDeptApproved && !isAuto)
        {
            if (!HasPositiveBuyLines(lines))
            {
                return null;
            }

            var recipients = GetRecipientsByEmployeeFlag("IsCFO");
            if (recipients.Count == 0)
            {
                return null;
            }

            return CreateWorkflowNotifyRequest(
                header,
                recipients,
                "[Material Request] Waiting for CFO approval",
                "MATERIAL REQUEST",
                "#007bff",
                "has been checked by Purchaser and is waiting for your approval.",
                "Purchaser check");
        }

        return null;
    }

    private MaterialRequestWorkflowNotifyRequestViewModel CreateWorkflowNotifyRequest(
        MaterialRequestDetailDto header,
        List<MaterialRequestWorkflowRecipientViewModel> recipients,
        string subject,
        string title,
        string color,
        string actionText,
        string stepLabel)
    {
        var requestNo = header.RequestNo > 0 ? header.RequestNo.ToString() : string.Empty;
        var storeGroupText = ResolveStoreGroupText(header.StoreGroup);
        var detailUrl = Url.Page("/Purchasing/MaterialRequest/MaterialRequestDetail", values: new
        {
            id = header.RequestNo,
            mode = "view"
        });

        var absoluteUrl = string.IsNullOrWhiteSpace(detailUrl)
            ? string.Empty
            : $"{Request.Scheme}://{Request.Host}{detailUrl}";
        var body = $@"
        <p>Dear {{RECIPIENT_LABEL}},</p>
        <p>Material Request <b><span style='font-family: ""VNI-Times"", ""VNI-Helve"", sans-serif;'>{WebUtility.HtmlEncode(requestNo)}</span></b> <span style='font-family: ""VNI-Times"", ""VNI-Helve"", sans-serif;'>{WebUtility.HtmlEncode(actionText)}</span></p>
        <ul>
        <li>Request No: <b><span style='font-family: ""VNI-Times"", ""VNI-Helve"", sans-serif;'>{WebUtility.HtmlEncode(requestNo)}</span></b></li>
        <li>Date: <b>{header.DateCreate:dd/MM/yyyy}</b></li>
        <li>Store Group: <b><span style='font-family: ""VNI-Times"", ""VNI-Helve"", sans-serif;'>{WebUtility.HtmlEncode(storeGroupText)}</span></b></li>
        <li>According To: <b><span style='font-family: ""VNI-Times"", ""VNI-Helve"", sans-serif;'>{WebUtility.HtmlEncode(header.AccordingTo ?? string.Empty)}</span></b></li>
        <li>Step: <b><span style='font-family: ""VNI-Times"", ""VNI-Helve"", sans-serif;'>{WebUtility.HtmlEncode(stepLabel)}</span></b></li>
        </ul>
        {(string.IsNullOrWhiteSpace(absoluteUrl) ? string.Empty : $"<p>Click Here to Approve: <a href=\"{WebUtility.HtmlEncode(absoluteUrl)}\">Material Request Detail</a></p>")}
        <p>SmartSam System</p>";

        var htmlBody = EmailTemplateHelper.WrapInNotifyTemplate(title, color, DateTime.Now, body);
        var senderEmail = _config.GetValue<string>("EmailSettings:SenderEmail") ?? string.Empty;
        var password = _config.GetValue<string>("EmailSettings:Password") ?? string.Empty;
        var mailServer = _config.GetValue<string>("EmailSettings:MailServer") ?? string.Empty;
        var mailPort = _config.GetValue<int?>("EmailSettings:MailPort") ?? 0;

        var finalSubject = ApplyMailSubjectPrefix(subject);

        return new MaterialRequestWorkflowNotifyRequestViewModel
        {
            SenderEmail = senderEmail,
            Password = password,
            MailServer = mailServer,
            MailPort = mailPort,
            Subject = finalSubject,
            HtmlBody = htmlBody,
            Recipients = DeduplicateRecipients(recipients),
            CcRecipients = new List<string> { TestCcEmail }
        };
    }

    // Load Head Dept recipients for the Store Group and keep the email plus display label together.
    private List<MaterialRequestWorkflowRecipientViewModel> GetHeadDeptRecipientsByStoreGroup(int? storeGroup)
    {
        var rows = new List<MaterialRequestWorkflowRecipientViewModel>();
        if (!storeGroup.HasValue || storeGroup.Value <= 0)
        {
            return rows;
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
        SELECT
            LTRIM(RTRIM(e.TheEmail)) AS Email,
            LTRIM(RTRIM(e.EmployeeName)) AS EmployeeName,
            LTRIM(RTRIM(e.EmployeeCode)) AS EmployeeCode,
            LTRIM(RTRIM(ISNULL(e.Title, ''))) AS Title
        FROM dbo.INV_KPGroup kp
        INNER JOIN dbo.MS_Employee e ON e.DeptID = kp.DepID
        WHERE kp.KPGroupID = @StoreGroup
        AND ISNULL(e.HeadDept, 0) = 1
        AND ISNULL(LTRIM(RTRIM(e.TheEmail)), '') <> ''
        AND ISNULL(e.IsActive, 0) = 1
        ORDER BY LTRIM(RTRIM(e.TheEmail)), LTRIM(RTRIM(e.EmployeeCode))", conn);
        cmd.Parameters.Add("@StoreGroup", SqlDbType.Int).Value = storeGroup.Value;

        conn.Open();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var email = Convert.ToString(rd["Email"]) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(email))
            {
                continue;
            }

            rows.Add(new MaterialRequestWorkflowRecipientViewModel
            {
                Email = email.Trim(),
                GreetingLabel = BuildRecipientGreetingLabel(
                    Convert.ToString(rd["Title"]),
                    Convert.ToString(rd["EmployeeName"]),
                    Convert.ToString(rd["EmployeeCode"]),
                    email)
            });
        }

        return DeduplicateRecipients(rows);
    }

    // Chi gui mail CFO neu MR con it nhat 1 line Buy > 0.
    private static bool HasPositiveBuyLines(IEnumerable<MaterialRequestLineDto> lines)
    {
        return lines.Any(line => line.Buy.GetValueOrDefault() > 0m);
    }

    // Load workflow recipients by flag and keep one row per email only.
    private List<MaterialRequestWorkflowRecipientViewModel> GetRecipientsByEmployeeFlag(string flagColumn)
    {
        var rows = new List<MaterialRequestWorkflowRecipientViewModel>();

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand($@"
        SELECT
            LTRIM(RTRIM(TheEmail)) AS Email,
            LTRIM(RTRIM(EmployeeName)) AS EmployeeName,
            LTRIM(RTRIM(EmployeeCode)) AS EmployeeCode,
            LTRIM(RTRIM(ISNULL(Title, ''))) AS Title
        FROM dbo.MS_Employee
        WHERE ISNULL({flagColumn}, 0) = 1
        AND ISNULL(LTRIM(RTRIM(TheEmail)), '') <> ''
        AND ISNULL(IsActive, 0) = 1
        ORDER BY LTRIM(RTRIM(TheEmail)), LTRIM(RTRIM(EmployeeCode))", conn);

        conn.Open();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var email = Convert.ToString(rd["Email"]) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(email))
            {
                continue;
            }

            rows.Add(new MaterialRequestWorkflowRecipientViewModel
            {
                Email = email.Trim(),
                GreetingLabel = BuildRecipientGreetingLabel(
                    Convert.ToString(rd["Title"]),
                    Convert.ToString(rd["EmployeeName"]),
                    Convert.ToString(rd["EmployeeCode"]),
                    email)
            });
        }

        return DeduplicateRecipients(rows);
    }

    // Keep one recipient row per email because one user may have duplicate accounts.
    private static List<MaterialRequestWorkflowRecipientViewModel> DeduplicateRecipients(IEnumerable<MaterialRequestWorkflowRecipientViewModel> recipients)
    {
        return recipients
            .Where(x => !string.IsNullOrWhiteSpace(x.Email))
            .GroupBy(x => x.Email.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    // Build the greeting label for the Dear line: prefer "Mr. Name(Code)", then fall back to code/email.
    private static string BuildRecipientGreetingLabel(string? title, string? employeeName, string? employeeCode, string? email)
    {
        var normalizedTitle = NormalizeGreetingTitle(title);
        var name = (employeeName ?? string.Empty).Trim();
        var code = (employeeCode ?? string.Empty).Trim();
        var emailValue = (email ?? string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(name))
        {
            return string.IsNullOrWhiteSpace(normalizedTitle)
                ? (string.IsNullOrWhiteSpace(code) ? name : $"{name}({code})")
                : (string.IsNullOrWhiteSpace(code) ? $"{normalizedTitle} {name}" : $"{normalizedTitle} {name}({code})");
        }

        if (!string.IsNullOrWhiteSpace(code))
        {
            return code;
        }

        return emailValue;
    }

    private static string NormalizeGreetingTitle(string? title)
    {
        var trimmed = (title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        return trimmed.EndsWith(".", StringComparison.Ordinal) ? trimmed : $"{trimmed}.";
    }

    private string ResolveStoreGroupText(int? storeGroup)
    {
        if (!storeGroup.HasValue)
        {
            return string.Empty;
        }

        var selected = StoreGroups.FirstOrDefault(x => string.Equals(x.Value, storeGroup.Value.ToString(), StringComparison.OrdinalIgnoreCase));
        return selected?.Text ?? storeGroup.Value.ToString();
    }

    private async Task SendNotifyEmailAsync(MaterialRequestWorkflowNotifyRequestViewModel notifyRequest, long requestNo)
    {
        try
        {
            // Send one mail per recipient so each person gets a personal greeting.
            foreach (var recipient in notifyRequest.Recipients.DistinctBy(x => x.Email, StringComparer.OrdinalIgnoreCase))
            {
                var recipientLabel = string.IsNullOrWhiteSpace(recipient.GreetingLabel)
                    ? recipient.Email
                    : recipient.GreetingLabel;
                var htmlBody = notifyRequest.HtmlBody.Replace(
                    "{RECIPIENT_LABEL}",
                    WebUtility.HtmlEncode(recipientLabel ?? string.Empty),
                    StringComparison.Ordinal);

                using var mail = new MailMessage
                {
                    From = new MailAddress(notifyRequest.SenderEmail, "SmartSam System"),
                    Subject = notifyRequest.Subject,
                    Body = htmlBody,
                    IsBodyHtml = true
                };

                mail.To.Add(recipient.Email);

                foreach (var ccRecipient in notifyRequest.CcRecipients.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    mail.CC.Add(ccRecipient);
                }

                using var smtp = new SmtpClient(notifyRequest.MailServer, notifyRequest.MailPort)
                {
                    EnableSsl = true,
                    Credentials = new NetworkCredential(notifyRequest.SenderEmail, notifyRequest.Password)
                };

                await smtp.SendMailAsync(mail);
                _logger.LogInformation(
                    "Notification email sent for Material Request {RequestNo} to {RecipientEmail}.",
                    requestNo,
                    recipient.Email);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot send workflow email for Material Request {RequestNo}.", requestNo);
        }
    }

    // Ap dung tien to subject tu EmailSettings khi FunctionID cua Material Request nam trong danh sach test.
    private string ApplyMailSubjectPrefix(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return subject;
        }

        var prefix = _config.GetValue<string>("EmailSettings:PrefixSubject")?.Trim();
        if (string.IsNullOrWhiteSpace(prefix) || !ShouldApplyTestSubjectPrefix())
        {
            return subject;
        }

        return $"{prefix} - {subject}";
    }

    // Kiem tra FunctionID cua Material Request co nam trong danh sach test hay khong.
    private bool ShouldApplyTestSubjectPrefix()
    {
        var configuredIds = _config.GetValue<string>("EmailSettings:TestFunctionIDs");
        if (string.IsNullOrWhiteSpace(configuredIds))
        {
            return false;
        }

        return configuredIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(value => int.TryParse(value, out var id) && id == MaterialRequestFunctionId);
    }

    
    /// <summary>
    /// Resolve buoc workflow tiep theo tu action + trang thai hien tai.
    /// </summary>
    private static MaterialRequestWorkflowTransition? ResolveTransition(
        MaterialRequestWorkflowAction action,
        int currentStatus,
        bool isAuto,
        bool submitAsHeadDept)
    {
        if (action == MaterialRequestWorkflowAction.Submit && currentStatus == StatusJustCreated)
        {
            // Head Dept submit is treated as an immediate head approval and skips status 0.
            if (submitAsHeadDept)
            {
                return new MaterialRequestWorkflowTransition(
                        StatusHeadDeptApproved,
                        "Submitted successfully. Head Dept approval completed.",
                        true,
                        null,
                        null);
            }

            return new MaterialRequestWorkflowTransition(
                    StatusSubmittedToHead,
                    "Submitted successfully. Waiting for Head Dept approval.",
                    false,
                    false,
                    false);
        }

        if (action == MaterialRequestWorkflowAction.Approve && currentStatus == StatusSubmittedToHead)
        {
            return new MaterialRequestWorkflowTransition(
                    StatusHeadDeptApproved,
                    "Approved by Head Dept.",
                    true,
                    null,
                    null);
        }

        if (action == MaterialRequestWorkflowAction.Approve && currentStatus == StatusHeadDeptApproved)
        {
            return new MaterialRequestWorkflowTransition(
                    StatusPurchaserChecked,
                    "Checked by Purchaser.",
                    null,
                    null,
                    null);
        }

        if (action == MaterialRequestWorkflowAction.Approve && currentStatus == StatusPurchaserChecked)
        {
            if (isAuto)
            {
                return null;
            }

            return new MaterialRequestWorkflowTransition(
                    StatusCfoApproved,
                    "Approved by CFO.",
                    null,
                    null,
                    null);
        }

        if (action == MaterialRequestWorkflowAction.Approve && currentStatus == StatusCfoApproved)
        {
            return new MaterialRequestWorkflowTransition(
                    StatusCollectedToPr,
                    "Collected to PR.",
                    null,
                    true,
                    true);
        }

        if (action == MaterialRequestWorkflowAction.Issue && currentStatus == StatusCollectedToPr)
        {
            return new MaterialRequestWorkflowTransition(
                    StatusIssued,
                    "Material Request issued.",
                    null,
                    null,
                    null);
        }

        if (action == MaterialRequestWorkflowAction.Reject &&
            (currentStatus == StatusSubmittedToHead ||
             currentStatus == StatusHeadDeptApproved ||
             (currentStatus == StatusPurchaserChecked && !isAuto) ||
             currentStatus == StatusCfoApproved))
        {
            return new MaterialRequestWorkflowTransition(
                    StatusRejected,
                    "Material Request rejected.",
                    null,
                    true,
                    false);
        }

        return null;
    }

    private object BuildDetailRoute(long? requestNo)
    {
        return new
        {
            id = requestNo,
            mode = ResolveDetailMode(),
            returnUrl = ReturnUrl
        };
    }

    private string ResolveDetailMode()
    {
        var mode = Request.Query["mode"].ToString();
        if (!string.IsNullOrWhiteSpace(mode))
        {
            return mode;
        }

        return IsEdit ? "edit" : "add";
    }

    private string ResolveBackToListUrl()
    {
        var defaultUrl = Url.Page("./Index") ?? "/Purchasing/MaterialRequest/Index";
        if (string.IsNullOrWhiteSpace(ReturnUrl))
        {
            return defaultUrl;
        }

        var trimmed = ReturnUrl.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("//", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("\\\\", StringComparison.OrdinalIgnoreCase))
        {
            return defaultUrl;
        }

        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            return defaultUrl;
        }

        return trimmed;
    }

    private void ApplyTempDataMessagesToModelState()
    {
        if (!string.IsNullOrWhiteSpace(ErrorMessage))
        {
            ModelState.AddModelError(string.Empty, ErrorMessage);
        }

        if (!string.IsNullOrWhiteSpace(WarningMessage))
        {
            ModelState.AddModelError(string.Empty, WarningMessage);
        }
    }

}

internal enum MaterialRequestWorkflowAction
{
    Submit,
    Approve,
    Issue,
    Reject
}

internal sealed class MaterialRequestWorkflowTransition
{
    public int NextStatusId { get; set; }
    public string SuccessMessage { get; set; }
    public bool? Approval { get; set; }
    public bool? ApprovalEnd { get; set; }
    public bool? PostPr { get; set; }

    public MaterialRequestWorkflowTransition(
        int nextStatusId,
        string successMessage,
        bool? approval,
        bool? approvalEnd,
        bool? postPr)
    {
        NextStatusId = nextStatusId;
        SuccessMessage = successMessage;
        Approval = approval;
        ApprovalEnd = approvalEnd;
        PostPr = postPr;
    }
}




internal sealed class MaterialRequestWorkflowNotifyRequestViewModel
{
    public string SenderEmail { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MailServer { get; set; } = string.Empty;
    public int MailPort { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
    public List<MaterialRequestWorkflowRecipientViewModel> Recipients { get; set; } = new List<MaterialRequestWorkflowRecipientViewModel>();
    public List<string> CcRecipients { get; set; } = new List<string>();
}

internal sealed class MaterialRequestWorkflowRecipientViewModel
{
    public string Email { get; set; } = string.Empty;
    public string GreetingLabel { get; set; } = string.Empty;
}
