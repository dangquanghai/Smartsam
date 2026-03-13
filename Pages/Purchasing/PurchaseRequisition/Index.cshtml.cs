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
    public string? RequestNo { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Description { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool UseDateRange { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageIndex { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 10;

    [BindProperty]
    public int? SelectedPrId { get; set; }

    [BindProperty]
    public string AddAtDetailsJson { get; set; } = "[]";

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public string MessageType { get; set; } = "info";

    public List<PurchaseRequisitionListRowDto> Rows { get; set; } = [];
    public List<PurchaseRequisitionItemLookupDto> ItemList { get; set; } = [];
    public List<PurchaseRequisitionSupplierLookupDto> SupplierList { get; set; } = [];
    public int TotalRecords { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)PageSize));
    public bool HasPreviousPage => PageIndex > 1;
    public bool HasNextPage => PageIndex < TotalPages;
    public int PageStart => TotalRecords == 0 ? 0 : ((PageIndex - 1) * PageSize) + 1;
    public int PageEnd => TotalRecords == 0 ? 0 : Math.Min(PageIndex * PageSize, TotalRecords);

    public void OnGet()
    {
        PagePerm = GetUserPermissions();
        NormalizeQueryInputs();
        LoadLookups();

        if (!AllowedPageSizes.Contains(PageSize))
        {
            PageSize = DefaultPageSize;
        }

        if (PageIndex <= 0)
        {
            PageIndex = 1;
        }

        if (!UseDateRange)
        {
            FromDate = null;
            ToDate = null;
        }
        else if (!FromDate.HasValue)
        {
            ToDate = null;
        }

        if (UseDateRange && FromDate.HasValue && ToDate.HasValue && FromDate.Value.Date > ToDate.Value.Date)
        {
            ModelState.AddModelError(string.Empty, "From Date must be less than or equal to To Date.");
            Rows = [];
            TotalRecords = 0;
            return;
        }

        LoadRows();
    }

    public IActionResult OnPostSearch([FromBody] SearchRequest request)
    {
        PagePerm = GetUserPermissions();
        var criteria = BuildCriteria(includePaging: false);
        criteria.PageIndex = request.Page;
        criteria.PageSize = request.PageSize;

        var (rows, totalRecords) = SearchPaged(criteria);

        return new JsonResult(new
        {
            success = true,
            data = rows,
            total = totalRecords,
            page = request.Page,
            pageSize = request.PageSize,
            totalPages = request.PageSize <= 0 ? 1 : (int)Math.Ceiling((double)totalRecords / request.PageSize)
        });
    }

    public IActionResult OnPostAddAt()
    {
        NormalizePostInputs();

        if (!AllowedPageSizes.Contains(PageSize))
        {
            PageSize = DefaultPageSize;
        }

        if (PageIndex <= 0)
        {
            PageIndex = 1;
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
            if (details[i].ItemId <= 0)
            {
                ModelState.AddModelError(string.Empty, $"Row {i + 1}: Item is required.");
            }

            if (details[i].QtyPur <= 0)
            {
                ModelState.AddModelError(string.Empty, $"Row {i + 1}: QtyPur must be greater than 0.");
            }

            if (details[i].QtyFromM < 0 || details[i].UnitPrice < 0)
            {
                ModelState.AddModelError(string.Empty, $"Row {i + 1}: QtyFromM and U.Price must be valid numbers.");
            }
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
        var criteria = BuildCriteria(includePaging: true);
        var (rows, totalRecords) = SearchPaged(criteria);
        Rows = rows;
        TotalRecords = totalRecords;

        if (TotalRecords > 0 && PageIndex > TotalPages)
        {
            PageIndex = TotalPages;
            criteria = BuildCriteria(includePaging: true);
            (rows, totalRecords) = SearchPaged(criteria);
            Rows = rows;
            TotalRecords = totalRecords;
        }
    }

    private PurchaseRequisitionFilterCriteria BuildCriteria(bool includePaging)
    {
        return new PurchaseRequisitionFilterCriteria
        {
            RequestNo = RequestNo,
            Description = Description,
            UseDateRange = UseDateRange,
            FromDate = FromDate,
            ToDate = ToDate,
            PageIndex = includePaging ? PageIndex : null,
            PageSize = includePaging ? PageSize : null
        };
    }

    private (List<PurchaseRequisitionListRowDto> rows, int totalRecords) SearchPaged(PurchaseRequisitionFilterCriteria criteria)
    {
        var rows = new List<PurchaseRequisitionListRowDto>();
        var totalRecords = 0;
        var page = criteria.PageIndex.GetValueOrDefault() <= 0 ? 1 : criteria.PageIndex!.Value;
        var pageSize = criteria.PageSize.GetValueOrDefault() <= 0 ? DefaultPageSize : criteria.PageSize!.Value;
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
            BindSearchParams(countCmd, criteria);
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

        BindSearchParams(cmd, criteria);
        cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = offset;
        cmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = pageSize;

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(new PurchaseRequisitionListRowDto
            {
                Id = Convert.ToInt32(rd["PRID"]),
                RequestCode = Convert.ToString(rd["RequestNo"]) ?? string.Empty,
                RequestDate = rd.IsDBNull(rd.GetOrdinal("RequestDate")) ? DateTime.MinValue : Convert.ToDateTime(rd["RequestDate"]),
                Description = Convert.ToString(rd["Description"]) ?? string.Empty,
                StatusId = rd.IsDBNull(rd.GetOrdinal("Status")) ? null : Convert.ToByte(rd["Status"]),
                Status = Convert.ToString(rd["StatusName"]) ?? string.Empty,
                Purchaser = Convert.ToString(rd["PurchaserCode"]) ?? string.Empty,
                ChiefA = Convert.ToString(rd["ChiefACode"]) ?? string.Empty,
                GDirector = Convert.ToString(rd["GDirectorCode"]) ?? string.Empty
            });
        }

        return (rows, totalRecords);
    }

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
            ItemList.Add(new PurchaseRequisitionItemLookupDto
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
            SupplierList.Add(new PurchaseRequisitionSupplierLookupDto
            {
                Id = Convert.ToInt32(rd["SupplierID"]),
                SupplierCode = Convert.ToString(rd["SupplierCode"]) ?? string.Empty,
                SupplierName = Convert.ToString(rd["SupplierName"]) ?? string.Empty
            });
        }
    }

    private void AddDetails(int prId, IReadOnlyList<PurchaseRequisitionDetailInputDto> details)
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

            trans.Commit();
        }
        catch
        {
            trans.Rollback();
            throw;
        }
    }

    private static void BindSearchParams(SqlCommand cmd, PurchaseRequisitionFilterCriteria criteria)
    {
        cmd.Parameters.Add("@RequestNo", SqlDbType.VarChar, 20).Value = string.IsNullOrWhiteSpace(criteria.RequestNo) ? DBNull.Value : criteria.RequestNo.Trim();
        cmd.Parameters.Add("@Description", SqlDbType.VarChar, 500).Value = string.IsNullOrWhiteSpace(criteria.Description) ? DBNull.Value : criteria.Description.Trim();
        cmd.Parameters.Add("@UseDateRange", SqlDbType.Bit).Value = criteria.UseDateRange;
        cmd.Parameters.Add("@FromDate", SqlDbType.DateTime).Value = criteria.FromDate.HasValue ? criteria.FromDate.Value.Date : DBNull.Value;
        cmd.Parameters.Add("@ToDate", SqlDbType.DateTime).Value = criteria.ToDate.HasValue ? criteria.ToDate.Value.Date : DBNull.Value;
    }

    private object BuildRouteValues() => new
    {
        RequestNo,
        Description,
        UseDateRange,
        FromDate = UseDateRange ? FromDate?.ToString("yyyy-MM-dd") : null,
        ToDate = UseDateRange && FromDate.HasValue ? ToDate?.ToString("yyyy-MM-dd") : null,
        PageIndex,
        PageSize
    };

    private void NormalizeQueryInputs()
    {
        NormalizeBoolQuery(nameof(UseDateRange), value => UseDateRange = value);

        if (Request.Query.ContainsKey(nameof(PageIndex)))
        {
            var raw = Request.Query[nameof(PageIndex)].ToString();
            PageIndex = int.TryParse(raw, out var parsed) ? parsed : 1;
            ModelState.Remove(nameof(PageIndex));
        }

        if (Request.Query.ContainsKey(nameof(PageSize)))
        {
            var raw = Request.Query[nameof(PageSize)].ToString();
            PageSize = int.TryParse(raw, out var parsed) ? parsed : DefaultPageSize;
            ModelState.Remove(nameof(PageSize));
        }

        NormalizeDateQuery(nameof(FromDate), value => FromDate = value);
        NormalizeDateQuery(nameof(ToDate), value => ToDate = value);
    }

    private void NormalizePostInputs()
    {
        NormalizeBoolForm(nameof(UseDateRange), value => UseDateRange = value);

        if (Request.HasFormContentType && Request.Form.ContainsKey(nameof(PageIndex)))
        {
            var raw = Request.Form[nameof(PageIndex)].ToString();
            PageIndex = int.TryParse(raw, out var parsed) ? parsed : 1;
            ModelState.Remove(nameof(PageIndex));
        }

        if (Request.HasFormContentType && Request.Form.ContainsKey(nameof(PageSize)))
        {
            var raw = Request.Form[nameof(PageSize)].ToString();
            PageSize = int.TryParse(raw, out var parsed) ? parsed : DefaultPageSize;
            ModelState.Remove(nameof(PageSize));
        }

        NormalizeDateForm(nameof(FromDate), value => FromDate = value);
        NormalizeDateForm(nameof(ToDate), value => ToDate = value);

        if (Request.HasFormContentType && Request.Form.ContainsKey(nameof(SelectedPrId)))
        {
            var raw = Request.Form[nameof(SelectedPrId)].ToString();
            SelectedPrId = int.TryParse(raw, out var parsed) ? parsed : null;
            ModelState.Remove(nameof(SelectedPrId));
        }
    }

    private void NormalizeDateQuery(string key, Action<DateTime?> assign)
    {
        if (!Request.Query.ContainsKey(key))
        {
            return;
        }

        var raw = Request.Query[key].ToString();
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
        if (!Request.Query.ContainsKey(key))
        {
            return;
        }

        var boolValue = false;
        foreach (var value in Request.Query[key])
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (value == "1" || value.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                boolValue = true;
                break;
            }

            if (value != "0" && bool.TryParse(value, out var parsed) && parsed)
            {
                boolValue = true;
                break;
            }
        }

        assign(boolValue);
        ModelState.Remove(key);
    }

    private void NormalizeDateForm(string key, Action<DateTime?> assign)
    {
        if (!Request.HasFormContentType || !Request.Form.ContainsKey(key))
        {
            return;
        }

        var raw = Request.Form[key].ToString();
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

    private void NormalizeBoolForm(string key, Action<bool> assign)
    {
        if (!Request.HasFormContentType || !Request.Form.ContainsKey(key))
        {
            return;
        }

        var boolValue = false;
        foreach (var value in Request.Form[key])
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (value == "1" || value.Equals("on", StringComparison.OrdinalIgnoreCase))
            {
                boolValue = true;
                break;
            }

            if (value != "0" && bool.TryParse(value, out var parsed) && parsed)
            {
                boolValue = true;
                break;
            }
        }

        assign(boolValue);
        ModelState.Remove(key);
    }

    private List<PurchaseRequisitionDetailInputDto> ParseAddAtDetails()
    {
        if (string.IsNullOrWhiteSpace(AddAtDetailsJson))
        {
            return [];
        }

        try
        {
            var details = JsonSerializer.Deserialize<List<PurchaseRequisitionDetailInputDto>>(
                AddAtDetailsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return details ?? [];
        }
        catch
        {
            ModelState.AddModelError(string.Empty, "Detail data format is invalid.");
            return [];
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
        var roleClaim = User.FindFirst("RoleID")?.Value;
        var roleId = int.Parse(roleClaim ?? "0");

        PagePerm = new PagePermissions();
        if (isAdmin)
        {
            PagePerm.AllowedNos = Enumerable.Range(1, 10).ToList();
            return PagePerm;
        }

        PagePerm.AllowedNos = _permissionService.GetPermissionsForPage(roleId, FUNCTION_ID);
        return PagePerm;
    }
}

public class SearchRequest
{
    public string? RequestNo { get; set; }
    public string? Description { get; set; }
    public bool UseDateRange { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class PurchaseRequisitionFilterCriteria
{
    public string? RequestNo { get; set; }
    public string? Description { get; set; }
    public bool UseDateRange { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int? PageIndex { get; set; }
    public int? PageSize { get; set; }
}

public class PurchaseRequisitionListRowDto
{
    public int Id { get; set; }
    public string RequestCode { get; set; } = string.Empty;
    public DateTime RequestDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public byte? StatusId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Purchaser { get; set; } = string.Empty;
    public string ChiefA { get; set; } = string.Empty;
    public string GDirector { get; set; } = string.Empty;
}

public class PurchaseRequisitionItemLookupDto
{
    public int Id { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
}

public class PurchaseRequisitionSupplierLookupDto
{
    public int Id { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
}

public class PurchaseRequisitionDetailInputDto
{
    public int ItemId { get; set; }
    public decimal QtyFromM { get; set; }
    public decimal QtyPur { get; set; }
    public decimal UnitPrice { get; set; }
    public string? Remark { get; set; }
    public int? SupplierId { get; set; }
}
