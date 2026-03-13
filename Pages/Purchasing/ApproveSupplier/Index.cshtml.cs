using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Pages.Purchasing.Supplier;
using SmartSam.Services;

namespace SmartSam.Pages.Purchasing.ApproveSupplier
{
    public class IndexModel : BasePageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly PermissionService _permissionService;
        private const int NoDepartmentScopeValue = -1;
        private const int NoStatusScopeValue = -999;
        private const int SupplierFunctionId = 71;
        private const int PermissionViewList = 1;
        private const int PermissionApprove = 4;

        private ApproveEmployeeDataScopeDto _dataScope = new();
        private bool _isAdminRole;

        public IndexModel(IConfiguration config, ILogger<IndexModel> logger, PermissionService permissionService) : base(config)
        {
            _logger = logger;
            _permissionService = permissionService;
        }

        [BindProperty(SupportsGet = true)]
        public int? DeptId { get; set; }

        [BindProperty(SupportsGet = true)]
        public int PageIndex { get; set; } = 1;

        [BindProperty(SupportsGet = true)]
        public int PageSize { get; set; } = 25;

        [BindProperty(SupportsGet = true)]
        public int? CurrentSupplierId { get; set; }

        [BindProperty]
        public ApproveSupplierDetailDto EditSupplier { get; set; } = new();

        [BindProperty]
        public int? GoToOrder { get; set; }

        [TempData]
        public string? FlashMessage { get; set; }

        [TempData]
        public string? FlashMessageType { get; set; }

