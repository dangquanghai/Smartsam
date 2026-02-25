using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using SmartSam.Models.Purchasing.Supplier;
using SmartSam.Services;
using SmartSam.Services.Purchasing.Supplier.Abstractions;

namespace SmartSam.Pages.Purchasing.Supplier;

public class IndexModel : PageModel
{
    private readonly ISupplierService _supplierService;
    private readonly PermissionService _permissionService;
    private const int SupplierFunctionId = 71;

    public IndexModel(ISupplierService supplierService, PermissionService permissionService)
    {
        _supplierService = supplierService;
        _permissionService = permissionService;
    }

    [BindProperty(SupportsGet = true)]
    public string? ViewMode { get; set; } = "current"; // current | byyear

    [BindProperty(SupportsGet = true)]
    public int? Year { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? DeptId { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SupplierCode { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? SupplierName { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Business { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Contact { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? StatusId { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool IsNew { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageIndex { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 25;

    [BindProperty]
    public int CopyYear { get; set; }

    [BindProperty]
    public bool ConfirmCopy { get; set; }

    [BindProperty]
    public int SelectedSupplierId { get; set; }

    [BindProperty]
    public string? SelectedSupplierIdsCsv { get; set; }

    [TempData]
    public string? FlashMessage { get; set; }

    [TempData]
    public string? FlashMessageType { get; set; }

    public string? Message { get; set; }
    public string MessageType { get; set; } = "info";

    public PagePermissions PagePerm { get; private set; } = new();

    public List<SelectListItem> Departments { get; set; } = [];
    public List<SelectListItem> Statuses { get; set; } = [];
    public List<SupplierListRowDto> Rows { get; set; } = [];
    public int TotalRecords { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)PageSize));
    public bool HasPreviousPage => PageIndex > 1;
    public bool HasNextPage => PageIndex < TotalPages;

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        if (!HasPermission(1))
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(Message) && !string.IsNullOrWhiteSpace(FlashMessage))
        {
            Message = FlashMessage;
            MessageType = string.IsNullOrWhiteSpace(FlashMessageType) ? "info" : FlashMessageType!;
        }

        await LoadFiltersAsync(cancellationToken);
        await LoadRowsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostCopyYearAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        if (!HasPermission(5))
        {
            return Forbid();
        }

        await LoadFiltersAsync(cancellationToken);

        var selectedIds = ParseSelectedSupplierIds();
        if (selectedIds.Count == 0)
        {
            SetFlashMessage("Select at least one supplier.", "warning");
            return RedirectToCurrentList();
        }

        var currentYear = DateTime.Today.Year;
        if (!ConfirmCopy || CopyYear < 2000 || CopyYear >= currentYear)
        {
            SetFlashMessage($"Enter a valid year before {currentYear} and confirm the copy.", "warning");
            return RedirectToCurrentList();
        }

        try
        {
            await _supplierService.CopyCurrentSuppliersToYearAsync(CopyYear, selectedIds, cancellationToken);
            SetFlashMessage("Copy completed.", "success");
        }
        catch (Exception)
        {
            SetFlashMessage("Copy failed.", "error");
        }
        return RedirectToCurrentList();
    }

    public async Task<IActionResult> OnGetExportCsvAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        if (!HasPermission(1))
        {
            return Forbid();
        }

        var rows = await _supplierService.SearchAsync(BuildCriteria(includePaging: false), cancellationToken);
        var lines = new List<string>
        {
            "SupplierCode,SupplierName,Address,Phone,Mobile,Fax,Contact,Position,Business,Status,DeptCode"
        };

        foreach (var r in rows)
        {
            lines.Add(string.Join(",",
                Csv(r.SupplierCode), Csv(r.SupplierName), Csv(r.Address), Csv(r.Phone),
                Csv(r.Mobile), Csv(r.Fax), Csv(r.Contact), Csv(r.Position),
                Csv(r.Business), Csv(r.SupplierStatusName), Csv(r.DeptCode)
            ));
        }

        var bytes = System.Text.Encoding.UTF8.GetBytes(string.Join("\n", lines));
        return File(bytes, "text/csv", "suppliers.csv");
    }

    public async Task<IActionResult> OnPostSubmitAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        if (!HasPermission(4))
        {
            return Forbid();
        }

        await LoadFiltersAsync(cancellationToken);

        var selectedIds = ParseSelectedSupplierIds();
        if (selectedIds.Count == 0)
        {
            SetFlashMessage("Select at least one supplier.", "warning");
            return RedirectToCurrentList();
        }

        var operatorCode = User.Identity?.Name ?? "SYSTEM";
        var successCount = 0;
        var alreadySubmittedCount = 0;
        var notFoundCount = 0;

        foreach (var supplierId in selectedIds)
        {
            var supplier = await _supplierService.GetDetailAsync(supplierId, cancellationToken);
            if (supplier is null)
            {
                notFoundCount++;
                continue;
            }

            if (supplier.Status == 1)
            {
                alreadySubmittedCount++;
                continue;
            }

            await _supplierService.SubmitApprovalAsync(supplierId, operatorCode, cancellationToken);
            successCount++;
        }

        SetFlashMessage(
            BuildSubmitMessage(successCount, alreadySubmittedCount, notFoundCount, selectedIds.Count),
            successCount > 0 ? "success" : (alreadySubmittedCount > 0 ? "warning" : "info"));
        return RedirectToCurrentList();
    }

    private async Task LoadFiltersAsync(CancellationToken cancellationToken)
    {
        var departments = await _supplierService.GetDepartmentsAsync(cancellationToken);
        var statuses = await _supplierService.GetStatusesAsync(cancellationToken);

        Departments = [
            new SelectListItem { Value = string.Empty, Text = "--- All ---" },
            .. departments.Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.CodeOrName })
        ];

        Statuses = [
            new SelectListItem { Value = string.Empty, Text = "--- All ---" },
            .. statuses.Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.CodeOrName })
        ];
    }

    private async Task LoadRowsAsync(CancellationToken cancellationToken)
    {
        if (PageSize <= 0) PageSize = 25;
        if (PageSize > 200) PageSize = 200;
        if (PageIndex <= 0) PageIndex = 1;

        var result = await _supplierService.SearchPagedAsync(BuildCriteria(includePaging: true), cancellationToken);
        TotalRecords = result.TotalCount;
        if (TotalRecords > 0 && PageIndex > TotalPages)
        {
            PageIndex = TotalPages;
            result = await _supplierService.SearchPagedAsync(BuildCriteria(includePaging: true), cancellationToken);
            TotalRecords = result.TotalCount;
        }
        Rows = result.Rows;
    }

    private SupplierFilterCriteria BuildCriteria(bool includePaging = true)
        => new()
        {
            ViewMode = string.IsNullOrWhiteSpace(ViewMode) ? "current" : ViewMode.Trim(),
            Year = Year,
            DeptId = DeptId,
            SupplierCode = NullIfEmpty(SupplierCode),
            SupplierName = NullIfEmpty(SupplierName),
            Business = NullIfEmpty(Business),
            Contact = NullIfEmpty(Contact),
            StatusId = StatusId,
            IsNew = IsNew,
            PageIndex = includePaging ? PageIndex : null,
            PageSize = includePaging ? PageSize : null
        };

    private static string Csv(string? value)
    {
        var v = value ?? string.Empty;
        if (v.Contains(',') || v.Contains('"') || v.Contains('\n'))
        {
            return $"\"{v.Replace("\"", "\"\"")}\"";
        }

        return v;
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private List<int> ParseSelectedSupplierIds()
    {
        var ids = new HashSet<int>();

        if (SelectedSupplierId > 0)
        {
            ids.Add(SelectedSupplierId);
        }

        if (!string.IsNullOrWhiteSpace(SelectedSupplierIdsCsv))
        {
            foreach (var part in SelectedSupplierIdsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, out var id) && id > 0)
                {
                    ids.Add(id);
                }
            }
        }

        return ids.ToList();
    }

    private static string BuildSubmitMessage(int successCount, int alreadySubmittedCount, int notFoundCount, int totalSelected)
    {
        if (totalSelected == 1)
        {
            if (successCount == 1) return "Submitted successfully.";
            if (alreadySubmittedCount == 1) return "Supplier already submitted.";
            if (notFoundCount == 1) return "Supplier not found.";
        }

        var parts = new List<string>();
        if (successCount > 0) parts.Add($"submitted: {successCount}");
        if (alreadySubmittedCount > 0) parts.Add($"already submitted: {alreadySubmittedCount}");
        if (notFoundCount > 0) parts.Add($"not found: {notFoundCount}");

        return parts.Count == 0
            ? "No suppliers submitted."
            : $"Submit completed. {string.Join(", ", parts)}.";
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

    private IActionResult RedirectToCurrentList()
        => RedirectToPage("./Index", new
        {
            ViewMode,
            Year,
            DeptId,
            SupplierCode,
            SupplierName,
            Business,
            Contact,
            StatusId,
            IsNew,
            PageIndex,
            PageSize
        });
}
