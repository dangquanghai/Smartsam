using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Text.Json;
using SmartSam.Services;

namespace SmartSam.Pages.Purchasing.MaterialRequest;

public class IndexModel : PageModel
{
    private readonly MaterialRequestService _materialRequestService;
    private readonly PermissionService _permissionService;

    private const int MaterialRequestFunctionId = 104;
    private const int PermissionCreate = 1;
    private const int PermissionCreateAuto = 2;
    private const int PermissionEdit = 3;
    private const int PermissionShowAll = 4;
    private const int PermissionShowCreate = 7;
    private const int NoScopeStoreGroup = -1;
    private const string ConditionModeAllUsers = "allUsers";
    private const string ConditionModeStoreman = "storeman";

    private EmployeeMaterialScopeDto _dataScope = new();
    private bool _isAdminRole;

    public IndexModel(IConfiguration configuration, PermissionService permissionService)
    {
        _materialRequestService = new MaterialRequestService(configuration);
        _permissionService = permissionService;
    }

    [BindProperty(SupportsGet = true)]
    public long? RequestNo { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? StoreGroup { get; set; }

    [BindProperty(SupportsGet = true)]
    public List<int> StatusIds { get; set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? ItemCode { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool IssueLessThanOrder { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool BuyGreaterThanZero { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool AutoOnly { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? AccordingToKeyword { get; set; }

    [BindProperty(SupportsGet = true)]
    public string ConditionMode { get; set; } = ConditionModeAllUsers;

    [BindProperty(SupportsGet = true)]
    public int PageIndex { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 25;

    [BindProperty]
    public string? CreateDescription { get; set; }

    [BindProperty]
    public string? CreateLinesJson { get; set; }

    [BindProperty]
    public int? CreateStoreGroup { get; set; }

    [TempData]
    public string? FlashMessage { get; set; }

    [TempData]
    public string? FlashMessageType { get; set; }

    public string? Message { get; set; }
    public string MessageType { get; set; } = "info";

    public PagePermissions PagePerm { get; private set; } = new();
    public List<SelectListItem> StoreGroups { get; set; } = [];
    public List<SelectListItem> Statuses { get; set; } = [];
    public List<MaterialRequestListRowDto> Rows { get; set; } = [];

    public int TotalRecords { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)PageSize));
    public bool HasPreviousPage => PageIndex > 1;
    public bool HasNextPage => PageIndex < TotalPages;

    public bool CanCreate => HasPermission(PermissionCreate) || HasPermission(PermissionShowCreate);
    public bool CanCreateAuto => HasPermission(PermissionCreateAuto);
    public bool CanEdit => HasPermission(PermissionEdit);
    public bool CanShowAll => _isAdminRole || HasPermission(PermissionShowAll);
    public bool StoreGroupLocked => !CanShowAll;
    public int UserApprovalLevel => _dataScope.ApprovalLevel;
    public bool ConditionForAllUsers => CanShowAll;
    public bool IsStoremanConditionMode => string.Equals(ConditionMode, ConditionModeStoreman, StringComparison.OrdinalIgnoreCase);

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        await LoadUserScopeAsync(cancellationToken);
        if (!CanAccessPage())
        {
            return Forbid();
        }

        if (string.IsNullOrWhiteSpace(Message) && !string.IsNullOrWhiteSpace(FlashMessage))
        {
            Message = FlashMessage;
            MessageType = string.IsNullOrWhiteSpace(FlashMessageType) ? "info" : FlashMessageType!;
        }

        NormalizeConditionMode();
        FromDate ??= DateTime.Today.AddMonths(-3).Date;
        ToDate ??= DateTime.Today.Date;

        await LoadFiltersAsync(cancellationToken);
        await LoadRowsAsync(cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnGetSearchItemsAsync(string? keyword, bool checkBalanceInStore = false, int? storeGroup = null, CancellationToken cancellationToken = default)
    {
        LoadPagePermissions();
        await LoadUserScopeAsync(cancellationToken);
        if (!CanAccessPage())
        {
            return new JsonResult(new { success = false, message = "Access denied." });
        }

        var scopedStoreGroup = StoreGroupLocked ? _dataScope.StoreGroup : (storeGroup ?? StoreGroup);
        var items = await _materialRequestService.SearchItemsAsync(keyword, checkBalanceInStore, scopedStoreGroup, cancellationToken);

        // Legacy popup cho phép search rộng: nếu lọc theo store group mà rỗng thì fallback search all.
        if (items.Count == 0 && scopedStoreGroup.HasValue)
        {
            items = await _materialRequestService.SearchItemsAsync(keyword, checkBalanceInStore, null, cancellationToken);
        }

        return new JsonResult(new { success = true, data = items });
    }

    public async Task<IActionResult> OnPostCreateMrAsync(CancellationToken cancellationToken)
    {
        LoadPagePermissions();
        await LoadUserScopeAsync(cancellationToken);
        if (!CanAccessPage() || !CanCreate)
        {
            return Forbid();
        }

        var selectedItems = ParseCreateItems(CreateLinesJson);
        if (selectedItems.Count == 0)
        {
            FlashMessage = "Please select at least one item.";
            FlashMessageType = "warning";
            return RedirectToPage("./Index");
        }

        if (string.IsNullOrWhiteSpace(CreateDescription))
        {
            FlashMessage = "Description is required.";
            FlashMessageType = "warning";
            return RedirectToPage("./Index");
        }

        var scopedStoreGroup = StoreGroupLocked ? _dataScope.StoreGroup : (CreateStoreGroup ?? StoreGroup);
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
                FlashMessage = "Please choose Store Group before creating MR.";
                FlashMessageType = "warning";
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
            var requestNo = await _materialRequestService.SaveAsync(null, header, lines, cancellationToken);
            FlashMessage = "Material Request created successfully.";
            FlashMessageType = "success";
            return RedirectToPage("./Detail", new { id = requestNo });
        }
        catch (Exception ex)
        {
            FlashMessage = $"Cannot create Material Request. {ex.Message}";
            FlashMessageType = "error";
            return RedirectToPage("./Index");
        }
    }

    private async Task LoadFiltersAsync(CancellationToken cancellationToken)
    {
        var stores = await _materialRequestService.GetStoreGroupsAsync(cancellationToken);
        StoreGroups = [
            new SelectListItem { Value = string.Empty, Text = "--- All ---" },
            .. stores.Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.Name })
        ];

        var statuses = await _materialRequestService.GetStatusesAsync(cancellationToken);
        Statuses = [
            .. statuses.Select(x => new SelectListItem { Value = x.Id.ToString(), Text = x.Name })
        ];

        if (!CanShowAll && (StatusIds?.Count ?? 0) == 0 && !Request.Query.ContainsKey(nameof(StatusIds)))
        {
            var defaultStatus = _dataScope.ApprovalLevel switch
            {
                <= 0 => 0,
                1 => 1,
                2 => 2,
                _ => 3
            };

            StatusIds = [defaultStatus];
        }

        if (StoreGroupLocked)
        {
            StoreGroup = _dataScope.StoreGroup ?? NoScopeStoreGroup;
            StoreGroups = _dataScope.StoreGroup.HasValue
                ? [new SelectListItem { Value = _dataScope.StoreGroup.Value.ToString(), Text = $"Store Group {_dataScope.StoreGroup.Value}" }]
                : [new SelectListItem { Value = NoScopeStoreGroup.ToString(), Text = "(No Store Group Scope)" }];
        }
    }

    private async Task LoadRowsAsync(CancellationToken cancellationToken)
    {
        if (PageSize <= 0) PageSize = 25;
        if (PageSize > 200) PageSize = 200;
        if (PageIndex <= 0) PageIndex = 1;

        var result = await _materialRequestService.SearchPagedAsync(BuildCriteria(includePaging: true), cancellationToken);
        TotalRecords = result.TotalCount;
        if (TotalRecords > 0 && PageIndex > TotalPages)
        {
            PageIndex = TotalPages;
            result = await _materialRequestService.SearchPagedAsync(BuildCriteria(includePaging: true), cancellationToken);
            TotalRecords = result.TotalCount;
        }

        Rows = result.Rows;
    }

    private MaterialRequestFilterCriteria BuildCriteria(bool includePaging)
    {
        var scopedStoreGroup = StoreGroupLocked
            ? (_dataScope.StoreGroup ?? NoScopeStoreGroup)
            : StoreGroup;
        var isStoremanMode = IsStoremanConditionMode;

        return new MaterialRequestFilterCriteria
        {
            RequestNo = isStoremanMode ? null : RequestNo,
            StoreGroup = scopedStoreGroup == NoScopeStoreGroup ? null : scopedStoreGroup,
            StatusIds = isStoremanMode ? [] : StatusIds,
            ItemCode = isStoremanMode && !string.IsNullOrWhiteSpace(ItemCode) ? ItemCode.Trim() : null,
            NoIssue = isStoremanMode && IssueLessThanOrder ? 1 : null,
            IsAuto = isStoremanMode ? null : (AutoOnly ? true : null),
            BuyGreaterThanZero = isStoremanMode ? null : (BuyGreaterThanZero ? true : null),
            FromDate = FromDate?.Date,
            ToDate = ToDate?.Date,
            AccordingToKeyword = string.IsNullOrWhiteSpace(AccordingToKeyword) ? null : AccordingToKeyword.Trim(),
            PageIndex = includePaging ? PageIndex : null,
            PageSize = includePaging ? PageSize : null
        };
    }

    private void NormalizeConditionMode()
    {
        if (!string.Equals(ConditionMode, ConditionModeStoreman, StringComparison.OrdinalIgnoreCase))
        {
            ConditionMode = ConditionModeAllUsers;
            return;
        }

        ConditionMode = ConditionModeStoreman;
    }

    private async Task LoadUserScopeAsync(CancellationToken cancellationToken)
    {
        _isAdminRole = string.Equals(User.FindFirst("IsAdminRole")?.Value, "True", StringComparison.OrdinalIgnoreCase);
        if (_isAdminRole)
        {
            _dataScope = new EmployeeMaterialScopeDto();
            return;
        }

        _dataScope = await _materialRequestService.GetEmployeeScopeAsync(User.Identity?.Name, cancellationToken);
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

        PagePerm.AllowedNos = _permissionService.GetPermissionsForPage(roleId, MaterialRequestFunctionId);
    }

    private bool CanAccessPage()
    {
        if (_isAdminRole)
        {
            return true;
        }

        return PagePerm.AllowedNos.Count > 0;
    }

    private bool HasPermission(int permissionNo) => PagePerm.HasPermission(permissionNo);

    private static List<MaterialRequestItemLookupDto> ParseCreateItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            var rows = JsonSerializer.Deserialize<List<MaterialRequestItemLookupDto>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? [];

            return rows
                .Where(x => !string.IsNullOrWhiteSpace(x.ItemCode))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static List<MaterialRequestLineDto> MapItemsToCreateLines(IEnumerable<MaterialRequestItemLookupDto> items)
    {
        return items
            .Where(x => !string.IsNullOrWhiteSpace(x.ItemCode))
            .Select(x => new MaterialRequestLineDto
            {
                ItemCode = x.ItemCode.Trim(),
                ItemName = (x.ItemName ?? string.Empty).Trim(),
                Unit = (x.Unit ?? string.Empty).Trim(),
                OrderQty = 0,
                NotReceipt = 0,
                InStock = x.InStock,
                AccIn = 0,
                Buy = 0,
                Price = 0,
                Note = string.Empty,
                NewItem = false,
                Selected = true
            })
            .ToList();
    }
}