        public string? Message { get; set; }
        public string MessageType { get; set; } = "info";
        public PagePermissions PagePerm { get; set; } = new();
        public List<SelectListItem> Departments { get; set; } = new();
        public List<ApproveListRowDto> Rows { get; set; } = new();
        public int TotalRecords { get; set; }
        public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)PageSize));
        public bool HasPreviousPage => PageIndex > 1;
        public bool HasNextPage => PageIndex < TotalPages;
        public bool CanApprove => HasPermission(PermissionApprove);
        public bool IsDepartmentFilterLocked => !_isAdminRole;
        public ApproveSupplierDetailDto? CurrentSupplierDetail { get; set; }
        public int CurrentSupplierPosition { get; set; }
        public int CurrentPageSupplierCount => Rows.Count;
        public int? FirstSupplierId { get; set; }
        public int? LastSupplierId { get; set; }
        public int? PrevSupplierId { get; set; }
        public int? NextSupplierId { get; set; }
        public bool HasFirstSupplier => FirstSupplierId.HasValue && CurrentSupplierPosition > 1;
        public bool HasLastSupplier => LastSupplierId.HasValue && CurrentSupplierPosition < CurrentPageSupplierCount;
        public bool HasPrevSupplier => PrevSupplierId.HasValue;
        public bool HasNextSupplier => NextSupplierId.HasValue;
        public ApprovePurchaseOrderInfoDto PurchaseOrderInfo { get; set; } = new();
        public List<ApproveSupplierServiceRowDto> SupplierServiceRows { get; set; } = new();
        public int? LevelCheckSupplier => _dataScope.LevelCheckSupplier;
        public bool CanEditAllSupplierFields => _isAdminRole || _dataScope.LevelCheckSupplier == 1;
        public bool CanEditCommentOnly => !_isAdminRole && _dataScope.LevelCheckSupplier.HasValue && _dataScope.LevelCheckSupplier.Value > 1;
        public bool CanEditComment => CanEditAllSupplierFields || CanEditCommentOnly;
        public bool CanApproveByLevel => _dataScope.LevelCheckSupplier.HasValue && _dataScope.LevelCheckSupplier.Value is >= 1 and <= 4;
        public bool CanDisapproveByLevel => _dataScope.LevelCheckSupplier.HasValue && _dataScope.LevelCheckSupplier.Value is >= 2 and <= 4;

        public void OnGet()
        {
            LoadPagePermissions();
            LoadUserDataScope();
            if (!HasPermission(PermissionViewList))
            {
                Response.StatusCode = 403;
                return;
            }

            if (!string.IsNullOrWhiteSpace(FlashMessage))
            {
                Message = FlashMessage;
                MessageType = string.IsNullOrWhiteSpace(FlashMessageType) ? "info" : FlashMessageType!;
            }

            LoadDepartments();
            LoadRows();
            LoadCurrentSupplierDetail();
        }

        public IActionResult OnPostSave()
        {
            LoadPagePermissions();
            LoadUserDataScope();
            if (!HasPermission(PermissionViewList))
            {
                return Forbid();
            }

            var supplierId = EditSupplier.SupplierID;
            if (supplierId <= 0)
            {
                SetFlashMessage("Invalid supplier.", "warning");
                return RedirectToCurrentList();
            }

            var current = GetSupplierDetail(supplierId);
            if (current is null)
            {
                SetFlashMessage("Supplier not found.", "warning");
                return RedirectToCurrentList();
            }

            if (!CanAccessDepartment(current.DeptID))
            {
                return Forbid();
            }

            if (!CanEditAllSupplierFields && !CanEditCommentOnly)
            {
                return Forbid();
            }

            var saveValidationMessage = ValidateSupplierInputLikeDetail(
                supplierId,
                current,
                EditSupplier,
                CanEditAllSupplierFields,
                CanEditComment);
            if (!string.IsNullOrWhiteSpace(saveValidationMessage))
            {
                CurrentSupplierId = supplierId;
                SetFlashMessage(saveValidationMessage, "warning");
                return RedirectToCurrentList();
            }

            UpdateSupplierForApproval(supplierId, EditSupplier, CanEditAllSupplierFields);
            CurrentSupplierId = supplierId;
            SetFlashMessage("Updated supplier information successfully.", "success");
            return RedirectToCurrentList();
        }

        public IActionResult OnPostGoTo()
        {
            LoadPagePermissions();
            LoadUserDataScope();
            if (!HasPermission(PermissionViewList))
            {
                return Forbid();
            }

            LoadRows();
            if (Rows.Count == 0)
            {
                return RedirectToCurrentList();
            }

            var order = GoToOrder.GetValueOrDefault();
            if (order < 1 || order > Rows.Count)
            {
                SetFlashMessage($"Go to must be from 1 to {Rows.Count}.", "warning");
                return RedirectToCurrentList();
            }

            CurrentSupplierId = Rows[order - 1].SupplierID;
            return RedirectToCurrentList();
        }

        public IActionResult OnPostApprove()
        {
            LoadPagePermissions();
            LoadUserDataScope();

            if (!_dataScope.LevelCheckSupplier.HasValue || _dataScope.LevelCheckSupplier.Value is < 1 or > 4)
            {
                SetFlashMessage("You have no right to approve.", "warning");
                return RedirectToCurrentList();
            }

            var supplierId = EditSupplier.SupplierID;
            if (supplierId <= 0)
            {
                SetFlashMessage("Invalid supplier.", "warning");
                return RedirectToCurrentList();
            }

            var current = GetSupplierDetail(supplierId);
            if (current is null)
            {
                SetFlashMessage("Supplier not found.", "warning");
                return RedirectToCurrentList();
            }

            if (!CanAccessDepartment(current.DeptID))
            {
                return Forbid();
            }

            var approveValidationMessage = ValidateSupplierInputLikeDetail(
                supplierId,
                current,
                EditSupplier,
                CanEditAllSupplierFields,
                CanEditComment);
            if (!string.IsNullOrWhiteSpace(approveValidationMessage))
            {
                CurrentSupplierId = supplierId;
                SetFlashMessage(approveValidationMessage, "warning");
                return RedirectToCurrentList();
            }

            LoadRows();
            var nextSupplierId = GetNextSupplierIdFromCurrentRows(supplierId);
            var isLastSupplier = IsLastSupplierInCurrentRows(supplierId);
            var currentLevel = _dataScope.LevelCheckSupplier.Value;
            var operatorCode = User.Identity?.Name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(operatorCode))
            {
                SetFlashMessage("Cannot identify operator.", "error");
                return RedirectToCurrentList();
            }

            var approved = ApproveByLevel(
                supplierId,
                currentLevel,
                operatorCode,
                EditSupplier,
                CanEditAllSupplierFields,
                CanEditComment);
            if (!approved)
            {
                SetFlashMessage("Cannot approve because supplier status is not in the expected workflow step.", "warning");
                return RedirectToCurrentList();
            }

            var notifyResult = isLastSupplier ? TryNotifyNextLevel(currentLevel, current, "approved") : null;
            CurrentSupplierId = nextSupplierId;
            var approveMessage = "Approved supplier successfully.";
            if (!string.IsNullOrWhiteSpace(notifyResult))
            {
                approveMessage += $" {notifyResult}";
            }

            SetFlashMessage(approveMessage, "success");
            return RedirectToCurrentList();
        }

        public IActionResult OnPostDisapprove()
        {
            LoadPagePermissions();
            LoadUserDataScope();

            if (!_dataScope.LevelCheckSupplier.HasValue || _dataScope.LevelCheckSupplier.Value is < 2 or > 4)
            {
                SetFlashMessage("You have no right to disapprove.", "warning");
                return RedirectToCurrentList();
            }

            var supplierId = EditSupplier.SupplierID;
            if (supplierId <= 0)
            {
                SetFlashMessage("Invalid supplier.", "warning");
                return RedirectToCurrentList();
            }

            var current = GetSupplierDetail(supplierId);
            if (current is null)
            {
                SetFlashMessage("Supplier not found.", "warning");
                return RedirectToCurrentList();
            }

            if (!CanAccessDepartment(current.DeptID))
            {
                return Forbid();
            }

            LoadRows();
            var nextSupplierId = GetNextSupplierIdFromCurrentRows(supplierId);
            var isLastSupplier = IsLastSupplierInCurrentRows(supplierId);
            var currentLevel = _dataScope.LevelCheckSupplier.Value;
            var operatorCode = User.Identity?.Name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(operatorCode))
            {
                SetFlashMessage("Cannot identify operator.", "error");
                return RedirectToCurrentList();
            }

            var disapproved = DisapproveByLevel(
                supplierId,
                currentLevel,
                operatorCode,
                EditSupplier.Comment,
                CanEditComment);
            if (!disapproved)
            {
                SetFlashMessage("Disapprove failed.", "warning");
                return RedirectToCurrentList();
            }

            var notifyResult = isLastSupplier ? TryNotifyNextLevel(currentLevel, current, "disapproved") : null;
            CurrentSupplierId = nextSupplierId;
            var disapproveMessage = "Disapproved supplier successfully.";
            if (!string.IsNullOrWhiteSpace(notifyResult))
            {
                disapproveMessage += $" {notifyResult}";
            }

            SetFlashMessage(disapproveMessage, "success");
            return RedirectToCurrentList();
        }

        public IActionResult OnPostSaveMoreComment()
        {
            LoadPagePermissions();
            LoadUserDataScope();
            if (!HasPermission(PermissionViewList))
            {
                return Forbid();
            }

            var supplierId = EditSupplier.SupplierID;
            if (supplierId <= 0)
            {
                SetFlashMessage("Invalid supplier.", "warning");
                return RedirectToCurrentList();
            }

            var current = GetSupplierDetail(supplierId);
            if (current is null)
            {
                SetFlashMessage("Supplier not found.", "warning");
                return RedirectToCurrentList();
            }

            if (!CanAccessDepartment(current.DeptID))
            {
                return Forbid();
            }

            if (!CanEditComment)
            {
                return Forbid();
            }

            UpdateSupplierComment(supplierId, EditSupplier.Comment);
            CurrentSupplierId = supplierId;
            SetFlashMessage("Saved supplier comment successfully.", "success");
            return RedirectToCurrentList();
        }

        private void LoadDepartments()
        {
            var list = new List<SelectListItem>();

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand(@"
                SELECT DeptID, DeptCode
                FROM MS_Department
                ORDER BY DeptCode", conn);

            conn.Open();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add(new SelectListItem
                {
                    Value = Convert.ToString(rd[0]),
                    Text = Convert.ToString(rd[1])
                });
            }

            if (_isAdminRole)
            {
                Departments = new List<SelectListItem>
                {
                    new() { Value = string.Empty, Text = "--- All ---" }
                };
                Departments.AddRange(list);
                return;
            }

            var scopedDepartments = list
                .Where(x => _dataScope.DeptID.HasValue && x.Value == _dataScope.DeptID.Value.ToString())
                .ToList();

            Departments = scopedDepartments;
            DeptId = _dataScope.DeptID ?? NoDepartmentScopeValue;
        }

        private void LoadRows()
        {
            if (PageSize <= 0) PageSize = 25;
            if (PageSize > 200) PageSize = 200;
            if (PageIndex <= 0) PageIndex = 1;

            var criteria = BuildCriteria(includePaging: true);
            var (rows, totalRecords) = SearchPaged(criteria);
            TotalRecords = totalRecords;

            if (TotalRecords > 0 && PageIndex > TotalPages)
            {
                PageIndex = TotalPages;
                criteria = BuildCriteria(includePaging: true);
                (rows, totalRecords) = SearchPaged(criteria);
                TotalRecords = totalRecords;
            }

            Rows = rows;
        }

        private void LoadCurrentSupplierDetail()
        {
            CurrentSupplierDetail = null;
            PurchaseOrderInfo = new ApprovePurchaseOrderInfoDto();
            SupplierServiceRows = new List<ApproveSupplierServiceRowDto>();
            CurrentSupplierPosition = 0;
            FirstSupplierId = null;
            LastSupplierId = null;
            PrevSupplierId = null;
            NextSupplierId = null;

            if (Rows.Count == 0)
            {
                CurrentSupplierId = null;
                GoToOrder = null;
                return;
            }

            var selectedId = CurrentSupplierId.GetValueOrDefault();
            var selectedIndex = Rows.FindIndex(x => x.SupplierID == selectedId);
            if (selectedIndex < 0)
            {
                selectedIndex = 0;
                CurrentSupplierId = Rows[0].SupplierID;
            }

            CurrentSupplierPosition = selectedIndex + 1;
            GoToOrder = CurrentSupplierPosition;
            FirstSupplierId = Rows[0].SupplierID;
            LastSupplierId = Rows[^1].SupplierID;
            PrevSupplierId = selectedIndex > 0 ? Rows[selectedIndex - 1].SupplierID : null;
            NextSupplierId = selectedIndex < Rows.Count - 1 ? Rows[selectedIndex + 1].SupplierID : null;

            CurrentSupplierDetail = GetSupplierDetail(Rows[selectedIndex].SupplierID);
            EditSupplier = CurrentSupplierDetail is null ? new ApproveSupplierDetailDto() : CloneForEdit(CurrentSupplierDetail);

            if (CurrentSupplierDetail is not null)
            {
                PurchaseOrderInfo = GetPurchaseOrderInfo(CurrentSupplierDetail.SupplierID, DateTime.Now.Year);
                SupplierServiceRows = GetSupplierServiceRows(CurrentSupplierDetail.SupplierID);
            }
        }

        private (List<ApproveListRowDto> rows, int totalRecords) SearchPaged(ApproveFilterCriteria criteria)
        {
            var rows = new List<ApproveListRowDto>();
            var totalRecords = 0;
            var pageIndex = criteria.PageIndex.GetValueOrDefault() <= 0 ? 1 : criteria.PageIndex!.Value;
            var pageSize = criteria.PageSize.GetValueOrDefault() <= 0 ? 25 : criteria.PageSize!.Value;
            var offset = (pageIndex - 1) * pageSize;

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            conn.Open();

            using (var countCmd = new SqlCommand(@"
                SELECT COUNT(1)
                FROM dbo.PC_Suppliers s
                LEFT JOIN dbo.PC_SupplierStatus st ON s.[Status] = st.SupplierStatusID
                LEFT JOIN dbo.MS_Department d ON s.DeptID = d.DeptID
                WHERE (@DeptID IS NULL OR s.DeptID = @DeptID)
                  AND (@StatusID IS NULL OR s.[Status] = @StatusID)
                  AND (@MaxStatusExclusive IS NULL OR ISNULL(s.[Status], 0) < @MaxStatusExclusive)", conn))
            {
                BindSearchParams(countCmd, criteria);
                totalRecords = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);
            }

            using var cmd = new SqlCommand(@"
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
                    s.DeptID,
                    d.DeptCode,
                    st.SupplierStatusName,
                    s.[Status]
                FROM dbo.PC_Suppliers s
                LEFT JOIN dbo.PC_SupplierStatus st ON s.[Status] = st.SupplierStatusID
                LEFT JOIN dbo.MS_Department d ON s.DeptID = d.DeptID
                WHERE (@DeptID IS NULL OR s.DeptID = @DeptID)
                  AND (@StatusID IS NULL OR s.[Status] = @StatusID)
                  AND (@MaxStatusExclusive IS NULL OR ISNULL(s.[Status], 0) < @MaxStatusExclusive)
                ORDER BY s.SupplierID DESC
                OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", conn);

            BindSearchParams(cmd, criteria);
            cmd.Parameters.AddWithValue("@Offset", offset);
            cmd.Parameters.AddWithValue("@PageSize", pageSize);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                rows.Add(new ApproveListRowDto
                {
                    SupplierID = Convert.ToInt32(rd[0]),
                    SupplierCode = Convert.ToString(rd[1]) ?? string.Empty,
                    SupplierName = Convert.ToString(rd[2]) ?? string.Empty,
                    Address = Convert.ToString(rd[3]) ?? string.Empty,
                    Phone = Convert.ToString(rd[4]) ?? string.Empty,
                    Mobile = Convert.ToString(rd[5]) ?? string.Empty,
                    Fax = Convert.ToString(rd[6]) ?? string.Empty,
                    Contact = Convert.ToString(rd[7]) ?? string.Empty,
                    Position = Convert.ToString(rd[8]) ?? string.Empty,
                    Business = Convert.ToString(rd[9]) ?? string.Empty,
                    DeptID = rd.IsDBNull(10) ? null : Convert.ToInt32(rd[10]),
                    DeptCode = Convert.ToString(rd[11]) ?? string.Empty,
                    SupplierStatusName = Convert.ToString(rd[12]) ?? string.Empty,
                    Status = rd.IsDBNull(13) ? null : Convert.ToInt32(rd[13])
                });
            }

            return (rows, totalRecords);
        }

        private ApproveSupplierDetailDto? GetSupplierDetail(int supplierId)
        {
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand(@"
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
                    s.[Status],
                    st.SupplierStatusName
                FROM dbo.PC_Suppliers s
                LEFT JOIN dbo.PC_SupplierStatus st ON s.[Status] = st.SupplierStatusID
                LEFT JOIN dbo.MS_Department d ON s.DeptID = d.DeptID
                WHERE s.SupplierID = @SupplierID", conn);

            cmd.Parameters.AddWithValue("@SupplierID", supplierId);
            conn.Open();
            using var rd = cmd.ExecuteReader();
            if (!rd.Read())
            {
                return null;
            }

            return new ApproveSupplierDetailDto
            {
                SupplierID = Convert.ToInt32(rd[0]),
                SupplierCode = Convert.ToString(rd[1]) ?? string.Empty,
                SupplierName = Convert.ToString(rd[2]) ?? string.Empty,
                Address = Convert.ToString(rd[3]) ?? string.Empty,
                Phone = Convert.ToString(rd[4]) ?? string.Empty,
                Mobile = Convert.ToString(rd[5]) ?? string.Empty,
                Fax = Convert.ToString(rd[6]) ?? string.Empty,
                Contact = Convert.ToString(rd[7]) ?? string.Empty,
                Position = Convert.ToString(rd[8]) ?? string.Empty,
                Business = Convert.ToString(rd[9]) ?? string.Empty,
                ApprovedDate = rd.IsDBNull(10) ? null : Convert.ToDateTime(rd[10]),
                Document = !rd.IsDBNull(11) && Convert.ToBoolean(rd[11]),
                Certificate = Convert.ToString(rd[12]) ?? string.Empty,
                Service = Convert.ToString(rd[13]) ?? string.Empty,
                Comment = Convert.ToString(rd[14]) ?? string.Empty,
                IsNew = !rd.IsDBNull(15) && Convert.ToBoolean(rd[15]),
                CodeOfAcc = Convert.ToString(rd[16]) ?? string.Empty,
                DeptID = rd.IsDBNull(17) ? null : Convert.ToInt32(rd[17]),
                DeptCode = Convert.ToString(rd[18]) ?? string.Empty,
                Status = rd.IsDBNull(19) ? null : Convert.ToInt32(rd[19]),
                SupplierStatusName = Convert.ToString(rd[20]) ?? string.Empty
            };
        }

        private ApprovePurchaseOrderInfoDto GetPurchaseOrderInfo(int supplierId, int currentYear)
        {
            var result = new ApprovePurchaseOrderInfoDto
            {
                Year = currentYear - 1
            };

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            conn.Open();

            using (var cmd = new SqlCommand(@"
                SELECT COUNT(1)
                FROM dbo.PC_PO
                WHERE SupplierID = @SupplierID
                  AND YEAR(PODate) = @TheYear", conn))
            {
                cmd.Parameters.AddWithValue("@SupplierID", supplierId);
                cmd.Parameters.AddWithValue("@TheYear", result.Year);
                result.CountPO = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            }

            using (var cmd = new SqlCommand(@"
                SELECT ISNULL(SUM(
                    CASE
                        WHEN p.Currency = 1 THEN d.POAmount
                        ELSE d.POAmount * p.ExRate
                    END
                ), 0)
                FROM dbo.PC_PO p
                INNER JOIN dbo.PC_PODetail d ON p.POID = d.POID
                WHERE YEAR(p.PODate) = @TheYear
                  AND p.SupplierID = @SupplierID", conn))
            {
                cmd.Parameters.AddWithValue("@SupplierID", supplierId);
                cmd.Parameters.AddWithValue("@TheYear", result.Year);
                result.TotalCost = Convert.ToDecimal(cmd.ExecuteScalar() ?? 0m);
            }

            using var cmd2 = new SqlCommand(@"
                SELECT
                    e.PO_IndexDetailID,
                    i.PO_IndexName AS IndexName,
                    id.PO_IndexDetailName AS SubIndex,
                    COUNT(p.POID) AS TheTime,
                    e.Point
                FROM dbo.PC_PO p
                INNER JOIN dbo.PO_Estimate e ON p.POID = e.POID
                INNER JOIN dbo.PO_IndexDetail id ON e.PO_IndexDetailID = id.PO_IndexDetailID
                INNER JOIN dbo.PO_Index i ON id.PO_Index = i.PO_Index
                WHERE YEAR(e.TheDate) = @TheYear
                  AND p.SupplierID = @SupplierID
                GROUP BY e.PO_IndexDetailID, i.PO_IndexName, id.PO_IndexDetailName, e.Point
                ORDER BY e.PO_IndexDetailID", conn);
            cmd2.Parameters.AddWithValue("@SupplierID", supplierId);
            cmd2.Parameters.AddWithValue("@TheYear", result.Year);

            using var rd = cmd2.ExecuteReader();
            while (rd.Read())
            {
                result.Rows.Add(new ApprovePurchaseOrderRowDto
                {
                    PO_IndexDetailID = rd.IsDBNull(0) ? 0 : Convert.ToInt32(rd[0]),
                    IndexName = Convert.ToString(rd[1]) ?? string.Empty,
                    SubIndex = Convert.ToString(rd[2]) ?? string.Empty,
                    TheTime = rd.IsDBNull(3) ? 0 : Convert.ToInt32(rd[3]),
                    Point = rd.IsDBNull(4) ? 0m : Convert.ToDecimal(rd[4])
                });
            }

            return result;
        }

        private List<ApproveSupplierServiceRowDto> GetSupplierServiceRows(int supplierId)
        {
            var rows = new List<ApproveSupplierServiceRowDto>();

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand(@"
                SELECT TheDate, Comment, ThePoint, WarrantyOrService, UserCode
                FROM dbo.PC_SupplierService_TMP
                WHERE SupplierID = @SupplierID
                ORDER BY TheDate DESC", conn);

            cmd.Parameters.AddWithValue("@SupplierID", supplierId);
            conn.Open();

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                rows.Add(new ApproveSupplierServiceRowDto
                {
                    TheDate = rd.IsDBNull(0) ? null : Convert.ToDateTime(rd[0]),
                    Comment = Convert.ToString(rd[1]) ?? string.Empty,
                    ThePoint = rd.IsDBNull(2) ? null : Convert.ToInt32(rd[2]),
                    WarrantyOrService = rd.IsDBNull(3) ? null : Convert.ToInt32(rd[3]),
                    UserCode = Convert.ToString(rd[4]) ?? string.Empty
                });
            }

            return rows;
        }

        private void LoadUserDataScope()
        {
            _isAdminRole = string.Equals(User.FindFirst("IsAdminRole")?.Value, "True", StringComparison.OrdinalIgnoreCase);
            _dataScope = new ApproveEmployeeDataScopeDto();

            var employeeCode = User.Identity?.Name?.Trim();
            if (string.IsNullOrWhiteSpace(employeeCode))
            {
                return;
            }

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand(@"
                SELECT TOP 1 DeptID, ISNULL(CanAsk, 0) AS CanAsk, LevelCheckSupplier
                FROM dbo.MS_Employee
                WHERE EmployeeCode = @EmployeeCode", conn);

            cmd.Parameters.AddWithValue("@EmployeeCode", employeeCode);
            conn.Open();
            using var rd = cmd.ExecuteReader();
            if (rd.Read())
            {
                _dataScope.DeptID = rd.IsDBNull(0) ? null : Convert.ToInt32(rd[0]);
                _dataScope.CanAsk = !rd.IsDBNull(1) && Convert.ToBoolean(rd[1]);
                _dataScope.LevelCheckSupplier = rd.IsDBNull(2) ? null : Convert.ToInt32(rd[2]);
            }

            if (_isAdminRole)
            {
                _dataScope.CanAsk = true;
            }

            if (!_dataScope.CanAsk)
            {
                DeptId = _dataScope.DeptID ?? NoDepartmentScopeValue;
            }
        }

        private void LoadPagePermissions()
        {
            bool isAdmin = User.FindFirst("IsAdminRole")?.Value == "True";
            var roleClaim = User.FindFirst("RoleID")?.Value;
            int roleId = int.Parse(roleClaim ?? "0");

            PagePerm = new PagePermissions();
            if (isAdmin)
            {
                PagePerm.AllowedNos = Enumerable.Range(1, 10).ToList();
            }
            else
            {
                PagePerm.AllowedNos = _permissionService.GetPermissionsForPage(roleId, SupplierFunctionId);
            }
        }

        private bool HasPermission(int permissionNo) => PagePerm.HasPermission(permissionNo);

        private bool CanAccessDepartment(int? supplierDeptId)
        {
            if (_isAdminRole || _dataScope.CanAsk)
            {
                return true;
            }

            if (!_dataScope.DeptID.HasValue || !supplierDeptId.HasValue)
            {
                return false;
            }

            return _dataScope.DeptID.Value == supplierDeptId.Value;
        }

        private ApproveFilterCriteria BuildCriteria(bool includePaging = true)
        {
            return new ApproveFilterCriteria
            {
                DeptId = ResolveDepartmentFilter(),
                StatusId = ResolveStatusFilterByLevel(),
                MaxStatusExclusive = null,
                PageIndex = includePaging ? PageIndex : null,
                PageSize = includePaging ? PageSize : null
            };
        }

        private int? ResolveDepartmentFilter()
        {
            if (_isAdminRole)
            {
                return DeptId;
            }

            if (_dataScope.CanAsk)
            {
                return null;
            }

            return _dataScope.DeptID ?? NoDepartmentScopeValue;
        }

        private int? ResolveStatusFilterByLevel()
        {
            if (!_dataScope.LevelCheckSupplier.HasValue)
            {
                return NoStatusScopeValue;
            }

            var currentLevel = _dataScope.LevelCheckSupplier.Value;
            if (currentLevel is < 1 or > 4)
            {
                return NoStatusScopeValue;
            }

            return currentLevel - 1;
        }

        private void BindSearchParams(SqlCommand cmd, ApproveFilterCriteria criteria)
        {
            cmd.Parameters.Add("@DeptID", SqlDbType.Int).Value = criteria.DeptId.HasValue ? criteria.DeptId.Value : DBNull.Value;
            cmd.Parameters.Add("@StatusID", SqlDbType.Int).Value = criteria.StatusId.HasValue ? criteria.StatusId.Value : DBNull.Value;
            cmd.Parameters.Add("@MaxStatusExclusive", SqlDbType.Int).Value = criteria.MaxStatusExclusive.HasValue ? criteria.MaxStatusExclusive.Value : DBNull.Value;
        }

        private bool SupplierCodeExists(string supplierCode, int? excludeSupplierId = null)
        {
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand(@"
                SELECT COUNT(1)
                FROM dbo.PC_Suppliers
                WHERE LTRIM(RTRIM(ISNULL(SupplierCode, ''))) = LTRIM(RTRIM(@SupplierCode))
                  AND (@ExcludeSupplierId IS NULL OR SupplierID <> @ExcludeSupplierId)", conn);

            cmd.Parameters.AddWithValue("@SupplierCode", supplierCode.Trim());
            cmd.Parameters.Add("@ExcludeSupplierId", SqlDbType.Int).Value = excludeSupplierId.HasValue ? excludeSupplierId.Value : DBNull.Value;
            conn.Open();
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0;
        }

        private void UpdateSupplierComment(int supplierId, string? comment)
        {
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand(@"
                UPDATE dbo.PC_Suppliers
                SET Comment = @Comment
                WHERE SupplierID = @SupplierID", conn);

            cmd.Parameters.AddWithValue("@SupplierID", supplierId);
            cmd.Parameters.Add("@Comment", SqlDbType.NVarChar, 2000).Value = string.IsNullOrWhiteSpace(comment) ? DBNull.Value : comment.Trim();
            conn.Open();
            cmd.ExecuteNonQuery();
        }

        private void UpdateSupplierForApproval(int supplierId, ApproveSupplierDetailDto input, bool canEditAllFields)
        {
            var sql = canEditAllFields
                ? @"
                UPDATE dbo.PC_Suppliers
                SET SupplierCode = @SupplierCode,
                    SupplierName = @SupplierName,
                    Address = @Address,
                    Phone = @Phone,
                    Mobile = @Mobile,
                    Fax = @Fax,
                    Contact = @Contact,
                    [Position] = @Position,
                    Business = @Business,
                    [Document] = @Document,
                    Certificate = @Certificate,
                    Service = @Service,
                    Comment = @Comment,
                    IsNew = @IsNew
                WHERE SupplierID = @SupplierID"
                : @"
                UPDATE dbo.PC_Suppliers
                SET Comment = @Comment
                WHERE SupplierID = @SupplierID";

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@SupplierID", supplierId);
            cmd.Parameters.Add("@Comment", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(input.Comment) ? DBNull.Value : input.Comment.Trim();

            if (canEditAllFields)
            {
                cmd.Parameters.Add("@SupplierCode", SqlDbType.NVarChar, 255).Value = string.IsNullOrWhiteSpace(input.SupplierCode) ? DBNull.Value : input.SupplierCode.Trim();
                cmd.Parameters.Add("@SupplierName", SqlDbType.NVarChar, 255).Value = string.IsNullOrWhiteSpace(input.SupplierName) ? DBNull.Value : input.SupplierName.Trim();
                cmd.Parameters.Add("@Address", SqlDbType.NVarChar, 255).Value = string.IsNullOrWhiteSpace(input.Address) ? DBNull.Value : input.Address.Trim();
                cmd.Parameters.Add("@Phone", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(input.Phone) ? DBNull.Value : input.Phone.Trim();
                cmd.Parameters.Add("@Mobile", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(input.Mobile) ? DBNull.Value : input.Mobile.Trim();
                cmd.Parameters.Add("@Fax", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(input.Fax) ? DBNull.Value : input.Fax.Trim();
                cmd.Parameters.Add("@Contact", SqlDbType.NVarChar, 255).Value = string.IsNullOrWhiteSpace(input.Contact) ? DBNull.Value : input.Contact.Trim();
                cmd.Parameters.Add("@Position", SqlDbType.NVarChar, 255).Value = string.IsNullOrWhiteSpace(input.Position) ? DBNull.Value : input.Position.Trim();
                cmd.Parameters.Add("@Business", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(input.Business) ? DBNull.Value : input.Business.Trim();
                cmd.Parameters.Add("@Document", SqlDbType.Bit).Value = input.Document;
                cmd.Parameters.Add("@Certificate", SqlDbType.NVarChar, 255).Value = string.IsNullOrWhiteSpace(input.Certificate) ? DBNull.Value : input.Certificate.Trim();
                cmd.Parameters.Add("@Service", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(input.Service) ? DBNull.Value : input.Service.Trim();
                cmd.Parameters.Add("@IsNew", SqlDbType.Bit).Value = input.IsNew;
            }

            conn.Open();
            cmd.ExecuteNonQuery();
        }

        private bool ApproveByLevel(int supplierId, int levelCheckSupplier, string operatorCode, ApproveSupplierDetailDto input, bool canEditAllFields, bool canEditComment)
        {
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand(@"
                UPDATE dbo.PC_Suppliers
                SET
                    SupplierCode = CASE WHEN @CanEditAll = 1 THEN @SupplierCode ELSE SupplierCode END,
                    SupplierName = CASE WHEN @CanEditAll = 1 THEN @SupplierName ELSE SupplierName END,
                    Address = CASE WHEN @CanEditAll = 1 THEN @Address ELSE Address END,
                    Phone = CASE WHEN @CanEditAll = 1 THEN @Phone ELSE Phone END,
                    Mobile = CASE WHEN @CanEditAll = 1 THEN @Mobile ELSE Mobile END,
                    Fax = CASE WHEN @CanEditAll = 1 THEN @Fax ELSE Fax END,
                    Contact = CASE WHEN @CanEditAll = 1 THEN @Contact ELSE Contact END,
                    [Position] = CASE WHEN @CanEditAll = 1 THEN @Position ELSE [Position] END,
                    Business = CASE WHEN @CanEditAll = 1 THEN @Business ELSE Business END,
                    [Document] = CASE WHEN @CanEditAll = 1 THEN @Document ELSE [Document] END,
                    Certificate = CASE WHEN @CanEditAll = 1 THEN @Certificate ELSE Certificate END,
                    Service = CASE WHEN @CanEditAll = 1 THEN @Service ELSE Service END,
                    IsNew = CASE WHEN @CanEditAll = 1 THEN @IsNew ELSE IsNew END,
                    Comment = CASE WHEN @CanEditComment = 1 THEN @Comment ELSE Comment END,
                    PurchaserCode = CASE WHEN @LevelCheck = 1 THEN @OperatorCode ELSE PurchaserCode END,
                    PurchaserPreparedDate = CASE WHEN @LevelCheck = 1 THEN GETDATE() ELSE PurchaserPreparedDate END,
                    DepartmentCode = CASE WHEN @LevelCheck = 2 THEN @OperatorCode ELSE DepartmentCode END,
                    DepartmentApproveDate = CASE WHEN @LevelCheck = 2 THEN GETDATE() ELSE DepartmentApproveDate END,
                    FinancialCode = CASE WHEN @LevelCheck = 3 THEN @OperatorCode ELSE FinancialCode END,
                    FinancialApproveDate = CASE WHEN @LevelCheck = 3 THEN GETDATE() ELSE FinancialApproveDate END,
                    BODCode = CASE WHEN @LevelCheck = 4 THEN @OperatorCode ELSE BODCode END,
                    BODApproveDate = CASE WHEN @LevelCheck = 4 THEN GETDATE() ELSE BODApproveDate END,
                    IsApproved = CASE WHEN @LevelCheck = 4 THEN 1 ELSE IsApproved END,
                    ApprovedDate = CASE WHEN @LevelCheck = 4 THEN GETDATE() ELSE ApprovedDate END,
                    [Status] = @LevelCheck
                WHERE SupplierID = @SupplierID
                  AND ISNULL([Status], 0) + 1 = @LevelCheck", conn);

            cmd.Parameters.AddWithValue("@SupplierID", supplierId);
            cmd.Parameters.AddWithValue("@LevelCheck", levelCheckSupplier);
            cmd.Parameters.AddWithValue("@OperatorCode", operatorCode);
            cmd.Parameters.AddWithValue("@CanEditAll", canEditAllFields ? 1 : 0);
            cmd.Parameters.AddWithValue("@CanEditComment", canEditComment ? 1 : 0);
            cmd.Parameters.Add("@Comment", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(input.Comment) ? DBNull.Value : input.Comment.Trim();
            cmd.Parameters.Add("@SupplierCode", SqlDbType.NVarChar, 255).Value = string.IsNullOrWhiteSpace(input.SupplierCode) ? DBNull.Value : input.SupplierCode.Trim();
            cmd.Parameters.Add("@SupplierName", SqlDbType.NVarChar, 255).Value = string.IsNullOrWhiteSpace(input.SupplierName) ? DBNull.Value : input.SupplierName.Trim();
            cmd.Parameters.Add("@Address", SqlDbType.NVarChar, 255).Value = string.IsNullOrWhiteSpace(input.Address) ? DBNull.Value : input.Address.Trim();
            cmd.Parameters.Add("@Phone", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(input.Phone) ? DBNull.Value : input.Phone.Trim();
            cmd.Parameters.Add("@Mobile", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(input.Mobile) ? DBNull.Value : input.Mobile.Trim();
            cmd.Parameters.Add("@Fax", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(input.Fax) ? DBNull.Value : input.Fax.Trim();
            cmd.Parameters.Add("@Contact", SqlDbType.NVarChar, 255).Value = string.IsNullOrWhiteSpace(input.Contact) ? DBNull.Value : input.Contact.Trim();
            cmd.Parameters.Add("@Position", SqlDbType.NVarChar, 255).Value = string.IsNullOrWhiteSpace(input.Position) ? DBNull.Value : input.Position.Trim();
            cmd.Parameters.Add("@Business", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(input.Business) ? DBNull.Value : input.Business.Trim();
            cmd.Parameters.Add("@Document", SqlDbType.Bit).Value = input.Document;
            cmd.Parameters.Add("@Certificate", SqlDbType.NVarChar, 255).Value = string.IsNullOrWhiteSpace(input.Certificate) ? DBNull.Value : input.Certificate.Trim();
            cmd.Parameters.Add("@Service", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(input.Service) ? DBNull.Value : input.Service.Trim();
            cmd.Parameters.Add("@IsNew", SqlDbType.Bit).Value = input.IsNew;

            conn.Open();
            return cmd.ExecuteNonQuery() > 0;
        }

        private bool DisapproveByLevel(int supplierId, int levelCheckSupplier, string operatorCode, string? comment, bool canEditComment)
        {
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand(@"
                UPDATE dbo.PC_Suppliers
                SET
                    Comment = CASE WHEN @CanEditComment = 1 THEN @Comment ELSE Comment END,
                    DepartmentCode = CASE WHEN @LevelCheck = 2 THEN @OperatorCode ELSE DepartmentCode END,
                    DepartmentApproveDate = CASE WHEN @LevelCheck = 2 THEN GETDATE() ELSE DepartmentApproveDate END,
                    FinancialCode = CASE WHEN @LevelCheck = 3 THEN @OperatorCode ELSE FinancialCode END,
                    FinancialApproveDate = CASE WHEN @LevelCheck = 3 THEN GETDATE() ELSE FinancialApproveDate END,
                    BODCode = CASE WHEN @LevelCheck = 4 THEN @OperatorCode ELSE BODCode END,
                    BODApproveDate = CASE WHEN @LevelCheck = 4 THEN GETDATE() ELSE BODApproveDate END,
                    IsApproved = 0,
                    [Status] = 9
                WHERE SupplierID = @SupplierID
                  AND @LevelCheck IN (2, 3, 4)", conn);

            cmd.Parameters.AddWithValue("@SupplierID", supplierId);
            cmd.Parameters.AddWithValue("@LevelCheck", levelCheckSupplier);
            cmd.Parameters.AddWithValue("@OperatorCode", operatorCode);
            cmd.Parameters.AddWithValue("@CanEditComment", canEditComment ? 1 : 0);
            cmd.Parameters.Add("@Comment", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(comment) ? DBNull.Value : comment.Trim();

            conn.Open();
            return cmd.ExecuteNonQuery() > 0;
        }

        private List<string> GetEmailsByLevelCheck(int levelCheckSupplier)
        {
            var rows = new List<string>();

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand(@"
                SELECT DISTINCT LTRIM(RTRIM(TheEmail))
                FROM dbo.MS_Employee
                WHERE LevelCheckSupplier = @LevelCheckSupplier
                  AND ISNULL(LTRIM(RTRIM(TheEmail)), '') <> ''
                  AND ISNULL(IsActive, 0) = 1", conn);

            cmd.Parameters.AddWithValue("@LevelCheckSupplier", levelCheckSupplier);
            conn.Open();

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var email = Convert.ToString(rd[0]) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(email))
                {
                    rows.Add(email.Trim());
                }
            }

            return rows.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private string? TryNotifyNextLevel(int currentLevel, ApproveSupplierDetailDto supplier, string action)
        {
            if (currentLevel >= 4)
            {
                return null;
            }

            var nextLevel = currentLevel + 1;
            var recipients = GetEmailsByLevelCheck(nextLevel);
            if (recipients.Count == 0)
            {
                return $"No email recipients found for level {nextLevel}.";
            }

            var senderEmail = _config.GetValue<string>("EmailSettings:SenderEmail");
            var mailPass = _config.GetValue<string>("EmailSettings:Password");
            var mailServer = _config.GetValue<string>("EmailSettings:MailServer");
            var mailPort = _config.GetValue<int?>("EmailSettings:MailPort") ?? 0;
            if (string.IsNullOrWhiteSpace(senderEmail) ||
                string.IsNullOrWhiteSpace(mailPass) ||
                string.IsNullOrWhiteSpace(mailServer) ||
                mailPort <= 0)
            {
                return $"Email settings are missing. Skip notify to level {nextLevel}.";
            }

            var operatorCode = User.Identity?.Name?.Trim() ?? "SYSTEM";
            var supplierCode = string.IsNullOrWhiteSpace(supplier.SupplierCode) ? supplier.SupplierID.ToString() : supplier.SupplierCode;
            var supplierName = string.IsNullOrWhiteSpace(supplier.SupplierName) ? "-" : supplier.SupplierName;

            var detailUrl = Url.Page("/Purchasing/ApproveSupplierAnnualy/Index", values: new
            {
                DeptId,
                PageIndex,
                PageSize,
                CurrentSupplierId = supplier.SupplierID
            });

            var absoluteUrl = string.IsNullOrWhiteSpace(detailUrl)
                ? string.Empty
                : $"{Request.Scheme}://{Request.Host}{detailUrl}";

            var subject = $"[Supplier Approval] Last supplier processed at level {currentLevel}";
            var body = $@"
<p>Dear Approver Level {nextLevel},</p>
<p>The last supplier in current approval list has been <b>{action}</b> at level {currentLevel}.</p>
<ul>
  <li>Supplier Code: <b>{WebUtility.HtmlEncode(supplierCode)}</b></li>
  <li>Supplier Name: <b>{WebUtility.HtmlEncode(supplierName)}</b></li>
  <li>Action by: <b>{WebUtility.HtmlEncode(operatorCode)}</b></li>
  <li>Action time: <b>{DateTime.Now:yyyy-MM-dd HH:mm:ss}</b></li>
</ul>
{(string.IsNullOrWhiteSpace(absoluteUrl) ? string.Empty : $"<p>Open page: <a href=\"{WebUtility.HtmlEncode(absoluteUrl)}\">Approve Supplier Annualy</a></p>")}
<p>SmartSam System</p>";

            try
            {
                using var mail = new MailMessage
                {
                    From = new MailAddress(senderEmail, "SmartSam System"),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };

                foreach (var recipient in recipients)
                {
                    mail.To.Add(recipient);
                }

                using var smtp = new SmtpClient(mailServer, mailPort)
                {
                    EnableSsl = true,
                    Credentials = new NetworkCredential(senderEmail, mailPass)
                };

                smtp.Send(mail);
                return $"Notification email sent to level {nextLevel}.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cannot send notification email.");
                return $"Cannot send notification email to level {nextLevel}: {ex.Message}";
            }
        }

        private void SetFlashMessage(string message, string type = "info")
        {
            FlashMessage = message;
            FlashMessageType = type;
        }

        private IActionResult RedirectToCurrentList()
        {
            return RedirectToPage("./Index", new
            {
                DeptId,
                PageIndex,
                PageSize,
                CurrentSupplierId
            });
        }

        private int? GetNextSupplierIdFromCurrentRows(int supplierId)
        {
            if (Rows.Count == 0)
            {
                return null;
            }

            var currentIndex = Rows.FindIndex(x => x.SupplierID == supplierId);
            if (currentIndex < 0)
            {
                return CurrentSupplierId;
            }

            if (currentIndex < Rows.Count - 1)
            {
                return Rows[currentIndex + 1].SupplierID;
            }

            if (currentIndex > 0)
            {
                return Rows[currentIndex - 1].SupplierID;
            }

            return null;
        }

        private bool IsLastSupplierInCurrentRows(int supplierId)
        {
            if (Rows.Count == 0)
            {
                return false;
            }

            var currentIndex = Rows.FindIndex(x => x.SupplierID == supplierId);
            return currentIndex >= 0 && currentIndex == Rows.Count - 1;
        }

        private static ApproveSupplierDetailDto CloneForEdit(ApproveSupplierDetailDto source)
        {
            return new ApproveSupplierDetailDto
            {
                SupplierID = source.SupplierID,
                SupplierCode = source.SupplierCode,
                SupplierName = source.SupplierName,
                Address = source.Address,
                Phone = source.Phone,
                Mobile = source.Mobile,
                Fax = source.Fax,
                Contact = source.Contact,
                Position = source.Position,
                Business = source.Business,
                ApprovedDate = source.ApprovedDate,
                Document = source.Document,
                Certificate = source.Certificate,
                Service = source.Service,
                Comment = source.Comment,
                IsNew = source.IsNew,
                CodeOfAcc = source.CodeOfAcc,
                DeptID = source.DeptID,
                DeptCode = source.DeptCode,
                Status = source.Status,
                SupplierStatusName = source.SupplierStatusName
            };
        }

        private string? ValidateSupplierInputLikeDetail(int supplierId, ApproveSupplierDetailDto current, ApproveSupplierDetailDto posted, bool canEditAllFields, bool canEditComment)
        {
            var model = new ApproveSupplierValidationDto
            {
                SupplierCode = canEditAllFields ? posted.SupplierCode : current.SupplierCode,
                SupplierName = canEditAllFields ? posted.SupplierName : current.SupplierName,
                Address = canEditAllFields ? posted.Address : current.Address,
                Phone = canEditAllFields ? posted.Phone : current.Phone,
                Mobile = canEditAllFields ? posted.Mobile : current.Mobile,
                Fax = canEditAllFields ? posted.Fax : current.Fax,
                Contact = canEditAllFields ? posted.Contact : current.Contact,
                Position = canEditAllFields ? posted.Position : current.Position,
                Business = canEditAllFields ? posted.Business : current.Business,
                Document = canEditAllFields ? posted.Document : current.Document,
                Certificate = canEditAllFields ? posted.Certificate : current.Certificate,
                Service = canEditAllFields ? posted.Service : current.Service,
                Comment = canEditComment ? posted.Comment : current.Comment,
                IsNew = canEditAllFields ? posted.IsNew : current.IsNew,
                CodeOfAcc = canEditAllFields ? posted.CodeOfAcc : current.CodeOfAcc,
                DeptID = current.DeptID,
                Status = current.Status
            };

            model.SupplierCode = NormalizeText(model.SupplierCode);
            model.SupplierName = NormalizeText(model.SupplierName);
            model.Address = NormalizeText(model.Address);
            model.Phone = NormalizeText(model.Phone);
            model.Mobile = NormalizeText(model.Mobile);
            model.Fax = NormalizeText(model.Fax);
            model.Contact = NormalizeText(model.Contact);
            model.Position = NormalizeText(model.Position);
            model.Business = NormalizeText(model.Business);
            model.Certificate = NormalizeText(model.Certificate);
            model.Service = NormalizeText(model.Service);
            model.Comment = NormalizeText(model.Comment);
            model.CodeOfAcc = NormalizeText(model.CodeOfAcc);

            var context = new ValidationContext(model);
            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(model, context, results, true))
            {
                var firstError = results.FirstOrDefault()?.ErrorMessage;
                return string.IsNullOrWhiteSpace(firstError) ? "Supplier data is invalid." : firstError;
            }

            if (!string.IsNullOrWhiteSpace(model.SupplierCode) && SupplierCodeExists(model.SupplierCode, supplierId))
            {
                return "Supplier code already exists.";
            }

            posted.SupplierCode = model.SupplierCode ?? string.Empty;
            posted.SupplierName = model.SupplierName ?? string.Empty;
            posted.Address = model.Address ?? string.Empty;
            posted.Phone = model.Phone ?? string.Empty;
            posted.Mobile = model.Mobile ?? string.Empty;
            posted.Fax = model.Fax ?? string.Empty;
            posted.Contact = model.Contact ?? string.Empty;
            posted.Position = model.Position ?? string.Empty;
            posted.Business = model.Business ?? string.Empty;
            posted.Certificate = model.Certificate ?? string.Empty;
            posted.Service = model.Service ?? string.Empty;
            posted.Comment = model.Comment ?? string.Empty;
            posted.CodeOfAcc = model.CodeOfAcc ?? string.Empty;

            return null;
        }

        private static string? NormalizeText(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    public class ApproveLookupOptionDto
    {
        public int Id { get; set; }
        public string CodeOrName { get; set; } = string.Empty;
    }

    public class ApproveFilterCriteria
    {
        public int? DeptId { get; set; }
        public int? StatusId { get; set; }
        public int? MaxStatusExclusive { get; set; }
        public int? PageIndex { get; set; }
        public int? PageSize { get; set; }
    }

    public class ApproveListRowDto
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
        public int? DeptID { get; set; }
        public string DeptCode { get; set; } = string.Empty;
        public string SupplierStatusName { get; set; } = string.Empty;
        public int? Status { get; set; }
    }

    public class ApproveSupplierDetailDto
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
        public bool Document { get; set; }
        public string Certificate { get; set; } = string.Empty;
        public string Service { get; set; } = string.Empty;
        public string Comment { get; set; } = string.Empty;
        public bool IsNew { get; set; }
        public string CodeOfAcc { get; set; } = string.Empty;
        public int? DeptID { get; set; }
        public string DeptCode { get; set; } = string.Empty;
        public int? Status { get; set; }
        public string SupplierStatusName { get; set; } = string.Empty;
    }

    public class ApproveEmployeeDataScopeDto
    {
        public int? DeptID { get; set; }
        public bool CanAsk { get; set; }
        public int? LevelCheckSupplier { get; set; }
    }

    public class ApprovePurchaseOrderInfoDto
    {
        public int Year { get; set; }
        public int CountPO { get; set; }
        public decimal TotalCost { get; set; }
        public List<ApprovePurchaseOrderRowDto> Rows { get; set; } = new();
    }

    public class ApprovePurchaseOrderRowDto
    {
        public int PO_IndexDetailID { get; set; }
        public string IndexName { get; set; } = string.Empty;
        public string SubIndex { get; set; } = string.Empty;
        public int TheTime { get; set; }
        public decimal Point { get; set; }
    }

    public class ApproveSupplierServiceRowDto
    {
        public DateTime? TheDate { get; set; }
        public string Comment { get; set; } = string.Empty;
        public int? ThePoint { get; set; }
        public int? WarrantyOrService { get; set; }
        public string UserCode { get; set; } = string.Empty;
    }

    public class ApproveSupplierValidationDto
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

        public int? Status { get; set; }
    }
}
