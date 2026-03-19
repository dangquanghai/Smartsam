using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Text.Json;
using SmartSam.Services;
using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Linq;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Data.SqlClient;
using SmartSam.Helpers;
using SmartSam.Pages;

namespace SmartSam.Pages.Purchasing.MaterialRequest;

public class IndexModel : BasePageModel
{
    // Tracking comment: keep Material Request index page model marked as touched for current work.
    private readonly MaterialRequestService _materialRequestService;
    private readonly PermissionService _permissionService;

    // ID của chức năng trong bảng SYS_Function
    private const int MaterialRequestFunctionId = 104;
    private const int PermissionCreate = 1;
    private const int PermissionCreateAuto = 2;
    private const int PermissionEdit = 3;
    private const int PermissionShowAll = 4;
    private const int PermissionShowPc = 5;
    private const int PermissionShowStore = 6;
    private const int PermissionShowCreate = 7;
    private const int NoScopeStoreGroup = -1;
    private const string ConditionModeAllUsers = "allUsers";
    private const string ConditionModeStoreman = "storeman";

    private EmployeeMaterialScopeDto _dataScope = new EmployeeMaterialScopeDto();
    private bool _isAdminRole;

    public IndexModel(IConfiguration configuration, PermissionService permissionService) : base(configuration)
    {
        _materialRequestService = new MaterialRequestService(configuration);
        _permissionService = permissionService;
    }

    [BindProperty(SupportsGet = true)]
    public MaterialRequestFilter Filter { get; set; } = new();

    public int DefaultPageSize
    {
        get { return _config.GetValue<int>("AppSettings:DefaultPageSize", 10); }
    }

    [BindProperty]
    public string? CreateDescription { get; set; }

    [BindProperty]
    public string? CreateLinesJson { get; set; }

    [BindProperty]
    public int? CreateStoreGroup { get; set; }

    [BindProperty]
    public string? CreateAutoDescription { get; set; }

