using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;
using SmartSam.Services.Interfaces;
using System.Data;

namespace SmartSam.Pages.Purchasing.MaterialRequest;

public class MaterialRequestDetailModel : BasePageModel
{
    private readonly MaterialRequestService _materialRequestService;
    private readonly ISecurityService _securityService;
    private readonly PermissionService _permissionService;

    private const int MaterialRequestFunctionId = 104;
    private const int PermissionCreate = 1;
    private const int PermissionEdit = 3;
    private const int PermissionShowAll = 4;
    private const int PermissionShowCreate = 7;
    private const int StatusJustCreated = 0;
    private const int StatusSubmittedByOwner = 1;
    private const int StatusHeadDeptApproved = 2;
    private const int StatusPurchaserChecked = 3;
    private const int StatusCompleted = 4;
    private const int StatusRejected = 5;

    private EmployeeMaterialScopeDto _dataScope = new EmployeeMaterialScopeDto();
    private bool _isAdminRole;

    // Khởi tạo model detail và nạp các service cần dùng.
    public MaterialRequestDetailModel(ISecurityService securityService, IConfiguration configuration, PermissionService permissionService) : base(configuration)
    {
        _materialRequestService = new MaterialRequestService(configuration);
        _securityService = securityService;
        _permissionService = permissionService;
    }

    [BindProperty(SupportsGet = true)]
    public long? Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    public MaterialRequestDetailDto Input { get; set; } = new MaterialRequestDetailDto();

    [BindProperty]
    public string? LinesJson { get; set; }

    [BindProperty]
    public List<MaterialRequestLineDto> Lines { get; set; } = new List<MaterialRequestLineDto>();

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? WarningMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    [BindProperty]
    public PagePermissions PagePerm { get; set; } = new PagePermissions();
    public List<SelectListItem> StoreGroups { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> Statuses { get; set; } = new List<SelectListItem>();

    public bool IsEdit
    {
        get { return Id.HasValue && Id.Value > 0; }
    }

    public bool CanShowAll
    {
        get { return _isAdminRole || HasPermission(PermissionShowAll) || _dataScope.SeeDataAllDept; }
    }

    public bool CanSave
    {
        get
        {
            if (IsEdit)
            {
                return HasPermission(PermissionEdit) && CurrentStatusId == StatusJustCreated;
            }

            return HasPermission(PermissionCreate) || HasPermission(PermissionShowCreate);
        }
    }

    public bool StoreGroupLocked
    {
        get { return !CanShowAll; }
    }

    public int CurrentStatusId
    {
        get { return Input.MaterialStatusId ?? StatusJustCreated; }
    }

    public bool CanSubmit
    {
        get { return IsEdit && HasPermission(PermissionEdit) && CurrentStatusId == StatusJustCreated; }
    }

    public bool CanApprove
    {
        get { return IsEdit && CanApproveStatus(CurrentStatusId); }
    }

    public bool CanReject
    {
        get { return IsEdit && CanRejectStatus(CurrentStatusId); }
    }

    public string BackToListUrl
    {
        get { return ResolveBackToListUrl(); }
    }

    // Tải trang detail: kiểm tra quyền, nạp dropdown, rồi nạp dữ liệu nếu đang edit.
    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");
        PagePerm = new PagePermissions();
        PagePerm.AllowedNos = _securityService.GetEffectivePermissions(MaterialRequestFunctionId, roleId, 0);
        await LoadUserScopeAsync(cancellationToken);
        if (!CanAccessPage())
        {
            return Forbid();
        }

        LoadDropdowns();
        ApplyTempDataMessagesToModelState();

        if (!IsEdit)
        {
            if (StoreGroupLocked)
            {
                Input.StoreGroup = _dataScope.StoreGroup;
            }

            Input.DateCreate = DateTime.Today;
            Input.FromDate = DateTime.Today.AddMonths(-3);
            Input.ToDate = DateTime.Today;
            Input.MaterialStatusId ??= 0;
            return Page();
        }

        var detail = await _materialRequestService.GetDetailAsync(Id!.Value, cancellationToken);
        if (detail is null)
        {
            ErrorMessage = "Material Request not found.";
            return RedirectToPage("./Index");
        }

        if (StoreGroupLocked && _dataScope.StoreGroup.HasValue && detail.StoreGroup != _dataScope.StoreGroup)
        {
            return Forbid();
        }

        Input = detail;
        Lines = (await _materialRequestService.GetLinesAsync(Id.Value, cancellationToken)).ToList();
        PagePerm.AllowedNos = _securityService.GetEffectivePermissions(MaterialRequestFunctionId, roleId, CurrentStatusId);
        return Page();
    }

