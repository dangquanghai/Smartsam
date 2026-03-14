using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Data;
using SmartSam.Pages;
using SmartSam.Services;

namespace SmartSam.Pages.Purchasing.Supplier;

public class IndexModel : BasePageModel
{
    private readonly PermissionService _permissionService;
    private readonly string _connectionString;
    private const string AnnualYearColumn = "ForYear";
    private const int NoDepartmentScopeValue = -1;
    private const int SupplierFunctionId = 71;
    private const int PermissionViewList = 1;
    private const int PermissionViewDetail = 2;
    private const int PermissionAdd = 3;
    private const int PermissionEdit = 4;
    private const int PermissionSubmit = 5;
    private const int PermissionCopy = 6;
    private EmployeeDataScope _dataScope = new();
    private bool _isAdminRole;

    // Khoi tao PageModel, lay dependency dung chung cho truy van SQL + permission.
    public IndexModel(IConfiguration configuration, PermissionService permissionService) : base(configuration)
    {
        _permissionService = permissionService;
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Missing connection string: DefaultConnection");
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
    public int PageSize { get; set; }

    public int DefaultPageSize => _config.GetValue<int>("AppSettings:DefaultPageSize", 10);

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
    public List<SupplierRow> Rows { get; set; } = [];
    public int TotalRecords { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)PageSize));
    public bool HasPreviousPage => PageIndex > 1;
    public bool HasNextPage => PageIndex < TotalPages;

    // Wrapper cho View:
    // - Giu duoc style doc de hieu (Model.CanAdd, Model.CanSubmit, ...)
    // - Nguon goc van map ve PermissionNo trong he thong (khong doi logic quyen).
    public bool CanViewDetail => HasPermission(PermissionViewDetail);
    public bool CanAdd => HasPermission(PermissionAdd);
    public bool CanCopy => HasPermission(PermissionCopy);
    public bool CanSubmit => HasPermission(PermissionSubmit);
    public bool CanSearchAllDepartments => _isAdminRole || _dataScope.SeeDataAllDept;
    public string SelectedDeptText { get; private set; } = string.Empty;

    // Tai lan dau vao trang list:
    // - Nap permission va data scope
    // - Nap bo loc + du lieu grid.
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        await LoadUserDataScopeAsync(cancellationToken);

        if (PageSize <= 0)
        {
            PageSize = DefaultPageSize;
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

    // Xu ly Copy theo nam:
    // - Chi cho phep khi co PermissionCopy
    // - Van check scope phong ban truoc khi copy.
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
            var supplierDeptId = await GetSupplierDepartmentAsync(supplierId, "current", null, cancellationToken);
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
            await CopyCurrentSuppliersToYearAsync(CopyYear, accessibleIds, cancellationToken);
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

    // Export danh sach Supplier ra Excel theo bo loc hien tai.
    public async Task<IActionResult> OnGetExportExcelAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        await LoadUserDataScopeAsync(cancellationToken);
        if (!HasPermission(PermissionViewList))
        {
            return Forbid();
        }

        var rows = await SearchAsync(BuildCriteria(includePaging: false), cancellationToken);
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

    // Submit Supplier vao workflow:
    // - Reset lai step duyet ban dau (Status = 0)
    // - Danh dau da submit bang PurchaserPreparedDate.
    // Co them check scope phong ban + trang thai nghiep vu Supplier.
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

        var submittedCount = 0;
        var notFoundCount = 0;
        var noAccessCount = 0;
        var notDraftCount = 0;
        var alreadySubmittedCount = 0;
        var operatorCode = User.Identity?.Name ?? "SYSTEM";

        foreach (var supplierId in selectedIds)
        {
            var supplier = await GetSupplierSubmitInfoAsync(supplierId, cancellationToken);
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

            if ((supplier.Status ?? 0) != 0)
            {
                notDraftCount++;
                continue;
            }

            if (supplier.PurchaserPreparedDate.HasValue)
            {
                alreadySubmittedCount++;
                continue;
            }

            await SubmitSupplierWorkflowAsync(supplierId, operatorCode, cancellationToken);
            submittedCount++;
        }

        SetFlashMessage(
            BuildSubmitMessage(submittedCount, notFoundCount, noAccessCount, notDraftCount, alreadySubmittedCount, selectedIds.Count),
            submittedCount > 0 ? "success" : "info");
        return RedirectToCurrentList();
    }

    // API search cho grid:
    // - Kiem tra permission view list
    // - Tra du lieu + action flags de UI an/hien nut dung quyen.
    public async Task<IActionResult> OnPostSearchAsync([FromBody] SupplierSearchRequest request, CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        await LoadUserDataScopeAsync(cancellationToken);
        if (!HasPermission(PermissionViewList))
        {
            return new JsonResult(new { success = false, message = "Forbidden" })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }

        ViewMode = string.IsNullOrWhiteSpace(request.ViewMode) ? "current" : request.ViewMode.Trim();
        Year = request.Year;
        DeptId = request.DeptId;
        SupplierCode = request.SupplierCode;
        SupplierName = request.SupplierName;
        Business = request.Business;
        Contact = request.Contact;
        StatusId = request.StatusId;
        IsNew = request.IsNew;
        PageIndex = request.Page <= 0 ? 1 : request.Page;
        PageSize = request.PageSize <= 0 ? DefaultPageSize : Math.Min(request.PageSize, 200);

        var result = await SearchPagedAsync(BuildCriteria(includePaging: true), cancellationToken);
        var totalPages = PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(result.TotalCount / (double)PageSize));

        var rowsWithActions = result.Rows.Select(r =>
        {
            var canAccessDepartment = CanAccessDepartment(r.DeptID);
            var canViewDetail = HasPermission(PermissionViewDetail) && canAccessDepartment;
            var canEditDetail = HasPermission(PermissionEdit)
                && canAccessDepartment
                && !string.Equals(ViewMode, "byyear", StringComparison.OrdinalIgnoreCase)
                && (r.Status ?? 0) < 1
                && !r.PurchaserPreparedDate.HasValue;

            return new
            {
            data = r,
            actions = new
            {
                canAccess = canEditDetail || canViewDetail,
                accessMode = canEditDetail ? "edit" : "view",
                canCopy = HasPermission(PermissionCopy) && canAccessDepartment,
                canSubmit = HasPermission(PermissionSubmit)
                    && !string.Equals(ViewMode, "byyear", StringComparison.OrdinalIgnoreCase)
                    && (r.Status ?? 0) == 0
                    && !r.PurchaserPreparedDate.HasValue
                    && canAccessDepartment
            }
            };
        });

        return new JsonResult(new
        {
            success = true,
            data = rowsWithActions,
            total = result.TotalCount,
            page = PageIndex,
            pageSize = PageSize,
            totalPages
        });
    }

    // Nap du lieu dropdown bo loc (Department, Status).
    // Gioi han Department theo data scope.
    private Task LoadFiltersAsync(CancellationToken cancellationToken)
    {
        var departments = LoadListFromSql(
            "SELECT DeptID, DeptCode FROM dbo.MS_Department ORDER BY DeptCode",
            "DeptID",
            "DeptCode");

        var statuses = LoadListFromSql(
            "SELECT SupplierStatusID, SupplierStatusName FROM dbo.PC_SupplierStatus ORDER BY SupplierStatusID",
            "SupplierStatusID",
            "SupplierStatusName");

        if (!_isAdminRole && !_dataScope.SeeDataAllDept)
        {
            var scopedDepartments = departments
                .Where(x => _dataScope.DeptID.HasValue && int.TryParse(x.Value, out var id) && id == _dataScope.DeptID.Value)
                .ToList();

            Departments = [.. scopedDepartments];

            DeptId = _dataScope.DeptID ?? NoDepartmentScopeValue;
        }
        else
        {
            Departments = [
                new SelectListItem { Value = string.Empty, Text = "--- All ---" },
                .. departments
            ];
        }

        Statuses = [
            new SelectListItem { Value = string.Empty, Text = "--- All ---" },
            .. statuses
        ];

        var selectedDeptValue = DeptId?.ToString() ?? string.Empty;
        SelectedDeptText = Departments.FirstOrDefault(x => x.Value == selectedDeptValue)?.Text ?? string.Empty;

        return Task.CompletedTask;
    }

    // Nap du lieu grid theo paging hien tai.
    private async Task LoadRowsAsync(CancellationToken cancellationToken)
    {
        if (PageSize <= 0) PageSize = DefaultPageSize;
        if (PageSize > 200) PageSize = 200;
        if (PageIndex <= 0) PageIndex = 1;

        var result = await SearchPagedAsync(BuildCriteria(includePaging: true), cancellationToken);
        TotalRecords = result.TotalCount;
        if (TotalRecords > 0 && PageIndex > TotalPages)
        {
            PageIndex = TotalPages;
            result = await SearchPagedAsync(BuildCriteria(includePaging: true), cancellationToken);
            TotalRecords = result.TotalCount;
        }
        Rows = result.Rows;
    }

    // Gom toan bo gia tri filter tren man hinh ve 1 object criteria.
    private SupplierFilterInput BuildCriteria(bool includePaging = true)
    {
        var restrictedDeptId = (!_isAdminRole && !_dataScope.SeeDataAllDept)
            ? (_dataScope.DeptID ?? NoDepartmentScopeValue)
            : DeptId;

        return new SupplierFilterInput
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

    // Chuan hoa chuoi rong ve null de query SQL de hon.
    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    // Lay SupplierID duoc chon tu radio/hidden field.
    // Hien tai chi lay 1 ID dau tien (single selection).
    private List<int> ParseSelectedSupplierIds()
    {
        if (SelectedSupplierId > 0)
        {
            return [SelectedSupplierId];
        }

        if (!string.IsNullOrWhiteSpace(SelectedSupplierIdsCsv))
        {
            foreach (var part in SelectedSupplierIdsCsv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (int.TryParse(part, out var id) && id > 0)
                {
                    return [id];
                }
            }
        }

        return [];
    }

    // Tao message tong hop sau khi submit.
    private static string BuildSubmitMessage(int submittedCount, int notFoundCount, int noAccessCount, int notDraftCount, int alreadySubmittedCount, int totalSelected)
    {
        if (totalSelected == 1)
        {
            if (submittedCount == 1) return "Supplier submitted successfully.";
            if (notFoundCount == 1) return "Supplier not found.";
            if (noAccessCount == 1) return "You do not have permission to submit this supplier.";
            if (notDraftCount == 1) return "Only Draft supplier can be submitted.";
            if (alreadySubmittedCount == 1) return "Supplier has already been submitted.";
        }

        var parts = new List<string>();
        if (submittedCount > 0) parts.Add($"submitted: {submittedCount}");
        if (notFoundCount > 0) parts.Add($"not found: {notFoundCount}");
        if (noAccessCount > 0) parts.Add($"no access: {noAccessCount}");
        if (notDraftCount > 0) parts.Add($"not draft: {notDraftCount}");
        if (alreadySubmittedCount > 0) parts.Add($"already submitted: {alreadySubmittedCount}");

        return parts.Count == 0
            ? "No supplier status was changed."
            : $"Submit completed. {string.Join(", ", parts)}.";
    }

    // Lay data scope user (DeptID + SeeDataAllDept).
    // Supplier co data scope theo phong ban nen can ham rieng.
    private async Task<EmployeeDataScope> GetEmployeeDataScopeAsync(string? employeeCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(employeeCode))
        {
            return new EmployeeDataScope();
        }

        const string sql = @"
            SELECT TOP 1 DeptID, ISNULL(SeeDataAllDept, 0) AS SeeDataAllDept
            FROM dbo.MS_Employee
            WHERE EmployeeCode = @EmployeeCode";

        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@EmployeeCode", SqlDbType.NVarChar, 50).Value = employeeCode.Trim();

        await conn.OpenAsync(cancellationToken);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await rd.ReadAsync(cancellationToken))
        {
            return new EmployeeDataScope();
        }

        return new EmployeeDataScope
        {
            DeptID = rd.IsDBNull(0) ? null : Convert.ToInt32(rd[0]),
            SeeDataAllDept = !rd.IsDBNull(1) && Convert.ToBoolean(rd[1])
        };
    }

    // Tim Dept cua Supplier de check xem user co duoc thao tac record nay khong.
    private async Task<int?> GetSupplierDepartmentAsync(int supplierId, string? viewMode, int? year, CancellationToken cancellationToken)
    {
        var isByYear = string.Equals(viewMode, "byyear", StringComparison.OrdinalIgnoreCase);
        var tableName = isByYear ? "dbo.PC_SupplierAnualy" : "dbo.PC_Suppliers";
        var yearSql = isByYear ? $@" AND [{AnnualYearColumn}] = @Year" : string.Empty;

        var sql = $@"
            SELECT TOP 1 DeptID
            FROM {tableName}
            WHERE SupplierID = @SupplierID{yearSql}";

        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@SupplierID", SqlDbType.Int).Value = supplierId;
        if (isByYear)
        {
            cmd.Parameters.Add("@Year", SqlDbType.Int).Value = (object?)year ?? DBNull.Value;
        }

        await conn.OpenAsync(cancellationToken);
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        if (result == null || result == DBNull.Value)
        {
            return null;
        }

        return Convert.ToInt32(result);
    }

    // Lay thong tin toi thieu (Dept + Status) phuc vu submit/reset workflow.
    private async Task<SupplierSubmitInfo?> GetSupplierSubmitInfoAsync(int supplierId, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT TOP 1 DeptID, [Status], PurchaserPreparedDate
            FROM dbo.PC_Suppliers
            WHERE SupplierID = @SupplierID";

        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@SupplierID", SqlDbType.Int).Value = supplierId;

        await conn.OpenAsync(cancellationToken);
        await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
        if (!await rd.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new SupplierSubmitInfo
        {
            DeptID = rd.IsDBNull(0) ? null : Convert.ToInt32(rd[0]),
            Status = rd.IsDBNull(1) ? null : Convert.ToInt32(rd[1]),
            PurchaserPreparedDate = rd.IsDBNull(2) ? null : Convert.ToDateTime(rd[2])
        };
    }

    // Submit supplier vao workflow:
    // - Dua supplier vao step cho level 1 xu ly (Status = 0)
    // - Ghi nhan nguoi submit + thoi gian submit.
    private async Task SubmitSupplierWorkflowAsync(int supplierId, string operatorCode, CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE dbo.PC_Suppliers
            SET [Status] = 0,
                ApprovedDate = NULL,
                PurchaserCode = @OperatorCode,
                PurchaserPreparedDate = GETDATE(),
                PurchaserCPT = NULL,
                DepartmentCode = NULL,
                DepartmentApproveDate = NULL,
                DepartmentCPT = NULL,
                FinancialCode = NULL,
                FinancialApproveDate = NULL,
                FinancialCPT = NULL,
                BODCode = NULL,
                BODApproveDate = NULL,
                BODCPT = NULL
            WHERE SupplierID = @SupplierID";

        await using var conn = new SqlConnection(_connectionString);
        await using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@SupplierID", SqlDbType.Int).Value = supplierId;
        cmd.Parameters.Add("@OperatorCode", SqlDbType.NVarChar, 50).Value = operatorCode;
        await conn.OpenAsync(cancellationToken);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    // Copy danh sach Supplier hien tai sang bang annual theo nam chi dinh.
    // Dong thoi set IsNew = 0 o ban current.
    private async Task CopyCurrentSuppliersToYearAsync(int copyYear, IReadOnlyCollection<int> supplierIds, CancellationToken cancellationToken)
    {
        if (supplierIds == null || supplierIds.Count == 0)
        {
            throw new InvalidOperationException("No supplier selected for copy.");
        }

        var normalizedIds = supplierIds
            .Distinct()
            .Where(x => x > 0)
            .ToList();

        if (normalizedIds.Count == 0)
        {
            throw new InvalidOperationException("No valid supplier selected for copy.");
        }

        var selectedIdValues = string.Join(", ", normalizedIds.Select(id => $"({id})"));

        var sql = $@"
            CREATE TABLE #SelectedIds (SupplierID int NOT NULL PRIMARY KEY);
            CREATE TABLE #CopyIds (SupplierID int NOT NULL PRIMARY KEY);

            INSERT INTO #SelectedIds (SupplierID)
            VALUES {selectedIdValues};

            INSERT INTO #CopyIds (SupplierID)
            SELECT s.SupplierID
            FROM dbo.PC_Suppliers s
            INNER JOIN #SelectedIds sel ON sel.SupplierID = s.SupplierID
            AND NOT EXISTS (
                SELECT 1
                FROM dbo.PC_SupplierAnualy a
                WHERE a.SupplierID = s.SupplierID
                AND a.[{AnnualYearColumn}] = @CopyYear
            );

            INSERT INTO dbo.PC_SupplierAnualy
            (
                SupplierID, SupplierCode, SupplierName, Address, Phone, Mobile, Fax,
                Contact, [Position], Business, ApprovedDate, [Document], Certificate,
                Service, Comment, Appcept, IsApproved, DeptID, IsNew, CodeOfAcc, IsLinen, [Status],
                PurchaserCode, PurchaserPreparedDate, PurchaserCPT,
                DepartmentCode, DepartmentApproveDate, DepartmentCPT,
                FinancialCode, FinancialApproveDate, FinancialCPT,
                BODCode, BODApproveDate, BODCPT, [{AnnualYearColumn}]
            )
            SELECT
                SupplierID, SupplierCode, SupplierName, Address, Phone, Mobile, Fax,
                Contact, [Position], Business, ApprovedDate, [Document], Certificate,
                Service, Comment, Appcept, IsApproved, DeptID, IsNew, CodeOfAcc, IsLinen, [Status],
                PurchaserCode, PurchaserPreparedDate, PurchaserCPT,
                DepartmentCode, DepartmentApproveDate, DepartmentCPT,
                FinancialCode, FinancialApproveDate, FinancialCPT,
                BODCode, BODApproveDate, BODCPT, @CopyYear
            FROM dbo.PC_Suppliers
            WHERE SupplierID IN (SELECT SupplierID FROM #CopyIds);

            UPDATE s
            SET s.IsNew = 0
            FROM dbo.PC_Suppliers s
            INNER JOIN #SelectedIds sel ON sel.SupplierID = s.SupplierID;";

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        await using var tx = await conn.BeginTransactionAsync(cancellationToken);
        try
        {
            await using var cmd = new SqlCommand(sql, conn, (SqlTransaction)tx);
            cmd.Parameters.Add("@CopyYear", SqlDbType.Int).Value = copyYear;
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // Search khong paging (dung cho export).
    private async Task<List<SupplierRow>> SearchAsync(SupplierFilterInput criteria, CancellationToken cancellationToken)
    {
        var noPagingCriteria = new SupplierFilterInput
        {
            ViewMode = criteria.ViewMode,
            Year = criteria.Year,
            DeptId = criteria.DeptId,
            SupplierCode = criteria.SupplierCode,
            SupplierName = criteria.SupplierName,
            Business = criteria.Business,
            Contact = criteria.Contact,
            StatusId = criteria.StatusId,
            IsNew = criteria.IsNew,
            PageIndex = null,
            PageSize = null
        };

        var result = await SearchPagedAsync(noPagingCriteria, cancellationToken);
        return result.Rows;
    }

    // Ham search chinh: query DB + paging.
    // Pattern tong the thong nhat, nhung SQL va filter la nghiep vu Supplier.
    private async Task<SupplierSearchResult> SearchPagedAsync(SupplierFilterInput criteria, CancellationToken cancellationToken)
    {
        var isByYear = string.Equals(criteria.ViewMode, "byyear", StringComparison.OrdinalIgnoreCase);
        var sourceTable = isByYear ? "dbo.PC_SupplierAnualy" : "dbo.PC_Suppliers";
        var pageIndex = criteria.PageIndex.GetValueOrDefault() <= 0 ? 1 : criteria.PageIndex!.Value;
        var pageSize = criteria.PageSize.GetValueOrDefault() <= 0 ? DefaultPageSize : criteria.PageSize!.Value;
        var applyPaging = criteria.PageIndex.HasValue && criteria.PageSize.HasValue;
        var yearFilterSql = isByYear
            ? $"\n    AND (@Year IS NULL OR s.[{AnnualYearColumn}] = @Year)"
            : string.Empty;

        var fromWhereSql = $@"
FROM {sourceTable} s
LEFT JOIN dbo.PC_SupplierStatus st ON s.[Status] = st.SupplierStatusID
LEFT JOIN dbo.MS_Department d ON s.DeptID = d.DeptID
WHERE
    (@SupplierCode IS NULL OR s.SupplierCode LIKE '%' + @SupplierCode + '%')
    AND (@SupplierName IS NULL OR s.SupplierName LIKE '%' + @SupplierName + '%')
    AND (@Business IS NULL OR s.Business LIKE '%' + @Business + '%')
    AND (@Contact IS NULL OR s.Contact LIKE '%' + @Contact + '%')
    AND (@DeptID IS NULL OR s.DeptID = @DeptID)
    AND (@StatusID IS NULL OR s.[Status] = @StatusID)
    AND (@IsNew = 0 OR s.IsNew = 1){yearFilterSql}";

        var countSql = "SELECT COUNT(1) " + fromWhereSql;
        var selectSql = $@"
SELECT
    s.SupplierID,
    s.SupplierCode,
    s.SupplierName,
    s.Address,
    s.Phone,
    s.Mobile,
    s.Fax,
    s.Contact,
    s.[Position],
    s.Business,
    s.ApprovedDate,
    s.[Document],
    s.Certificate,
    s.Service,
    s.Comment,
    s.IsNew,
    s.CodeOfAcc,
    s.DeptID,
    d.DeptCode,
    st.SupplierStatusName,
    s.[Status],
    s.PurchaserPreparedDate
{fromWhereSql}
ORDER BY s.SupplierID DESC";

        if (applyPaging)
        {
            selectSql += "\nOFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";
        }

        var rows = new List<SupplierRow>();
        int totalRecords = 0;

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        // 1) Dem tong record
        await using (var countCmd = new SqlCommand(countSql, conn))
        {
            BindSearchParams(countCmd, criteria, isByYear);
            var totalObj = await countCmd.ExecuteScalarAsync(cancellationToken);
            totalRecords = Convert.ToInt32(totalObj);
        }

        // 2) Lay data
        await using (var cmd = new SqlCommand(selectSql, conn))
        {
            BindSearchParams(cmd, criteria, isByYear);
            if (applyPaging)
            {
                cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = (pageIndex - 1) * pageSize;
                cmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;
            }

            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                rows.Add(new SupplierRow
                {
                    SupplierID = rd.GetInt32(0),
                    SupplierCode = rd[1]?.ToString() ?? string.Empty,
                    SupplierName = rd[2]?.ToString() ?? string.Empty,
                    Address = rd[3]?.ToString() ?? string.Empty,
                    Phone = rd[4]?.ToString() ?? string.Empty,
                    Mobile = rd[5]?.ToString() ?? string.Empty,
                    Fax = rd[6]?.ToString() ?? string.Empty,
                    Contact = rd[7]?.ToString() ?? string.Empty,
                    Position = rd[8]?.ToString() ?? string.Empty,
                    Business = rd[9]?.ToString() ?? string.Empty,
                    ApprovedDate = rd.IsDBNull(10) ? null : rd.GetDateTime(10),
                    Document = rd[11]?.ToString() ?? string.Empty,
                    Certificate = rd[12]?.ToString() ?? string.Empty,
                    Service = rd[13]?.ToString() ?? string.Empty,
                    Comment = rd[14]?.ToString() ?? string.Empty,
                    IsNew = !rd.IsDBNull(15) && Convert.ToBoolean(rd[15]),
                    CodeOfAcc = rd[16]?.ToString() ?? string.Empty,
                    DeptID = rd.IsDBNull(17) ? null : rd.GetInt32(17),
                    DeptCode = rd[18]?.ToString() ?? string.Empty,
                    SupplierStatusName = rd[19]?.ToString() ?? string.Empty,
                    Status = rd.IsDBNull(20) ? null : Convert.ToInt32(rd[20]),
                    PurchaserPreparedDate = rd.IsDBNull(21) ? null : Convert.ToDateTime(rd[21])
                });
            }
        }

        return new SupplierSearchResult
        {
            Rows = rows,
            TotalCount = totalRecords
        };
    }

    // Bind parameter cho ca cau query count va data.
    private static void BindSearchParams(SqlCommand cmd, SupplierFilterInput criteria, bool isByYear)
    {
        cmd.Parameters.Add("@SupplierCode", SqlDbType.NVarChar, 255).Value = (object?)NullIfEmpty(criteria.SupplierCode) ?? DBNull.Value;
        cmd.Parameters.Add("@SupplierName", SqlDbType.NVarChar, 255).Value = (object?)NullIfEmpty(criteria.SupplierName) ?? DBNull.Value;
        cmd.Parameters.Add("@Business", SqlDbType.NVarChar, 255).Value = (object?)NullIfEmpty(criteria.Business) ?? DBNull.Value;
        cmd.Parameters.Add("@Contact", SqlDbType.NVarChar, 255).Value = (object?)NullIfEmpty(criteria.Contact) ?? DBNull.Value;
        cmd.Parameters.Add("@DeptID", SqlDbType.Int).Value = (object?)criteria.DeptId ?? DBNull.Value;
        cmd.Parameters.Add("@StatusID", SqlDbType.Int).Value = (object?)criteria.StatusId ?? DBNull.Value;
        cmd.Parameters.Add("@IsNew", SqlDbType.Int).Value = criteria.IsNew ? 1 : 0;

        if (isByYear)
        {
            cmd.Parameters.Add("@Year", SqlDbType.Int).Value = (object?)criteria.Year ?? DBNull.Value;
        }
    }

    // Nap data scope cua user login hien tai.
    private async Task LoadUserDataScopeAsync(CancellationToken cancellationToken)
    {
        _isAdminRole = string.Equals(User.FindFirst("IsAdminRole")?.Value, "True", StringComparison.OrdinalIgnoreCase);
        if (_isAdminRole)
        {
            _dataScope = new EmployeeDataScope { SeeDataAllDept = true };
            return;
        }

        _dataScope = await GetEmployeeDataScopeAsync(User.Identity?.Name, cancellationToken);
        if (!_dataScope.SeeDataAllDept)
        {
            DeptId = _dataScope.DeptID ?? NoDepartmentScopeValue;
        }
    }

    // Kiem tra record co nam trong pham vi phong ban user duoc xem/lam hay khong.
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

    // Nap permission cho Supplier theo FunctionID co dinh.
    // Lay AllowedNos tu PermissionService.
    private void LoadPagePermissions()
    {
        var isAdmin = string.Equals(User.FindFirst("IsAdminRole")?.Value, "True", StringComparison.OrdinalIgnoreCase);
        if (isAdmin)
        {
            PagePerm.AllowedNos = Enumerable.Range(1, 20).ToList();
            return;
        }

        if (!int.TryParse(User.FindFirst("RoleID")?.Value, out var roleId))
        {
            roleId = 0;
        }

        PagePerm.AllowedNos = _permissionService.GetPermissionsForPage(roleId, SupplierFunctionId);
    }

    // Helper check 1 permission number trong AllowedNos.
    private bool HasPermission(int permissionNo) => PagePerm.HasPermission(permissionNo);

    // Set flash message de hien sau redirect.
    private void SetFlashMessage(string message, string type = "info")
    {
        FlashMessage = message;
        FlashMessageType = type;
    }

    // Set message de hien ngay tren response hien tai.
    private void SetMessage(string message, string type = "info")
    {
        Message = message;
        MessageType = type;
    }

    // Redirect ve lai list, giu nguyen bo loc hien tai.
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

public class SupplierSearchRequest
{
    public string? ViewMode { get; set; }
    public int? Year { get; set; }
    public int? DeptId { get; set; }
    public string? SupplierCode { get; set; }
    public string? SupplierName { get; set; }
    public string? Business { get; set; }
    public string? Contact { get; set; }
    public int? StatusId { get; set; }
    public bool IsNew { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class SupplierFilterInput
{
    public string ViewMode { get; set; } = "current";
    public int? Year { get; set; }
    public int? DeptId { get; set; }
    public string? SupplierCode { get; set; }
    public string? SupplierName { get; set; }
    public string? Business { get; set; }
    public string? Contact { get; set; }
    public int? StatusId { get; set; }
    public bool IsNew { get; set; }
    public int? PageIndex { get; set; }
    public int? PageSize { get; set; }
}

public class SupplierSearchResult
{
    public List<SupplierRow> Rows { get; set; } = [];
    public int TotalCount { get; set; }
}

public class SupplierRow
{
    public int SupplierID { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Mobile { get; set; } = string.Empty;
    public string Fax { get; set; } = string.Empty;
    public string Contact { get; set; } = string.Empty;
    public string Position { get; set; } = string.Empty;
    public string Business { get; set; } = string.Empty;
    public DateTime? ApprovedDate { get; set; }
    public string Document { get; set; } = string.Empty;
    public string Certificate { get; set; } = string.Empty;
    public string Service { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
    public bool IsNew { get; set; }
    public string CodeOfAcc { get; set; } = string.Empty;
    public int? DeptID { get; set; }
    public string DeptCode { get; set; } = string.Empty;
    public string SupplierStatusName { get; set; } = string.Empty;
    public int? Status { get; set; }
    public DateTime? PurchaserPreparedDate { get; set; }
}

public class EmployeeDataScope
{
    public int? DeptID { get; set; }
    public bool SeeDataAllDept { get; set; }
}

public class SupplierSubmitInfo
{
    public int? DeptID { get; set; }
    public int? Status { get; set; }
    public DateTime? PurchaserPreparedDate { get; set; }
}
