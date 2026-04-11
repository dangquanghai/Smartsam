using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Pages;
using SmartSam.Services;
using SmartSam.Services.Interfaces;

namespace SmartSam.Pages.Purchasing.PurchaseOrder;

public class PurchaseOrderDetailModel : BasePageModel
{
    private const int FUNCTION_ID = 73;
    private const int PermissionView = 2;
    private const int PermissionAdd = 3;
    private const int PermissionEdit = 4;
    private const int PermissionBackToProcessing = 6;
    private const int PermissionEvaluate = 7;
    private readonly PermissionService _permissionService;
    private readonly ISecurityService _securityService;
    private readonly ILogger<PurchaseOrderDetailModel> _logger;
    private PurchaseOrderWorkflowUser _workflowUser = new PurchaseOrderWorkflowUser();

    public PurchaseOrderDetailModel(IConfiguration config, PermissionService permissionService, ISecurityService securityService, ILogger<PurchaseOrderDetailModel> logger)
        : base(config)
    {
        _permissionService = permissionService;
        _securityService = securityService;
        _logger = logger;
    }

    public PagePermissions PagePerm { get; private set; } = new PagePermissions();
    public bool CanSave { get; private set; }
    public bool CanPurchaserApprove { get; private set; }
    public bool CanEvaluate { get; private set; }
    public bool CanApprove { get; private set; }
    public bool CanBackToProcessing { get; private set; }
    public bool OpenConvertModal { get; private set; }
    public bool IsViewMode => string.Equals(Mode, "view", StringComparison.OrdinalIgnoreCase);
    public string BackToListUrl => string.IsNullOrWhiteSpace(ReturnUrl) ? Url.Page("./Index") ?? "./Index" : ReturnUrl;
    public string DepartmentOptionsJson => JsonSerializer.Serialize(DepartmentOptions.Select(x => new { value = x.Value, text = x.Text }));
    public string CurrentSupplierText { get; private set; } = string.Empty;
    public string CurrentPrText { get; private set; } = string.Empty;

    [BindProperty(SupportsGet = true)]
    public string Mode { get; set; } = "add";

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    public PurchaseOrderHeader Header { get; set; } = new PurchaseOrderHeader();

    [BindProperty]
    public string DetailsJson { get; set; } = "[]";

    [BindProperty]
    public int EstimatePoint { get; set; }

    [BindProperty]
    public string? ConvertReason { get; set; }

