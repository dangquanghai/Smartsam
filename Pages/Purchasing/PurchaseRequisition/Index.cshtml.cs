using System.Data;
using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;

namespace SmartSam.Pages.Purchasing.PurchaseRequisition;

public class IndexModel : BasePageModel
{
    private static readonly HashSet<int> AllowedPageSizes = [10, 20, 50, 100, 200];
    private static readonly string[] AcceptedDateFormats = ["yyyy-MM-dd", "M/d/yyyy", "MM/dd/yyyy", "d/M/yyyy", "dd/MM/yyyy"];
    private const int FUNCTION_ID = 72;
    private readonly ILogger<IndexModel> _logger;
    private readonly PermissionService _permissionService;

    public IndexModel(IConfiguration config, ILogger<IndexModel> logger, PermissionService permissionService) : base(config)
    {
        _logger = logger;
        _permissionService = permissionService;
    }

    public PagePermissions PagePerm { get; set; } = new();
    public int DefaultPageSize => _config.GetValue<int>("AppSettings:DefaultPageSize", 10);

    [BindProperty(SupportsGet = true)]
    public PurchaseRequisitionFilter Filter { get; set; } = new();

    [BindProperty]
    public int? SelectedPrId { get; set; }

    [BindProperty]
    public string AddAtDetailsJson { get; set; } = "[]";

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public string MessageType { get; set; } = "info";

