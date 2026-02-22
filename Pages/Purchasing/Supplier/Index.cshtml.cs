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

    [BindProperty]
    public int CopyYear { get; set; }

    [BindProperty]
    public bool ConfirmCopy { get; set; }

    [BindProperty]
    public int SelectedSupplierId { get; set; }

    public string? Message { get; set; }

    public PagePermissions PagePerm { get; private set; } = new();

    public List<SelectListItem> Departments { get; set; } = [];
    public List<SelectListItem> Statuses { get; set; } = [];
    public List<SupplierListRowDto> Rows { get; set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        if (!HasPermission(1))
        {
            return Forbid();
        }

        await LoadFiltersAsync(cancellationToken);
        Rows = (await _supplierService.SearchAsync(BuildCriteria(), cancellationToken)).ToList();
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

        if (!ConfirmCopy || CopyYear < 2000 || CopyYear > 2100)
        {
            Message = "Xác nhận copy và nhập năm hợp lệ (2000-2100).";
            Rows = (await _supplierService.SearchAsync(BuildCriteria(), cancellationToken)).ToList();
            return Page();
        }

        try
        {
            await _supplierService.CopyCurrentSuppliersToYearAsync(CopyYear, cancellationToken);
            Message = $"Đã copy danh sách nhà cung cấp sang dữ liệu năm {CopyYear}.";
        }
        catch (Exception ex)
        {
            Message = "Copy failed: " + ex.Message;
        }

        Rows = (await _supplierService.SearchAsync(BuildCriteria(), cancellationToken)).ToList();
        return Page();
    }

    public async Task<IActionResult> OnGetExportCsvAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        if (!HasPermission(1))
        {
            return Forbid();
        }

        var rows = await _supplierService.SearchAsync(BuildCriteria(), cancellationToken);
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

        if (SelectedSupplierId <= 0)
        {
            Message = "Vui lòng chọn nhà cung cấp trước khi submit.";
            Rows = (await _supplierService.SearchAsync(BuildCriteria(), cancellationToken)).ToList();
            return Page();
        }

        var supplier = await _supplierService.GetDetailAsync(SelectedSupplierId, cancellationToken);
        if (supplier is null)
        {
            Message = "Không tìm thấy nhà cung cấp đã chọn.";
            Rows = (await _supplierService.SearchAsync(BuildCriteria(), cancellationToken)).ToList();
            return Page();
        }

        if (supplier.Status == 1)
        {
            Message = "Nhà cung cấp này đã submit trước đó.";
            Rows = (await _supplierService.SearchAsync(BuildCriteria(), cancellationToken)).ToList();
            return Page();
        }

        var operatorCode = User.Identity?.Name ?? "SYSTEM";
        await _supplierService.SubmitApprovalAsync(SelectedSupplierId, operatorCode, cancellationToken);
        Message = "Submit thành công.";
        Rows = (await _supplierService.SearchAsync(BuildCriteria(), cancellationToken)).ToList();
        return Page();
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

    private SupplierFilterCriteria BuildCriteria()
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
            IsNew = IsNew
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
}
