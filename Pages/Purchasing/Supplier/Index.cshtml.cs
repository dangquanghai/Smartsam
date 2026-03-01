using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using SmartSam.Services;

namespace SmartSam.Pages.Purchasing.Supplier;

public class IndexModel : PageModel
{
    private readonly SupplierService _supplierService;
    private readonly PermissionService _permissionService;
    private const int NoDepartmentScopeValue = -1;
    private const int SupplierFunctionId = 71;
    private const int PermissionViewList = 1;
    private const int PermissionSubmit = 4;
    private const int PermissionCopy = 5;
    private const int PermissionViewDetail = 6;
    private EmployeeDataScopeDto _dataScope = new();
    private bool _isAdminRole;

    public IndexModel(IConfiguration configuration, PermissionService permissionService)
    {
        _supplierService = new SupplierService(configuration);
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
    public bool CanViewDetail => HasPermission(PermissionViewDetail);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        await LoadUserDataScopeAsync(cancellationToken);
        if (!HasPermission(PermissionViewList))
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
        await LoadUserDataScopeAsync(cancellationToken);
        if (!HasPermission(PermissionCopy))
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

        var accessibleIds = new List<int>();
        var noAccessCount = 0;
        foreach (var supplierId in selectedIds)
        {
            var supplierDeptId = await _supplierService.GetSupplierDepartmentAsync(supplierId, "current", null, cancellationToken);
            if (CanAccessDepartment(supplierDeptId))
            {
                accessibleIds.Add(supplierId);
            }
            else
            {
                noAccessCount++;
            }
        }

        if (accessibleIds.Count == 0)
        {
            SetFlashMessage("No selected supplier is accessible by your department scope.", "warning");
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
            await _supplierService.CopyCurrentSuppliersToYearAsync(CopyYear, accessibleIds, cancellationToken);
            var message = noAccessCount > 0
                ? $"Copy completed for {accessibleIds.Count} supplier(s). Skipped (no access): {noAccessCount}."
                : "Copy completed.";
            SetFlashMessage(message, "success");
        }
        catch (Exception)
        {
            SetFlashMessage("Copy failed.", "error");
        }
        return RedirectToCurrentList();
    }

    public async Task<IActionResult> OnGetExportExcelAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        await LoadUserDataScopeAsync(cancellationToken);
        if (!HasPermission(PermissionViewList))
        {
            return Forbid();
        }

        var rows = await _supplierService.SearchAsync(BuildCriteria(includePaging: false), cancellationToken);
        using var workbook = new XLWorkbook();
        var ws = workbook.Worksheets.Add("Suppliers");
        var headers = new[]
        {
            "Supplier Code", "Supplier Name", "Address", "Phone", "Mobile",
            "Fax", "Contact", "Position", "Business", "Status", "Dept"
        };
        for (var col = 0; col < headers.Length; col++)
        {
            ws.Cell(1, col + 1).Value = headers[col];
        }
        ws.Row(1).Style.Font.Bold = true;

        var rowIndex = 2;
        foreach (var r in rows)
        {
            ws.Cell(rowIndex, 1).Value = r.SupplierCode ?? string.Empty;
            ws.Cell(rowIndex, 2).Value = r.SupplierName ?? string.Empty;
            ws.Cell(rowIndex, 3).Value = r.Address ?? string.Empty;
            ws.Cell(rowIndex, 4).Value = r.Phone ?? string.Empty;
            ws.Cell(rowIndex, 5).Value = r.Mobile ?? string.Empty;
            ws.Cell(rowIndex, 6).Value = r.Fax ?? string.Empty;
            ws.Cell(rowIndex, 7).Value = r.Contact ?? string.Empty;
            ws.Cell(rowIndex, 8).Value = r.Position ?? string.Empty;
            ws.Cell(rowIndex, 9).Value = r.Business ?? string.Empty;
            ws.Cell(rowIndex, 10).Value = r.SupplierStatusName ?? string.Empty;
            ws.Cell(rowIndex, 11).Value = r.DeptCode ?? string.Empty;
            rowIndex++;
        }
        ws.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        var fileName = $"suppliers_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        return File(
            stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    public async Task<IActionResult> OnPostSubmitAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        await LoadUserDataScopeAsync(cancellationToken);
        if (!HasPermission(PermissionSubmit))
        {
            return Forbid();
        }

        if (string.Equals(ViewMode, "byyear", StringComparison.OrdinalIgnoreCase))
        {
            SetFlashMessage("Submit is available only in Current List mode.", "warning");
            return RedirectToCurrentList();
        }

