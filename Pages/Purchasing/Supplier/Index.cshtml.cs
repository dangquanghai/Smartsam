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

    [BindProperty]
    public string? SelectedSupplierIdsCsv { get; set; }

    public string? Message { get; set; }
    public string MessageType { get; set; } = "info";

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
            SetMessage("Xác nhận copy và nhập năm hợp lệ (2000-2100).", "warning");
            Rows = (await _supplierService.SearchAsync(BuildCriteria(), cancellationToken)).ToList();
            return Page();
        }

        try
        {
            await _supplierService.CopyCurrentSuppliersToYearAsync(CopyYear, cancellationToken);
            SetMessage($"Đã copy danh sách nhà cung cấp sang dữ liệu năm {CopyYear}.", "success");
        }
        catch (Exception ex)
        {
            SetMessage("Copy failed: " + ex.Message, "error");
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

        var selectedIds = ParseSelectedSupplierIds();
        if (selectedIds.Count == 0)
        {
            SetMessage("Vui lòng chọn nhà cung cấp trước khi submit.", "warning");
            Rows = (await _supplierService.SearchAsync(BuildCriteria(), cancellationToken)).ToList();
            return Page();
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

        SetMessage(
            BuildSubmitMessage(successCount, alreadySubmittedCount, notFoundCount, selectedIds.Count),
            successCount > 0 ? "success" : (alreadySubmittedCount > 0 ? "warning" : "info"));
        Rows = (await _supplierService.SearchAsync(BuildCriteria(), cancellationToken)).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostDeleteAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        if (!HasPermission(6))
        {
            return Forbid();
        }

        await LoadFiltersAsync(cancellationToken);

        var selectedIds = ParseSelectedSupplierIds();
        if (selectedIds.Count != 1)
        {
            SetMessage("Please select exactly one supplier to delete.", "warning");
            Rows = (await _supplierService.SearchAsync(BuildCriteria(), cancellationToken)).ToList();
            return Page();
        }

        var operatorCode = User.Identity?.Name ?? "SYSTEM";
        var result = await _supplierService.DeleteAsync(selectedIds[0], operatorCode, cancellationToken);

        if (result.NotFound)
        {
            SetMessage("Supplier not found or already deleted.", "error");
        }
        else if (!result.Success)
        {
            SetMessage(result.Reason ?? "Delete failed.", "error");
        }
        else if (result.IsHardDelete)
        {
            SetMessage("Supplier deleted successfully.", "success");
        }
        else if (result.IsSoftDelete)
        {
            SetMessage("Supplier deleted (soft delete).", "info");
        }
        else
        {
            SetMessage("Supplier delete processed.", "info");
        }

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
            if (successCount == 1) return "Submit thành công.";
            if (alreadySubmittedCount == 1) return "Nhà cung cấp này đã submit trước đó.";
            if (notFoundCount == 1) return "Không tìm thấy nhà cung cấp đã chọn.";
        }

        var parts = new List<string>();
        if (successCount > 0) parts.Add($"thành công {successCount}");
        if (alreadySubmittedCount > 0) parts.Add($"đã submit trước {alreadySubmittedCount}");
        if (notFoundCount > 0) parts.Add($"không tìm thấy {notFoundCount}");

        return parts.Count == 0
            ? "Không có bản ghi nào được submit."
            : $"Kết quả submit: {string.Join(", ", parts)}.";
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
