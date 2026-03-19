using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;
using SmartSam.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq;

namespace SmartSam.Pages.Purchasing.Supplier
{
    public class SupplierDetailModel : BasePageModel
    {
        // Tracking comment: keep Supplier detail page model marked as touched for current work.
        private readonly ISecurityService _securityService;
        private readonly PermissionService _permissionService;
        private readonly string _connectionString;

        private const string AnnualYearColumn = "ForYear";

        private const int FUNCTION_ID = 71;

        private const int PermissionViewDetail = 2;
        private const int PermissionAdd = 3;
        private const int PermissionEdit = 4;
        private const int PermissionSubmit = 5;

        private EmployeeDataScopeViewModel _dataScope = new EmployeeDataScopeViewModel();
        private bool _isAdminRole;
        private List<int> _effectivePerms = new List<int>();

        // Constructor truyền config vào BasePageModel
        public SupplierDetailModel(ISecurityService securityService, PermissionService permissionService, IConfiguration config) : base(config)
        {
            _securityService = securityService;
            _permissionService = permissionService;
            _connectionString = _config.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Missing connection string: DefaultConnection");
        }

        [BindProperty(SupportsGet = true)]
        public int? Id { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? ViewMode { get; set; }

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
        public int? PageSize { get; set; }

        [BindProperty(SupportsGet = true)]
        public string Mode { get; set; } = "view";

        [BindProperty]
        public SupplierViewModel Input { get; set; } = new SupplierViewModel();

        public List<SelectListItem> Departments { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> Statuses { get; set; } = new List<SelectListItem>();
        public List<SupplierApprovalHistoryViewModel> Histories { get; set; } = new List<SupplierApprovalHistoryViewModel>();

        public string? Message { get; set; }
        public string MessageType { get; set; } = "info";

        [TempData]
        public string? FlashMessage { get; set; }

        [TempData]
        public string? FlashMessageType { get; set; }

        public bool IsEdit => Id.HasValue && Id.Value > 0;
        public bool IsViewMode => string.Equals(Mode, "view", StringComparison.OrdinalIgnoreCase);
        public bool IsAnnualView => string.Equals(ViewMode, "byyear", StringComparison.OrdinalIgnoreCase) && Year.HasValue;
        public bool HasSubmitPermission => _effectivePerms.Contains(PermissionSubmit);
        public bool CanSave => !IsViewMode && !IsAnnualView && (IsEdit ? _effectivePerms.Contains(PermissionEdit) : _effectivePerms.Contains(PermissionAdd));
        public bool IsDisapproved => (Input.Status ?? 0) == 9;
        public bool IsSubmitted => IsWorkflowSubmitted(Input);
        public bool CanSubmit => !IsViewMode && !IsAnnualView && IsEdit && _effectivePerms.Contains(PermissionSubmit) && !IsSubmitted;
        public bool CanReuse => !IsViewMode && !IsAnnualView && IsEdit && _effectivePerms.Contains(PermissionEdit) && IsDisapproved && (_isAdminRole || IsReuseOwner());

        private void LoadSupplierData(int id)
        {
            var detail = IsAnnualView
                ? GetAnnualDetail(id, Year!.Value)
                : GetDetail(id);

            Input = detail;
        }

        public async Task<IActionResult> OnGetAsync(int? id, string mode = "view")
        {
            // 1. Lấy thông tin Role của User hiện tại
            int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "0");

            // Gán mặc định ban đầu
            Id = id;
            Mode = mode ?? "view";

            LoadUserDataScope();

            if (Id.HasValue && Id.Value > 0)
            {
                // 2. Load dữ liệu thực tế
                LoadSupplierData(Id.Value);

                if (Input == null)
                {
                    return NotFound();
                }

                if (!CanAccessDepartment(Input.DeptID))
                {
                    return Forbid();
                }

                // 3. BIỆN LUẬN BẢO MẬT GIAI ĐOẠN 3: Giao thoa Quyền hệ thống & Trạng thái đối tượng
                // Gọi Service để lấy tập quyền "thực tế" (đã giao thoa permission + status)
                var effectiveStatus = GetSupplierPermissionStatus(Input, IsAnnualView);
                _effectivePerms = _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, effectiveStatus);

                // Bước A: Kiểm tra quyền truy cập bản ghi (Mã 2: View)
                if (!_effectivePerms.Contains(PermissionViewDetail))
                {
                    return RedirectToPage("./Index", new { msg = "Record does not exist or you have no permission to access." });
                }

                // Bước B: Ép Mode dựa trên trạng thái (Mã 4: Edit)
                // Nếu yêu cầu sửa (mode=edit) nhưng tập quyền thực tế không cho phép (do trạng thái hoặc do role)
                if (Mode == "edit" && !_effectivePerms.Contains(PermissionEdit))
                {
                    Mode = "view";
                }

                if (IsAnnualView || IsWorkflowSubmitted(Input))
                {
                    Mode = "view";
                }
            }
            else
            {
                // TRƯỜNG HỢP ADD MỚI: Kiểm tra mã quyền 3 (Add) từ SecurityService
                // Truyền trạng thái 0 (vì chưa có bản ghi) để check quyền Add
                var addPerms = _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, 0);
                _effectivePerms = addPerms;

                if (!addPerms.Contains(PermissionAdd))
                {
                    return RedirectToPage("./Index", new { msg = "You do not have permission to do it." });
                }

                Mode = "add";
                Input.Status = 0;

                if (!_isAdminRole && !_dataScope.SeeDataAllDept)
                {
                    if (!_dataScope.DeptID.HasValue)
                    {
                        return Forbid();
                    }

                    Input.DeptID = _dataScope.DeptID.Value;
                }
            }

