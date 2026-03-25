using ClosedXML.Excel;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using System.Data;
using SmartSam.Helpers;
using SmartSam.Pages;
using SmartSam.Services;
using SmartSam.Services.Interfaces;

namespace SmartSam.Pages.Purchasing.Supplier
{
    public class IndexModel : BasePageModel
    {
        // Tracking comment: keep Supplier index page model marked as touched for current work.
        private readonly ILogger<IndexModel> _logger;
        private readonly ISecurityService _securityService;
        private readonly PermissionService _permissionService;

        private readonly string _connectionString;

        // ID của chức năng trong bảng SYS_Function
        private const int FUNCTION_ID = 71;

        private const string AnnualYearColumn = "ForYear";
        private const int NoDepartmentScopeValue = -1;

        private const int PermissionViewList = 1;
        private const int PermissionViewDetail = 2;
        private const int PermissionAdd = 3;
        private const int PermissionEdit = 4;
        private const int PermissionSubmit = 5;
        private const int PermissionCopy = 6;

        private EmployeeDataScope _dataScope = new();
        private bool _isAdminRole;

        // Khoi tao PageModel, lay dependency dung chung cho truy van SQL + permission.
        public IndexModel(
            ISecurityService securityService,
            IConfiguration config,
            ILogger<IndexModel> logger,
            PermissionService permissionService) : base(config)
        {
            _securityService = securityService;
            _logger = logger;
            _permissionService = permissionService;
            _connectionString = config.GetConnectionString("DefaultConnection")
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

        // Đối tượng chứa quyền của trang này (Dùng cho Giai đoạn 2).
        public PagePermissions PagePerm { get; set; } = new();

        public List<SelectListItem> Departments { get; set; } = [];
        public List<SelectListItem> Statuses { get; set; } = [];
        public List<SupplierRow> Rows { get; set; } = [];
        public int TotalRecords { get; set; }
        public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)PageSize));
        public bool HasPreviousPage => PageIndex > 1;
        public bool HasNextPage => PageIndex < TotalPages;

        public bool CanSearchAllDepartments => CanLoadAllDepartments();
        public string SelectedDeptText { get; private set; } = string.Empty;

        // ==========================================
        // 1. GIAI ĐOẠN LOAD TRANG (GET)
        // ==========================================
        public void OnGet()
        {
            // Lấy quyền được admin cấp cho role login
            PagePerm = GetUserPermissions();

            // Load scope dữ liệu (Dept/LevelCheckSupplier)
            LoadUserDataScope();

            // Khởi tạo các giá trị mặc định cho paging
            if (PageSize <= 0)
            {
                PageSize = DefaultPageSize;
            }

            // Load dữ liệu cho các Dropdown
            LoadDepartments();
            LoadSupplierStatuses();

            ViewData["DefaultPageSize"] = DefaultPageSize;
        }

        // ==========================================
        // 2. XỬ LÝ SEARCH AJAX (POST)
        // ==========================================
        public IActionResult OnPostSearch([FromBody] SearchRequest request)
        {
            try
            {
                // 1. Lấy thông tin Role của người dùng hiện tại
                int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "0");

                // 2. Load scope dữ liệu (Dept/LevelCheckSupplier)
                LoadUserDataScope();

                // 3. Gọi Database lấy dữ liệu thô
                var (rows, totalRecords, page, pageSize) = SearchSuppliers(request);
                var totalPages = pageSize <= 0
                    ? 1
                    : Math.Max(1, (int)Math.Ceiling(totalRecords / (double)pageSize));

                
                var viewMode = string.IsNullOrWhiteSpace(request.ViewMode) ? "current" : request.ViewMode.Trim();
                var dataWithActions = rows.Select(r => {
                    var canAccessDepartment = CanAccessDepartment(r.DeptID);
                    // Lấy tập quyền thực tế cho bản ghi này (đã giao thoa Quyền + Trạng thái)
                    var effectivePerms = _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, r.Status ?? 0);

                    return new
                    {
                        data = r,
                        actions = new
                        {
                            // Chế độ truy cập khi click vào link.
                            // Nếu không có quyền View Detail hoặc không thuộc Dept được phép xem thì không vào được.
                            canAccess = effectivePerms.Contains(PermissionViewDetail) && canAccessDepartment,
                            accessMode = effectivePerms.Contains(PermissionEdit)
                                && canAccessDepartment
                                && !string.Equals(viewMode, "byyear", StringComparison.OrdinalIgnoreCase)
                                    ? "edit"
                                    : "view",

                            canCopy = effectivePerms.Contains(PermissionCopy) && canAccessDepartment,
                            canSubmit = effectivePerms.Contains(PermissionSubmit)
                                && canAccessDepartment
                                && !string.Equals(viewMode, "byyear", StringComparison.OrdinalIgnoreCase)
                        }
                    };
                });

                return new JsonResult(new
                {
                    success = true,
                    data = dataWithActions,
                    total = totalRecords,
                    page,
                    pageSize,
                    totalPages
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnPostSearch");
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // ==========================================
        // 3. CÁC HÀM BỔ TRỢ (HELPER)
        // ==========================================
        public async Task<IActionResult> OnPostCopyYearAsync(CancellationToken cancellationToken)
        {
            PagePerm = GetUserPermissions();
            await LoadUserDataScopeAsync(cancellationToken);
            if (!PagePerm.HasPermission(PermissionCopy))
            {
                return Forbid();
            }

            LoadDepartments();
            LoadSupplierStatuses();

            var selectedIds = ParseSelectedSupplierIds();
            if (selectedIds.Count == 0)
            {
                SetFlashMessage("Select at least one supplier.", "warning");
                return RedirectToCurrentList();
            }

            var accessibleIds = new List<int>();
            var noAccessCount = 0;
            var notFoundCount = 0;
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

                accessibleIds.Add(supplierId);
            }

            if (accessibleIds.Count == 0)
            {
                SetFlashMessage("No selected supplier is accessible or you have no permission to copy.", "warning");
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
                var skippedParts = new List<string>();
                if (notFoundCount > 0) skippedParts.Add($"not found: {notFoundCount}");
                if (noAccessCount > 0) skippedParts.Add($"no access: {noAccessCount}");

                var message = skippedParts.Count > 0
                    ? $"Copy completed for {accessibleIds.Count} supplier(s). Skipped ({string.Join(", ", skippedParts)})."
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
            PagePerm = GetUserPermissions();
            await LoadUserDataScopeAsync(cancellationToken);
            if (!PagePerm.HasPermission(PermissionViewList))
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

        public async Task<IActionResult> OnPostSubmitAsync(CancellationToken cancellationToken)
        {
            var result = await ExecuteSubmitWorkflowAsync(cancellationToken);
            if (result.IsForbidden)
            {
                return Forbid();
            }

            SetFlashMessage(result.Message, result.MessageType);
            return RedirectToCurrentList();
        }

        public async Task<IActionResult> OnPostSubmitAjaxAsync(CancellationToken cancellationToken)
        {
            var result = await ExecuteSubmitWorkflowAsync(cancellationToken);
            if (result.IsForbidden)
            {
                return new JsonResult(new
                {
                    success = false,
                    message = result.Message,
                    messageType = result.MessageType
                })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }

            return new JsonResult(new
            {
                success = result.Success,
                message = result.Message,
                messageType = result.MessageType,
                selectedSupplierId = result.PrimarySupplierId,
                selectedSupplierCode = result.PrimarySupplierCode,
                submittedSupplierIds = result.SubmittedSupplierIds,
                submittedSupplierCodes = result.SubmittedSupplierCodes
            });
        }

        private async Task<SubmitWorkflowResult> ExecuteSubmitWorkflowAsync(CancellationToken cancellationToken)
        {
            PagePerm = GetUserPermissions();
            await LoadUserDataScopeAsync(cancellationToken);
            if (!PagePerm.HasPermission(PermissionSubmit))
            {
                return SubmitWorkflowResult.ForbiddenResult("You do not have permission to submit suppliers.");
            }

            if (string.Equals(ViewMode, "byyear", StringComparison.OrdinalIgnoreCase))
            {
                return SubmitWorkflowResult.Warning("Submit is available only in Current List mode.");
            }

            LoadDepartments();
            LoadSupplierStatuses();

            var selectedIds = ParseSelectedSupplierIds();
            if (selectedIds.Count == 0)
            {
                return SubmitWorkflowResult.Warning("Select at least one supplier.");
            }

            var result = new SubmitWorkflowResult
            {
                TotalSelected = selectedIds.Count
            };

            var operatorCode = User.Identity?.Name ?? "SYSTEM";

            foreach (var supplierId in selectedIds)
            {
                var supplier = await GetSupplierSubmitInfoAsync(supplierId, cancellationToken);
                if (supplier is null)
                {
                    result.NotFoundCount++;
                    continue;
                }

                if (!CanAccessDepartment(supplier.DeptID))
                {
                    result.NoAccessCount++;
                    continue;
                }

                await SubmitSupplierWorkflowAsync(supplierId, operatorCode, cancellationToken);
                result.SubmittedCount++;
                result.SubmittedSupplierIds.Add(supplierId);
                result.SubmittedSupplierCodes.Add(supplier.SupplierCode?.Trim() ?? string.Empty);
            }

            result.Success = result.SubmittedCount > 0;
            result.MessageType = result.Success ? "success" : "info";
            result.Message = BuildSubmitAjaxMessage(result);
            return result;
        }

        private sealed class SubmitWorkflowResult
        {
            public bool Success { get; set; }
            public bool IsForbidden { get; set; }
            public string Message { get; set; } = string.Empty;
            public string MessageType { get; set; } = "info";
            public int TotalSelected { get; set; }
            public int SubmittedCount { get; set; }
            public int NotFoundCount { get; set; }
            public int NoAccessCount { get; set; }
            public int NoPermissionCount { get; set; }
            public int NotDraftCount { get; set; }
            public int AlreadySubmittedCount { get; set; }
            public List<int> SubmittedSupplierIds { get; } = [];
            public List<string> SubmittedSupplierCodes { get; } = [];
            public int? PrimarySupplierId => SubmittedSupplierIds.Count > 0 ? SubmittedSupplierIds[0] : null;
            public string? PrimarySupplierCode => SubmittedSupplierCodes.FirstOrDefault();

            public static SubmitWorkflowResult ForbiddenResult(string message)
                => new()
                {
                    IsForbidden = true,
                    Message = message,
                    MessageType = "warning"
                };

            public static SubmitWorkflowResult Warning(string message)
                => new()
                {
                    Message = message,
                    MessageType = "warning"
                };
        }

        private (List<SupplierRow> rows, int totalRecords, int page, int pageSize) SearchSuppliers(SearchRequest request)
        {
            var viewMode = string.IsNullOrWhiteSpace(request.ViewMode) ? "current" : request.ViewMode.Trim();
            var isByYear = string.Equals(viewMode, "byyear", StringComparison.OrdinalIgnoreCase);

            var page = request.Page <= 0 ? 1 : request.Page;
            var pageSize = request.PageSize <= 0 ? DefaultPageSize : Math.Min(request.PageSize, 200);

            var restrictedDeptId = !CanLoadAllDepartments()
                ? (_dataScope.DeptID ?? NoDepartmentScopeValue)
                : request.DeptId;

            var criteria = new SupplierFilterInput
            {
                ViewMode = viewMode,
                Year = request.Year,
                DeptId = restrictedDeptId,
                SupplierCode = NullIfEmpty(request.SupplierCode),
                SupplierName = NullIfEmpty(request.SupplierName),
                Business = NullIfEmpty(request.Business),
                Contact = NullIfEmpty(request.Contact),
                StatusId = request.StatusId,
                IsNew = request.IsNew,
                PageIndex = page,
                PageSize = pageSize
            };

            var result = SearchPagedAsync(criteria, CancellationToken.None).GetAwaiter().GetResult();
            return (result.Rows, result.TotalCount, page, pageSize);
        }

        private void LoadUserDataScope()
        {
            _isAdminRole = IsAdminUser();
            if (_isAdminRole)
            {
                _dataScope = new EmployeeDataScope();
                return;
            }

            _dataScope = GetEmployeeDataScope(User.Identity?.Name);
            if (!CanLoadAllDepartments())
            {
                DeptId = _dataScope.DeptID ?? NoDepartmentScopeValue;
            }
        }

        private EmployeeDataScope GetEmployeeDataScope(string? employeeCode)
        {
            if (string.IsNullOrWhiteSpace(employeeCode))
            {
                return new EmployeeDataScope();
            }

            const string sql = @"
            SELECT TOP 1 DeptID, ISNULL(LevelCheckSupplier, 0) AS LevelCheckSupplier
            FROM dbo.MS_Employee
            WHERE EmployeeCode = @EmployeeCode";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add("@EmployeeCode", SqlDbType.NVarChar, 50).Value = employeeCode.Trim();

            conn.Open();
            using var rd = cmd.ExecuteReader();
            if (!rd.Read())
            {
                return new EmployeeDataScope();
            }

        return new EmployeeDataScope
        {
            DeptID = rd.IsDBNull(0) ? null : Convert.ToInt32(rd[0]),
            LevelCheckSupplier = rd.IsDBNull(1) ? 0 : Convert.ToInt32(rd[1])
        };
    }

        private bool CanLoadAllDepartments()
        {
            return _isAdminRole || _dataScope.LevelCheckSupplier is 1 or 3 or 4;
        }

        private void LoadDepartments()
        {
            var list = new List<(int Id, string Name)>();
            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand("SELECT DeptID, DeptCode FROM dbo.MS_Department ORDER BY DeptCode", conn);
            conn.Open();
            using (var rd = cmd.ExecuteReader())
            {
                while (rd.Read())
                {
                    list.Add((Convert.ToInt32(rd[0]), rd[1]?.ToString() ?? string.Empty));
                }
            }

            var departments = Helper.BuildIntSelectList(list, x => x.Id, x => x.Name, showAll: CanLoadAllDepartments());

            if (!CanLoadAllDepartments())
            {
                if (_dataScope.DeptID.HasValue)
                {
                    Departments = departments.Where(x => x.Value == _dataScope.DeptID.Value.ToString()).ToList();
                    DeptId = _dataScope.DeptID.Value;
                }
                else
                {
                    Departments = new List<SelectListItem>();
                    DeptId = NoDepartmentScopeValue;
                }
            }
            else
            {
                Departments = departments;
            }

            var selectedDeptValue = DeptId?.ToString() ?? string.Empty;
            SelectedDeptText = Departments.FirstOrDefault(x => x.Value == selectedDeptValue)?.Text ?? string.Empty;
        }

        private void LoadSupplierStatuses()
        {
            var list = new List<(int Id, string Name)>();
            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand("SELECT SupplierStatusID, SupplierStatusName FROM dbo.PC_SupplierStatus ORDER BY SupplierStatusID", conn);
            conn.Open();
            using (var rd = cmd.ExecuteReader())
            {
                while (rd.Read())
                {
                    list.Add((Convert.ToInt32(rd[0]), rd[1]?.ToString() ?? string.Empty));
                }
            }

            Statuses = Helper.BuildIntSelectList(list, x => x.Id, x => x.Name, showAll: true);
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
            var restrictedDeptId = !CanLoadAllDepartments()
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
        private static string BuildSubmitMessage(int submittedCount, int notFoundCount, int noAccessCount, int noPermissionCount, int notDraftCount, int alreadySubmittedCount, int totalSelected)
        {
            if (totalSelected == 1)
            {
                if (submittedCount == 1) return "Supplier submitted successfully.";
                if (notFoundCount == 1) return "Supplier not found.";
                if (noAccessCount == 1) return "You do not have permission to submit this supplier.";
                if (noPermissionCount == 1) return "You do not have permission to submit this supplier.";
                if (notDraftCount == 1) return "Only Draft supplier can be submitted.";
                if (alreadySubmittedCount == 1) return "Supplier has already been submitted.";
            }

            var parts = new List<string>();
            if (submittedCount > 0) parts.Add($"submitted: {submittedCount}");
            if (notFoundCount > 0) parts.Add($"not found: {notFoundCount}");
            if (noAccessCount > 0) parts.Add($"no access: {noAccessCount}");
            if (noPermissionCount > 0) parts.Add($"no permission: {noPermissionCount}");
            if (notDraftCount > 0) parts.Add($"not draft: {notDraftCount}");
            if (alreadySubmittedCount > 0) parts.Add($"already submitted: {alreadySubmittedCount}");

            return parts.Count == 0
                ? "No supplier status was changed."
                : $"Submit completed. {string.Join(", ", parts)}.";
        }

        private static string BuildSubmitAjaxMessage(SubmitWorkflowResult result)
        {
            var submittedCodes = result.SubmittedSupplierCodes
                .Where(code => !string.IsNullOrWhiteSpace(code))
                .Select(code => code.Trim())
                .ToList();

            if (result.Success && submittedCodes.Count == 1)
            {
                return $"Supplier code {submittedCodes[0]} submitted successfully.";
            }

            if (result.Success && submittedCodes.Count > 1)
            {
                return $"Suppliers submitted successfully: {string.Join(", ", submittedCodes)}.";
            }

            if (result.IsForbidden)
            {
                return result.Message;
            }

            return BuildSubmitMessage(
                result.SubmittedCount,
                result.NotFoundCount,
                result.NoAccessCount,
                result.NoPermissionCount,
                result.NotDraftCount,
                result.AlreadySubmittedCount,
                result.TotalSelected);
        }

        // Lay data scope user (DeptID + LevelCheckSupplier).
        // Supplier co data scope theo phong ban nen can ham rieng.
        private async Task<EmployeeDataScope> GetEmployeeDataScopeAsync(string? employeeCode, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(employeeCode))
            {
                return new EmployeeDataScope();
            }

            const string sql = @"
                SELECT TOP 1 DeptID, ISNULL(LevelCheckSupplier, 0) AS LevelCheckSupplier
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
                LevelCheckSupplier = rd.IsDBNull(1) ? 0 : Convert.ToInt32(rd[1])
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
                SELECT TOP 1 SupplierCode, DeptID, [Status], PurchaserPreparedDate
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
                SupplierCode = rd[0]?.ToString(),
                DeptID = rd.IsDBNull(1) ? null : Convert.ToInt32(rd[1]),
                Status = rd.IsDBNull(2) ? null : Convert.ToInt32(rd[2]),
                PurchaserPreparedDate = rd.IsDBNull(3) ? null : Convert.ToDateTime(rd[3])
            };
        }

        // Submit supplier vao workflow:
        // - Dua supplier vao step da duoc level 1 xac nhan (Status = 1)
        // - Ghi nhan nguoi submit + thoi gian submit.
        private async Task SubmitSupplierWorkflowAsync(int supplierId, string operatorCode, CancellationToken cancellationToken)
        {
            const string sql = @"
                UPDATE dbo.PC_Suppliers
                SET [Status] = 1,
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
            _isAdminRole = IsAdminUser();
            if (_isAdminRole)
            {
                _dataScope = new EmployeeDataScope();
                return;
            }

            _dataScope = await GetEmployeeDataScopeAsync(User.Identity?.Name, cancellationToken);
            if (!CanLoadAllDepartments())
            {
                DeptId = _dataScope.DeptID ?? NoDepartmentScopeValue;
            }
        }

        // Kiem tra record co nam trong pham vi phong ban user duoc xem/lam hay khong.
        private bool CanAccessDepartment(int? supplierDeptId)
        {
            if (CanLoadAllDepartments())
            {
                return true;
            }

            if (!_dataScope.DeptID.HasValue || !supplierDeptId.HasValue)
            {
                return false;
            }

            return _dataScope.DeptID.Value == supplierDeptId.Value;
        }

        private PagePermissions GetUserPermissions()
        {
            bool isAdmin = User.FindFirst("IsAdminRole")?.Value == "True";
            int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "0");

            // 1. Khởi tạo đối tượng PagePermissions mới
            var permsObj = new PagePermissions();

            if (isAdmin)
            {
                // Admin: Gán danh sách quyền giả lập
                permsObj.AllowedNos = Enumerable.Range(1, 20).ToList();
            }
            else
            {
                // 2. Lấy danh sách List<int> từ Service và gán vào thuộc tính AllowedNos của Object
                permsObj.AllowedNos = _permissionService.GetPermissionsForPage(roleId, FUNCTION_ID);
            }

            // 3. Trả về đối tượng (Object) chứa danh sách đó
            return permsObj;
        }

        // Check admin role theo claim moi ("True"/"1"), co fallback DB theo IsAdminUser cua employee.
        private bool IsAdminUser()
        {
            var adminClaim = (User.FindFirst("IsAdminRole")?.Value ?? string.Empty).Trim();
            if (string.Equals(adminClaim, "True", StringComparison.OrdinalIgnoreCase)
                || string.Equals(adminClaim, "1", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var employeeCode = User.Identity?.Name?.Trim();
            if (string.IsNullOrWhiteSpace(employeeCode))
            {
                return false;
            }

            try
            {
                using var conn = new SqlConnection(_connectionString);
                using var cmd = new SqlCommand("SELECT TOP 1 IsAdminUser FROM MS_Employee WHERE EmployeeCode = @EmployeeCode AND IsActive = 1", conn);
                cmd.Parameters.Add("@EmployeeCode", SqlDbType.NVarChar, 50).Value = employeeCode;
                conn.Open();
                var value = cmd.ExecuteScalar();
                if (value == null || value == DBNull.Value)
                {
                    return false;
                }

                return Convert.ToBoolean(value);
            }
            catch
            {
                return false;
            }
        }

        // Set flash message de hien sau redirect.
        private void SetFlashMessage(string message, string type = "info")
        {
            FlashMessage = message;
            FlashMessageType = type;
            TempData["SuccessMessage"] = message;
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

    // --- CÁC CLASS DỮ LIỆU ---
    public class SearchRequest
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
        public int LevelCheckSupplier { get; set; }
    }

        public class SupplierSubmitInfo
        {
            public string? SupplierCode { get; set; }
            public int? DeptID { get; set; }
            public int? Status { get; set; }
            public DateTime? PurchaserPreparedDate { get; set; }
        }

}
