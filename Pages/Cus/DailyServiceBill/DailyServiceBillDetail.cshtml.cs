using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;

namespace SmartSam.Pages.Cus.DailyServiceBill;

public class DailyServiceBillDetailModel : BasePageModel
{
    private const int FunctionId = 42;
    private const int PermissionViewDetail = 2;
    private const int PermissionEditDetail = 4;

    private readonly PermissionService _permissionService;

    public DailyServiceBillDetailModel(IConfiguration config, PermissionService permissionService) : base(config)
    {
        _permissionService = permissionService;
    }

    [BindProperty(SupportsGet = true)]
    public int Id { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Mode { get; set; } = "view";

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true)]
    public bool Popup { get; set; }

    [BindProperty]
    public DailyServiceBillHeader Bill { get; set; } = new DailyServiceBillHeader();

    [BindProperty]
    public List<DailyServiceBillDetailRow> DetailRows { get; set; } = new List<DailyServiceBillDetailRow>();

    public PagePermissions PagePerm { get; private set; } = new PagePermissions();
    public bool IsViewMode => Popup || !string.Equals(Mode, "edit", StringComparison.OrdinalIgnoreCase);
    public bool CanEdit { get; private set; }
    public bool IsPopup => Popup;
    public string SafeReturnUrl { get; private set; } = "/Cus/DailyServiceBill";

    public IActionResult OnGet()
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewDetail))
        {
            return RedirectToPage("/Index");
        }

        SafeReturnUrl = NormalizeReturnUrl(ReturnUrl);

        using var conn = OpenConnection();
        Bill = LoadBill(conn, Id);
        if (Bill.BillID <= 0)
        {
            return NotFound();
        }

        CanEdit = !Popup && PagePerm.HasPermission(PermissionEditDetail) && Bill.BillStatus == 1;
        if (!CanEdit)
        {
            Mode = "view";
        }

        DetailRows = LoadBillDetails(conn, Id);
        return Page();
    }

    public IActionResult OnPostSave()
    {
        PagePerm = GetUserPermissions();
        SafeReturnUrl = NormalizeReturnUrl(ReturnUrl);

        if (!PagePerm.HasPermission(PermissionEditDetail))
        {
            ModelState.AddModelError(string.Empty, "You do not have permission to edit this bill.");
        }

        using var conn = OpenConnection();
        var currentBill = LoadBill(conn, Bill.BillID);
        if (currentBill.BillID <= 0)
        {
            return NotFound();
        }

        if (currentBill.BillStatus != 1)
        {
            ModelState.AddModelError(string.Empty, "Only pending bills can be edited.");
        }

        if (!ModelState.IsValid)
        {
            Bill = currentBill;
            DetailRows = LoadBillDetails(conn, currentBill.BillID);
            CanEdit = false;
            Mode = "view";
            return Page();
        }

        using var trans = conn.BeginTransaction();
        try
        {
            foreach (var row in DetailRows.Where(x => x.DetailID > 0))
            {
                using var detailCmd = new SqlCommand(@"
UPDATE dbo.SV_BillDetail
SET Quantity = @Quantity,
    Price = @Price,
    Amount = @Amount,
    Notes = @Notes
WHERE DetailID = @DetailID
  AND BillNumber = @BillID;", conn, trans);
                detailCmd.Parameters.AddWithValue("@Quantity", row.Quantity);
                detailCmd.Parameters.AddWithValue("@Price", row.Price);
                detailCmd.Parameters.AddWithValue("@Amount", row.Amount);
                detailCmd.Parameters.AddWithValue("@Notes", (object?)row.Notes ?? DBNull.Value);
                detailCmd.Parameters.AddWithValue("@DetailID", row.DetailID);
                detailCmd.Parameters.AddWithValue("@BillID", currentBill.BillID);
                detailCmd.ExecuteNonQuery();
            }

            var beforeVat = SumBillAmount(conn, trans, currentBill.BillID);
            var pctTax = currentBill.PctTax;
            var vat = pctTax > 0 ? Math.Round(beforeVat / 100 * pctTax, 2) : 0;
            var afterVat = Math.Round(beforeVat + vat, 0);

            using var billCmd = new SqlCommand(@"
UPDATE dbo.SV_Bill
SET Description = @Description,
    Note = @Note,
    ForFromDate = @ForFromDate,
    ForToDate = @ForToDate,
    IsPersonalPayment = @IsPersonalPayment,
    VNDAmountBefVAT = @VNDAmountBefVAT,
    VNDAmountVAT = @VNDAmountVAT,
    VNDAmount = @VNDAmount,
    PaidDate = NULL,
    BillStatus = 1
WHERE BillID = @BillID;", conn, trans);
            billCmd.Parameters.AddWithValue("@Description", (object?)Bill.Description ?? DBNull.Value);
            billCmd.Parameters.AddWithValue("@Note", (object?)Bill.Note ?? DBNull.Value);
            billCmd.Parameters.AddWithValue("@ForFromDate", (object?)Bill.ForFromDate ?? DBNull.Value);
            billCmd.Parameters.AddWithValue("@ForToDate", (object?)Bill.ForToDate ?? DBNull.Value);
            billCmd.Parameters.AddWithValue("@IsPersonalPayment", Bill.IsPersonalPayment);
            billCmd.Parameters.AddWithValue("@VNDAmountBefVAT", beforeVat);
            billCmd.Parameters.AddWithValue("@VNDAmountVAT", vat);
            billCmd.Parameters.AddWithValue("@VNDAmount", afterVat);
            billCmd.Parameters.AddWithValue("@BillID", currentBill.BillID);
            billCmd.ExecuteNonQuery();

            trans.Commit();
        }
        catch
        {
            trans.Rollback();
            throw;
        }

        if (Popup)
        {
            TempData["SuccessMessage"] = "Saved successfully.";
            return RedirectToPage("./DailyServiceBillDetail", new { id = currentBill.BillID, mode = Mode, returnUrl = SafeReturnUrl, popup = true });
        }

        var redirectUrl = AppendFlag(SafeReturnUrl, "billUpdated=1");
        return Redirect(redirectUrl);
    }

    private SqlConnection OpenConnection()
    {
        var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        return conn;
    }

    private DailyServiceBillHeader LoadBill(SqlConnection conn, int billId)
    {
        using var cmd = new SqlCommand(@"
SELECT b.BillID,
       b.BillDate,
       b.SalePoint,
       ISNULL(sp.SalePointName, '') AS SalePointName,
       b.CustomerID,
       ISNULL(c.CustomerName, '') AS CustomerName,
       b.CompanyID,
       ISNULL(co.CompanyName, '') AS CompanyName,
       ISNULL(b.ApartmentNo, '') AS ApartmentNo,
       ISNULL(b.Description, '') AS Description,
       ISNULL(b.Note, '') AS Note,
       ISNULL(b.PctTax, 0) AS PctTax,
       ISNULL(b.ExRate, 0) AS ExRate,
       ISNULL(b.VNDAmountBefVAT, 0) AS VNDAmountBefVAT,
       ISNULL(b.VNDAmountVAT, 0) AS VNDAmountVAT,
       ISNULL(b.VNDAmount, 0) AS VNDAmount,
       ISNULL(b.BillStatus, 0) AS BillStatus,
       ISNULL(bs.StatusName, '') AS BillStatusName,
       b.PaidDate,
       b.ForFromDate,
       b.ForToDate,
       ISNULL(b.IsPersonalPayment, 0) AS IsPersonalPayment,
       ISNULL(e.EmployeeCode, '') AS CreatedOperatorCode,
       ISNULL(e.EmployeeName, '') AS CreatedOperatorName
FROM dbo.SV_Bill b
LEFT JOIN dbo.SV_SalePoint sp ON sp.SalePointID = b.SalePoint
LEFT JOIN dbo.CM_Customer c ON c.CustomerID = b.CustomerID
LEFT JOIN dbo.CM_Company co ON co.CompanyID = b.CompanyID
LEFT JOIN dbo.SV_BillStatus bs ON bs.StatusID = b.BillStatus
LEFT JOIN dbo.MS_Employee e ON e.EmployeeID = b.CreatedUser
WHERE b.BillID = @BillID;", conn);
        cmd.Parameters.AddWithValue("@BillID", billId);

        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
        {
            return new DailyServiceBillHeader();
        }

        return new DailyServiceBillHeader
        {
            BillID = Convert.ToInt32(rd["BillID"]),
            BillDate = ToDate(rd["BillDate"]) ?? DateTime.Today,
            SalePoint = ToInt(rd["SalePoint"]),
            SalePointName = Convert.ToString(rd["SalePointName"]) ?? string.Empty,
            CustomerID = ToNullableInt(rd["CustomerID"]),
            CustomerName = Convert.ToString(rd["CustomerName"]) ?? string.Empty,
            CompanyID = ToNullableInt(rd["CompanyID"]),
            CompanyName = Convert.ToString(rd["CompanyName"]) ?? string.Empty,
            ApartmentNo = Convert.ToString(rd["ApartmentNo"]) ?? string.Empty,
            Description = Convert.ToString(rd["Description"]) ?? string.Empty,
            Note = Convert.ToString(rd["Note"]) ?? string.Empty,
            PctTax = ToDecimal(rd["PctTax"]),
            ExRate = ToDecimal(rd["ExRate"]),
            VNDAmountBefVAT = ToDecimal(rd["VNDAmountBefVAT"]),
            VNDAmountVAT = ToDecimal(rd["VNDAmountVAT"]),
            VNDAmount = ToDecimal(rd["VNDAmount"]),
            BillStatus = Convert.ToInt32(rd["BillStatus"]),
            BillStatusName = Convert.ToString(rd["BillStatusName"]) ?? string.Empty,
            PaidDate = ToDate(rd["PaidDate"]),
            ForFromDate = ToDate(rd["ForFromDate"]),
            ForToDate = ToDate(rd["ForToDate"]),
            IsPersonalPayment = ToBool(rd["IsPersonalPayment"]),
            CreatedOperatorCode = Convert.ToString(rd["CreatedOperatorCode"]) ?? string.Empty,
            CreatedOperatorName = Convert.ToString(rd["CreatedOperatorName"]) ?? string.Empty
        };
    }

    private List<DailyServiceBillDetailRow> LoadBillDetails(SqlConnection conn, int billId)
    {
        var rows = new List<DailyServiceBillDetailRow>();
        using var cmd = new SqlCommand(@"
SELECT d.DetailID,
       d.BillNumber,
       d.ServiceItem,
       ISNULL(s.ServiceName, '') AS ServiceName,
       ISNULL(d.Unit, s.Unit) AS Unit,
       ISNULL(d.Quantity, 0) AS Quantity,
       ISNULL(d.Price, 0) AS Price,
       ISNULL(d.Amount, 0) AS Amount,
       ISNULL(d.Notes, '') AS Notes
FROM dbo.SV_BillDetail d
LEFT JOIN dbo.SV_ServiceList s ON s.ServiceID = d.ServiceItem
WHERE d.BillNumber = @BillID
ORDER BY d.DetailID;", conn);
        cmd.Parameters.AddWithValue("@BillID", billId);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(new DailyServiceBillDetailRow
            {
                DetailID = Convert.ToInt32(rd["DetailID"]),
                BillNumber = Convert.ToInt32(rd["BillNumber"]),
                ServiceItem = Convert.ToInt32(rd["ServiceItem"]),
                ServiceName = Convert.ToString(rd["ServiceName"]) ?? string.Empty,
                Unit = Convert.ToString(rd["Unit"]) ?? string.Empty,
                Quantity = ToDecimal(rd["Quantity"]),
                Price = ToDecimal(rd["Price"]),
                Amount = ToDecimal(rd["Amount"]),
                Notes = Convert.ToString(rd["Notes"]) ?? string.Empty
            });
        }

        return rows;
    }

    private decimal SumBillAmount(SqlConnection conn, SqlTransaction trans, int billId)
    {
        using var cmd = new SqlCommand("SELECT ISNULL(SUM(Amount), 0) FROM dbo.SV_BillDetail WHERE BillNumber = @BillID;", conn, trans);
        cmd.Parameters.AddWithValue("@BillID", billId);
        return ToDecimal(cmd.ExecuteScalar() ?? 0);
    }

    private PagePermissions GetUserPermissions()
    {
        var isAdmin = User.FindFirst("IsAdminRole")?.Value == "True";
        var roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "0");
        var perms = new PagePermissions();
        perms.AllowedNos = isAdmin
            ? Enumerable.Range(1, 20).ToList()
            : _permissionService.GetPermissionsForPage(roleId, FunctionId);
        return perms;
    }

    private string NormalizeReturnUrl(string? value)
    {
        if (!string.IsNullOrWhiteSpace(value) && Url.IsLocalUrl(value))
        {
            return value;
        }

        return "/Cus/DailyServiceBill";
    }

    private static string AppendFlag(string url, string flag)
    {
        return url + (url.Contains('?') ? "&" : "?") + flag;
    }

    private static decimal ToDecimal(object value)
    {
        if (value == DBNull.Value || value == null)
        {
            return 0;
        }

        return Convert.ToDecimal(value);
    }

    private static int ToInt(object value)
    {
        if (value == DBNull.Value || value == null)
        {
            return 0;
        }

        return Convert.ToInt32(value);
    }

    private static int? ToNullableInt(object value)
    {
        if (value == DBNull.Value || value == null)
        {
            return null;
        }

        return Convert.ToInt32(value);
    }

    private static DateTime? ToDate(object value)
    {
        if (value == DBNull.Value || value == null)
        {
            return null;
        }

        return Convert.ToDateTime(value);
    }

    private static bool ToBool(object value)
    {
        if (value == DBNull.Value || value == null)
        {
            return false;
        }

        return Convert.ToBoolean(value);
    }
}

public class DailyServiceBillHeader
{
    public int BillID { get; set; }
    public DateTime BillDate { get; set; }
    public int SalePoint { get; set; }
    public string SalePointName { get; set; } = string.Empty;
    public int? CustomerID { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int? CompanyID { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string ApartmentNo { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public decimal PctTax { get; set; }
    public decimal ExRate { get; set; }
    public decimal VNDAmountBefVAT { get; set; }
    public decimal VNDAmountVAT { get; set; }
    public decimal VNDAmount { get; set; }
    public decimal DisplayVNDAmountVAT
    {
        get
        {
            if (VNDAmountVAT != 0)
            {
                return VNDAmountVAT;
            }

            var derivedVat = VNDAmount - VNDAmountBefVAT;
            if (derivedVat > 0)
            {
                return derivedVat;
            }

            if (PctTax > 0 && VNDAmountBefVAT > 0)
            {
                return Math.Round(VNDAmountBefVAT / 100 * PctTax, 2);
            }

            return 0;
        }
    }
    public decimal DisplayVNDAmount
    {
        get
        {
            if (VNDAmount != 0)
            {
                return VNDAmount;
            }

            return Math.Round(VNDAmountBefVAT + DisplayVNDAmountVAT, 0);
        }
    }
    public int BillStatus { get; set; }
    public string BillStatusName { get; set; } = string.Empty;
    public DateTime? PaidDate { get; set; }
    public DateTime? ForFromDate { get; set; }
    public DateTime? ForToDate { get; set; }
    public bool IsPersonalPayment { get; set; }
    public string CreatedOperatorCode { get; set; } = string.Empty;
    public string CreatedOperatorName { get; set; } = string.Empty;
}

public class DailyServiceBillDetailRow
{
    public int DetailID { get; set; }
    public int BillNumber { get; set; }
    public int ServiceItem { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal Amount { get; set; }
    public string Notes { get; set; } = string.Empty;
}
