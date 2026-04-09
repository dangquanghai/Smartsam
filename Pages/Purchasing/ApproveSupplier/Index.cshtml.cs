using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
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
        // private const string NotifyCcEmail = "maiquangvinhi4@gmail.com";
        private const string NotifyCcEmail = "hai.dq@saigonskygarden.com.vn";
        private readonly ILogger<ApproveSupplierDetailModel> _logger;
        private readonly ISecurityService _securityService;
        private const int NoDepartmentScopeValue = -1;
        private const int NoStatusScopeValue = -999;
        private const int PermissionViewList = 1;
        private const int PermissionApprove = 2;
        private const string ProcessedSupplierSessionKeyPrefix = "ApproveSupplierProcessed";

        private ApproveEmployeeDataScopeViewModel _dataScope = new ApproveEmployeeDataScopeViewModel();
        private bool _isAdminRole;

        // Khởi tạo các service và thành phần cần dùng cho màn hình duyệt supplier.
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

        // Xử lý tải dữ liệu ban đầu của màn hình.
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

        // Xử lý yêu cầu lưu thông tin từ giao diện.
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

        // Xử lý điều hướng đến đúng supplier theo số thứ tự người dùng nhập.
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

        // Xử lý thao tác duyệt supplier trên màn hình.
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

            if (!IsSupplierLinkMode)
            {
                MarkSupplierProcessedInCurrentLogin(supplierId);
            }

            var notifyResult = shouldNotifyNextLevel ? TryQueueNotifyNextLevel(currentLevel, current, "approved") : null;
            CurrentSupplierId = IsSupplierLinkMode ? supplierId : GetNextActiveSupplierIdAfterProcessing(supplierId);
            var approveMessage = "Approved supplier successfully.";
            if (!string.IsNullOrWhiteSpace(notifyResult))
            {
                approveMessage += $" {notifyResult}";
            }

            SetFlashMessage(approveMessage, "success");
            return RedirectToCurrentList();
        }

        // Xử lý thao tác từ chối supplier trên màn hình.
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

            if (!IsSupplierLinkMode)
            {
                MarkSupplierProcessedInCurrentLogin(supplierId);
            }

            CurrentSupplierId = IsSupplierLinkMode ? supplierId : GetNextActiveSupplierIdAfterProcessing(supplierId);
            var disapproveMessage = "Disapproved supplier successfully.";

            SetFlashMessage(disapproveMessage, "success");
            return RedirectToCurrentList();
        }

        // Xử lý lưu nhanh phần ghi chú bổ sung của supplier.
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

        // Nạp danh sách supplier theo đúng phạm vi quyền hiện tại.
        private void LoadSupplierRows()
        {
            var criteria = BuildCriteria();
            Rows = SearchSupplierRows(criteria);
            Rows = MergeProcessedSupplierRows(Rows);
        }

        // Nạp chi tiết supplier đang được chọn trên màn hình.
        private void LoadCurrentSupplierDetailData()
        {
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
            var selectedIndex = CurrentSupplierId.HasValue
                ? Rows.FindIndex(x => x.SupplierID == selectedId)
                : GetDefaultCurrentSupplierIndex();
            if (selectedIndex < 0)
            {
                selectedIndex = GetDefaultCurrentSupplierIndex();
                CurrentSupplierId = Rows[0].SupplierID;
            }

            if (selectedIndex < 0)
            {
                selectedIndex = 0;
            }

            CurrentSupplierId = Rows[selectedIndex].SupplierID;

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

                if (!IsSupplierLinkMode && IsProcessedSupplierInCurrentLogin(CurrentSupplierDetail.SupplierID))
                {
                    // 4. Trong cùng phiên đăng nhập, supplier đã approve/disapprove vẫn hiển thị nhưng bị khóa thao tác.
                    IsCurrentSupplierReadOnly = true;
                }
            }
        }

        // Truy vấn danh sách supplier theo điều kiện lọc hiện tại.
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

        // Lấy thông tin chi tiết của supplier để hiển thị và xử lý.
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

        // Lấy thông tin Purchase Order liên quan của supplier.
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

        // Lấy lịch sử dịch vụ của supplier để hiển thị.
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

        // Nạp phạm vi dữ liệu của người dùng đăng nhập.
        private void LoadUserDataScope()
        {
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

        // Lấy tập quyền thực tế của người dùng trên chức năng hiện tại.
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

        // Kiểm tra người dùng có được xem tất cả phòng ban hay không.
        private bool CanViewAllDepartments()
        {
            return _isAdminRole
                || (_dataScope.LevelCheckSupplier.HasValue
                && (_dataScope.LevelCheckSupplier.Value == 1
                    || _dataScope.LevelCheckSupplier.Value == 3
                    || _dataScope.LevelCheckSupplier.Value == 4));
        }

        // Kiểm tra supplier có thuộc phạm vi phòng ban được phép truy cập hay không.
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

        // Tạo điều kiện tìm kiếm supplier theo phạm vi dữ liệu hiện tại.
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

        // Xác định phòng ban cần áp dụng khi truy vấn danh sách.
        private int? ResolveDepartmentFilter()
        {
            if (CanViewAllDepartments())
            {
                return null;
            }

            return _dataScope.DeptID ?? NoDepartmentScopeValue;
        }

        // Xác định trạng thái cần duyệt theo cấp duyệt hiện tại.
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

        // Gán tham số tìm kiếm vào câu lệnh truy vấn supplier.
        private void BindSearchParams(SqlCommand cmd, ApproveFilterCriteria criteria)
        {
            cmd.Parameters.Add("@DeptID", SqlDbType.Int).Value = criteria.DeptId.HasValue ? criteria.DeptId.Value : DBNull.Value;
            cmd.Parameters.Add("@StatusID", SqlDbType.Int).Value = criteria.StatusId.HasValue ? criteria.StatusId.Value : DBNull.Value;
            cmd.Parameters.Add("@MaxStatusExclusive", SqlDbType.Int).Value = criteria.MaxStatusExclusive.HasValue ? criteria.MaxStatusExclusive.Value : DBNull.Value;
            cmd.Parameters.Add("@SupplierID", SqlDbType.Int).Value = criteria.SupplierId.HasValue ? criteria.SupplierId.Value : DBNull.Value;
            cmd.Parameters.Add("@IsNew", SqlDbType.Bit).Value = criteria.IsNew;
        }

        // Đọc mã supplier từ request hiện tại để xử lý đúng bản ghi.
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

        // Áp dụng qu tắc truy cập khi mở supplier theo link trực tiếp.
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

        // Kiểm tra supplier có còn được phép chỉnh sửa ở bước duyệt hiện tại hay không.
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

        // Kiểm tra mã supplier đã tồn tại trong hệ thống hay chưa.
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

        // Cập nhật phần ghi chú của supplier theo yêu cầu duyệt.
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

        // Cập nhật thông tin supplier phục vụ thao tác duyệt.
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

        // Thực hiện nghiệp vụ duyệt supplier theo cấp duyệt hiện tại.
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

        // Thực hiện nghiệp vụ từ chối supplier theo cấp duyệt hiện tại.
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

        // Lấy danh sách người nhận mail theo cấp duyệt kế tiếp.
        private List<ApproveSupplierNotifyRecipientViewModel> GetEmailsByLevelCheck(int levelCheckSupplier, int? deptId)
        {
            var rows = new List<ApproveSupplierNotifyRecipientViewModel>();

            var useDepartmentFilter = IsApproveSupplierNewMode && deptId.HasValue;

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand(@"
                SELECT DISTINCT
                    LTRIM(RTRIM(TheEmail)) AS TheEmail,
                    LTRIM(RTRIM(EmployeeCode)) AS EmployeeCode,
                    LTRIM(RTRIM(EmployeeName)) AS EmployeeName,
                    LTRIM(RTRIM(ISNULL(Title, ''))) AS Title
                FROM dbo.MS_Employee
                WHERE LevelCheckSupplier = @LevelCheckSupplier
                  AND (@UseDepartmentFilter = 0 OR DeptID = @DeptID)
                  AND ISNULL(LTRIM(RTRIM(TheEmail)), '') <> ''
                  AND ISNULL(IsActive, 0) = 1", conn);

            cmd.Parameters.AddWithValue("@LevelCheckSupplier", levelCheckSupplier);
            cmd.Parameters.Add("@UseDepartmentFilter", SqlDbType.Bit).Value = useDepartmentFilter;
            cmd.Parameters.Add("@DeptID", SqlDbType.Int).Value = deptId.HasValue ? deptId.Value : DBNull.Value;
            conn.Open();

            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                var email = Convert.ToString(rd["TheEmail"]) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(email))
                {
                    rows.Add(new ApproveSupplierNotifyRecipientViewModel
                    {
                        Email = email.Trim(),
                        EmployeeCode = Convert.ToString(rd["EmployeeCode"])?.Trim() ?? string.Empty,
                        EmployeeName = Convert.ToString(rd["EmployeeName"])?.Trim() ?? string.Empty,
                        Title = Convert.ToString(rd["Title"])?.Trim() ?? string.Empty
                    });
                }
            }

            return rows;
        }

        // Tạo yêu cầu gửi mail thông báo cho cấp duyệt tiếp theo.
        private string? TryQueueNotifyNextLevel(int currentLevel, ApproveSupplierDetailViewModel supplier, string action)
        {
            if (currentLevel >= 4)
            {
                return null;
            }

            var nextLevel = currentLevel + 1;
            var recipients = GetEmailsByLevelCheck(nextLevel, supplier.DeptID);
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
            var recipientDisplayNames = string.Join(", ", recipients.Select(BuildRecipientDisplayName));

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
            subject = ApplyMailSubjectPrefix(subject);
            var body = IsApproveSupplierNewMode
                ? $@"
<p>Dear {{RECIPIENT_LABEL}},</p>
<p>The last supplier in current approval list has been <b>{action}</b> at level {currentLevel}.</p>
<ul>
  <li>Supplier Code: <b>{WebUtility.HtmlEncode(supplierCode)}</b></li>
  <li>Supplier Name: <b>{WebUtility.HtmlEncode(supplierName)}</b></li>
  <li>Action by: <b>{WebUtility.HtmlEncode(operatorCode)}</b></li>
  <li>Action time: <b>{DateTime.Now:yyyy-MM-dd HH:mm:ss}</b></li>
</ul>
{(string.IsNullOrWhiteSpace(absoluteUrl) ? string.Empty : $"<p>Open page: <a href=\"{WebUtility.HtmlEncode(absoluteUrl)}\">{WebUtility.HtmlEncode(PageTitle)}</a></p>")}
<p>SmartSam System</p>"
                : $@"
<p>Dear {{RECIPIENT_LABEL}},</p>
<p>The current supplier approval list has been <b>{action}</b> at level {currentLevel}.</p>
<p>Please access the approval page to continue reviewing the next suppliers in your workflow.</p>
<ul>
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
                Recipients = recipients.Select(x => x.Email).ToList(),
                RecipientDetails = recipients,
                DefaultRecipientLabel = recipientDisplayNames,
                SendIndividually = true
            };

            _ = SendNotifyEmailAsync(notifyRequest);
            return $"The notification email is being sent to the next level.";
        }

        // Áp tiền tố subject theo cấu hình EmailSettings khi FunctionID hiện tại thuộc danh sách test.
        private string ApplyMailSubjectPrefix(string subject)
        {
            if (string.IsNullOrWhiteSpace(subject))
            {
                return subject;
            }

            var prefix = _config.GetValue<string>("EmailSettings:PrefixSubject")?.Trim();
            if (string.IsNullOrWhiteSpace(prefix) || !ShouldApplyTestSubjectPrefix())
            {
                return subject;
            }

            return $"{prefix} - {subject}";
        }

        // Kiểm tra FunctionID của chức năng duyệt supplier có nằm trong cấu hình TestFunctionIDs hay không.
        private bool ShouldApplyTestSubjectPrefix()
        {
            var configuredIds = _config.GetValue<string>("EmailSettings:TestFunctionIDs");
            if (string.IsNullOrWhiteSpace(configuredIds))
            {
                return false;
            }

            return configuredIds
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Any(value => int.TryParse(value, out var id) && id == FUNCTION_ID);
        }

        // Gửi email thông báo bất đồng bộ cho cấp duyệt tiếp theo.
        private async Task SendNotifyEmailAsync(ApproveSupplierNotifyRequestViewModel notifyRequest)
        {
            try
            {
                if (notifyRequest.SendIndividually && notifyRequest.RecipientDetails.Count > 0)
                {
                    foreach (var recipient in notifyRequest.RecipientDetails)
                    {
                        using var mail = new MailMessage
                        {
                            From = new MailAddress(notifyRequest.SenderEmail, "SmartSam System"),
                            Subject = notifyRequest.Subject,
                            Body = notifyRequest.HtmlBody.Replace("{RECIPIENT_LABEL}", WebUtility.HtmlEncode(BuildRecipientDisplayName(recipient))),
                            IsBodyHtml = true
                        };

                        mail.To.Add(recipient.Email);
                        mail.CC.Add(NotifyCcEmail);

                        using var smtp = new SmtpClient(notifyRequest.MailServer, notifyRequest.MailPort)
                        {
                            EnableSsl = true,
                            Credentials = new NetworkCredential(notifyRequest.SenderEmail, notifyRequest.Password)
                        };

                        await smtp.SendMailAsync(mail);
                    }
                }
                else
                {
                    using var mail = new MailMessage
                    {
                        From = new MailAddress(notifyRequest.SenderEmail, "SmartSam System"),
                        Subject = notifyRequest.Subject,
                        Body = notifyRequest.HtmlBody.Replace("{RECIPIENT_LABEL}", WebUtility.HtmlEncode(notifyRequest.DefaultRecipientLabel)),
                        IsBodyHtml = true
                    };

                    foreach (var recipient in notifyRequest.Recipients)
                    {
                        mail.To.Add(recipient);
                    }

                    mail.CC.Add(NotifyCcEmail);

                    using var smtp = new SmtpClient(notifyRequest.MailServer, notifyRequest.MailPort)
                    {
                        EnableSsl = true,
                        Credentials = new NetworkCredential(notifyRequest.SenderEmail, notifyRequest.Password)
                    };

                    await smtp.SendMailAsync(mail);
                }

                _logger.LogInformation("Notification email sent to level {NextLevel}.", notifyRequest.NextLevel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Cannot send notification email to level {NextLevel}.", notifyRequest.NextLevel);
            }
        }

        // Chuẩn hóa tên hiển thị của người nhận trong nội dung mail.
        private static string BuildRecipientDisplayName(ApproveSupplierNotifyRecipientViewModel recipient)
        {
            var title = (recipient.Title ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(title))
            {
                var titledEmployeeName = string.IsNullOrWhiteSpace(recipient.EmployeeName) ? recipient.Email : recipient.EmployeeName;
                return string.IsNullOrWhiteSpace(recipient.EmployeeCode)
                    ? $"{title}. {titledEmployeeName}"
                    : $"{title}. {titledEmployeeName}({recipient.EmployeeCode})";
            }

            var employeeName = string.IsNullOrWhiteSpace(recipient.EmployeeName) ? recipient.Email : recipient.EmployeeName;
            return string.IsNullOrWhiteSpace(recipient.EmployeeCode)
                ? employeeName
                : $"{employeeName} ({recipient.EmployeeCode})";
        }

        // Gán thông báo tạm thời để hiển thị lại sau khi redirect.
        private void SetFlashMessage(string message, string type = "info")
        {
            FlashMessage = message;
            FlashMessageType = type;
        }

        // Điều hướng người dùng về đúng màn hình danh sách hiện tại.
        private IActionResult RedirectToCurrentList()
        {
            var routeValues = new Dictionary<string, object?>();

            if (IsSupplierLinkMode)
            {
                routeValues["Type"] = Type;
                routeValues["supplier_id"] = SupplierId;
            }
            else
            {
                routeValues["CurrentSupplierId"] = CurrentSupplierId;
                routeValues["Type"] = Type;
            }

            return RedirectToPage("./Index", routeValues);
        }

        // Xác định supplier kế tiếp trong danh sách hiện tại.
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

        // Kiểm tra supplier hiện tại có phải bản ghi cuối danh sách hay không.
        private bool IsLastSupplierInCurrentRows(int supplierId)
        {
            if (Rows.Count == 0)
            {
                return false;
            }

            var currentIndex = Rows.FindIndex(x => x.SupplierID == supplierId);
            return currentIndex >= 0 && currentIndex == Rows.Count - 1;
        }

        // Gộp các supplier đã xử lý trong phiên vào danh sách đang hiển thị.
        private List<ApproveListRowViewModel> MergeProcessedSupplierRows(List<ApproveListRowViewModel> rows)
        {
            if (IsSupplierLinkMode)
            {
                return rows;
            }

            var mergedRows = new List<ApproveListRowViewModel>(rows);
            var processedIds = GetProcessedSupplierIdsInCurrentLogin();
            if (processedIds.Count == 0)
            {
                return mergedRows;
            }

            foreach (var supplierId in processedIds)
            {
                if (mergedRows.Any(x => x.SupplierID == supplierId))
                {
                    continue;
                }

                var processedRow = GetSupplierRowForCurrentSession(supplierId);
                if (processedRow is null)
                {
                    continue;
                }

                mergedRows.Add(processedRow);
            }

            return mergedRows
                .OrderBy(x => IsProcessedSupplierInCurrentLogin(x.SupplierID) ? 0 : 1)
                .ThenByDescending(x => x.SupplierID)
                .ToList();
        }

        // Xác định vị trí mặc định cần focus khi mở màn hình duyệt.
        private int GetDefaultCurrentSupplierIndex()
        {
            var unprocessedIndex = Rows.FindIndex(x => !IsProcessedSupplierInCurrentLogin(x.SupplierID));
            return unprocessedIndex >= 0 ? unprocessedIndex : 0;
        }

        // Lấy lại dòng supplier đã xử lý để hiển thị trong cùng phiên đăng nhập.
        private ApproveListRowViewModel? GetSupplierRowForCurrentSession(int supplierId)
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
                    s.DeptID,
                    d.DeptCode,
                    st.SupplierStatusName,
                    s.[Status]
                FROM dbo.PC_Suppliers s
                LEFT JOIN dbo.PC_SupplierStatus st ON s.[Status] = st.SupplierStatusID
                LEFT JOIN dbo.MS_Department d ON s.DeptID = d.DeptID
                WHERE s.SupplierID = @SupplierID
                  AND ISNULL(s.IsNew, 0) = 0", conn);

            cmd.Parameters.Add("@SupplierID", SqlDbType.Int).Value = supplierId;
            conn.Open();

            using var rd = cmd.ExecuteReader();
            if (!rd.Read())
            {
                return null;
            }

            var row = new ApproveListRowViewModel
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
            };

            if (!CanAccessDepartment(row.DeptID))
            {
                return null;
            }

            return row;
        }

        // Kiểm tra supplier đã được xử lý trong phiên đăng nhập hiện tại hay chưa.
        private bool IsProcessedSupplierInCurrentLogin(int supplierId)
        {
            return GetProcessedSupplierIdsInCurrentLogin().Contains(supplierId);
        }

        // Xác định supplier chưa xử lý tiếp theo sau khi vừa duyệt xong.
        private int? GetNextActiveSupplierIdAfterProcessing(int supplierId)
        {
            if (Rows.Count == 0)
            {
                return supplierId;
            }

            var processedIds = GetProcessedSupplierIdsInCurrentLogin();
            processedIds.Add(supplierId);

            var currentIndex = Rows.FindIndex(x => x.SupplierID == supplierId);
            if (currentIndex < 0)
            {
                return Rows
                    .FirstOrDefault(x => !processedIds.Contains(x.SupplierID))
                    ?.SupplierID ?? supplierId;
            }

            for (var index = currentIndex + 1; index < Rows.Count; index++)
            {
                if (!processedIds.Contains(Rows[index].SupplierID))
                {
                    return Rows[index].SupplierID;
                }
            }

            for (var index = 0; index < currentIndex; index++)
            {
                if (!processedIds.Contains(Rows[index].SupplierID))
                {
                    return Rows[index].SupplierID;
                }
            }

            return supplierId;
        }

        // Đánh dấu supplier đã xử lý trong phiên đăng nhập hiện tại.
        private void MarkSupplierProcessedInCurrentLogin(int supplierId)
        {
            var processedIds = GetProcessedSupplierIdsInCurrentLogin();
            if (!processedIds.Add(supplierId))
            {
                return;
            }

            HttpContext.Session.SetString(GetProcessedSupplierSessionKey(), string.Join(",", processedIds.OrderBy(x => x)));
        }

        // Lấy tập supplier đã xử lý trong phiên đăng nhập hiện tại.
        private HashSet<int> GetProcessedSupplierIdsInCurrentLogin()
        {
            var rawValue = HttpContext.Session.GetString(GetProcessedSupplierSessionKey());
            var supplierIds = new HashSet<int>();
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return supplierIds;
            }

            foreach (var item in rawValue.Split(',', StringSplitOptions.RemoveEmptyEntries))
            {
                if (int.TryParse(item, out var supplierId) && supplierId > 0)
                {
                    supplierIds.Add(supplierId);
                }
            }

            return supplierIds;
        }

        // Tạo khóa session riêng cho danh sách supplier đã xử lý.
        private string GetProcessedSupplierSessionKey()
        {
            var employeeCode = User.Identity?.Name?.Trim() ?? "ANONYMOUS";
            var authCookie = Request.Cookies[".AspNetCore.Cookies"] ?? string.Empty;
            var authCookieHash = ComputeSha256(authCookie);
            return $"{ProcessedSupplierSessionKeyPrefix}:{employeeCode}:{authCookieHash}";
        }

        // Tạo mã băm để tách dữ liệu phiên làm việc theo đăng nhập hiện tại.
        private static string ComputeSha256(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "empty";
            }

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes);
        }

        // Sao chép dữ liệu supplier sang model chỉnh sửa trên giao diện.
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

        // Kiểm tra dữ liệu supplier trước khi lưu theo qu tắc của màn hình chi tiết.
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

        // Chuẩn hóa chuỗi nhập liệu trước khi so sánh và lưu dữ liệu.
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
        public List<ApproveSupplierNotifyRecipientViewModel> RecipientDetails { get; set; } = new List<ApproveSupplierNotifyRecipientViewModel>();
        public string DefaultRecipientLabel { get; set; } = string.Empty;
        public bool SendIndividually { get; set; }
    }

    public class ApproveSupplierNotifyRecipientViewModel
    {
        public string Email { get; set; } = string.Empty;
        public string EmployeeCode { get; set; } = string.Empty;
        public string EmployeeName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
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







