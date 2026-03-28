using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Helpers;
using SmartSam.Pages;
using SmartSam.Services;
using SmartSam.Services.Interfaces;

namespace SmartSam.Pages.Purchasing.PurchaseRequisition;

public class PurchaseRequisitionDetailModel : BasePageModel
{
    private const int FUNCTION_ID = 72;
    private const int PermissionViewDetail = 2;
    private const int PermissionAdd = 3;
    private const int PermissionEdit = 4;
    private readonly PermissionService _permissionService;
    private readonly ISecurityService _securityService;
    private readonly ILogger<PurchaseRequisitionDetailModel> _logger;

    public PurchaseRequisitionDetailModel(IConfiguration config, PermissionService permissionService, ISecurityService securityService, ILogger<PurchaseRequisitionDetailModel> logger) : base(config)
    {
        _permissionService = permissionService;
        _securityService = securityService;
        _logger = logger;
    }

    public PagePermissions PagePerm { get; private set; } = new PagePermissions();
    public bool CanSave { get; set; }
    public bool IsViewMode => string.Equals(Mode, "view", StringComparison.OrdinalIgnoreCase);

    [BindProperty(SupportsGet = true)]
    public string Mode { get; set; } = "add";

    [BindProperty]
    public PurchaseRequisitionHeader Requisition { get; set; } = new PurchaseRequisitionHeader();

    [BindProperty]
    public string DetailsJson { get; set; } = "[]";

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public string MessageType { get; set; } = "info";

    public List<SelectListItem> StatusList { get; set; } = new List<SelectListItem>();
    public List<PurchaseRequisitionItemLookup> ItemList { get; set; } = new List<PurchaseRequisitionItemLookup>();
    public List<PurchaseRequisitionSupplierLookup> SupplierList { get; set; } = new List<PurchaseRequisitionSupplierLookup>();

    public IActionResult OnGet(int? id, string mode = "view")
    {
        // 1. Lấy quyền của trang trước khi xử lý dữ liệu chi tiết.
        PagePerm = GetUserPermissions();
        Mode = string.IsNullOrWhiteSpace(mode) ? "view" : mode.Trim().ToLowerInvariant();

        // 2. Luôn nạp dữ liệu dropdown để màn hình hiển thị đầy đủ khi add, edit hoặc view.
        LoadAllDropdowns();

        if (id.HasValue && id.Value > 0)
        {
            // 3. Trường hợp mở bản ghi sẵn có thì phải nạp header và detail trước.
            LoadPurchaseRequisition(id.Value);
            if (Requisition.Id <= 0)
            {
                return NotFound();
            }

            // 4. Quyền xem và sửa phải bám theo quyền hiệu lực của đúng trạng thái chứng từ.
            var effectivePermissions = GetEffectivePermissionsByStatus(Requisition.Status);
            if (!effectivePermissions.Contains(PermissionViewDetail))
            {
                TempData["Message"] = "You have no permission to access this requisition.";
                TempData["MessageType"] = "warning";
                return RedirectToPage("./Index");
            }

            if (Mode == "edit" && !effectivePermissions.Contains(PermissionEdit))
            {
                TempData["Message"] = "You have no permission to edit this requisition.";
                TempData["MessageType"] = "warning";
                return RedirectToPage("./Index");
            }
        }
        else
        {
            // 5. Trường hợp tạo mới phải kiểm tra quyền Add theo trạng thái khởi tạo mặc định.
            var effectivePermissions = GetEffectivePermissionsByStatus(1);
            if (!effectivePermissions.Contains(PermissionAdd))
            {
                TempData["Message"] = "You have no permission to add requisition.";
                TempData["MessageType"] = "warning";
                return RedirectToPage("./Index");
            }

            Mode = "add";
            Requisition = new PurchaseRequisitionHeader
            {
                RequestNo = GetSuggestedRequestNo(DateTime.Today),
                RequestDate = DateTime.Today,
                Currency = 1,
                Status = StatusList.Count > 0 && byte.TryParse(StatusList[0].Value, out var statusId) ? statusId : (byte)1
            };
            DetailsJson = "[]";
        }

        // 6. Cờ CanSave quyết định form có được phép nhập liệu và lưu hay không.
        CanSave = Mode == "add"
            ? GetEffectivePermissionsByStatus(1).Contains(PermissionAdd)
            : Mode == "edit" && GetEffectivePermissionsByStatus(Requisition.Status).Contains(PermissionEdit);

        return Page();
    }

