using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
        private const string UndoSupplierSessionKeyPrefix = "ApproveSupplierUndo";
        private const string ScopeActionLockedSessionKeyPrefix = "ApproveSupplierActionLocked";
        private const string NotifyFontFamily = "'VNI-WIN', 'VNI-Times', 'VNI-Helve', sans-serif";

        private ApproveEmployeeDataScopeViewModel _dataScope = new ApproveEmployeeDataScopeViewModel();
        private bool _isAdminRole;

        // Kh?i t?o c�c service v� th�nh ph?n c?n d�ng cho m�n h�nh duy?t supplier.
        public ApproveSupplierDetailModel(ISecurityService securityService, IConfiguration config, ILogger<ApproveSupplierDetailModel> logger) : base(config)
        {
            _securityService = securityService;
            _logger = logger;
        }
        // ID ch?c nang trong b?ng SYS_Function
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
        public string? GoToKeyword { get; set; }

        [TempData]
        public string? FlashMessage { get; set; }

        [TempData]
        public string? FlashMessageType { get; set; }

        [TempData]
        public string? GoToKeywordTemp { get; set; }

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
        public bool CanRestoreCurrentSupplier { get; private set; }
        public string RestoreActionCaption { get; private set; } = "Restore Action";
        public bool ShowRestoreInsteadOfApprovalButtons => IsCurrentSupplierReadOnly && CanRestoreCurrentSupplier;
        public bool HideApprovalActionButtons { get; private set; }
        public bool CanBatchApprove => !IsSupplierLinkMode
            && HasPermission(PermissionApprove)
            && _dataScope.LevelCheckSupplier.HasValue
            && _dataScope.LevelCheckSupplier.Value is >= 1 and <= 4
            && Rows.Any(x => !IsProcessedSupplierInCurrentLogin(x.SupplierID));

        // X? l� t?i d? li?u ban d?u c?a m�n h�nh.
        public IActionResult OnGet()
        {
            SupplierId = ResolveSupplierIdFromRequest();

            // 1. L?y t?p quy?n th?c t? c?a role login
            PagePerm = GetUserPermissions();
            LoadUserDataScope();
            if (!HasPermission(PermissionViewList))
            {
                return RedirectHomeWithAccessError("Missing permission: View Approve Supplier list (FunctionID 145, PermissionNo 1).");
            }

            if (IsSupplierLinkMode && (!_dataScope.LevelCheckSupplier.HasValue || _dataScope.LevelCheckSupplier.Value is < 1 or > 4))
            {
                return RedirectHomeWithAccessError("Missing approval data scope: LevelCheckSupplier must be from 1 to 4 for Approve Supplier New links.");
            }

            if (!string.IsNullOrWhiteSpace(FlashMessage))
            {
                Message = FlashMessage;
                MessageType = string.IsNullOrWhiteSpace(FlashMessageType) ? "info" : FlashMessageType!;
            }

            if (!string.IsNullOrWhiteSpace(GoToKeywordTemp))
            {
                GoToKeyword = GoToKeywordTemp;
            }

            RefreshCurrentApprovalScopeActionLock();

            // 2. Load d? li?u m�n h�nh theo d�ng ph?m vi quy?n
            LoadSupplierRows();
            LoadCurrentSupplierDetailData();
            if (IsSupplierLinkMode && CurrentSupplierDetail is null)
            {
                SetGlobalFlashMessage(
                    string.IsNullOrWhiteSpace(FlashMessage)
                        ? "Cannot access this Approve Supplier New link. The supplier is outside your permission scope or is not in your approval step."
                        : FlashMessage,
                    "error");
                return Redirect("/");
            }

            return Page();
        }

        // X? l� y�u c?u luu th�ng tin t? giao di?n.
        public IActionResult OnPostSave()
        {
            SupplierId = ResolveSupplierIdFromRequest();

            // 1. L?y t?p quy?n th?c t? c?a role login
            PagePerm = GetUserPermissions();
            LoadUserDataScope();
            if (!HasPermission(PermissionViewList))
            {
                return RedirectHomeWithAccessError("Missing permission: View Approve Supplier list (FunctionID 145, PermissionNo 1).");
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

        // X? l� di?u hu?ng d?n d�ng supplier theo s? th? t? ngu?i d�ng nh?p.
        public IActionResult OnPostGoTo()
        {
            SupplierId = ResolveSupplierIdFromRequest();

            // 1. L?y t?p quy?n th?c t? c?a role login
            PagePerm = GetUserPermissions();
            LoadUserDataScope();
            if (!HasPermission(PermissionViewList))
            {
                return RedirectHomeWithAccessError("Missing permission: View Approve Supplier list (FunctionID 145, PermissionNo 1).");
            }

            LoadSupplierRows();
            if (Rows.Count == 0)
            {
                return RedirectToCurrentList();
            }

            var keyword = (GoToKeyword ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(keyword))
            {
                SetFlashMessage("Please input Supplier Code or Supplier Name.", "warning");
                return RedirectToCurrentList();
            }

            var targetSupplierId = FindSupplierIdByKeyword(keyword);
            if (!targetSupplierId.HasValue)
            {
                SetFlashMessage("Cannot find supplier by the entered code or name in current approval scope.", "warning");
                return RedirectToCurrentList();
            }

            CurrentSupplierId = targetSupplierId.Value;
            GoToKeywordTemp = keyword;
            return RedirectToCurrentList();
        }

        // X? l� thao t�c duy?t supplier tr�n m�n h�nh.
        public IActionResult OnPostApprove()
        {
            SupplierId = ResolveSupplierIdFromRequest();

            // 1. L?y t?p quy?n th?c t? c?a role login
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

            var currentLevel = _dataScope.LevelCheckSupplier.Value;
            var operatorCode = User.Identity?.Name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(operatorCode))
            {
                SetFlashMessage("Cannot identify operator.", "error");
                return RedirectToCurrentList();
            }

            var undoSnapshot = GetSupplierUndoSnapshot(supplierId);
            if (undoSnapshot is null)
            {
                SetFlashMessage("Cannot load supplier snapshot for restore.", "error");
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

            var shouldNotifyNextLevel = false;
            var shouldNotifyCompletionToPurchaser = false;
            if (IsSupplierLinkMode)
            {
                shouldNotifyNextLevel = currentLevel < 4;
                shouldNotifyCompletionToPurchaser = currentLevel == 4;
            }
            else
            {
                var remainingSupplierCount = CountPendingSuppliersInCurrentApprovalScope();
                shouldNotifyNextLevel = currentLevel < 4 && remainingSupplierCount == 0;
                shouldNotifyCompletionToPurchaser = currentLevel == 4 && remainingSupplierCount == 0;
            }

            if (!IsSupplierLinkMode)
            {
                LoadSupplierRows();
            }

            if (!IsSupplierLinkMode)
            {
                MarkSupplierProcessedInCurrentLogin(supplierId);
            }

            var notifyResult = shouldNotifyCompletionToPurchaser
                ? TryQueueNotifyApprovalCompleted(currentLevel, current, "approved")
                : shouldNotifyNextLevel
                    ? TryQueueNotifyNextLevel(currentLevel, current, "approved")
                    : null;
            if (DidTriggerScopeMailNotification(notifyResult))
            {
                SetCurrentApprovalScopeActionLocked(true);
            }
            var canRestore = string.IsNullOrWhiteSpace(notifyResult);
            SetSupplierUndoStateInCurrentLogin(new ApproveSupplierUndoState
            {
                ActionType = ApproveSupplierActionType.Approve,
                SupplierId = supplierId,
                AppliedStatus = currentLevel,
                IsLocked = !canRestore,
                Snapshot = undoSnapshot
            });

            CurrentSupplierId = IsSupplierLinkMode ? supplierId : GetNextActiveSupplierIdAfterProcessing(supplierId);
            var approveMessage = "Approved supplier successfully.";
            if (!string.IsNullOrWhiteSpace(notifyResult))
            {
                approveMessage += $" {notifyResult}";
            }

            SetFlashMessage(approveMessage, "success");
            return RedirectToCurrentList();
        }

        // X? l� thao t�c t? ch?i supplier tr�n m�n h�nh.
        public IActionResult OnPostDisapprove()
        {
            SupplierId = ResolveSupplierIdFromRequest();

            // 1. L?y t?p quy?n th?c t? c?a role login
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

            var undoSnapshot = GetSupplierUndoSnapshot(supplierId);
            if (undoSnapshot is null)
            {
                SetFlashMessage("Cannot load supplier snapshot for restore.", "error");
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

            SetSupplierUndoStateInCurrentLogin(new ApproveSupplierUndoState
            {
                ActionType = ApproveSupplierActionType.Disapprove,
                SupplierId = supplierId,
                AppliedStatus = 5,
                IsLocked = false,
                Snapshot = undoSnapshot
            });

            CurrentSupplierId = IsSupplierLinkMode ? supplierId : GetNextActiveSupplierIdAfterProcessing(supplierId);
            var disapproveMessage = "Disapproved supplier successfully.";

            SetFlashMessage(disapproveMessage, "success");
            return RedirectToCurrentList();
        }

        // Kh�i ph?c l?i tr?ng th�i tru?c khi v?a approve/disapprove.
        public IActionResult OnPostRestore()
        {
            SupplierId = ResolveSupplierIdFromRequest();

            PagePerm = GetUserPermissions();
            LoadUserDataScope();
            if (!HasPermission(PermissionApprove))
            {
                return Redirect("/");
            }

            var supplierId = EditSupplier.SupplierID;
            if (supplierId <= 0)
            {
                SetFlashMessage("Invalid supplier.", "warning");
                return RedirectToCurrentList();
            }

            var undoState = GetSupplierUndoStateInCurrentLogin(supplierId);
            if (undoState is null)
            {
                SetFlashMessage("No restore information was found for this supplier.", "warning");
                return RedirectToCurrentList();
            }

            if (undoState.IsLocked)
            {
                SetFlashMessage("This supplier can no longer be restored because notification email has already been sent.", "warning");
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

            if (!RestoreSupplierFromUndoState(undoState))
            {
                SetFlashMessage("Restore failed because supplier state has changed.", "warning");
                return RedirectToCurrentList();
            }

            RemoveSupplierUndoStateInCurrentLogin(supplierId);
            UnmarkSupplierProcessedInCurrentLogin(supplierId);
            CurrentSupplierId = supplierId;
            SetFlashMessage("Restored supplier approval state successfully.", "success");
            return RedirectToCurrentList();
        }

        // Duy?t h�ng lo?t t?t c? supplier dang ch? duy?t trong scope hi?n t?i.
        public IActionResult OnPostBatchApprove()
        {
            SupplierId = ResolveSupplierIdFromRequest();

            PagePerm = GetUserPermissions();
            LoadUserDataScope();
            if (!HasPermission(PermissionApprove))
            {
                return Redirect("/");
            }

            if (IsSupplierLinkMode)
            {
                SetFlashMessage("Batch approve is only available in the approval list.", "warning");
                return RedirectToCurrentList();
            }

            if (!_dataScope.LevelCheckSupplier.HasValue || _dataScope.LevelCheckSupplier.Value is < 1 or > 4)
            {
                SetFlashMessage("You have no right to batch approve.", "warning");
                return RedirectToCurrentList();
            }

            var currentLevel = _dataScope.LevelCheckSupplier.Value;
            var operatorCode = User.Identity?.Name?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(operatorCode))
            {
                SetFlashMessage("Cannot identify operator.", "error");
                return RedirectToCurrentList();
            }

            var pendingRows = SearchSupplierRows(BuildCriteria());
            if (pendingRows.Count == 0)
            {
                SetFlashMessage("There are no suppliers waiting for approval in the current scope.", "warning");
                return RedirectToCurrentList();
            }

            var sampleSupplier = GetSupplierDetail(pendingRows[0].SupplierID);
            if (sampleSupplier is null)
            {
                SetFlashMessage("Cannot load supplier data for batch approval.", "error");
                return RedirectToCurrentList();
            }

            var updatedRows = BatchApproveSuppliers(pendingRows.Select(x => x.SupplierID).ToList(), currentLevel, operatorCode);
            if (updatedRows <= 0)
            {
                SetFlashMessage("Batch approve failed because supplier status is no longer in the expected workflow step.", "warning");
                return RedirectToCurrentList();
            }

            MarkSuppliersProcessedInCurrentLogin(pendingRows.Select(x => x.SupplierID));
            ClearAllSupplierUndoStatesInCurrentLogin();

            var notifyResult = currentLevel == 4
                ? TryQueueNotifyApprovalCompleted(currentLevel, sampleSupplier, "approved")
                : TryQueueNotifyNextLevel(currentLevel, sampleSupplier, "approved");
            if (DidTriggerScopeMailNotification(notifyResult))
            {
                SetCurrentApprovalScopeActionLocked(true);
            }

            CurrentSupplierId = null;
            var batchMessage = $"Batch approved {updatedRows} supplier(s) successfully.";
            if (!string.IsNullOrWhiteSpace(notifyResult))
            {
                batchMessage += $" {notifyResult}";
            }

            SetFlashMessage(batchMessage, "success");
            return RedirectToCurrentList();
        }

        // X? l� luu nhanh ph?n ghi ch� b? sung c?a supplier.
        public IActionResult OnPostSaveMoreComment()
        {
            SupplierId = ResolveSupplierIdFromRequest();

            // 1. L?y t?p quy?n th?c t? c?a role login
            PagePerm = GetUserPermissions();
            LoadUserDataScope();
            if (!HasPermission(PermissionViewList))
            {
                return RedirectHomeWithAccessError("Missing permission: View Approve Supplier list (FunctionID 145, PermissionNo 1).");
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

        // N?p danh s�ch supplier theo d�ng ph?m vi quy?n hi?n t?i.
        private void LoadSupplierRows()
        {
            var criteria = BuildCriteria();
            Rows = SearchSupplierRows(criteria);
            Rows = MergeProcessedSupplierRows(Rows);
        }

        // N?p chi ti?t supplier dang du?c ch?n tr�n m�n h�nh.
        private void LoadCurrentSupplierDetailData()
        {
            CurrentSupplierDetail = null;
            PurchaseOrderInfo = new ApprovePurchaseOrderInfoViewModel();
            SupplierServiceRows = new List<ApproveSupplierServiceRowViewModel>();
            CurrentSupplierPosition = 0;
            IsCurrentSupplierReadOnly = false;
            CanRestoreCurrentSupplier = false;
            HideApprovalActionButtons = IsCurrentApprovalScopeActionLocked() || HasLockedSupplierUndoStateInCurrentLogin();
            RestoreActionCaption = "Restore Action";
            FirstSupplierId = null;
            LastSupplierId = null;
            PrevSupplierId = null;
            NextSupplierId = null;

            if (Rows.Count == 0)
            {
                CurrentSupplierId = null;
                if (string.IsNullOrWhiteSpace(GoToKeywordTemp))
                {
                    GoToKeyword = string.Empty;
                }
                return;
            }

            // 2. X�c d?nh v? tr� record dang ch?n tr�n danh s�ch hi?n t?i
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
            FirstSupplierId = Rows[0].SupplierID;
            LastSupplierId = Rows[^1].SupplierID;
            PrevSupplierId = selectedIndex > 0 ? Rows[selectedIndex - 1].SupplierID : null;
            NextSupplierId = selectedIndex < Rows.Count - 1 ? Rows[selectedIndex + 1].SupplierID : null;

            // 3. Load chi ti?t, th�ng tin PO v� l?ch s? service
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
                    if (string.IsNullOrWhiteSpace(GoToKeywordTemp))
                    {
                        GoToKeyword = string.Empty;
                    }
                    return;
                }

                if (!IsApproveSupplierNewMode)
                {
                    PurchaseOrderInfo = GetPurchaseOrderInfo(CurrentSupplierDetail.SupplierID, DateTime.Now.Year);
                }

                SupplierServiceRows = GetSupplierServiceRows(CurrentSupplierDetail.SupplierID);

                if (!IsSupplierLinkMode && IsProcessedSupplierInCurrentLogin(CurrentSupplierDetail.SupplierID))
                {
                    // 4. Trong c�ng phi�n dang nh?p, supplier d� approve/disapprove v?n hi?n th? nhung b? kh�a thao t�c.
                    IsCurrentSupplierReadOnly = true;

                    var undoState = GetSupplierUndoStateInCurrentLogin(CurrentSupplierDetail.SupplierID);
                    if (undoState is not null && !undoState.IsLocked && !HideApprovalActionButtons)
                    {
                        CanRestoreCurrentSupplier = true;
                        RestoreActionCaption = undoState.ActionType == ApproveSupplierActionType.Disapprove
                            ? "Restore Disapprove"
                            : "Restore Approve";
                    }
                }
            }
        }

        // Truy v?n danh s�ch supplier theo di?u ki?n l?c hi?n t?i.
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

        // L?y th�ng tin chi ti?t c?a supplier d? hi?n th? v� x? l�.
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
                    st.SupplierStatusName,
                    s.PurchaserCode,
                    s.PurchaserPreparedDate
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
                SupplierStatusName = Convert.ToString(rd[20]) ?? string.Empty,
                PurchaserCode = Convert.ToString(rd[21]) ?? string.Empty,
                PurchaserPreparedDate = rd.IsDBNull(22) ? null : Convert.ToDateTime(rd[22])
            };
        }

        // L?y th�ng tin Purchase Order li�n quan c?a supplier.
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

        // L?y l?ch s? d?ch v? c?a supplier d? hi?n th?.
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

        // N?p ph?m vi d? li?u c?a ngu?i d�ng dang nh?p.
        private void LoadUserDataScope()
        {
            _isAdminRole = string.Equals(User.FindFirst("IsAdminRole")?.Value, "True", StringComparison.OrdinalIgnoreCase);
            _dataScope = new ApproveEmployeeDataScopeViewModel();

            // 2. L?y m� nh�n vi�n d? d?c ph?m vi ph�ng ban v� c?p duy?t
            var employeeCode = User.Identity?.Name?.Trim();
            if (string.IsNullOrWhiteSpace(employeeCode))
            {
                return;
            }

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand(@"
                SELECT TOP 1 DeptID, LevelCheckSupplier, ISNULL(HeadDept,0) AS HeadDept, ISNULL(IsCFO,0) AS IsCFO, ISNULL(IsBOD,0) AS IsBOD, ISNULL(IsPurchaser,0) AS IsPurchaser
                FROM dbo.MS_Employee
                WHERE EmployeeCode = @EmployeeCode", conn);

            cmd.Parameters.AddWithValue("@EmployeeCode", employeeCode);
            conn.Open();
            using var rd = cmd.ExecuteReader();
            if (rd.Read())
            {
                _dataScope.DeptID = rd.IsDBNull(0) ? null : Convert.ToInt32(rd[0]);
                _dataScope.LevelCheckSupplier = rd.IsDBNull(1) ? null : Convert.ToInt32(rd[1]);
                _dataScope.HeadDept = !rd.IsDBNull(2) && Convert.ToInt32(rd[2]) == 1;
                _dataScope.IsCFO = !rd.IsDBNull(3) && Convert.ToInt32(rd[3]) == 1;
                _dataScope.IsBOD = !rd.IsDBNull(4) && Convert.ToInt32(rd[4]) == 1;
                _dataScope.IsPurchaser = !rd.IsDBNull(5) && Convert.ToInt32(rd[5]) == 1;

                if (IsSupplierLinkMode)
                {
                    _dataScope.LevelCheckSupplier = _dataScope.HeadDept ? 2
                        : _dataScope.IsCFO ? 3
                        : _dataScope.IsBOD ? 4
                        : _dataScope.LevelCheckSupplier;
                }
                _dataScope.HeadDept = !rd.IsDBNull(2) && Convert.ToInt32(rd[2]) == 1;
                _dataScope.IsCFO = !rd.IsDBNull(3) && Convert.ToInt32(rd[3]) == 1;
                _dataScope.IsBOD = !rd.IsDBNull(4) && Convert.ToInt32(rd[4]) == 1;
                _dataScope.IsPurchaser = !rd.IsDBNull(5) && Convert.ToInt32(rd[5]) == 1;

                if (IsSupplierLinkMode)
                {
                    _dataScope.LevelCheckSupplier = _dataScope.HeadDept ? 2
                        : _dataScope.IsCFO ? 3
                        : _dataScope.IsBOD ? 4
                        : _dataScope.LevelCheckSupplier;
                }
            }

        }

        // L?y t?p quy?n th?c t? c?a ngu?i d�ng tr�n ch?c nang hi?n t?i.
        private PagePermissions GetUserPermissions()
        {
            bool isAdmin = string.Equals(User.FindFirst("IsAdminRole")?.Value, "True", StringComparison.OrdinalIgnoreCase)
                || string.Equals(User.FindFirst("IsAdminRole")?.Value, "1", StringComparison.OrdinalIgnoreCase);
            int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");

            // 1. Kh?i t?o d?i tu?ng PagePermissions m?i
            var permsObj = new PagePermissions();

            if (isAdmin)
            {
                // 2. N?u l� admin th� c?p d?y d? quy?n d? d?ng b? v?i c�ch hi?n th? menu
                permsObj.AllowedNos = Enumerable.Range(1, 20).ToList();
            }
            else
            {
                // 3. User thu?ng l?y t?p quy?n th?c t? c?a role theo d�ng FunctionID
                permsObj.AllowedNos = _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, 1);
            }

            // 4. Tr? v? object ch?a t?p quy?n c?a ngu?i d�ng
            return permsObj;
        }

        private bool HasPermission(int permissionNo) => PagePerm.HasPermission(permissionNo);

        // Ki?m tra ngu?i d�ng c� du?c xem t?t c? ph�ng ban hay kh�ng.
        private bool CanViewAllDepartments()
        {
            return _isAdminRole
                || (_dataScope.LevelCheckSupplier.HasValue
                && (_dataScope.LevelCheckSupplier.Value == 1
                    || _dataScope.LevelCheckSupplier.Value == 3
                    || _dataScope.LevelCheckSupplier.Value == 4));
        }

        // Ki?m tra supplier c� thu?c ph?m vi ph�ng ban du?c ph�p truy c?p hay kh�ng.
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

        // T?o di?u ki?n t�m ki?m supplier theo ph?m vi d? li?u hi?n t?i.
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

        // X�c d?nh ph�ng ban c?n �p d?ng khi truy v?n danh s�ch.
        private int? ResolveDepartmentFilter()
        {
            if (CanViewAllDepartments())
            {
                return null;
            }

            return _dataScope.DeptID ?? NoDepartmentScopeValue;
        }

        // X�c d?nh tr?ng th�i c?n duy?t theo c?p duy?t hi?n t?i.
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

        // G�n tham s? t�m ki?m v�o c�u l?nh truy v?n supplier.
        private void BindSearchParams(SqlCommand cmd, ApproveFilterCriteria criteria)
        {
            cmd.Parameters.Add("@DeptID", SqlDbType.Int).Value = criteria.DeptId.HasValue ? criteria.DeptId.Value : DBNull.Value;
            cmd.Parameters.Add("@StatusID", SqlDbType.Int).Value = criteria.StatusId.HasValue ? criteria.StatusId.Value : DBNull.Value;
            cmd.Parameters.Add("@MaxStatusExclusive", SqlDbType.Int).Value = criteria.MaxStatusExclusive.HasValue ? criteria.MaxStatusExclusive.Value : DBNull.Value;
            cmd.Parameters.Add("@SupplierID", SqlDbType.Int).Value = criteria.SupplierId.HasValue ? criteria.SupplierId.Value : DBNull.Value;
            cmd.Parameters.Add("@IsNew", SqlDbType.Bit).Value = criteria.IsNew;
        }

        // �?c m� supplier t? request hi?n t?i d? x? l� d�ng b?n ghi.
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

        // T�m supplier theo Supplier Code ho?c Supplier Name trong danh s�ch hi?n t?i.
        private int? FindSupplierIdByKeyword(string keyword)
        {
            if (string.IsNullOrWhiteSpace(keyword) || Rows.Count == 0)
            {
                return null;
            }

            var normalizedKeyword = keyword.Trim();

            static bool TextEquals(string? left, string right) =>
                string.Equals(left?.Trim(), right, StringComparison.OrdinalIgnoreCase);

            static bool TextContains(string? left, string right) =>
                !string.IsNullOrWhiteSpace(left) && left.Contains(right, StringComparison.OrdinalIgnoreCase);

            return Rows.FirstOrDefault(x => TextEquals(x.SupplierCode, normalizedKeyword))?.SupplierID
                ?? Rows.FirstOrDefault(x => TextEquals(x.SupplierName, normalizedKeyword))?.SupplierID
                ?? Rows.FirstOrDefault(x => TextContains(x.SupplierCode, normalizedKeyword))?.SupplierID
                ?? Rows.FirstOrDefault(x => TextContains(x.SupplierName, normalizedKeyword))?.SupplierID;
        }

        // �?m s? supplier c�n dang ch? duy?t trong scope hi?n t?i sau khi thao t�c.
        private int CountPendingSuppliersInCurrentApprovalScope()
        {
            if (IsSupplierLinkMode)
            {
                return 0;
            }

            return SearchSupplierRows(BuildCriteria()).Count;
        }

        // �?ng b? c? ?n thao t�c c?a scope hi?n t?i theo tr?ng th�i pending th?c t?.
        private void RefreshCurrentApprovalScopeActionLock()
        {
            if (!IsCurrentApprovalScopeActionLocked())
            {
                return;
            }

            if (IsSupplierLinkMode)
            {
                return;
            }

            // Khi scope d� bu?c sang ph�t mail th� gi? nguy�n c? kh�a cho to�n b? phi�n hi?n t?i.
        }

        private bool DidTriggerScopeMailNotification(string? notifyResult)
        {
            return !string.IsNullOrWhiteSpace(notifyResult)
                && notifyResult.Contains("being sent", StringComparison.OrdinalIgnoreCase);
        }

        // �p d?ng qu t?c truy c?p khi m? supplier theo link tr?c ti?p.
        private bool ApplySupplierLinkAccessRule(ApproveSupplierDetailViewModel supplier)
        {
            if (!IsSupplierLinkMode)
            {
                return true;
            }

            if (!supplier.IsNew)
            {
                SetFlashMessage("Supplier is not a new supplier.", "error");
                return false;
            }

            if (!_dataScope.LevelCheckSupplier.HasValue || _dataScope.LevelCheckSupplier.Value is < 1 or > 4)
            {
                SetFlashMessage("You have no right to access this approval link.", "error");
                return false;
            }

            var currentLevel = _dataScope.LevelCheckSupplier.Value;
            var supplierStatus = supplier.Status ?? 0;

            // 4. Link duy?t m?i ch? cho ph�p 3 tr?ng th�i: dang ch? m�nh duy?t, d� duy?t xong b?i m�nh, ho?c ?n n?u l?ch workflow.
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
                SetFlashMessage("This supplier has already passed your approval level.", "error");
                return false;
            }

            SetFlashMessage("This supplier is not in your approval step yet.", "error");
            return false;
        }

        // Ki?m tra supplier c� c�n du?c ph�p ch?nh s?a ? bu?c duy?t hi?n t?i hay kh�ng.
        private bool CanEditBySupplierLinkState(ApproveSupplierDetailViewModel supplier)
        {
            if (!_dataScope.LevelCheckSupplier.HasValue || _dataScope.LevelCheckSupplier.Value is < 1 or > 4)
            {
                return false;
            }

            return (supplier.Status ?? 0) == _dataScope.LevelCheckSupplier.Value - 1;
        }

        // Ki?m tra m� supplier d� t?n t?i trong h? th?ng hay chua.
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

        // C?p nh?t ph?n ghi ch� c?a supplier theo y�u c?u duy?t.
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

        // C?p nh?t th�ng tin supplier ph?c v? thao t�c duy?t.
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

        // Duy?t h�ng lo?t danh s�ch supplier dang ? c�ng m?t bu?c workflow.
        private int BatchApproveSuppliers(List<int> supplierIds, int levelCheckSupplier, string operatorCode)
        {
            if (supplierIds.Count == 0)
            {
                return 0;
            }

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            conn.Open();
            using var trans = conn.BeginTransaction();
            using var cmd = new SqlCommand(@"
                UPDATE dbo.PC_Suppliers
                SET
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
                  AND ISNULL([Status], 0) + 1 = @LevelCheck", conn, trans);

            cmd.Parameters.Add("@SupplierID", SqlDbType.Int);
            cmd.Parameters.AddWithValue("@LevelCheck", levelCheckSupplier);
            cmd.Parameters.AddWithValue("@OperatorCode", operatorCode);

            var totalUpdated = 0;
            foreach (var supplierId in supplierIds)
            {
                cmd.Parameters["@SupplierID"].Value = supplierId;
                totalUpdated += cmd.ExecuteNonQuery();
            }

            if (totalUpdated != supplierIds.Count)
            {
                trans.Rollback();
                return 0;
            }

            trans.Commit();
            return totalUpdated;
        }

        // Th?c hi?n nghi?p v? duy?t supplier theo c?p duy?t hi?n t?i.
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

        // Th?c hi?n nghi?p v? t? ch?i supplier theo c?p duy?t hi?n t?i.
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

        // L?y danh s�ch ngu?i nh?n mail theo c?p duy?t k? ti?p.
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

        private List<ApproveSupplierNotifyRecipientViewModel> GetEmailsByEmployeeFlag(string flagColumnName, int? deptId)
        {
            var rows = new List<ApproveSupplierNotifyRecipientViewModel>();
            if (!deptId.HasValue)
            {
                return rows;
            }

            var allowedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "HeadDept",
                "IsCFO",
                "IsBOD",
                "IsPurchaser"
            };
            if (!allowedColumns.Contains(flagColumnName))
            {
                return rows;
            }

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand($@"
                SELECT DISTINCT
                    LTRIM(RTRIM(TheEmail)) AS TheEmail,
                    LTRIM(RTRIM(EmployeeCode)) AS EmployeeCode,
                    LTRIM(RTRIM(EmployeeName)) AS EmployeeName,
                    LTRIM(RTRIM(ISNULL(Title, ''))) AS Title
                FROM dbo.MS_Employee
                WHERE ISNULL({flagColumnName}, 0) = 1
                  AND DeptID = @DeptID
                  AND ISNULL(LTRIM(RTRIM(TheEmail)), '') <> ''
                  AND ISNULL(IsActive, 0) = 1", conn);
            cmd.Parameters.Add("@DeptID", SqlDbType.Int).Value = deptId.Value;
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

        private List<ApproveSupplierNotifyRecipientViewModel> GetPurchaserRecipientsByCode(string? purchaserCode)
        {
            var rows = new List<ApproveSupplierNotifyRecipientViewModel>();
            if (string.IsNullOrWhiteSpace(purchaserCode))
            {
                return rows;
            }

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand(@"
                SELECT DISTINCT
                    LTRIM(RTRIM(TheEmail)) AS TheEmail,
                    LTRIM(RTRIM(EmployeeCode)) AS EmployeeCode,
                    LTRIM(RTRIM(EmployeeName)) AS EmployeeName,
                    LTRIM(RTRIM(ISNULL(Title, ''))) AS Title
                FROM dbo.MS_Employee
                WHERE EmployeeCode = @EmployeeCode
                  AND ISNULL(LTRIM(RTRIM(TheEmail)), '') <> ''
                  AND ISNULL(IsActive, 0) = 1", conn);
            cmd.Parameters.Add("@EmployeeCode", SqlDbType.NVarChar, 50).Value = purchaserCode.Trim();
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
        private static string? GetNewSupplierNextRecipientFlag(int currentLevel)
        {
            return currentLevel switch
            {
                2 => "IsCFO",
                3 => "IsBOD",
                _ => null
            };
        }

        private static string GetNewSupplierApprovalRoleName(int currentLevel)
        {
            return currentLevel switch
            {
                2 => "CFO",
                3 => "BOD",
                _ => "next approver"
            };
        }

        // T?o y�u c?u g?i mail th�ng b�o cho c?p duy?t ti?p theo.
        private string? TryQueueNotifyNextLevel(int currentLevel, ApproveSupplierDetailViewModel supplier, string action)
        {
            if (currentLevel >= 4)
            {
                return null;
            }

            var nextLevel = currentLevel + 1;
            var recipients = IsApproveSupplierNewMode
                ? GetEmailsByEmployeeFlag(GetNewSupplierNextRecipientFlag(currentLevel) ?? string.Empty, supplier.DeptID)
                : GetEmailsByLevelCheck(nextLevel, supplier.DeptID);
            if (recipients.Count == 0)
            {
                return IsApproveSupplierNewMode
                    ? $"No email recipients were found for {GetNewSupplierApprovalRoleName(currentLevel)} in the same department."
                    : $"No email recipients were found for the next level.";
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
            var actionTimeText = DateTime.Now.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

            // 1. D�ng chung m?t page duy?t supplier, ch? ph�n bi?t b?ng tham s? type=new
            var detailUrl = Url.Page("/Purchasing/ApproveSupplier/Index", values: new
            {
                Type = IsApproveSupplierNewMode ? "new" : null,
                supplier_id = IsApproveSupplierNewMode ? (int?)supplier.SupplierID : null
            });

            var absoluteUrl = string.IsNullOrWhiteSpace(detailUrl)
                ? string.Empty
                : $"{Request.Scheme}://{Request.Host}{detailUrl}";

            var subject = IsApproveSupplierNewMode
                ? $"[Approve Supplier New] Supplier is waiting for {GetNewSupplierApprovalRoleName(currentLevel)} approval"
                : $"[Supplier Approval] Last supplier processed at level {currentLevel}";
            subject = ApplyMailSubjectPrefix(subject);
            var body = IsApproveSupplierNewMode
                ? $@"
<p>Dear {{RECIPIENT_LABEL}},</p>
<p>The supplier has been <b>{action}</b> and is now waiting for {WebUtility.HtmlEncode(GetNewSupplierApprovalRoleName(currentLevel))} approval.</p>
<ul>
  <li>Supplier Code: <b>{WebUtility.HtmlEncode(supplierCode)}</b></li>
  <li>Supplier Name: <b>{WebUtility.HtmlEncode(supplierName)}</b></li>
  <li>Action by: <b>{WebUtility.HtmlEncode(operatorCode)}</b></li>
  <li>Action time: <b>{actionTimeText}</b></li>
</ul>
{(string.IsNullOrWhiteSpace(absoluteUrl) ? string.Empty : $"<p>Open Approval Page: <a href=\"{WebUtility.HtmlEncode(absoluteUrl)}\">{WebUtility.HtmlEncode(PageTitle)}</a></p>")}
<p>SmartSam System</p>"
                : $@"
<p>Dear {{RECIPIENT_LABEL}},</p>
<p>The current supplier approval list has been <b>{action}</b> at level {currentLevel}.</p>
<p>Please access the approval page to continue reviewing the next suppliers in your workflow.</p>
<ul>
  <li>Action by: <b>{WebUtility.HtmlEncode(operatorCode)}</b></li>
  <li>Action time: <b>{actionTimeText}</b></li>
</ul>
{(string.IsNullOrWhiteSpace(absoluteUrl) ? string.Empty : $"<p>Open Approval Page: <a href=\"{WebUtility.HtmlEncode(absoluteUrl)}\">{WebUtility.HtmlEncode(PageTitle)}</a></p>")}
<p>SmartSam System</p>";
            body = WrapNotifyMessageBody(body);
            var htmlBody = IsApproveSupplierNewMode
                ? EmailTemplateHelper.WrapInNotifyTemplate("APPROVE SUPPLIER NEW", "#17a2b8", DateTime.Now, body)
                : EmailTemplateHelper.WrapInNotifyTemplate("APPROVE SUPPLIER", "#007bff", DateTime.Now, body);

            // 2. �?y t�c v? g?i mail sang n?n d? ngu?i d�ng kh�ng ph?i ch? SMTP ph?n h?i
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
            return IsApproveSupplierNewMode
                ? $"The notification email is being sent to {GetNewSupplierApprovalRoleName(currentLevel)}."
                : $"The notification email is being sent to the next level.";
        }

        // T?o y�u c?u g?i mail th�ng b�o d� ho�n t?t duy?t supplier cho purchaser.
        private string? TryQueueNotifyApprovalCompleted(int currentLevel, ApproveSupplierDetailViewModel supplier, string action)
        {
            if (currentLevel != 4)
            {
                return null;
            }

            var recipients = IsApproveSupplierNewMode
                ? GetPurchaserRecipientsByCode(supplier.PurchaserCode)
                : GetEmailsByLevelCheck(1, supplier.DeptID);
            if (recipients.Count == 0)
            {
                return IsApproveSupplierNewMode
                    ? "No purchaser email recipient was found for the original submitter."
                    : "No PU email recipients were found.";
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
                return "Email settings are missing. Skip completion notification to PU.";
            }

            var operatorCode = User.Identity?.Name?.Trim() ?? "SYSTEM";
            var supplierCode = string.IsNullOrWhiteSpace(supplier.SupplierCode) ? supplier.SupplierID.ToString() : supplier.SupplierCode;
            var supplierName = string.IsNullOrWhiteSpace(supplier.SupplierName) ? "-" : supplier.SupplierName;
            var recipientDisplayNames = string.Join(", ", recipients.Select(BuildRecipientDisplayName));
            var actionTimeText = DateTime.Now.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);

            var detailUrl = IsApproveSupplierNewMode
                ? Url.Page("/Purchasing/Supplier/SupplierDetail", values: new
                {
                    id = supplier.SupplierID,
                    mode = "view"
                })
                : Url.Page("/Purchasing/Supplier/Index", values: new
                {
                    StatusIdsCsv = "4"
                });

            var absoluteUrl = string.IsNullOrWhiteSpace(detailUrl)
                ? string.Empty
                : $"{Request.Scheme}://{Request.Host}{detailUrl}";

            var subject = IsApproveSupplierNewMode
                ? "[Approve Supplier New] Supplier approval completed by BOD"
                : "[Supplier Approval] Supplier approval completed by BOD";
            subject = ApplyMailSubjectPrefix(subject);

            var body = IsApproveSupplierNewMode
                ? $@"
<p>Dear {{RECIPIENT_LABEL}},</p>
<p>The supplier new approval workflow has been <b>{action}</b> completely by BOD.</p>
<p>This is a notification email for the purchaser. No further approval action is required.</p>
<ul>
  <li>Supplier Code: <b>{WebUtility.HtmlEncode(supplierCode)}</b></li>
  <li>Supplier Name: <b>{WebUtility.HtmlEncode(supplierName)}</b></li>
  <li>Department ID: <b>{WebUtility.HtmlEncode(supplier.DeptID?.ToString() ?? "-")}</b></li>
  <li>Action by: <b>{WebUtility.HtmlEncode(operatorCode)}</b></li>
  <li>Action time: <b>{actionTimeText}</b></li>
</ul>
{(string.IsNullOrWhiteSpace(absoluteUrl) ? string.Empty : $"<p>Open Review Page: <a href=\"{WebUtility.HtmlEncode(absoluteUrl)}\">{WebUtility.HtmlEncode(IsApproveSupplierNewMode ? "Supplier Detail" : "Supplier List")}</a></p>")}
<p>SmartSam System</p>"
                : $@"
<p>Dear {{RECIPIENT_LABEL}},</p>
<p>The current supplier approval list has been <b>{action}</b> completely by BOD.</p>
<p>All suppliers in the current approval batch have finished the approval workflow.</p>
<ul>
  <li>Action by: <b>{WebUtility.HtmlEncode(operatorCode)}</b></li>
  <li>Action time: <b>{actionTimeText}</b></li>
</ul>
{(string.IsNullOrWhiteSpace(absoluteUrl) ? string.Empty : $"<p>Open Review Page: <a href=\"{WebUtility.HtmlEncode(absoluteUrl)}\">Supplier List</a></p>")}
<p>SmartSam System</p>";
            body = WrapNotifyMessageBody(body);

            var htmlBody = IsApproveSupplierNewMode
                ? EmailTemplateHelper.WrapInNotifyTemplate("APPROVE SUPPLIER NEW", "#17a2b8", DateTime.Now, body)
                : EmailTemplateHelper.WrapInNotifyTemplate("APPROVE SUPPLIER", "#007bff", DateTime.Now, body);

            var notifyRequest = new ApproveSupplierNotifyRequestViewModel
            {
                NextLevel = 1,
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
            return IsApproveSupplierNewMode
                ? "The completion notification email is being sent to the purchaser."
                : "The completion notification email is being sent to PU.";
        }

        // Ch? �p font cho n?i dung ri�ng c?a ch?c nang, c�n khung ngo�i d�ng template h? th?ng.
        private static string WrapNotifyMessageBody(string messageBody)
        {
            return $"<div style='font-family:{NotifyFontFamily};'>{messageBody}</div>";
        }

        // �p ti?n t? subject theo c?u h�nh EmailSettings khi FunctionID hi?n t?i thu?c danh s�ch test.
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

        // Ki?m tra FunctionID c?a ch?c nang duy?t supplier c� n?m trong c?u h�nh TestFunctionIDs hay kh�ng.
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

        // G?i email th�ng b�o b?t d?ng b? cho c?p duy?t ti?p theo.
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

        // Chu?n h�a t�n hi?n th? c?a ngu?i nh?n trong n?i dung mail.
        private static string BuildRecipientDisplayName(ApproveSupplierNotifyRecipientViewModel recipient)
        {
            var title = (recipient.Title ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(title))
            {
                var titledEmployeeName = string.IsNullOrWhiteSpace(recipient.EmployeeName) ? recipient.Email : recipient.EmployeeName;
                return string.IsNullOrWhiteSpace(recipient.EmployeeCode)
                    ? $"{title} {titledEmployeeName}"
                    : $"{title} {titledEmployeeName}({recipient.EmployeeCode})";
            }

            var employeeName = string.IsNullOrWhiteSpace(recipient.EmployeeName) ? recipient.Email : recipient.EmployeeName;
            return string.IsNullOrWhiteSpace(recipient.EmployeeCode)
                ? employeeName
                : $"{employeeName} ({recipient.EmployeeCode})";
        }

        // G�n th�ng b�o t?m th?i d? hi?n th? l?i sau khi redirect.
        private void SetFlashMessage(string message, string type = "info")
        {
            FlashMessage = message;
            FlashMessageType = type;
        }

        private void SetGlobalFlashMessage(string message, string type = "info")
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var normalizedType = string.IsNullOrWhiteSpace(type) ? "info" : type.Trim().ToLowerInvariant();
            var tempDataKey = normalizedType switch
            {
                "success" => "LayoutSuccessMessage",
                "warning" => "LayoutWarningMessage",
                "error" => "LayoutErrorMessage",
                "danger" => "LayoutErrorMessage",
                _ => "LayoutWarningMessage"
            };

            TempData[tempDataKey] = message;
        }

        private IActionResult RedirectHomeWithAccessError(string detail)
        {
            SetGlobalFlashMessage($"Cannot access Approve Supplier. {detail}", "error");
            return Redirect("/");
        }


        // �i?u hu?ng ngu?i d�ng v? d�ng m�n h�nh danh s�ch hi?n t?i.
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

        // Ch?p snapshot ph?c v? thao t�c kh�i ph?c sau approve/disapprove.
        private ApproveSupplierUndoSnapshot? GetSupplierUndoSnapshot(int supplierId)
        {
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand(@"
                SELECT
                    SupplierID,
                    SupplierCode,
                    SupplierName,
                    Address,
                    Phone,
                    Mobile,
                    Fax,
                    Contact,
                    [Position],
                    Business,
                    ApprovedDate,
                    [Document],
                    Certificate,
                    Service,
                    Comment,
                    IsNew,
                    CodeOfAcc,
                    DeptID,
                    [Status],
                    PurchaserCode,
                    PurchaserPreparedDate,
                    DepartmentCode,
                    DepartmentApproveDate,
                    FinancialCode,
                    FinancialApproveDate,
                    BODCode,
                    BODApproveDate,
                    IsApproved
                FROM dbo.PC_Suppliers
                WHERE SupplierID = @SupplierID", conn);

            cmd.Parameters.Add("@SupplierID", SqlDbType.Int).Value = supplierId;
            conn.Open();
            using var rd = cmd.ExecuteReader();
            if (!rd.Read())
            {
                return null;
            }

            return new ApproveSupplierUndoSnapshot
            {
                SupplierId = Convert.ToInt32(rd["SupplierID"]),
                SupplierCode = Convert.ToString(rd["SupplierCode"]),
                SupplierName = Convert.ToString(rd["SupplierName"]),
                Address = Convert.ToString(rd["Address"]),
                Phone = Convert.ToString(rd["Phone"]),
                Mobile = Convert.ToString(rd["Mobile"]),
                Fax = Convert.ToString(rd["Fax"]),
                Contact = Convert.ToString(rd["Contact"]),
                Position = Convert.ToString(rd["Position"]),
                Business = Convert.ToString(rd["Business"]),
                ApprovedDate = rd["ApprovedDate"] == DBNull.Value ? null : Convert.ToDateTime(rd["ApprovedDate"]),
                Document = rd["Document"] != DBNull.Value && Convert.ToBoolean(rd["Document"]),
                Certificate = Convert.ToString(rd["Certificate"]),
                Service = Convert.ToString(rd["Service"]),
                Comment = Convert.ToString(rd["Comment"]),
                IsNew = rd["IsNew"] != DBNull.Value && Convert.ToBoolean(rd["IsNew"]),
                CodeOfAcc = Convert.ToString(rd["CodeOfAcc"]),
                DeptID = rd["DeptID"] == DBNull.Value ? null : Convert.ToInt32(rd["DeptID"]),
                Status = rd["Status"] == DBNull.Value ? null : Convert.ToInt32(rd["Status"]),
                PurchaserCode = Convert.ToString(rd["PurchaserCode"]),
                PurchaserPreparedDate = rd["PurchaserPreparedDate"] == DBNull.Value ? null : Convert.ToDateTime(rd["PurchaserPreparedDate"]),
                DepartmentCode = Convert.ToString(rd["DepartmentCode"]),
                DepartmentApproveDate = rd["DepartmentApproveDate"] == DBNull.Value ? null : Convert.ToDateTime(rd["DepartmentApproveDate"]),
                FinancialCode = Convert.ToString(rd["FinancialCode"]),
                FinancialApproveDate = rd["FinancialApproveDate"] == DBNull.Value ? null : Convert.ToDateTime(rd["FinancialApproveDate"]),
                BODCode = Convert.ToString(rd["BODCode"]),
                BODApproveDate = rd["BODApproveDate"] == DBNull.Value ? null : Convert.ToDateTime(rd["BODApproveDate"]),
                IsApproved = rd["IsApproved"] != DBNull.Value && Convert.ToBoolean(rd["IsApproved"])
            };
        }

        // Kh�i ph?c supplier v? tr?ng th�i tru?c khi v?a thao t�c.
        private bool RestoreSupplierFromUndoState(ApproveSupplierUndoState undoState)
        {
            var snapshot = undoState.Snapshot;
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand(@"
                UPDATE dbo.PC_Suppliers
                SET
                    SupplierCode = @SupplierCode,
                    SupplierName = @SupplierName,
                    Address = @Address,
                    Phone = @Phone,
                    Mobile = @Mobile,
                    Fax = @Fax,
                    Contact = @Contact,
                    [Position] = @Position,
                    Business = @Business,
                    ApprovedDate = @ApprovedDate,
                    [Document] = @Document,
                    Certificate = @Certificate,
                    Service = @Service,
                    Comment = @Comment,
                    IsNew = @IsNew,
                    CodeOfAcc = @CodeOfAcc,
                    DeptID = @DeptID,
                    [Status] = @Status,
                    PurchaserCode = @PurchaserCode,
                    PurchaserPreparedDate = @PurchaserPreparedDate,
                    DepartmentCode = @DepartmentCode,
                    DepartmentApproveDate = @DepartmentApproveDate,
                    FinancialCode = @FinancialCode,
                    FinancialApproveDate = @FinancialApproveDate,
                    BODCode = @BODCode,
                    BODApproveDate = @BODApproveDate,
                    IsApproved = @IsApproved
                WHERE SupplierID = @SupplierID
                  AND ISNULL([Status], 0) = @AppliedStatus", conn);

            cmd.Parameters.Add("@SupplierID", SqlDbType.Int).Value = snapshot.SupplierId;
            cmd.Parameters.Add("@AppliedStatus", SqlDbType.Int).Value = undoState.AppliedStatus;
            cmd.Parameters.Add("@SupplierCode", SqlDbType.NVarChar, 255).Value = (object?)snapshot.SupplierCode ?? DBNull.Value;
            cmd.Parameters.Add("@SupplierName", SqlDbType.NVarChar, 255).Value = (object?)snapshot.SupplierName ?? DBNull.Value;
            cmd.Parameters.Add("@Address", SqlDbType.NVarChar, 255).Value = (object?)snapshot.Address ?? DBNull.Value;
            cmd.Parameters.Add("@Phone", SqlDbType.NVarChar, 50).Value = (object?)snapshot.Phone ?? DBNull.Value;
            cmd.Parameters.Add("@Mobile", SqlDbType.NVarChar, 50).Value = (object?)snapshot.Mobile ?? DBNull.Value;
            cmd.Parameters.Add("@Fax", SqlDbType.NVarChar, 50).Value = (object?)snapshot.Fax ?? DBNull.Value;
            cmd.Parameters.Add("@Contact", SqlDbType.NVarChar, 255).Value = (object?)snapshot.Contact ?? DBNull.Value;
            cmd.Parameters.Add("@Position", SqlDbType.NVarChar, 255).Value = (object?)snapshot.Position ?? DBNull.Value;
            cmd.Parameters.Add("@Business", SqlDbType.NVarChar, 1000).Value = (object?)snapshot.Business ?? DBNull.Value;
            cmd.Parameters.Add("@ApprovedDate", SqlDbType.DateTime).Value = snapshot.ApprovedDate.HasValue ? snapshot.ApprovedDate.Value : DBNull.Value;
            cmd.Parameters.Add("@Document", SqlDbType.Bit).Value = snapshot.Document;
            cmd.Parameters.Add("@Certificate", SqlDbType.NVarChar, 255).Value = (object?)snapshot.Certificate ?? DBNull.Value;
            cmd.Parameters.Add("@Service", SqlDbType.NVarChar, 1000).Value = (object?)snapshot.Service ?? DBNull.Value;
            cmd.Parameters.Add("@Comment", SqlDbType.NVarChar, 1000).Value = (object?)snapshot.Comment ?? DBNull.Value;
            cmd.Parameters.Add("@IsNew", SqlDbType.Bit).Value = snapshot.IsNew;
            cmd.Parameters.Add("@CodeOfAcc", SqlDbType.NVarChar, 50).Value = (object?)snapshot.CodeOfAcc ?? DBNull.Value;
            cmd.Parameters.Add("@DeptID", SqlDbType.Int).Value = snapshot.DeptID.HasValue ? snapshot.DeptID.Value : DBNull.Value;
            cmd.Parameters.Add("@Status", SqlDbType.Int).Value = snapshot.Status.HasValue ? snapshot.Status.Value : DBNull.Value;
            cmd.Parameters.Add("@PurchaserCode", SqlDbType.NVarChar, 50).Value = (object?)snapshot.PurchaserCode ?? DBNull.Value;
            cmd.Parameters.Add("@PurchaserPreparedDate", SqlDbType.DateTime).Value = snapshot.PurchaserPreparedDate.HasValue ? snapshot.PurchaserPreparedDate.Value : DBNull.Value;
            cmd.Parameters.Add("@DepartmentCode", SqlDbType.NVarChar, 50).Value = (object?)snapshot.DepartmentCode ?? DBNull.Value;
            cmd.Parameters.Add("@DepartmentApproveDate", SqlDbType.DateTime).Value = snapshot.DepartmentApproveDate.HasValue ? snapshot.DepartmentApproveDate.Value : DBNull.Value;
            cmd.Parameters.Add("@FinancialCode", SqlDbType.NVarChar, 50).Value = (object?)snapshot.FinancialCode ?? DBNull.Value;
            cmd.Parameters.Add("@FinancialApproveDate", SqlDbType.DateTime).Value = snapshot.FinancialApproveDate.HasValue ? snapshot.FinancialApproveDate.Value : DBNull.Value;
            cmd.Parameters.Add("@BODCode", SqlDbType.NVarChar, 50).Value = (object?)snapshot.BODCode ?? DBNull.Value;
            cmd.Parameters.Add("@BODApproveDate", SqlDbType.DateTime).Value = snapshot.BODApproveDate.HasValue ? snapshot.BODApproveDate.Value : DBNull.Value;
            cmd.Parameters.Add("@IsApproved", SqlDbType.Bit).Value = snapshot.IsApproved;

            conn.Open();
            return cmd.ExecuteNonQuery() > 0;
        }

        // G?p c�c supplier d� x? l� trong phi�n v�o danh s�ch dang hi?n th?.
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

        // X�c d?nh v? tr� m?c d?nh c?n focus khi m? m�n h�nh duy?t.
        private int GetDefaultCurrentSupplierIndex()
        {
            var unprocessedIndex = Rows.FindIndex(x => !IsProcessedSupplierInCurrentLogin(x.SupplierID));
            return unprocessedIndex >= 0 ? unprocessedIndex : 0;
        }

        // L?y l?i d�ng supplier d� x? l� d? hi?n th? trong c�ng phi�n dang nh?p.
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

        // Ki?m tra supplier d� du?c x? l� trong phi�n dang nh?p hi?n t?i hay chua.
        private bool IsProcessedSupplierInCurrentLogin(int supplierId)
        {
            return GetProcessedSupplierIdsInCurrentLogin().Contains(supplierId);
        }

        // X�c d?nh supplier chua x? l� ti?p theo sau khi v?a duy?t xong.
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

        // ��nh d?u supplier d� x? l� trong phi�n dang nh?p hi?n t?i.
        private void MarkSupplierProcessedInCurrentLogin(int supplierId)
        {
            var processedIds = GetProcessedSupplierIdsInCurrentLogin();
            if (!processedIds.Add(supplierId))
            {
                return;
            }

            HttpContext.Session.SetString(GetProcessedSupplierSessionKey(), string.Join(",", processedIds.OrderBy(x => x)));
        }

        private void MarkSuppliersProcessedInCurrentLogin(IEnumerable<int> supplierIds)
        {
            var processedIds = GetProcessedSupplierIdsInCurrentLogin();
            var hasChanges = false;

            foreach (var supplierId in supplierIds)
            {
                if (processedIds.Add(supplierId))
                {
                    hasChanges = true;
                }
            }

            if (!hasChanges)
            {
                return;
            }

            HttpContext.Session.SetString(GetProcessedSupplierSessionKey(), string.Join(",", processedIds.OrderBy(x => x)));
        }

        // B? d�nh d?u supplier d� x? l� trong phi�n hi?n t?i.
        private void UnmarkSupplierProcessedInCurrentLogin(int supplierId)
        {
            var processedIds = GetProcessedSupplierIdsInCurrentLogin();
            if (!processedIds.Remove(supplierId))
            {
                return;
            }

            if (processedIds.Count == 0)
            {
                HttpContext.Session.Remove(GetProcessedSupplierSessionKey());
                return;
            }

            HttpContext.Session.SetString(GetProcessedSupplierSessionKey(), string.Join(",", processedIds.OrderBy(x => x)));
        }

        // L?y t?p supplier d� x? l� trong phi�n dang nh?p hi?n t?i.
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

        // T?o kh�a session ri�ng cho danh s�ch supplier d� x? l�.
        private string GetProcessedSupplierSessionKey()
        {
            var employeeCode = User.Identity?.Name?.Trim() ?? "ANONYMOUS";
            var authCookie = Request.Cookies[".AspNetCore.Cookies"] ?? string.Empty;
            var authCookieHash = ComputeSha256(authCookie);
            return $"{ProcessedSupplierSessionKeyPrefix}:{employeeCode}:{authCookieHash}";
        }

        // Luu tr?ng th�i kh�i ph?c c?a supplier trong phi�n hi?n t?i.
        private void SetSupplierUndoStateInCurrentLogin(ApproveSupplierUndoState state)
        {
            var undoStates = GetAllSupplierUndoStatesInCurrentLogin();
            undoStates[state.SupplierId] = state;
            HttpContext.Session.SetString(GetUndoSupplierSessionKey(), JsonSerializer.Serialize(undoStates));
        }

        // L?y tr?ng th�i kh�i ph?c c?a supplier trong phi�n hi?n t?i.
        private ApproveSupplierUndoState? GetSupplierUndoStateInCurrentLogin(int supplierId)
        {
            var undoStates = GetAllSupplierUndoStatesInCurrentLogin();
            return undoStates.TryGetValue(supplierId, out var state) ? state : null;
        }

        // X�a tr?ng th�i kh�i ph?c c?a supplier trong phi�n hi?n t?i.
        private void RemoveSupplierUndoStateInCurrentLogin(int supplierId)
        {
            var undoStates = GetAllSupplierUndoStatesInCurrentLogin();
            if (!undoStates.Remove(supplierId))
            {
                return;
            }

            if (undoStates.Count == 0)
            {
                HttpContext.Session.Remove(GetUndoSupplierSessionKey());
                return;
            }

            HttpContext.Session.SetString(GetUndoSupplierSessionKey(), JsonSerializer.Serialize(undoStates));
        }

        // X�a to�n b? tr?ng th�i kh�i ph?c trong phi�n hi?n t?i.
        private void ClearAllSupplierUndoStatesInCurrentLogin()
        {
            HttpContext.Session.Remove(GetUndoSupplierSessionKey());
        }

        private Dictionary<int, ApproveSupplierUndoState> GetAllSupplierUndoStatesInCurrentLogin()
        {
            var rawValue = HttpContext.Session.GetString(GetUndoSupplierSessionKey());
            if (string.IsNullOrWhiteSpace(rawValue))
            {
                return new Dictionary<int, ApproveSupplierUndoState>();
            }

            try
            {
                return JsonSerializer.Deserialize<Dictionary<int, ApproveSupplierUndoState>>(rawValue)
                    ?? new Dictionary<int, ApproveSupplierUndoState>();
            }
            catch
            {
                return new Dictionary<int, ApproveSupplierUndoState>();
            }
        }

        private string GetUndoSupplierSessionKey()
        {
            var employeeCode = User.Identity?.Name?.Trim() ?? "ANONYMOUS";
            var authCookie = Request.Cookies[".AspNetCore.Cookies"] ?? string.Empty;
            var authCookieHash = ComputeSha256(authCookie);
            return $"{UndoSupplierSessionKeyPrefix}:{employeeCode}:{authCookieHash}";
        }

        private bool IsCurrentApprovalScopeActionLocked()
        {
            return string.Equals(HttpContext.Session.GetString(GetCurrentApprovalScopeActionLockedSessionKey()), "1", StringComparison.Ordinal);
        }

        private void SetCurrentApprovalScopeActionLocked(bool isLocked)
        {
            var key = GetCurrentApprovalScopeActionLockedSessionKey();
            if (isLocked)
            {
                HttpContext.Session.SetString(key, "1");
                LockAllSupplierUndoStatesInCurrentLogin();
            }
            else
            {
                HttpContext.Session.Remove(key);
            }
        }

        private bool HasLockedSupplierUndoStateInCurrentLogin()
        {
            return GetAllSupplierUndoStatesInCurrentLogin().Values.Any(x => x.IsLocked);
        }

        private void LockAllSupplierUndoStatesInCurrentLogin()
        {
            var undoStates = GetAllSupplierUndoStatesInCurrentLogin();
            if (undoStates.Count == 0)
            {
                return;
            }

            var updated = false;
            foreach (var undoState in undoStates.Values)
            {
                if (undoState.IsLocked)
                {
                    continue;
                }

                undoState.IsLocked = true;
                updated = true;
            }

            if (!updated)
            {
                return;
            }

            HttpContext.Session.SetString(GetUndoSupplierSessionKey(), JsonSerializer.Serialize(undoStates));
        }

        private string GetCurrentApprovalScopeActionLockedSessionKey()
        {
            var employeeCode = User.Identity?.Name?.Trim() ?? "ANONYMOUS";
            var authCookie = Request.Cookies[".AspNetCore.Cookies"] ?? string.Empty;
            var authCookieHash = ComputeSha256(authCookie);
            var mode = IsSupplierLinkMode ? "LINK" : "LIST";
            var approvalType = IsApproveSupplierNewMode ? "NEW" : "NORMAL";
            var level = _dataScope.LevelCheckSupplier?.ToString() ?? "NOLEVEL";
            var deptScope = ResolveDepartmentFilter()?.ToString() ?? "ALL";
            var supplierScope = IsSupplierLinkMode ? (SupplierId?.ToString() ?? "NOSUPPLIER") : "ALLSUPPLIERS";
            return $"{ScopeActionLockedSessionKeyPrefix}:{employeeCode}:{authCookieHash}:{mode}:{approvalType}:{level}:{deptScope}:{supplierScope}";
        }

        // T?o m� bam d? t�ch d? li?u phi�n l�m vi?c theo dang nh?p hi?n t?i.
        private static string ComputeSha256(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "empty";
            }

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes);
        }

        // Sao ch�p d? li?u supplier sang model ch?nh s?a tr�n giao di?n.
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

        // Ki?m tra d? li?u supplier tru?c khi luu theo qu t?c c?a m�n h�nh chi ti?t.
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

        // Chu?n h�a chu?i nh?p li?u tru?c khi so s�nh v� luu d? li?u.
        private static string? NormalizeText(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    public enum ApproveSupplierActionType
    {
        Approve = 1,
        Disapprove = 2
    }

    public class ApproveSupplierUndoState
    {
        public ApproveSupplierActionType ActionType { get; set; }
        public int SupplierId { get; set; }
        public int AppliedStatus { get; set; }
        public bool IsLocked { get; set; }
        public ApproveSupplierUndoSnapshot Snapshot { get; set; } = new ApproveSupplierUndoSnapshot();
    }

    public class ApproveSupplierUndoSnapshot
    {
        public int SupplierId { get; set; }
        public string? SupplierCode { get; set; }
        public string? SupplierName { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? Mobile { get; set; }
        public string? Fax { get; set; }
        public string? Contact { get; set; }
        public string? Position { get; set; }
        public string? Business { get; set; }
        public DateTime? ApprovedDate { get; set; }
        public bool Document { get; set; }
        public string? Certificate { get; set; }
        public string? Service { get; set; }
        public string? Comment { get; set; }
        public bool IsNew { get; set; }
        public string? CodeOfAcc { get; set; }
        public int? DeptID { get; set; }
        public int? Status { get; set; }
        public string? PurchaserCode { get; set; }
        public DateTime? PurchaserPreparedDate { get; set; }
        public string? DepartmentCode { get; set; }
        public DateTime? DepartmentApproveDate { get; set; }
        public string? FinancialCode { get; set; }
        public DateTime? FinancialApproveDate { get; set; }
        public string? BODCode { get; set; }
        public DateTime? BODApproveDate { get; set; }
        public bool IsApproved { get; set; }
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
        public string PurchaserCode { get; set; } = string.Empty;
        public DateTime? PurchaserPreparedDate { get; set; }
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
        public string PurchaserCode { get; set; } = string.Empty;
        public DateTime? PurchaserPreparedDate { get; set; }
    }

    public class ApproveEmployeeDataScopeViewModel
    {
        public int? DeptID { get; set; }
        public int? LevelCheckSupplier { get; set; }
        public bool HeadDept { get; set; }
        public bool IsCFO { get; set; }
        public bool IsBOD { get; set; }
        public bool IsPurchaser { get; set; }
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