    public List<PurchaseOrderDetailInput> Details { get; set; } = new List<PurchaseOrderDetailInput>();
    public List<SelectListItem> StatusOptions { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> CurrencyOptions { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> AssessLevelOptions { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> DepartmentOptions { get; set; } = new List<SelectListItem>();

    public IActionResult OnGet(int? id, string mode = "view", string? returnUrl = null, bool openConvertModal = false)
    {
        PagePerm = GetUserPermissions();
        Mode = string.IsNullOrWhiteSpace(mode) ? "view" : mode.Trim().ToLowerInvariant();
        ReturnUrl = returnUrl;
        OpenConvertModal = openConvertModal;
        LoadAllDropdowns();
        LoadWorkflowUser();

        if (id.HasValue && id.Value > 0)
        {
            LoadPurchaseOrder(id.Value);
            if (Header.Id <= 0)
            {
                return NotFound();
            }

            var effectivePermissions = GetEffectivePermissionsByStatus(Header.StatusId);
            if (!effectivePermissions.Contains(PermissionView))
            {
                TempData["SuccessMessage"] = "You have no permission to access this purchase order.";
                return RedirectToPage("./Index");
            }

            if (Mode == "edit" && !effectivePermissions.Contains(PermissionEdit))
            {
                Mode = "view";
            }
        }
        else
        {
            var effectivePermissions = GetEffectivePermissionsByStatus(1);
            if (!effectivePermissions.Contains(PermissionAdd))
            {
                TempData["SuccessMessage"] = "You have no permission to add purchase order.";
                return RedirectToPage("./Index");
            }

            Mode = "add";
            Header = new PurchaseOrderHeader
            {
                PONo = GetSuggestedPONo(),
                PODate = DateTime.Today,
                StatusId = 1,
                Currency = 1,
                ExRate = GetDefaultExchangeRate(),
                PerVAT = 10
            };
            Details = new List<PurchaseOrderDetailInput>();
            DetailsJson = "[]";
        }

        LoadSelectedLookupTexts();
        SetActionFlags();
        DetailsJson = JsonSerializer.Serialize(Details);
        return Page();
    }

    public IActionResult OnPost()
    {
        PagePerm = GetUserPermissions();
        LoadAllDropdowns();
        LoadWorkflowUser();

        var isNew = Header.Id <= 0;
        Mode = isNew ? "add" : (string.IsNullOrWhiteSpace(Mode) ? "edit" : Mode.Trim().ToLowerInvariant());
        if (!isNew)
        {
            LoadExistingWorkflowData(Header.Id);
        }
        LoadSelectedLookupTexts();

        var effectivePermissions = GetEffectivePermissionsByStatus(isNew ? 1 : Header.StatusId);
        var canSaveCurrent = isNew ? effectivePermissions.Contains(PermissionAdd) : effectivePermissions.Contains(PermissionEdit);
        if (!canSaveCurrent)
        {
            ModelState.AddModelError(string.Empty, "You have no permission to save purchase order.");
            SetActionFlags();
            DetailsJson = JsonSerializer.Serialize(Details);
            return Page();
        }

        Details = ParseDetails();
        RecalculateTotals(Details);
        ValidateHeader(Details);

        if (!ModelState.IsValid)
        {
            SetActionFlags();
            DetailsJson = JsonSerializer.Serialize(Details);
            return Page();
        }

        try
        {
            SavePurchaseOrder(Details);
            TempData["SuccessMessage"] = "Purchase order saved successfully.";
            return RedirectToPage("./PurchaseOrderDetail", new { id = Header.Id, mode = "edit", returnUrl = ReturnUrl });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot save purchase order.");
            ModelState.AddModelError(string.Empty, $"Cannot save purchase order. {ex.Message}");
        }

        SetActionFlags();
        DetailsJson = JsonSerializer.Serialize(Details);
        return Page();
    }

    public IActionResult OnPostPurchaserApprove()
    {
        var prepare = PrepareExistingRecordForWorkflow();
        if (prepare != null)
        {
            return prepare;
        }

        if (!CanPurchaserApprove)
        {
            TempData["SuccessMessage"] = "You have no permission to approve this purchase order.";
            return RedirectToCurrentDetail();
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var trans = conn.BeginTransaction();
        try
        {
            using var cmd = new SqlCommand(@"
            UPDATE dbo.PC_PO
            SET PurId = @EmployeeID,
                PurApproDate = @ApproveDate,
                StatusID = 2,
                noted = ''
            WHERE POID = @POID
            AND ISNULL(StatusID, 0) = 1", conn, trans);
            cmd.Parameters.Add("@POID", SqlDbType.Int).Value = Header.Id;
            cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = _workflowUser.EmployeeId;
            cmd.Parameters.Add("@ApproveDate", SqlDbType.VarChar, 12).Value = DateTime.Now.ToString("dd/MM/yyyy");

            if (cmd.ExecuteNonQuery() <= 0)
            {
                throw new InvalidOperationException("Purchaser approval failed because purchase order is not in processing status.");
            }

            trans.Commit();
            TempData["SuccessMessage"] = "Purchase order sent to approval successfully.";
        }
        catch (Exception ex)
        {
            trans.Rollback();
            TempData["SuccessMessage"] = ex.Message;
        }

        return RedirectToCurrentDetail("view");
    }

    public IActionResult OnGetPrLines(int prId)
    {
        try
        {
            return new JsonResult(new { success = true, data = LoadPrDetailRows(prId) });
        }
        catch (Exception ex)
        {
            return new JsonResult(new { success = false, message = ex.Message });
        }
    }

    public IActionResult OnGetSupplierLookup(string? term)
    {
        return new JsonResult(LoadSupplierLookup(term));
    }

    public IActionResult OnGetPrLookup(string? term)
    {
        return new JsonResult(LoadPrLookup(term));
    }

    public IActionResult OnPostEvaluate()
    {
        var prepare = PrepareExistingRecordForWorkflow();
        if (prepare != null)
        {
            return prepare;
        }

        if (!CanEvaluate)
        {
            TempData["SuccessMessage"] = "You have no permission to evaluate this purchase order.";
            return RedirectToCurrentDetail();
        }

        if (EstimatePoint < 1 || EstimatePoint > 4)
        {
            TempData["SuccessMessage"] = "Please select estimate point from 1 to 4.";
            return RedirectToCurrentDetail("edit");
        }

        var estimateDetailId = EstimatePoint switch
        {
            1 => 16,
            2 => 15,
            3 => 14,
            _ => 13
        };

        // PU chi evaluate sau khi CFO va BOD da duyet xong.
        // Status se chuyen sang 3 de danh dau PO da duoc purchaser chot gia.
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var trans = conn.BeginTransaction();
        try
        {
            using (var deleteCmd = new SqlCommand("DELETE FROM dbo.PO_Estimate WHERE POID = @POID AND PO_IndexDetailID IN (13, 14, 15, 16)", conn, trans))
            {
                deleteCmd.Parameters.Add("@POID", SqlDbType.Int).Value = Header.Id;
                deleteCmd.ExecuteNonQuery();
            }

            using (var insertCmd = new SqlCommand(@"
            INSERT INTO dbo.PO_Estimate (POID, PO_IndexDetailID, Point, TheDate, UserCode)
            VALUES (@POID, @POIndexDetailID, @Point, GETDATE(), @UserCode)", conn, trans))
            {
                insertCmd.Parameters.Add("@POID", SqlDbType.Int).Value = Header.Id;
                insertCmd.Parameters.Add("@POIndexDetailID", SqlDbType.Int).Value = estimateDetailId;
                insertCmd.Parameters.Add("@Point", SqlDbType.Int).Value = EstimatePoint;
                insertCmd.Parameters.Add("@UserCode", SqlDbType.NVarChar, 50).Value = _workflowUser.EmployeeCode;
                insertCmd.ExecuteNonQuery();
            }

            using (var updateCmd = new SqlCommand(@"
            UPDATE dbo.PC_PO
            SET PurId = @EmployeeID,
                PurApproDate = @ApproveDate,
                StatusID = 3,
                noted = ''
            WHERE POID = @POID
            AND ISNULL(StatusID, 0) = 2
            AND CAId IS NOT NULL
            AND GDId IS NOT NULL", conn, trans))
            {
                updateCmd.Parameters.Add("@POID", SqlDbType.Int).Value = Header.Id;
                updateCmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = _workflowUser.EmployeeId;
                updateCmd.Parameters.Add("@ApproveDate", SqlDbType.VarChar, 12).Value = DateTime.Now.ToString("dd/MM/yyyy");
                if (updateCmd.ExecuteNonQuery() <= 0)
                {
                    throw new InvalidOperationException("Evaluate failed because purchase order is not ready for purchaser evaluation.");
                }
            }

            trans.Commit();
            TempData["SuccessMessage"] = "Purchase order evaluated successfully.";
        }
        catch (Exception ex)
        {
            trans.Rollback();
            TempData["SuccessMessage"] = ex.Message;
        }

        return RedirectToCurrentDetail("view");
    }

    public IActionResult OnPostApprove()
    {
        var prepare = PrepareExistingRecordForWorkflow();
        if (prepare != null)
        {
            return prepare;
        }

        if (!CanApprove)
        {
            TempData["SuccessMessage"] = "You have no permission to approve this purchase order.";
            return RedirectToCurrentDetail();
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var trans = conn.BeginTransaction();
        try
        {
            if (CanApproveAsCfo())
            {
                using var cmd = new SqlCommand(@"
                UPDATE dbo.PC_PO
                SET CAId = @EmployeeID,
                    CAApproDate = @ApproveDate
                WHERE POID = @POID
                AND ISNULL(StatusID, 0) = 2
                AND CAId IS NULL", conn, trans);
                cmd.Parameters.Add("@POID", SqlDbType.Int).Value = Header.Id;
                cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = _workflowUser.EmployeeId;
                cmd.Parameters.Add("@ApproveDate", SqlDbType.VarChar, 12).Value = DateTime.Now.ToString("dd/MM/yyyy");
                if (cmd.ExecuteNonQuery() <= 0)
                {
                    throw new InvalidOperationException("Approve failed because CFO step was already processed.");
                }
            }
            else if (CanApproveAsBod())
            {
                using var cmd = new SqlCommand(@"
                UPDATE dbo.PC_PO
                SET GDId = @EmployeeID,
                    GDApproDate = @ApproveDate
                WHERE POID = @POID
                AND ISNULL(StatusID, 0) = 2
                AND CAId IS NOT NULL
                AND GDId IS NULL", conn, trans);
                cmd.Parameters.Add("@POID", SqlDbType.Int).Value = Header.Id;
                cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = _workflowUser.EmployeeId;
                cmd.Parameters.Add("@ApproveDate", SqlDbType.VarChar, 12).Value = DateTime.Now.ToString("dd/MM/yyyy");
                if (cmd.ExecuteNonQuery() <= 0)
                {
                    throw new InvalidOperationException("Approve failed because BOD step was already processed.");
                }
            }
            else
            {
                throw new InvalidOperationException("Current user cannot approve this purchase order.");
            }

            trans.Commit();
            TempData["SuccessMessage"] = "Purchase order approved successfully.";
        }
        catch (Exception ex)
        {
            trans.Rollback();
            TempData["SuccessMessage"] = ex.Message;
        }

        return RedirectToCurrentDetail("view");
    }

    public IActionResult OnPostBackToProcessing()
    {
        var prepare = PrepareExistingRecordForWorkflow();
        if (prepare != null)
        {
            return prepare;
        }

        if (!CanBackToProcessing)
        {
            TempData["SuccessMessage"] = "You have no permission to back this purchase order to processing.";
            return RedirectToCurrentDetail("edit");
        }

        if (string.IsNullOrWhiteSpace(ConvertReason))
        {
            TempData["SuccessMessage"] = "Please enter reason to convert PO.";
            return RedirectToPage("./PurchaseOrderDetail", new { id = Header.Id, mode = "edit", returnUrl = ReturnUrl, openConvertModal = true });
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var trans = conn.BeginTransaction();
        try
        {
            using (var reasonCmd = new SqlCommand(@"
            INSERT INTO dbo.PC_PO_ReasonToConvert (poID, Reason, operatorID, theDate)
            VALUES (@POID, @Reason, @OperatorID, GETDATE())", conn, trans))
            {
                reasonCmd.Parameters.Add("@POID", SqlDbType.Int).Value = Header.Id;
                reasonCmd.Parameters.Add("@Reason", SqlDbType.NVarChar, 1000).Value = ConvertReason.Trim();
                reasonCmd.Parameters.Add("@OperatorID", SqlDbType.Int).Value = _workflowUser.EmployeeId;
                reasonCmd.ExecuteNonQuery();
            }

            using (var updateCmd = new SqlCommand(@"
            UPDATE dbo.PC_PO
            SET PurId = NULL,
                PurApproDate = NULL,
                CAId = NULL,
                CAApproDate = NULL,
                GDId = NULL,
                GDApproDate = NULL,
                StatusID = 1,
                edited = 1,
                KeepStatus = @KeepStatus
            WHERE POID = @POID", conn, trans))
            {
                updateCmd.Parameters.Add("@POID", SqlDbType.Int).Value = Header.Id;
                updateCmd.Parameters.Add("@KeepStatus", SqlDbType.Int).Value = Header.StatusId;
                updateCmd.ExecuteNonQuery();
            }

            trans.Commit();
            TempData["SuccessMessage"] = "Purchase order returned to processing successfully.";
        }
        catch (Exception ex)
        {
            trans.Rollback();
            TempData["SuccessMessage"] = ex.Message;
        }

        return RedirectToCurrentDetail("edit");
    }

    private void LoadAllDropdowns()
    {
        StatusOptions = LoadListFromSql("SELECT POStatusID, POStatusName FROM dbo.PC_POStatus ORDER BY POStatusID", "POStatusID", "POStatusName");
        CurrencyOptions = LoadListFromSql("SELECT CurrencyID, CurrencyName FROM dbo.MS_CurrencyFL ORDER BY CurrencyID", "CurrencyID", "CurrencyName");
        AssessLevelOptions = LoadListFromSql("SELECT AssessLevelID, AssessLevelName FROM dbo.PC_AssessLevel ORDER BY AssessLevelID", "AssessLevelID", "AssessLevelName");
        DepartmentOptions = LoadListFromSql("SELECT DeptID, DeptCode + ' (' + DeptName + ')' AS DeptText FROM dbo.MS_Department ORDER BY DeptCode", "DeptID", "DeptText");
    }

    private void LoadSelectedLookupTexts()
    {
        CurrentSupplierText = GetSupplierDisplayText(Header.SupplierID);
        CurrentPrText = GetPrDisplayText(Header.PRID);
    }

    private string GetSupplierDisplayText(int? supplierId)
    {
        if (!supplierId.HasValue || supplierId.Value <= 0)
        {
            return string.Empty;
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
        SELECT TOP 1 ISNULL(SupplierCode, '')
        FROM dbo.PC_Suppliers
        WHERE SupplierID = @SupplierID", conn);
        cmd.Parameters.Add("@SupplierID", SqlDbType.Int).Value = supplierId.Value;
        conn.Open();
        return Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
    }

    private string GetPrDisplayText(int? prId)
    {
        if (!prId.HasValue || prId.Value <= 0)
        {
            return string.Empty;
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
        SELECT TOP 1
            CAST(RequestNo AS nvarchar(50))
        FROM dbo.PC_PR
        WHERE PRID = @PRID", conn);
        cmd.Parameters.Add("@PRID", SqlDbType.Int).Value = prId.Value;
        conn.Open();
        return Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
    }

    private List<object> LoadSupplierLookup(string? term)
    {
        var rows = new List<object>();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
        SELECT TOP 50
            SupplierID,
            ISNULL(SupplierCode, '') AS SupplierCode
        FROM dbo.PC_Suppliers
        WHERE Status >= 0
          AND Status < 5
          AND (
                @term IS NULL
                OR SupplierCode LIKE '%' + @term + '%'
                OR SupplierName LIKE '%' + @term + '%'
          )
        ORDER BY SupplierCode", conn);
        cmd.Parameters.Add("@term", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(term) ? DBNull.Value : term.Trim();
        conn.Open();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new
            {
                id = Convert.ToString(reader["SupplierID"]) ?? string.Empty,
                text = Convert.ToString(reader["SupplierCode"]) ?? string.Empty
            });
        }

        return rows.Cast<object>().ToList();
    }

    private List<object> LoadPrLookup(string? term)
    {
        var rows = new List<object>();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
        SELECT TOP 50
            PRID,
            ISNULL(RequestNo, '') AS RequestNo
        FROM dbo.PC_PR
        WHERE ISNULL(Status, 0) <> 3
          AND (
                @term IS NULL
                OR RequestNo LIKE '%' + @term + '%'
          )
        ORDER BY RequestDate DESC, RequestNo DESC", conn);
        cmd.Parameters.Add("@term", SqlDbType.NVarChar, 100).Value = string.IsNullOrWhiteSpace(term) ? DBNull.Value : term.Trim();
        conn.Open();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new
            {
                id = Convert.ToString(reader["PRID"]) ?? string.Empty,
                text = Convert.ToString(reader["RequestNo"]) ?? string.Empty
            });
        }

        return rows.Cast<object>().ToList();
    }

    private void LoadPurchaseOrder(int id)
    {
        Details = new List<PurchaseOrderDetailInput>();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        using (var cmd = new SqlCommand(@"
        SELECT TOP 1
            POID,
            ISNULL(PONo, '') AS PONo,
            PODate,
            PRID,
            SupplierID,
            ISNULL(POTerms, '') AS POTerms,
            ISNULL(StatusID, 1) AS StatusID,
            AssessLevel,
            Currency,
            ISNULL(ExRate, 0) AS ExRate,
            ISNULL(Comment, '') AS Comment,
            ISNULL(BeforeVAT, 0) AS BeforeVAT,
            ISNULL(PerVAT, 0) AS PerVAT,
            ISNULL(VAT, 0) AS VAT,
            ISNULL(AfterVAT, 0) AS AfterVAT,
            ISNULL(Remark, '') AS Remark,
            PurId,
            CAId,
            GDId,
            ISNULL(KeepStatus, 0) AS KeepStatus
        FROM dbo.PC_PO
        WHERE POID = @POID", conn))
                {
                    cmd.Parameters.Add("@POID", SqlDbType.Int).Value = id;
                    using var rd = cmd.ExecuteReader();
                    if (!rd.Read())
                    {
                        return;
                    }

                    Header = new PurchaseOrderHeader
                    {
                        Id = Convert.ToInt32(rd["POID"]),
                        PONo = Convert.ToString(rd["PONo"]) ?? string.Empty,
                        PODate = rd.IsDBNull(rd.GetOrdinal("PODate")) ? DateTime.Today : Convert.ToDateTime(rd["PODate"]),
                        PRID = rd.IsDBNull(rd.GetOrdinal("PRID")) ? null : Convert.ToInt32(rd["PRID"]),
                        SupplierID = rd.IsDBNull(rd.GetOrdinal("SupplierID")) ? null : Convert.ToInt32(rd["SupplierID"]),
                        POTerms = Convert.ToString(rd["POTerms"]) ?? string.Empty,
                        StatusId = Convert.ToInt32(rd["StatusID"]),
                        AssessLevel = rd.IsDBNull(rd.GetOrdinal("AssessLevel")) ? null : Convert.ToInt32(rd["AssessLevel"]),
                        Currency = rd.IsDBNull(rd.GetOrdinal("Currency")) ? 1 : Convert.ToInt32(rd["Currency"]),
                        ExRate = rd.IsDBNull(rd.GetOrdinal("ExRate")) ? 0 : Convert.ToDecimal(rd["ExRate"]),
                        Comment = Convert.ToString(rd["Comment"]) ?? string.Empty,
                        BeforeVAT = rd.IsDBNull(rd.GetOrdinal("BeforeVAT")) ? 0 : Convert.ToDecimal(rd["BeforeVAT"]),
                        PerVAT = rd.IsDBNull(rd.GetOrdinal("PerVAT")) ? 0 : Convert.ToDecimal(rd["PerVAT"]),
                        VAT = rd.IsDBNull(rd.GetOrdinal("VAT")) ? 0 : Convert.ToDecimal(rd["VAT"]),
                        AfterVAT = rd.IsDBNull(rd.GetOrdinal("AfterVAT")) ? 0 : Convert.ToDecimal(rd["AfterVAT"]),
                        Remark = Convert.ToString(rd["Remark"]) ?? string.Empty,
                        PurId = rd.IsDBNull(rd.GetOrdinal("PurId")) ? null : Convert.ToInt32(rd["PurId"]),
                        CAId = rd.IsDBNull(rd.GetOrdinal("CAId")) ? null : Convert.ToInt32(rd["CAId"]),
                        GDId = rd.IsDBNull(rd.GetOrdinal("GDId")) ? null : Convert.ToInt32(rd["GDId"]),
                        KeepStatus = rd.IsDBNull(rd.GetOrdinal("KeepStatus")) ? 0 : Convert.ToInt32(rd["KeepStatus"])
                    };
                }

                using var detailCmd = new SqlCommand(@"
        SELECT
            d.ItemID,
            ISNULL(i.ItemCode, '') AS ItemCode,
            ISNULL(i.ItemName, '') AS ItemName,
            ISNULL(i.Unit, '') AS Unit,
            ISNULL(d.Quantity, 0) AS Quantity,
            ISNULL(d.UnitPrice, 0) AS UnitPrice,
            ISNULL(d.POAmount, 0) AS POAmount,
            d.RecDept,
            ISNULL(dep.DeptName, '') AS RecDeptName,
            ISNULL(d.Note, '') AS Note,
            ISNULL(d.RecQty, 0) AS RecQty,
            ISNULL(d.RecAmount, 0) AS RecAmount,
            d.RecDate,
            ISNULL(d.MRRequestNO, '') AS MRRequestNo
        FROM dbo.PC_PODetail d
        LEFT JOIN dbo.INV_ItemList i ON i.ItemID = d.ItemID
        LEFT JOIN dbo.MS_Department dep ON dep.DeptID = d.RecDept
        WHERE d.POID = @POID
        ORDER BY ISNULL(i.ItemCode, '')", conn);
                detailCmd.Parameters.Add("@POID", SqlDbType.Int).Value = id;

        using var detailReader = detailCmd.ExecuteReader();
        while (detailReader.Read())
        {
            Details.Add(new PurchaseOrderDetailInput
            {
                TempKey = Guid.NewGuid().ToString("N"),
                ItemID = Convert.ToInt32(detailReader["ItemID"]),
                ItemCode = Convert.ToString(detailReader["ItemCode"]) ?? string.Empty,
                ItemName = Convert.ToString(detailReader["ItemName"]) ?? string.Empty,
                Unit = Convert.ToString(detailReader["Unit"]) ?? string.Empty,
                Quantity = detailReader.IsDBNull(detailReader.GetOrdinal("Quantity")) ? 0 : Convert.ToDecimal(detailReader["Quantity"]),
                UnitPrice = detailReader.IsDBNull(detailReader.GetOrdinal("UnitPrice")) ? 0 : Convert.ToDecimal(detailReader["UnitPrice"]),
                POAmount = detailReader.IsDBNull(detailReader.GetOrdinal("POAmount")) ? 0 : Convert.ToDecimal(detailReader["POAmount"]),
                RecDept = detailReader.IsDBNull(detailReader.GetOrdinal("RecDept")) ? null : Convert.ToInt32(detailReader["RecDept"]),
                RecDeptName = Convert.ToString(detailReader["RecDeptName"]) ?? string.Empty,
                Note = Convert.ToString(detailReader["Note"]) ?? string.Empty,
                RecQty = detailReader.IsDBNull(detailReader.GetOrdinal("RecQty")) ? 0 : Convert.ToDecimal(detailReader["RecQty"]),
                RecAmount = detailReader.IsDBNull(detailReader.GetOrdinal("RecAmount")) ? 0 : Convert.ToDecimal(detailReader["RecAmount"]),
                RecDate = detailReader.IsDBNull(detailReader.GetOrdinal("RecDate")) ? null : Convert.ToDateTime(detailReader["RecDate"]),
                MRRequestNo = Convert.ToString(detailReader["MRRequestNo"]) ?? string.Empty
            });
        }
    }

    private void LoadExistingWorkflowData(int id)
    {
        var currentInput = Header;
        var parsedDetails = ParseDetails();
        LoadPurchaseOrder(id);
        Header.PONo = currentInput.PONo;
        Header.PODate = currentInput.PODate;
        Header.PRID = currentInput.PRID;
        Header.SupplierID = currentInput.SupplierID;
        Header.POTerms = currentInput.POTerms;
        Header.AssessLevel = currentInput.AssessLevel;
        Header.Currency = currentInput.Currency;
        Header.ExRate = currentInput.ExRate;
        Header.Comment = currentInput.Comment;
        Header.Remark = currentInput.Remark;
        Header.PerVAT = currentInput.PerVAT;
        Header.BeforeVAT = currentInput.BeforeVAT;
        Header.VAT = currentInput.VAT;
        Header.AfterVAT = currentInput.AfterVAT;
        if (parsedDetails.Count > 0)
        {
            Details = parsedDetails;
        }
    }

    private List<PurchaseOrderPrLineLookup> LoadPrDetailRows(int prId)
    {
        var rows = new List<PurchaseOrderPrLineLookup>();
        if (prId <= 0)
        {
            return rows;
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
        SELECT
            d.RecordID,
            d.ItemID,
            ISNULL(i.ItemCode, '') AS ItemCode,
            ISNULL(i.ItemName, '') AS ItemName,
            ISNULL(i.Unit, '') AS Unit,
            ISNULL(d.Quantity, 0) AS Quantity,
            ISNULL(d.UnitPrice, 0) AS UnitPrice,
            ISNULL(d.Remark, '') AS Remark,
            d.SupplierID,
            CASE WHEN s.SupplierID IS NULL THEN '' ELSE ISNULL(s.SupplierCode, '') + ' / ' + ISNULL(s.SupplierName, '') END AS SupplierText
        FROM dbo.PC_PRDetail d
        LEFT JOIN dbo.INV_ItemList i ON i.ItemID = d.ItemID
        LEFT JOIN dbo.PC_Suppliers s ON s.SupplierID = d.SupplierID
        WHERE d.PRID = @PRID
        ORDER BY d.RecordID", conn);
        cmd.Parameters.Add("@PRID", SqlDbType.Int).Value = prId;
        conn.Open();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new PurchaseOrderPrLineLookup
            {
                PrDetailId = Convert.ToInt64(reader["RecordID"]),
                ItemID = Convert.ToInt32(reader["ItemID"]),
                ItemCode = Convert.ToString(reader["ItemCode"]) ?? string.Empty,
                ItemName = Convert.ToString(reader["ItemName"]) ?? string.Empty,
                Unit = Convert.ToString(reader["Unit"]) ?? string.Empty,
                Quantity = reader.IsDBNull(reader.GetOrdinal("Quantity")) ? 0 : Convert.ToDecimal(reader["Quantity"]),
                UnitPrice = reader.IsDBNull(reader.GetOrdinal("UnitPrice")) ? 0 : Convert.ToDecimal(reader["UnitPrice"]),
                Remark = Convert.ToString(reader["Remark"]) ?? string.Empty,
                SupplierID = reader.IsDBNull(reader.GetOrdinal("SupplierID")) ? null : Convert.ToInt32(reader["SupplierID"]),
                SupplierText = Convert.ToString(reader["SupplierText"]) ?? string.Empty
            });
        }

        return rows;
    }

    private void SavePurchaseOrder(List<PurchaseOrderDetailInput> details)
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var trans = conn.BeginTransaction();
        try
        {
            if (Header.Id <= 0)
            {
                using var insertCmd = new SqlCommand(@"
            INSERT INTO dbo.PC_PO
            (PRID, PONo, PODate, Remark, SupplierID, POTerms, StatusID, AssessLevel, Comment, Currency, ExRate, BeforeVAT, PerVAT, VAT, AfterVAT)
            VALUES
            (@PRID, @PONo, @PODate, @Remark, @SupplierID, @POTerms, @StatusID, @AssessLevel, @Comment, @Currency, @ExRate, @BeforeVAT, @PerVAT, @VAT, @AfterVAT);
            SELECT CAST(SCOPE_IDENTITY() AS int);", conn, trans);
                            FillHeaderCommand(insertCmd);
                            Header.Id = Convert.ToInt32(insertCmd.ExecuteScalar() ?? 0);
                        }
                        else
                        {
                            using var updateCmd = new SqlCommand(@"
            UPDATE dbo.PC_PO
            SET PRID = @PRID,
                PONo = @PONo,
                PODate = @PODate,
                Remark = @Remark,
                SupplierID = @SupplierID,
                POTerms = @POTerms,
                AssessLevel = @AssessLevel,
                Comment = @Comment,
                Currency = @Currency,
                ExRate = @ExRate,
                BeforeVAT = @BeforeVAT,
                PerVAT = @PerVAT,
                VAT = @VAT,
                AfterVAT = @AfterVAT
            WHERE POID = @POID", conn, trans);
                FillHeaderCommand(updateCmd);
                updateCmd.Parameters.Add("@POID", SqlDbType.Int).Value = Header.Id;
                updateCmd.ExecuteNonQuery();
            }

            using (var deleteCmd = new SqlCommand("DELETE FROM dbo.PC_PODetail WHERE POID = @POID", conn, trans))
            {
                deleteCmd.Parameters.Add("@POID", SqlDbType.Int).Value = Header.Id;
                deleteCmd.ExecuteNonQuery();
            }

            foreach (var detail in details)
            {
                using var insertDetailCmd = new SqlCommand(@"
                INSERT INTO dbo.PC_PODetail
                (POID, ItemID, Quantity, UnitPrice, POAmount, RecDept, Note, RecQty, RecAmount, RecDate, MRRequestNO)
                VALUES
                (@POID, @ItemID, @Quantity, @UnitPrice, @POAmount, @RecDept, @Note, @RecQty, @RecAmount, @RecDate, @MRRequestNO)", conn, trans);
                insertDetailCmd.Parameters.Add("@POID", SqlDbType.Int).Value = Header.Id;
                insertDetailCmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = detail.ItemID;
                insertDetailCmd.Parameters.Add("@Quantity", SqlDbType.Decimal).Value = detail.Quantity;
                insertDetailCmd.Parameters.Add("@UnitPrice", SqlDbType.Decimal).Value = detail.UnitPrice;
                insertDetailCmd.Parameters.Add("@POAmount", SqlDbType.Decimal).Value = detail.POAmount;
                insertDetailCmd.Parameters.Add("@RecDept", SqlDbType.Int).Value = detail.RecDept.HasValue ? detail.RecDept.Value : DBNull.Value;
                insertDetailCmd.Parameters.Add("@Note", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(detail.Note) ? DBNull.Value : detail.Note;
                insertDetailCmd.Parameters.Add("@RecQty", SqlDbType.Decimal).Value = detail.RecQty;
                insertDetailCmd.Parameters.Add("@RecAmount", SqlDbType.Decimal).Value = detail.RecAmount;
                insertDetailCmd.Parameters.Add("@RecDate", SqlDbType.Date).Value = detail.RecDate.HasValue ? detail.RecDate.Value.Date : DBNull.Value;
                insertDetailCmd.Parameters.Add("@MRRequestNO", SqlDbType.NVarChar, 50).Value = string.IsNullOrWhiteSpace(detail.MRRequestNo) ? DBNull.Value : detail.MRRequestNo;
                insertDetailCmd.ExecuteNonQuery();
            }

            trans.Commit();
        }
        catch
        {
            trans.Rollback();
            throw;
        }
    }

    private void FillHeaderCommand(SqlCommand cmd)
    {
        cmd.Parameters.Add("@PRID", SqlDbType.Int).Value = Header.PRID.HasValue ? Header.PRID.Value : DBNull.Value;
        cmd.Parameters.Add("@PONo", SqlDbType.NVarChar, 50).Value = Header.PONo.Trim();
        cmd.Parameters.Add("@PODate", SqlDbType.Date).Value = Header.PODate.Date;
        cmd.Parameters.Add("@Remark", SqlDbType.NVarChar, 1000).Value = string.IsNullOrWhiteSpace(Header.Remark) ? DBNull.Value : Header.Remark;
        cmd.Parameters.Add("@SupplierID", SqlDbType.Int).Value = Header.SupplierID.HasValue ? Header.SupplierID.Value : DBNull.Value;
        cmd.Parameters.Add("@POTerms", SqlDbType.NVarChar, 2000).Value = string.IsNullOrWhiteSpace(Header.POTerms) ? DBNull.Value : Header.POTerms;
        cmd.Parameters.Add("@StatusID", SqlDbType.Int).Value = Header.StatusId <= 0 ? 1 : Header.StatusId;
        cmd.Parameters.Add("@AssessLevel", SqlDbType.Int).Value = Header.AssessLevel.HasValue ? Header.AssessLevel.Value : DBNull.Value;
        cmd.Parameters.Add("@Comment", SqlDbType.NVarChar, 2000).Value = string.IsNullOrWhiteSpace(Header.Comment) ? DBNull.Value : Header.Comment;
        cmd.Parameters.Add("@Currency", SqlDbType.Int).Value = Header.Currency <= 0 ? 1 : Header.Currency;
        cmd.Parameters.Add("@ExRate", SqlDbType.Decimal).Value = Header.ExRate;
        cmd.Parameters.Add("@BeforeVAT", SqlDbType.Decimal).Value = Header.BeforeVAT;
        cmd.Parameters.Add("@PerVAT", SqlDbType.Decimal).Value = Header.PerVAT;
        cmd.Parameters.Add("@VAT", SqlDbType.Decimal).Value = Header.VAT;
        cmd.Parameters.Add("@AfterVAT", SqlDbType.Decimal).Value = Header.AfterVAT;
    }

    private void ValidateHeader(List<PurchaseOrderDetailInput> details)
    {
        if (string.IsNullOrWhiteSpace(Header.PONo))
        {
            ModelState.AddModelError("Header.PONo", "PO No. is required.");
        }

        if (Header.PODate == DateTime.MinValue)
        {
            ModelState.AddModelError("Header.PODate", "Order Date is required.");
        }

        if (!Header.SupplierID.HasValue || Header.SupplierID.Value <= 0)
        {
            ModelState.AddModelError("Header.SupplierID", "Supplier is required.");
        }

        if (!string.IsNullOrWhiteSpace(Header.Comment) && Header.Comment.Length > 100)
        {
            ModelState.AddModelError("Header.Comment", "Comment must not exceed 100 characters.");
        }

        if (!string.IsNullOrWhiteSpace(Header.Remark) && Header.Remark.Length > 100)
        {
            ModelState.AddModelError("Header.Remark", "Remark must not exceed 100 characters.");
        }

        if (details.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Please add at least one detail row.");
        }

        for (var index = 0; index < details.Count; index++)
        {
            var detail = details[index];
            if (detail.ItemID <= 0)
            {
                ModelState.AddModelError(string.Empty, $"Row {index + 1}: Item is required.");
            }

            if (detail.Quantity <= 0)
            {
                ModelState.AddModelError(string.Empty, $"Row {index + 1}: Quantity must be greater than 0.");
            }

            if (detail.UnitPrice < 0)
            {
                ModelState.AddModelError(string.Empty, $"Row {index + 1}: Unit price is invalid.");
            }
        }
    }

    private void RecalculateTotals(List<PurchaseOrderDetailInput> details)
    {
        Header.BeforeVAT = details.Sum(x => x.POAmount);
        Header.VAT = Math.Round(Header.BeforeVAT * (Header.PerVAT / 100m), 2);
        Header.AfterVAT = Header.BeforeVAT + Header.VAT;
    }

    private List<PurchaseOrderDetailInput> ParseDetails()
    {
        if (string.IsNullOrWhiteSpace(DetailsJson))
        {
            return new List<PurchaseOrderDetailInput>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<PurchaseOrderDetailInput>>(DetailsJson, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<PurchaseOrderDetailInput>();
        }
        catch
        {
            ModelState.AddModelError(string.Empty, "Detail data is invalid.");
            return new List<PurchaseOrderDetailInput>();
        }
    }

    private string GetSuggestedPONo()
    {
        try
        {
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand("exec HaiAutoNumPO null", conn);
            conn.Open();
            return Convert.ToString(cmd.ExecuteScalar()) ?? $"PO-{DateTime.Now:yyyyMMddHHmmss}";
        }
        catch
        {
            return $"PO-{DateTime.Now:yyyyMMddHHmmss}";
        }
    }

    private decimal GetDefaultExchangeRate()
    {
        try
        {
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand("SELECT TOP 1 ISNULL(USDRate, 0) FROM dbo.SV_Parameters", conn);
            conn.Open();
            return Convert.ToDecimal(cmd.ExecuteScalar() ?? 0m);
        }
        catch
        {
            return 0m;
        }
    }

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

    private List<int> GetEffectivePermissionsByStatus(int status)
    {
        if (IsAdminRole())
        {
            return Enumerable.Range(1, 20).ToList();
        }

        return _securityService.GetEffectivePermissions(FUNCTION_ID, GetCurrentRoleId(), status);
    }

    private void LoadWorkflowUser()
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
        SELECT TOP 1
            EmployeeID,
            ISNULL(EmployeeCode, '') AS EmployeeCode,
            ISNULL(IsPurchaser, 0) AS IsPurchaser,
            ISNULL(IsCFO, 0) AS IsCFO,
            ISNULL(IsBOD, 0) AS IsBOD
        FROM dbo.MS_Employee
        WHERE EmployeeID = @EmployeeID", conn);

        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = GetCurrentEmployeeId();
        conn.Open();
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            _workflowUser = new PurchaseOrderWorkflowUser();
            return;
        }

        _workflowUser = new PurchaseOrderWorkflowUser
        {
            EmployeeId = Convert.ToInt32(reader["EmployeeID"]),
            EmployeeCode = Convert.ToString(reader["EmployeeCode"]) ?? string.Empty,
            IsPurchaser = Convert.ToBoolean(reader["IsPurchaser"]),
            IsCFO = Convert.ToBoolean(reader["IsCFO"]),
            IsBOD = Convert.ToBoolean(reader["IsBOD"])
        };
    }

    private void SetActionFlags()
    {
        var effectivePermissions = GetEffectivePermissionsByStatus(Header.StatusId <= 0 ? 1 : Header.StatusId);
        CanSave = !IsViewMode && (Mode == "add"
            ? effectivePermissions.Contains(PermissionAdd)
            : effectivePermissions.Contains(PermissionEdit));
        CanPurchaserApprove = Header.Id > 0
            && Header.StatusId == 1
            && (IsAdminRole() || _workflowUser.IsPurchaser);
        // Evaluate chi hien sau khi CFO va BOD da ghi nhan xong.
        CanEvaluate = Header.Id > 0
            && Header.StatusId == 2
            && Header.CAId.HasValue
            && Header.GDId.HasValue
            && effectivePermissions.Contains(PermissionEvaluate)
            && (_workflowUser.IsPurchaser || IsAdminRole());
        CanApprove = Header.Id > 0 && Header.StatusId == 2 && (CanApproveAsCfo() || CanApproveAsBod());
        CanBackToProcessing = Header.Id > 0
            && Header.StatusId == 2
            && effectivePermissions.Contains(PermissionBackToProcessing)
            && (_workflowUser.IsPurchaser || _workflowUser.IsCFO || _workflowUser.IsBOD || IsAdminRole());
    }

    private bool CanApproveAsCfo()
    {
        return Header.Id > 0 && Header.StatusId == 2 && !Header.CAId.HasValue && (_workflowUser.IsCFO || IsAdminRole());
    }

    private bool CanApproveAsBod()
    {
        return Header.Id > 0 && Header.StatusId == 2 && Header.CAId.HasValue && !Header.GDId.HasValue && (_workflowUser.IsBOD || IsAdminRole());
    }

    private IActionResult? PrepareExistingRecordForWorkflow()
    {
        PagePerm = GetUserPermissions();
        LoadAllDropdowns();
        LoadWorkflowUser();
        if (Header.Id <= 0)
        {
            TempData["SuccessMessage"] = "Purchase order is not found.";
            return RedirectToPage("./Index");
        }

        try
        {
            LoadPurchaseOrder(Header.Id);
            SetActionFlags();
        }
        catch (Exception ex)
        {
            TempData["SuccessMessage"] = ex.Message;
            return RedirectToPage("./Index");
        }

        return null;
    }

    private IActionResult RedirectToCurrentDetail(string mode = "view")
    {
        return RedirectToPage("./PurchaseOrderDetail", new { id = Header.Id, mode, returnUrl = ReturnUrl });
    }

    private int GetCurrentRoleId()
    {
        return int.Parse(User.FindFirst("RoleID")?.Value ?? "0");
    }

    private int GetCurrentEmployeeId()
    {
        return int.Parse(User.FindFirst("EmployeeID")?.Value ?? "0");
    }

    private bool IsAdminRole()
    {
        return User.FindFirst("IsAdminRole")?.Value == "True";
    }
}

public class PurchaseOrderHeader
{
    public int Id { get; set; }

    [Required]
    [StringLength(50)]
    public string PONo { get; set; } = string.Empty;

    [DataType(DataType.Date)]
    public DateTime PODate { get; set; } = DateTime.Today;

    public int? PRID { get; set; }
    public int? SupplierID { get; set; }
    public string? POTerms { get; set; }
    public int StatusId { get; set; } = 1;
    public int? AssessLevel { get; set; }
    public int Currency { get; set; } = 1;
    public decimal ExRate { get; set; }
    [StringLength(100)]
    public string? Comment { get; set; }
    public decimal BeforeVAT { get; set; }
    public decimal PerVAT { get; set; }
    public decimal VAT { get; set; }
    public decimal AfterVAT { get; set; }
    [StringLength(100)]
    public string? Remark { get; set; }
    public int? PurId { get; set; }
    public int? CAId { get; set; }
    public int? GDId { get; set; }
    public int KeepStatus { get; set; }
}

public class PurchaseOrderDetailInput
{
    public string TempKey { get; set; } = Guid.NewGuid().ToString("N");
    public int ItemID { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal POAmount { get; set; }
    public int? RecDept { get; set; }
    public string RecDeptName { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public decimal RecQty { get; set; }
    public decimal RecAmount { get; set; }
    public DateTime? RecDate { get; set; }
    public string MRRequestNo { get; set; } = string.Empty;
}

public class PurchaseOrderPrLineLookup
{
    public long PrDetailId { get; set; }
    public int ItemID { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public string Remark { get; set; } = string.Empty;
    public int? SupplierID { get; set; }
    public string SupplierText { get; set; } = string.Empty;
}
