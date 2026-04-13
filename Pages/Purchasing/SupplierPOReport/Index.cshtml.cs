using System.Data;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;

namespace SmartSam.Pages.Purchasing.SupplierPOReport;

public class IndexModel : BasePageModel
{
    private const int FUNCTION_ID = 74;
    private const int PermissionViewList = 1;
    private readonly PermissionService _permissionService;

    public IndexModel(IConfiguration config, PermissionService permissionService) : base(config)
    {
        _permissionService = permissionService;
    }

    public PagePermissions PagePerm { get; private set; } = new();
    public int DefaultPageSize => _config.GetValue<int?>("AppSettings:DefaultPageSize") ?? 10;
    public IReadOnlyList<int> PageSizeOptions => GetConfiguredPageSizeOptions();

    [BindProperty(SupportsGet = true)]
    public SupplierPOReportFilter Filter { get; set; } = new();

    public List<SupplierPOReportRow> Rows { get; set; } = new();
    public int TotalRecords { get; set; }
    public decimal TotalBeforeVat { get; set; }
    public decimal TotalAfterVat { get; set; }
    public string TotalBeforeVatText => FormatNumber(TotalBeforeVat);
    public string TotalAfterVatText => FormatNumber(TotalAfterVat);
    public int TotalPages => Filter.PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)Filter.PageSize));
    public int PageStart => TotalRecords == 0 ? 0 : ((Filter.PageNumber - 1) * Filter.PageSize) + 1;
    public int PageEnd => TotalRecords == 0 ? 0 : Math.Min(Filter.PageNumber * Filter.PageSize, TotalRecords);
    public bool HasPreviousPage => Filter.PageNumber > 1;
    public bool HasNextPage => Filter.PageNumber < TotalPages;

    public IActionResult OnGet()
    {
        PagePerm = GetUserPermissions();
        if (!HasPermission(PermissionViewList))
        {
            return Redirect("/");
        }

        NormalizeFilter();
        LoadRows();
        return Page();
    }

    public IActionResult OnGetExportExcel()
    {
        PagePerm = GetUserPermissions();
        if (!HasPermission(PermissionViewList))
        {
            return Redirect("/");
        }

        NormalizeFilter();
        var exportFilter = CloneFilterForExport();
        var (rows, _, totalBeforeVat, totalAfterVat) = SearchRows(exportFilter);

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Supplier PO Report");
        var headers = new[]
        {
            "SupplierCode",
            "SupplierName",
            "PONo",
            "PODate",
            "Remark",
            "PRNo",
            "dept",
            "ItemCode",
            "ItemName",
            "Quantity",
            "unitPrice",
            "POAmount",
            "PerVAT",
            "AfterVAT"
        };

        for (var i = 0; i < headers.Length; i++)
        {
            worksheet.Cell(1, i + 1).Value = headers[i];
        }

        var rowIndex = 2;
        foreach (var row in rows)
        {
            worksheet.Cell(rowIndex, 1).Value = row.SupplierCode;
            worksheet.Cell(rowIndex, 2).Value = row.SupplierName;
            worksheet.Cell(rowIndex, 3).Value = row.PONo;
            worksheet.Cell(rowIndex, 4).Value = row.PODateText;
            worksheet.Cell(rowIndex, 5).Value = row.Remark;
            worksheet.Cell(rowIndex, 6).Value = row.PRNo;
            worksheet.Cell(rowIndex, 7).Value = row.Dept;
            worksheet.Cell(rowIndex, 8).Value = row.ItemCode;
            worksheet.Cell(rowIndex, 9).Value = row.ItemName;
            worksheet.Cell(rowIndex, 10).Value = row.Quantity;
            worksheet.Cell(rowIndex, 11).Value = row.UnitPrice;
            worksheet.Cell(rowIndex, 12).Value = row.POAmount;
            worksheet.Cell(rowIndex, 13).Value = row.PerVAT;
            worksheet.Cell(rowIndex, 14).Value = row.AfterVAT;
            rowIndex++;
        }

        worksheet.Cell(rowIndex, 11).Value = "Total:";
        worksheet.Cell(rowIndex, 12).Value = totalBeforeVat;
        worksheet.Cell(rowIndex, 14).Value = totalAfterVat;

        worksheet.Column(10).Style.NumberFormat.Format = "#,##0";
        worksheet.Column(11).Style.NumberFormat.Format = "#,##0";
        worksheet.Column(12).Style.NumberFormat.Format = "#,##0";
        worksheet.Column(13).Style.NumberFormat.Format = "#,##0";
        worksheet.Column(14).Style.NumberFormat.Format = "#,##0";

        var usedRange = worksheet.Range(1, 1, Math.Max(1, rowIndex), headers.Length);
        usedRange.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        usedRange.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        usedRange.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        worksheet.Row(1).Style.Font.Bold = true;
        worksheet.Row(rowIndex).Style.Font.Bold = true;
        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"supplier_po_report_{DateTime.Now:yyyyMMddHHmmss}.xlsx");
    }

    private void LoadRows()
    {
        var (rows, totalRecords, totalBeforeVat, totalAfterVat) = SearchRows(Filter);
        Rows = rows;
        TotalRecords = totalRecords;
        TotalBeforeVat = totalBeforeVat;
        TotalAfterVat = totalAfterVat;

        if (TotalRecords > 0 && Filter.PageNumber > TotalPages)
        {
            Filter.PageNumber = TotalPages;
            (rows, totalRecords, totalBeforeVat, totalAfterVat) = SearchRows(Filter);
            Rows = rows;
            TotalRecords = totalRecords;
            TotalBeforeVat = totalBeforeVat;
            TotalAfterVat = totalAfterVat;
        }
    }

    private (List<SupplierPOReportRow> rows, int totalRecords, decimal totalBeforeVat, decimal totalAfterVat) SearchRows(SupplierPOReportFilter filter)
    {
        var rows = new List<SupplierPOReportRow>();
        var whereParts = new List<string>();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = conn.CreateCommand();

        BuildWhereClause(filter, whereParts, cmd);

        var whereSql = whereParts.Count == 0 ? string.Empty : $"WHERE {string.Join(" AND ", whereParts)}";
        cmd.CommandText = $@"
SELECT COUNT(1) AS TotalRecords,
       ISNULL(SUM(ISNULL(d.POAmount, 0)), 0) AS TotalBeforeVat,
       ISNULL(SUM(ISNULL(d.POAmount, 0) + (ISNULL(d.POAmount, 0) * ISNULL(p.PerVAT, 0) / 100.0)), 0) AS TotalAfterVat
FROM dbo.PC_PO p
INNER JOIN dbo.PC_PODetail d ON d.POID = p.POID
INNER JOIN dbo.PC_Suppliers s ON s.SupplierID = p.SupplierID
LEFT JOIN dbo.PC_PR pr ON pr.PRID = p.PRID
LEFT JOIN dbo.MATERIAL_REQUEST mr ON mr.REQUEST_NO = d.MRNo
LEFT JOIN dbo.INV_KPGroup kg ON kg.KPGroupID = mr.STORE_GROUP
INNER JOIN dbo.INV_ItemList i ON i.ItemID = d.ItemID
{whereSql};

SELECT
    s.SupplierID,
    ISNULL(s.SupplierCode, '') AS SupplierCode,
    ISNULL(s.SupplierName, '') AS SupplierName,
    ISNULL(p.PONo, '') AS PONo,
    p.PODate,
    ISNULL(p.Remark, '') AS Remark,
    ISNULL(pr.RequestNo, '') AS PRNo,
    LEFT(ISNULL(kg.KPGroupName, ''), 2) AS Dept,
    ISNULL(i.ItemCode, '') AS ItemCode,
    ISNULL(i.ItemName, '') AS ItemName,
    ISNULL(i.Unit, '') AS Unit,
    ISNULL(d.Quantity, 0) AS Quantity,
    ISNULL(d.UnitPrice, 0) AS UnitPrice,
    ISNULL(d.POAmount, 0) AS POAmount,
    ISNULL(p.PerVAT, 0) AS PerVAT,
    ISNULL(d.POAmount, 0) + (ISNULL(d.POAmount, 0) * ISNULL(p.PerVAT, 0) / 100.0) AS AfterVAT
FROM dbo.PC_PO p
INNER JOIN dbo.PC_PODetail d ON d.POID = p.POID
INNER JOIN dbo.PC_Suppliers s ON s.SupplierID = p.SupplierID
LEFT JOIN dbo.PC_PR pr ON pr.PRID = p.PRID
LEFT JOIN dbo.MATERIAL_REQUEST mr ON mr.REQUEST_NO = d.MRNo
LEFT JOIN dbo.INV_KPGroup kg ON kg.KPGroupID = mr.STORE_GROUP
INNER JOIN dbo.INV_ItemList i ON i.ItemID = d.ItemID
{whereSql}
ORDER BY p.PODate DESC, p.POID DESC, d.RecordID DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;";

        cmd.Parameters.Add("@Offset", SqlDbType.Int).Value = (filter.PageNumber - 1) * filter.PageSize;
        cmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = filter.PageSize;

        conn.Open();
        using var reader = cmd.ExecuteReader();

        var totalRecords = 0;
        var totalBeforeVat = 0m;
        var totalAfterVat = 0m;

        if (reader.Read())
        {
            totalRecords = reader.IsDBNull(reader.GetOrdinal("TotalRecords")) ? 0 : Convert.ToInt32(reader["TotalRecords"]);
            totalBeforeVat = reader.IsDBNull(reader.GetOrdinal("TotalBeforeVat")) ? 0 : Convert.ToDecimal(reader["TotalBeforeVat"]);
            totalAfterVat = reader.IsDBNull(reader.GetOrdinal("TotalAfterVat")) ? 0 : Convert.ToDecimal(reader["TotalAfterVat"]);
        }

        if (reader.NextResult())
        {
            while (reader.Read())
            {
                var quantity = reader.IsDBNull(reader.GetOrdinal("Quantity")) ? 0 : Convert.ToDecimal(reader["Quantity"]);
                var unitPrice = reader.IsDBNull(reader.GetOrdinal("UnitPrice")) ? 0 : Convert.ToDecimal(reader["UnitPrice"]);
                var poAmount = reader.IsDBNull(reader.GetOrdinal("POAmount")) ? 0 : Convert.ToDecimal(reader["POAmount"]);
                var perVat = reader.IsDBNull(reader.GetOrdinal("PerVAT")) ? 0 : Convert.ToDecimal(reader["PerVAT"]);
                var afterVat = reader.IsDBNull(reader.GetOrdinal("AfterVAT")) ? 0 : Convert.ToDecimal(reader["AfterVAT"]);

                rows.Add(new SupplierPOReportRow
                {
                    SupplierId = reader.IsDBNull(reader.GetOrdinal("SupplierID")) ? 0 : Convert.ToInt32(reader["SupplierID"]),
                    SupplierCode = Convert.ToString(reader["SupplierCode"]) ?? string.Empty,
                    SupplierName = Convert.ToString(reader["SupplierName"]) ?? string.Empty,
                    PONo = Convert.ToString(reader["PONo"]) ?? string.Empty,
                    PODate = reader.IsDBNull(reader.GetOrdinal("PODate")) ? null : Convert.ToDateTime(reader["PODate"]),
                    Remark = Convert.ToString(reader["Remark"]) ?? string.Empty,
                    PRNo = Convert.ToString(reader["PRNo"]) ?? string.Empty,
                    Dept = Convert.ToString(reader["Dept"]) ?? string.Empty,
                    ItemCode = Convert.ToString(reader["ItemCode"]) ?? string.Empty,
                    ItemName = Convert.ToString(reader["ItemName"]) ?? string.Empty,
                    Unit = Convert.ToString(reader["Unit"]) ?? string.Empty,
                    Quantity = quantity,
                    UnitPrice = unitPrice,
                    POAmount = poAmount,
                    PerVAT = perVat,
                    AfterVAT = afterVat
                });
            }
        }

        return (rows, totalRecords, totalBeforeVat, totalAfterVat);
    }

    private void BuildWhereClause(SupplierPOReportFilter filter, List<string> whereParts, SqlCommand cmd)
    {
        if (filter.UseDateRange && filter.FromDate.HasValue)
        {
            whereParts.Add("CAST(p.PODate AS date) >= @FromDate");
            cmd.Parameters.Add("@FromDate", SqlDbType.Date).Value = filter.FromDate.Value.Date;
        }

        if (filter.UseDateRange && filter.ToDate.HasValue)
        {
            whereParts.Add("CAST(p.PODate AS date) <= @ToDate");
            cmd.Parameters.Add("@ToDate", SqlDbType.Date).Value = filter.ToDate.Value.Date;
        }

        if (!string.IsNullOrWhiteSpace(filter.SupplierCode))
        {
            whereParts.Add("s.SupplierCode LIKE @SupplierCode");
            cmd.Parameters.Add("@SupplierCode", SqlDbType.NVarChar, 50).Value = $"%{filter.SupplierCode.Trim()}%";
        }

        if (!string.IsNullOrWhiteSpace(filter.SupplierName))
        {
            whereParts.Add("s.SupplierName LIKE @SupplierName");
            cmd.Parameters.Add("@SupplierName", SqlDbType.NVarChar, 250).Value = $"%{filter.SupplierName.Trim()}%";
        }

        if (!string.IsNullOrWhiteSpace(filter.Business))
        {
            whereParts.Add("s.Business LIKE @Business");
            cmd.Parameters.Add("@Business", SqlDbType.NVarChar, 250).Value = $"%{filter.Business.Trim()}%";
        }

        if (!string.IsNullOrWhiteSpace(filter.PRNo))
        {
            whereParts.Add("pr.RequestNo LIKE @PRNo");
            cmd.Parameters.Add("@PRNo", SqlDbType.NVarChar, 50).Value = $"%{filter.PRNo.Trim()}%";
        }

        if (!string.IsNullOrWhiteSpace(filter.PONo))
        {
            whereParts.Add("p.PONo LIKE @PONo");
            cmd.Parameters.Add("@PONo", SqlDbType.NVarChar, 50).Value = $"%{filter.PONo.Trim()}%";
        }

        if (!string.IsNullOrWhiteSpace(filter.ItemCode))
        {
            whereParts.Add("i.ItemCode LIKE @ItemCode");
            cmd.Parameters.Add("@ItemCode", SqlDbType.NVarChar, 50).Value = $"%{filter.ItemCode.Trim()}%";
        }
    }

    private void NormalizeFilter()
    {
        Filter.SupplierCode = Filter.SupplierCode?.Trim();
        Filter.SupplierName = Filter.SupplierName?.Trim();
        Filter.Business = Filter.Business?.Trim();
        Filter.PONo = Filter.PONo?.Trim();
        Filter.PRNo = Filter.PRNo?.Trim();
        Filter.ItemCode = Filter.ItemCode?.Trim();
        Filter.PageNumber = Filter.PageNumber <= 0 ? 1 : Filter.PageNumber;
        Filter.PageSize = NormalizePageSize(Filter.PageSize);

        if (!Filter.UseDateRange)
        {
            Filter.FromDate = null;
            Filter.ToDate = null;
            return;
        }

        Filter.FromDate ??= new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        Filter.ToDate ??= DateTime.Today;
        if (Filter.FromDate > Filter.ToDate)
        {
            Filter.ToDate = Filter.FromDate;
        }
    }

    private SupplierPOReportFilter CloneFilterForExport()
    {
        return new SupplierPOReportFilter
        {
            SupplierCode = Filter.SupplierCode,
            SupplierName = Filter.SupplierName,
            Business = Filter.Business,
            PONo = Filter.PONo,
            PRNo = Filter.PRNo,
            ItemCode = Filter.ItemCode,
            UseDateRange = Filter.UseDateRange,
            FromDate = Filter.FromDate,
            ToDate = Filter.ToDate,
            PageNumber = 1,
            PageSize = int.MaxValue
        };
    }

    private IReadOnlyList<int> GetConfiguredPageSizeOptions()
    {
        var configured = _config.GetSection("AppSettings:PageSizeOptions").Get<int[]>() ?? Array.Empty<int>();
        var options = configured.Where(value => value > 0).Distinct().ToList();
        if (options.Count == 0)
        {
            options = new List<int> { DefaultPageSize, 20, 25, 30, 35 }.Distinct().ToList();
        }

        if (!options.Contains(DefaultPageSize))
        {
            options.Insert(0, DefaultPageSize);
        }

        return options;
    }

    private int NormalizePageSize(int pageSize)
    {
        if (pageSize <= 0)
        {
            return DefaultPageSize;
        }

        return PageSizeOptions.Contains(pageSize) ? pageSize : DefaultPageSize;
    }

    private string FormatNumber(decimal value)
    {
        return value.ToString("#,##0.###");
    }

    private bool HasPermission(int permissionNo) => PagePerm.HasPermission(permissionNo);

    private PagePermissions GetUserPermissions()
    {
        var permsObj = new PagePermissions();
        if (IsAdminRole())
        {
            permsObj.AllowedNos = Enumerable.Range(1, 20).ToList();
        }
        else
        {
            permsObj.AllowedNos = _permissionService.GetPermissionsForPage(GetCurrentRoleId(), FUNCTION_ID);
        }

        return permsObj;
    }

    private int GetCurrentRoleId()
    {
        return int.Parse(User.FindFirst("RoleID")?.Value ?? "0");
    }

    private bool IsAdminRole()
    {
        return User.FindFirst("IsAdminRole")?.Value == "True";
    }
}

public class SupplierPOReportFilter
{
    public string? SupplierCode { get; set; }
    public string? SupplierName { get; set; }
    public string? Business { get; set; }
    public string? PONo { get; set; }
    public string? PRNo { get; set; }
    public string? ItemCode { get; set; }
    public bool UseDateRange { get; set; } = true;
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class SupplierPOReportRow
{
    public int SupplierId { get; set; }
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public string PONo { get; set; } = string.Empty;
    public DateTime? PODate { get; set; }
    public string PODateText => PODate?.ToString("MM/dd/yyyy") ?? string.Empty;
    public string Remark { get; set; } = string.Empty;
    public string PRNo { get; set; } = string.Empty;
    public string Dept { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal POAmount { get; set; }
    public decimal PerVAT { get; set; }
    public decimal AfterVAT { get; set; }
    public string QuantityText => Quantity.ToString("#,##0.###");
    public string UnitPriceText => UnitPrice.ToString("#,##0.###");
    public string POAmountText => POAmount.ToString("#,##0.###");
    public string PerVATText => PerVAT.ToString("#,##0.###");
    public string AfterVATText => AfterVAT.ToString("#,##0.###");
}
