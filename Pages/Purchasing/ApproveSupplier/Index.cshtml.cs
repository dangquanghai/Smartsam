using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;
using System.Net;
using System.Net.Mail;
using SmartSam.Pages.Purchasing.Supplier;
using SmartSam.Services;

namespace SmartSam.Pages.Purchasing.ApproveSupplier;

public class IndexModel : PageModel
{
    private readonly IConfiguration _configuration;
    private readonly ApproveSupplierService _approveService;
    private readonly PermissionService _permissionService;
    private const int NoDepartmentScopeValue = -1;
    private const int SupplierFunctionId = 71;
    private const int PermissionViewList = 1;
    private const int PermissionApprove = 4;
    
    private ApproveEmployeeDataScopeDto _dataScope = new();
    private bool _isAdminRole;

    public IndexModel(IConfiguration configuration, PermissionService permissionService)
    {
        _configuration = configuration;
        _approveService = new ApproveSupplierService(configuration);
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

    public PagePermissions PagePerm { get; private set; } = new();

    public List<SelectListItem> Departments { get; set; } = [];
    
    public List<ApproveListRowDto> Rows { get; set; } = [];

    public int TotalRecords { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)PageSize));
    public bool HasPreviousPage => PageIndex > 1;
    public bool HasNextPage => PageIndex < TotalPages;
    public bool CanApprove => HasPermission(PermissionApprove);
    public bool IsDepartmentFilterLocked => !_isAdminRole;
    public ApproveSupplierDetailDto? CurrentSupplierDetail { get; private set; }
    public int CurrentSupplierPosition { get; private set; }
    public int CurrentPageSupplierCount => Rows.Count;
    public int? FirstSupplierId { get; private set; }
    public int? LastSupplierId { get; private set; }
    public int? PrevSupplierId { get; private set; }
    public int? NextSupplierId { get; private set; }
    public bool HasFirstSupplier => FirstSupplierId.HasValue && CurrentSupplierPosition > 1;
    public bool HasLastSupplier => LastSupplierId.HasValue && CurrentSupplierPosition < CurrentPageSupplierCount;
    public bool HasPrevSupplier => PrevSupplierId.HasValue;
    public bool HasNextSupplier => NextSupplierId.HasValue;
    
    public ApprovePurchaseOrderInfoDto PurchaseOrderInfo { get; private set; } = new();
    public List<ApproveSupplierServiceRowDto> SupplierServiceRows { get; private set; } = [];

    public int? LevelCheckSupplier => _dataScope.LevelCheckSupplier;
    public bool CanEditAllSupplierFields => _isAdminRole || _dataScope.LevelCheckSupplier == 1;
    public bool CanEditCommentOnly => !_isAdminRole && _dataScope.LevelCheckSupplier.HasValue && _dataScope.LevelCheckSupplier.Value > 1;
    public bool CanEditComment => CanEditAllSupplierFields || CanEditCommentOnly;
    public bool CanApproveByLevel => _dataScope.LevelCheckSupplier.HasValue && _dataScope.LevelCheckSupplier.Value is >= 1 and <= 4;
    public bool CanDisapproveByLevel => _dataScope.LevelCheckSupplier.HasValue && _dataScope.LevelCheckSupplier.Value is >= 2 and <= 4;

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
        await LoadCurrentSupplierDetailAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        await LoadUserDataScopeAsync(cancellationToken);
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

        var current = await _approveService.GetSupplierDetailAsync(supplierId, cancellationToken);
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

        var saveValidationMessage = await ValidateSupplierInputLikeDetailAsync(
            supplierId,
            current,
            EditSupplier,
            CanEditAllSupplierFields,
            CanEditComment,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(saveValidationMessage))
        {
            CurrentSupplierId = supplierId;
            SetFlashMessage(saveValidationMessage, "warning");
            return RedirectToCurrentList();
        }

        await _approveService.UpdateSupplierForApprovalAsync(supplierId, EditSupplier, CanEditAllSupplierFields, cancellationToken);

        CurrentSupplierId = supplierId;
        SetFlashMessage("Updated supplier information successfully.", "success");
        return RedirectToCurrentList();
    }

    public async Task<IActionResult> OnPostGoToAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        await LoadUserDataScopeAsync(cancellationToken);
        if (!HasPermission(PermissionViewList))
        {
            return Forbid();
        }

        await LoadRowsAsync(cancellationToken);
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

    public async Task<IActionResult> OnPostApproveAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        await LoadUserDataScopeAsync(cancellationToken);

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

        var current = await _approveService.GetSupplierDetailAsync(supplierId, cancellationToken);
        if (current is null)
        {
            SetFlashMessage("Supplier not found.", "warning");
            return RedirectToCurrentList();
        }

        if (!CanAccessDepartment(current.DeptID))
        {
            return Forbid();
        }

        var approveValidationMessage = await ValidateSupplierInputLikeDetailAsync(
            supplierId,
            current,
            EditSupplier,
            CanEditAllSupplierFields,
            CanEditComment,
            cancellationToken);
        if (!string.IsNullOrWhiteSpace(approveValidationMessage))
        {
            CurrentSupplierId = supplierId;
            SetFlashMessage(approveValidationMessage, "warning");
            return RedirectToCurrentList();
        }

        await LoadRowsAsync(cancellationToken);
        var nextSupplierId = GetNextSupplierIdFromCurrentRows(supplierId);
        var isLastSupplier = IsLastSupplierInCurrentRows(supplierId);
        var currentLevel = _dataScope.LevelCheckSupplier.Value;

        var operatorCode = User.Identity?.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(operatorCode))
        {
            SetFlashMessage("Cannot identify operator.", "error");
            return RedirectToCurrentList();
        }

        var approved = await _approveService.ApproveByLevelAsync(
            supplierId,
            currentLevel,
            operatorCode,
            EditSupplier,
            CanEditAllSupplierFields,
            CanEditComment,
            cancellationToken);

        if (!approved)
        {
            SetFlashMessage("Cannot approve because supplier status is not in the expected workflow step.", "warning");
            return RedirectToCurrentList();
        }

        var notifyResult = isLastSupplier
            ? await TryNotifyNextLevelAsync(currentLevel, current, "approved", cancellationToken)
            : null;

        CurrentSupplierId = nextSupplierId;
        var approveMessage = "Approved supplier successfully.";
        if (!string.IsNullOrWhiteSpace(notifyResult))
        {
            approveMessage += $" {notifyResult}";
        }

        SetFlashMessage(approveMessage, "success");
        return RedirectToCurrentList();
    }

    public async Task<IActionResult> OnPostDisapproveAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        await LoadUserDataScopeAsync(cancellationToken);

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

        var current = await _approveService.GetSupplierDetailAsync(supplierId, cancellationToken);
        if (current is null)
        {
            SetFlashMessage("Supplier not found.", "warning");
            return RedirectToCurrentList();
        }

        if (!CanAccessDepartment(current.DeptID))
        {
            return Forbid();
        }

        await LoadRowsAsync(cancellationToken);
        var nextSupplierId = GetNextSupplierIdFromCurrentRows(supplierId);
        var isLastSupplier = IsLastSupplierInCurrentRows(supplierId);
        var currentLevel = _dataScope.LevelCheckSupplier.Value;

        var operatorCode = User.Identity?.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(operatorCode))
        {
            SetFlashMessage("Cannot identify operator.", "error");
            return RedirectToCurrentList();
        }

        var disapproved = await _approveService.DisapproveByLevelAsync(
            supplierId,
            currentLevel,
            operatorCode,
            EditSupplier.Comment,
            CanEditComment,
            cancellationToken);

        if (!disapproved)
        {
            SetFlashMessage("Disapprove failed.", "warning");
            return RedirectToCurrentList();
        }

        var notifyResult = isLastSupplier
            ? await TryNotifyNextLevelAsync(currentLevel, current, "disapproved", cancellationToken)
            : null;

        CurrentSupplierId = nextSupplierId;
        var disapproveMessage = "Disapproved supplier successfully.";
        if (!string.IsNullOrWhiteSpace(notifyResult))
        {
            disapproveMessage += $" {notifyResult}";
        }

        SetFlashMessage(disapproveMessage, "success");
        return RedirectToCurrentList();
    }

    public async Task<IActionResult> OnPostSaveMoreCommentAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        await LoadUserDataScopeAsync(cancellationToken);
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

        var current = await _approveService.GetSupplierDetailAsync(supplierId, cancellationToken);
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

        await _approveService.UpdateSupplierCommentAsync(supplierId, EditSupplier.Comment, cancellationToken);
        CurrentSupplierId = supplierId;
        SetFlashMessage("Saved supplier comment successfully.", "success");
        return RedirectToCurrentList();
    }

    private async Task LoadFiltersAsync(CancellationToken cancellationToken)
    {
        var departments = await _approveService.GetDepartmentsAsync(cancellationToken);

        if (_isAdminRole)
        {
            Departments = [
                new SelectListItem { Value = string.Empty, Text = "--- All ---" },
                .. departments.Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.CodeOrName })
            ];
            return;
        }

        var scopedDepartments = departments
            .Where(x => _dataScope.DeptID.HasValue && x.Id == _dataScope.DeptID.Value)
            .ToList();

        Departments = [
            .. scopedDepartments.Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.CodeOrName })
        ];

        DeptId = _dataScope.DeptID ?? NoDepartmentScopeValue;
    }

    private async Task LoadRowsAsync(CancellationToken cancellationToken)
    {
        if (PageSize <= 0) PageSize = 25;
        if (PageSize > 200) PageSize = 200;
        if (PageIndex <= 0) PageIndex = 1;

        var result = await _approveService.SearchPagedAsync(BuildCriteria(includePaging: true), cancellationToken);
        TotalRecords = result.TotalCount;
        if (TotalRecords > 0 && PageIndex > TotalPages)
        {
            PageIndex = TotalPages;
            result = await _approveService.SearchPagedAsync(BuildCriteria(includePaging: true), cancellationToken);
            TotalRecords = result.TotalCount;
        }
        Rows = result.Rows;
    }

    private async Task LoadCurrentSupplierDetailAsync(CancellationToken cancellationToken)
    {
        CurrentSupplierDetail = null;
        PurchaseOrderInfo = new ApprovePurchaseOrderInfoDto();
        SupplierServiceRows = [];
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

        CurrentSupplierDetail = await _approveService.GetSupplierDetailAsync(Rows[selectedIndex].SupplierID, cancellationToken);
        EditSupplier = CurrentSupplierDetail is null
            ? new ApproveSupplierDetailDto()
            : CloneForEdit(CurrentSupplierDetail);

        if (CurrentSupplierDetail is not null)
        {
            PurchaseOrderInfo = await _approveService.GetPurchaseOrderInfoAsync(
                CurrentSupplierDetail.SupplierID,
                DateTime.Now.Year,
                cancellationToken);
            SupplierServiceRows = (await _approveService.GetSupplierServiceRowsAsync(
                CurrentSupplierDetail.SupplierID,
                cancellationToken)).ToList();
        }
    }

    private ApproveFilterCriteria BuildCriteria(bool includePaging = true)
    {
        var restrictedDeptId = ResolveDepartmentFilter();

        return new ApproveFilterCriteria
        {
            DeptId = restrictedDeptId,
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
        if (!_dataScope.LevelCheckSupplier.HasValue || _dataScope.LevelCheckSupplier.Value <= 0)
        {
            return null;
        }

        return _dataScope.LevelCheckSupplier.Value - 1;
    }

    private async Task LoadUserDataScopeAsync(CancellationToken cancellationToken)
    {
        _isAdminRole = string.Equals(User.FindFirst("IsAdminRole")?.Value, "True", StringComparison.OrdinalIgnoreCase);
        var employeeScope = await _approveService.GetEmployeeDataScopeAsync(User.Identity?.Name, cancellationToken);
        if (_isAdminRole)
        {
            employeeScope.CanAsk = true;
            _dataScope = employeeScope;
            return;
        }

        _dataScope = employeeScope;
        if (!_dataScope.CanAsk)
        {
            DeptId = _dataScope.DeptID ?? NoDepartmentScopeValue;
        }
    }

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

    private IActionResult RedirectToCurrentList()
        => RedirectToPage("./Index", new
        {
            DeptId,
            PageIndex,
            PageSize,
            CurrentSupplierId
        });

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

    private async Task<string?> TryNotifyNextLevelAsync(
        int currentLevel,
        ApproveSupplierDetailDto supplier,
        string action,
        CancellationToken cancellationToken)
    {
        if (currentLevel >= 4)
        {
            return null;
        }

        var nextLevel = currentLevel + 1;
        var recipients = (await _approveService.GetEmailsByLevelCheckAsync(nextLevel, cancellationToken)).ToList();
        if (recipients.Count == 0)
        {
            return $"No email recipients found for level {nextLevel}.";
        }

        var senderEmail = _configuration.GetValue<string>("EmailSettings:SenderEmail");
        var mailPass = _configuration.GetValue<string>("EmailSettings:Password");
        var mailServer = _configuration.GetValue<string>("EmailSettings:MailServer");
        var mailPort = _configuration.GetValue<int?>("EmailSettings:MailPort") ?? 0;
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

            await smtp.SendMailAsync(mail, cancellationToken);
            return $"Notification email sent to level {nextLevel}.";
        }
        catch (Exception ex)
        {
            return $"Cannot send notification email to level {nextLevel}: {ex.Message}";
        }
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

    private async Task<string?> ValidateSupplierInputLikeDetailAsync(
        int supplierId,
        ApproveSupplierDetailDto current,
        ApproveSupplierDetailDto posted,
        bool canEditAllFields,
        bool canEditComment,
        CancellationToken cancellationToken)
    {
        var model = new SupplierDetailDto
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
        if (!Validator.TryValidateObject(model, context, results, validateAllProperties: true))
        {
            var firstError = results.FirstOrDefault()?.ErrorMessage;
            return string.IsNullOrWhiteSpace(firstError) ? "Supplier data is invalid." : firstError;
        }

        if (!string.IsNullOrWhiteSpace(model.SupplierCode))
        {
            var exists = await _approveService.SupplierCodeExistsAsync(model.SupplierCode, supplierId, cancellationToken);
            if (exists)
            {
                return "Supplier code already exists.";
            }
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
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
