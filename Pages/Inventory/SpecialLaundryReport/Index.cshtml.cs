using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using ClosedXML.Excel;
using SmartSam.Pages;
using SmartSam.Services;
using SmartSam.Services.Interfaces;

namespace SmartSam.Pages.Inventory.SpecialLaundryReport;

public class IndexModel : BasePageModel
{
    private const int FunctionId = 148;
    private static readonly DateTime DefaultFromDate = new DateTime(2019, 4, 1);

    private readonly ISecurityService _securityService;

    public IndexModel(IConfiguration config, ISecurityService securityService) : base(config)
    {
        _securityService = securityService;
    }

    public PagePermissions PagePerm { get; private set; } = new PagePermissions();

    [BindProperty(SupportsGet = true)]
    public string? ReportMode { get; set; } = "TotalInMonth";

    [BindProperty(SupportsGet = true)]
    public DateTime? FromDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? ToDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? MonthDate { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? BetweenFrom { get; set; }

    [BindProperty(SupportsGet = true)]
    public DateTime? BetweenTo { get; set; }

    [BindProperty(SupportsGet = true)]
    public string SortBy { get; set; } = "Apartment";

    public List<string> SelectedApartments { get; set; } = new List<string>();

    public List<SelectListItem> ApartmentOptions { get; set; } = new List<SelectListItem>();

    public List<ContractRow> ContractRows { get; set; } = new List<ContractRow>();
    public List<DeliveryRow> DeliveryRows { get; set; } = new List<DeliveryRow>();
    public List<DeliveryAggRow> DeliveryAggRows { get; set; } = new List<DeliveryAggRow>();

    public bool HasData { get; set; }

    public IActionResult OnGet()
    {
        PagePerm = GetUserPermissions();
        if (!HasPageAccess())
        {
            return RedirectToPage("/Index");
        }

        ApplyDefaultValues();
        var fromDate = FromDate ?? DefaultFromDate;
        var toDate = ToDate ?? DateTime.Today;
        LoadApartments(fromDate, toDate);
        SelectedApartments = ApartmentOptions.Select(x => x.Value).ToList();

        return Page();
    }

    public IActionResult OnPostPreview(
        [FromForm] string reportMode,
        [FromForm] string sortBy,
        [FromForm] string? selectedApartments,
        [FromForm] DateTime? fromDate,
        [FromForm] DateTime? toDate,
        [FromForm] DateTime? monthDate,
        [FromForm] DateTime? betweenFrom,
        [FromForm] DateTime? betweenTo)
    {
        PagePerm = GetUserPermissions();
        if (!HasPageAccess())
        {
            return Redirect("/");
        }

        ReportMode = reportMode ?? "TotalInMonth";
        SortBy = sortBy ?? "Apartment";
        FromDate = fromDate ?? DefaultFromDate;
        ToDate = toDate ?? DateTime.Today;
        MonthDate = monthDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        BetweenFrom = betweenFrom ?? MonthDate;
        BetweenTo = betweenTo ?? DateTime.Today;
        SelectedApartments = ParseSelectedApartments(selectedApartments);

        using var conn = OpenConnection();

        // Load apartment options for the filter panel
        LoadApartments(conn, FromDate.Value, ToDate.Value);

        // Load contract section
        ContractRows = LoadContractRows(conn, FromDate.Value, ToDate.Value);

        // Load delivery section based on report mode
        DeliveryRows = new List<DeliveryRow>();
        DeliveryAggRows = new List<DeliveryAggRow>();
        HasData = false;

        if (SelectedApartments.Count > 0)
        {
            if (ReportMode == "TotalInMonth")
            {
                DeliveryAggRows = LoadDeliveryAggRows(conn, MonthDate.Value);
                HasData = DeliveryAggRows.Count > 0;
            }
            else
            {
                DateTime filterFrom;
                DateTime filterTo;

                if (ReportMode == "Total")
                {
                    // Total mode: no date filter on deliveries
                    filterFrom = DateTime.MinValue;
                    filterTo = DateTime.MaxValue;
                }
                else
                {
                    // Between mode: use date range
                    filterFrom = BetweenFrom.Value.Date;
                    filterTo = BetweenTo.Value.Date.AddDays(1).AddTicks(-1);
                }

                DeliveryRows = LoadDeliveryRows(conn, filterFrom, filterTo);
                HasData = DeliveryRows.Count > 0;
            }
        }

        return Page();
    }

    public IActionResult OnPostExport(
        [FromForm] string reportMode,
        [FromForm] string sortBy,
        [FromForm] string? selectedApartments,
        [FromForm] DateTime? fromDate,
        [FromForm] DateTime? toDate,
        [FromForm] DateTime? monthDate,
        [FromForm] DateTime? betweenFrom,
        [FromForm] DateTime? betweenTo)
    {
        PagePerm = GetUserPermissions();
        if (!HasPageAccess())
        {
            return RedirectToPage("/Index");
        }

        var effReportMode = reportMode ?? "TotalInMonth";
        var effSortBy = sortBy ?? "Apartment";
        var effFromDate = fromDate ?? DefaultFromDate;
        var effToDate = toDate ?? DateTime.Today;
        var effMonthDate = monthDate ?? new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        var effBetweenFrom = betweenFrom ?? effMonthDate;
        var effBetweenTo = betweenTo ?? DateTime.Today;
        var apartments = ParseSelectedApartments(selectedApartments);

        ReportMode = effReportMode;
        SortBy = effSortBy;
        FromDate = effFromDate;
        ToDate = effToDate;
        MonthDate = effMonthDate;
        BetweenFrom = effBetweenFrom;
        BetweenTo = effBetweenTo;
        SelectedApartments = apartments;

        using var conn = OpenConnection();

        // Load contract data
        var contracts = LoadContractRows(conn, effFromDate, effToDate);

        // Load delivery data
        var deliveryRows = new List<DeliveryRow>();
        var deliveryAggRows = new List<DeliveryAggRow>();

        if (apartments.Count > 0)
        {
            if (effReportMode == "TotalInMonth")
            {
                deliveryAggRows = LoadDeliveryAggRows(conn, effMonthDate);
            }
            else
            {
                DateTime filterFrom;
                DateTime filterTo;

                if (effReportMode == "Total")
                {
                    filterFrom = DateTime.MinValue;
                    filterTo = DateTime.MaxValue;
                }
                else
                {
                    filterFrom = effBetweenFrom.Date;
                    filterTo = effBetweenTo.Date.AddDays(1).AddTicks(-1);
                }

                deliveryRows = LoadDeliveryRows(conn, filterFrom, filterTo);
            }
        }

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Special Laundry Report");

        worksheet.Cell(1, 1).Value = $"Special Laundry Report ({effFromDate:dd/MM/yyyy} - {effToDate:dd/MM/yyyy})";
        worksheet.Range(1, 1, 1, 10).Merge();
        worksheet.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        worksheet.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        worksheet.Cell(1, 1).Style.Font.FontSize = 20;
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 1).Style.Font.FontColor = XLColor.Blue;
        worksheet.Row(1).Height = 50;

        int currentRow = 2;
        string[] contractHeaders = {
            "ContractNo", "AmptNo", "Con.From", "Con.To",
            "Con.Status", "Service From", "Ser.To", "Ser.Charge",
            "MaxQuantity", "Ser.Notes"
        };

        WriteHeaderRow(worksheet, currentRow, contractHeaders);
        currentRow++;

        foreach (var contract in contracts)
        {
            worksheet.Cell(currentRow, 1).Value = contract.ContractNo;
            worksheet.Cell(currentRow, 2).Value = contract.ApartmentNo;
            worksheet.Cell(currentRow, 3).Value = contract.ContractFromDate;
            worksheet.Cell(currentRow, 3).Style.NumberFormat.Format = "dd/MM/yyyy";
            worksheet.Cell(currentRow, 4).Value = contract.ContractToDate;
            worksheet.Cell(currentRow, 4).Style.NumberFormat.Format = "dd/MM/yyyy";
            worksheet.Cell(currentRow, 5).Value = contract.StatusName;
            worksheet.Cell(currentRow, 6).Value = contract.ServiceFromDate;
            worksheet.Cell(currentRow, 6).Style.NumberFormat.Format = "dd/MM/yyyy";
            worksheet.Cell(currentRow, 7).Value = contract.ServiceToDate;
            worksheet.Cell(currentRow, 7).Style.NumberFormat.Format = "dd/MM/yyyy";
            worksheet.Cell(currentRow, 8).Value = contract.ChargeIntervalText;
            worksheet.Cell(currentRow, 9).Value = contract.MaxQuantity;
            worksheet.Cell(currentRow, 10).Value = contract.Notes;

            for (int i = 1; i <= 10; i++)
            {
                worksheet.Cell(currentRow, i).Style.Font.FontSize = 10;
                worksheet.Cell(currentRow, i).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            currentRow++;
        }

        currentRow += 2;
        worksheet.Cell(currentRow, 1).Value = BuildOptionText(effReportMode, effMonthDate, effBetweenFrom, effBetweenTo);
        worksheet.Range(currentRow, 1, currentRow, 10).Merge();
        worksheet.Cell(currentRow, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        worksheet.Cell(currentRow, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        worksheet.Cell(currentRow, 1).Style.Font.FontSize = 12;
        worksheet.Cell(currentRow, 1).Style.Font.Bold = true;
        worksheet.Cell(currentRow, 1).Style.Font.FontColor = XLColor.Red;

        if (effReportMode == "TotalInMonth")
        {
            currentRow++;
            WriteHeaderRow(worksheet, currentRow, new[] { "Location", "Total item(s)" });
            currentRow++;

            foreach (var agg in deliveryAggRows)
            {
                worksheet.Cell(currentRow, 1).Value = agg.ApartmentNo;
                worksheet.Cell(currentRow, 2).Value = agg.TotalQuantity;
                worksheet.Cell(currentRow, 2).Style.NumberFormat.Format = "#,##0";

                for (int i = 1; i <= 2; i++)
                {
                    worksheet.Cell(currentRow, i).Style.Font.FontSize = 10;
                    worksheet.Cell(currentRow, i).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }

                currentRow++;
            }
        }
        else
        {
            currentRow++;
            string[] deliveryHeaders = {
                "DeliveryID", "Location", "LinnenCode", "Quantity",
                "Price", "Amount", "DeliveryDate"
            };

            WriteHeaderRow(worksheet, currentRow, deliveryHeaders);
            currentRow++;

            foreach (var delivery in deliveryRows)
            {
                worksheet.Cell(currentRow, 1).Value = delivery.DeliveryId;
                worksheet.Cell(currentRow, 2).Value = delivery.ApartmentNo;
                worksheet.Cell(currentRow, 3).Value = delivery.LinenCode;
                worksheet.Cell(currentRow, 4).Value = delivery.Quantity;
                worksheet.Cell(currentRow, 4).Style.NumberFormat.Format = "#,##0";
                worksheet.Cell(currentRow, 5).Value = delivery.Price;
                worksheet.Cell(currentRow, 5).Style.NumberFormat.Format = "#,##0";
                worksheet.Cell(currentRow, 6).Value = delivery.Amount;
                worksheet.Cell(currentRow, 6).Style.NumberFormat.Format = "#,##0";
                worksheet.Cell(currentRow, 7).Value = delivery.DeliveryDate;
                worksheet.Cell(currentRow, 7).Style.NumberFormat.Format = "dd/MM/yyyy";

                for (int i = 1; i <= 7; i++)
                {
                    worksheet.Cell(currentRow, i).Style.Font.FontSize = 10;
                    worksheet.Cell(currentRow, i).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }

                currentRow++;
            }

            if (deliveryRows.Count > 0)
            {
                worksheet.Cell(currentRow, 4).Value = deliveryRows.Sum(d => d.Quantity);
                worksheet.Cell(currentRow, 4).Style.NumberFormat.Format = "#,##0";
                worksheet.Cell(currentRow, 4).Style.Font.Bold = true;
                worksheet.Cell(currentRow, 5).Value = deliveryRows.Sum(d => d.Price);
                worksheet.Cell(currentRow, 5).Style.NumberFormat.Format = "#,##0";
                worksheet.Cell(currentRow, 5).Style.Font.Bold = true;
                worksheet.Cell(currentRow, 6).Value = deliveryRows.Sum(d => d.Amount);
                worksheet.Cell(currentRow, 6).Style.NumberFormat.Format = "#,##0";
                worksheet.Cell(currentRow, 6).Style.Font.Bold = true;
            }
        }

        worksheet.Columns().AdjustToContents();
        worksheet.Row(2).Height = 22;

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var fileName = $"SpecialLaundryReport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private void ApplyDefaultValues()
    {
        FromDate ??= DefaultFromDate;
        ToDate ??= DateTime.Today;
        MonthDate ??= new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        BetweenFrom ??= MonthDate;
        BetweenTo ??= DateTime.Today;
        ReportMode ??= "TotalInMonth";
        SortBy ??= "Apartment";
    }

    private static List<string> ParseSelectedApartments(string? selectedApartments)
    {
        if (string.IsNullOrWhiteSpace(selectedApartments))
        {
            return new List<string>();
        }

        return selectedApartments.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
    }

    private static string BuildOptionText(string reportMode, DateTime monthDate, DateTime betweenFrom, DateTime betweenTo)
    {
        if (reportMode == "Total")
        {
            return $"Special Laundry option: Total from 04/01/2019 to {DateTime.Now:dd/MM/yyyy}";
        }

        if (reportMode == "TotalInMonth")
        {
            return $"Special Laundry option: Total in {monthDate.Month}/{monthDate.Year}";
        }

        return $"Special Laundry option: Between date from {betweenFrom:dd/MM/yyyy} to {betweenTo:dd/MM/yyyy}";
    }

    private static void WriteHeaderRow(IXLWorksheet worksheet, int row, IReadOnlyList<string> headers)
    {
        for (int i = 0; i < headers.Count; i++)
        {
            var cell = worksheet.Cell(row, i + 1);
            cell.Value = headers[i];
            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Font.FontSize = 12;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = XLColor.Blue;
        }
    }

    private void LoadApartments(SqlConnection conn, DateTime fromDate, DateTime toDate)
    {
        ApartmentOptions = new List<SelectListItem>();

        var sql = @"
SELECT DISTINCT c.CurrentApartmentNo
FROM dbo.CM_Contract c
INNER JOIN dbo.CM_ContractService cs ON c.ContractID = cs.ContractID
WHERE cs.ServiceID IN (1217, 1219)
  AND c.ContractStatus IN (1, 2, 3)
  AND @FromDate <= cs.ServiceFromDate
  AND @ToDate >= cs.ServiceFromDate
  AND @FromDate <= cs.ServiceToDate
  AND @ToDate <= cs.ServiceToDate
ORDER BY c.CurrentApartmentNo;";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@FromDate", SqlDbType.DateTime).Value = fromDate;
        cmd.Parameters.Add("@ToDate", SqlDbType.DateTime).Value = toDate;

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var aptNo = Convert.ToString(rd["CurrentApartmentNo"]) ?? string.Empty;
            ApartmentOptions.Add(new SelectListItem
            {
                Value = aptNo,
                Text = aptNo
            });
        }
    }

    private void LoadApartments(DateTime fromDate, DateTime toDate)
    {
        using var conn = OpenConnection();
        LoadApartments(conn, fromDate, toDate);
    }

    private List<ContractRow> LoadContractRows(SqlConnection conn, DateTime fromDate, DateTime toDate)
    {
        var rows = new List<ContractRow>();
        var orderBy = SortBy == "Apartment" ? "c.CurrentApartmentNo" : "cs.ServiceFromDate";

        var sql = $@"
SELECT c.ContractNo,
       c.CurrentApartmentNo,
       c.ContractFromDate,
       c.ContractToDate,
       cs.ServiceFromDate,
       cs.ServiceToDate,
       cs.ChargeInterval,
       cs.MaxQuantity,
       cs.Notes,
       st.statusName
FROM dbo.CM_Contract c
INNER JOIN dbo.CM_ContractService cs ON c.ContractID = cs.ContractID
INNER JOIN dbo.CM_ContractStatus st ON c.ContractStatus = st.statusID
WHERE cs.ServiceID IN (1217, 1219)
  AND c.ContractStatus IN (1, 2, 3)
  AND @FromDate <= cs.ServiceFromDate
  AND @ToDate >= cs.ServiceFromDate
  AND @FromDate <= cs.ServiceToDate
  AND @ToDate <= cs.ServiceToDate
  {BuildApartmentFilterSql()}
ORDER BY {orderBy};";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@FromDate", SqlDbType.DateTime).Value = fromDate;
        cmd.Parameters.Add("@ToDate", SqlDbType.DateTime).Value = toDate;
        AddApartmentFilterParams(cmd);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(new ContractRow
            {
                ContractNo = Convert.ToString(rd["ContractNo"]) ?? string.Empty,
                ApartmentNo = Convert.ToString(rd["CurrentApartmentNo"]) ?? string.Empty,
                ContractFromDate = rd["ContractFromDate"] == DBNull.Value
                    ? DateTime.MinValue
                    : Convert.ToDateTime(rd["ContractFromDate"]),
                ContractToDate = rd["ContractToDate"] == DBNull.Value
                    ? DateTime.MinValue
                    : Convert.ToDateTime(rd["ContractToDate"]),
                StatusName = Convert.ToString(rd["statusName"]) ?? string.Empty,
                ServiceFromDate = rd["ServiceFromDate"] == DBNull.Value
                    ? DateTime.MinValue
                    : Convert.ToDateTime(rd["ServiceFromDate"]),
                ServiceToDate = rd["ServiceToDate"] == DBNull.Value
                    ? DateTime.MinValue
                    : Convert.ToDateTime(rd["ServiceToDate"]),
                ChargeInterval = ToInt(rd["ChargeInterval"]),
                ChargeIntervalText = MapChargeInterval(ToInt(rd["ChargeInterval"])),
                MaxQuantity = rd["MaxQuantity"] == DBNull.Value
                    ? 0
                    : ToDecimal(rd["MaxQuantity"]),
                Notes = Convert.ToString(rd["Notes"]) ?? string.Empty
            });
        }

        return rows;
    }

