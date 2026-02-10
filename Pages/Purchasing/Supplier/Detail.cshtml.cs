using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using SmartSam.Models.Purchasing.Supplier;
using SmartSam.Services.Purchasing.Supplier.Abstractions;

namespace SmartSam.Pages.Purchasing.Supplier;

public class DetailModel : PageModel
{
    private readonly ISupplierService _supplierService;

    public DetailModel(ISupplierService supplierService)
    {
        _supplierService = supplierService;
    }

    [BindProperty(SupportsGet = true)]
    public int? Id { get; set; }

    [BindProperty]
    public SupplierDetailDto Input { get; set; } = new();

    public List<SelectListItem> Departments { get; set; } = [];
    public List<SelectListItem> Statuses { get; set; } = [];

    public List<SupplierApprovalHistoryDto> Histories { get; set; } = [];

    public string? Message { get; set; }

    public bool IsEdit => Id.HasValue && Id.Value > 0;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadDropdownsAsync(cancellationToken);

        if (!IsEdit)
        {
            return;
        }

        Input = await _supplierService.GetDetailAsync(Id!.Value, cancellationToken) ?? new SupplierDetailDto();
        Histories = (await _supplierService.GetApprovalHistoryAsync(Id.Value, cancellationToken)).ToList();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        await LoadDropdownsAsync(cancellationToken);

        if (!ModelState.IsValid)
        {
            if (IsEdit)
            {
                Histories = (await _supplierService.GetApprovalHistoryAsync(Id!.Value, cancellationToken)).ToList();
            }

            return Page();
        }

        var operatorCode = User.Identity?.Name ?? "SYSTEM";
        var savedId = await _supplierService.SaveAsync(Id, Input, operatorCode, cancellationToken);

        if (!IsEdit)
        {
            return RedirectToPage("./Detail", new { id = savedId });
        }

        Message = "Saved.";
        Input = await _supplierService.GetDetailAsync(savedId, cancellationToken) ?? new SupplierDetailDto();
        Histories = (await _supplierService.GetApprovalHistoryAsync(savedId, cancellationToken)).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostSubmitApprovalAsync(CancellationToken cancellationToken)
    {
        if (!IsEdit)
        {
            return RedirectToPage("./Index");
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
    }
}
