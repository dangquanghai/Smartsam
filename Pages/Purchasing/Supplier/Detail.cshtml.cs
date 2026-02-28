using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using SmartSam.Services;

namespace SmartSam.Pages.Purchasing.Supplier;

public class DetailModel : PageModel
{
    private readonly SupplierService _supplierService;
    private readonly PermissionService _permissionService;
    private const int SupplierFunctionId = 71;
    private const int PermissionAdd = 2;
    private const int PermissionEdit = 3;
    private const int PermissionSubmit = 4;
    private const int PermissionViewDetail = 6;
    private EmployeeDataScopeDto _dataScope = new();
    private bool _isAdminRole;

    public DetailModel(IConfiguration configuration, PermissionService permissionService)
    {
        _supplierService = new SupplierService(configuration);
        _permissionService = permissionService;
    }

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Msg { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ViewMode { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    [BindProperty]
    public SupplierDetailDto Input { get; set; } = new();

    public List<SelectListItem> Departments { get; set; } = [];
    public List<SelectListItem> Statuses { get; set; } = [];

    public List<SupplierApprovalHistoryDto> Histories { get; set; } = [];

    public string? Message { get; set; }
    public string MessageType { get; set; } = "info";

    [TempData]
    public string? FlashMessage { get; set; }

    [TempData]
    public string? FlashMessageType { get; set; }

    public bool IsEdit => Id.HasValue && Id.Value > 0;
    public bool IsAnnualView => string.Equals(ViewMode, "byyear", StringComparison.OrdinalIgnoreCase) && Year.HasValue;
    public PagePermissions PagePerm { get; private set; } = new();
    public bool CanSave => !IsAnnualView && (IsEdit ? HasPermission(PermissionEdit) : HasPermission(PermissionAdd));
    public bool IsSubmitted => (Input.Status ?? 0) == 1;
    public bool CanSubmit => !IsAnnualView && IsEdit && HasPermission(PermissionSubmit) && !IsSubmitted;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        await LoadUserDataScopeAsync(cancellationToken);
        if (IsEdit && !HasPermission(PermissionViewDetail))
        {
            return Forbid();
        }

        if (!IsEdit && !HasPermission(PermissionAdd))
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
            Input.Status = 0;
            if (!_isAdminRole && !_dataScope.SeeDataAllDept)
            {
                if (!_dataScope.DeptID.HasValue)
                {
                    return Forbid();
                }

                Input.DeptID = _dataScope.DeptID.Value;
            }
            return Page();
        }

        if (IsAnnualView)
        {
            Input = await _supplierService.GetAnnualDetailAsync(Id!.Value, Year!.Value, cancellationToken) ?? new SupplierDetailDto();
            if (!CanAccessDepartment(Input.DeptID))
            {
                return Forbid();
            }
            Histories = (await _supplierService.GetAnnualApprovalHistoryAsync(Id.Value, Year.Value, cancellationToken)).ToList();
        }
        else
        {
            Input = await _supplierService.GetDetailAsync(Id!.Value, cancellationToken) ?? new SupplierDetailDto();
            if (!CanAccessDepartment(Input.DeptID))
            {
                return Forbid();
            }
            Histories = (await _supplierService.GetApprovalHistoryAsync(Id.Value, cancellationToken)).ToList();
        }
        if (string.Equals(Msg, "submitted", StringComparison.OrdinalIgnoreCase))
        {
            SetMessage("Supplier submitted successfully.", "success");
        }
        else if (string.Equals(Msg, "created", StringComparison.OrdinalIgnoreCase))
        {
            SetMessage("Supplier created successfully.", "success");
        }
        else if (string.Equals(Msg, "saved", StringComparison.OrdinalIgnoreCase))
        {
            SetMessage("Saved successfully.", "success");
        }
        return Page();
    }