    public IActionResult OnPost()
    {
        // 1. Lấy quyền trước khi lưu để chặn submit trực tiếp không đúng quyền.
        PagePerm = GetUserPermissions();
        LoadAllDropdowns();

        var isNew = Requisition.Id <= 0;
        Mode = isNew ? "add" : (string.IsNullOrWhiteSpace(Mode) ? "edit" : Mode.Trim().ToLowerInvariant());

        // 2. Quyền lưu phụ thuộc vào việc đang tạo mới hay đang sửa bản ghi hiện hữu.
        var effectivePermissions = isNew
            ? GetEffectivePermissionsByStatus(1)
            : GetEffectivePermissionsByStatus(Requisition.Status);
        if (isNew && !effectivePermissions.Contains(PermissionAdd) || !isNew && !effectivePermissions.Contains(PermissionEdit))
        {
            ModelState.AddModelError(string.Empty, "You have no permission to save requisition.");
            CanSave = false;
            return Page();
        }

        CanSave = !IsViewMode;

        // 3. Parse các dòng chi tiết và kiểm tra dữ liệu bắt buộc trước khi lưu.
        var details = ParseDetails();
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
            return Page();
        }

        try
        {
            // 4. Header và detail luôn được lưu trong cùng một transaction để tránh lệch dữ liệu.
            SavePurchaseRequisition(details);
            TempData["Message"] = "Purchase requisition saved successfully.";
            TempData["MessageType"] = "success";
            return RedirectToPage("./PurchaseRequisitionDetail", new { id = Requisition.Id, mode = "edit" });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot save purchase requisition.");
            ModelState.AddModelError(string.Empty, $"Cannot save purchase requisition. {ex.Message}");
            return Page();
        }
    }

    private void LoadAllDropdowns()
    {
        // 1. Màn hình chi tiết dùng chung 3 nhóm dropdown chính.
        LoadStatusList();
        LoadItemList();
        LoadSupplierList();
    }

    private void LoadPurchaseRequisition(int id)
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        // 1. Nạp thông tin header của phiếu đề nghị mua hàng.
        using (var cmd = new SqlCommand(@"
SELECT PRID, ISNULL(RequestNo, '') AS RequestNo, RequestDate, ISNULL([Description], '') AS [Description],
       ISNULL(Currency, 1) AS Currency, ISNULL([Status], 1) AS [Status]
FROM dbo.PC_PR
WHERE PRID = @PRID", conn))
        {
            cmd.Parameters.Add("@PRID", SqlDbType.Int).Value = id;
            using var rd = cmd.ExecuteReader();
            if (rd.Read())
            {
                Requisition = new PurchaseRequisitionHeader
                {
                    Id = Convert.ToInt32(rd["PRID"]),
                    RequestNo = Convert.ToString(rd["RequestNo"]) ?? string.Empty,
                    RequestDate = rd.IsDBNull(rd.GetOrdinal("RequestDate")) ? DateTime.Today : Convert.ToDateTime(rd["RequestDate"]),
                    Description = Convert.ToString(rd["Description"]) ?? string.Empty,
                    Currency = Convert.ToInt32(rd["Currency"]),
                    Status = Convert.ToByte(rd["Status"])
                };
            }
        }

        if (Requisition.Id > 0)
        {
            // 2. Sau khi có header thì nạp tiếp các dòng vật tư chi tiết để đổ ra bảng item.
            var details = LoadDetailRows(conn, Requisition.Id);
            DetailsJson = JsonSerializer.Serialize(details);
        }
    }

    private List<PurchaseRequisitionDetailInput> LoadDetailRows(SqlConnection conn, int prId)
    {
        var rows = new List<PurchaseRequisitionDetailInput>();

        using var cmd = new SqlCommand(@"
SELECT d.RecordID,
       d.ItemID,
       ISNULL(i.ItemCode, '') AS ItemCode,
       ISNULL(i.ItemName, '') AS ItemName,
       ISNULL(i.Unit, '') AS Unit,
       ISNULL(d.SugQty, 0) AS QtyFromM,
       ISNULL(d.Quantity, 0) AS QtyPur,
       ISNULL(d.UnitPrice, 0) AS UnitPrice,
       ISNULL(d.Remark, '') AS Remark,
       d.SupplierID,
       CASE WHEN s.SupplierID IS NULL THEN '' ELSE ISNULL(s.SupplierCode, '') + ' - ' + ISNULL(s.SupplierName, '') END AS SupplierText
FROM dbo.PC_PRDetail d
LEFT JOIN dbo.INV_ItemList i ON d.ItemID = i.ItemID
LEFT JOIN dbo.PC_Suppliers s ON d.SupplierID = s.SupplierID
WHERE d.PRID = @PRID
ORDER BY d.RecordID", conn);

        cmd.Parameters.Add("@PRID", SqlDbType.Int).Value = prId;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(new PurchaseRequisitionDetailInput
            {
                DetailId = rd.IsDBNull(rd.GetOrdinal("RecordID")) ? 0 : Convert.ToInt64(rd["RecordID"]),
                ItemId = Convert.ToInt32(rd["ItemID"]),
                ItemCode = Convert.ToString(rd["ItemCode"]) ?? string.Empty,
                ItemName = Convert.ToString(rd["ItemName"]) ?? string.Empty,
                Unit = Convert.ToString(rd["Unit"]) ?? string.Empty,
                QtyFromM = Convert.ToDecimal(rd["QtyFromM"]),
                QtyPur = Convert.ToDecimal(rd["QtyPur"]),
                UnitPrice = Convert.ToDecimal(rd["UnitPrice"]),
                Remark = Convert.ToString(rd["Remark"]),
                SupplierId = rd.IsDBNull(rd.GetOrdinal("SupplierID")) ? null : Convert.ToInt32(rd["SupplierID"]),
                SupplierText = Convert.ToString(rd["SupplierText"]) ?? string.Empty
            });
        }

        return rows;
    }

    private void LoadStatusList()
    {
        // 1. Trạng thái hiển thị đúng theo bảng cấu hình PC_PRStatus.
        StatusList = LoadListFromSql(
            "SELECT PRStatusID, PRStatusName FROM dbo.PC_PRStatus ORDER BY PRStatusID",
            "PRStatusID",
            "PRStatusName");
    }

    private void LoadItemList()
    {
        // 1. Chỉ nạp các vật tư được phép dùng cho nghiệp vụ mua hàng.
        ItemList = new List<PurchaseRequisitionItemLookup>();
        const string sql = @"
SELECT TOP 200 ItemID, ISNULL(ItemCode, '') AS ItemCode, ISNULL(ItemName, '') AS ItemName, ISNULL(Unit, '') AS Unit
FROM dbo.INV_ItemList
WHERE ISNULL(IsPurchase, 0) = 1
ORDER BY ItemCode";

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(sql, conn);
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
        // 1. Danh sách supplier phục vụ cho popup thêm chi tiết chỉ lấy các supplier chưa bị xóa.
        SupplierList = new List<PurchaseRequisitionSupplierLookup>();
        const string sql = @"
SELECT TOP 200 SupplierID, ISNULL(SupplierCode, '') AS SupplierCode, ISNULL(SupplierName, '') AS SupplierName
FROM dbo.PC_Suppliers
WHERE ISNULL(IsDeleted, 0) = 0
ORDER BY SupplierCode";

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(sql, conn);
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

    private string GetSuggestedRequestNo(DateTime requestDate)
    {
        // 1. Mã PR được sinh theo mẫu PRxx/MMyy để đồng bộ với dữ liệu đang có.
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

    private void SavePurchaseRequisition(IReadOnlyList<PurchaseRequisitionDetailInput> details)
    {
        // 1. Người thao tác hiện tại được dùng để xác định Purchaser mặc định khi tạo mới.
        var operatorCode = User.Identity?.Name?.Trim() ?? string.Empty;
        var isNew = Requisition.Id <= 0;

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var trans = conn.BeginTransaction();

        try
        {
            // 2. RequestNo là duy nhất, không được phép trùng với bản ghi khác.
            using (var checkCmd = new SqlCommand(@"
SELECT COUNT(1)
FROM dbo.PC_PR
WHERE RequestNo = @RequestNo
  AND (@PRID = 0 OR PRID <> @PRID)", conn, trans))
            {
                checkCmd.Parameters.Add("@RequestNo", SqlDbType.VarChar, 20).Value = Requisition.RequestNo.Trim();
                checkCmd.Parameters.Add("@PRID", SqlDbType.Int).Value = Requisition.Id;
                var exists = Convert.ToInt32(checkCmd.ExecuteScalar() ?? 0);
                if (exists > 0)
                {
                    throw new InvalidOperationException("Request No. already exists.");
                }
            }

            if (isNew)
            {
                // 3. Khi tạo mới, hệ thống tự gán Purchaser theo tài khoản đăng nhập.
                var purId = ResolvePurchaserId(conn, trans, operatorCode);
                using var headerCmd = new SqlCommand(@"
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
SELECT CAST(SCOPE_IDENTITY() AS int);", conn, trans);

                headerCmd.Parameters.Add("@RequestNo", SqlDbType.VarChar, 20).Value = Requisition.RequestNo.Trim();
                headerCmd.Parameters.Add("@RequestDate", SqlDbType.DateTime).Value = Requisition.RequestDate.Date;
                headerCmd.Parameters.Add("@Description", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(Requisition.Description) ? DBNull.Value : Requisition.Description.Trim();
                headerCmd.Parameters.Add("@Currency", SqlDbType.Int).Value = Requisition.Currency;
                headerCmd.Parameters.Add("@Status", SqlDbType.TinyInt).Value = Requisition.Status;
                headerCmd.Parameters.Add("@PurId", SqlDbType.Int).Value = purId.HasValue ? purId.Value : DBNull.Value;
                Requisition.Id = Convert.ToInt32(headerCmd.ExecuteScalar());
            }
            else
            {
                // 4. Khi sửa, header được cập nhật lại và toàn bộ detail cũ được thay bằng danh sách mới.
                using var headerCmd = new SqlCommand(@"
UPDATE dbo.PC_PR
SET RequestNo = @RequestNo,
    RequestDate = @RequestDate,
    [Description] = @Description,
    Currency = @Currency,
    [Status] = @Status,
    edited = 1
WHERE PRID = @PRID", conn, trans);

                headerCmd.Parameters.Add("@PRID", SqlDbType.Int).Value = Requisition.Id;
                headerCmd.Parameters.Add("@RequestNo", SqlDbType.VarChar, 20).Value = Requisition.RequestNo.Trim();
                headerCmd.Parameters.Add("@RequestDate", SqlDbType.DateTime).Value = Requisition.RequestDate.Date;
                headerCmd.Parameters.Add("@Description", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(Requisition.Description) ? DBNull.Value : Requisition.Description.Trim();
                headerCmd.Parameters.Add("@Currency", SqlDbType.Int).Value = Requisition.Currency;
                headerCmd.Parameters.Add("@Status", SqlDbType.TinyInt).Value = Requisition.Status;
                headerCmd.ExecuteNonQuery();

                using var deleteCmd = new SqlCommand("DELETE FROM dbo.PC_PRDetail WHERE PRID = @PRID", conn, trans);
                deleteCmd.Parameters.Add("@PRID", SqlDbType.Int).Value = Requisition.Id;
                deleteCmd.ExecuteNonQuery();
            }

            // 5. Sau khi lưu header thì ghi lại toàn bộ detail hiện tại của chứng từ.
            foreach (var detail in details)
            {
                IndexModel.InsertDetail(conn, trans, Requisition.Id, detail);
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
        // 1. Ưu tiên lấy chính nhân viên đang đăng nhập làm người tạo phiếu.
        // 2. Nếu không tìm thấy thì fallback về các mã đang được dùng để test trong hệ thống.
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

    private List<PurchaseRequisitionDetailInput> ParseDetails()
    {
        if (string.IsNullOrWhiteSpace(DetailsJson)) return new List<PurchaseRequisitionDetailInput>();

        try
        {
            var details = JsonSerializer.Deserialize<List<PurchaseRequisitionDetailInput>>(DetailsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return details ?? new List<PurchaseRequisitionDetailInput>();
        }
        catch
        {
            ModelState.AddModelError(string.Empty, "Detail data format is invalid.");
            return new List<PurchaseRequisitionDetailInput>();
        }
    }

    private void ValidateDetail(PurchaseRequisitionDetailInput detail, int rowNo)
    {
        // 1. Mỗi dòng chi tiết bắt buộc có Item và QtyPur lớn hơn 0 trước khi lưu.
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

    private PagePermissions GetUserPermissions()
    {
        var isAdmin = IsAdminRole();
        var roleId = GetCurrentRoleId();
        var permsObj = new PagePermissions();

        if (isAdmin)
        {
            // 1. Admin được cấp danh sách quyền đầy đủ để giao diện mở toàn bộ chức năng.
            permsObj.AllowedNos = Enumerable.Range(1, 20).ToList();
        }
        else
        {
            // 2. User thường lấy quyền tĩnh của page theo RoleID và FunctionID.
            permsObj.AllowedNos = _permissionService.GetPermissionsForPage(roleId, FUNCTION_ID);
        }

        return permsObj;
    }

    private List<int> GetEffectivePermissionsByStatus(int status)
    {
        var isAdmin = IsAdminRole();
        var roleId = GetCurrentRoleId();

        if (isAdmin)
        {
            // 1. Admin luôn có đầy đủ quyền hiệu lực trên mọi trạng thái.
            return Enumerable.Range(1, 20).ToList();
        }

        // 2. User thường phải lấy quyền hiệu lực qua SecurityService theo trạng thái hiện tại.
        return _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, status);
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

public class PurchaseRequisitionHeader
{
    public int Id { get; set; }

    [Required]
    [StringLength(20)]
    public string RequestNo { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    public DateTime RequestDate { get; set; } = DateTime.Today;

    [StringLength(500)]
    public string? Description { get; set; }

    public int Currency { get; set; } = 1;
    public byte Status { get; set; }
}