    [BindProperty]
    public int? CreateAutoStoreGroup { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? WarningMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public PagePermissions PagePerm { get; set; } = new PagePermissions();
    public List<SelectListItem> StoreGroups { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> Statuses { get; set; } = new List<SelectListItem>();
    public List<MaterialRequestListRowDto> Rows { get; set; } = new List<MaterialRequestListRowDto>();

    public int TotalRecords { get; set; }
    public int TotalPages
    {
        get
        {
            if (Filter.PageSize <= 0)
            {
                return 1;
            }

            return Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)Filter.PageSize));
        }
    }

    public bool HasPreviousPage
    {
        get { return Filter.PageIndex > 1; }
    }

    public bool HasNextPage
    {
        get { return Filter.PageIndex < TotalPages; }
    }

    // ==========================================
    // 1. GIAI ĐOẠN LOAD TRANG (GET)
    // ==========================================
    public void OnGet()
    {
        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;

        PagePerm = GetUserPermissions();
        LoadUserScopeAsync(cancellationToken).GetAwaiter().GetResult();
        if (!CanAccessPage())
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        ApplyTempDataMessagesToModelState();
        NormalizeConditionMode();
        if (Filter.PageSize <= 0)
        {
            Filter.PageSize = DefaultPageSize;
        }
        if (!Filter.FromDate.HasValue)
        {
            Filter.FromDate = DateTime.Today.AddMonths(-3).Date;
        }

        if (!Filter.ToDate.HasValue)
        {
            Filter.ToDate = DateTime.Today.Date;
        }

        ViewData["CanShowPcCondition"] = AllowShowPcCondition();
        ViewData["CanShowStoreCondition"] = AllowShowStoreCondition();
        ViewData["StoreGroupLocked"] = IsStoreGroupLocked();

        LoadFilters();
        LoadRowsAsync(cancellationToken).GetAwaiter().GetResult();
        ViewData["DefaultPageSize"] = DefaultPageSize;
    }

    // ==========================================
    // 2. XỬ LÝ SEARCH AJAX (POST)
    // ==========================================
    public IActionResult OnPostSearch([FromBody] MaterialRequestSearchRequest request)
    {
        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;

        PagePerm = GetUserPermissions();
        LoadUserScopeAsync(cancellationToken).GetAwaiter().GetResult();
        if (!CanAccessPage())
        {
            return new JsonResult(new { success = false, message = "Forbidden" })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }

        Filter.RequestNo = request.RequestNo;
        Filter.StoreGroup = request.StoreGroup;
        if (request.StatusIds == null)
        {
            Filter.StatusIds = new List<int>();
        }
        else
        {
            Filter.StatusIds = request.StatusIds.Distinct().ToList();
        }
        Filter.ItemCode = request.ItemCode;
        Filter.IssueLessThanOrder = request.IssueLessThanOrder;
        Filter.BuyGreaterThanZero = request.BuyGreaterThanZero;
        Filter.AutoOnly = request.AutoOnly;
        Filter.FromDate = request.FromDate;
        Filter.ToDate = request.ToDate;
        Filter.AccordingToKeyword = request.AccordingToKeyword;
        Filter.ConditionMode = string.IsNullOrWhiteSpace(request.ConditionMode) ? ConditionModeAllUsers : request.ConditionMode.Trim();
        Filter.PageIndex = request.Page <= 0 ? 1 : request.Page;
        Filter.PageSize = request.PageSize <= 0 ? DefaultPageSize : Math.Min(request.PageSize, 200);

        NormalizeConditionMode();

        var result = _materialRequestService.SearchPagedAsync(BuildCriteria(includePaging: true), cancellationToken).GetAwaiter().GetResult();
        var totalPages = Filter.PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(result.TotalCount / (double)Filter.PageSize));

        var dataWithActions = result.Rows.Select(r =>
        {
            var statusId = r.MaterialStatusId ?? 0;

            // Quyền thực tế trên từng dòng dữ liệu theo trạng thái hiện tại.
            var canEditRow = AllowEdit() && statusId == 0;
            var canApprove = CanApproveStatus(statusId);
            var canReject = CanRejectStatus(statusId);

            return new
            {
                data = r,
                actions = new
                {
                    // Chế độ truy cập khi click vào số chứng từ.
                    canAccess = true,
                    accessMode = (canEditRow || canApprove || canReject) ? "edit" : "view",

                    // Các nút chức năng theo quyền + trạng thái của dòng.
                    canEdit = canEditRow,
                    canSubmit = canEditRow,
                    canApprove = canApprove,
                    canReject = canReject
                }
            };
        });

        return new JsonResult(new
        {
            success = true,
            data = dataWithActions,
            total = result.TotalCount,
            page = Filter.PageIndex,
            pageSize = Filter.PageSize,
            totalPages
        });
    }

    public IActionResult OnGetSearchItems(string? keyword, bool checkBalanceInStore = false, int? storeGroup = null)
    {
        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;

        PagePerm = GetUserPermissions();
        LoadUserScopeAsync(cancellationToken).GetAwaiter().GetResult();
        if (!CanAccessPage())
        {
            return new JsonResult(new { success = false, message = "Access denied." });
        }

        var scopedStoreGroup = IsStoreGroupLocked() ? _dataScope.StoreGroup : (storeGroup ?? Filter.StoreGroup);
        var items = _materialRequestService.SearchItemsAsync(keyword, checkBalanceInStore, scopedStoreGroup, cancellationToken).GetAwaiter().GetResult();

        // Legacy popup cho phép search rộng: nếu lọc theo store group mà rỗng thì fallback search all.
        if (items.Count == 0 && scopedStoreGroup.HasValue)
        {
            items = _materialRequestService.SearchItemsAsync(keyword, checkBalanceInStore, null, cancellationToken).GetAwaiter().GetResult();
        }

        return new JsonResult(new { success = true, data = items });
    }

    public IActionResult OnPostCreateMr()
    {
        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;

        PagePerm = GetUserPermissions();
        LoadUserScopeAsync(cancellationToken).GetAwaiter().GetResult();
        if (!CanAccessPage() || !AllowCreate())
        {
            return Forbid();
        }

        var selectedItems = ParseCreateItems(CreateLinesJson);
        if (selectedItems.Count == 0)
        {
            WarningMessage = "Please select at least one item.";
            return RedirectToPage("./Index");
        }

        if (string.IsNullOrWhiteSpace(CreateDescription))
        {
            WarningMessage = "Description is required.";
            return RedirectToPage("./Index");
        }

        var scopedStoreGroup = IsStoreGroupLocked() ? _dataScope.StoreGroup : (CreateStoreGroup ?? Filter.StoreGroup);
        if (!scopedStoreGroup.HasValue || scopedStoreGroup.Value <= 0)
        {
            var itemGroups = selectedItems
                .Select(x => x.StoreGroupId.GetValueOrDefault())
                .Where(x => x > 0)
                .Distinct()
                .ToList();

            if (itemGroups.Count == 1)
            {
                scopedStoreGroup = itemGroups[0];
            }
            else
            {
                WarningMessage = "Please choose Store Group before creating MR.";
                return RedirectToPage("./Index");
            }
        }

        var lines = MapItemsToCreateLines(selectedItems);

        var header = new MaterialRequestDetailDto
        {
            StoreGroup = scopedStoreGroup,
            DateCreate = DateTime.Today,
            FromDate = DateTime.Today.AddMonths(-3),
            ToDate = DateTime.Today,
            AccordingTo = CreateDescription.Trim(),
            MaterialStatusId = 0,
            NoIssue = 0,
            IsAuto = false,
            Approval = false,
            ApprovalEnd = false,
            PostPr = false
        };

        try
        {
            var requestNo = _materialRequestService.SaveAsync(null, header, lines, cancellationToken).GetAwaiter().GetResult();
            SuccessMessage = "Material Request created successfully.";
            return RedirectToPage("./MaterialRequestDetail", new { id = requestNo, mode = "edit" });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Cannot create Material Request. {ex.Message}";
            return RedirectToPage("./Index");
        }
    }

    public IActionResult OnPostCreateAuto()
    {
        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;

        PagePerm = GetUserPermissions();
        LoadUserScopeAsync(cancellationToken).GetAwaiter().GetResult();
        if (!CanAccessPage() || !AllowCreateAuto())
        {
            return Forbid();
        }

        var scopedStoreGroup = IsStoreGroupLocked()
            ? _dataScope.StoreGroup
            : (CreateAutoStoreGroup ?? Filter.StoreGroup);

        if (!scopedStoreGroup.HasValue || scopedStoreGroup.Value <= 0)
        {
            WarningMessage = "Please choose Store Group before creating Auto MR.";
            return RedirectToPage("./Index");
        }

        var lines = _materialRequestService.BuildAutoLinesAsync(scopedStoreGroup.Value, cancellationToken).GetAwaiter().GetResult().ToList();
        if (lines.Count == 0)
        {
            WarningMessage = "No item template found for Auto MR.";
            return RedirectToPage("./Index");
        }

        var description = string.IsNullOrWhiteSpace(CreateAutoDescription)
            ? $"Auto MR {DateTime.Today:MM/yyyy}"
            : CreateAutoDescription.Trim();

        var header = new MaterialRequestDetailDto
        {
            StoreGroup = scopedStoreGroup,
            DateCreate = DateTime.Today,
            FromDate = DateTime.Today.AddMonths(-3),
            ToDate = DateTime.Today,
            AccordingTo = description,
            MaterialStatusId = 0,
            NoIssue = 0,
            IsAuto = true,
            Approval = false,
            ApprovalEnd = false,
            PostPr = false
        };

        try
        {
            var requestNo = _materialRequestService.SaveAsync(null, header, lines, cancellationToken).GetAwaiter().GetResult();
            SuccessMessage = "Auto Material Request created successfully.";
            return RedirectToPage("./MaterialRequestDetail", new { id = requestNo, mode = "edit" });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Cannot create Auto MR. {ex.Message}";
            return RedirectToPage("./Index");
        }
    }

    // ==========================================
    // 3. CÁC HÀM BỔ TRỢ (HELPER)
    // ==========================================
    private void LoadFilters()
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

        StoreGroups = new List<SelectListItem>();
        StoreGroups.Add(new SelectListItem { Value = string.Empty, Text = "--- All ---" });
        StoreGroups.AddRange(stores);

        var statuses = LoadListFromSql(
            @"SELECT MaterialStatusID, MaterialStatusName
              FROM dbo.MaterialStatus
              ORDER BY MaterialStatusID",
            "MaterialStatusID",
            "MaterialStatusName");

        Statuses = new List<SelectListItem>(statuses);

        if (!AllowShowAll() && (Filter.StatusIds?.Count ?? 0) == 0 && !Request.Query.ContainsKey("Filter.StatusIds"))
        {
            int defaultStatus;
            if (_dataScope.ApprovalLevel <= 0)
            {
                defaultStatus = 0;
            }
            else if (_dataScope.ApprovalLevel == 1)
            {
                defaultStatus = 1;
            }
            else if (_dataScope.ApprovalLevel == 2)
            {
                defaultStatus = 2;
            }
            else
            {
                defaultStatus = 3;
            }

            Filter.StatusIds = new List<int> { defaultStatus };
        }

        if (IsStoreGroupLocked())
        {
            Filter.StoreGroup = _dataScope.StoreGroup ?? NoScopeStoreGroup;
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
                        Value = NoScopeStoreGroup.ToString(),
                        Text = "(No Store Group Scope)"
                    }
                };
            }
        }
    }

    private async Task LoadRowsAsync(CancellationToken cancellationToken)
    {
        if (Filter.PageSize <= 0) Filter.PageSize = DefaultPageSize;
        if (Filter.PageSize > 200) Filter.PageSize = 200;
        if (Filter.PageIndex <= 0) Filter.PageIndex = 1;

        var result = await _materialRequestService.SearchPagedAsync(BuildCriteria(includePaging: true), cancellationToken);
        TotalRecords = result.TotalCount;
        if (TotalRecords > 0 && Filter.PageIndex > TotalPages)
        {
            Filter.PageIndex = TotalPages;
            result = await _materialRequestService.SearchPagedAsync(BuildCriteria(includePaging: true), cancellationToken);
            TotalRecords = result.TotalCount;
        }

        Rows = result.Rows;
    }

    private MaterialRequestFilterCriteria BuildCriteria(bool includePaging)
    {
        var scopedStoreGroup = IsStoreGroupLocked()
            ? (_dataScope.StoreGroup ?? NoScopeStoreGroup)
            : Filter.StoreGroup;
        var isStoremanMode = AllowShowStoreCondition() && string.Equals(Filter.ConditionMode, ConditionModeStoreman, StringComparison.OrdinalIgnoreCase);

        return new MaterialRequestFilterCriteria
        {
            RequestNo = isStoremanMode ? null : Filter.RequestNo,
            StoreGroup = scopedStoreGroup == NoScopeStoreGroup ? null : scopedStoreGroup,
            StatusIds = isStoremanMode ? new List<int>() : (Filter.StatusIds ?? new List<int>()),
            ItemCode = isStoremanMode && !string.IsNullOrWhiteSpace(Filter.ItemCode) ? Filter.ItemCode.Trim() : null,
            NoIssue = isStoremanMode && Filter.IssueLessThanOrder ? 1 : null,
            IsAuto = isStoremanMode ? null : (Filter.AutoOnly ? true : null),
            BuyGreaterThanZero = isStoremanMode ? null : (Filter.BuyGreaterThanZero ? true : null),
            FromDate = Filter.FromDate?.Date,
            ToDate = Filter.ToDate?.Date,
            AccordingToKeyword = string.IsNullOrWhiteSpace(Filter.AccordingToKeyword) ? null : Filter.AccordingToKeyword.Trim(),
            PageIndex = includePaging ? Filter.PageIndex : null,
            PageSize = includePaging ? Filter.PageSize : null
        };
    }

    private void NormalizeConditionMode()
    {
        if (AllowShowStoreCondition() && !AllowShowPcCondition())
        {
            Filter.ConditionMode = ConditionModeStoreman;
            return;
        }

        if (!AllowShowStoreCondition() && AllowShowPcCondition())
        {
            Filter.ConditionMode = ConditionModeAllUsers;
            return;
        }

        if (!AllowShowStoreCondition() && !AllowShowPcCondition())
        {
            Filter.ConditionMode = ConditionModeAllUsers;
            return;
        }

        if (!string.Equals(Filter.ConditionMode, ConditionModeStoreman, StringComparison.OrdinalIgnoreCase))
        {
            Filter.ConditionMode = ConditionModeAllUsers;
            return;
        }

        Filter.ConditionMode = ConditionModeStoreman;
    }

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

    private PagePermissions GetUserPermissions()
    {
        bool isAdmin = IsAdminUser();
        int roleId = int.TryParse(User.FindFirst("RoleID")?.Value, out var roleValue) ? roleValue : 0;

        var permsObj = new PagePermissions();
        _isAdminRole = isAdmin;

        if (isAdmin)
        {
            permsObj.AllowedNos = Enumerable.Range(1, 10).ToList();
            return permsObj;
        }

        permsObj.AllowedNos = _permissionService.GetPermissionsForPage(roleId, MaterialRequestFunctionId);
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

    private bool CanAccessPage()
    {
        if (_isAdminRole)
        {
            return true;
        }

        return PagePerm.AllowedNos.Count > 0;
    }

    private bool HasPermission(int permissionNo)
    {
        return PagePerm.HasPermission(permissionNo);
    }

    private bool AllowCreate()
    {
        return HasPermission(PermissionCreate) || HasPermission(PermissionShowCreate);
    }

    private bool AllowCreateAuto()
    {
        return HasPermission(PermissionCreateAuto);
    }

    private bool AllowEdit()
    {
        return HasPermission(PermissionEdit);
    }

    private bool AllowShowAll()
    {
        return _isAdminRole || HasPermission(PermissionShowAll) || _dataScope.SeeDataAllDept;
    }

    private bool AllowShowPcCondition()
    {
        return _isAdminRole || HasPermission(PermissionShowPc) || AllowShowAll();
    }

    private bool AllowShowStoreCondition()
    {
        return _isAdminRole || HasPermission(PermissionShowStore) || AllowShowAll();
    }

    private bool IsStoreGroupLocked()
    {
        return !AllowShowAll();
    }

    // Chuyển warning/error từ TempData sang ModelState để hiển thị đúng chuẩn trang.
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

    private bool CanApproveStatus(int statusId)
    {
        if (_isAdminRole)
        {
            return true;
        }

        if (statusId == 1)
        {
            return _dataScope.IsHeadDept || _dataScope.ApprovalLevel >= 2;
        }

        if (statusId == 2)
        {
            return _dataScope.ApprovalLevel >= 2;
        }

        if (statusId == 3)
        {
            return _dataScope.ApprovalLevel >= 3;
        }

        return false;
    }

    private bool CanRejectStatus(int statusId)
    {
        if (_isAdminRole)
        {
            return true;
        }
        if (statusId is not (1 or 2 or 3))
        {
            return false;
        }

        return _dataScope.IsHeadDept || _dataScope.ApprovalLevel >= 2;
    }

    private static List<MaterialRequestItemLookupDto> ParseCreateItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<MaterialRequestItemLookupDto>();
        }

        try
        {
            var rows = JsonSerializer.Deserialize<List<MaterialRequestItemLookupDto>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (rows == null)
            {
                return new List<MaterialRequestItemLookupDto>();
            }

            return rows
                .Where(x => !string.IsNullOrWhiteSpace(x.ItemCode))
                .ToList();
        }
        catch
        {
            return new List<MaterialRequestItemLookupDto>();
        }
    }

    private static List<MaterialRequestLineDto> MapItemsToCreateLines(IEnumerable<MaterialRequestItemLookupDto> items)
    {
        var result = new List<MaterialRequestLineDto>();

        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.ItemCode))
            {
                continue;
            }

            var line = new MaterialRequestLineDto
            {
                ItemCode = item.ItemCode.Trim(),
                ItemName = (item.ItemName ?? string.Empty).Trim(),
                Unit = (item.Unit ?? string.Empty).Trim(),
                OrderQty = 0,
                NotReceipt = 0,
                InStock = item.InStock,
                AccIn = 0,
                Buy = 0,
                Price = 0,
                Note = string.Empty,
                NewItem = false,
                Selected = true
            };

            result.Add(line);
        }

        return result;
    }
}

