using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using SmartSam.Helpers;
using SmartSam.Pages;
using SmartSam.Pages.Purchasing.Supplier;
using SmartSam.Services;
using SmartSam.Services.Interfaces;

namespace SmartSam.Pages.Purchasing.ApproveSupplier
{
    public class ApproveSupplierDetailModel : BasePageModel
    {
        private readonly ILogger<ApproveSupplierDetailModel> _logger;
        private readonly ISecurityService _securityService;
        private const int NoDepartmentScopeValue = -1;
        private const int NoStatusScopeValue = -999;
        private const int PermissionViewList = 1;
        private const int PermissionApprove = 2;

        private ApproveEmployeeDataScopeViewModel _dataScope = new ApproveEmployeeDataScopeViewModel();
        private bool _isAdminRole;

        public ApproveSupplierDetailModel(ISecurityService securityService, IConfiguration config, ILogger<ApproveSupplierDetailModel> logger) : base(config)
        {
            _securityService = securityService;
            _logger = logger;
        }
        // ID chức năng trong bảng SYS_Function
        private const int FUNCTION_ID = 145;

        [BindProperty(SupportsGet = true)]
        public int? CurrentSupplierId { get; set; }

        [BindProperty(SupportsGet = true)]
        public string? Type { get; set; }

        [BindProperty]
        public int? SupplierId { get; set; }

        [BindProperty]
        public ApproveSupplierDetailViewModel EditSupplier { get; set; } = new ApproveSupplierDetailViewModel();

        [BindProperty]
        public int? GoToOrder { get; set; }

        [TempData]
        public string? FlashMessage { get; set; }

        [TempData]
        public string? FlashMessageType { get; set; }

        public string? Message { get; set; }
        public string MessageType { get; set; } = "info";
        public PagePermissions PagePerm { get; private set; } = new PagePermissions();
        public List<ApproveListRowViewModel> Rows { get; set; } = new List<ApproveListRowViewModel>();
        public ApproveSupplierDetailViewModel? CurrentSupplierDetail { get; set; }
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
        public ApprovePurchaseOrderInfoViewModel PurchaseOrderInfo { get; set; } = new ApprovePurchaseOrderInfoViewModel();
        public List<ApproveSupplierServiceRowViewModel> SupplierServiceRows { get; set; } = new List<ApproveSupplierServiceRowViewModel>();
        public int? LevelCheckSupplier => _dataScope.LevelCheckSupplier;
        public bool IsSupplierLinkMode => string.Equals(Type, "new", StringComparison.OrdinalIgnoreCase) && SupplierId.HasValue && SupplierId.Value > 0;
        public bool IsApproveSupplierNewMode => IsSupplierLinkMode;
        public string PageTitle => IsSupplierLinkMode ? "Approve Supplier New" : "Approve Supplier";
        public bool IsCurrentSupplierReadOnly { get; private set; }
        public bool CanEditAllSupplierFields => !IsCurrentSupplierReadOnly && (_isAdminRole || _dataScope.LevelCheckSupplier == 1);
        public bool CanEditCommentOnly => !IsCurrentSupplierReadOnly && !_isAdminRole && _dataScope.LevelCheckSupplier.HasValue && _dataScope.LevelCheckSupplier.Value > 1;
        public bool CanEditComment => CanEditAllSupplierFields || CanEditCommentOnly;
        public bool CanApproveByLevel => !IsCurrentSupplierReadOnly && _dataScope.LevelCheckSupplier.HasValue && _dataScope.LevelCheckSupplier.Value is >= 1 and <= 4;
        public bool CanDisapproveByLevel => !IsCurrentSupplierReadOnly && _dataScope.LevelCheckSupplier.HasValue && _dataScope.LevelCheckSupplier.Value is >= 2 and <= 4;