    private List<DeliveryRow> LoadDeliveryRows(SqlConnection conn, DateTime fromDate, DateTime toDate)
    {
        var rows = new List<DeliveryRow>();
        var orderBy = SortBy == "Apartment" ? "p.ApartmentNo" : "b.ID";

        var dateFilter = "";
        if (fromDate > DateTime.MinValue && toDate < DateTime.MaxValue)
        {
            dateFilter = " AND a.DeliveryDate >= @FilterFromDate AND a.DeliveryDate <= @FilterToDate";
        }

        var sql = $@"
SELECT b.DeliveryID,
       p.ApartmentNo,
       l.LinnenCode,
       b.Quantity,
       b.Price,
       b.Amount,
       a.DeliveryDate
FROM dbo.LN_DeliveryMT a
INNER JOIN dbo.LN_DeliveryDT b ON a.DeliveryID = b.DeliveryID
INNER JOIN dbo.AM_Apmt p ON b.LocationID = p.ApmtID
INNER JOIN dbo.LN_Linnen l ON b.LinnenID = l.ID
WHERE a.IsSpecialLaundry = 1
  {BuildApartmentFilterSql("p.ApartmentNo")}
  {dateFilter}
ORDER BY {orderBy};";

        using var cmd = new SqlCommand(sql, conn);

        if (fromDate > DateTime.MinValue && toDate < DateTime.MaxValue)
        {
            cmd.Parameters.Add("@FilterFromDate", SqlDbType.DateTime).Value = fromDate;
            cmd.Parameters.Add("@FilterToDate", SqlDbType.DateTime).Value = toDate;
        }

        AddApartmentFilterParams(cmd);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(new DeliveryRow
            {
                DeliveryId = ToInt(rd["DeliveryID"]),
                ApartmentNo = Convert.ToString(rd["ApartmentNo"]) ?? string.Empty,
                LinenCode = Convert.ToString(rd["LinnenCode"]) ?? string.Empty,
                Quantity = ToDecimal(rd["Quantity"]),
                Price = ToDecimal(rd["Price"]),
                Amount = ToDecimal(rd["Amount"]),
                DeliveryDate = rd["DeliveryDate"] == DBNull.Value
                    ? DateTime.MinValue
                    : Convert.ToDateTime(rd["DeliveryDate"])
            });
        }