    // Lưu form detail (tạo mới/cập nhật) sau khi validate dữ liệu.
    public async Task<IActionResult> OnPostSave(CancellationToken cancellationToken)
    {
        int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");
        PagePerm = new PagePermissions();
        PagePerm.AllowedNos = _securityService.GetEffectivePermissions(MaterialRequestFunctionId, roleId, 0);
        await LoadUserScopeAsync(cancellationToken);
        if (!CanAccessPage())
        {
            return Forbid();
        }

        LoadDropdowns();

        if (StoreGroupLocked)
        {
            Input.StoreGroup = _dataScope.StoreGroup;
        }

        MaterialRequestDetailDto? existing = null;
        if (IsEdit)
        {
            existing = await _materialRequestService.GetDetailAsync(Id!.Value, cancellationToken);
            if (existing is null)
            {
                ErrorMessage = "Material Request not found.";
                return RedirectToPage("./Index");
            }

            // Giữ nguyên trạng thái workflow khi người dùng chỉ save thông tin trên form.
            var currentStatus = existing.MaterialStatusId ?? StatusJustCreated;
            PagePerm.AllowedNos = _securityService.GetEffectivePermissions(MaterialRequestFunctionId, roleId, currentStatus);
            if (!HasPermission(PermissionEdit) || currentStatus != StatusJustCreated)
            {
                WarningMessage = "You cannot edit this Material Request at current status.";
                return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(Id));
            }

            Input.MaterialStatusId = existing.MaterialStatusId;
            Input.Approval = existing.Approval;
            Input.ApprovalEnd = existing.ApprovalEnd;
            Input.PostPr = existing.PostPr;
            Input.FromDate ??= existing.FromDate;
            Input.ToDate ??= existing.ToDate;
        }
        else if (!(HasPermission(PermissionCreate) || HasPermission(PermissionShowCreate)))
        {
            return Forbid();
        }

        var lines = ResolvePostedLines();
        await ApplyReadonlyLineRulesAsync(lines, Id, cancellationToken);

        if (!ModelState.IsValid)
        {
            Lines = lines;
            return Page();
        }

