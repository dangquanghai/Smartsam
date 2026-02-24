using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using SmartSam.Models.Purchasing.Supplier;
using SmartSam.Services;
using SmartSam.Services.Purchasing.Supplier.Abstractions;

namespace SmartSam.Pages.Purchasing.Supplier;

public class DetailModel : PageModel
{
    private readonly ISupplierService _supplierService;
    private readonly PermissionService _permissionService;
    private const int SupplierFunctionId = 71;

    public DetailModel(ISupplierService supplierService, PermissionService permissionService)
    {
        _supplierService = supplierService;
        _permissionService = permissionService;
    }

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Msg { get; set; }

    [BindProperty]
    public SupplierDetailDto Input { get; set; } = new();

    public List<SelectListItem> Departments { get; set; } = [];
    public List<SelectListItem> Statuses { get; set; } = [];

    public List<SupplierApprovalHistoryDto> Histories { get; set; } = [];

    public string? Message { get; set; }
    public string MessageType { get; set; } = "info";

    public bool IsEdit => Id.HasValue && Id.Value > 0;
    public PagePermissions PagePerm { get; private set; } = new();
    public bool CanSave => !Input.IsDeleted && (IsEdit ? HasPermission(3) : HasPermission(2));
    public bool IsSubmitted => (Input.Status ?? 0) == 1;
    public bool CanSubmit => IsEdit && !Input.IsDeleted && HasPermission(4) && !IsSubmitted;
    private static readonly HashSet<int> CreateAllowedStatuses = [0, 1];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        if (IsEdit && !HasPermission(1))
        {
            return Forbid();
        }

        if (!IsEdit && !HasPermission(2))
        {
            return Forbid();
        }

        await LoadDropdownsAsync(cancellationToken);

        if (!IsEdit)
        {
            return Page();
        }

        Input = await _supplierService.GetDetailAsync(Id!.Value, cancellationToken) ?? new SupplierDetailDto();
        Histories = (await _supplierService.GetApprovalHistoryAsync(Id.Value, cancellationToken)).ToList();
        if (Input.IsDeleted)
        {
            SetMessage("This supplier has been deleted. View only mode is applied.", "warning");
        }
        if (string.Equals(Msg, "submitted", StringComparison.OrdinalIgnoreCase))
        {
            SetMessage("Supplier submitted successfully.", "success");
        }
        else if (string.Equals(Msg, "created", StringComparison.OrdinalIgnoreCase))
        {
            SetMessage("Supplier created successfully.", "success");
        }
        return Page();
    }

    public async Task<IActionResult> OnGetCheckSupplierCodeAsync(string? supplierCode, int? id, CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        var canAccess = (id.HasValue && id.Value > 0) ? HasPermission(1) : HasPermission(2);
        if (!canAccess)
        {
            return new JsonResult(new { ok = false, message = "Forbidden" }) { StatusCode = StatusCodes.Status403Forbidden };
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
        if (!HasPermission(2))
        {
            return new JsonResult(new { ok = false, message = "Forbidden" }) { StatusCode = StatusCodes.Status403Forbidden };
        }

        var suggestion = await _supplierService.GetSuggestedSupplierCodeAsync(cancellationToken);
        return new JsonResult(new { ok = true, supplierCode = suggestion });
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        if ((IsEdit && !HasPermission(3)) || (!IsEdit && !HasPermission(2)))
        {
            return Forbid();
        }

        await LoadDropdownsAsync(cancellationToken);

        if (IsEdit)
        {
            var current = await _supplierService.GetDetailAsync(Id!.Value, cancellationToken);
            if (current is null)
            {
                SetMessage("Supplier not found.", "error");
                return Page();
            }

            if (current.IsDeleted)
            {
                Input = current;
                Histories = (await _supplierService.GetApprovalHistoryAsync(Id.Value, cancellationToken)).ToList();
                SetMessage("Cannot save a deleted supplier.", "warning");
                return Page();
            }
        }

        if (!IsEdit)
        {
            Input.Status ??= 0;
        }

        if (!ModelState.IsValid)
        {
            if (IsEdit)
            {
                Histories = (await _supplierService.GetApprovalHistoryAsync(Id!.Value, cancellationToken)).ToList();
            }

            return Page();
        }

        if (!IsEdit && Input.Status.HasValue && !CreateAllowedStatuses.Contains(Input.Status.Value))
        {
            ModelState.AddModelError("Input.Status", "New supplier only allows Draft or Purchaser Submitted status.");
            return Page();
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

            if (IsEdit)
            {
                Histories = (await _supplierService.GetApprovalHistoryAsync(Id!.Value, cancellationToken)).ToList();
            }

            SetMessage(ex.Message, "error");
            return Page();
        }
        catch (SqlException ex)
        {
            if (IsEdit)
            {
                Histories = (await _supplierService.GetApprovalHistoryAsync(Id!.Value, cancellationToken)).ToList();
            }

            var friendlyMessage = ex.Number == 515 && ex.Message.Contains("SupplierName", StringComparison.OrdinalIgnoreCase)
                ? "Cannot save this supplier. Supplier Name is required. The current record may be incomplete (for example, a deleted test record)."
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

        SetMessage("Saved successfully.", "success");
        Input = await _supplierService.GetDetailAsync(savedId, cancellationToken) ?? new SupplierDetailDto();
        Histories = (await _supplierService.GetApprovalHistoryAsync(savedId, cancellationToken)).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostSubmitApprovalAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        if (!HasPermission(4))
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
        if (Input.IsDeleted)
        {
            Histories = (await _supplierService.GetApprovalHistoryAsync(Id.Value, cancellationToken)).ToList();
            SetMessage("Cannot submit a deleted supplier.", "warning");
            return Page();
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

        Departments = departments.Select(x => new SelectListItem
        {
            Value = x.Id.ToString(),
            Text = x.CodeOrName
        }).ToList();

        Statuses = statuses.Select(x => new SelectListItem
        {
            Value = x.Id.ToString(),
            Text = x.CodeOrName
        }).ToList();

        if (!IsEdit)
        {
            Statuses = Statuses.Where(x => int.TryParse(x.Value, out var id) && CreateAllowedStatuses.Contains(id)).ToList();
        }
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

    private void SetMessage(string message, string type = "info")
    {
        Message = message;
        MessageType = type;
    }
}