    public List<PurchaseRequisitionRow> Rows { get; set; } = [];
    public List<PurchaseRequisitionItemLookup> ItemList { get; set; } = [];
    public List<PurchaseRequisitionSupplierLookup> SupplierList { get; set; } = [];
    public int TotalRecords { get; set; }
    public int TotalPages => Filter.PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)Filter.PageSize));
    public int PageStart => TotalRecords == 0 ? 0 : ((Filter.Page - 1) * Filter.PageSize) + 1;
    public int PageEnd => TotalRecords == 0 ? 0 : Math.Min(Filter.Page * Filter.PageSize, TotalRecords);
    public bool HasPreviousPage => Filter.Page > 1;
    public bool HasNextPage => Filter.Page < TotalPages;

    public void OnGet()
    {
        PagePerm = GetUserPermissions();
        NormalizeQueryInputs();
        NormalizeFilter();
        LoadLookups();
        LoadRows();
    }

    public IActionResult OnPostSearch([FromBody] PurchaseRequisitionSearchRequest request)
    {
        try
        {
            PagePerm = GetUserPermissions();
            var filter = BuildFilter(request);
            var (rows, totalRecords) = SearchPaged(filter);

            var data = rows.Select(row => new
            {
                data = row,
                actions = BuildActions()
            });

            return new JsonResult(new
            {
                success = true,
                data,
                total = totalRecords,
                page = filter.Page,
                pageSize = filter.PageSize,
                totalPages = filter.PageSize <= 0 ? 1 : (int)Math.Ceiling((double)totalRecords / filter.PageSize)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in PurchaseRequisition search.");
            return new JsonResult(new { success = false, message = ex.Message });
        }
    }

    public IActionResult OnPostAddAt()
    {
        PagePerm = GetUserPermissions();
        NormalizePostInputs();
        NormalizeFilter();

        if (!PagePerm.HasPermission(4))
        {
            Message = "You have no permission to update requisition details.";
            MessageType = "error";
            return RedirectToPage("./Index", BuildRouteValues());
        }

        var details = ParseAddAtDetails();
        if (!SelectedPrId.HasValue || SelectedPrId.Value <= 0)
        {
            ModelState.AddModelError(string.Empty, "Please select exactly one requisition row.");
        }

        if (details.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Please add at least one detail row.");
        }

        for (var i = 0; i < details.Count; i++)
        {
            ValidateDetail(details[i], i + 1);
        }

        if (!ModelState.IsValid)
        {
            Message = GetModelStateErrorMessage();
            MessageType = "error";
            return RedirectToPage("./Index", BuildRouteValues());
        }

        try
        {
            AddDetails(SelectedPrId!.Value, details);
            Message = "Add AT details saved successfully.";
            MessageType = "success";
        }
        catch (InvalidOperationException ex)
        {
            Message = ex.Message;
            MessageType = "error";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot save Add AT details.");
            Message = "Cannot save Add AT details. Please try again.";
            MessageType = "error";
        }

        return RedirectToPage("./Index", BuildRouteValues());
    }

    private void LoadRows()
    {
        var (rows, totalRecords) = SearchPaged(Filter);
        Rows = rows;
        TotalRecords = totalRecords;

        if (TotalRecords > 0 && Filter.Page > TotalPages)
        {
            Filter.Page = TotalPages;
            (rows, totalRecords) = SearchPaged(Filter);
            Rows = rows;
            TotalRecords = totalRecords;
        }
    }

    private (List<PurchaseRequisitionRow> rows, int totalRecords) SearchPaged(PurchaseRequisitionFilter filter)
    {
        var rows = new List<PurchaseRequisitionRow>();
        var totalRecords = 0;
        var page = filter.Page <= 0 ? 1 : filter.Page;
        var pageSize = AllowedPageSizes.Contains(filter.PageSize) ? filter.PageSize : DefaultPageSize;
        var offset = (page - 1) * pageSize;

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        using (var countCmd = new SqlCommand(@"
        SELECT COUNT(1)
        FROM dbo.PC_PR p
        LEFT JOIN dbo.PC_PRStatus st ON p.Status = st.PRStatusID
        LEFT JOIN dbo.MS_Employee ep ON p.PurId = ep.EmployeeID
        LEFT JOIN dbo.MS_Employee ec ON p.CAId = ec.EmployeeID
        LEFT JOIN dbo.MS_Employee eg ON p.GDId = eg.EmployeeID
        WHERE (@RequestNo IS NULL OR p.RequestNo LIKE '%' + @RequestNo + '%')
        AND (@Description IS NULL OR p.[Description] LIKE '%' + @Description + '%')
        AND (
                @UseDateRange = 0
                OR (@FromDate IS NULL OR p.RequestDate >= @FromDate)
                AND (@ToDate IS NULL OR p.RequestDate < DATEADD(DAY, 1, @ToDate))
            )", conn))
        {
            BindSearchParams(countCmd, filter);
            totalRecords = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);
        }

        using var cmd = new SqlCommand(@"
        SELECT
            p.PRID,
            ISNULL(p.RequestNo, '') AS RequestNo,
            p.RequestDate,
            ISNULL(p.[Description], '') AS [Description],
            p.[Status],
            ISNULL(NULLIF(st.PRStatusName, ''), CASE p.[Status]
                WHEN 1 THEN 'Done'
                WHEN 2 THEN 'In Progress'
                WHEN 3 THEN 'Disapproved'
                ELSE 'Preparing'
            END) AS StatusName,
            ISNULL(ep.EmployeeCode, '') AS PurchaserCode,
            ISNULL(ec.EmployeeCode, '') AS ChiefACode,
            ISNULL(eg.EmployeeCode, '') AS GDirectorCode
        FROM dbo.PC_PR p
        LEFT JOIN dbo.PC_PRStatus st ON p.Status = st.PRStatusID
        LEFT JOIN dbo.MS_Employee ep ON p.PurId = ep.EmployeeID
        LEFT JOIN dbo.MS_Employee ec ON p.CAId = ec.EmployeeID
        LEFT JOIN dbo.MS_Employee eg ON p.GDId = eg.EmployeeID
        WHERE (@RequestNo IS NULL OR p.RequestNo LIKE '%' + @RequestNo + '%')
        AND (@Description IS NULL OR p.[Description] LIKE '%' + @Description + '%')
        AND (
                @UseDateRange = 0
                OR (@FromDate IS NULL OR p.RequestDate >= @FromDate)
                AND (@ToDate IS NULL OR p.RequestDate < DATEADD(DAY, 1, @ToDate))
            )
        ORDER BY p.RequestDate DESC, p.PRID DESC
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", conn);

        BindSearchParams(cmd, filter);
        cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
        cmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(new PurchaseRequisitionRow
            {
                Id = Convert.ToInt32(rd["PRID"]),
                RequestNo = Convert.ToString(rd["RequestNo"]) ?? string.Empty,
                RequestDate = rd.IsDBNull(rd.GetOrdinal("RequestDate")) ? DateTime.MinValue : Convert.ToDateTime(rd["RequestDate"]),
                Description = Convert.ToString(rd["Description"]) ?? string.Empty,
                StatusId = rd.IsDBNull(rd.GetOrdinal("Status")) ? null : Convert.ToByte(rd["Status"]),
                StatusName = Convert.ToString(rd["StatusName"]) ?? string.Empty,
                PurchaserCode = Convert.ToString(rd["PurchaserCode"]) ?? string.Empty,
                ChiefACode = Convert.ToString(rd["ChiefACode"]) ?? string.Empty,
                GDirectorCode = Convert.ToString(rd["GDirectorCode"]) ?? string.Empty
            });
        }

        return (rows, totalRecords);
    }

    private object BuildActions() => new
    {
        canView = PagePerm.HasPermission(2),
        canEdit = PagePerm.HasPermission(4),
        canAddAt = PagePerm.HasPermission(4),
        canViewDetail = PagePerm.HasPermission(2),
        canDisapproval = PagePerm.HasPermission(6),
        accessMode = PagePerm.HasPermission(4) ? "edit" : "view"
    };

    private void LoadLookups()
    {
        LoadItemList();
        LoadSupplierList();
    }

    private void LoadItemList()
    {
        ItemList = [];

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
SELECT TOP 200
    ItemID,
    ISNULL(ItemCode, '') AS ItemCode,
    ISNULL(ItemName, '') AS ItemName,
    ISNULL(Unit, '') AS Unit
FROM dbo.INV_ItemList
WHERE ISNULL(IsPurchase, 0) = 1
ORDER BY ItemCode", conn);

        conn.Open();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            ItemList.Add(new PurchaseRequisitionItemLookup
            {
                Id = Convert.ToInt32(rd["ItemID"]),
                ItemCode = Convert.ToString(rd["ItemCode"]) ?? string.Empty,
                ItemName = Convert.ToString(rd["ItemName"]) ?? string.Empty,
                Unit = Convert.ToString(rd["Unit"]) ?? string.Empty
            });
        }
    }

    private void LoadSupplierList()
    {
        SupplierList = [];

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
SELECT TOP 200
    SupplierID,
    ISNULL(SupplierCode, '') AS SupplierCode,
    ISNULL(SupplierName, '') AS SupplierName
FROM dbo.PC_Suppliers
ORDER BY SupplierCode", conn);

        conn.Open();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            SupplierList.Add(new PurchaseRequisitionSupplierLookup
            {
                Id = Convert.ToInt32(rd["SupplierID"]),
                SupplierCode = Convert.ToString(rd["SupplierCode"]) ?? string.Empty,
                SupplierName = Convert.ToString(rd["SupplierName"]) ?? string.Empty
            });
        }
    }

    private void AddDetails(int prId, IReadOnlyList<PurchaseRequisitionDetailInput> details)
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var trans = conn.BeginTransaction();

        try
        {
            using (var checkCmd = new SqlCommand("SELECT COUNT(1) FROM dbo.PC_PR WHERE PRID = @PRID", conn, trans))
            {
                checkCmd.Parameters.Add("@PRID", SqlDbType.Int).Value = prId;
                var exists = Convert.ToInt32(checkCmd.ExecuteScalar() ?? 0);
                if (exists <= 0)
                {
                    throw new InvalidOperationException("Selected requisition does not exist.");
                }
            }

            foreach (var detail in details)
            {
                InsertDetail(conn, trans, prId, detail);
            }

            trans.Commit();
        }
        catch
        {
            trans.Rollback();
            throw;
        }
    }

    internal static void InsertDetail(SqlConnection conn, SqlTransaction trans, int prId, PurchaseRequisitionDetailInput detail)
    {
        using var detailCmd = new SqlCommand(@"
INSERT INTO dbo.PC_PRDetail
(
    PRID, ItemID, Quantity, UnitPrice, Remark, RecQty, OrdAmount, RecAmount,
    POed, MRRequestNO, SugQty, SupplierID, PoQuantity, PoQuantitySug
)
VALUES
(
    @PRID, @ItemID, @Quantity, @UnitPrice, @Remark, 0, @OrdAmount, 0,
    0, NULL, @SugQty, @SupplierID, 0, @PoQuantitySug
)", conn, trans);

        detailCmd.Parameters.Add("@PRID", SqlDbType.Int).Value = prId;
        detailCmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = detail.ItemId;
        detailCmd.Parameters.Add("@Quantity", SqlDbType.Decimal).Value = detail.QtyPur;
        detailCmd.Parameters.Add("@UnitPrice", SqlDbType.Decimal).Value = detail.UnitPrice;
        detailCmd.Parameters.Add("@Remark", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(detail.Remark) ? DBNull.Value : detail.Remark.Trim();
        detailCmd.Parameters.Add("@OrdAmount", SqlDbType.Decimal).Value = detail.QtyPur * detail.UnitPrice;
        detailCmd.Parameters.Add("@SugQty", SqlDbType.Decimal).Value = detail.QtyFromM;
        detailCmd.Parameters.Add("@SupplierID", SqlDbType.Int).Value = detail.SupplierId.HasValue ? detail.SupplierId.Value : DBNull.Value;
        detailCmd.Parameters.Add("@PoQuantitySug", SqlDbType.Decimal).Value = detail.QtyFromM;
        detailCmd.ExecuteNonQuery();
    }

    internal static void BindSearchParams(SqlCommand cmd, PurchaseRequisitionFilter filter)
    {
        cmd.Parameters.Add("@RequestNo", SqlDbType.VarChar, 20).Value = string.IsNullOrWhiteSpace(filter.RequestNo) ? DBNull.Value : filter.RequestNo.Trim();
        cmd.Parameters.Add("@Description", SqlDbType.VarChar, 500).Value = string.IsNullOrWhiteSpace(filter.Description) ? DBNull.Value : filter.Description.Trim();
        cmd.Parameters.Add("@UseDateRange", SqlDbType.Bit).Value = filter.UseDateRange;
        cmd.Parameters.Add("@FromDate", SqlDbType.DateTime).Value = filter.FromDate.HasValue ? filter.FromDate.Value.Date : DBNull.Value;
        cmd.Parameters.Add("@ToDate", SqlDbType.DateTime).Value = filter.ToDate.HasValue ? filter.ToDate.Value.Date : DBNull.Value;
    }

    private void NormalizeFilter()
    {
        if (!AllowedPageSizes.Contains(Filter.PageSize))
        {
            Filter.PageSize = DefaultPageSize;
        }

        if (Filter.Page <= 0)
        {
            Filter.Page = 1;
        }

        if (!Filter.UseDateRange)
        {
            Filter.FromDate = null;
            Filter.ToDate = null;
        }
        else if (!Filter.FromDate.HasValue)
        {
            Filter.ToDate = null;
        }

        if (Filter.UseDateRange && Filter.FromDate.HasValue && Filter.ToDate.HasValue && Filter.FromDate.Value.Date > Filter.ToDate.Value.Date)
        {
            ModelState.AddModelError(string.Empty, "From Date must be less than or equal to To Date.");
            Filter.ToDate = null;
        }
    }

    private PurchaseRequisitionFilter BuildFilter(PurchaseRequisitionSearchRequest request)
    {
        return new PurchaseRequisitionFilter
        {
            RequestNo = request.RequestNo,
            Description = request.Description,
            UseDateRange = request.UseDateRange,
            FromDate = request.FromDate,
            ToDate = request.ToDate,
            Page = request.Page <= 0 ? 1 : request.Page,
            PageSize = AllowedPageSizes.Contains(request.PageSize) ? request.PageSize : DefaultPageSize
        };
    }

    private object BuildRouteValues() => new
    {
        RequestNo = Filter.RequestNo,
        Description = Filter.Description,
        UseDateRange = Filter.UseDateRange,
        FromDate = Filter.UseDateRange ? Filter.FromDate?.ToString("yyyy-MM-dd") : null,
        ToDate = Filter.UseDateRange && Filter.FromDate.HasValue ? Filter.ToDate?.ToString("yyyy-MM-dd") : null,
        Page = Filter.Page,
        PageSize = Filter.PageSize
    };

    private void NormalizeQueryInputs()
    {
        Filter.RequestNo = Request.Query[nameof(Filter.RequestNo)].ToString();
        Filter.Description = Request.Query[nameof(Filter.Description)].ToString();
        NormalizeBoolQuery(nameof(Filter.UseDateRange), value => Filter.UseDateRange = value);
        NormalizeIntQuery(nameof(Filter.Page), value => Filter.Page = value, 1);
        NormalizeIntQuery(nameof(Filter.PageSize), value => Filter.PageSize = value, DefaultPageSize);
        NormalizeDateQuery(nameof(Filter.FromDate), value => Filter.FromDate = value);
        NormalizeDateQuery(nameof(Filter.ToDate), value => Filter.ToDate = value);
    }

    private void NormalizePostInputs()
    {
        Filter.RequestNo = Request.Form[nameof(Filter.RequestNo)].ToString();
        Filter.Description = Request.Form[nameof(Filter.Description)].ToString();
        NormalizeBoolForm(nameof(Filter.UseDateRange), value => Filter.UseDateRange = value);
        NormalizeIntForm(nameof(Filter.Page), value => Filter.Page = value, 1);
        NormalizeIntForm(nameof(Filter.PageSize), value => Filter.PageSize = value, DefaultPageSize);
        NormalizeDateForm(nameof(Filter.FromDate), value => Filter.FromDate = value);
        NormalizeDateForm(nameof(Filter.ToDate), value => Filter.ToDate = value);

        if (Request.HasFormContentType && Request.Form.ContainsKey(nameof(SelectedPrId)))
        {
            var raw = Request.Form[nameof(SelectedPrId)].ToString();
            SelectedPrId = int.TryParse(raw, out var parsed) ? parsed : null;
            ModelState.Remove(nameof(SelectedPrId));
        }
    }

    private void NormalizeDateQuery(string key, Action<DateTime?> assign)
    {
        if (!Request.Query.ContainsKey(key)) return;
        ParseDate(Request.Query[key].ToString(), assign, key);
    }

    private void NormalizeDateForm(string key, Action<DateTime?> assign)
    {
        if (!Request.HasFormContentType || !Request.Form.ContainsKey(key)) return;
        ParseDate(Request.Form[key].ToString(), assign, key);
    }

    private void ParseDate(string raw, Action<DateTime?> assign, string key)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            assign(null);
            ModelState.Remove(key);
            return;
        }

        if (DateTime.TryParseExact(raw, AcceptedDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedExact)
            || DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsedExact)
            || DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedExact))
        {
            assign(parsedExact.Date);
            ModelState.Remove(key);
            return;
        }

        assign(null);
        ModelState.Remove(key);
    }

    private void NormalizeBoolQuery(string key, Action<bool> assign)
    {
        if (!Request.Query.ContainsKey(key)) return;
        assign(ParseBoolValues(Request.Query[key]));
        ModelState.Remove(key);
    }

    private void NormalizeBoolForm(string key, Action<bool> assign)
    {
        if (!Request.HasFormContentType || !Request.Form.ContainsKey(key)) return;
        assign(ParseBoolValues(Request.Form[key]));
        ModelState.Remove(key);
    }

    private static bool ParseBoolValues(IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            if (value == "1" || value.Equals("on", StringComparison.OrdinalIgnoreCase)) return true;
            if (value != "0" && bool.TryParse(value, out var parsed) && parsed) return true;
        }
        return false;
    }

    private void NormalizeIntQuery(string key, Action<int> assign, int defaultValue)
    {
        if (!Request.Query.ContainsKey(key)) return;
        assign(int.TryParse(Request.Query[key].ToString(), out var parsed) ? parsed : defaultValue);
        ModelState.Remove(key);
    }

    private void NormalizeIntForm(string key, Action<int> assign, int defaultValue)
    {
        if (!Request.HasFormContentType || !Request.Form.ContainsKey(key)) return;
        assign(int.TryParse(Request.Form[key].ToString(), out var parsed) ? parsed : defaultValue);
        ModelState.Remove(key);
    }

    private List<PurchaseRequisitionDetailInput> ParseAddAtDetails()
    {
        if (string.IsNullOrWhiteSpace(AddAtDetailsJson)) return [];

        try
        {
            var details = JsonSerializer.Deserialize<List<PurchaseRequisitionDetailInput>>(AddAtDetailsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return details ?? [];
        }
        catch
        {
            ModelState.AddModelError(string.Empty, "Detail data format is invalid.");
            return [];
        }
    }

    private void ValidateDetail(PurchaseRequisitionDetailInput detail, int rowNo)
    {
        if (detail.ItemId <= 0)
        {
            ModelState.AddModelError(string.Empty, $"Row {rowNo}: Item is required.");
        }

        if (detail.QtyPur <= 0)
        {
            ModelState.AddModelError(string.Empty, $"Row {rowNo}: QtyPur must be greater than 0.");
        }

        if (detail.QtyFromM < 0 || detail.UnitPrice < 0)
        {
            ModelState.AddModelError(string.Empty, $"Row {rowNo}: QtyFromM and U.Price must be valid numbers.");
        }
    }

    private string GetModelStateErrorMessage()
    {
        var errors = ModelState.Values
            .SelectMany(x => x.Errors)
            .Select(x => x.ErrorMessage)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        return errors.Count > 0
            ? string.Join(" ", errors)
            : "Cannot save Add AT details. Please review data and try again.";
    }

    private PagePermissions GetUserPermissions()
    {
        var isAdmin = User.FindFirst("IsAdminRole")?.Value == "True";
        var roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "0");
        var permsObj = new PagePermissions();

        if (isAdmin)
        {
            permsObj.AllowedNos = Enumerable.Range(1, 20).ToList();
        }
        else
        {
            permsObj.AllowedNos = _permissionService.GetPermissionsForPage(roleId, FUNCTION_ID);
        }

        return permsObj;
    }
}

public class PurchaseRequisitionSearchRequest
{
    public string? RequestNo { get; set; }
    public string? Description { get; set; }
    public bool UseDateRange { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class PurchaseRequisitionFilter
{
    public string? RequestNo { get; set; }
    public string? Description { get; set; }
    public bool UseDateRange { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class PurchaseRequisitionRow
{
    public int Id { get; set; }
    public string RequestNo { get; set; } = string.Empty;
    public DateTime RequestDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public byte? StatusId { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public string PurchaserCode { get; set; } = string.Empty;
    public string ChiefACode { get; set; } = string.Empty;
    public string GDirectorCode { get; set; } = string.Empty;
}

public class PurchaseRequisitionItemLookup
{
    public int Id { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
}

public class PurchaseRequisitionSupplierLookup
{
    public int Id { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
}

public class PurchaseRequisitionDetailInput
{
    public long DetailId { get; set; }
    public int ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal QtyFromM { get; set; }
    public decimal QtyPur { get; set; }
    public decimal UnitPrice { get; set; }
    public string? Remark { get; set; }
    public int? SupplierId { get; set; }
    public string SupplierText { get; set; } = string.Empty;
}