            LoadAllDropdowns();
            ApplyFlashMessage();

            if (Id.HasValue && Id.Value > 0)
            {
                Histories = IsAnnualView
                    ? GetAnnualApprovalHistory(Id.Value, Year)
                    : GetApprovalHistory(Id.Value);
            }

            return Page();
        }

        private static int GetSupplierPermissionStatus(SupplierViewModel supplier, bool isAnnualView)
        {
            if (isAnnualView)
            {
                return 99;
            }

            if (supplier.PurchaserPreparedDate.HasValue)
            {
                return 1;
            }

            return supplier.Status ?? 0;
        }

        // AJAX kiểm tra trùng SupplierCode khi user nhập.
        // Kiểm tra thêm quyền add/edit và phạm vi dữ liệu (scope) theo phòng ban.
        public JsonResult OnGetCheckSupplierCode(string? supplierCode, int? id)
        {
            int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "0");
            LoadUserDataScope();

            int statusToCheck = 0;
            if (id.HasValue && id.Value > 0)
            {
                var current = GetDetail(id.Value);
                if (current != null)
                {
                    statusToCheck = GetSupplierPermissionStatus(current, isAnnualView: false);
                }
            }

            var currentPerms = _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, statusToCheck);
            bool canAccess = (id.HasValue && id.Value > 0) ? currentPerms.Contains(PermissionEdit) : currentPerms.Contains(PermissionAdd);
            if (!canAccess)
            {
                return new JsonResult(new { ok = false, message = "Forbidden" })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }

            if (id.HasValue && id.Value > 0)
            {
                var supplierDept = GetSupplierDepartment(id.Value, "current", null);
                if (!CanAccessDepartment(supplierDept))
                {
                    return new JsonResult(new { ok = false, message = "Forbidden" })
                    {
                        StatusCode = StatusCodes.Status403Forbidden
                    };
                }
            }

            var code = (supplierCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                return new JsonResult(new { ok = true, exists = false });
            }

            var exists = SupplierCodeExists(code, id);
            return new JsonResult(new { ok = true, exists = exists });
        }

        // AJAX gợi ý mã Supplier mới theo quy tắc SP + số tăng dần.
        public JsonResult OnGetSuggestSupplierCode()
        {
            int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "0");
            LoadUserDataScope();

            var currentPerms = _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, 0);
            if (!currentPerms.Contains(PermissionAdd))
            {
                return new JsonResult(new { ok = false, message = "Forbidden" })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }

            var suggestion = GetSuggestedSupplierCode();
            return new JsonResult(new { ok = true, supplierCode = suggestion });
        }

        // AJAX tải lịch sử phê duyệt; tbody render bằng JS.
        public IActionResult OnGetApprovalHistory(int supplierId, string? viewMode, int? year)
        {
            int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "0");
            LoadUserDataScope();

            int statusToCheck = 0;
            var isAnnual = string.Equals(viewMode, "byyear", StringComparison.OrdinalIgnoreCase) && year.HasValue;
            if (isAnnual)
            {
                statusToCheck = 99;
            }
            else
            {
                var current = GetDetail(supplierId);
                if (current != null)
                {
                    statusToCheck = GetSupplierPermissionStatus(current, isAnnualView: false);
                }
            }

            var currentPerms = _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, statusToCheck);
            if (!currentPerms.Contains(PermissionViewDetail))
            {
                return new JsonResult(new { success = false, message = "Forbidden" })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }

            var supplierDept = GetSupplierDepartment(supplierId, viewMode, year);
            if (!CanAccessDepartment(supplierDept))
            {
                return new JsonResult(new { success = false, message = "Forbidden" })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }

            var rows = isAnnual
                ? GetAnnualApprovalHistory(supplierId, year!.Value)
                : GetApprovalHistory(supplierId);

            var data = rows.Select(x => new
            {
                action = x.Action,
                userName = x.UserName,
                actionDate = x.ActionDate?.ToString("dd/MM/yyyy HH:mm") ?? string.Empty
            });

            return new JsonResult(new { success = true, data });
        }

        private PagePermissions GetUserPermissions()
        {
            bool isAdmin = IsAdminUser();
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

        // Nạp data scope của user login hiện tại (Dept + SeeDataAllDept).
        private void LoadUserDataScope()
        {
            _isAdminRole = IsAdminUser();
            if (_isAdminRole)
            {
                _dataScope = new EmployeeDataScopeViewModel { SeeDataAllDept = true };
                return;
            }

            _dataScope = GetEmployeeDataScope(User.Identity?.Name);
        }

        // Check admin role theo claim moi ("True"/"1"), co fallback DB theo RoleID.
        private bool IsAdminUser()
        {
            var adminClaim = (User.FindFirst("IsAdminRole")?.Value ?? string.Empty).Trim();
            if (string.Equals(adminClaim, "True", StringComparison.OrdinalIgnoreCase)
                || string.Equals(adminClaim, "1", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!int.TryParse(User.FindFirst("RoleID")?.Value, out var roleId) || roleId <= 0)
            {
                return false;
            }

            try
            {
                using var conn = new SqlConnection(_connectionString);
                using var cmd = new SqlCommand("SELECT TOP 1 IsAdminRole FROM SYS_Role WHERE RoleID = @RoleID", conn);
                cmd.Parameters.Add("@RoleID", SqlDbType.Int).Value = roleId;
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

        // Đọc scope user từ bảng MS_Employee.
        private EmployeeDataScopeViewModel GetEmployeeDataScope(string? employeeCode)
        {
            if (string.IsNullOrWhiteSpace(employeeCode))
            {
                return new EmployeeDataScopeViewModel();
            }

            const string sql = @"
                                SELECT TOP 1 DeptID, ISNULL(SeeDataAllDept, 0) AS SeeDataAllDept
                                FROM dbo.MS_Employee
                                WHERE EmployeeCode = @EmployeeCode";

            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@EmployeeCode", employeeCode.Trim());
                conn.Open();

                using (var rd = cmd.ExecuteReader())
                {
                    if (!rd.Read())
                    {
                        return new EmployeeDataScopeViewModel();
                    }

                    return new EmployeeDataScopeViewModel
                    {
                        DeptID = rd.IsDBNull(0) ? null : Convert.ToInt32(rd[0]),
                        SeeDataAllDept = !rd.IsDBNull(1) && Convert.ToBoolean(rd[1])
                    };
                }
            }
        }

        // Kiểm tra record supplier có nằm trong phạm vi dept user được thao tác không.
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

        // Lấy DeptID của supplier theo current/annual để phục vụ check scope.
        private int? GetSupplierDepartment(int supplierId, string? viewMode, int? year)
        {
            var isByYear = string.Equals(viewMode, "byyear", StringComparison.OrdinalIgnoreCase);
            var tableName = isByYear ? "dbo.PC_SupplierAnualy" : "dbo.PC_Suppliers";
            var yearSql = isByYear ? $" AND [{AnnualYearColumn}] = @Year" : string.Empty;

            var sql = $@"
                        SELECT TOP 1 DeptID
                        FROM {tableName}
                        WHERE SupplierID = @SupplierID{yearSql}";

            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@SupplierID", supplierId);
                if (isByYear)
                {
                    cmd.Parameters.AddWithValue("@Year", (object?)year ?? DBNull.Value);
                }

                conn.Open();
                var result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
            }
        }

        // Save/Add/Edit/Submit/Reuse Supplier (theo action của nút submit).
        // Ngoài UI, backend vẫn check quyền + scope để chặn gọi trực tiếp.
        public IActionResult OnPost()
        {
            int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "0");
            LoadUserDataScope();

            if (IsViewMode || IsAnnualView)
            {
                return Forbid();
            }

            var normalizedAction = ((string?)Request.Form["action"]).Trim();
            normalizedAction = string.IsNullOrWhiteSpace(normalizedAction) ? "save" : normalizedAction.ToLowerInvariant();

            if (normalizedAction == "submit")
            {
                return SubmitApprovalCore();
            }

            if (normalizedAction == "reuse")
            {
                return ReuseCore();
            }

            int statusToCheck = 0;
            if (IsEdit)
            {
                var current = GetDetail(Id!.Value);
                if (current != null)
                {
                    statusToCheck = GetSupplierPermissionStatus(current, isAnnualView: false);
                }
            }

            var currentPerms = _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, statusToCheck);
            bool hasPermission = IsEdit ? currentPerms.Contains(PermissionEdit) : currentPerms.Contains(PermissionAdd);
            if (!hasPermission)
            {
                return Forbid();
            }

            LoadAllDropdowns();
            SupplierViewModel? currentDetail = null;

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

                Input.Status = 0;
            }
            else
            {
                currentDetail = GetDetail(Id!.Value);
                if (currentDetail == null)
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

                if (IsWorkflowSubmitted(currentDetail))
                {
                    Input = currentDetail;
                    Histories = GetApprovalHistory(Id.Value);
                    SetMessage("Supplier is in approval workflow and is read-only.", "warning");
                    return Page();
                }

                Input.Status = currentDetail.Status;
            }

            var code = (Input.SupplierCode ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(code) && SupplierCodeExists(code, IsEdit ? Id : null))
            {
                ModelState.AddModelError("Input.SupplierCode", "Supplier code already exists.");
            }

            if (!ModelState.IsValid)
            {
                if (IsEdit)
                {
                    Histories = GetApprovalHistory(Id!.Value);
                }

                return Page();
            }

            if (IsEdit)
            {
                Input.Status = currentDetail?.Status;
            }

            var operatorCode = User.Identity?.Name ?? "SYSTEM";
            int savedId;
            try
            {
                using var conn = new SqlConnection(_connectionString);
                conn.Open();
                using var trans = conn.BeginTransaction();

                savedId = SaveSupplier(conn, trans, operatorCode);
                trans.Commit();
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
                    Histories = GetApprovalHistory(Id!.Value);
                }

                return Page();
            }
            catch (SqlException ex)
            {
                if (IsEdit)
                {
                    Histories = GetApprovalHistory(Id!.Value);
                }

                var friendlyMessage = ex.Number == 515 && ex.Message.Contains("SupplierName", StringComparison.OrdinalIgnoreCase)
                    ? "Cannot save this supplier. Supplier Name is required."
                    : "Cannot save supplier due to database validation rules. Please review the form data and try again.";

                SetMessage(friendlyMessage, "error");
                return Page();
            }
            catch
            {
                if (IsEdit)
                {
                    Histories = GetApprovalHistory(Id!.Value);
                }

                SetMessage("An unexpected error occurred while saving supplier. Please try again.", "error");
                return Page();
            }

            if (!IsEdit)
            {
                SetFlashMessage("Supplier created successfully.", "success");
                return RedirectToPage("./SupplierDetail", BuildDetailRouteValues(savedId));
            }

            SetFlashMessage("Saved successfully.", "success");
            return RedirectToPage("./SupplierDetail", BuildDetailRouteValues(savedId));
        }

        private IActionResult SubmitApprovalCore()
        {
            int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "0");

            if (!IsEdit)
            {
                return RedirectToPage("./Index", BuildListRouteValues());
            }

            LoadAllDropdowns();

            var current = GetDetail(Id!.Value);
            if (current == null)
            {
                SetMessage("Supplier not found.", "error");
                return Page();
            }

            int statusToCheck = GetSupplierPermissionStatus(current, isAnnualView: false);
            var currentPerms = _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, statusToCheck);
            if (!currentPerms.Contains(PermissionSubmit))
            {
                return Forbid();
            }

            Input = current;
            if (!CanAccessDepartment(Input.DeptID))
            {
                return Forbid();
            }

            if (IsSubmitted)
            {
                Histories = GetApprovalHistory(Id.Value);
                SetMessage("Supplier has already been submitted.", "warning");
                return Page();
            }

            var operatorCode = User.Identity?.Name ?? "SYSTEM";
            SubmitApproval(Id!.Value, operatorCode);

            SetFlashMessage("Supplier submitted successfully.", "success");
            return RedirectToPage("./SupplierDetail", BuildDetailRouteValues(Id.Value));
        }

        private IActionResult ReuseCore()
        {
            int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "0");

            if (!IsEdit)
            {
                return RedirectToPage("./Index", BuildListRouteValues());
            }

            LoadAllDropdowns();

            var current = GetDetail(Id!.Value);
            if (current == null)
            {
                SetMessage("Supplier not found.", "error");
                return Page();
            }

            int statusToCheck = GetSupplierPermissionStatus(current, isAnnualView: false);
            var currentPerms = _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, statusToCheck);
            if (!currentPerms.Contains(PermissionEdit))
            {
                return Forbid();
            }

            Input = current;
            if (!CanAccessDepartment(Input.DeptID))
            {
                return Forbid();
            }

            if (!IsDisapproved)
            {
                Histories = GetApprovalHistory(Id.Value);
                SetMessage("Only disapproved supplier can Re Use.", "warning");
                return Page();
            }

            if (!_isAdminRole && !IsReuseOwner())
            {
                return Forbid();
            }

            ResetForReuse(Id.Value);
            SetFlashMessage("Re Use successful. Supplier is reset to draft (status 0).", "success");
            return RedirectToPage("./SupplierDetail", BuildDetailRouteValues(Id.Value));
        }

        private object BuildListRouteValues() => new
        {
            ViewMode,
            Year,
            DeptId,
            Business,
            SupplierCode,
            Contact,
            SupplierName,
            StatusId,
            IsNew,
            PageIndex,
            PageSize
        };

        private object BuildDetailRouteValues(int supplierId) => new
        {
            id = supplierId,
            mode = "edit",
            viewMode = ViewMode,
            year = Year
        };

        // Nạp dropdown Department + Status cho form detail.
        // Department bị giới hạn theo scope nếu user không có quyền xem all dept.
        private void LoadAllDropdowns()
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
                if (!_dataScope.DeptID.HasValue)
                {
                    Departments = new List<SelectListItem>();
                    Input.DeptID = null;
                    return;
                }

                Departments = departments
                    .Where(x => int.TryParse(x.Value, out var deptId) && deptId == _dataScope.DeptID.Value)
                    .ToList();

                Input.DeptID = _dataScope.DeptID.Value;
            }
            else
            {
                Departments = departments;
            }

            Statuses = statuses;
        }

        // Load chi tiet supplier tu bang current.
        private SupplierViewModel? GetDetail(int supplierId)
        {
            const string sql = @"
                                SELECT SupplierCode,SupplierName,Address,Phone,Mobile,Fax,Contact,[Position],Business,
                                    ApprovedDate,[Document],Certificate,Service,Comment,IsNew,CodeOfAcc,DeptID,[Status],
                                    PurchaserCode,PurchaserPreparedDate
                                FROM dbo.PC_Suppliers
                                WHERE SupplierID = @ID";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ID", supplierId);
            conn.Open();

            using var rd = cmd.ExecuteReader();
            if (!rd.Read())
            {
                return null;
            }

            return new SupplierViewModel
            {
                SupplierCode = rd[0]?.ToString(),
                SupplierName = rd[1]?.ToString(),
                Address = rd[2]?.ToString(),
                Phone = rd[3]?.ToString(),
                Mobile = rd[4]?.ToString(),
                Fax = rd[5]?.ToString(),
                Contact = rd[6]?.ToString(),
                Position = rd[7]?.ToString(),
                Business = rd[8]?.ToString(),
                ApprovedDate = rd.IsDBNull(9) ? null : rd.GetDateTime(9),
                Document = !rd.IsDBNull(10) && Convert.ToBoolean(rd[10]),
                Certificate = rd[11]?.ToString(),
                Service = rd[12]?.ToString(),
                Comment = rd[13]?.ToString(),
                IsNew = !rd.IsDBNull(14) && Convert.ToBoolean(rd[14]),
                CodeOfAcc = rd[15]?.ToString(),
                DeptID = rd.IsDBNull(16) ? null : Convert.ToInt32(rd[16]),
                Status = rd.IsDBNull(17) ? null : Convert.ToInt32(rd[17]),
                PurchaserCode = rd[18]?.ToString(),
                PurchaserPreparedDate = rd.IsDBNull(19) ? null : Convert.ToDateTime(rd[19])
            };
        }

        // Load chi tiet supplier tu bang annual theo nam.
        private SupplierViewModel? GetAnnualDetail(int supplierId, int year)
        {
            var sql = $@"
                        SELECT SupplierCode,SupplierName,Address,Phone,Mobile,Fax,Contact,[Position],Business,
                            ApprovedDate,[Document],Certificate,Service,Comment,IsNew,CodeOfAcc,DeptID,[Status],
                            PurchaserCode,PurchaserPreparedDate
                        FROM dbo.PC_SupplierAnualy
                        WHERE SupplierID = @ID
                        AND [{AnnualYearColumn}] = @Year";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ID", supplierId);
            cmd.Parameters.AddWithValue("@Year", year);
            conn.Open();

            using var rd = cmd.ExecuteReader();
            if (!rd.Read())
            {
                return null;
            }

            return new SupplierViewModel
            {
                SupplierCode = rd[0]?.ToString(),
                SupplierName = rd[1]?.ToString(),
                Address = rd[2]?.ToString(),
                Phone = rd[3]?.ToString(),
                Mobile = rd[4]?.ToString(),
                Fax = rd[5]?.ToString(),
                Contact = rd[6]?.ToString(),
                Position = rd[7]?.ToString(),
                Business = rd[8]?.ToString(),
                ApprovedDate = rd.IsDBNull(9) ? null : rd.GetDateTime(9),
                Document = !rd.IsDBNull(10) && Convert.ToBoolean(rd[10]),
                Certificate = rd[11]?.ToString(),
                Service = rd[12]?.ToString(),
                Comment = rd[13]?.ToString(),
                IsNew = !rd.IsDBNull(14) && Convert.ToBoolean(rd[14]),
                CodeOfAcc = rd[15]?.ToString(),
                DeptID = rd.IsDBNull(16) ? null : Convert.ToInt32(rd[16]),
                Status = rd.IsDBNull(17) ? null : Convert.ToInt32(rd[17]),
                PurchaserCode = rd[18]?.ToString(),
                PurchaserPreparedDate = rd.IsDBNull(19) ? null : Convert.ToDateTime(rd[19])
            };
        }

        // Lấy lịch sử phê duyệt cho current supplier.
        private List<SupplierApprovalHistoryViewModel> GetApprovalHistory(int supplierId)
        {
            const string sql = @"
                WITH ApprovalHistory AS
                (
                    SELECT 'Purchasing Officer submitted' AS [Action], PurchaserCode AS [UserCode], PurchaserPreparedDate AS [ActionDate]
                    FROM dbo.PC_Suppliers
                    WHERE SupplierID = @ID

                    UNION ALL
                    SELECT 'Head Department approved/dis', DepartmentCode, DepartmentApproveDate
                    FROM dbo.PC_Suppliers
                    WHERE SupplierID = @ID

                    UNION ALL
                    SELECT 'Head Financial approved/dis', FinancialCode, FinancialApproveDate
                    FROM dbo.PC_Suppliers
                    WHERE SupplierID = @ID

                    UNION ALL
                    SELECT 'BOD approved/dis', BODCode, BODApproveDate
                    FROM dbo.PC_Suppliers
                    WHERE SupplierID = @ID
                )
                SELECT
                    h.[Action],
                    COALESCE(NULLIF(e.EmployeeName, ''), h.[UserCode], '') AS [UserName],
                    h.[ActionDate]
                FROM ApprovalHistory h
                LEFT JOIN dbo.MS_Employee e ON e.EmployeeCode = h.[UserCode]";

            return LoadApprovalHistory(sql, supplierId, null);
        }

        // Lấy lịch sử phê duyệt cho annual supplier.
        private List<SupplierApprovalHistoryViewModel> GetAnnualApprovalHistory(int supplierId, int? year)
        {
            var sql = $@"
                WITH ApprovalHistory AS
                (
                    SELECT 'Purchasing Officer submitted' AS [Action], PurchaserCode AS [UserCode], PurchaserPreparedDate AS [ActionDate]
                    FROM dbo.PC_SupplierAnualy
                    WHERE SupplierID = @ID
                      AND [{AnnualYearColumn}] = @Year

                    UNION ALL
                    SELECT 'Head Department approved/dis', DepartmentCode, DepartmentApproveDate
                    FROM dbo.PC_SupplierAnualy
                    WHERE SupplierID = @ID
                      AND [{AnnualYearColumn}] = @Year

                    UNION ALL
                    SELECT 'Head Financial approved/dis', FinancialCode, FinancialApproveDate
                    FROM dbo.PC_SupplierAnualy
                    WHERE SupplierID = @ID
                      AND [{AnnualYearColumn}] = @Year

                    UNION ALL
                    SELECT 'BOD approved/dis', BODCode, BODApproveDate
                    FROM dbo.PC_SupplierAnualy
                    WHERE SupplierID = @ID
                      AND [{AnnualYearColumn}] = @Year
                )
                SELECT
                    h.[Action],
                    COALESCE(NULLIF(e.EmployeeName, ''), h.[UserCode], '') AS [UserName],
                    h.[ActionDate]
                FROM ApprovalHistory h
                LEFT JOIN dbo.MS_Employee e ON e.EmployeeCode = h.[UserCode]";

            return LoadApprovalHistory(sql, supplierId, year);
        }

        // Hàm dùng chung để map kết quả lịch sử phê duyệt.
        private List<SupplierApprovalHistoryViewModel> LoadApprovalHistory(string sql, int supplierId, int? year)
        {
            var rows = new List<SupplierApprovalHistoryViewModel>();

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@ID", supplierId);
            if (year.HasValue)
            {
                cmd.Parameters.AddWithValue("@Year", year.Value);
            }

            conn.Open();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                rows.Add(new SupplierApprovalHistoryViewModel
                {
                    Action = rd[0]?.ToString() ?? string.Empty,
                    UserName = rd[1]?.ToString() ?? string.Empty,
                    ActionDate = rd.IsDBNull(2) ? null : rd.GetDateTime(2)
                });
            }

            return rows;
        }

        // Kiểm tra trùng mã supplier (bỏ qua supplier hiện tại khi edit).
        private bool SupplierCodeExists(string supplierCode, int? excludeSupplierId)
        {
            const string sql = @"
                SELECT COUNT(1)
                FROM dbo.PC_Suppliers
                WHERE LTRIM(RTRIM(ISNULL(SupplierCode, ''))) = LTRIM(RTRIM(@SupplierCode))
                  AND (@ExcludeSupplierId IS NULL OR SupplierID <> @ExcludeSupplierId);";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@SupplierCode", supplierCode.Trim());
            cmd.Parameters.AddWithValue("@ExcludeSupplierId", (object?)excludeSupplierId ?? DBNull.Value);
            conn.Open();

            var result = cmd.ExecuteScalar();
            return Convert.ToInt32(result) > 0;
        }

        // Sinh mã supplier gợi ý (SP + số lớn nhất + 1).
        private string GetSuggestedSupplierCode()
        {
            const string sql = @"
                SELECT
                    ISNULL(MAX(TRY_CONVERT(int, SUBSTRING(LTRIM(RTRIM(SupplierCode)), 3, 50))), 0) AS MaxNo,
                    ISNULL(MAX(LEN(LTRIM(RTRIM(SupplierCode))) - 2), 3) AS NumWidth
                FROM dbo.PC_Suppliers
                WHERE LEFT(UPPER(LTRIM(RTRIM(SupplierCode))), 2) = 'SP'
                  AND TRY_CONVERT(int, SUBSTRING(LTRIM(RTRIM(SupplierCode)), 3, 50)) IS NOT NULL;";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            conn.Open();

            using var rd = cmd.ExecuteReader();
            var maxNo = 0;
            var numWidth = 3;
            if (rd.Read())
            {
                maxNo = rd.IsDBNull(0) ? 0 : Convert.ToInt32(rd[0]);
                numWidth = rd.IsDBNull(1) ? 3 : Convert.ToInt32(rd[1]);
            }

            return $"SP{(maxNo + 1).ToString().PadLeft(Math.Max(3, numWidth), '0')}";
        }

        // Save chinh:
        // - Add thi insert + tra ve ID moi
        // - Edit thi update theo SupplierID.
        private int SaveSupplier(SqlConnection conn, SqlTransaction trans, string operatorCode)
        {
            bool isNew = !Id.HasValue || Id.Value <= 0;
            int supplierId = Id ?? 0;

            var code = (Input.SupplierCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new InvalidOperationException("Supplier code is required.");
            }

            if (SupplierCodeExists(code, isNew ? null : supplierId))
            {
                throw new InvalidOperationException("Supplier code already exists.");
            }

            Input.SupplierCode = code;

            string sql;
            if (isNew)
            {
                sql = @"
                    INSERT INTO dbo.PC_Suppliers
                    (
                        SupplierCode,SupplierName,Address,Phone,Mobile,Fax,Contact,[Position],Business,
                        [Document],Certificate,Service,Comment,IsNew,CodeOfAcc,DeptID,[Status],
                        PurchaserCode,PurchaserPreparedDate
                    )
                    VALUES
                    (
                        @SupplierCode,@SupplierName,@Address,@Phone,@Mobile,@Fax,@Contact,@Position,@Business,
                        @Document,@Certificate,@Service,@Comment,@IsNew,@CodeOfAcc,@DeptID,0,
                        NULL,NULL
                    );
                    SELECT CAST(SCOPE_IDENTITY() as int);";
            }
            else
            {
                sql = @"
                    UPDATE dbo.PC_Suppliers
                    SET SupplierCode=@SupplierCode,
                        SupplierName=@SupplierName,
                        Address=@Address,
                        Phone=@Phone,
                        Mobile=@Mobile,
                        Fax=@Fax,
                        Contact=@Contact,
                        [Position]=@Position,
                        Business=@Business,
                        [Document]=@Document,
                        Certificate=@Certificate,
                        Service=@Service,
                        Comment=@Comment,
                        IsNew=@IsNew,
                        CodeOfAcc=@CodeOfAcc,
                        DeptID=@DeptID
                    WHERE SupplierID=@SupplierID";
            }

            using var cmd = new SqlCommand(sql, conn, trans);
            AddSupplierParameters(cmd, Input);
            if (!isNew)
            {
                cmd.Parameters.AddWithValue("@SupplierID", supplierId);
            }
            cmd.Parameters.AddWithValue("@User", operatorCode);

            if (isNew)
            {
                var result = cmd.ExecuteScalar();
                return Convert.ToInt32(result);
            }

            cmd.ExecuteNonQuery();
            return supplierId;
        }

        // Cap nhat trang thai submit vao workflow phe duyet.
        private void SubmitApproval(int supplierId, string operatorCode)
        {
            const string sql = @"
                UPDATE dbo.PC_Suppliers
                SET [Status]=0,
                    ApprovedDate=NULL,
                    PurchaserCode=@OperatorCode,
                    PurchaserPreparedDate=GETDATE(),
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
                WHERE SupplierID=@SupplierID";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@SupplierID", supplierId);
            cmd.Parameters.AddWithValue("@OperatorCode", operatorCode);
            conn.Open();
            cmd.ExecuteNonQuery();
        }

        // Reset workflow de supplier disapproved co the submit lai tu dau.
        private void ResetForReuse(int supplierId)
        {
            const string sql = @"
                UPDATE dbo.PC_Suppliers
                SET [Status]=0,
                    ApprovedDate=NULL,
                    PurchaserCode=NULL,
                    PurchaserPreparedDate=NULL,
                    PurchaserCPT = NULL,
                    DepartmentCode = NULL,
                    DepartmentApproveDate = NULL,
                    DepartmentCPT = NULL,
                    FinancialCode = NULL,
                    FinancialApproveDate = NULL,
                    FinancialCPT = NULL,
                    BODCode = NULL,
                    BODApproveDate = NULL,
                    BODCPT = NULL,
                    IsApproved = 0
                WHERE SupplierID=@SupplierID";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@SupplierID", supplierId);
            conn.Open();
            cmd.ExecuteNonQuery();
        }

        // Owner duoc phep Re Use theo du lieu hien co:
        // he thong hien tai khong co cot creator rieng trong PC_Suppliers,
        // nen owner cua luong Re Use duoc xac dinh boi PurchaserCode (submitter).
        private bool IsReuseOwner()
        {
            return IsSubmitOwner();
        }

        // User submitter cua workflow: duoc xac dinh boi PurchaserCode.
        private bool IsSubmitOwner()
        {
            var currentUser = User.Identity?.Name?.Trim();
            if (string.IsNullOrWhiteSpace(currentUser)) return false;
            if (string.IsNullOrWhiteSpace(Input.PurchaserCode)) return false;
            return string.Equals(Input.PurchaserCode.Trim(), currentUser, StringComparison.OrdinalIgnoreCase);
        }

        // Bind parameter dung chung cho insert/update supplier.
        private static void AddSupplierParameters(SqlCommand cmd, SupplierViewModel supplier)
        {
            cmd.Parameters.AddWithValue("@SupplierCode", (object?)supplier.SupplierCode ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SupplierName", (object?)supplier.SupplierName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Address", (object?)supplier.Address ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Phone", (object?)supplier.Phone ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Mobile", (object?)supplier.Mobile ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Fax", (object?)supplier.Fax ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Contact", (object?)supplier.Contact ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Position", (object?)supplier.Position ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Business", (object?)supplier.Business ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Document", supplier.Document);
            cmd.Parameters.AddWithValue("@Certificate", (object?)supplier.Certificate ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Service", (object?)supplier.Service ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@Comment", (object?)supplier.Comment ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@IsNew", supplier.IsNew);
            cmd.Parameters.AddWithValue("@CodeOfAcc", (object?)supplier.CodeOfAcc ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DeptID", (object?)supplier.DeptID ?? DBNull.Value);
        }

        // Da submit vao workflow khi co thong tin nguoi submit/thoi gian submit
        // hoac trang thai da qua cac buoc duyet tiep theo.
        private static bool IsWorkflowSubmitted(SupplierViewModel? supplier)
        {
            if (supplier == null) return false;
            if (supplier.PurchaserPreparedDate.HasValue) return true;
            return (supplier.Status ?? 0) >= 1;
        }

        // Gan flash message vao message hien thi trong trang hien tai.
        private void ApplyFlashMessage()
        {
            // Giữ hàm để tương thích luồng cũ; hiện dùng TempData cho thành công và ModelState cho lỗi.
        }

        // Set flash message de hien sau redirect.
        private void SetFlashMessage(string message, string type)
        {
            FlashMessage = message;
            FlashMessageType = type;
            TempData["SuccessMessage"] = message;
        }

        // Set message hien ngay tren response hien tai.
        private void SetMessage(string message, string type)
        {
            Message = message;
            MessageType = type;
            ModelState.AddModelError(string.Empty, message);
        }

        // Vùng 1: PC_Suppliers (thông tin chính của Supplier)
        public class SupplierViewModel
        {
            [Required(ErrorMessage = "Supplier code is required.")]
            [StringLength(10, ErrorMessage = "Supplier code must be at most 10 characters.")]
            public string? SupplierCode { get; set; }

            [Required(ErrorMessage = "Supplier name is required.")]
            [StringLength(254, ErrorMessage = "Supplier name must be at most 254 characters.")]
            public string? SupplierName { get; set; }

            [Required(ErrorMessage = "Address is required.")]
            [StringLength(254, ErrorMessage = "Address must be at most 254 characters.")]
            public string? Address { get; set; }

            [StringLength(20, ErrorMessage = "Phone must be at most 20 characters.")]
            public string? Phone { get; set; }

            [StringLength(20, ErrorMessage = "Mobile must be at most 20 characters.")]
            public string? Mobile { get; set; }

            [StringLength(20, ErrorMessage = "Fax must be at most 20 characters.")]
            public string? Fax { get; set; }

            [StringLength(40, ErrorMessage = "Contact person must be at most 40 characters.")]
            public string? Contact { get; set; }

            [StringLength(40, ErrorMessage = "Position must be at most 40 characters.")]
            public string? Position { get; set; }

            [StringLength(1000, ErrorMessage = "Business must be at most 1000 characters.")]
            public string? Business { get; set; }

            [BindNever]
            public DateTime? ApprovedDate { get; set; }

            public bool Document { get; set; }

            [StringLength(100, ErrorMessage = "Certificate must be at most 100 characters.")]
            public string? Certificate { get; set; }

            [StringLength(1000, ErrorMessage = "Service must be at most 1000 characters.")]
            public string? Service { get; set; }

            [StringLength(1000, ErrorMessage = "Comment must be at most 1000 characters.")]
            public string? Comment { get; set; }

            public bool IsNew { get; set; }

            [StringLength(20, ErrorMessage = "CodeOfAcc must be at most 20 characters.")]
            public string? CodeOfAcc { get; set; }

            [Required(ErrorMessage = "Department is required.")]
            public int? DeptID { get; set; }

            [BindNever]
            public int? Status { get; set; }

            [BindNever]
            public string? PurchaserCode { get; set; }

            [BindNever]
            public DateTime? PurchaserPreparedDate { get; set; }
        }

        // Vùng 2: Approval Information (lịch sử duyệt)
        public class SupplierApprovalHistoryViewModel
        {
            public string Action { get; set; } = string.Empty;
            public string UserName { get; set; } = string.Empty;
            public DateTime? ActionDate { get; set; }
        }

        // Vùng phụ: scope dữ liệu của user đăng nhập (Dept/SeeDataAllDept)
        public class EmployeeDataScopeViewModel
        {
            public int? DeptID { get; set; }
            public bool SeeDataAllDept { get; set; }
        }
    }
}