        public IActionResult OnGet()
        {
            SupplierId = ResolveSupplierIdFromRequest();

            // 1. Lấy tập quyền thực tế của role login
            PagePerm = GetUserPermissions();
            LoadUserDataScope();
            if (!HasPermission(PermissionViewList))
            {
                return Redirect("/");
            }

            if (IsSupplierLinkMode && (!_dataScope.LevelCheckSupplier.HasValue || _dataScope.LevelCheckSupplier.Value is < 1 or > 4))
            {
                return Redirect("/");
            }

            if (!string.IsNullOrWhiteSpace(FlashMessage))
            {
                Message = FlashMessage;
                MessageType = string.IsNullOrWhiteSpace(FlashMessageType) ? "info" : FlashMessageType!;
            }

            // 2. Load dữ liệu màn hình theo đúng phạm vi quyền
            LoadSupplierRows();
            LoadCurrentSupplierDetailData();
            return Page();
        }

        public IActionResult OnPostSave()
        {
            SupplierId = ResolveSupplierIdFromRequest();

            // 1. Lấy tập quyền thực tế của role login
            PagePerm = GetUserPermissions();
            LoadUserDataScope();
            if (!HasPermission(PermissionViewList))
            {
                return Redirect("/");
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

            if (!CanEditBySupplierLinkState(current))
            {
                SetFlashMessage("Supplier is no longer editable in current approval step.", "warning");
                return RedirectToCurrentList();
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
            SupplierId = ResolveSupplierIdFromRequest();

            // 1. Lấy tập quyền thực tế của role login
            PagePerm = GetUserPermissions();
            LoadUserDataScope();
            if (!HasPermission(PermissionViewList))
            {
                return Redirect("/");
            }

            LoadSupplierRows();
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
            SupplierId = ResolveSupplierIdFromRequest();

            // 1. Lấy tập quyền thực tế của role login
            PagePerm = GetUserPermissions();
            LoadUserDataScope();
            if (!HasPermission(PermissionApprove))
            {
                return Redirect("/");
            }

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

            if (!CanEditBySupplierLinkState(current))
            {
                SetFlashMessage("Supplier is no longer in approvable step.", "warning");
                return RedirectToCurrentList();
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

            LoadSupplierRows();
            var nextSupplierId = GetNextSupplierIdFromCurrentRows(supplierId);
            var shouldNotifyNextLevel = IsApproveSupplierNewMode
                ? true
                : IsLastSupplierInCurrentRows(supplierId);
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

            var notifyResult = shouldNotifyNextLevel ? TryQueueNotifyNextLevel(currentLevel, current, "approved") : null;
            CurrentSupplierId = IsSupplierLinkMode ? supplierId : nextSupplierId;
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
            SupplierId = ResolveSupplierIdFromRequest();

            // 1. Lấy tập quyền thực tế của role login
            PagePerm = GetUserPermissions();
            LoadUserDataScope();
            if (!HasPermission(PermissionApprove))
            {
                return Redirect("/");
            }

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

            if (!CanEditBySupplierLinkState(current))
            {
                SetFlashMessage("Supplier is no longer in disapprovable step.", "warning");
                return RedirectToCurrentList();
            }

            LoadSupplierRows();
            var nextSupplierId = GetNextSupplierIdFromCurrentRows(supplierId);
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

            CurrentSupplierId = IsSupplierLinkMode ? supplierId : nextSupplierId;
            var disapproveMessage = "Disapproved supplier successfully.";

            SetFlashMessage(disapproveMessage, "success");
            return RedirectToCurrentList();
        }

        public IActionResult OnPostSaveMoreComment()
        {
            SupplierId = ResolveSupplierIdFromRequest();

            // 1. Lấy tập quyền thực tế của role login
            PagePerm = GetUserPermissions();
            LoadUserDataScope();
            if (!HasPermission(PermissionViewList))
            {
                return Redirect("/");
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

            if (!CanEditBySupplierLinkState(current))
            {
                SetFlashMessage("Supplier is no longer editable in current approval step.", "warning");
                return RedirectToCurrentList();
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

        private void LoadSupplierRows()
        {
            // 1. Build điều kiện tìm kiếm theo đúng phạm vi quyền
            var criteria = BuildCriteria();
            Rows = SearchSupplierRows(criteria);
        }

        private void LoadCurrentSupplierDetailData()
        {
            // 1. Reset dữ liệu chi tiết trước khi load record mới
            CurrentSupplierDetail = null;
            PurchaseOrderInfo = new ApprovePurchaseOrderInfoViewModel();
            SupplierServiceRows = new List<ApproveSupplierServiceRowViewModel>();
            CurrentSupplierPosition = 0;
            IsCurrentSupplierReadOnly = false;
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

            // 2. Xác định vị trí record đang chọn trên danh sách hiện tại
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

            // 3. Load chi tiết, thông tin PO và lịch sử service
            CurrentSupplierDetail = GetSupplierDetail(Rows[selectedIndex].SupplierID);
            EditSupplier = CurrentSupplierDetail is null ? new ApproveSupplierDetailViewModel() : CloneForEdit(CurrentSupplierDetail);

            if (CurrentSupplierDetail is not null)
            {
                if (!ApplySupplierLinkAccessRule(CurrentSupplierDetail))
                {
                    CurrentSupplierDetail = null;
                    EditSupplier = new ApproveSupplierDetailViewModel();
                    Rows = new List<ApproveListRowViewModel>();
                    CurrentSupplierPosition = 0;
                    FirstSupplierId = null;
                    LastSupplierId = null;
                    PrevSupplierId = null;
                    NextSupplierId = null;
                    GoToOrder = null;
                    return;
                }

                if (!IsApproveSupplierNewMode)
                {
                    PurchaseOrderInfo = GetPurchaseOrderInfo(CurrentSupplierDetail.SupplierID, DateTime.Now.Year);
                }

                SupplierServiceRows = GetSupplierServiceRows(CurrentSupplierDetail.SupplierID);
            }
        }

        private List<ApproveListRowViewModel> SearchSupplierRows(ApproveFilterCriteria criteria)
        {
            var rows = new List<ApproveListRowViewModel>();

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            conn.Open();

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
                  AND (@SupplierID IS NULL OR s.SupplierID = @SupplierID)
                  AND ISNULL(s.IsNew, 0) = @IsNew
                ORDER BY s.SupplierID DESC", conn);

            BindSearchParams(cmd, criteria);

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                rows.Add(new ApproveListRowViewModel
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

            return rows;
        }

        private ApproveSupplierDetailViewModel? GetSupplierDetail(int supplierId)
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
                WHERE s.SupplierID = @SupplierID
                  AND ISNULL(s.IsNew, 0) = @IsNew", conn);

            cmd.Parameters.AddWithValue("@SupplierID", supplierId);
            cmd.Parameters.Add("@IsNew", SqlDbType.Bit).Value = IsApproveSupplierNewMode;
            conn.Open();
            using var rd = cmd.ExecuteReader();
            if (!rd.Read())
            {
                return null;
            }

            return new ApproveSupplierDetailViewModel
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

        private ApprovePurchaseOrderInfoViewModel GetPurchaseOrderInfo(int supplierId, int currentYear)
        {
            var result = new ApprovePurchaseOrderInfoViewModel
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
                result.Rows.Add(new ApprovePurchaseOrderRowViewModel
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

        private List<ApproveSupplierServiceRowViewModel> GetSupplierServiceRows(int supplierId)
        {
            var rows = new List<ApproveSupplierServiceRowViewModel>();

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
                rows.Add(new ApproveSupplierServiceRowViewModel
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
            // 1. Xác định user hiện tại có phải admin hay không
            _isAdminRole = string.Equals(User.FindFirst("IsAdminRole")?.Value, "True", StringComparison.OrdinalIgnoreCase);
            _dataScope = new ApproveEmployeeDataScopeViewModel();

            // 2. Lấy mã nhân viên để đọc phạm vi phòng ban và cấp duyệt
            var employeeCode = User.Identity?.Name?.Trim();
            if (string.IsNullOrWhiteSpace(employeeCode))
            {
                return;
            }

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand(@"
                SELECT TOP 1 DeptID, LevelCheckSupplier
                FROM dbo.MS_Employee
                WHERE EmployeeCode = @EmployeeCode", conn);

            cmd.Parameters.AddWithValue("@EmployeeCode", employeeCode);
            conn.Open();
            using var rd = cmd.ExecuteReader();
            if (rd.Read())
            {
                _dataScope.DeptID = rd.IsDBNull(0) ? null : Convert.ToInt32(rd[0]);
                _dataScope.LevelCheckSupplier = rd.IsDBNull(1) ? null : Convert.ToInt32(rd[1]);
            }

        }

        private PagePermissions GetUserPermissions()
        {
            bool isAdmin = string.Equals(User.FindFirst("IsAdminRole")?.Value, "True", StringComparison.OrdinalIgnoreCase)
                || string.Equals(User.FindFirst("IsAdminRole")?.Value, "1", StringComparison.OrdinalIgnoreCase);
            int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");

            // 1. Khởi tạo đối tượng PagePermissions mới
            var permsObj = new PagePermissions();

            if (isAdmin)
            {
                // 2. Nếu là admin thì cấp đầy đủ quyền để đồng bộ với cách hiển thị menu
                permsObj.AllowedNos = Enumerable.Range(1, 20).ToList();
            }
            else
            {
                // 3. User thường lấy tập quyền thực tế của role theo đúng FunctionID
                permsObj.AllowedNos = _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, 1);
            }

            // 4. Trả về object chứa tập quyền của người dùng
            return permsObj;
        }

        private bool HasPermission(int permissionNo) => PagePerm.HasPermission(permissionNo);

        private bool CanViewAllDepartments()
        {
            return _isAdminRole
                || (_dataScope.LevelCheckSupplier.HasValue
                && (_dataScope.LevelCheckSupplier.Value == 1
                    || _dataScope.LevelCheckSupplier.Value == 3
                    || _dataScope.LevelCheckSupplier.Value == 4));
        }

        private bool CanAccessDepartment(int? supplierDeptId)
        {
            if (CanViewAllDepartments())
            {
                return true;
            }

            if (!_dataScope.DeptID.HasValue || !supplierDeptId.HasValue)
            {
                return false;
            }

            return _dataScope.DeptID.Value == supplierDeptId.Value;
        }

        private ApproveFilterCriteria BuildCriteria()
        {
            return new ApproveFilterCriteria
            {
                DeptId = ResolveDepartmentFilter(),
                StatusId = IsSupplierLinkMode ? null : ResolveStatusFilterByLevel(),
                MaxStatusExclusive = null,
                IsNew = IsApproveSupplierNewMode,
                SupplierId = IsSupplierLinkMode ? SupplierId : null
            };
        }

        private int? ResolveDepartmentFilter()
        {
            if (CanViewAllDepartments())
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
            cmd.Parameters.Add("@SupplierID", SqlDbType.Int).Value = criteria.SupplierId.HasValue ? criteria.SupplierId.Value : DBNull.Value;
            cmd.Parameters.Add("@IsNew", SqlDbType.Bit).Value = criteria.IsNew;
        }

        private int? ResolveSupplierIdFromRequest()
        {
            if (Request.HasFormContentType && Request.Form.ContainsKey("supplier_id"))
            {
                var rawForm = Request.Form["supplier_id"].ToString();
                if (int.TryParse(rawForm, out var parsedForm))
                {
                    return parsedForm;
                }
            }

            var rawQuery = Request.Query["supplier_id"].ToString();
            return int.TryParse(rawQuery, out var parsedQuery) ? parsedQuery : null;
        }

        private bool ApplySupplierLinkAccessRule(ApproveSupplierDetailViewModel supplier)
        {
            if (!IsSupplierLinkMode)
            {
                return true;
            }

            if (!supplier.IsNew)
            {
                SetFlashMessage("Supplier is not a new supplier.", "warning");
                return false;
            }

            if (!_dataScope.LevelCheckSupplier.HasValue || _dataScope.LevelCheckSupplier.Value is < 1 or > 4)
            {
                SetFlashMessage("You have no right to access this approval link.", "warning");
                return false;
            }

            var currentLevel = _dataScope.LevelCheckSupplier.Value;
            var supplierStatus = supplier.Status ?? 0;

            // 4. Link duyệt mới chỉ cho phép 3 trạng thái: đang chờ mình duyệt, đã duyệt xong bởi mình, hoặc ẩn nếu lệch workflow.
            if (supplierStatus == currentLevel - 1)
            {
                IsCurrentSupplierReadOnly = false;
                return true;
            }

            if (supplierStatus == currentLevel)
            {
                IsCurrentSupplierReadOnly = true;
                return true;
            }

            if (supplierStatus > currentLevel)
            {
                SetFlashMessage("This supplier has already passed your approval level.", "warning");
                return false;
            }

            SetFlashMessage("This supplier is not in your approval step yet.", "warning");
            return false;
        }

        private bool CanEditBySupplierLinkState(ApproveSupplierDetailViewModel supplier)
        {
            if (!IsSupplierLinkMode)
            {
                return true;
            }

            if (!_dataScope.LevelCheckSupplier.HasValue)
            {
                return false;
            }

            return (supplier.Status ?? 0) == _dataScope.LevelCheckSupplier.Value - 1;
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

        private void UpdateSupplierForApproval(int supplierId, ApproveSupplierDetailViewModel input, bool canEditAllFields)
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

        private bool ApproveByLevel(int supplierId, int levelCheckSupplier, string operatorCode, ApproveSupplierDetailViewModel input, bool canEditAllFields, bool canEditComment)
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
                    [Status] = 5
                WHERE SupplierID = @SupplierID
                  AND @LevelCheck IN (2, 3, 4)
                  AND ISNULL([Status], 0) + 1 = @LevelCheck", conn);

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

        private string? TryQueueNotifyNextLevel(int currentLevel, ApproveSupplierDetailViewModel supplier, string action)
        {
            if (currentLevel >= 4)
            {
                return null;
            }

            var nextLevel = currentLevel + 1;
            var recipients = GetEmailsByLevelCheck(nextLevel);
            if (recipients.Count == 0)
            {
                return $"No email recipients were found for the next level.";
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

            // 1. Dùng chung một page duyệt supplier, chỉ phân biệt bằng tham số type=new
            var detailUrl = Url.Page("/Purchasing/ApproveSupplier/Index", values: new
            {
                Type = IsApproveSupplierNewMode ? "new" : null,
                supplier_id = IsApproveSupplierNewMode ? (int?)supplier.SupplierID : null
            });

            var absoluteUrl = string.IsNullOrWhiteSpace(detailUrl)
                ? string.Empty
                : $"{Request.Scheme}://{Request.Host}{detailUrl}";

            var subject = IsApproveSupplierNewMode
                ? $"[Approve Supplier New] Last supplier processed at level {currentLevel}"
                : $"[Supplier Approval] Last supplier processed at level {currentLevel}";
            var body = $@"
<p>Dear Approver Level {nextLevel},</p>
<p>The last supplier in current approval list has been <b>{action}</b> at level {currentLevel}.</p>
<ul>
  <li>Supplier Code: <b>{WebUtility.HtmlEncode(supplierCode)}</b></li>
  <li>Supplier Name: <b>{WebUtility.HtmlEncode(supplierName)}</b></li>
  <li>Action by: <b>{WebUtility.HtmlEncode(operatorCode)}</b></li>
  <li>Action time: <b>{DateTime.Now:yyyy-MM-dd HH:mm:ss}</b></li>
</ul>
{(string.IsNullOrWhiteSpace(absoluteUrl) ? string.Empty : $"<p>Open page: <a href=\"{WebUtility.HtmlEncode(absoluteUrl)}\">{WebUtility.HtmlEncode(PageTitle)}</a></p>")}
<p>SmartSam System</p>";
            var htmlBody = IsApproveSupplierNewMode
                ? EmailTemplateHelper.WrapInNotifyTemplate("APPROVE SUPPLIER NEW", "#17a2b8", DateTime.Now, body)
                : EmailTemplateHelper.WrapInNotifyTemplate("APPROVE SUPPLIER", "#007bff", DateTime.Now, body);

            // 2. Đẩy tác vụ gửi mail sang nền để người dùng không phải chờ SMTP phản hồi
            var notifyRequest = new ApproveSupplierNotifyRequestViewModel
            {
                NextLevel = nextLevel,
                SenderEmail = senderEmail,
                Password = mailPass,
                MailServer = mailServer,
                MailPort = mailPort,
                Subject = subject,
                HtmlBody = htmlBody,
                Recipients = recipients
            };

            _ = SendNotifyEmailAsync(notifyRequest);
            return $"The notification email is being sent to the next level.";
        }

        private async Task SendNotifyEmailAsync(ApproveSupplierNotifyRequestViewModel notifyRequest)
        {
            try
            {
                using var mail = new MailMessage
                {
                    From = new MailAddress(notifyRequest.SenderEmail, "SmartSam System"),
                    Subject = notifyRequest.Subject,
                    Body = notifyRequest.HtmlBody,
                    IsBodyHtml = true
                };

                foreach (var recipient in notifyRequest.Recipients)
                {
                    mail.To.Add(recipient);
                }

                using var smtp = new SmtpClient(notifyRequest.MailServer, notifyRequest.MailPort)
                {
                    EnableSsl = true,
                    Credentials = new NetworkCredential(notifyRequest.SenderEmail, notifyRequest.Password)
                };

                await smtp.SendMailAsync(mail);
                _logger.LogInformation("Notification email sent to level {NextLevel}.", notifyRequest.NextLevel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cannot send notification email to level {NextLevel}.", notifyRequest.NextLevel);
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
                CurrentSupplierId,
                Type,
                supplier_id = SupplierId
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

        private static ApproveSupplierDetailViewModel CloneForEdit(ApproveSupplierDetailViewModel source)
        {
            return new ApproveSupplierDetailViewModel
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

        private string? ValidateSupplierInputLikeDetail(int supplierId, ApproveSupplierDetailViewModel current, ApproveSupplierDetailViewModel posted, bool canEditAllFields, bool canEditComment)
        {
            var model = new ApproveSupplierValidationViewModel
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

    public class ApproveLookupOptionViewModel
    {
        public int Id { get; set; }
        public string CodeOrName { get; set; } = string.Empty;
    }

    public class ApproveFilterCriteria
    {
        public int? DeptId { get; set; }
        public int? StatusId { get; set; }
        public int? MaxStatusExclusive { get; set; }
        public int? SupplierId { get; set; }
        public bool IsNew { get; set; }
    }

    public class ApproveListRowViewModel
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

    public class ApproveSupplierDetailViewModel
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

    public class ApproveEmployeeDataScopeViewModel
    {
        public int? DeptID { get; set; }
        public int? LevelCheckSupplier { get; set; }
    }

    public class ApprovePurchaseOrderInfoViewModel
    {
        public int Year { get; set; }
        public int CountPO { get; set; }
        public decimal TotalCost { get; set; }
        public List<ApprovePurchaseOrderRowViewModel> Rows { get; set; } = new List<ApprovePurchaseOrderRowViewModel>();
    }

    public class ApprovePurchaseOrderRowViewModel
    {
        public int PO_IndexDetailID { get; set; }
        public string IndexName { get; set; } = string.Empty;
        public string SubIndex { get; set; } = string.Empty;
        public int TheTime { get; set; }
        public decimal Point { get; set; }
    }

    public class ApproveSupplierServiceRowViewModel
    {
        public DateTime? TheDate { get; set; }
        public string Comment { get; set; } = string.Empty;
        public int? ThePoint { get; set; }
        public int? WarrantyOrService { get; set; }
        public string UserCode { get; set; } = string.Empty;
    }

    public class ApproveSupplierNotifyRequestViewModel
    {
        public int NextLevel { get; set; }
        public string SenderEmail { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string MailServer { get; set; } = string.Empty;
        public int MailPort { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string HtmlBody { get; set; } = string.Empty;
        public List<string> Recipients { get; set; } = new List<string>();
    }

    public class ApproveSupplierValidationViewModel
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



