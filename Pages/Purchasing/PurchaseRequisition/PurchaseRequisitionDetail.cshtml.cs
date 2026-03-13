using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Helpers;
using SmartSam.Pages;

namespace SmartSam.Pages.Purchasing.PurchaseRequisition;

public class PurchaseRequisitionDetailModel : BasePageModel
{
    public PurchaseRequisitionDetailModel(IConfiguration config) : base(config) { }

    [BindProperty(SupportsGet = true)]
    public string Mode { get; set; } = "add";

    [BindProperty]
    [Required]
    [StringLength(20)]
    public string RequestNo { get; set; } = string.Empty;

    [BindProperty]
    [DataType(DataType.Date)]
    public DateTime RequestDate { get; set; } = DateTime.Today;

    [BindProperty]
    [StringLength(500)]
    public string? Description { get; set; }

    [BindProperty]
    public int Currency { get; set; } = 1;

    [BindProperty]
    public byte Status { get; set; }

    [BindProperty]
    public string DetailsJson { get; set; } = "[]";

    public List<SelectListItem> StatusList { get; set; } = [];
    public List<PurchaseRequisitionItemLookupDto> ItemList { get; set; } = [];
    public List<PurchaseRequisitionSupplierLookupDto> SupplierList { get; set; } = [];

    public string? Message { get; set; }
    public string MessageType { get; set; } = "info";

    public void OnGet()
    {
        LoadAllDropdowns();
        RequestNo = GetSuggestedRequestNo(RequestDate);
        if (StatusList.Count > 0)
        {
            Status = byte.TryParse(StatusList[0].Value, out var statusId) ? statusId : (byte)0;
        }
    }

    public IActionResult OnPost()
    {
        LoadAllDropdowns();

        var details = ParseDetails();
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
            return Page();
        }