        return rows;
    }

    private List<DeliveryAggRow> LoadDeliveryAggRows(SqlConnection conn, DateTime monthDate)
    {
        var rows = new List<DeliveryAggRow>();

        DateTime monthStart = new DateTime(monthDate.Year, monthDate.Month, 1);
        DateTime monthEnd = monthDate.Month < 12
            ? new DateTime(monthDate.Year, monthDate.Month + 1, 1)
            : new DateTime(monthDate.Year + 1, 1, 1);

        var sql = $@"
SELECT p.ApartmentNo,
       SUM(b.Quantity) as TotalQuantity
FROM dbo.LN_DeliveryMT a
INNER JOIN dbo.LN_DeliveryDT b ON a.DeliveryID = b.DeliveryID
INNER JOIN dbo.AM_Apmt p ON b.LocationID = p.ApmtID
WHERE a.IsSpecialLaundry = 1
  AND a.DeliveryDate >= @MonthStart
  AND a.DeliveryDate < @MonthEnd
  {BuildApartmentFilterSql("p.ApartmentNo")}
GROUP BY p.ApartmentNo
ORDER BY p.ApartmentNo;";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@MonthStart", SqlDbType.DateTime).Value = monthStart;
        cmd.Parameters.Add("@MonthEnd", SqlDbType.DateTime).Value = monthEnd;
        AddApartmentFilterParams(cmd);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(new DeliveryAggRow
            {
                ApartmentNo = Convert.ToString(rd["ApartmentNo"]) ?? string.Empty,
                TotalQuantity = ToDecimal(rd["TotalQuantity"])
            });
        }

        return rows;
    }

    private string BuildApartmentFilterSql(string columnName = "c.CurrentApartmentNo")
    {
        if (SelectedApartments.Count == 0)
        {
            return "";
        }

        var placeholders = SelectedApartments.Select((_, i) => $"@Apt{i}")
            .ToArray();
        return $" AND {columnName} IN ({string.Join(", ", placeholders)})";
    }

    private void AddApartmentFilterParams(SqlCommand cmd)
    {
        for (int i = 0; i < SelectedApartments.Count; i++)
        {
            cmd.Parameters.Add($"@Apt{i}", SqlDbType.NVarChar, 50).Value = SelectedApartments[i];
        }
    }

    private static string MapChargeInterval(int interval)
    {
        return interval switch
        {
            1 => "Monthly",
            2 => "Daily",
            3 => "Once",
            4 => "30 nights",
            _ => "Unknown"
        };
    }

    private static int ToInt(object? value)
    {
        if (value == null || value == DBNull.Value) return 0;
        return Convert.ToInt32(value);
    }

    private static decimal ToDecimal(object? value)
    {
        if (value == null || value == DBNull.Value) return 0m;
        return Convert.ToDecimal(value);
    }

    private SqlConnection OpenConnection()
    {
        var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        return conn;
    }

    private PagePermissions GetUserPermissions()
    {
        var roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");
        var perms = new PagePermissions();
        perms.AllowedNos = _securityService.GetEffectivePermissions(FunctionId, roleId, 1);
        return perms;
    }

    private bool HasPageAccess()
    {
        return PagePerm.AllowedNos.Count > 0;
    }
}

public class ContractRow
{
    public string ContractNo { get; set; } = string.Empty;
    public string ApartmentNo { get; set; } = string.Empty;
    public DateTime ContractFromDate { get; set; }
    public DateTime ContractToDate { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public DateTime ServiceFromDate { get; set; }
    public DateTime ServiceToDate { get; set; }
    public int ChargeInterval { get; set; }
    public string ChargeIntervalText { get; set; } = string.Empty;
    public decimal MaxQuantity { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class DeliveryRow
{
    public int DeliveryId { get; set; }
    public string ApartmentNo { get; set; } = string.Empty;
    public string LinenCode { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal Amount { get; set; }
    public DateTime DeliveryDate { get; set; }
}

public class DeliveryAggRow
{
    public string ApartmentNo { get; set; } = string.Empty;
    public decimal TotalQuantity { get; set; }
}