        try
        {
            var savedNo = await _materialRequestService.SaveAsync(IsEdit ? Id : null, Input, lines, cancellationToken);
            SuccessMessage = IsEdit ? "Saved successfully." : "Material Request created successfully.";
            return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(savedNo));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MaterialRequest][Save] {ex}");
            Lines = lines;
            ModelState.AddModelError(string.Empty, ex is InvalidOperationException ? ex.Message : $"Cannot save Material Request. {ex.Message}");
            return Page();
        }
    }

    // Handler POST mặc định: form không chỉ định handler thì đi vào Save.

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        return await OnPostSave(cancellationToken);
    }

    // Handler Submit: chuyển request sang bước workflow kế tiếp.
    public async Task<IActionResult> OnPostSubmit(CancellationToken cancellationToken)
    {
        return await HandleWorkflowActionAsync(MaterialRequestWorkflowAction.Submit, cancellationToken);
    }

    // Handler Approve: duyệt request theo trạng thái hiện tại.
    public async Task<IActionResult> OnPostApprove(CancellationToken cancellationToken)
    {
        return await HandleWorkflowActionAsync(MaterialRequestWorkflowAction.Approve, cancellationToken);
    }

    // Handler Reject: từ chối request theo trạng thái hiện tại.
    public async Task<IActionResult> OnPostReject(CancellationToken cancellationToken)
    {
        return await HandleWorkflowActionAsync(MaterialRequestWorkflowAction.Reject, cancellationToken);
    }

    // Xử lý chung cho các thao tác workflow: Submit/Approve/Reject.
    private async Task<IActionResult> HandleWorkflowActionAsync(MaterialRequestWorkflowAction action, CancellationToken cancellationToken)
    {
        int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");
        PagePerm = new PagePermissions();
        PagePerm.AllowedNos = _securityService.GetEffectivePermissions(MaterialRequestFunctionId, roleId, 0);
        await LoadUserScopeAsync(cancellationToken);
        if (!CanAccessPage())
        {
            return Forbid();
        }

        if (!IsEdit)
        {
            WarningMessage = "Please save Material Request first.";
            return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(null));
        }

        LoadDropdowns();

        if (StoreGroupLocked)
        {
            Input.StoreGroup = _dataScope.StoreGroup;
        }

        var existing = await _materialRequestService.GetDetailAsync(Id!.Value, cancellationToken);
        if (existing is null)
        {
            ErrorMessage = "Material Request not found.";
            return RedirectToPage("./Index");
        }

        var currentStatus = existing.MaterialStatusId ?? StatusJustCreated;
        PagePerm.AllowedNos = _securityService.GetEffectivePermissions(MaterialRequestFunctionId, roleId, currentStatus);
        var transition = ResolveTransition(action, currentStatus);
        if (transition is null)
        {
            WarningMessage = "Invalid workflow action for current status.";
            return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(Id));
        }

        if (!CanExecuteTransition(action, currentStatus))
        {
            WarningMessage = "You do not have permission for this workflow action.";
            return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(Id));
        }

        List<MaterialRequestLineDto> lines;
        MaterialRequestDetailDto toSave;

        if (action == MaterialRequestWorkflowAction.Submit)
        {
            lines = ResolvePostedLines();
            await ApplyReadonlyLineRulesAsync(lines, Id, cancellationToken);
            toSave = MergeEditableInput(existing, Input);
        }
        else
        {
            lines = (await _materialRequestService.GetLinesAsync(Id!.Value, cancellationToken)).ToList();
            toSave = CloneHeader(existing);
        }

        try
        {
            toSave.MaterialStatusId = transition.NextStatusId;
            if (transition.Approval.HasValue) toSave.Approval = transition.Approval.Value;
            if (transition.ApprovalEnd.HasValue) toSave.ApprovalEnd = transition.ApprovalEnd.Value;
            if (transition.PostPr.HasValue) toSave.PostPr = transition.PostPr.Value;

            var savedNo = await _materialRequestService.SaveAsync(Id, toSave, lines, cancellationToken);
            SuccessMessage = transition.SuccessMessage;
            return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(savedNo));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MaterialRequest][WorkflowAction:{action}] {ex}");
            ErrorMessage = ex is InvalidOperationException
                ? ex.Message
                : $"Cannot process workflow action. {ex.Message}";
            return RedirectToPage("./MaterialRequestDetail", BuildDetailRoute(Id));
        }
    }

    // API lookup item cho popup Add Detail.
    public async Task<IActionResult> OnGetSearchItems(string? keyword, bool checkBalanceInStore = false, CancellationToken cancellationToken = default)
    {
        int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");
        PagePerm = new PagePermissions();
        PagePerm.AllowedNos = _securityService.GetEffectivePermissions(MaterialRequestFunctionId, roleId, 0);
        await LoadUserScopeAsync(cancellationToken);
        if (!CanAccessPage())
        {
            return new JsonResult(new { success = false, message = "Access denied." });
        }

        var rows = await _materialRequestService.SearchItemsAsync(keyword, checkBalanceInStore, Input.StoreGroup, cancellationToken);
        return new JsonResult(new { success = true, data = rows });
    }

    // API tạo nhanh item mới khi item chưa có trong danh mục.
    public async Task<IActionResult> OnPostCreateItem([FromForm] string? itemName, [FromForm] string? unit, CancellationToken cancellationToken)
    {
        int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");
        PagePerm = new PagePermissions();
        PagePerm.AllowedNos = _securityService.GetEffectivePermissions(MaterialRequestFunctionId, roleId, 0);
        await LoadUserScopeAsync(cancellationToken);
        if (!CanAccessPage())
        {
            return new JsonResult(new { success = false, message = "Access denied." });
        }

        if (IsEdit)
        {
            var existing = await _materialRequestService.GetDetailAsync(Id!.Value, cancellationToken);
            if (existing is null)
            {
                return new JsonResult(new { success = false, message = "Material Request not found." });
            }

            var currentStatus = existing.MaterialStatusId ?? StatusJustCreated;
            if (!HasPermission(PermissionEdit) || currentStatus != StatusJustCreated)
            {
                return new JsonResult(new { success = false, message = "You cannot add item at current status." });
            }
        }
        else if (!(HasPermission(PermissionCreate) || HasPermission(PermissionShowCreate)))
        {
            return new JsonResult(new { success = false, message = "Access denied." });
        }

        try
        {
            var created = await _materialRequestService.CreateQuickItemAsync(itemName ?? string.Empty, unit, cancellationToken);
            return new JsonResult(new { success = true, data = created });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, message = ex.Message });
        }
    }

    // Nạp dropdown Store Group và Status cho form detail.
    private void LoadDropdowns()
    {
        var stores = LoadListFromSql(
            @"SELECT KPGroupID, KPGroupName
              FROM dbo.INV_KPGroup
              ORDER BY KPGroupID",
            "KPGroupID",
            "KPGroupName");

        if (stores.Count == 0)
        {
            stores = LoadListFromSql(
                @"SELECT DISTINCT
                        CAST(StoreGR AS int) AS StoreGroup,
                        CONCAT('Store Group ', CAST(StoreGR AS varchar(20))) AS StoreGroupName
                  FROM dbo.MS_Employee
                  WHERE StoreGR IS NOT NULL AND StoreGR > 0
                  ORDER BY StoreGroup",
                "StoreGroup",
                "StoreGroupName");
        }

        StoreGroups = stores;

        var statuses = LoadListFromSql(
            @"SELECT MaterialStatusID, MaterialStatusName
              FROM dbo.MaterialStatus
              ORDER BY MaterialStatusID",
            "MaterialStatusID",
            "MaterialStatusName");

        Statuses = statuses;

        if (StoreGroupLocked)
        {
            if (_dataScope.StoreGroup.HasValue)
            {
                StoreGroups = new List<SelectListItem>
                {
                    new SelectListItem
                    {
                        Value = _dataScope.StoreGroup.Value.ToString(),
                        Text = $"Store Group {_dataScope.StoreGroup.Value}"
                    }
                };
            }
            else
            {
                StoreGroups = new List<SelectListItem>
                {
                    new SelectListItem
                    {
                        Value = string.Empty,
                        Text = "(No Store Group Scope)"
                    }
                };
            }
        }
    }

    // Parse danh sách line từ JSON do JS gửi lên.
    private static List<MaterialRequestLineDto> ParseLinesFromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<MaterialRequestLineDto>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<MaterialRequestLineDto>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (parsed == null)
            {
                return new List<MaterialRequestLineDto>();
            }

            return parsed;
        }
        catch
        {
            return new List<MaterialRequestLineDto>();
        }
    }

    // Lấy line đã post: ưu tiên JSON, thiếu thì fallback sang model binding.
    private List<MaterialRequestLineDto> ResolvePostedLines()
    {
        var linesFromJson = ParseLinesFromJson(LinesJson);
        if (linesFromJson.Count > 0)
        {
            return NormalizeLines(linesFromJson);
        }

        return NormalizeLines(Lines);
    }

    // Ép các cột chỉ đọc ở backend để tránh sửa dữ liệu trái nghiệp vụ.
    private async Task ApplyReadonlyLineRulesAsync(List<MaterialRequestLineDto> lines, long? requestNo, CancellationToken cancellationToken)
    {
        if (lines.Count == 0)
        {
            return;
        }

        var itemCodes = lines
            .Select(x => (x.ItemCode ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var itemUnits = await _materialRequestService.GetItemUnitsAsync(itemCodes, cancellationToken);
        var existingSnapshots = requestNo.HasValue
            ? await _materialRequestService.GetLineReadonlySnapshotsAsync(requestNo.Value, cancellationToken)
            : new Dictionary<string, MaterialRequestLineReadonlySnapshotDto>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in lines)
        {
            var itemCode = (line.ItemCode ?? string.Empty).Trim();
            line.ItemCode = itemCode;
            line.ItemName = (line.ItemName ?? string.Empty).Trim();
            line.Note = (line.Note ?? string.Empty).Trim();
            line.Selected = true;

            if (string.IsNullOrWhiteSpace(itemCode))
            {
                continue;
            }

            if (existingSnapshots.TryGetValue(itemCode, out var snapshot))
            {
                line.NotReceipt = snapshot.NotReceipt;
                line.InStock = snapshot.InStock;
                line.AccIn = snapshot.AccIn;
                line.Buy = snapshot.Buy;
                line.Unit = snapshot.Unit;
            }
            else
            {
                line.NotReceipt = 0m;
                line.InStock = 0m;
                line.AccIn = 0m;
                line.Buy = 0m;
            }

            if (itemUnits.TryGetValue(itemCode, out var unitFromMaster) && !string.IsNullOrWhiteSpace(unitFromMaster))
            {
                line.Unit = unitFromMaster;
            }

            line.Unit = (line.Unit ?? string.Empty).Trim();
        }
    }

    // Chuẩn hóa line và loại bỏ các dòng trống trước khi lưu.
    private static List<MaterialRequestLineDto> NormalizeLines(IEnumerable<MaterialRequestLineDto> lines)
    {
        var result = new List<MaterialRequestLineDto>();
        foreach (var line in lines)
        {
            var itemCode = (line.ItemCode ?? string.Empty).Trim();
            var itemName = (line.ItemName ?? string.Empty).Trim();
            var unit = (line.Unit ?? string.Empty).Trim();
            var note = (line.Note ?? string.Empty).Trim();

            var isBlank = string.IsNullOrWhiteSpace(itemCode)
                && string.IsNullOrWhiteSpace(itemName)
                && string.IsNullOrWhiteSpace(unit)
                && string.IsNullOrWhiteSpace(note)
                && !(line.OrderQty.HasValue && line.OrderQty.Value != 0)
                && !(line.NotReceipt.HasValue && line.NotReceipt.Value != 0)
                && !(line.InStock.HasValue && line.InStock.Value != 0)
                && !(line.AccIn.HasValue && line.AccIn.Value != 0)
                && !(line.Buy.HasValue && line.Buy.Value != 0)
                && !(line.Price.HasValue && line.Price.Value != 0);

            if (isBlank)
            {
                continue;
            }

            result.Add(new MaterialRequestLineDto
            {
                Id = line.Id,
                ItemCode = itemCode,
                ItemName = itemName,
                Unit = unit,
                OrderQty = line.OrderQty ?? 0,
                NotReceipt = line.NotReceipt ?? 0,
                InStock = line.InStock ?? 0,
                AccIn = line.AccIn ?? 0,
                Buy = line.Buy ?? 0,
                Price = line.Price ?? 0,
                Note = note,
                NewItem = line.NewItem,
                Selected = true
            });
        }

        return result;
    }

    // Nạp data scope người dùng từ claim + bảng MS_Employee.
    private async Task LoadUserScopeAsync(CancellationToken cancellationToken)
    {
        _isAdminRole = IsAdminUser();
        if (_isAdminRole)
        {
            _dataScope = new EmployeeMaterialScopeDto();
            return;
        }

        _dataScope = await _materialRequestService.GetEmployeeScopeAsync(User.Identity?.Name, cancellationToken);
    }

    // Nạp permission number của user cho function Material Request.
    private PagePermissions GetUserPermissions()
    {
        bool isAdmin = User.FindFirst("IsAdminRole")?.Value == "True";
        int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");

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
            permsObj.AllowedNos = _permissionService.GetPermissionsForPage(roleId, MaterialRequestFunctionId);
        }

        // 3. Trả về đối tượng (Object) chứa danh sách đó
        return permsObj;
    }

    private bool IsAdminUser()
    {
        var adminClaim = (User.FindFirst("IsAdminRole")?.Value ?? string.Empty).Trim();
        if (string.Equals(adminClaim, "True", StringComparison.OrdinalIgnoreCase)
            || string.Equals(adminClaim, "1", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Fallback: nếu claim chưa đúng, kiểm tra trực tiếp cờ IsAdminRole theo RoleID trong DB.
        if (!int.TryParse(User.FindFirst("RoleID")?.Value, out var roleId) || roleId <= 0)
        {
            return false;
        }

        try
        {
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
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

    // Kiểm tra user có quyền truy cập trang hay không.
    private bool CanAccessPage()
    {
        return _isAdminRole || PagePerm.AllowedNos.Count > 0;
    }

    // Wrapper kiểm tra một permission number cụ thể.
    private bool HasPermission(int permissionNo)
    {
        return PagePerm.HasPermission(permissionNo);
    }

    // Gộp dữ liệu editable từ form vào header hiện tại, giữ nguyên cờ workflow.
    private static MaterialRequestDetailDto MergeEditableInput(MaterialRequestDetailDto existing, MaterialRequestDetailDto posted)
    {
        return new MaterialRequestDetailDto
        {
            RequestNo = existing.RequestNo,
            StoreGroup = posted.StoreGroup ?? existing.StoreGroup,
            DateCreate = posted.DateCreate ?? existing.DateCreate,
            AccordingTo = string.IsNullOrWhiteSpace(posted.AccordingTo) ? existing.AccordingTo : posted.AccordingTo,
            Approval = existing.Approval,
            ApprovalEnd = existing.ApprovalEnd,
            PostPr = existing.PostPr,
            IsAuto = posted.IsAuto,
            FromDate = posted.FromDate ?? existing.FromDate,
            ToDate = posted.ToDate ?? existing.ToDate,
            MaterialStatusId = existing.MaterialStatusId,
            PrNo = string.IsNullOrWhiteSpace(posted.PrNo) ? existing.PrNo : posted.PrNo,
            NoIssue = posted.NoIssue
        };
    }

    // Clone header để xử lý workflow mà không làm bẩn object nguồn.
    private static MaterialRequestDetailDto CloneHeader(MaterialRequestDetailDto source)
    {
        return new MaterialRequestDetailDto
        {
            RequestNo = source.RequestNo,
            StoreGroup = source.StoreGroup,
            DateCreate = source.DateCreate,
            AccordingTo = source.AccordingTo,
            Approval = source.Approval,
            ApprovalEnd = source.ApprovalEnd,
            PostPr = source.PostPr,
            IsAuto = source.IsAuto,
            FromDate = source.FromDate,
            ToDate = source.ToDate,
            MaterialStatusId = source.MaterialStatusId,
            PrNo = source.PrNo,
            NoIssue = source.NoIssue
        };
    }

    // Kiểm tra user có quyền approve ở trạng thái hiện tại không.
    private bool CanApproveStatus(int statusId)
    {
        if (_isAdminRole)
        {
            return true;
        }

        if (statusId == StatusSubmittedByOwner)
        {
            return _dataScope.IsHeadDept || _dataScope.ApprovalLevel >= 2;
        }

        if (statusId == StatusHeadDeptApproved)
        {
            return _dataScope.ApprovalLevel >= 2;
        }

        if (statusId == StatusPurchaserChecked)
        {
            return _dataScope.ApprovalLevel >= 3;
        }

        return false;
    }

    // Kiểm tra user có quyền reject ở trạng thái hiện tại không.
    private bool CanRejectStatus(int statusId)
    {
        if (_isAdminRole)
        {
            return true;
        }
        if (statusId is not (StatusSubmittedByOwner or StatusHeadDeptApproved or StatusPurchaserChecked))
        {
            return false;
        }

        return _dataScope.IsHeadDept || _dataScope.ApprovalLevel >= 2;
    }

    // Kiểm tra action workflow có hợp lệ với quyền + trạng thái hiện tại không.
    private bool CanExecuteTransition(MaterialRequestWorkflowAction action, int currentStatus)
    {
        if (action == MaterialRequestWorkflowAction.Submit)
        {
            return HasPermission(PermissionEdit) && currentStatus == StatusJustCreated;
        }

        if (action == MaterialRequestWorkflowAction.Approve)
        {
            return CanApproveStatus(currentStatus);
        }

        if (action == MaterialRequestWorkflowAction.Reject)
        {
            return CanRejectStatus(currentStatus);
        }

        return false;
    }

    // Xác định bước chuyển trạng thái và thông điệp thành công tương ứng.
    private static MaterialRequestWorkflowTransition? ResolveTransition(MaterialRequestWorkflowAction action, int currentStatus)
    {
        if (action == MaterialRequestWorkflowAction.Submit && currentStatus == StatusJustCreated)
        {
            return new MaterialRequestWorkflowTransition(
                    StatusSubmittedByOwner,
                    "Submitted successfully. Waiting for Head Dept approval.",
                    false,
                    false,
                    false);
        }

        if (action == MaterialRequestWorkflowAction.Approve && currentStatus == StatusSubmittedByOwner)
        {
            return new MaterialRequestWorkflowTransition(
                    StatusHeadDeptApproved,
                    "Approved by Head Dept.",
                    true,
                    null,
                    null);
        }

        if (action == MaterialRequestWorkflowAction.Approve && currentStatus == StatusHeadDeptApproved)
        {
            return new MaterialRequestWorkflowTransition(
                    StatusPurchaserChecked,
                    "Checked by Purchaser.",
                    null,
                    null,
                    null);
        }

        if (action == MaterialRequestWorkflowAction.Approve && currentStatus == StatusPurchaserChecked)
        {
            return new MaterialRequestWorkflowTransition(
                    StatusCompleted,
                    "Approved by Chief of Accounting. MR completed.",
                    null,
                    true,
                    true);
        }

        if (action == MaterialRequestWorkflowAction.Reject &&
            (currentStatus == StatusSubmittedByOwner ||
             currentStatus == StatusHeadDeptApproved ||
             currentStatus == StatusPurchaserChecked))
        {
            return new MaterialRequestWorkflowTransition(
                    StatusRejected,
                    "Material Request rejected.",
                    null,
                    true,
                    false);
        }

        return null;
    }

    // Build route quay lại trang detail sau save/workflow và giữ returnUrl.
    private object BuildDetailRoute(long? requestNo)
    {
        return new
        {
            id = requestNo,
            returnUrl = ReturnUrl
        };
    }

    // Resolve URL quay về list và chặn URL không an toàn.
    private string ResolveBackToListUrl()
    {
        var defaultUrl = Url.Page("./Index") ?? "/Purchasing/MaterialRequest/Index";
        if (string.IsNullOrWhiteSpace(ReturnUrl))
        {
            return defaultUrl;
        }

        var trimmed = ReturnUrl.Trim();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("//", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("\\\\", StringComparison.OrdinalIgnoreCase))
        {
            return defaultUrl;
        }

        if (!trimmed.StartsWith("/", StringComparison.Ordinal))
        {
            return defaultUrl;
        }

        return trimmed;
    }

    // Chuyển warning/error từ TempData sang ModelState để hiển thị trên form.

    private void ApplyTempDataMessagesToModelState()
    {
        if (!string.IsNullOrWhiteSpace(ErrorMessage))
        {
            ModelState.AddModelError(string.Empty, ErrorMessage);
        }

        if (!string.IsNullOrWhiteSpace(WarningMessage))
        {
            ModelState.AddModelError(string.Empty, WarningMessage);
        }
    }

}

// Danh sách action workflow hỗ trợ ở màn hình detail.
internal enum MaterialRequestWorkflowAction
{
    Submit,
    Approve,
    Reject
}

// Mô tả một bước chuyển trạng thái workflow.
internal sealed class MaterialRequestWorkflowTransition
{
    public int NextStatusId { get; set; }
    public string SuccessMessage { get; set; }
    public bool? Approval { get; set; }
    public bool? ApprovalEnd { get; set; }
    public bool? PostPr { get; set; }

    // Khởi tạo dữ liệu cho một bước chuyển trạng thái.
    public MaterialRequestWorkflowTransition(
        int nextStatusId,
        string successMessage,
        bool? approval,
        bool? approvalEnd,
        bool? postPr)
    {
        NextStatusId = nextStatusId;
        SuccessMessage = successMessage;
        Approval = approval;
        ApprovalEnd = approvalEnd;
        PostPr = postPr;
    }
}