        try
        {
            SavePurchaseRequisition(details);
            TempData["SuccessMessage"] = "Purchase requisition saved successfully.";
            return RedirectToPage("./Index");
        }
        catch (InvalidOperationException ex)
        {
            SetMessage(ex.Message, "error");
            return Page();
        }
        catch (Exception ex)
        {
            SetMessage($"Cannot save purchase requisition. {ex.Message}", "error");
            return Page();
        }
    }

    private void LoadAllDropdowns()
    {
        LoadStatusList();
        LoadItemList();
        LoadSupplierList();
    }

    private void LoadStatusList()
    {
        StatusList = [];
        StatusList = LoadListFromSql(
            "SELECT PRStatusID, PRStatusName FROM dbo.PC_PRStatus ORDER BY PRStatusID",
            "PRStatusID",
            "PRStatusName");
    }

    private void LoadItemList()
    {
        ItemList = [];
        const string sql = @"
SELECT TOP 200
    ItemID,
    ISNULL(ItemCode, '') AS ItemCode,
    ISNULL(ItemName, '') AS ItemName,
    ISNULL(Unit, '') AS Unit
FROM dbo.INV_ItemList
WHERE ISNULL(IsPurchase, 0) = 1
ORDER BY ItemCode";

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(sql, conn);
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
        const string sql = @"
SELECT TOP 200
    SupplierID,
    ISNULL(SupplierCode, '') AS SupplierCode,
    ISNULL(SupplierName, '') AS SupplierName
FROM dbo.PC_Suppliers
ORDER BY SupplierCode";

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(sql, conn);
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

    private string GetSuggestedRequestNo(DateTime requestDate)
    {
        var suffix = requestDate.ToString("MMyy");
        var prefix = $"PR%/{suffix}";

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
SELECT ISNULL(MAX(TRY_CONVERT(int, SUBSTRING(RequestNo, 3, CHARINDEX('/', RequestNo + '/') - 3))), 0)
FROM dbo.PC_PR
WHERE RequestNo LIKE @Prefix", conn);

        cmd.Parameters.Add("@Prefix", SqlDbType.VarChar, 20).Value = prefix;
        conn.Open();

        var maxNo = Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
        return $"PR{(maxNo + 1):00}/{suffix}";
    }

    private void SavePurchaseRequisition(IReadOnlyList<PurchaseRequisitionDetailInputDto> details)
    {
        var operatorCode = User.Identity?.Name?.Trim() ?? string.Empty;

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var trans = conn.BeginTransaction();

        try
        {
            using (var checkCmd = new SqlCommand("SELECT COUNT(1) FROM dbo.PC_PR WHERE RequestNo = @RequestNo", conn, trans))
            {
                checkCmd.Parameters.Add("@RequestNo", SqlDbType.VarChar, 20).Value = RequestNo.Trim();
                var exists = Convert.ToInt32(checkCmd.ExecuteScalar() ?? 0);
                if (exists > 0)
                {
                    throw new InvalidOperationException("Request No. already exists.");
                }
            }

            var purId = ResolvePurchaserId(conn, trans, operatorCode);

            int prId;
            using (var headerCmd = new SqlCommand(@"
INSERT INTO dbo.PC_PR
(
    RequestNo, RequestDate, [Description], Currency, [Status], IsAuto, MRNo, PostPO,
    PurId, PurApproDate, CAId, CAApproDate, GDId, GDApproDate, noted, edited
)
VALUES
(
    @RequestNo, @RequestDate, @Description, @Currency, @Status, 0, NULL, 0,
    @PurId, NULL, NULL, NULL, NULL, NULL, NULL, 0
);
SELECT CAST(SCOPE_IDENTITY() AS int);", conn, trans))
            {
                headerCmd.Parameters.Add("@RequestNo", SqlDbType.VarChar, 20).Value = RequestNo.Trim();
                headerCmd.Parameters.Add("@RequestDate", SqlDbType.DateTime).Value = RequestDate.Date;
                headerCmd.Parameters.Add("@Description", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(Description) ? DBNull.Value : Description.Trim();
                headerCmd.Parameters.Add("@Currency", SqlDbType.Int).Value = Currency;
                headerCmd.Parameters.Add("@Status", SqlDbType.TinyInt).Value = Status;
                headerCmd.Parameters.Add("@PurId", SqlDbType.Int).Value = purId.HasValue ? purId.Value : DBNull.Value;
                prId = Convert.ToInt32(headerCmd.ExecuteScalar());
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

    private static int? ResolvePurchaserId(SqlConnection conn, SqlTransaction trans, string operatorCode)
    {
        using var cmd = new SqlCommand(@"
SELECT TOP 1 EmployeeID
FROM dbo.MS_Employee
WHERE EmployeeCode = @OperatorCode;

IF @@ROWCOUNT = 0
BEGIN
    SELECT TOP 1 EmployeeID
    FROM dbo.MS_Employee
    WHERE EmployeeCode IN ('FD031', 'FD031X')
    ORDER BY CASE WHEN EmployeeCode = 'FD031' THEN 0 ELSE 1 END;
END", conn, trans);

        cmd.Parameters.Add("@OperatorCode", SqlDbType.VarChar, 50).Value = string.IsNullOrWhiteSpace(operatorCode) ? DBNull.Value : operatorCode;
        var result = cmd.ExecuteScalar();
        return result == null || result == DBNull.Value ? null : Convert.ToInt32(result);
    }

    private List<PurchaseRequisitionDetailInputDto> ParseDetails()
    {
        if (string.IsNullOrWhiteSpace(DetailsJson))
        {
            return [];
        }

        try
        {
            var details = JsonSerializer.Deserialize<List<PurchaseRequisitionDetailInputDto>>(
                DetailsJson,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            return details ?? [];
        }
        catch
        {
            ModelState.AddModelError(string.Empty, "Detail data format is invalid.");
            return [];
        }
    }

    private void SetMessage(string message, string type)
    {
        Message = message;
        MessageType = type;
    }
}
