using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;
using System.ComponentModel.DataAnnotations;
using System.Data;

namespace SmartSam.Pages.Purchasing.Supplier
{
    public class SupplierDetailModel : BasePageModel
    {
        private readonly PermissionService _permissionService;
        private readonly string _connectionString;

        private const string AnnualYearColumn = "ForYear";
        private const int SupplierFunctionId = 71;

        private const int PermissionViewDetail = 2;
        private const int PermissionAdd = 3;
        private const int PermissionEdit = 4;
        private const int PermissionSubmit = 5;

        private EmployeeDataScopeViewModel _dataScope = new EmployeeDataScopeViewModel();
        private bool _isAdminRole;

        // Khoi tao PageModel detail, dung chung permission service + connection string.
        public SupplierDetailModel(IConfiguration config, PermissionService permissionService) : base(config)
        {
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
        public string? Mode { get; set; }

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

        public PagePermissions PagePerm { get; set; } = new PagePermissions();

        public bool IsEdit => Id.HasValue && Id.Value > 0;
        public bool IsViewMode => string.Equals(Mode, "view", StringComparison.OrdinalIgnoreCase);
        public bool IsAnnualView => string.Equals(ViewMode, "byyear", StringComparison.OrdinalIgnoreCase) && Year.HasValue;
        public bool HasSubmitPermission => HasPermission(PermissionSubmit);
        public bool CanSave => !IsViewMode && !IsAnnualView && (IsEdit ? HasPermission(PermissionEdit) : HasPermission(PermissionAdd));
        public bool IsDisapproved => (Input.Status ?? 0) == 9;
        public bool IsSubmitted => IsWorkflowSubmitted(Input);
        public bool CanSubmit => !IsViewMode && !IsAnnualView && IsEdit && HasPermission(PermissionSubmit) && !IsSubmitted;
        // Re Use la case nghiep vu:
        // - record dang disapproved
        // - user co quyen edit
        // - user la owner cua luong submit (hoac admin)
        public bool CanReuse => !IsViewMode && !IsAnnualView && IsEdit && HasPermission(PermissionEdit) && IsDisapproved && (_isAdminRole || IsReuseOwner());

        // Tai trang detail:
        // - Nap permission + data scope + dropdown
        // - Neu la edit thi load du lieu tu DB.
        // Luu y: vao trang do middleware xu ly, nhung thao tac du lieu ben duoi van check them.
        public IActionResult OnGet(
            int? id,
            string? viewMode,
            int? year,
            string? mode,
            int? deptId,
            string? supplierCode,
            string? supplierName,
            string? business,
            string? contact,
            int? statusId,
            bool isNew = false,
            int pageIndex = 1,
            int? pageSize = null)
        {
            Id = id;
            ViewMode = viewMode;
            Year = year;
            Mode = mode;
            DeptId = deptId;
            SupplierCode = supplierCode;
            SupplierName = supplierName;
            Business = business;
            Contact = contact;
            StatusId = statusId;
            IsNew = isNew;
            PageIndex = pageIndex <= 0 ? 1 : pageIndex;
            PageSize = pageSize;

            // Má»—i request pháº£i náº¡p láº¡i quyá»n + scope dá»¯ liá»‡u cá»§a user hiá»‡n táº¡i
            // Ä‘á»ƒ chá»‘ng gá»i trá»±c tiáº¿p URL/handler bá» qua UI.
            LoadPagePermissions();
            LoadUserDataScope();

            LoadAllDropdowns();
            ApplyFlashMessage();

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

            var detail = IsAnnualView
                ? GetAnnualDetail(Id!.Value, Year!.Value)
                : GetDetail(Id!.Value);

            if (detail == null)
            {
                SetMessage("Supplier not found.", "error");
                return Page();
            }

            if (!CanAccessDepartment(detail.DeptID))
            {
                return Forbid();
            }

            Input = detail;
            Histories = IsAnnualView
                ? GetAnnualApprovalHistory(Id.Value, Year.Value)
                : GetApprovalHistory(Id.Value);

            return Page();
        }

        // AJAX check trung SupplierCode khi user nhap.
        // Check them quyen add/edit + scope record.
        public JsonResult OnGetCheckSupplierCode(string? supplierCode, int? id)
        {
            // Handler AJAX cÅ©ng pháº£i náº¡p láº¡i quyá»n/scope nhÆ° page chÃ­nh.
            LoadPagePermissions();
            LoadUserDataScope();

            var canAccess = (id.HasValue && id.Value > 0) ? HasPermission(PermissionEdit) : HasPermission(PermissionAdd);
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

        // AJAX goi y ma Supplier moi theo quy tac SP + so tang dan.
        public JsonResult OnGetSuggestSupplierCode()
        {
            // Handler AJAX cÅ©ng pháº£i náº¡p láº¡i quyá»n/scope nhÆ° page chÃ­nh.
            LoadPagePermissions();
            LoadUserDataScope();

            if (!HasPermission(PermissionAdd))
            {
                return new JsonResult(new { ok = false, message = "Forbidden" })
                {
                    StatusCode = StatusCodes.Status403Forbidden
                };
            }

            var suggestion = GetSuggestedSupplierCode();
            return new JsonResult(new { ok = true, supplierCode = suggestion });
        }

        // AJAX load lich su phe duyet (giu pattern giong STContract: table body render bang JS).
        public IActionResult OnGetApprovalHistory(int supplierId, string? viewMode, int? year)
        {
            LoadPagePermissions();
            LoadUserDataScope();

            if (!HasPermission(PermissionViewDetail))
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

            var isAnnual = string.Equals(viewMode, "byyear", StringComparison.OrdinalIgnoreCase) && year.HasValue;
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

        // Save Add/Edit Supplier.
        // Ngoai UI, backend van check quyen + scope de chan goi truc tiep handler.
        public IActionResult OnPost()
        {
            // POST lÃ  request Ä‘á»™c láº­p => luÃ´n náº¡p láº¡i quyá»n vÃ  pháº¡m vi dá»¯ liá»‡u.
            LoadPagePermissions();
            LoadUserDataScope();

            if (IsViewMode || IsAnnualView)
            {
                return Forbid();
            }

            if ((IsEdit && !HasPermission(PermissionEdit)) || (!IsEdit && !HasPermission(PermissionAdd)))
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

        // Submit supplier vao workflow phe duyet:
        // - Reset ve step dau (Status = 0) de level 1 xu ly.
        // Chi dung cho current list, khong ap dung cho annual view.
        public IActionResult OnPostSubmitApproval()
        {
            // Submit approval cÃ³ kiá»ƒm tra quyá»n + scope riÃªng nÃªn pháº£i náº¡p láº¡i.
            LoadPagePermissions();
            LoadUserDataScope();

            if (IsViewMode || IsAnnualView)
            {
                return Forbid();
            }

            if (!HasPermission(PermissionSubmit))
            {
                return Forbid();
            }

            if (!IsEdit)
            {
                return RedirectToPage("./Index", BuildListRouteValues());
            }

            LoadAllDropdowns();

            Input = GetDetail(Id!.Value) ?? new SupplierViewModel();
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

        // Re Use supplier da bi disapprove:
        // - Reset ve Status = 0 va xoa dau submit/approve de owner luong submit xu ly lai.
        public IActionResult OnPostReuse()
        {
            LoadPagePermissions();
            LoadUserDataScope();

            if (IsViewMode || IsAnnualView)
            {
                return Forbid();
            }

            // Re Use dung quyen Edit, khong dung quyen Submit.
            if (!HasPermission(PermissionEdit))
            {
                return Forbid();
            }

            if (!IsEdit)
            {
                return RedirectToPage("./Index", BuildListRouteValues());
            }

            LoadAllDropdowns();

            Input = GetDetail(Id!.Value) ?? new SupplierViewModel();
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

        // Dong goi route value de quay lai list dung filter/page user dang thao tac.
        private object BuildListRouteValues() => new
        {
            viewMode = ViewMode,
            year = Year,
            deptId = DeptId,
            supplierCode = SupplierCode,
            supplierName = SupplierName,
            business = Business,
            contact = Contact,
            statusId = StatusId,
            isNew = IsNew,
            pageIndex = PageIndex,
            pageSize = PageSize
        };

        // Dong goi route value detail + list state de sau Save/Submit van Back dung bo loc cu.
        private object BuildDetailRouteValues(int supplierId) => new
        {
            id = supplierId,
            mode = Mode,
            viewMode = ViewMode,
            year = Year,
            deptId = DeptId,
            supplierCode = SupplierCode,
            supplierName = SupplierName,
            business = Business,
            contact = Contact,
            statusId = StatusId,
            isNew = IsNew,
            pageIndex = PageIndex,
            pageSize = PageSize
        };

        // Nap dropdown Department + Status cho form detail.
        // Department bi gioi han theo scope neu user khong co xem all dept.
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

        // Nap danh sach permission cua user cho function Supplier.
        // Lay AllowedNos theo functionId.
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

        // Helper check so quyen trong AllowedNos.
        private bool HasPermission(int permissionNo)
        {
            return PagePerm.HasPermission(permissionNo);
        }

        // Nap data scope cua user login hien tai (Dept + SeeDataAllDept).
        private void LoadUserDataScope()
        {
            _isAdminRole = string.Equals(User.FindFirst("IsAdminRole")?.Value, "True", StringComparison.OrdinalIgnoreCase);
            if (_isAdminRole)
            {
                _dataScope = new EmployeeDataScopeViewModel { SeeDataAllDept = true };
                return;
            }

            _dataScope = GetEmployeeDataScope(User.Identity?.Name);
        }

        // Doc scope user tu bang MS_Employee.
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

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@EmployeeCode", employeeCode.Trim());
            conn.Open();

            using var rd = cmd.ExecuteReader();
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

        // Kiem tra record supplier co nam trong pham vi dept user duoc thao tac khong.
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

        // Lay DeptID cua supplier theo current/annual de phuc vu check scope.
        private int? GetSupplierDepartment(int supplierId, string? viewMode, int? year)
        {
            var isByYear = string.Equals(viewMode, "byyear", StringComparison.OrdinalIgnoreCase);
            var tableName = isByYear ? "dbo.PC_SupplierAnualy" : "dbo.PC_Suppliers";
            var yearSql = isByYear ? $" AND [{AnnualYearColumn}] = @Year" : string.Empty;

            var sql = $@"
                        SELECT TOP 1 DeptID
                        FROM {tableName}
                        WHERE SupplierID = @SupplierID{yearSql}";

            using var conn = new SqlConnection(_connectionString);
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@SupplierID", supplierId);
            if (isByYear)
            {
                cmd.Parameters.AddWithValue("@Year", (object?)year ?? DBNull.Value);
            }

            conn.Open();
            var result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
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

        // Lay lich su phe duyet cho current supplier.
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

        // Lay lich su phe duyet cho annual supplier.
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

        // Ham dung chung de map ket qua lich su phe duyet.
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

        // Kiem tra trung ma supplier (co bo qua supplier hien tai khi edit).
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

        // Sinh ma supplier goi y (SP + so lon nhat + 1).
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
            if (string.IsNullOrWhiteSpace(Message) && !string.IsNullOrWhiteSpace(FlashMessage))
            {
                Message = FlashMessage;
                MessageType = string.IsNullOrWhiteSpace(FlashMessageType) ? "info" : FlashMessageType!;
            }
        }

        // Set flash message de hien sau redirect.
        private void SetFlashMessage(string message, string type)
        {
            FlashMessage = message;
            FlashMessageType = type;
        }

        // Set message hien ngay tren response hien tai.
        private void SetMessage(string message, string type)
        {
            Message = message;
            MessageType = type;
        }

        // VÃ¹ng 1: PC_Suppliers (thÃ´ng tin chÃ­nh cá»§a Supplier)
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

        // VÃ¹ng 2: Approval Information (lá»‹ch sá»­ duyá»‡t)
        public class SupplierApprovalHistoryViewModel
        {
            public string Action { get; set; } = string.Empty;
            public string UserName { get; set; } = string.Empty;
            public DateTime? ActionDate { get; set; }
        }

        // VÃ¹ng phá»¥: scope dá»¯ liá»‡u cá»§a user Ä‘Äƒng nháº­p (Dept/SeeDataAllDept)
        public class EmployeeDataScopeViewModel
        {
            public int? DeptID { get; set; }
            public bool SeeDataAllDept { get; set; }
        }
    }
}