    public async Task<IActionResult> OnGetCheckSupplierCodeAsync(string? supplierCode, int? id, CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        await LoadUserDataScopeAsync(cancellationToken);
        var canAccess = (id.HasValue && id.Value > 0) ? HasPermission(PermissionEdit) : HasPermission(PermissionAdd);
        if (!canAccess)
        {
            return new JsonResult(new { ok = false, message = "Forbidden" }) { StatusCode = StatusCodes.Status403Forbidden };
        }

        if (id.HasValue && id.Value > 0)
        {
            var supplierDept = await _supplierService.GetSupplierDepartmentAsync(id.Value, "current", null, cancellationToken);
            if (!CanAccessDepartment(supplierDept))
            {
                return new JsonResult(new { ok = false, message = "Forbidden" }) { StatusCode = StatusCodes.Status403Forbidden };
            }
        }

        var normalized = (supplierCode ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return new JsonResult(new { ok = true, exists = false });
        }

        var exists = await _supplierService.SupplierCodeExistsAsync(normalized, id, cancellationToken);
        return new JsonResult(new { ok = true, exists });
    }

    public async Task<IActionResult> OnGetSuggestSupplierCodeAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        await LoadUserDataScopeAsync(cancellationToken);
        if (!HasPermission(PermissionAdd))
        {
            return new JsonResult(new { ok = false, message = "Forbidden" }) { StatusCode = StatusCodes.Status403Forbidden };
        }

        var suggestion = await _supplierService.GetSuggestedSupplierCodeAsync(cancellationToken);
        return new JsonResult(new { ok = true, supplierCode = suggestion });
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        await LoadUserDataScopeAsync(cancellationToken);
        if (IsAnnualView)
        {
            return Forbid();
        }
        if ((IsEdit && !HasPermission(PermissionEdit)) || (!IsEdit && !HasPermission(PermissionAdd)))
        {
            return Forbid();
        }

        await LoadDropdownsAsync(cancellationToken);
        SupplierDetailDto? currentDetail = null;

        if (!IsEdit)
        {
            if (!_isAdminRole && !_dataScope.SeeDataAllDept)
            {
                if (!_dataScope.DeptID.HasValue)
                {
                    return Forbid();
                }

                Input.DeptID = _dataScope.DeptID.Value;
            }

            Input.Status = 0; // New supplier is always Draft. Status changes only via workflow actions.
        }
        else
        {
            currentDetail = await _supplierService.GetDetailAsync(Id!.Value, cancellationToken);
            if (currentDetail is null)
            {
                SetMessage("Supplier not found.", "error");
                return Page();
            }

            if (!CanAccessDepartment(currentDetail.DeptID))
            {
                return Forbid();
            }

            if (!_isAdminRole && !_dataScope.SeeDataAllDept)
            {
                Input.DeptID = currentDetail.DeptID;
            }

            Input.Status = currentDetail.Status;
        }

        if (!ModelState.IsValid)
        {
            if (IsEdit)
            {
                Histories = (await _supplierService.GetApprovalHistoryAsync(Id!.Value, cancellationToken)).ToList();
            }

            return Page();
        }

        if (IsEdit)
        {
            // Preserve current workflow status. Save action must not change status directly.
            Input.Status = currentDetail?.Status;
        }

        var operatorCode = User.Identity?.Name ?? "SYSTEM";
        int savedId;
        try
        {
            savedId = await _supplierService.SaveAsync(Id, Input, operatorCode, cancellationToken);
        }
        catch (InvalidOperationException ex)
        {
            if (string.Equals(ex.Message, "Supplier code already exists.", StringComparison.OrdinalIgnoreCase))
            {
                ModelState.AddModelError("Input.SupplierCode", ex.Message);
            }
            else
            {
                SetMessage(ex.Message, "error");
            }

            if (IsEdit)
            {
                Histories = (await _supplierService.GetApprovalHistoryAsync(Id!.Value, cancellationToken)).ToList();
            }

            return Page();
        }
        catch (SqlException ex)
        {
            if (IsEdit)
            {
                Histories = (await _supplierService.GetApprovalHistoryAsync(Id!.Value, cancellationToken)).ToList();
            }

            var friendlyMessage = ex.Number == 515 && ex.Message.Contains("SupplierName", StringComparison.OrdinalIgnoreCase)
                ? "Cannot save this supplier. Supplier Name is required."
                : "Cannot save supplier due to database validation rules. Please review the form data and try again.";

            SetMessage(friendlyMessage, "error");
            return Page();
        }
        catch (Exception)
        {
            if (IsEdit)
            {
                Histories = (await _supplierService.GetApprovalHistoryAsync(Id!.Value, cancellationToken)).ToList();
            }

            SetMessage("An unexpected error occurred while saving supplier. Please try again.", "error");
            return Page();
        }

