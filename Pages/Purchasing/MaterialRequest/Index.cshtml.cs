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
using SmartSam.Services.Interfaces;

namespace SmartSam.Pages.Purchasing.MaterialRequest;

public class IndexModel : BasePageModel
{
    private readonly MaterialRequestService _materialRequestService;
    private readonly ISecurityService _securityService;
    private readonly PermissionService _permissionService;

    private const int MaterialRequestFunctionId = 104;
    private const int PermissionCreate = 1;
    private const int PermissionCreateAuto = 2;
    private const int PermissionEdit = 3;
    private const int PermissionShowAll = 4;
    private const int PermissionShowPc = 5;
    private const int PermissionShowStore = 6;
    private const int StatusJustCreated = -1;
    private const int NoScopeStoreGroup = -1;
    private const string ConditionModeAllUsers = "allUsers";
    private const string ConditionModeStoreman = "storeman";


    private EmployeeMaterialScopeDto _dataScope = new EmployeeMaterialScopeDto();
    private bool _isAdminRole;

    public IndexModel(ISecurityService securityService, IConfiguration configuration, PermissionService permissionService) : base(configuration)
    {
        _materialRequestService = new MaterialRequestService(configuration);
        _securityService = securityService;
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

    [BindProperty]
    public DateTime? CreateAutoDateCreate { get; set; }

    [BindProperty]
    public DateTime? CreateAutoFromDate { get; set; }

    [BindProperty]
    public DateTime? CreateAutoToDate { get; set; }

    [BindProperty]
    public long? CreateAutoRequestNoPreview { get; set; }

    [TempData]
    public string? SuccessMessage { get; set; }

    [TempData]
    public string? WarningMessage { get; set; }

    [TempData]
    public string? ErrorMessage { get; set; }

    public PagePermissions PagePerm { get; private set; } = new PagePermissions();
    public List<SelectListItem> StoreGroups { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> Statuses { get; set; } = new List<SelectListItem>();
   public List<MaterialRequestListRowDto> Rows { get; set; } = new List<MaterialRequestListRowDto>();
    public bool CanCreateAutoMr => CanCreateAutoMrCore();
    public bool CanCreateMr => AllowCreate();

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
    // 1. PAGE LOAD (GET)
    // ==========================================
    /// <summary>
    /// Xu ly GET cho trang danh sach MaterialRequest.
    /// </summary>
    public void OnGet()
    {
        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
        int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");

        PagePerm = new PagePermissions();
        PagePerm = GetUserPermissions();
        LoadUserScopeAsync(cancellationToken).GetAwaiter().GetResult();
        if (!CanAccessPage())
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        ApplyTempDataMessagesToModelState();
        NormalizeConditionMode();
        PopulateCreateAutoDefaults(cancellationToken);
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

        ApplyDefaultInboxStatusIfNeeded();
        ApplyDefaultBuyFilterIfNeeded();
        ViewData["CanShowPcCondition"] = AllowShowPcCondition();
        ViewData["CanShowStoreCondition"] = AllowShowStoreCondition();
        ViewData["StoreGroupLocked"] = IsStoreGroupLocked();
        LoadFilters();
        ViewData["DefaultPageSize"] = DefaultPageSize;
    }

    // ==========================================
    // 2. AJAX SEARCH AND ITEM ACTIONS
    // ==========================================
    /// <summary>
    /// Tim danh sach MaterialRequest theo bo loc.
    /// </summary>
    /// <param name="request">Du lieu tim kiem tu client.</param>
    /// <returns>Ket qua JSON de render bang.</returns>
    public IActionResult OnPostSearch([FromBody] MaterialRequestSearchRequest request)
    {
        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
        int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");
        request ??= new MaterialRequestSearchRequest();
        Filter ??= new MaterialRequestFilter();

        PagePerm = new PagePermissions();
        PagePerm = GetUserPermissions();
        LoadUserScopeAsync(cancellationToken).GetAwaiter().GetResult();
        if (!CanAccessPage())
        {
            return new JsonResult(new { success = false, message = "Forbidden" })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }

        Filter.RequestNo = string.IsNullOrWhiteSpace(request.RequestNo) ? null : request.RequestNo.Trim();
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
        ApplyDefaultInboxStatusIfNeeded();

        var result = _materialRequestService.SearchPagedAsync(BuildCriteria(includePaging: true), cancellationToken).GetAwaiter().GetResult();
        var totalPages = Filter.PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(result.TotalCount / (double)Filter.PageSize));
        var currentEmployeeCode = User.Identity?.Name?.Trim() ?? string.Empty;

        var dataWithActions = result.Rows.Select(r =>
        {
            var statusId = r.MaterialStatusId ?? StatusJustCreated;
            var effectivePerms = PagePerm.AllowedNos;
            // Kiem tra status MR la Just Created va dung user dang login tao ra.
            var isDraftCreator = statusId == StatusJustCreated
                && !string.IsNullOrWhiteSpace(currentEmployeeCode)
                && string.Equals(r.DraftCreatorEmployeeCode, currentEmployeeCode, StringComparison.OrdinalIgnoreCase);

            // MR chi co the Edit khi status la Just Created va:
            // - hoac la draft cua user dang login
            // - hoac user co quyen edit MR (Permission 3 / admin)
            var canEditRow = statusId == StatusJustCreated
                && (effectivePerms.Contains(PermissionEdit) || isDraftCreator);
            var isAuto = r.IsAuto;
            var canApprove = CanApproveStatus(statusId, isAuto);
            var canReject = CanRejectStatus(statusId, isAuto);

            return new
            {
                data = r,
                actions = new
                {
                    canAccess = effectivePerms.Count > 0 || isDraftCreator,
                    accessMode = canEditRow ? "edit" : "view",

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

    // ==========================================
    // 3. MATERIAL REQUEST CREATION ACTIONS
    // ==========================================
    /// <summary>
    /// Lay danh sach item cho popup tim kiem.
    /// </summary>
    /// <param name="keyword">Tu khoa tim item theo ma hoac ten.</param>
    /// <param name="itemCode">Thong so cu de giu tuong thich.</param>
    /// <param name="itemName">Thong so cu de giu tuong thich.</param>
    /// <param name="checkBalanceInStore">Co kiem tra ton kho hay khong.</param>
    /// <returns>Ket qua JSON cho lookup.</returns>
    public IActionResult OnGetSearchItems(string? keyword, string? itemCode, string? itemName, bool checkBalanceInStore = false)
    {
        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
        int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");

        PagePerm = new PagePermissions();
        PagePerm = GetUserPermissions();
        LoadUserScopeAsync(cancellationToken).GetAwaiter().GetResult();
        if (!CanAccessPage())
        {
            return new JsonResult(new { success = false, message = "Access denied." });
        }

        var normalizedKeyword = !string.IsNullOrWhiteSpace(keyword)
            ? keyword
            : !string.IsNullOrWhiteSpace(itemCode)
                ? itemCode
                : itemName;
        var items = _materialRequestService.SearchItemsAsync(normalizedKeyword, checkBalanceInStore, cancellationToken).GetAwaiter().GetResult();

        return new JsonResult(new { success = true, data = items });
    }

    /// <summary>
    /// Tao MR thu cong tu popup.
    /// </summary>
    /// <returns>Chuyen ve man detail cua request moi.</returns>
    public IActionResult OnPostCreateMr()
    {
        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
        var operatorCode = User.Identity?.Name ?? string.Empty;
        int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");

        PagePerm = new PagePermissions();
        PagePerm = GetUserPermissions();
        LoadUserScopeAsync(cancellationToken).GetAwaiter().GetResult();
        if (!CanAccessPage() || !AllowCreate())
        {
            return Forbid();
        }

        if (IsStoreGroupLocked() && !_dataScope.StoreGroup.HasValue)
        {
            WarningMessage = "Store Group scope is required.";
            return RedirectToPage("./Index");
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
            MaterialStatusId = StatusJustCreated,
            NoIssue = 0,
            IsAuto = false,
            Approval = false,
            ApprovalEnd = false,
            PostPr = false
        };

        try
        {
            var requestNo = _materialRequestService.SaveAsync(null, header, lines, cancellationToken).GetAwaiter().GetResult();
            _materialRequestService.InsertSuperRequestAsync(
                requestNo,
                "Create MR",
                StatusJustCreated,
                operatorCode,
                cancellationToken).GetAwaiter().GetResult();
            SuccessMessage = "Material Request created successfully.";
            return RedirectToPage("./MaterialRequestDetail", new { id = requestNo, mode = "edit" });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Cannot create Material Request. {ex.Message}";
            return RedirectToPage("./Index");
        }
    }

    /// <summary>
    /// Tao Auto MR tu form Generate MR.
    /// Ham nay doc du lieu tu popup Generate MR, kiem tra scope hien tai cua user,
    /// lay dung Store Group dang ap dung cho user do, sau do goi service de tao
    /// header moi va fill detail bang rule legacy.
    /// </summary>
    /// <returns>Chuyen ve man detail cua request moi.</returns>
    public IActionResult OnPostCreateAuto()
    {
        var cancellationToken = HttpContext?.RequestAborted ?? CancellationToken.None;
        var operatorCode = User.Identity?.Name ?? string.Empty;
        int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");

        PagePerm = new PagePermissions();
        PagePerm = GetUserPermissions();
        // Doc scope truoc de biet Store Group nao dang ap dung cho user hien tai.
        LoadUserScopeAsync(cancellationToken).GetAwaiter().GetResult();
        if (!CanAccessPage() || !AllowCreateAuto())
        {
            return Forbid();
        }

        // Auto MR chi nam tren 1 Store Group. Neu scope bi khoa thi dung group hien tai,
        // con neu scope mo thi form van can co mot group dang duoc chon hop le.
        if (IsStoreGroupLocked() && !_dataScope.StoreGroup.HasValue)
        {
            WarningMessage = "Store Group scope is required.";
            return RedirectToPage("./Index");
        }

        var scopedStoreGroup = IsStoreGroupLocked()
            ? _dataScope.StoreGroup
            : (CreateAutoStoreGroup ?? Filter.StoreGroup);

        if (!scopedStoreGroup.HasValue || scopedStoreGroup.Value <= 0)
        {
            WarningMessage = "Please choose Store Group before creating Auto MR.";
            return RedirectToPage("./Index");
        }

        // Lay gia tri tu popup, neu nguoi dung khong nhap thi dung mac dinh an toan.
        var dateCreate = CreateAutoDateCreate ?? DateTime.Today;
        var fromDate = CreateAutoFromDate ?? dateCreate.AddMonths(-3);
        var toDate = CreateAutoToDate ?? dateCreate;
        // Theo form Generate MR, if user does not type text then use month text.
        var description = string.IsNullOrWhiteSpace(CreateAutoDescription)
            ? $"Auto MR {dateCreate:MM/yyyy}"
            : CreateAutoDescription.Trim();

        try
        {
            // Auto MR uses the old DB rule.
            var requestNo = _materialRequestService.CreateAutoRequestAsync(
                scopedStoreGroup.Value,
                dateCreate,
                fromDate,
                toDate,
                description,
                cancellationToken).GetAwaiter().GetResult();
            _materialRequestService.InsertSuperRequestAsync(
                requestNo,
                "CreateAT MR",
                StatusJustCreated,
                operatorCode,
                cancellationToken).GetAwaiter().GetResult();
            SuccessMessage = "Auto Material Request created successfully.";
            return RedirectToPage("./MaterialRequestDetail", new { id = requestNo, mode = "edit" });
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Cannot create Auto MR. {ex.Message}";
            return RedirectToPage("./Index");
        }
    }

    /// <summary>
    /// Tra ve lich su workflow cua MR dang duoc chon.
    /// </summary>
    public async Task<IActionResult> OnGetHistory(long requestNo, CancellationToken cancellationToken)
    {
        PagePerm = new PagePermissions();
        PagePerm = GetUserPermissions();
        LoadUserScopeAsync(cancellationToken).GetAwaiter().GetResult();
        if (!CanAccessPage() || requestNo <= 0)
        {
            return new JsonResult(new { success = false, message = "Forbidden" })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }

        var rows = await _materialRequestService.GetWorkflowHistoryAsync(requestNo, cancellationToken);
        return new JsonResult(new
        {
            success = true,
            requestNo,
            rows
        });
    }

    // ==========================================
    // 4. FILTER AND DROPDOWN HELPERS
    // ==========================================
    /// <summary>
    /// Nap danh sach bo loc va dropdown cho trang.
    /// </summary>
    private void LoadFilters()
    {
        var stores = LoadListFromSql(
            @"SELECT
                    kp.KPGroupID,
                    CASE
                        WHEN kp.KPGroupName IS NOT NULL AND dep.DeptCode IS NOT NULL THEN CONCAT(kp.KPGroupName, ' (', dep.DeptCode, ')')
                        ELSE NULL
                    END AS KPGroupName
              FROM dbo.INV_KPGroup kp
              LEFT JOIN dbo.MS_Department dep ON dep.DeptID = kp.DepID
              ORDER BY kp.KPGroupID",
            "KPGroupID",
            "KPGroupName");

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

        if (IsStoreGroupLocked())
        {
            Filter.StoreGroup = _dataScope.StoreGroup ?? NoScopeStoreGroup;
            if (_dataScope.StoreGroup.HasValue)
            {
                var selectedStore = stores.FirstOrDefault(x => string.Equals(x.Value, _dataScope.StoreGroup.Value.ToString(), StringComparison.OrdinalIgnoreCase));
                StoreGroups = new List<SelectListItem>
                {
                    new SelectListItem
                    {
                        Value = _dataScope.StoreGroup.Value.ToString(),
                        Text = selectedStore?.Text ?? string.Empty
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

    /// <summary>
    /// Nap gia tri mac dinh cho form Generate MR.
    /// </summary>
    /// <param name="cancellationToken">Token de huy yeu cau neu can.</param>
    private void PopulateCreateAutoDefaults(CancellationToken cancellationToken)
    {
        try
        {
            if (!CreateAutoRequestNoPreview.HasValue || CreateAutoRequestNoPreview.Value <= 0)
            {
                var connectionString = _config.GetConnectionString("DefaultConnection")
                    ?? throw new InvalidOperationException("Missing connection string: DefaultConnection");
                var previewValue = Helper.ExecuteScalarAsync(
                    connectionString,
                    "SELECT ISNULL(MAX(REQUEST_NO), 0) + 1 FROM dbo.MATERIAL_REQUEST",
                    cancellationToken: cancellationToken).GetAwaiter().GetResult();
                CreateAutoRequestNoPreview = Convert.ToInt64(previewValue ?? 0L);
            }
        }
        catch
        {
            CreateAutoRequestNoPreview = null;
        }

        if (!CreateAutoDateCreate.HasValue)
        {
            CreateAutoDateCreate = DateTime.Today;
        }

        if (!CreateAutoFromDate.HasValue)
        {
            CreateAutoFromDate = CreateAutoDateCreate.Value.AddMonths(-3);
        }

        if (!CreateAutoToDate.HasValue)
        {
            CreateAutoToDate = CreateAutoDateCreate.Value;
        }

        if (string.IsNullOrWhiteSpace(CreateAutoDescription))
        {
            CreateAutoDescription = $"Auto MR {CreateAutoDateCreate.Value:MM/yyyy}";
        }
    }

    // ==========================================
    // 5. SHARED DATA TRANSFORM HELPERS
    // ==========================================
    /// <summary>
    /// Nap du lieu grid theo bo loc hien tai.
    /// </summary>
    /// <param name="cancellationToken">Token de huy yeu cau neu can.</param>
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

    /// <summary>
    /// Tao criteria de query danh sach.
    /// </summary>
    /// <param name="includePaging">Co them phan trang hay khong.</param>
    /// <returns>Criteria cho query.</returns>
    private MaterialRequestFilterCriteria BuildCriteria(bool includePaging)
    {
        var scopedStoreGroup = IsStoreGroupLocked()
            ? (_dataScope.StoreGroup ?? NoScopeStoreGroup)
            : Filter.StoreGroup;
        var isStoremanMode = AllowShowStoreCondition() && string.Equals(Filter.ConditionMode, ConditionModeStoreman, StringComparison.OrdinalIgnoreCase);

        return new MaterialRequestFilterCriteria
        {
            RequestNo = isStoremanMode || string.IsNullOrWhiteSpace(Filter.RequestNo) ? null : Filter.RequestNo.Trim(),
            StoreGroup = scopedStoreGroup,
            StatusIds = isStoremanMode ? new List<int>() : (Filter.StatusIds ?? new List<int>()),
            ItemCode = isStoremanMode && !string.IsNullOrWhiteSpace(Filter.ItemCode) ? Filter.ItemCode.Trim() : null,
            NoIssue = isStoremanMode && Filter.IssueLessThanOrder ? 1 : null,
            // Check = show Auto MR only. Uncheck = hide Auto MR and keep non-auto rows.
            IsAuto = isStoremanMode ? null : (Filter.AutoOnly ? true : false),
            BuyGreaterThanZero = isStoremanMode ? null : (Filter.BuyGreaterThanZero ? true : null),
            FromDate = Filter.FromDate?.Date,
            ToDate = Filter.ToDate?.Date,
            AccordingToKeyword = string.IsNullOrWhiteSpace(Filter.AccordingToKeyword) ? null : Filter.AccordingToKeyword.Trim(),
            PageIndex = includePaging ? Filter.PageIndex : null,
            PageSize = includePaging ? Filter.PageSize : null
        };
    }

    /// <summary>
    /// Chuan hoa mode loc cua trang.
    /// </summary>
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

    /// <summary>
    /// Nap scope va quyen cua user hien tai.
    /// </summary>
    /// <param name="cancellationToken">Token de huy yeu cau neu can.</param>
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

    /// <summary>
    /// Lay quyen page cua user.
    /// </summary>
    /// <returns>Thong tin quyen trang.</returns>
    private PagePermissions GetUserPermissions()
    {
        bool isAdmin = IsAdminUser();
        int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");

        var permsObj = new PagePermissions();

        if (isAdmin)
        {
            permsObj.AllowedNos = Enumerable.Range(1, 20).ToList();
        }
        else
        {
            permsObj.AllowedNos = _permissionService.GetPermissionsForPage(roleId, MaterialRequestFunctionId);
        }

        return permsObj;
    }

    /// <summary>
    /// Kiem tra user co la admin hay khong.
    /// </summary>
    /// <returns>True neu la admin.</returns>
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
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand(@"
                SELECT TOP 1 ISNULL(IsAdminUser, 0)
                FROM dbo.MS_Employee
                WHERE EmployeeCode = @EmployeeCode", conn);
            cmd.Parameters.Add("@EmployeeCode", SqlDbType.VarChar, 50).Value = employeeCode;
            conn.Open();
            var value = cmd.ExecuteScalar();
            if (value == null || value == DBNull.Value)
            {
                using var roleCmd = new SqlCommand(@"
                    SELECT TOP 1 ISNULL(r.IsAdminRole, 0)
                    FROM dbo.SYS_RoleMember rm
                    INNER JOIN dbo.SYS_Role r ON r.RoleID = rm.RoleID
                    INNER JOIN dbo.MS_Employee e ON e.EmployeeID = rm.Operator
                    WHERE e.EmployeeCode = @EmployeeCode
                    ORDER BY r.IsAdminRole DESC, rm.RoleID ASC", conn);
                roleCmd.Parameters.Add("@EmployeeCode", SqlDbType.VarChar, 50).Value = employeeCode;
                var roleValue = roleCmd.ExecuteScalar();
                if (roleValue == null || roleValue == DBNull.Value)
                {
                    return false;
                }

                return Convert.ToBoolean(roleValue);
            }

            return Convert.ToBoolean(value);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Kiem tra user co duoc vao trang hay khong.
    /// </summary>
    /// <returns>True neu co quyen vao trang.</returns>
    private bool CanAccessPage()
    {
        return User.Identity?.IsAuthenticated == true
            || IsAdminUser()
            || HasPermission(PermissionCreateAuto)
            || HasPermission(PermissionEdit)
            || HasPermission(PermissionShowAll)
            || HasPermission(PermissionShowPc)
            || HasPermission(PermissionShowStore)
            || HasPermission(7);
    }

    /// <summary>
    /// Kiem tra user co quyen so can dung hay khong.
    /// </summary>
    /// <param name="permissionNo">So quyen can kiem tra.</param>
    /// <returns>True neu co quyen.</returns>
    private bool HasPermission(int permissionNo)
    {
        return PagePerm.HasPermission(permissionNo);
    }

    /// <summary>
    /// Kiem tra co hien nut Create MR hay khong.
    /// </summary>
    /// <returns>True neu duoc tao MR thu cong.</returns>
    private bool AllowCreate()
    {
        return CanCreateDraftMaterialRequest();
    }

    /// <summary>
    /// Kiem tra co hien nut CreateAT hay khong.
    /// </summary>
    /// <returns>True neu duoc tao Auto MR.</returns>
    private bool AllowCreateAuto()
    {
        return CanCreateAutoMrCore();
    }

    /// <summary>
    /// Kiem tra dieu kien nghiep vu de tao Auto MR.
    /// </summary>
    /// <returns>True neu user duoc tao Auto MR.</returns>
    private bool CanCreateAutoMrCore()
    {
        return IsAdminUser()
            || (_dataScope.IsInventoryControlInDep && _dataScope.StoreGroup.HasValue);
    }

    /// <summary>
    /// Check if the user can create or edit a draft MR.
    /// Normal scoped users are allowed. Workflow users need Edit permission.
    /// </summary>
    /// <returns>True if draft MR actions are allowed.</returns>
    private bool CanCreateDraftMaterialRequest()
    {
        if (IsAdminUser())
        {
            return true;
        }

        var isWorkflowUser = _dataScope.IsHeadDept
            || _dataScope.IsPurchaser
            || _dataScope.IsCFO
            || _dataScope.IsBOD
            || _dataScope.ApprovalLevel >= 2;

        if (!isWorkflowUser)
        {
            return User.Identity?.IsAuthenticated == true;
        }

        return HasPermission(PermissionEdit);
    }

    /// <summary>
    /// Kiem tra co duoc sua MR hay khong.
    /// </summary>
    /// <returns>True neu duoc edit.</returns>
    private bool AllowEdit()
    {
        return IsAdminUser() || HasPermission(PermissionEdit);
    }

    /// <summary>
    /// Kiem tra co xem duoc tat ca MR hay khong.
    /// </summary>
    /// <returns>True neu duoc xem all MR.</returns>
    private bool CanShowAllRequests()
    {
        return IsAdminUser()
            || HasPermission(PermissionShowAll);
    }

    /// <summary>
    /// Kiem tra co mo het Store Group hay khong.
    /// </summary>
    /// <returns>True neu duoc mo scope Store Group.</returns>
    private bool CanAccessAllStoreGroups()
    {
        return IsAdminUser()
            || _dataScope.IsPurchaser
            || _dataScope.IsCFO
            || _dataScope.IsBOD
            || _dataScope.ApprovalLevel >= 2;
    }

    /// <summary>
    /// Kiem tra dieu kien xem nhom PC.
    /// </summary>
    /// <returns>True neu co the xem PC condition.</returns>
    private bool AllowShowPcCondition()
    {
        return User.Identity?.IsAuthenticated == true;
    }

    /// <summary>
    /// Kiem tra dieu kien xem nhom Store.
    /// </summary>
    /// <returns>True neu co the xem Store condition.</returns>
    private bool AllowShowStoreCondition()
    {
        return IsAdminUser() || HasPermission(PermissionShowStore);
    }

    /// <summary>
    /// Kiem tra Store Group co bi khoa theo scope khong.
    /// </summary>
    /// <returns>True neu Store Group dang bi khoa.</returns>
    private bool IsStoreGroupLocked()
    {
        return !CanAccessAllStoreGroups();
    }

    /// <summary>
    /// Day message tu TempData vao ModelState.
    /// </summary>
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

    /// <summary>
    /// Dat status inbox mac dinh neu user chua chon gi.
    /// </summary>
    private void ApplyDefaultInboxStatusIfNeeded()
    {
        if (Filter.StatusIds is { Count: > 0 })
        {
            return;
        }

        Filter.StatusIds = new List<int> { GetDefaultInboxStatusId() };
    }

    /// <summary>
    /// Bat loc Buy > 0 mac dinh cho CFO / cap approval cao khi page vua load.
    /// User van co the bo chon sau khi form da hien ra.
    /// </summary>
    private void ApplyDefaultBuyFilterIfNeeded()
    {
        if (Request.Query.ContainsKey("Filter.BuyGreaterThanZero"))
        {
            return;
        }

        if (_dataScope.IsCFO || _dataScope.ApprovalLevel >= 3)
        {
            Filter.BuyGreaterThanZero = true;
        }
    }

    /// <summary>
    /// Lay status inbox mac dinh theo role.
    /// </summary>
    /// <returns>Status mac dinh.</returns>
    private int GetDefaultInboxStatusId()
    {
        if (_dataScope.IsHeadDept)
        {
            return 0;
        }

        if (_dataScope.IsCFO || _dataScope.ApprovalLevel >= 3)
        {
            return 2;
        }

        if (_dataScope.IsPurchaser || _dataScope.ApprovalLevel >= 2)
        {
            return 1;
        }

        return StatusJustCreated;
    }

    /// <summary>
    /// Kiem tra status nao duoc phe duyet.
    /// </summary>
    /// <param name="statusId">Status cua MR.</param>
    /// <param name="isAuto">True neu la Auto MR.</param>
    /// <returns>True neu duoc approve.</returns>
    private bool CanApproveStatus(int statusId, bool isAuto)
    {
        if (_isAdminRole)
        {
            return true;
        }

        return statusId switch
        {
            1 => _dataScope.IsHeadDept,
            2 => _dataScope.IsPurchaser,
            3 => !isAuto && _dataScope.IsCFO,
            _ => false
        };
    }

    /// <summary>
    /// Kiem tra status nao duoc reject.
    /// </summary>
    /// <param name="statusId">Status cua MR.</param>
    /// <param name="isAuto">True neu la Auto MR.</param>
    /// <returns>True neu duoc reject.</returns>
    private bool CanRejectStatus(int statusId, bool isAuto)
    {
        if (_isAdminRole)
        {
            return true;
        }
        return statusId switch
        {
            1 => _dataScope.IsHeadDept,
            2 => _dataScope.IsPurchaser,
            3 => !isAuto && _dataScope.IsCFO,
            _ => false
        };
    }

    /// <summary>
    /// Doc danh sach item tu JSON cua form tao MR.
    /// </summary>
    /// <param name="json">Chuoi JSON tu client.</param>
    /// <returns>Danh sach item da doc.</returns>
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

    /// <summary>
    /// Doi item lookup sang detail line.
    /// </summary>
    /// <param name="items">Danh sach item lookup.</param>
    /// <returns>Danh sach line de save.</returns>
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
                OrderQty = item.OrderQty.HasValue && item.OrderQty.Value > 0 ? item.OrderQty.Value : 1m,
                NotReceipt = 0,
                InStock = item.InStock,
                AccIn = 0,
                Buy = 0,
                NormMain = 0,
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
    public string? RequestNo { get; set; }
    public int? StoreGroup { get; set; }
    /// <summary>
    /// Ham List<int>.
    /// </summary>
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
    public string? RequestNo { get; set; }
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

public class MaterialRequestLookupOptionDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
}

public class MaterialRequestFilterCriteria
{
    public string? RequestNo { get; set; }
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
    /// <summary>
    /// Ham List<MaterialRequestListRowDto>.
    /// </summary>
    public List<MaterialRequestListRowDto> Rows { get; set; } = new List<MaterialRequestListRowDto>();
    public int TotalCount { get; set; }
}

public class MaterialRequestListRowDto
{
    public long RequestNo { get; set; }
    public int? StoreGroup { get; set; }
    public string KPGroupName { get; set; } = string.Empty;
    public string DraftCreatorEmployeeCode { get; set; } = string.Empty;
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
    public decimal? NormMain { get; set; }
    public decimal? Price { get; set; }

    [StringLength(255, ErrorMessage = "Note must be at most 255 characters.")]
    public string? Note { get; set; }

    public bool NewItem { get; set; }
    public bool Selected { get; set; }
    public decimal? Issued { get; set; }
}

public class MaterialRequestLineReadonlySnapshotDto
{
    public string ItemCode { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public decimal OrderQty { get; set; }
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
    public bool IsPurchaser { get; set; }
    public bool IsCFO { get; set; }
    public bool IsBOD { get; set; }
    public bool IsInventoryControlInDep { get; set; }
    public bool SeeDataAllDept { get; set; }
}

public class MaterialRequestItemLookupDto
{
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal InStock { get; set; }
    public int? StoreGroupId { get; set; }
    public decimal? OrderQty { get; set; }
}

public class MaterialRequestWorkflowHistoryDto
{
    public int AutoNum { get; set; }
    public long RequestNo { get; set; }
    public int? EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTime? TimeEffective { get; set; }
    public int? TypeEffective { get; set; }
    public string StatusLabel { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;

    public string TimeEffectiveDisplay => TimeEffective.HasValue ? TimeEffective.Value.ToString("dd/MM/yyyy HH:mm") : string.Empty;

    public string EmployeeDisplayName
    {
        get
        {
            var employeeName = (EmployeeName ?? string.Empty).Trim();
            var employeeCode = (EmployeeCode ?? string.Empty).Trim();

            if (!string.IsNullOrWhiteSpace(employeeName))
            {
                return string.IsNullOrWhiteSpace(employeeCode)
                    ? employeeName
                    : $"{employeeName} ({employeeCode})";
            }

            return string.IsNullOrWhiteSpace(employeeCode) ? string.Empty : employeeCode;
        }
    }

    public string ActionLabel => string.IsNullOrWhiteSpace(Note) ? StatusLabel : Note;

}

public class MaterialRequestService
{
    private readonly string _connectionString;
    /// <summary>
    /// Ham MaterialRequestService.
    /// </summary>
    public MaterialRequestService(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("Missing connection string: DefaultConnection");
    }

    /// <summary>
    /// Lay scope cua employee cho MaterialRequest.
    /// </summary>
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
                ISNULL(IsPurchaser, 0) AS IsPurchaser,
                ISNULL(IsCFO, 0) AS IsCFO,
                ISNULL(IsBOD, 0) AS IsBOD,
                ISNULL(IsInventoryControlInDep, 0) AS IsInventoryControlInDep,
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
                IsPurchaser = !rd.IsDBNull(3) && Convert.ToInt32(rd[3]) == 1,
                IsCFO = !rd.IsDBNull(4) && Convert.ToInt32(rd[4]) == 1,
                IsBOD = !rd.IsDBNull(5) && Convert.ToInt32(rd[5]) == 1,
                IsInventoryControlInDep = !rd.IsDBNull(6) && Convert.ToInt32(rd[6]) == 1,
                SeeDataAllDept = !rd.IsDBNull(7) && Convert.ToInt32(rd[7]) == 1
            },
            cmd => Helper.AddParameter(cmd, "@EmployeeCode", employeeCode.Trim(), SqlDbType.VarChar, 50),
            cancellationToken) ?? new EmployeeMaterialScopeDto();
    }

    /// <summary>
    /// Lay danh sach Store Group.
    /// </summary>
    public async Task<IReadOnlyList<MaterialRequestLookupOptionDto>> GetStoreGroupsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                kp.KPGroupID,
                CASE
                    WHEN kp.KPGroupName IS NOT NULL AND dep.DeptCode IS NOT NULL THEN CONCAT(kp.KPGroupName, ' (', dep.DeptCode, ')')
                    ELSE NULL
                END AS KPGroupName
            FROM dbo.INV_KPGroup kp
            LEFT JOIN dbo.MS_Department dep ON dep.DeptID = kp.DepID
            ORDER BY KPGroupID";

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

    /// <summary>
    /// Lay danh sach status MR.
    /// </summary>
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

    /// <summary>
    /// Lay lich su workflow cua MR tu SUPER_REQUEST.
    /// </summary>
    public async Task<IReadOnlyList<MaterialRequestWorkflowHistoryDto>> GetWorkflowHistoryAsync(long requestNo, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                sr.AutoNum,
                CAST(sr.RequestNo AS bigint) AS RequestNo,
                sr.EmployeeID,
                ISNULL(LTRIM(RTRIM(e.EmployeeCode)), '') AS EmployeeCode,
                ISNULL(LTRIM(RTRIM(e.EmployeeName)), '') AS EmployeeName,
                ISNULL(LTRIM(RTRIM(e.Title)), '') AS Title,
                sr.TimeEffective,
                sr.TypeEffective,
                ISNULL(LTRIM(RTRIM(sr.Note)), '') AS Note
            FROM dbo.SUPER_REQUEST sr
            LEFT JOIN dbo.MS_Employee e ON e.EmployeeID = sr.EmployeeID
            WHERE sr.RequestNo = @RequestNo
            ORDER BY sr.AutoNum";

        return await Helper.QueryAsync(
            _connectionString,
            sql,
            rd =>
            {
                var typeEffective = rd.IsDBNull(7) ? (int?)null : Convert.ToInt32(rd[7]);
                var note = rd[8]?.ToString() ?? string.Empty;

                return new MaterialRequestWorkflowHistoryDto
                {
                    AutoNum = rd.IsDBNull(0) ? 0 : Convert.ToInt32(rd[0]),
                    RequestNo = rd.IsDBNull(1) ? 0 : Convert.ToInt64(rd[1]),
                    EmployeeId = rd.IsDBNull(2) ? null : Convert.ToInt32(rd[2]),
                    EmployeeCode = rd[3]?.ToString() ?? string.Empty,
                    EmployeeName = rd[4]?.ToString() ?? string.Empty,
                    Title = rd[5]?.ToString() ?? string.Empty,
                    TimeEffective = rd.IsDBNull(6) ? null : rd.GetDateTime(6),
                    TypeEffective = typeEffective,
                    StatusLabel = ResolveWorkflowStatusLabel(typeEffective),
                    Note = note
                };
            },
            cmd => AddNumeric18_0Param(cmd, "@RequestNo", requestNo),
            cancellationToken);
    }

    /// <summary>
    /// Tao Auto MR theo rule cu: lay so moi, tao header moi, roi goi HaiGenMR de fill detail.
    /// </summary>
    /// <param name="storeGroup">Store Group hien tai duoc dung de tao Auto MR.</param>
    /// <param name="dateCreate">Ngay tao MR.</param>
    /// <param name="fromDate">Ngay bat dau ky lay lieu.</param>
    /// <param name="toDate">Ngay ket thuc ky lay lieu.</param>
    /// <param name="accordingTo">Noi dung According/Description cua MR.</param>
    /// <param name="cancellationToken">Token de huy yeu cau neu can.</param>
    /// <returns>Request No moi vua tao.</returns>
    public async Task<long> CreateAutoRequestAsync(
        int storeGroup,
        DateTime dateCreate,
        DateTime fromDate,
        DateTime toDate,
        string accordingTo,
        CancellationToken cancellationToken = default)
    {
        // Legacy luong nay can giu request no, header insert, va proc fill detail trong cung transaction.
        var normalizedAccording = string.IsNullOrWhiteSpace(accordingTo)
            ? $"Auto MR {dateCreate:MM/yyyy}"
            : accordingTo.Trim();

        if (normalizedAccording.Length > 50)
        {
            normalizedAccording = normalizedAccording[..50];
        }

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        try
        {
            // Xin Request No tiep theo trong cung transaction de tranh trung so khi nhieu nguoi bam Generate cung luc.
            var requestNo = await GetNextRequestNoAsync(conn, (SqlTransaction)tx, cancellationToken);

            // HaiGenMR khong tu tao header. No can mot dong header san co de update lai sau do fill detail.
            const string insertHeaderSql = @"
                INSERT INTO dbo.MATERIAL_REQUEST
                (
                    REQUEST_NO, STORE_GROUP, DATE_CREATE, ACCORDINGTO, APPROVAL, POST_PR, IS_AUTO,
                    FROM_DATE, TO_DATE, APPROVAL_END, MATERIALSTATUSID, PRNO, NO_ISSUE
                )
                VALUES
                (
                    @RequestNo, @StoreGroup, @DateCreate, @AccordingTo, 0, 0, 1,
                    @FromDate, @ToDate, 0, -1, NULL, 0
                )";

            await using (var insertCmd = new SqlCommand(insertHeaderSql, conn, (SqlTransaction)tx))
            {
                AddNumeric18_0Param(insertCmd, "@RequestNo", requestNo);
                AddNumeric18_0Param(insertCmd, "@StoreGroup", storeGroup);
                AddDateTimeParam(insertCmd, "@DateCreate", dateCreate);
                Helper.AddParameter(insertCmd, "@AccordingTo", normalizedAccording, SqlDbType.VarChar, 50);
                AddDateTimeParam(insertCmd, "@FromDate", fromDate);
                AddDateTimeParam(insertCmd, "@ToDate", toDate);
                await insertCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // Goi proc legacy de tinh va insert detail lines theo dung rule cu cua DB.
            // Proc nay se update header ben tren va do ra cac dong detail can mua.
            await using (var procCmd = new SqlCommand("dbo.HaiGenMR", conn, (SqlTransaction)tx))
            {
                procCmd.CommandType = CommandType.StoredProcedure;
                AddNumeric18_0Param(procCmd, "@ReQuestNo", requestNo);
                AddDateTimeParam(procCmd, "@DateCreate", dateCreate);
                Helper.AddParameter(procCmd, "@According", normalizedAccording, SqlDbType.VarChar, 50);
                AddDateTimeParam(procCmd, "@FromDate", fromDate);
                AddDateTimeParam(procCmd, "@ToDate", toDate);
                AddNumeric18_0Param(procCmd, "@KPGroup", storeGroup);
                AddNumeric18_0Param(procCmd, "@MainGRStore", 1);

                await procCmd.ExecuteNonQueryAsync(cancellationToken);
            }

            // Chi commit khi ca header insert va proc fill detail deu thanh cong.
            await tx.CommitAsync(cancellationToken);
            return requestNo;
        }
        catch
        {
            // Neu co loi o bat ky buoc nao thi rollback het de khong de lai MR do.
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Tim danh sach MR theo bo loc va phan trang.
    /// </summary>
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
            OUTER APPLY (
                SELECT TOP 1 LTRIM(RTRIM(e.EmployeeCode)) AS EmployeeCode
                FROM dbo.SUPER_REQUEST sr
                INNER JOIN dbo.MS_Employee e ON e.EmployeeID = sr.EmployeeID
                WHERE sr.RequestNo = r.REQUEST_NO
                  AND sr.TypeEffective = -1
                ORDER BY sr.AutoNum ASC
            ) draftCreator
            WHERE
                (@RequestNo IS NULL OR CAST(r.REQUEST_NO AS varchar(50)) LIKE '%' + @RequestNo + '%')
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
                kp.KPGroupName AS KPGroupName,
                ISNULL(draftCreator.EmployeeCode, '') AS DraftCreatorEmployeeCode,
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
                DraftCreatorEmployeeCode = rd[3]?.ToString() ?? string.Empty,
                DateCreate = rd.IsDBNull(4) ? null : rd.GetDateTime(4),
                FromDate = rd.IsDBNull(5) ? null : rd.GetDateTime(5),
                ToDate = rd.IsDBNull(6) ? null : rd.GetDateTime(6),
                AccordingTo = rd[7]?.ToString() ?? string.Empty,
                Approval = !rd.IsDBNull(8) && Convert.ToBoolean(rd[8]),
                ApprovalEnd = !rd.IsDBNull(9) && Convert.ToBoolean(rd[9]),
                IsAuto = !rd.IsDBNull(10) && Convert.ToBoolean(rd[10]),
                PostPr = !rd.IsDBNull(11) && Convert.ToBoolean(rd[11]),
                MaterialStatusId = rd.IsDBNull(12) ? null : Convert.ToInt32(rd[12]),
                MaterialStatusName = rd[13]?.ToString() ?? string.Empty,
                NoIssue = rd.IsDBNull(14) ? null : Convert.ToInt32(rd[14]),
                PrNo = rd[15]?.ToString() ?? string.Empty
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

    /// <summary>
    /// Lay header cua MR.
    /// </summary>
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

    /// <summary>
    /// Lay danh sach detail line cua MR.
    /// </summary>
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
                ISNULL(d.NORM_Q_MAIN, 0) AS NORM_Q_MAIN,
                d.PRICE,
                d.NOTE,
                ISNULL(d.NEW_ITEM, 0) AS NEW_ITEM,
                ISNULL(d.SELECTED, 0) AS SELECTED,
                ISNULL(d.ISSUED, 0) AS ISSUED
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
                NormMain = rd.IsDBNull(9) ? null : Convert.ToDecimal(rd[9]),
                Price = rd.IsDBNull(10) ? null : Convert.ToDecimal(rd[10]),
                Note = rd[11]?.ToString(),
                NewItem = !rd.IsDBNull(12) && Convert.ToBoolean(rd[12]),
                Selected = !rd.IsDBNull(13) && Convert.ToBoolean(rd[13]),
                Issued = rd.IsDBNull(14) ? null : Convert.ToDecimal(rd[14])
            },
            cmd => AddNumeric18_0Param(cmd, "@RequestNo", requestNo),
            cancellationToken);

        return rows;
    }

    /// <summary>
    /// Lay snapshot chi doc cua cac line da luu.
    /// </summary>
    public async Task<Dictionary<string, MaterialRequestLineReadonlySnapshotDto>> GetLineReadonlySnapshotsAsync(long requestNo, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                ITEMCODE,
                ISNULL(UNIT, '') AS UNIT,
                ISNULL(NOTE, '') AS NOTE,
                ISNULL(NEW_ORDER, 0),
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
                Note = rd[2]?.ToString() ?? string.Empty,
                OrderQty = rd.IsDBNull(3) ? 0m : Convert.ToDecimal(rd[3]),
                NotReceipt = rd.IsDBNull(4) ? 0m : Convert.ToDecimal(rd[4]),
                InStock = rd.IsDBNull(5) ? 0m : Convert.ToDecimal(rd[5]),
                AccIn = rd.IsDBNull(6) ? 0m : Convert.ToDecimal(rd[6]),
                Buy = rd.IsDBNull(7) ? 0m : Convert.ToDecimal(rd[7])
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

    /// <summary>
    /// Tinh lai cac line cho Purchaser.
    /// </summary>
    public async Task CalculatePurchaserLinesAsync(
        long requestNo,
        int storeGroup,
        IList<MaterialRequestLineDto> lines,
        CancellationToken cancellationToken = default)
    {
        if (lines.Count == 0)
        {
            return;
        }

        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);

        foreach (var line in lines)
        {
            var itemCode = (line.ItemCode ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(itemCode))
            {
                continue;
            }

            if (itemCode.StartsWith("ORDER", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var normMain = 0m;
            var normDept = 0m;
            var rawInventory = 0m;
            var reservedQty = 0m;
            int? itemId = null;

            await using (var itemCmd = new SqlCommand(@"
                SELECT TOP 1
                    i.ItemID,
                    CAST(COALESCE(NULLIF(i.StoreNormInMain, 0), NULLIF(i.ReOrderPoint, 0), 1) AS decimal(18,2)) AS NormMain,
                    CAST(ISNULL(kgi.ReOrderPoint, 0) AS decimal(18,2)) AS NormDept
                FROM dbo.INV_ItemList i
                LEFT JOIN dbo.INV_KPGroupIndex kgi
                    ON kgi.ItemID = i.ItemID
                   AND kgi.KPGroupID = @StoreGroup
                WHERE i.ItemCode = @ItemCode", conn))
            {
                Helper.AddParameter(itemCmd, "@ItemCode", itemCode, SqlDbType.VarChar, 20);
                Helper.AddParameter(itemCmd, "@StoreGroup", storeGroup, SqlDbType.Int);

                await using var reader = await itemCmd.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    itemId = reader.IsDBNull(0) ? (int?)null : Convert.ToInt32(reader[0]);
                    normMain = reader.IsDBNull(1) ? 0m : Convert.ToDecimal(reader[1]);
                    normDept = reader.IsDBNull(2) ? 0m : Convert.ToDecimal(reader[2]);
                }
            }

            if (itemId.HasValue)
            {
                rawInventory = await GetMainInventoryAsync(conn, itemId.Value, storeGroup, cancellationToken);
            }

            reservedQty = await GetReservedQtyAsync(conn, requestNo, itemCode, cancellationToken);

            line.NormMain = normMain;
            line.NotReceipt = reservedQty;
            line.AccIn = rawInventory;
            line.InStock = rawInventory - reservedQty;
            line.Buy = (line.OrderQty ?? 0m) + normMain + normDept - (line.InStock ?? 0m);
        }
    }

    /// <summary>
    /// Lay don vi cua item.
    /// </summary>
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

    /// <summary>
    /// Lay ton kho chinh cua item.
    /// </summary>
    private static async Task<decimal> GetMainInventoryAsync(SqlConnection conn, int itemId, int storeGroup, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT
                ISNULL(bg.BGQty, 0)
                + ISNULL(rcv.RecQty, 0)
                - ISNULL(iss.IssQty, 0) AS EndQty
            FROM
                (
                    SELECT SUM(bg.BGQuantity) AS BGQty
                    FROM dbo.INV_ItemStoreBG bg
                    INNER JOIN dbo.INV_StoreList sl ON bg.StoreID = sl.StoreID
                    INNER JOIN dbo.INV_KPGroup kp ON sl.DeptID = kp.KPGroupID
                    WHERE bg.[Year] = YEAR(GETDATE())
                      AND bg.ItemID = @ItemID
                      AND kp.KPGroupID = @StoreGroup
                      AND sl.StoreID <> 23
                      AND sl.StoreID <> 24
                ) bg
            CROSS APPLY
                (
                    SELECT SUM(fd.Act_Qty) AS RecQty
                    FROM dbo.INV_ItemFlowDetail fd
                    INNER JOIN dbo.INV_ItemFlow f ON fd.FlowID = f.FlowID
                    INNER JOIN dbo.INV_StoreList sl ON f.ToStore = sl.StoreID
                    INNER JOIN dbo.INV_KPGroup kp ON sl.DeptID = kp.KPGroupID
                    WHERE (f.FlowType = 2 OR f.FlowType = 3)
                      AND YEAR(f.FlowDate) = YEAR(GETDATE())
                      AND fd.ItemID = @ItemID
                      AND kp.KPGroupID = @StoreGroup
                      AND sl.StoreID <> 23
                      AND sl.StoreID <> 24
                ) rcv
            CROSS APPLY
                (
                    SELECT SUM(fd.Act_Qty) AS IssQty
                    FROM dbo.INV_ItemFlowDetail fd
                    INNER JOIN dbo.INV_ItemFlow f ON fd.FlowID = f.FlowID
                    INNER JOIN dbo.INV_StoreList sl ON f.FromStore = sl.StoreID
                    INNER JOIN dbo.INV_KPGroup kp ON sl.DeptID = kp.KPGroupID
                    WHERE (f.FlowType = 1 OR f.FlowType = 3)
                      AND YEAR(f.FlowDate) = YEAR(GETDATE())
                      AND fd.ItemID = @ItemID
                      AND kp.KPGroupID = @StoreGroup
                      AND sl.StoreID <> 23
                      AND sl.StoreID <> 24
                ) iss";

        await using var cmd = new SqlCommand(sql, conn);
        Helper.AddParameter(cmd, "@ItemID", itemId, SqlDbType.Int);
        Helper.AddParameter(cmd, "@StoreGroup", storeGroup, SqlDbType.Int);

        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return value == null || value == DBNull.Value ? 0m : Convert.ToDecimal(value);
    }

    /// <summary>
    /// Lay so luong da giu o cac MR khac.
    /// </summary>
    private static async Task<decimal> GetReservedQtyAsync(SqlConnection conn, long requestNo, string itemCode, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT ISNULL(SUM(
                CASE
                    WHEN ISNULL(d.NEW_ORDER, 0) - ISNULL(d.ISSUED, 0) > 0
                        THEN ISNULL(d.NEW_ORDER, 0) - ISNULL(d.ISSUED, 0)
                    ELSE 0
                END
            ), 0)
            FROM dbo.MATERIAL_REQUEST r
            INNER JOIN dbo.MATERIAL_REQUEST_DETAIL d ON r.REQUEST_NO = d.REQUEST_NO
            WHERE d.ITEMCODE = @ItemCode
              AND r.REQUEST_NO <> @RequestNo
              AND ISNULL(r.NO_ISSUE, 0) = 0
              AND r.MATERIALSTATUSID BETWEEN 2 AND 4";

        await using var cmd = new SqlCommand(sql, conn);
        AddNumeric18_0Param(cmd, "@RequestNo", requestNo);
        Helper.AddParameter(cmd, "@ItemCode", itemCode, SqlDbType.VarChar, 20);

        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return value == null || value == DBNull.Value ? 0m : Convert.ToDecimal(value);
    }

    /// <summary>
    /// Luu MR vao database.
    /// </summary>
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
                        @NewItem, @Selected, @NormMain, @Price, @Note, 0, 0, 0, 0
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
                    AddDecimal18_2Param(lineCmd, "@NormMain", line.NormMain ?? 0m);
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

    /// <summary>
    /// Ghi 1 dong lich su workflow vao SUPER_REQUEST.
    /// </summary>
    public async Task InsertSuperRequestAsync(long requestNo, string note, int typeEffective, string operatorCode, CancellationToken cancellationToken = default)
    {
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        try
        {
            var employeeId = await ResolveEmployeeIdAsync(conn, (SqlTransaction)tx, operatorCode, cancellationToken);
            await InsertSuperRequestAsync(
                conn,
                (SqlTransaction)tx,
                requestNo,
                employeeId,
                DateTime.Now,
                note,
                typeEffective,
                cancellationToken);

            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Xu ly reject theo flow legacy.
    /// </summary>
    public async Task<long> ProcessLegacyRejectAsync(
        MaterialRequestDetailDto sourceHeader,
        IReadOnlyList<MaterialRequestLineDto> sourceLines,
        IReadOnlyCollection<int> selectedLineIds,
        string? rejectReason,
        string operatorCode,
        CancellationToken cancellationToken = default)
    {
        if (!sourceHeader.StoreGroup.HasValue)
        {
            throw new InvalidOperationException("Store Group scope is required.");
        }

        var normalizedLineIds = selectedLineIds
            .Where(x => x > 0)
            .Distinct()
            .ToList();

            if (normalizedLineIds.Count == 0)
            {
                throw new InvalidOperationException("Please select item row(s) to reject.");
            }

        var selectedLines = sourceLines
            .Where(line => line.Id.HasValue && normalizedLineIds.Contains(line.Id.Value))
            .ToList();

        if (selectedLines.Count == 0)
        {
            throw new InvalidOperationException("Please select item row(s) to reject.");
        }

        var normalizedSourceLines = NormalizeLines(sourceLines);
        var currentStatus = sourceHeader.MaterialStatusId ?? -1;
        await using var conn = new SqlConnection(_connectionString);
        await conn.OpenAsync(cancellationToken);
        await using var tx = await conn.BeginTransactionAsync(cancellationToken);

        try
        {
            var employeeId = await ResolveEmployeeIdAsync(conn, (SqlTransaction)tx, operatorCode, cancellationToken);
            var allSelected = selectedLines.Count == normalizedSourceLines.Count;

            if (normalizedSourceLines.Count == 1 || allSelected)
            {
                await UpdateHeaderStatusOnlyAsync(
                    conn,
                    (SqlTransaction)tx,
                    sourceHeader.RequestNo,
                    5,
                    cancellationToken);
                await InsertSuperRequestAsync(
                    conn,
                    (SqlTransaction)tx,
                    sourceHeader.RequestNo,
                    employeeId,
                    DateTime.Now,
                    "Reject request",
                    5,
                    cancellationToken);
            }
            else
            {
                var rejectedNo = await FindRejectedPoolRequestNoAsync(
                    conn,
                    (SqlTransaction)tx,
                    sourceHeader.StoreGroup.Value,
                    cancellationToken);

                if (!rejectedNo.HasValue)
                {
                    var rejectedHeader = new MaterialRequestDetailDto
                    {
                        StoreGroup = sourceHeader.StoreGroup,
                        DateCreate = DateTime.Now,
                        AccordingTo = "This request contain items that were reject from another requests",
                        Approval = false,
                        ApprovalEnd = true,
                        PostPr = false,
                        IsAuto = false,
                        FromDate = sourceHeader.FromDate,
                        ToDate = sourceHeader.ToDate,
                        MaterialStatusId = 5,
                        PrNo = null,
                        NoIssue = sourceHeader.NoIssue
                    };

                    rejectedNo = await SaveRequestCoreAsync(conn, (SqlTransaction)tx, null, rejectedHeader, Array.Empty<MaterialRequestLineDto>(), cancellationToken);
                    await InsertSuperRequestAsync(conn, (SqlTransaction)tx, rejectedNo.Value, employeeId, DateTime.Now, "Reject request", 5, cancellationToken);
                }

                await MoveLinesToRequestAsync(
                    conn,
                    (SqlTransaction)tx,
                    rejectedNo.Value,
                    selectedLines,
                    sourceHeader.RequestNo,
                    string.Empty,
                    operatorCode,
                    cancellationToken);
            }

            await tx.CommitAsync(cancellationToken);
            return sourceHeader.RequestNo;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken);
            throw;
        }
    }

    /// <summary>
    /// Luu MR theo core flow.
    /// </summary>
    private async Task<long> SaveRequestCoreAsync(
        SqlConnection conn,
        SqlTransaction tx,
        long? requestNo,
        MaterialRequestDetailDto header,
        IReadOnlyList<MaterialRequestLineDto> lines,
        CancellationToken cancellationToken)
    {
        var resolvedRequestNo = requestNo ?? await GetNextRequestNoAsync(conn, tx, cancellationToken);
        var statusId = header.MaterialStatusId ?? await GetDefaultStatusIdAsync(conn, tx, cancellationToken);

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

            await using var updateCmd = new SqlCommand(updateSql, conn, tx);
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

            await using var insertCmd = new SqlCommand(insertSql, conn, tx);
            BindHeaderParams(insertCmd, resolvedRequestNo, header, statusId);
            await insertCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        const string deleteLineSql = "DELETE FROM dbo.MATERIAL_REQUEST_DETAIL WHERE REQUEST_NO = @RequestNo";
        await using (var deleteCmd = new SqlCommand(deleteLineSql, conn, tx))
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
                    @NewItem, @Selected, @NormMain, @Price, @Note, 0, 0, 0, 0
                )";

            foreach (var line in lines)
            {
                await using var lineCmd = new SqlCommand(insertLineSql, conn, tx);
                AddNumeric18_0Param(lineCmd, "@RequestNo", resolvedRequestNo);
                Helper.AddParameter(lineCmd, "@ItemCode", (line.ItemCode ?? string.Empty).Trim(), SqlDbType.VarChar, 20);
                Helper.AddParameter(lineCmd, "@Unit", (line.Unit ?? string.Empty).Trim(), SqlDbType.VarChar, 50);
                AddDecimal18_2Param(lineCmd, "@OrderQty", line.OrderQty ?? 0m);
                AddDecimal18_2Param(lineCmd, "@NotReceipt", line.NotReceipt ?? 0m);
                AddDecimal18_2Param(lineCmd, "@InStock", line.InStock ?? 0m);
                AddDecimal18_2Param(lineCmd, "@AccIn", line.AccIn ?? 0m);
                AddDecimal18_2Param(lineCmd, "@Buy", line.Buy ?? 0m);
                AddDecimal18_2Param(lineCmd, "@NormMain", line.NormMain ?? 0m);
                AddDecimal18_2Param(lineCmd, "@Price", line.Price ?? 0m);
                Helper.AddParameter(lineCmd, "@Note", (line.Note ?? string.Empty).Trim(), SqlDbType.VarChar, 255);
                Helper.AddParameter(lineCmd, "@NewItem", line.NewItem, SqlDbType.Bit);
                Helper.AddParameter(lineCmd, "@Selected", line.Selected, SqlDbType.Bit);
                await lineCmd.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        return resolvedRequestNo;
    }

    /// <summary>
    /// Tim request status 5 phu hop de lam reject pool.
    /// </summary>
    private static async Task<long?> FindRejectedPoolRequestNoAsync(SqlConnection conn, SqlTransaction tx, int storeGroup, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT TOP 1 REQUEST_NO
            FROM dbo.MATERIAL_REQUEST
            WHERE MATERIALSTATUSID = 5
              AND STORE_GROUP = @StoreGroup
            ORDER BY REQUEST_NO DESC";

        await using var cmd = new SqlCommand(sql, conn, tx);
        Helper.AddParameter(cmd, "@StoreGroup", storeGroup, SqlDbType.Int);
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return value == null || value == DBNull.Value ? null : Convert.ToInt64(value);
    }

    private static string ResolveWorkflowStatusLabel(int? typeEffective)
    {
        return typeEffective switch
        {
            -1 => "Just Created",
            0 => "Submitted to Head",
            1 => "Head Dept Approved",
            2 => "Purchaser Checked",
            3 => "CFO Approved",
            4 => "Collected to PR",
            5 => "Rejected",
            6 => "Issued",
            _ when typeEffective.HasValue => typeEffective.Value.ToString(),
            _ => string.Empty
        };
    }

    /// <summary>
    /// Cap nhat header MR ma khong doi line.
    /// </summary>
    private static async Task UpdateHeaderStatusOnlyAsync(
        SqlConnection conn,
        SqlTransaction tx,
        long requestNo,
        int statusId,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE dbo.MATERIAL_REQUEST
            SET MATERIALSTATUSID = @StatusId,
                APPROVAL = CASE WHEN @StatusId = 5 THEN 0 ELSE APPROVAL END,
                APPROVAL_END = CASE WHEN @StatusId = 5 THEN 1 ELSE APPROVAL_END END,
                POST_PR = CASE WHEN @StatusId = 5 THEN 0 ELSE POST_PR END
            WHERE REQUEST_NO = @RequestNo";

        await using var cmd = new SqlCommand(sql, conn, tx);
        AddNumeric18_0Param(cmd, "@RequestNo", requestNo);
        Helper.AddParameter(cmd, "@StatusId", statusId, SqlDbType.Int);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Chuyen cac line sang request khac.
    /// </summary>
    private static async Task MoveLinesToRequestAsync(
        SqlConnection conn,
        SqlTransaction tx,
        long targetRequestNo,
        IReadOnlyList<MaterialRequestLineDto> lines,
        long sourceRequestNo,
        string rejectReason,
        string operatorCode,
        CancellationToken cancellationToken)
    {
        const string sql = @"
            UPDATE dbo.MATERIAL_REQUEST_DETAIL
            SET REQUEST_NO = @TargetRequestNo,
                NOTE = @Note
            WHERE ID = @LineId";

        foreach (var line in lines)
        {
            if (!line.Id.HasValue || line.Id.Value <= 0)
            {
                continue;
            }

            await using var cmd = new SqlCommand(sql, conn, tx);
            AddNumeric18_0Param(cmd, "@TargetRequestNo", targetRequestNo);
            AddNumeric18_0Param(cmd, "@LineId", line.Id.Value);
            Helper.AddParameter(cmd, "@Note", BuildRejectNote(line, sourceRequestNo, rejectReason, operatorCode), SqlDbType.VarChar, 255);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Lay EmployeeID theo ma employee.
    /// </summary>
    private static async Task<int> ResolveEmployeeIdAsync(SqlConnection conn, SqlTransaction tx, string employeeCode, CancellationToken cancellationToken)
    {
        const string sql = @"
            SELECT TOP 1 EmployeeID
            FROM dbo.MS_Employee
            WHERE EmployeeCode = @EmployeeCode";

        await using var cmd = new SqlCommand(sql, conn, tx);
        Helper.AddParameter(cmd, "@EmployeeCode", employeeCode, SqlDbType.VarChar, 50);
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return value == null || value == DBNull.Value ? 0 : Convert.ToInt32(value);
    }

    /// <summary>
    /// Ghi log vao SUPER_REQUEST.
    /// </summary>
    private static async Task InsertSuperRequestAsync(SqlConnection conn, SqlTransaction tx, long requestNo, int employeeId, DateTime timeEffective, string note, int typeEffective, CancellationToken cancellationToken)
    {
        const string sql = @"
            INSERT INTO dbo.SUPER_REQUEST
            (
                RequestNo, EmployeeID, TimeEffective, TypeEffective, Note
            )
            VALUES
            (
                @RequestNo, @EmployeeID, @TimeEffective, @TypeEffective, @Note
            )";

        await using var cmd = new SqlCommand(sql, conn, tx);
        AddNumeric18_0Param(cmd, "@RequestNo", requestNo);
        Helper.AddParameter(cmd, "@EmployeeID", employeeId, SqlDbType.Int);
        Helper.AddParameter(cmd, "@TimeEffective", timeEffective, SqlDbType.DateTime);
        Helper.AddParameter(cmd, "@TypeEffective", typeEffective, SqlDbType.Int);
        Helper.AddParameter(cmd, "@Note", note, SqlDbType.VarChar, 50);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Tao ban sao header cho luong luu.
    /// </summary>
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

    /// <summary>
    /// Tao header rejected de luu vao pool.
    /// </summary>
    private static MaterialRequestDetailDto CloneRejectedHeader(MaterialRequestDetailDto source, DateTime now)
    {
        var header = CloneHeader(source);
        header.DateCreate = now;
        header.AccordingTo = "This request contain items that were reject from another requests";
        header.MaterialStatusId = 5;
        header.Approval = false;
        header.ApprovalEnd = true;
        header.PostPr = false;
        header.IsAuto = false;
        return header;
    }

    /// <summary>
    /// Chuan hoa danh sach line truoc khi save.
    /// </summary>
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
                NormMain = line.NormMain ?? 0,
                Price = line.Price ?? 0,
                Note = note,
                NewItem = line.NewItem,
                Selected = line.Selected,
                Issued = line.Issued
            });
        }

        return result;
    }

    /// <summary>
    /// Tao note reject theo format legacy.
    /// </summary>
    private static string BuildRejectNote(MaterialRequestLineDto source, long requestNo, string rejectReason, string operatorCode)
    {
        var noteParts = new List<string>
        {
            $"rejected from request {requestNo}"
        };

        var originalNote = (source.Note ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(originalNote))
        {
            noteParts.Add(originalNote);
        }

        noteParts.Add($"-{operatorCode}");
        var note = string.Join(" ", noteParts);
        if (note.Length > 255)
        {
            note = note.Substring(0, 255);
        }

        return note;
    }

    /// <summary>
    /// Tim item trong popup search.
    /// </summary>
    public async Task<IReadOnlyList<MaterialRequestItemLookupDto>> SearchItemsAsync(
        string? keyword,
        bool checkBalanceInStore = false,
        CancellationToken cancellationToken = default)
    {
        var sql = checkBalanceInStore
            ? @"
                SELECT TOP 100
                    i.ItemCode,
                    i.ItemName,
                    ISNULL(i.Unit, '') AS Unit,
                    ISNULL(dbo.GetItemBalance(i.ItemID, 1, YEAR(GETDATE())), 0) AS InStock,
                    i.KPGroupItem,
                    CAST(1 AS decimal(18,2)) AS OrderQty
                FROM dbo.INV_ItemList i
                WHERE
                    (
                        @Keyword IS NULL
                        OR i.ItemCode LIKE '%' + @Keyword + '%'
                        OR i.ItemName LIKE '%' + @Keyword + '%'
                    )
                    AND (i.IsActive = 1 OR i.IsActive IS NULL)
                ORDER BY i.ItemCode"
            : @"
                SELECT TOP 100
                    i.ItemCode,
                    i.ItemName,
                    ISNULL(i.Unit, '') AS Unit,
                    CAST(NULL AS decimal(18,2)) AS InStock,
                    i.KPGroupItem,
                    CAST(1 AS decimal(18,2)) AS OrderQty
                FROM dbo.INV_ItemList i
                WHERE
                    (
                        @Keyword IS NULL
                        OR i.ItemCode LIKE '%' + @Keyword + '%'
                        OR i.ItemName LIKE '%' + @Keyword + '%'
                    )
                    AND (i.IsActive = 1 OR i.IsActive IS NULL)
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
                StoreGroupId = rd.IsDBNull(4) ? null : Convert.ToInt32(rd[4]),
                OrderQty = rd.IsDBNull(5) ? 1m : Convert.ToDecimal(rd[5])
            },
            cmd =>
            {
                Helper.AddParameter(cmd, "@Keyword", string.IsNullOrWhiteSpace(keyword) ? null : keyword.Trim(), SqlDbType.VarChar, 150);
            },
            cancellationToken);

        return rows;
    }

    /// <summary>
    /// Tao item moi nhanh.
    /// </summary>
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

    /// <summary>
    /// Gan tham so tim kiem vao SqlCommand.
    /// </summary>
    private static void BindSearchParams(SqlCommand cmd, MaterialRequestFilterCriteria criteria, IReadOnlyList<int> statusIds)
    {
        Helper.AddParameter(cmd, "@RequestNo", string.IsNullOrWhiteSpace(criteria.RequestNo) ? null : criteria.RequestNo.Trim(), SqlDbType.VarChar, 50);
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

    /// <summary>
    /// Gan tham so header vao SqlCommand.
    /// </summary>
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

    /// <summary>
    /// Tao dieu kien SQL cho danh sach status.
    /// </summary>
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

    /// <summary>
    /// Lay Request No tiep theo.
    /// Ham nay giu lock trong transaction de tranh trung so khi nhieu nguoi tao cung luc.
    /// </summary>
    /// <param name="conn">SqlConnection dang mo.</param>
    /// <param name="tx">Transaction dang dung de giu lock.</param>
    /// <param name="cancellationToken">Token de huy yeu cau neu can.</param>
    /// <returns>Request No tiep theo.</returns>
    private static async Task<long> GetNextRequestNoAsync(SqlConnection conn, SqlTransaction tx, CancellationToken cancellationToken)
    {
        // UPDLOCK de chuan bi sua, HOLDLOCK de giu lock den cuoi transaction.
        const string sql = "SELECT ISNULL(MAX(REQUEST_NO), 0) + 1 FROM dbo.MATERIAL_REQUEST WITH (UPDLOCK, HOLDLOCK)";
        await using var cmd = new SqlCommand(sql, conn, tx);
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt64(value);
    }

    /// <summary>
    /// Lay status mac dinh neu trang thai chua co.
    /// </summary>
    private static async Task<int> GetDefaultStatusIdAsync(SqlConnection conn, SqlTransaction tx, CancellationToken cancellationToken)
    {
        const string sql = "SELECT ISNULL(MIN(MaterialStatusID), -1) FROM dbo.MaterialStatus";
        await using var cmd = new SqlCommand(sql, conn, tx);
        var value = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value);
    }

    /// <summary>
    /// Tao ma item nhanh.
    /// </summary>
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

    /// <summary>
    /// Tao danh sach detail line cho Auto MR.
    /// </summary>
    public async Task<IReadOnlyList<MaterialRequestLineDto>> BuildAutoLinesAsync(int storeGroup, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                i.ItemCode,
                i.ItemName,
                ISNULL(i.Unit, '') AS Unit,
                CAST(COALESCE(NULLIF(i.StoreNormInMain, 0), NULLIF(i.ReOrderPoint, 0), 1) AS decimal(18,2)) AS OrderQty
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
                    NormMain = 0,
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
    /// Them tham so decimal 18,0 vao SqlCommand.
    /// </summary>
    private static void AddNumeric18_0Param(SqlCommand cmd, string name, object? value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
        p.Precision = 18;
        p.Scale = 0;
        p.Value = value is null ? DBNull.Value : Convert.ToDecimal(value);
    }

    /// <summary>
    /// Them tham so decimal 18,2 vao SqlCommand.
    /// </summary>
    private static void AddDecimal18_2Param(SqlCommand cmd, string name, object? value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.Decimal);
        p.Precision = 18;
        p.Scale = 2;
        p.Value = value is null ? DBNull.Value : Convert.ToDecimal(value);
    }

    /// <summary>
    /// Them tham so DateTime vao SqlCommand.
    /// </summary>
    private static void AddDateTimeParam(SqlCommand cmd, string name, DateTime value)
    {
        var p = cmd.Parameters.Add(name, SqlDbType.DateTime);
        p.Value = value;
    }
}
