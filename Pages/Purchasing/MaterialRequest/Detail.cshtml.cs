using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using SmartSam.Services;

namespace SmartSam.Pages.Purchasing.MaterialRequest;

public class DetailModel : PageModel
{
    private readonly MaterialRequestService _materialRequestService;
    private readonly PermissionService _permissionService;

    private const int MaterialRequestFunctionId = 104;
    private const int PermissionCreate = 1;
    private const int PermissionEdit = 3;
    private const int PermissionShowAll = 4;
    private const int PermissionShowCreate = 7;
    private const int StatusJustCreated = 0;
    private const int StatusSubmittedByOwner = 1;
    private const int StatusHeadDeptApproved = 2;
    private const int StatusPurchaserChecked = 3;
    private const int StatusCompleted = 4;
    private const int StatusRejected = 5;

    private EmployeeMaterialScopeDto _dataScope = new();
    private bool _isAdminRole;

    public DetailModel(IConfiguration configuration, PermissionService permissionService)
    {
        _materialRequestService = new MaterialRequestService(configuration);
        _permissionService = permissionService;
    }

    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty]
    public MaterialRequestDetailDto Input { get; set; } = new();

    [BindProperty]
    public string? LinesJson { get; set; }

    [BindProperty]
    public List<MaterialRequestLineDto> Lines { get; set; } = [];

    [TempData]
    public string? FlashMessage { get; set; }

    [TempData]
    public string? FlashMessageType { get; set; }

    public string? Message { get; set; }
    public string MessageType { get; set; } = "info";

    public PagePermissions PagePerm { get; private set; } = new();
    public List<SelectListItem> StoreGroups { get; set; } = [];
    public List<SelectListItem> Statuses { get; set; } = [];

    public bool IsEdit => Id.HasValue && Id.Value > 0;
    public bool CanShowAll => _isAdminRole || HasPermission(PermissionShowAll);
    public bool CanSave => IsEdit ? HasPermission(PermissionEdit) : (HasPermission(PermissionCreate) || HasPermission(PermissionShowCreate));
    public bool StoreGroupLocked => !CanShowAll;
    public int CurrentStatusId => Input.MaterialStatusId ?? StatusJustCreated;
    public bool CanSubmit => IsEdit && CanSave && CurrentStatusId == StatusJustCreated;
    public bool CanApprove => IsEdit && CanSave && CanApproveStatus(CurrentStatusId);
    public bool CanReject => IsEdit && CanSave && CanRejectStatus(CurrentStatusId);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        await LoadUserScopeAsync(cancellationToken);
        if (!CanAccessPage())
        {
            return Forbid();
        }

        await LoadDropdownsAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(Message) && !string.IsNullOrWhiteSpace(FlashMessage))
        {
            Message = FlashMessage;
            MessageType = string.IsNullOrWhiteSpace(FlashMessageType) ? "info" : FlashMessageType!;
        }

        if (!IsEdit)
        {
            if (StoreGroupLocked)
            {
                Input.StoreGroup = _dataScope.StoreGroup;
            }

            Input.DateCreate = DateTime.Today;
            Input.FromDate = DateTime.Today.AddMonths(-3);
            Input.ToDate = DateTime.Today;
            Input.MaterialStatusId ??= 0;
            return Page();
        }

        var detail = await _materialRequestService.GetDetailAsync(Id!.Value, cancellationToken);
        if (detail is null)
        {
            FlashMessage = "Material Request not found.";
            FlashMessageType = "error";
            return RedirectToPage("./Index");
        }

        if (StoreGroupLocked && _dataScope.StoreGroup.HasValue && detail.StoreGroup != _dataScope.StoreGroup)
        {
            return Forbid();
        }

        Input = detail;
        Lines = (await _materialRequestService.GetLinesAsync(Id.Value, cancellationToken)).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        await LoadUserScopeAsync(cancellationToken);
        if (!CanAccessPage())
        {
            return Forbid();
        }

        if (!CanSave)
        {
            return Forbid();
        }

        await LoadDropdownsAsync(cancellationToken);

        if (StoreGroupLocked)
        {
            Input.StoreGroup = _dataScope.StoreGroup;
        }

        MaterialRequestDetailDto? existing = null;
        if (IsEdit)
        {
            existing = await _materialRequestService.GetDetailAsync(Id!.Value, cancellationToken);
            if (existing is null)
            {
                FlashMessage = "Material Request not found.";
                FlashMessageType = "error";
                return RedirectToPage("./Index");
            }

            // Giá»¯ nguyÃªn tráº¡ng thÃ¡i workflow khi ngÆ°á»i dÃ¹ng chá»‰ save cÃ¡c trÆ°á»ng form.
            Input.MaterialStatusId = existing.MaterialStatusId;
            Input.Approval = existing.Approval;
            Input.ApprovalEnd = existing.ApprovalEnd;
            Input.PostPr = existing.PostPr;
            Input.FromDate ??= existing.FromDate;
            Input.ToDate ??= existing.ToDate;
        }

        if (Input.FromDate.HasValue && Input.ToDate.HasValue && Input.FromDate.Value.Date > Input.ToDate.Value.Date)
        {
            ModelState.AddModelError("Input.ToDate", "To Date must be greater than or equal to From Date.");
        }

        var lines = ResolvePostedLines();

        if (lines.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "At least one item line is required.");
        }

        ValidateLines(lines);

        if (!ModelState.IsValid)
        {
            Lines = lines;
            return Page();
        }

        try
        {
            var savedNo = await _materialRequestService.SaveAsync(IsEdit ? Id : null, Input, lines, cancellationToken);
            FlashMessage = IsEdit ? "Saved successfully." : "Material Request created successfully.";
            FlashMessageType = "success";
            return RedirectToPage("./Detail", new { id = savedNo });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MaterialRequest][Save] {ex}");
            Lines = lines;
            SetMessage(ex is InvalidOperationException ? ex.Message : $"Cannot save Material Request. {ex.Message}", "error");
            return Page();
        }
    }

    public Task<IActionResult> OnPostSubmitAsync(CancellationToken cancellationToken)
        => HandleWorkflowActionAsync(MaterialRequestWorkflowAction.Submit, cancellationToken);

    public Task<IActionResult> OnPostApproveAsync(CancellationToken cancellationToken)
        => HandleWorkflowActionAsync(MaterialRequestWorkflowAction.Approve, cancellationToken);

    public Task<IActionResult> OnPostRejectAsync(CancellationToken cancellationToken)
        => HandleWorkflowActionAsync(MaterialRequestWorkflowAction.Reject, cancellationToken);

    private async Task<IActionResult> HandleWorkflowActionAsync(MaterialRequestWorkflowAction action, CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        await LoadUserScopeAsync(cancellationToken);
        if (!CanAccessPage())
        {
            return Forbid();
        }

        if (!CanSave)
        {
            return Forbid();
        }

        if (!IsEdit)
        {
            FlashMessage = "Please save Material Request first.";
            FlashMessageType = "warning";
            return RedirectToPage("./Detail");
        }

        await LoadDropdownsAsync(cancellationToken);

        if (StoreGroupLocked)
        {
            Input.StoreGroup = _dataScope.StoreGroup;
        }

        var existing = await _materialRequestService.GetDetailAsync(Id!.Value, cancellationToken);
        if (existing is null)
        {
            FlashMessage = "Material Request not found.";
            FlashMessageType = "error";
            return RedirectToPage("./Index");
        }

        var lines = ResolvePostedLines();
        if (lines.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "At least one item line is required.");
        }
        ValidateLines(lines);

        if (!ModelState.IsValid)
        {
            Input = MergeEditableInput(existing, Input);
            Lines = lines;
            return Page();
        }

        var currentStatus = existing.MaterialStatusId ?? StatusJustCreated;
        var transition = ResolveTransition(action, currentStatus);
        if (transition is null)
        {
            SetMessage("Invalid workflow action for current status.", "warning");
            Input = MergeEditableInput(existing, Input);
            Lines = lines;
            return Page();
        }

        if (!CanExecuteTransition(action, currentStatus))
        {
            SetMessage("You do not have permission for this workflow action.", "warning");
            Input = MergeEditableInput(existing, Input);
            Lines = lines;
            return Page();
        }

        try
        {
            var toSave = MergeEditableInput(existing, Input);
            toSave.MaterialStatusId = transition.NextStatusId;
            if (transition.Approval.HasValue) toSave.Approval = transition.Approval.Value;
            if (transition.ApprovalEnd.HasValue) toSave.ApprovalEnd = transition.ApprovalEnd.Value;
            if (transition.PostPr.HasValue) toSave.PostPr = transition.PostPr.Value;

            var savedNo = await _materialRequestService.SaveAsync(Id, toSave, lines, cancellationToken);
            FlashMessage = transition.SuccessMessage;
            FlashMessageType = "success";
            return RedirectToPage("./Detail", new { id = savedNo });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MaterialRequest][WorkflowAction:{action}] {ex}");
            Input = MergeEditableInput(existing, Input);
            Lines = lines;
            SetMessage(ex is InvalidOperationException ? ex.Message : $"Cannot process workflow action. {ex.Message}", "error");
            return Page();
        }
    }

    public async Task<IActionResult> OnGetSearchItemsAsync(string? keyword, bool checkBalanceInStore = false, CancellationToken cancellationToken = default)
    {
        LoadPagePermissions();
        await LoadUserScopeAsync(cancellationToken);
        if (!CanAccessPage())
        {
            return new JsonResult(new { success = false, message = "Access denied." });
        }

        var rows = await _materialRequestService.SearchItemsAsync(keyword, checkBalanceInStore, Input.StoreGroup, cancellationToken);
        return new JsonResult(new { success = true, data = rows });
    }

    public async Task<IActionResult> OnPostCreateItemAsync([FromForm] string? itemName, [FromForm] string? unit, CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        await LoadUserScopeAsync(cancellationToken);
        if (!CanAccessPage() || !CanSave)
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

    private async Task LoadDropdownsAsync(CancellationToken cancellationToken)
    {
        var stores = await _materialRequestService.GetStoreGroupsAsync(cancellationToken);
        StoreGroups = [
            .. stores.Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.Name })
        ];

        var statuses = await _materialRequestService.GetStatusesAsync(cancellationToken);
        Statuses = [
            .. statuses.Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.Name })
        ];

        if (StoreGroupLocked)
        {
            StoreGroups = _dataScope.StoreGroup.HasValue
                ? [new SelectListItem { Value = _dataScope.StoreGroup.Value.ToString(), Text = $"Store Group {_dataScope.StoreGroup.Value}" }]
                : [new SelectListItem { Value = string.Empty, Text = "(No Store Group Scope)" }];
        }
    }

    private static List<MaterialRequestLineDto> ParseLinesFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<MaterialRequestLineDto>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return parsed ?? [];
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Ưu tiên lấy line từ JSON (được build bằng JS), nếu thiếu thì fallback sang model bind trực tiếp từ input Lines[i].*
    /// để tránh mất item khi user bấm Submit/Approve mà JSON không cập nhật kịp.
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

    private static List<MaterialRequestLineDto> NormalizeLines(IEnumerable<MaterialRequestLineDto> lines)
    {
        var result = new List<MaterialRequestLineDto>();
        foreach (var line in lines)
        {
            var itemCode = (line.ItemCode ?? string.Empty).Trim();
            var itemName = (line.ItemName ?? string.Empty).Trim();
            var unit = (line.Unit ?? string.Empty).Trim();
            var note = (line.Note ?? string.Empty).Trim();

            var isBlank = string.IsNullOrWhiteSpace(itemCode)
                && string.IsNullOrWhiteSpace(itemName)
                && string.IsNullOrWhiteSpace(unit)
                && string.IsNullOrWhiteSpace(note)
                && !(line.OrderQty.HasValue && line.OrderQty.Value != 0)
                && !(line.NotReceipt.HasValue && line.NotReceipt.Value != 0)
                && !(line.InStock.HasValue && line.InStock.Value != 0)
                && !(line.AccIn.HasValue && line.AccIn.Value != 0)
                && !(line.Buy.HasValue && line.Buy.Value != 0)
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
                Price = line.Price ?? 0,
                Note = note,
                NewItem = line.NewItem,
                Selected = true
            });
        }

        return result;
    }

    private void ValidateLines(IReadOnlyList<MaterialRequestLineDto> lines)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            var prefix = $"Line {i + 1}";

            if (string.IsNullOrWhiteSpace(line.ItemCode))
            {
                ModelState.AddModelError(string.Empty, $"{prefix}: Item Code is required.");
            }

            if (line.Buy.HasValue && line.Buy.Value < 0)
            {
                ModelState.AddModelError(string.Empty, $"{prefix}: Buy quantity must be greater than or equal to 0.");
            }

            if (line.OrderQty.HasValue && line.OrderQty.Value < 0)
            {
                ModelState.AddModelError(string.Empty, $"{prefix}: Order quantity must be greater than or equal to 0.");
            }

            if (line.NotReceipt.HasValue && line.NotReceipt.Value < 0)
            {
                ModelState.AddModelError(string.Empty, $"{prefix}: NotRec must be greater than or equal to 0.");
            }

            if (line.InStock.HasValue && line.InStock.Value < 0)
            {
                ModelState.AddModelError(string.Empty, $"{prefix}: In must be greater than or equal to 0.");
            }

            if (line.AccIn.HasValue && line.AccIn.Value < 0)
            {
                ModelState.AddModelError(string.Empty, $"{prefix}: Acc.In must be greater than or equal to 0.");
            }

            if (line.Price.HasValue && line.Price.Value < 0)
            {
                ModelState.AddModelError(string.Empty, $"{prefix}: Price must be greater than or equal to 0.");
            }

            var validationContext = new ValidationContext(line);
            var validationResults = new List<ValidationResult>();
            if (!Validator.TryValidateObject(line, validationContext, validationResults, validateAllProperties: true))
            {
                foreach (var vr in validationResults)
                {
                    ModelState.AddModelError(string.Empty, $"{prefix}: {vr.ErrorMessage}");
                }
            }
        }
    }

    private async Task LoadUserScopeAsync(CancellationToken cancellationToken)
    {
        _isAdminRole = string.Equals(User.FindFirst("IsAdminRole")?.Value, "True", StringComparison.OrdinalIgnoreCase);
        if (_isAdminRole)
        {
            _dataScope = new EmployeeMaterialScopeDto();
            return;
        }

        _dataScope = await _materialRequestService.GetEmployeeScopeAsync(User.Identity?.Name, cancellationToken);
    }

    private void LoadPagePermissions()
    {
        var isAdmin = string.Equals(User.FindFirst("IsAdminRole")?.Value, "True", StringComparison.OrdinalIgnoreCase);
        if (isAdmin)
        {
            PagePerm.AllowedNos = Enumerable.Range(1, 10).ToList();
            return;
        }

        if (!int.TryParse(User.FindFirst("RoleID")?.Value, out var roleId))
        {
            roleId = 0;
        }

        PagePerm.AllowedNos = _permissionService.GetPermissionsForPage(roleId, MaterialRequestFunctionId);
    }

    private bool CanAccessPage() => _isAdminRole || PagePerm.AllowedNos.Count > 0;
    private bool HasPermission(int permissionNo) => PagePerm.HasPermission(permissionNo);

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

    private bool CanApproveStatus(int statusId)
    {
        if (_isAdminRole) return true;

        return statusId switch
        {
            StatusSubmittedByOwner => _dataScope.IsHeadDept || _dataScope.ApprovalLevel >= 2,
            StatusHeadDeptApproved => _dataScope.ApprovalLevel >= 2,
            StatusPurchaserChecked => _dataScope.ApprovalLevel >= 3,
            _ => false
        };
    }

    private bool CanRejectStatus(int statusId)
    {
        if (_isAdminRole) return true;
        if (statusId is not (StatusSubmittedByOwner or StatusHeadDeptApproved or StatusPurchaserChecked))
        {
            return false;
        }

        return _dataScope.IsHeadDept || _dataScope.ApprovalLevel >= 2;
    }

    private bool CanExecuteTransition(MaterialRequestWorkflowAction action, int currentStatus)
    {
        return action switch
        {
            MaterialRequestWorkflowAction.Submit => currentStatus == StatusJustCreated,
            MaterialRequestWorkflowAction.Approve => CanApproveStatus(currentStatus),
            MaterialRequestWorkflowAction.Reject => CanRejectStatus(currentStatus),
            _ => false
        };
    }

    private static MaterialRequestWorkflowTransition? ResolveTransition(MaterialRequestWorkflowAction action, int currentStatus)
    {
        return action switch
        {
            MaterialRequestWorkflowAction.Submit when currentStatus == StatusJustCreated
                => new MaterialRequestWorkflowTransition(
                    StatusSubmittedByOwner,
                    "Submitted successfully. Waiting for Head Dept approval.",
                    Approval: false,
                    ApprovalEnd: false,
                    PostPr: false),

            MaterialRequestWorkflowAction.Approve when currentStatus == StatusSubmittedByOwner
                => new MaterialRequestWorkflowTransition(
                    StatusHeadDeptApproved,
                    "Approved by Head Dept.",
                    Approval: true),

            MaterialRequestWorkflowAction.Approve when currentStatus == StatusHeadDeptApproved
                => new MaterialRequestWorkflowTransition(
                    StatusPurchaserChecked,
                    "Checked by Purchaser."),

            MaterialRequestWorkflowAction.Approve when currentStatus == StatusPurchaserChecked
                => new MaterialRequestWorkflowTransition(
                    StatusCompleted,
                    "Approved by Chief of Accounting. MR completed.",
                    ApprovalEnd: true,
                    PostPr: true),

            MaterialRequestWorkflowAction.Reject when currentStatus is StatusSubmittedByOwner or StatusHeadDeptApproved or StatusPurchaserChecked
                => new MaterialRequestWorkflowTransition(
                    StatusRejected,
                    "Material Request rejected.",
                    ApprovalEnd: true,
                    PostPr: false),

            _ => null
        };
    }

    private void SetMessage(string message, string type = "info")
    {
        Message = message;
        MessageType = type;
    }
}

internal enum MaterialRequestWorkflowAction
{
    Submit,
    Approve,
    Reject
}

internal sealed record MaterialRequestWorkflowTransition(
    int NextStatusId,
    string SuccessMessage,
    bool? Approval = null,
    bool? ApprovalEnd = null,
    bool? PostPr = null);