public class MaterialRequestFilter
{
    public long? RequestNo { get; set; }
    public int? StoreGroup { get; set; }
    public List<int> StatusIds { get; set; } = new List<int>();
    public string? ItemCode { get; set; }
    public bool IssueLessThanOrder { get; set; }
    public bool BuyGreaterThanZero { get; set; }
    public bool AutoOnly { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? AccordingToKeyword { get; set; }
    public string ConditionMode { get; set; } = "allUsers";
    public int PageIndex { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class MaterialRequestSearchRequest
{
    public long? RequestNo { get; set; }
    public int? StoreGroup { get; set; }
    public List<int>? StatusIds { get; set; }
    public string? ItemCode { get; set; }
    public bool IssueLessThanOrder { get; set; }
    public bool BuyGreaterThanZero { get; set; }
    public bool AutoOnly { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? AccordingToKeyword { get; set; }
    public string? ConditionMode { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

// ==================== Material Request DTOs ====================
public class MaterialRequestLookupOptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class MaterialRequestFilterCriteria
{
    public long? RequestNo { get; set; }
    public int? StoreGroup { get; set; }
    public IReadOnlyList<int>? StatusIds { get; set; }
    public string? ItemCode { get; set; }
    public int? NoIssue { get; set; }
    public bool? IsAuto { get; set; }
    public bool? BuyGreaterThanZero { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? AccordingToKeyword { get; set; }
    public int? PageIndex { get; set; }
    public int? PageSize { get; set; }
}

public class MaterialRequestSearchResultDto
{
    public List<MaterialRequestListRowDto> Rows { get; set; } = new List<MaterialRequestListRowDto>();
    public int TotalCount { get; set; }
}

public class MaterialRequestListRowDto
{
    public long RequestNo { get; set; }
    public int? StoreGroup { get; set; }
    public string KPGroupName { get; set; } = string.Empty;
    public DateTime? DateCreate { get; set; }
    public string DateCreateDisplay => DateCreate.HasValue ? DateCreate.Value.ToString("dd/MM/yyyy") : string.Empty;
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string AccordingTo { get; set; } = string.Empty;
    public bool Approval { get; set; }
    public bool ApprovalEnd { get; set; }
    public bool IsAuto { get; set; }
    public bool PostPr { get; set; }
    public int? MaterialStatusId { get; set; }
    public string MaterialStatusName { get; set; } = string.Empty;
    public int? NoIssue { get; set; }
    public string PrNo { get; set; } = string.Empty;
}

public class MaterialRequestDetailDto
{
    [BindNever]
    public long RequestNo { get; set; }

    [Required(ErrorMessage = "Store Group is required.")]
    public int? StoreGroup { get; set; }

    [Required(ErrorMessage = "Date is required.")]
    public DateTime? DateCreate { get; set; }

    [Required(ErrorMessage = "Description is required.")]
    [StringLength(300, ErrorMessage = "Description must be at most 300 characters.")]
    public string? AccordingTo { get; set; }

    public bool Approval { get; set; }
    public bool ApprovalEnd { get; set; }
    public bool PostPr { get; set; }
    public bool IsAuto { get; set; }

    public DateTime? FromDate { get; set; }

    public DateTime? ToDate { get; set; }

    public int? MaterialStatusId { get; set; }

    [StringLength(50, ErrorMessage = "PR No must be at most 50 characters.")]
    public string? PrNo { get; set; }

    public int? NoIssue { get; set; }
}

public class MaterialRequestLineDto
{
    public int? Id { get; set; }

    [Required(ErrorMessage = "Item Code is required.")]
    [StringLength(20, ErrorMessage = "Item Code must be at most 20 characters.")]
    public string? ItemCode { get; set; }

    [StringLength(150, ErrorMessage = "Item Name must be at most 150 characters.")]
    public string? ItemName { get; set; }

    [StringLength(50, ErrorMessage = "Unit must be at most 50 characters.")]
    public string? Unit { get; set; }

    public decimal? OrderQty { get; set; }
    public decimal? NotReceipt { get; set; }
    public decimal? InStock { get; set; }
    public decimal? AccIn { get; set; }
    public decimal? Buy { get; set; }
    public decimal? Price { get; set; }

    [StringLength(255, ErrorMessage = "Note must be at most 255 characters.")]
    public string? Note { get; set; }

    public bool NewItem { get; set; }
    public bool Selected { get; set; }
}

public class MaterialRequestLineReadonlySnapshotDto
{
    public string ItemCode { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal NotReceipt { get; set; }
    public decimal InStock { get; set; }
    public decimal AccIn { get; set; }
    public decimal Buy { get; set; }
}

public class EmployeeMaterialScopeDto
{
    public int? StoreGroup { get; set; }
    public int ApprovalLevel { get; set; }
    public bool IsHeadDept { get; set; }
    public bool SeeDataAllDept { get; set; }
}

public class MaterialRequestItemLookupDto
{
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal InStock { get; set; }
    public int? StoreGroupId { get; set; }
}

// ==================== Material Request Data Access ====================
public class MaterialRequestService
{
    private readonly string _connectionString;
    public MaterialRequestService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Missing connection string: DefaultConnection");
    }

    public async Task<EmployeeMaterialScopeDto> GetEmployeeScopeAsync(string? employeeCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeCode))
        {
            return new EmployeeMaterialScopeDto();
        }

        const string sql = @"
            SELECT TOP 1
                StoreGR,
                ISNULL(Approval, 0) AS ApprovalLevel,
                ISNULL(HeadDept, 0) AS IsHeadDept,
                ISNULL(SeeDataAllDept, 0) AS SeeDataAllDept
            FROM dbo.MS_Employee
            WHERE EmployeeCode = @EmployeeCode";

        return await Helper.QuerySingleOrDefaultAsync(
            _connectionString,
            sql,
            rd => new EmployeeMaterialScopeDto
            {
                StoreGroup = rd.IsDBNull(0) ? null : Convert.ToInt32(rd[0]),
                ApprovalLevel = rd.IsDBNull(1) ? 0 : Convert.ToInt32(rd[1]),
                IsHeadDept = !rd.IsDBNull(2) && Convert.ToInt32(rd[2]) == 1,
                SeeDataAllDept = !rd.IsDBNull(3) && Convert.ToInt32(rd[3]) == 1
            },
            cmd => Helper.AddParameter(cmd, "@EmployeeCode", employeeCode.Trim(), SqlDbType.VarChar, 50),
            cancellationToken) ?? new EmployeeMaterialScopeDto();
    }

    public async Task<IReadOnlyList<MaterialRequestLookupOptionDto>> GetStoreGroupsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT KPGroupID, KPGroupName
            FROM dbo.INV_KPGroup
            ORDER BY KPGroupID";

        var rows = await Helper.QueryAsync(
            _connectionString,
            sql,
            rd => new MaterialRequestLookupOptionDto
            {
                Id = rd.GetInt32(0),
                Name = rd[1]?.ToString() ?? $"Store Group {rd.GetInt32(0)}"
            },
            null,
            cancellationToken);

        if (rows.Count > 0)
        {
            return rows;
        }

        const string fallbackSql = @"
            SELECT DISTINCT CAST(StoreGR AS int) AS StoreGroup
            FROM dbo.MS_Employee
            WHERE StoreGR IS NOT NULL AND StoreGR > 0
            ORDER BY StoreGroup";

        rows = await Helper.QueryAsync(
            _connectionString,
            fallbackSql,
            rd => new MaterialRequestLookupOptionDto
            {
                Id = rd.GetInt32(0),
                Name = $"Store Group {rd.GetInt32(0)}"
            },
            null,
            cancellationToken);

        return rows;
    }

    public async Task<IReadOnlyList<MaterialRequestLookupOptionDto>> GetStatusesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT MaterialStatusID, MaterialStatusName
            FROM dbo.MaterialStatus
            ORDER BY MaterialStatusID";

        var rows = await Helper.QueryAsync(
            _connectionString,
            sql,
            rd => new MaterialRequestLookupOptionDto
            {
                Id = rd.GetInt32(0),
                Name = rd[1]?.ToString() ?? string.Empty
            },
            null,
            cancellationToken);

        return rows;
    }

    public async Task<MaterialRequestSearchResultDto> SearchPagedAsync(MaterialRequestFilterCriteria criteria, CancellationToken cancellationToken = default)
    {
        var pageIndex = criteria.PageIndex.GetValueOrDefault() <= 0 ? 1 : criteria.PageIndex!.Value;
        var pageSize = criteria.PageSize.GetValueOrDefault() <= 0 ? 25 : criteria.PageSize!.Value;
        List<int> statusIds;
        if (criteria.StatusIds == null)
        {
            statusIds = new List<int>();
        }
        else
        {
            statusIds = criteria.StatusIds
                .Distinct()
                .ToList();
        }
        var statusFilterSql = BuildStatusFilterSql(statusIds);

        var fromWhereSql = $@"
            FROM dbo.MATERIAL_REQUEST r
            LEFT JOIN dbo.MaterialStatus ms ON ms.MaterialStatusID = r.MATERIALSTATUSID
            LEFT JOIN dbo.INV_KPGroup kp ON kp.KPGroupID = r.STORE_GROUP
            WHERE
                (@RequestNo IS NULL OR r.REQUEST_NO = @RequestNo)
                AND (@StoreGroup IS NULL OR r.STORE_GROUP = @StoreGroup)
                AND {statusFilterSql}
                AND (@NoIssue IS NULL OR r.NO_ISSUE = @NoIssue)
                AND (@IsAuto IS NULL OR r.IS_AUTO = @IsAuto)
                AND (@FromDate IS NULL OR r.DATE_CREATE >= @FromDate)
                AND (@ToDate IS NULL OR r.DATE_CREATE < DATEADD(DAY, 1, @ToDate))
                AND (@AccordingTo IS NULL OR r.ACCORDINGTO LIKE '%' + @AccordingTo + '%')
                AND (
                    @ItemCode IS NULL OR EXISTS (
                        SELECT 1
                        FROM dbo.MATERIAL_REQUEST_DETAIL d
                        WHERE d.REQUEST_NO = r.REQUEST_NO
                        AND d.ITEMCODE LIKE '%' + @ItemCode + '%'
                    )
                )
                AND (
                    @BuyGreaterThanZero IS NULL OR EXISTS (
                        SELECT 1
                        FROM dbo.MATERIAL_REQUEST_DETAIL d2
                        WHERE d2.REQUEST_NO = r.REQUEST_NO
                        AND ISNULL(d2.BUY, 0) > 0
                    )
                )";

        var countSql = "SELECT COUNT(1) " + fromWhereSql;
        var totalObj = await Helper.ExecuteScalarAsync(
            _connectionString,
            countSql,
            cmd => BindSearchParams(cmd, criteria, statusIds),
            cancellationToken);
        var totalCount = Convert.ToInt32(totalObj);

        var querySql = $@"
            SELECT
                CAST(r.REQUEST_NO AS bigint) AS REQUEST_NO,
                CAST(r.STORE_GROUP AS int) AS STORE_GROUP,
                ISNULL(kp.KPGroupName, CONCAT('Store Group ', CAST(r.STORE_GROUP AS varchar(20)))) AS KPGroupName,
                r.DATE_CREATE,
                r.FROM_DATE,
                r.TO_DATE,
                ISNULL(r.ACCORDINGTO, '') AS ACCORDINGTO,
                ISNULL(r.APPROVAL, 0) AS APPROVAL,
                ISNULL(r.APPROVAL_END, 0) AS APPROVAL_END,
                ISNULL(r.IS_AUTO, 0) AS IS_AUTO,
                ISNULL(r.POST_PR, 0) AS POST_PR,
                r.MATERIALSTATUSID,
                ISNULL(ms.MaterialStatusName, '') AS MaterialStatusName,
                r.NO_ISSUE,
                ISNULL(r.PRNO, '') AS PRNO
            {fromWhereSql}
            ORDER BY r.DATE_CREATE DESC, r.REQUEST_NO DESC
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

        var rows = await Helper.QueryAsync(
            _connectionString,
            querySql,
            rd => new MaterialRequestListRowDto
            {
                RequestNo = rd.IsDBNull(0) ? 0 : rd.GetInt64(0),
                StoreGroup = rd.IsDBNull(1) ? null : rd.GetInt32(1),
                KPGroupName = rd[2]?.ToString() ?? string.Empty,
                DateCreate = rd.IsDBNull(3) ? null : rd.GetDateTime(3),
                FromDate = rd.IsDBNull(4) ? null : rd.GetDateTime(4),
                ToDate = rd.IsDBNull(5) ? null : rd.GetDateTime(5),
                AccordingTo = rd[6]?.ToString() ?? string.Empty,
                Approval = !rd.IsDBNull(7) && Convert.ToBoolean(rd[7]),
                ApprovalEnd = !rd.IsDBNull(8) && Convert.ToBoolean(rd[8]),
                IsAuto = !rd.IsDBNull(9) && Convert.ToBoolean(rd[9]),
                PostPr = !rd.IsDBNull(10) && Convert.ToBoolean(rd[10]),
                MaterialStatusId = rd.IsDBNull(11) ? null : Convert.ToInt32(rd[11]),
                MaterialStatusName = rd[12]?.ToString() ?? string.Empty,
                NoIssue = rd.IsDBNull(13) ? null : Convert.ToInt32(rd[13]),
                PrNo = rd[14]?.ToString() ?? string.Empty
            },
            cmd =>
            {
                BindSearchParams(cmd, criteria, statusIds);
                Helper.AddParameter(cmd, "@Offset", (pageIndex - 1) * pageSize, SqlDbType.Int);
                Helper.AddParameter(cmd, "@PageSize", pageSize, SqlDbType.Int);
            },
            cancellationToken);

        return new MaterialRequestSearchResultDto
        {
            Rows = rows,
            TotalCount = totalCount
        };
    }

    public async Task<MaterialRequestDetailDto?> GetDetailAsync(long requestNo, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                CAST(REQUEST_NO AS bigint) AS REQUEST_NO,
                CAST(STORE_GROUP AS int) AS STORE_GROUP,
                DATE_CREATE,
                ACCORDINGTO,
                ISNULL(APPROVAL, 0) AS APPROVAL,
                ISNULL(APPROVAL_END, 0) AS APPROVAL_END,
                ISNULL(POST_PR, 0) AS POST_PR,
                ISNULL(IS_AUTO, 0) AS IS_AUTO,
                FROM_DATE,
                TO_DATE,
                MATERIALSTATUSID,
                PRNO,
                NO_ISSUE
            FROM dbo.MATERIAL_REQUEST
            WHERE REQUEST_NO = @RequestNo";

        return await Helper.QuerySingleOrDefaultAsync(
            _connectionString,
            sql,
            rd => new MaterialRequestDetailDto
            {
                RequestNo = rd.IsDBNull(0) ? 0 : rd.GetInt64(0),
                StoreGroup = rd.IsDBNull(1) ? null : rd.GetInt32(1),
                DateCreate = rd.IsDBNull(2) ? null : rd.GetDateTime(2),
                AccordingTo = rd[3]?.ToString(),
                Approval = !rd.IsDBNull(4) && Convert.ToBoolean(rd[4]),
                ApprovalEnd = !rd.IsDBNull(5) && Convert.ToBoolean(rd[5]),
                PostPr = !rd.IsDBNull(6) && Convert.ToBoolean(rd[6]),
                IsAuto = !rd.IsDBNull(7) && Convert.ToBoolean(rd[7]),
                FromDate = rd.IsDBNull(8) ? null : rd.GetDateTime(8),
                ToDate = rd.IsDBNull(9) ? null : rd.GetDateTime(9),
                MaterialStatusId = rd.IsDBNull(10) ? null : Convert.ToInt32(rd[10]),
                PrNo = rd[11]?.ToString(),
                NoIssue = rd.IsDBNull(12) ? null : Convert.ToInt32(rd[12])
            },
            cmd => AddNumeric18_0Param(cmd, "@RequestNo", requestNo),
            cancellationToken);
    }

    public async Task<IReadOnlyList<MaterialRequestLineDto>> GetLinesAsync(long requestNo, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                d.ID,
                d.ITEMCODE,
                ISNULL(i.ItemName, '') AS ITEMNAME,
                d.UNIT,
                d.NEW_ORDER,
                d.NOT_RECEIPT,
                d.INSTOCK,
                d.acctualyInventory,
                d.BUY,
                d.PRICE,
                d.NOTE,
                ISNULL(d.NEW_ITEM, 0) AS NEW_ITEM,
                ISNULL(d.SELECTED, 0) AS SELECTED
            FROM dbo.MATERIAL_REQUEST_DETAIL d
            LEFT JOIN dbo.INV_ItemList i ON i.ItemCode = d.ITEMCODE
            WHERE REQUEST_NO = @RequestNo
            ORDER BY d.ID";

        var rows = await Helper.QueryAsync(
            _connectionString,
            sql,
            rd => new MaterialRequestLineDto
            {
                Id = rd.IsDBNull(0) ? null : Convert.ToInt32(rd[0]),
                ItemCode = rd[1]?.ToString(),
                ItemName = rd[2]?.ToString(),
                Unit = rd[3]?.ToString(),
                OrderQty = rd.IsDBNull(4) ? null : Convert.ToDecimal(rd[4]),
                NotReceipt = rd.IsDBNull(5) ? null : Convert.ToDecimal(rd[5]),
                InStock = rd.IsDBNull(6) ? null : Convert.ToDecimal(rd[6]),
                AccIn = rd.IsDBNull(7) ? null : Convert.ToDecimal(rd[7]),
                Buy = rd.IsDBNull(8) ? null : Convert.ToDecimal(rd[8]),
                Price = rd.IsDBNull(9) ? null : Convert.ToDecimal(rd[9]),
                Note = rd[10]?.ToString(),
                NewItem = !rd.IsDBNull(11) && Convert.ToBoolean(rd[11]),
                Selected = !rd.IsDBNull(12) && Convert.ToBoolean(rd[12])
            },
            cmd => AddNumeric18_0Param(cmd, "@RequestNo", requestNo),
            cancellationToken);

        return rows;
    }

    public async Task<Dictionary<string, MaterialRequestLineReadonlySnapshotDto>> GetLineReadonlySnapshotsAsync(long requestNo, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                ITEMCODE,
                ISNULL(UNIT, '') AS UNIT,
                ISNULL(NOT_RECEIPT, 0) AS NOT_RECEIPT,
                ISNULL(INSTOCK, 0) AS INSTOCK,
                ISNULL(acctualyInventory, 0) AS acctualyInventory,
                ISNULL(BUY, 0) AS BUY
            FROM dbo.MATERIAL_REQUEST_DETAIL
            WHERE REQUEST_NO = @RequestNo
            ORDER BY ID";

        var rows = await Helper.QueryAsync(
            _connectionString,
            sql,
            rd => new MaterialRequestLineReadonlySnapshotDto
            {
                ItemCode = rd[0]?.ToString() ?? string.Empty,
                Unit = rd[1]?.ToString() ?? string.Empty,
                NotReceipt = rd.IsDBNull(2) ? 0m : Convert.ToDecimal(rd[2]),
                InStock = rd.IsDBNull(3) ? 0m : Convert.ToDecimal(rd[3]),
                AccIn = rd.IsDBNull(4) ? 0m : Convert.ToDecimal(rd[4]),
                Buy = rd.IsDBNull(5) ? 0m : Convert.ToDecimal(rd[5])
            },
            cmd => AddNumeric18_0Param(cmd, "@RequestNo", requestNo),
            cancellationToken);

        var snapshots = new Dictionary<string, MaterialRequestLineReadonlySnapshotDto>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var itemCode = (row.ItemCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(itemCode) || snapshots.ContainsKey(itemCode))
            {
                continue;
            }

            row.ItemCode = itemCode;
            row.Unit = (row.Unit ?? string.Empty).Trim();
            snapshots[itemCode] = row;
        }

        return snapshots;
    }

    public async Task<Dictionary<string, string>> GetItemUnitsAsync(IEnumerable<string> itemCodes, CancellationToken cancellationToken = default)
    {
        var normalizedCodes = itemCodes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalizedCodes.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var inParams = normalizedCodes.Select((_, idx) => $"@Code{idx}").ToList();
        var sql = $@"
            SELECT ItemCode, ISNULL(Unit, '') AS Unit
            FROM dbo.INV_ItemList
            WHERE ItemCode IN ({string.Join(", ", inParams)})";

        var rows = await Helper.QueryAsync(
            _connectionString,
            sql,
            rd => new MaterialRequestItemLookupDto
            {
                ItemCode = rd[0]?.ToString() ?? string.Empty,
                Unit = rd[1]?.ToString() ?? string.Empty
            },
            cmd =>
            {
                for (var i = 0; i < normalizedCodes.Count; i++)
                {
                    Helper.AddParameter(cmd, $"@Code{i}", normalizedCodes[i], SqlDbType.VarChar, 20);
                }
            },
            cancellationToken);

        var unitMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            var itemCode = (row.ItemCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(itemCode))
            {
                continue;
            }

            unitMap[itemCode] = (row.Unit ?? string.Empty).Trim();
        }

        return unitMap;
    }

    public async Task<long> SaveAsync(long? requestNo, MaterialRequestDetailDto header, IReadOnlyList<MaterialRequestLineDto> lines, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        try
        {
            var resolvedRequestNo = requestNo ?? await GetNextRequestNoAsync(conn, (SqlTransaction)tx, cancellationToken);
            var statusId = header.MaterialStatusId ?? await GetDefaultStatusIdAsync(conn, (SqlTransaction)tx, cancellationToken);

            if (requestNo.HasValue)
            {
                const string updateSql = @"
                    UPDATE dbo.MATERIAL_REQUEST
                    SET
                        STORE_GROUP = @StoreGroup,
                        DATE_CREATE = @DateCreate,
                        ACCORDINGTO = @AccordingTo,
                        APPROVAL = @Approval,
                        POST_PR = @PostPr,
                        IS_AUTO = @IsAuto,
                        FROM_DATE = @FromDate,
                        TO_DATE = @ToDate,
                        APPROVAL_END = @ApprovalEnd,
                        MATERIALSTATUSID = @StatusId,
                        PRNO = @PrNo,
                        NO_ISSUE = @NoIssue
                    WHERE REQUEST_NO = @RequestNo";

                await using var updateCmd = new SqlCommand(updateSql, conn, (SqlTransaction)tx);
                BindHeaderParams(updateCmd, resolvedRequestNo, header, statusId);
                var updated = await updateCmd.ExecuteNonQueryAsync(cancellationToken);
                if (updated == 0)
                {
                    throw new InvalidOperationException("Material Request not found.");
                }
            }
            else
            {
                const string insertSql = @"
                    INSERT INTO dbo.MATERIAL_REQUEST
                    (
                        REQUEST_NO, STORE_GROUP, DATE_CREATE, ACCORDINGTO, APPROVAL, POST_PR, IS_AUTO,
                        FROM_DATE, TO_DATE, APPROVAL_END, MATERIALSTATUSID, PRNO, NO_ISSUE
                    )
                    VALUES
                    (
                        @RequestNo, @StoreGroup, @DateCreate, @AccordingTo, @Approval, @PostPr, @IsAuto,
                        @FromDate, @ToDate, @ApprovalEnd, @StatusId, @PrNo, @NoIssue
                    )";

                await using var insertCmd = new SqlCommand(insertSql, conn, (SqlTransaction)tx);
                BindHeaderParams(insertCmd, resolvedRequestNo, header, statusId);
                await insertCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            const string deleteLineSql = "DELETE FROM dbo.MATERIAL_REQUEST_DETAIL WHERE REQUEST_NO = @RequestNo";
            await using (var deleteCmd = new SqlCommand(deleteLineSql, conn, (SqlTransaction)tx))
            {
                AddNumeric18_0Param(deleteCmd, "@RequestNo", resolvedRequestNo);
                await deleteCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            if (lines.Count > 0)
            {
                const string insertLineSql = @"
                    INSERT INTO dbo.MATERIAL_REQUEST_DETAIL
                    (
                        REQUEST_NO, ITEMCODE, UNIT, BEGIN_Q, RECEIPT_Q, USING_Q, END_Q,
                        NORM_Q, NOT_RECEIPT, NEW_ORDER, INSTOCK, acctualyInventory, BUY, ISSUED,
                        NEW_ITEM, SELECTED, NORM_Q_MAIN, PRICE, NOTE, PostedPR, ManualCheck, TempStore, CreatedAsset
                    )
                    VALUES
                    (
                        @RequestNo, @ItemCode, @Unit, 0, 0, 0, 0,
                        0, @NotReceipt, @OrderQty, @InStock, @AccIn, @Buy, 0,
                        @NewItem, @Selected, 0, @Price, @Note, 0, 0, 0, 0
                    )";

                foreach (var line in lines)
                {
                    await using var lineCmd = new SqlCommand(insertLineSql, conn, (SqlTransaction)tx);
                    AddNumeric18_0Param(lineCmd, "@RequestNo", resolvedRequestNo);
                    Helper.AddParameter(lineCmd, "@ItemCode", (line.ItemCode ?? string.Empty).Trim(), SqlDbType.VarChar, 20);
                    Helper.AddParameter(lineCmd, "@Unit", (line.Unit ?? string.Empty).Trim(), SqlDbType.VarChar, 50);
                    AddDecimal18_2Param(lineCmd, "@OrderQty", line.OrderQty ?? 0m);
                    AddDecimal18_2Param(lineCmd, "@NotReceipt", line.NotReceipt ?? 0m);
                    AddDecimal18_2Param(lineCmd, "@InStock", line.InStock ?? 0m);
                    AddDecimal18_2Param(lineCmd, "@AccIn", line.AccIn ?? 0m);
                    AddDecimal18_2Param(lineCmd, "@Buy", line.Buy ?? 0m);
                    AddDecimal18_2Param(lineCmd, "@Price", line.Price ?? 0m);
                    Helper.AddParameter(lineCmd, "@Note", (line.Note ?? string.Empty).Trim(), SqlDbType.VarChar, 255);
                    Helper.AddParameter(lineCmd, "@NewItem", line.NewItem, SqlDbType.Bit);
                    Helper.AddParameter(lineCmd, "@Selected", line.Selected, SqlDbType.Bit);

                    await lineCmd.ExecuteNonQueryAsync(cancellationToken);
                }
            }

            await tx.CommitAsync(cancellationToken);
            return resolvedRequestNo;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<IReadOnlyList<MaterialRequestItemLookupDto>> SearchItemsAsync(
        string? keyword,
        bool checkBalanceInStore = false,
        int? storeGroup = null,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT TOP 100
                i.ItemCode,
                i.ItemName,
                ISNULL(i.Unit, '') AS Unit,
                ISNULL(MAX(b.IsQ), 0) AS InStock,
                i.KPGroupItem
            FROM dbo.INV_ItemList i
            LEFT JOIN dbo.INV_ItemBalanceACC_TMP b ON b.ItemCode = i.ItemCode
            WHERE
                (@Keyword IS NULL
                    OR i.ItemCode LIKE '%' + @Keyword + '%'
                    OR i.ItemName LIKE '%' + @Keyword + '%')
                AND (i.IsActive = 1 OR i.IsActive IS NULL)
                AND (@CheckBalanceInStore = 0 OR ISNULL(b.IsQ, 0) > 0)
                AND (@StoreGroup IS NULL OR @StoreGroup = 0 OR i.KPGroupItem = @StoreGroup)
            GROUP BY i.ItemCode, i.ItemName, i.Unit, i.KPGroupItem
            ORDER BY i.ItemCode";

        var rows = await Helper.QueryAsync(
            _connectionString,
            sql,
            rd => new MaterialRequestItemLookupDto
            {
                ItemCode = rd[0]?.ToString() ?? string.Empty,
                ItemName = rd[1]?.ToString() ?? string.Empty,
                Unit = rd[2]?.ToString() ?? string.Empty,
                InStock = rd.IsDBNull(3) ? 0 : Convert.ToDecimal(rd[3]),
                StoreGroupId = rd.IsDBNull(4) ? null : Convert.ToInt32(rd[4])
            },
            cmd =>
            {
                Helper.AddParameter(cmd, "@Keyword", string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim(), SqlDbType.VarChar, 150);
                Helper.AddParameter(cmd, "@CheckBalanceInStore", checkBalanceInStore, SqlDbType.Bit);
                Helper.AddParameter(cmd, "@StoreGroup", storeGroup, SqlDbType.Int);
            },
            cancellationToken);

        return rows;
    }

    public async Task<MaterialRequestItemLookupDto> CreateQuickItemAsync(string itemName, string? unit, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(itemName))
        {
            throw new InvalidOperationException("Item Name is required.");
        }

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        try
        {
            var itemCode = await GenerateQuickItemCodeAsync(conn, (SqlTransaction)tx, cancellationToken);

            const string sql = @"
                INSERT INTO dbo.INV_ItemList
                (
                    ItemCode, ItemName, ItemCatg, Unit, IsMaterial, IsPurchase, IsActive,
                    IsNewItem, CreatedDate, ItemNameNew
                )
                VALUES
                (
                    @ItemCode, @ItemName, 0, @Unit, 1, 1, 1,
                    1, CONVERT(nvarchar(100), GETDATE(), 120), @ItemName
                )";

            await using var cmd = new SqlCommand(sql, conn, (SqlTransaction)tx);
            Helper.AddParameter(cmd, "@ItemCode", itemCode, SqlDbType.VarChar, 20);
            Helper.AddParameter(cmd, "@ItemName", itemName.Trim(), SqlDbType.VarChar, 150);
            Helper.AddParameter(cmd, "@Unit", string.IsNullOrWhiteSpace(unit) ? null : unit.Trim(), SqlDbType.VarChar, 10);
            await cmd.ExecuteNonQueryAsync(cancellationToken);

            await tx.CommitAsync(cancellationToken);

            return new MaterialRequestItemLookupDto
            {
                ItemCode = itemCode,
                ItemName = itemName.Trim(),
                Unit = string.IsNullOrWhiteSpace(unit) ? string.Empty : unit.Trim()
            };
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static void BindSearchParams(SqlCommand cmd, MaterialRequestFilterCriteria criteria, IReadOnlyList<int> statusIds)
    {
        AddNumeric18_0Param(cmd, "@RequestNo", criteria.RequestNo);
        AddNumeric18_0Param(cmd, "@StoreGroup", criteria.StoreGroup);
        Helper.AddParameter(cmd, "@NoIssue", criteria.NoIssue, SqlDbType.Int);
        Helper.AddParameter(cmd, "@IsAuto", criteria.IsAuto, SqlDbType.Bit);
        Helper.AddParameter(cmd, "@BuyGreaterThanZero", criteria.BuyGreaterThanZero, SqlDbType.Bit);
        Helper.AddParameter(cmd, "@FromDate", criteria.FromDate, SqlDbType.DateTime);
        Helper.AddParameter(cmd, "@ToDate", criteria.ToDate, SqlDbType.DateTime);
        Helper.AddParameter(cmd, "@AccordingTo", string.IsNullOrWhiteSpace(criteria.AccordingToKeyword) ? null : criteria.AccordingToKeyword.Trim(), SqlDbType.VarChar, 300);
        Helper.AddParameter(cmd, "@ItemCode", string.IsNullOrWhiteSpace(criteria.ItemCode) ? null : criteria.ItemCode.Trim(), SqlDbType.VarChar, 20);

        for (var i = 0; i < statusIds.Count; i++)
        {
            Helper.AddParameter(cmd, $"@StatusId{i}", statusIds[i], SqlDbType.Int);
        }
    }

    private static void BindHeaderParams(SqlCommand cmd, long requestNo, MaterialRequestDetailDto header, int statusId)
    {
        AddNumeric18_0Param(cmd, "@RequestNo", requestNo);
        AddNumeric18_0Param(cmd, "@StoreGroup", header.StoreGroup);
        Helper.AddParameter(cmd, "@DateCreate", header.DateCreate, SqlDbType.DateTime);
        Helper.AddParameter(cmd, "@AccordingTo", (header.AccordingTo ?? string.Empty).Trim(), SqlDbType.VarChar, 300);
        Helper.AddParameter(cmd, "@Approval", header.Approval, SqlDbType.Bit);
        Helper.AddParameter(cmd, "@PostPr", header.PostPr, SqlDbType.Bit);
        Helper.AddParameter(cmd, "@IsAuto", header.IsAuto, SqlDbType.Bit);
        Helper.AddParameter(cmd, "@FromDate", header.FromDate, SqlDbType.DateTime);
        Helper.AddParameter(cmd, "@ToDate", header.ToDate, SqlDbType.DateTime);
        Helper.AddParameter(cmd, "@ApprovalEnd", header.ApprovalEnd, SqlDbType.Bit);
        Helper.AddParameter(cmd, "@StatusId", statusId, SqlDbType.Int);
        Helper.AddParameter(cmd, "@PrNo", string.IsNullOrWhiteSpace(header.PrNo) ? null : header.PrNo.Trim(), SqlDbType.VarChar, 50);
        Helper.AddParameter(cmd, "@NoIssue", header.NoIssue, SqlDbType.Int);
    }

    private static string BuildStatusFilterSql(IReadOnlyList<int> statusIds)
    {
        if (statusIds.Count == 0)
        {
            return "1 = 1";
        }

        var parameters = Enumerable.Range(0, statusIds.Count)
            .Select(i => $"@StatusId{i}");
        return $"r.MATERIALSTATUSID IN ({string.Join(", ", parameters)})";
    }

    private static async Task<long> GetNextRequestNoAsync(SqlConnection conn, SqlTransaction tx, CancellationToken cancellationToken)
    {
        const string sql = "SELECT ISNULL(MAX(REQUEST_NO), 0) + 1 FROM dbo.MATERIAL_REQUEST WITH (UPDLOCK, HOLDLOCK)";
        await using var cmd = new SqlCommand(sql, conn, tx);
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value);
    }

    private static async Task<int> GetDefaultStatusIdAsync(SqlConnection conn, SqlTransaction tx, CancellationToken cancellationToken)
    {
        const string sql = "SELECT ISNULL(MIN(MaterialStatusID), 0) FROM dbo.MaterialStatus";
        await using var cmd = new SqlCommand(sql, conn, tx);
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value);
    }

    private static async Task<string> GenerateQuickItemCodeAsync(SqlConnection conn, SqlTransaction tx, CancellationToken cancellationToken)
    {
        for (var i = 0; i < 50; i++)
        {
            var baseCode = $"NI{DateTime.UtcNow:yyMMddHHmmss}";
            var suffix = i == 0 ? string.Empty : i.ToString("00");
            var code = $"{baseCode}{suffix}";
            if (code.Length > 20)
            {
                code = code.Substring(0, 20);
            }

            const string sql = "SELECT COUNT(1) FROM dbo.INV_ItemList WITH (UPDLOCK, HOLDLOCK) WHERE ItemCode = @ItemCode";
            await using var cmd = new SqlCommand(sql, conn, tx);
            Helper.AddParameter(cmd, "@ItemCode", code, SqlDbType.VarChar, 20);
            var count = Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken));
            if (count == 0)
            {
                return code;
            }
        }

        throw new InvalidOperationException("Cannot generate Item Code. Please retry.");
    }

    public async Task<IReadOnlyList<MaterialRequestLineDto>> BuildAutoLinesAsync(int storeGroup, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                i.ItemCode,
                i.ItemName,
                ISNULL(i.Unit, '') AS Unit,
                CAST(ISNULL(NULLIF(i.StoreNormInMain, 0), NULLIF(i.ReOrderPoint, 0), 1) AS decimal(18,2)) AS OrderQty
            FROM dbo.INV_ItemList i
            WHERE
                (i.IsActive = 1 OR i.IsActive IS NULL)
                AND (i.IsPurchase = 1 OR i.IsPurchase IS NULL)
                AND i.KPGroupItem = @StoreGroup
            ORDER BY i.ItemCode";

        var rows = await Helper.QueryAsync(
            _connectionString,
            sql,
            rd =>
            {
                var orderQty = rd.IsDBNull(3) ? 1m : Convert.ToDecimal(rd[3]);
                if (orderQty <= 0)
                {
                    orderQty = 1m;
                }

                return new MaterialRequestLineDto
                {
                    ItemCode = rd[0]?.ToString() ?? string.Empty,
                    ItemName = rd[1]?.ToString() ?? string.Empty,
                    Unit = rd[2]?.ToString() ?? string.Empty,
                    OrderQty = orderQty,
                    NotReceipt = 0,
                    InStock = 0,
                    AccIn = 0,
                    Buy = 0,
                    Price = 0,
                    Note = string.Empty,
                    NewItem = false,
                    Selected = true
                };
            },
            cmd => Helper.AddParameter(cmd, "@StoreGroup", storeGroup, SqlDbType.Int),
            cancellationToken);

        return rows;
    }

    /// <summary>
    /// Thêm parameter numeric(18,0) đúng precision/scale để tránh lỗi ép kiểu khi chạy ADO.NET.
    /// </summary>
    private static void AddNumeric18_0Param(SqlCommand cmd, string name, object? value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
        p.Precision = 18;
        p.Scale = 0;
        p.Value = value is null ? DBNull.Value : Convert.ToDecimal(value);
    }

    /// <summary>
    /// Dùng cho các cột decimal có scale 2 ở dòng chi tiết MR.
    /// </summary>
    private static void AddDecimal18_2Param(SqlCommand cmd, string name, object? value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
        p.Precision = 18;
        p.Scale = 2;
        p.Value = value is null ? DBNull.Value : Convert.ToDecimal(value);
    }
}