        if (!IsEdit)
        {
            return RedirectToPage("./Detail", new { id = savedId, msg = "created" });
        }

        SetFlashMessage("Saved successfully.", "success");
        return RedirectToPage("./Detail", new { id = savedId, msg = "saved", viewMode = ViewMode, year = Year });
    }

    public async Task<IActionResult> OnPostSubmitApprovalAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        await LoadUserDataScopeAsync(cancellationToken);
        if (IsAnnualView)
        {
            return Forbid();
        }
        if (!HasPermission(PermissionSubmit))
        {
            return Forbid();
        }

        if (!IsEdit)
        {
            return RedirectToPage("./Index");
        }

        await LoadDropdownsAsync(cancellationToken);

        // Re-check current status from DB to prevent re-submitting via forged request.
        Input = await _supplierService.GetDetailAsync(Id!.Value, cancellationToken) ?? new SupplierDetailDto();
        if (!CanAccessDepartment(Input.DeptID))
        {
            return Forbid();
        }

        if (IsSubmitted)
        {
            Histories = (await _supplierService.GetApprovalHistoryAsync(Id.Value, cancellationToken)).ToList();
            SetMessage("Supplier has already been submitted.", "warning");
            return Page();
        }

        var operatorCode = User.Identity?.Name ?? "SYSTEM";
        await _supplierService.SubmitApprovalAsync(Id!.Value, operatorCode, cancellationToken);

        return RedirectToPage("./Detail", new { id = Id, msg = "submitted" });
    }

    private async Task LoadDropdownsAsync(CancellationToken cancellationToken)
    {
        var departments = await _supplierService.GetDepartmentsAsync(cancellationToken);
        var statuses = await _supplierService.GetStatusesAsync(cancellationToken);

        if (!_isAdminRole && !_dataScope.SeeDataAllDept)
        {
            if (!_dataScope.DeptID.HasValue)
            {
                Departments = [];
                Input.DeptID = null;
                return;
            }

            Departments = departments
                .Where(x => _dataScope.DeptID.HasValue && x.Id == _dataScope.DeptID.Value)
                .Select(x => new SelectListItem
                {
                    Value = x.Id.ToString(),
                    Text = x.CodeOrName
                }).ToList();

            Input.DeptID = _dataScope.DeptID.Value;
        }
        else
        {
            Departments = departments.Select(x => new SelectListItem
            {
                Value = x.Id.ToString(),
                Text = x.CodeOrName
            }).ToList();
        }

        Statuses = statuses.Select(x => new SelectListItem
        {
            Value = x.Id.ToString(),
            Text = x.CodeOrName
        }).ToList();

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

        PagePerm.AllowedNos = _permissionService.GetPermissionsForPage(roleId, SupplierFunctionId);
    }

    private bool HasPermission(int permissionNo) => PagePerm.HasPermission(permissionNo);

    private async Task LoadUserDataScopeAsync(CancellationToken cancellationToken)
    {
        _isAdminRole = string.Equals(User.FindFirst("IsAdminRole")?.Value, "True", StringComparison.OrdinalIgnoreCase);
        if (_isAdminRole)
        {
            _dataScope = new EmployeeDataScopeDto { SeeDataAllDept = true };
            return;
        }

        _dataScope = await _supplierService.GetEmployeeDataScopeAsync(User.Identity?.Name, cancellationToken);
    }

    private bool CanAccessDepartment(int? supplierDeptId)
    {
        if (_isAdminRole || _dataScope.SeeDataAllDept)
        {
            return true;
        }

        if (!_dataScope.DeptID.HasValue || !supplierDeptId.HasValue)
        {
            return false;
        }

        return _dataScope.DeptID.Value == supplierDeptId.Value;
    }

    private void SetFlashMessage(string message, string type = "info")
    {
        FlashMessage = message;
        FlashMessageType = type;
    }

    private void SetMessage(string message, string type = "info")
    {
        Message = message;
        MessageType = type;
    }
}