        await LoadFiltersAsync(cancellationToken);

        var selectedIds = ParseSelectedSupplierIds();
        if (selectedIds.Count == 0)
        {
            SetFlashMessage("Select at least one supplier.", "warning");
            return RedirectToCurrentList();
        }

        var successCount = 0;
        var notFoundCount = 0;
        var noAccessCount = 0;
        var alreadyPreparingCount = 0;

        foreach (var supplierId in selectedIds)
        {
            var supplier = await _supplierService.GetDetailAsync(supplierId, cancellationToken);
            if (supplier is null)
            {
                notFoundCount++;
                continue;
            }

            if (!CanAccessDepartment(supplier.DeptID))
            {
                noAccessCount++;
                continue;
            }

            if ((supplier.Status ?? 0) == 0)
            {
                alreadyPreparingCount++;
                continue;
            }

            await _supplierService.ResetWorkflowToPreparingAsync(supplierId, cancellationToken);
            successCount++;
        }

        SetFlashMessage(
            BuildSubmitMessage(successCount, notFoundCount, noAccessCount, alreadyPreparingCount, selectedIds.Count),
            successCount > 0 ? "success" : "info");
        return RedirectToCurrentList();
    }

    private async Task LoadFiltersAsync(CancellationToken cancellationToken)
    {
        var departments = await _supplierService.GetDepartmentsAsync(cancellationToken);
        var statuses = await _supplierService.GetStatusesAsync(cancellationToken);

        if (!_isAdminRole && !_dataScope.SeeDataAllDept)
        {
            var scopedDepartments = departments
                .Where(x => _dataScope.DeptID.HasValue && x.Id == _dataScope.DeptID.Value)
                .ToList();

            Departments = [
                .. scopedDepartments.Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.CodeOrName })
            ];

            DeptId = _dataScope.DeptID ?? NoDepartmentScopeValue;
        }
        else
        {
            Departments = [
                new SelectListItem { Value = string.Empty, Text = "--- All ---" },
                .. departments.Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.CodeOrName })
            ];
        }

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
    {
        var restrictedDeptId = (!_isAdminRole && !_dataScope.SeeDataAllDept)
            ? (_dataScope.DeptID ?? NoDepartmentScopeValue)
            : DeptId;

        return new SupplierFilterCriteria
        {
            ViewMode = string.IsNullOrWhiteSpace(ViewMode) ? "current" : ViewMode.Trim(),
            Year = Year,
            DeptId = restrictedDeptId,
            SupplierCode = NullIfEmpty(SupplierCode),
            SupplierName = NullIfEmpty(SupplierName),
            Business = NullIfEmpty(Business),
            Contact = NullIfEmpty(Contact),
            StatusId = StatusId,
            IsNew = IsNew,
            PageIndex = includePaging ? PageIndex : null,
            PageSize = includePaging ? PageSize : null
        };
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

    private static string BuildSubmitMessage(int successCount, int notFoundCount, int noAccessCount, int alreadyPreparingCount, int totalSelected)
    {
        if (totalSelected == 1)
        {
            if (successCount == 1) return "Supplier submitted. Workflow reset to Preparing.";
            if (notFoundCount == 1) return "Supplier not found.";
            if (noAccessCount == 1) return "You do not have permission to submit this supplier.";
            if (alreadyPreparingCount == 1) return "Supplier is already in Preparing status.";
        }

        var parts = new List<string>();
        if (successCount > 0) parts.Add($"submitted and reset to preparing: {successCount}");
        if (notFoundCount > 0) parts.Add($"not found: {notFoundCount}");
        if (noAccessCount > 0) parts.Add($"no access: {noAccessCount}");
        if (alreadyPreparingCount > 0) parts.Add($"already preparing: {alreadyPreparingCount}");

        return parts.Count == 0
            ? "No supplier status was changed."
            : $"Submit completed. {string.Join(", ", parts)}.";
    }

    private async Task LoadUserDataScopeAsync(CancellationToken cancellationToken)
    {
        _isAdminRole = string.Equals(User.FindFirst("IsAdminRole")?.Value, "True", StringComparison.OrdinalIgnoreCase);
        if (_isAdminRole)
        {
            _dataScope = new EmployeeDataScopeDto { SeeDataAllDept = true };
            return;
        }

        _dataScope = await _supplierService.GetEmployeeDataScopeAsync(User.Identity?.Name, cancellationToken);
        if (!_dataScope.SeeDataAllDept)
        {
            DeptId = _dataScope.DeptID ?? NoDepartmentScopeValue;
        }
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
