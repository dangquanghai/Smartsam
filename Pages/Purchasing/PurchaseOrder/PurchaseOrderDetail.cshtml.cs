using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Routing;
using Microsoft.Data.SqlClient;
using SmartSam.Helpers;
using SmartSam.Pages;
using SmartSam.Services;
using SmartSam.Services.Interfaces;

namespace SmartSam.Pages.Purchasing.PurchaseOrder;

public class PurchaseOrderDetailModel : BasePageModel
{
    private const string NotifyCcEmail = "hai.dq@saigonskygarden.com.vn";
    private const int FUNCTION_ID = 73;
    private const int PermissionView = 2;
    private const int PermissionAdd = 3;
    private const int PermissionEdit = 4;
    private const int PermissionBackToProcessing = 6;
    private readonly PermissionService _permissionService;
    private readonly ISecurityService _securityService;
    private readonly ILogger<PurchaseOrderDetailModel> _logger;
    private readonly IWebHostEnvironment _webHostEnvironment;
    private PurchaseOrderWorkflowUser _workflowUser = new PurchaseOrderWorkflowUser();

    public PurchaseOrderDetailModel(IConfiguration config, PermissionService permissionService, ISecurityService securityService, ILogger<PurchaseOrderDetailModel> logger, IWebHostEnvironment webHostEnvironment)
        : base(config)
    {
        _permissionService = permissionService;
        _securityService = securityService;
        _logger = logger;
        _webHostEnvironment = webHostEnvironment;
    }

    public PagePermissions PagePerm { get; private set; } = new PagePermissions();
    public bool CanSave { get; private set; }
    public bool CanPurchaserApprove { get; private set; }
    public bool CanEvaluate { get; private set; }
    public bool CanEditEvaluate { get; private set; }
    public bool CanApprove { get; private set; }
    public bool CanBackToProcessing { get; private set; }
    public bool CanManageAttachments { get; private set; }
    public bool OpenConvertModal { get; private set; }
    public bool IsViewMode => string.Equals(Mode, "view", StringComparison.OrdinalIgnoreCase);
    public string BackToListUrl => string.IsNullOrWhiteSpace(ReturnUrl) ? Url.Page("./Index") ?? "./Index" : ReturnUrl;
    public string DepartmentOptionsJson => JsonSerializer.Serialize(DepartmentOptions.Select(x => new LookupOptionDto
    {
        Value = x.Value,
        Text = x.Text
    }));
    public string CurrentSupplierText { get; private set; } = string.Empty;
    public string CurrentPrText { get; private set; } = string.Empty;
    public string AllowedAttachmentExtensionsText => _config.GetValue<string>("FileUploads:AllowedExtensions") ?? ".doc,.docx,.xls,.xlsx,.pdf,.jpg,.jpeg,.png";
    public int MaxAttachmentSizeMb => _config.GetValue<int?>("FileUploads:MaxFileSizeMb") ?? 10;

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

    public int CurrentEstimatePoint { get; private set; }
    public List<PurchaseOrderEvaluateOptionViewModel> EvaluateOptions { get; private set; } = new List<PurchaseOrderEvaluateOptionViewModel>();

    [BindProperty]
    public string? ConvertReason { get; set; }

    public List<PurchaseOrderDetailInput> Details { get; set; } = new List<PurchaseOrderDetailInput>();
    public List<SelectListItem> StatusOptions { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> CurrencyOptions { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> AssessLevelOptions { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> DepartmentOptions { get; set; } = new List<SelectListItem>();
    public List<SelectListItem> PrOptions { get; set; } = new List<SelectListItem>();
    public List<PurchaseOrderAttachmentViewModel> AttachmentList { get; set; } = new List<PurchaseOrderAttachmentViewModel>();

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

            if (Header.StatusId != 1)
            {
                Mode = "view";
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
                PerVAT = 10,
                POTerms = BuildDefaultPurchaseOrderTerms(DateTime.Today)
            };
            Details = new List<PurchaseOrderDetailInput>();
            DetailsJson = "[]";
            AttachmentList = new List<PurchaseOrderAttachmentViewModel>();
        }

        PrOptions = LoadPrOptions(Header.PRID);
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
        PrOptions = LoadPrOptions(Header.PRID);
        LoadSelectedLookupTexts();

        var effectivePermissions = GetEffectivePermissionsByStatus(isNew ? 1 : Header.StatusId);
        var canSaveCurrent = isNew
            ? effectivePermissions.Contains(PermissionAdd)
            : Header.StatusId == 1 && effectivePermissions.Contains(PermissionEdit);
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
            return RedirectToPage("./PurchaseOrderDetail", BuildDetailRouteValues("edit"));
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

        string? notifySubject = null;
        string? notifyBody = null;
        List<PurchaseOrderNotifyRecipientViewModel> recipients = new List<PurchaseOrderNotifyRecipientViewModel>();

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

            recipients = GetWorkflowRecipientsByFlag(conn, trans, "IsCFO");
            notifySubject = "[Purchase Order] Waiting for CFO approve";
            notifyBody = BuildWorkflowNotifyBody(conn, trans, "PURCHASE ORDER", "#007bff", "is waiting for your approval.", true);

            trans.Commit();
        }
        catch (Exception ex)
        {
            trans.Rollback();
            TempData["SuccessMessage"] = ex.Message;
            return RedirectToCurrentDetail("view");
        }

        if (recipients.Count > 0 && !string.IsNullOrWhiteSpace(notifySubject) && !string.IsNullOrWhiteSpace(notifyBody))
        {
            TryQueueWorkflowNotifyEmail(recipients, notifySubject, notifyBody);
        }

        TempData["SuccessMessage"] = "Purchase order sent to approval successfully.";
        return RedirectToCurrentDetail("view");
    }

    public IActionResult OnGetPrLines(int prId, int? currentPoId)
    {
        try
        {
            return new JsonResult(new PrLinesResponse
            {
                Success = true,
                Data = LoadPrDetailRows(prId, currentPoId ?? Header.Id)
            });
        }
        catch (Exception ex)
        {
            return new JsonResult(new ApiResponse
            {
                Success = false,
                Message = ex.Message
            });
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

    public IActionResult OnPostUploadAttachment()
    {
        var prepare = PrepareExistingRecordForWorkflow();
        if (prepare != null)
        {
            return prepare;
        }

        if (!CanManageAttachments)
        {
            TempData["SuccessMessage"] = "You have no permission to upload attachment.";
            return RedirectToCurrentDetail("edit");
        }

        var attachmentUploads = Request.Form.Files;
        if (attachmentUploads == null || attachmentUploads.Count == 0)
        {
            TempData["SuccessMessage"] = "Please select at least one file to upload.";
            return RedirectToCurrentDetail("edit");
        }

        var validationMessages = attachmentUploads
            .Where(file => file != null && file.Length > 0)
            .Select(ValidateAttachment)
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToList();
        if (validationMessages.Count > 0)
        {
            TempData["SuccessMessage"] = string.Join(" ", validationMessages);
            return RedirectToCurrentDetail("edit");
        }

        var savedFilePaths = new List<string>();

        try
        {
            foreach (var attachment in attachmentUploads.Where(file => file != null && file.Length > 0))
            {
                SaveAttachment(attachment!, savedFilePaths);
            }

            TempData["SuccessMessage"] = "Attachments uploaded successfully.";
        }
        catch (Exception ex)
        {
            RemoveSavedFiles(savedFilePaths);
            TempData["SuccessMessage"] = ex.Message;
        }

        return RedirectToCurrentDetail("edit");
    }

    public IActionResult OnPostDeleteAttachments()
    {
        var prepare = PrepareExistingRecordForWorkflow();
        if (prepare != null)
        {
            return prepare;
        }

        if (!CanManageAttachments)
        {
            TempData["SuccessMessage"] = "You have no permission to delete attachment.";
            return RedirectToCurrentDetail("edit");
        }

        var selectedAttachmentFileNames = Request.Form["SelectedAttachmentFileNames"].ToArray();
        if (selectedAttachmentFileNames == null || selectedAttachmentFileNames.Length == 0)
        {
            TempData["SuccessMessage"] = "Please select at least one attachment to delete.";
            return RedirectToCurrentDetail("edit");
        }

        try
        {
            var folderPath = ResolveAttachmentFolder();
            if (!Directory.Exists(folderPath))
            {
                throw new InvalidOperationException("Attachment folder is not found.");
            }

            var fileNames = selectedAttachmentFileNames
                .Select(fileName => Path.GetFileName(fileName?.Trim() ?? string.Empty))
                .Where(fileName => !string.IsNullOrWhiteSpace(fileName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (fileNames.Count == 0)
            {
                throw new InvalidOperationException("Please select at least one attachment to delete.");
            }

            foreach (var fileName in fileNames)
            {
                var fullPath = Path.Combine(folderPath, fileName);
                if (!System.IO.File.Exists(fullPath))
                {
                    throw new InvalidOperationException($"Attachment file '{fileName}' is not found.");
                }
            }

            foreach (var fileName in fileNames)
            {
                var fullPath = Path.Combine(folderPath, fileName);
                System.IO.File.Delete(fullPath);
            }

            TempData["SuccessMessage"] = "Selected attachments deleted successfully.";
        }
        catch (Exception ex)
        {
            TempData["SuccessMessage"] = ex.Message;
        }

        return RedirectToCurrentDetail("edit");
    }

    public IActionResult OnGetDownloadAttachment(int id, string fileName)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionView))
        {
            return RedirectToPage("./Index");
        }

        if (id <= 0)
        {
            return RedirectToPage("./Index");
        }

        LoadPurchaseOrder(id);
        if (Header.Id <= 0)
        {
            return RedirectToPage("./Index");
        }

        var safeFileName = Path.GetFileName(fileName?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            return NotFound();
        }

        var fullPath = Path.Combine(ResolveAttachmentFolder(), safeFileName);
        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(fullPath, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        return File(System.IO.File.ReadAllBytes(fullPath), contentType, safeFileName);
    }

    public IActionResult OnGetReport(int id, bool autoPrint = false)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionView))
        {
            return new JsonResult(new { success = false, message = "No permission." });
        }

        if (id <= 0)
        {
            return new JsonResult(new { success = false, message = "Invalid PO id." });
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        var report = LoadPurchaseOrderReport(conn, id);
        if (string.IsNullOrWhiteSpace(report.PONo))
        {
            return new JsonResult(new { success = false, message = "Purchase order not found." });
        }

        return Partial("_PurchaseOrderHtmlReport", report);
    }



    public IActionResult OnGetReportQuestPdf(int id, bool download = true)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionView))
        {
            return RedirectToPage("./Index");
        }

        if (id <= 0)
        {
            return RedirectToPage("./Index");
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        var report = LoadPurchaseOrderReport(conn, id);
        if (string.IsNullOrWhiteSpace(report.PONo))
        {
            TempData["SuccessMessage"] = "Purchase order is not found.";
            return RedirectToPage("./Index");
        }

        var pdf = PurchaseOrderQuestPdfReport.BuildDetailPdf(report);
        var fileName = BuildReportPdfFileName(report.PONo, "questpdf");
        return download
            ? File(pdf, "application/pdf", fileName)
            : File(pdf, "application/pdf");
    }

    public IActionResult OnPostEvaluate()
    {
        var prepare = PrepareExistingRecordForWorkflow();
        if (prepare != null)
        {
            return prepare;
        }

        if (!CanEditEvaluate)
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

        string? notifySubject = null;
        string? notifyBody = null;
        List<PurchaseOrderNotifyRecipientViewModel> recipients = new List<PurchaseOrderNotifyRecipientViewModel>();

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

                recipients = GetWorkflowRecipientsByFlag(conn, trans, "IsBOD");
                notifySubject = "[Purchase Order] Waiting for BOD approve";
                notifyBody = BuildWorkflowNotifyBody(conn, trans, "PURCHASE ORDER", "#17a2b8", "is waiting for your approval.", true);
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

                recipients = GetPurchaserRecipient(conn, trans, Header.PurId);
                notifySubject = "[Purchase Order] Approved successfully";
                notifyBody = BuildWorkflowNotifyBody(conn, trans, "PURCHASE ORDER", "#28a745", "has been approved successfully.", false);
            }
            else
            {
                throw new InvalidOperationException("Current user cannot approve this purchase order.");
            }

            trans.Commit();
        }
        catch (Exception ex)
        {
            trans.Rollback();
            TempData["SuccessMessage"] = ex.Message;
            return RedirectToCurrentDetail("view");
        }

        if (recipients.Count > 0 && !string.IsNullOrWhiteSpace(notifySubject) && !string.IsNullOrWhiteSpace(notifyBody))
        {
            TryQueueWorkflowNotifyEmail(recipients, notifySubject, notifyBody);
        }

        TempData["SuccessMessage"] = "Purchase order approved successfully.";
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
            return RedirectToPage("./PurchaseOrderDetail", BuildDetailRouteValues("edit", true));
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
        DepartmentOptions = LoadListFromSql("SELECT DeptID, DeptCode AS DeptText FROM dbo.MS_Department ORDER BY DeptCode", "DeptID", "DeptText");
    }

    private List<SelectListItem> LoadPrOptions(int? selectedPrId)
    {
        var rows = new List<SelectListItem>();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
        SELECT TOP 20
            PRID,
            ISNULL(RequestNo, '') AS RequestNo
        FROM dbo.PC_PR
        WHERE ISNULL(Status, 0) <> 4
        ORDER BY RequestDate DESC, RequestNo DESC", conn);
        conn.Open();
        using (var reader = cmd.ExecuteReader())
        {
            while (reader.Read())
            {
                var value = Convert.ToString(reader["PRID"]) ?? string.Empty;
                rows.Add(new SelectListItem
                {
                    Value = value,
                    Text = Convert.ToString(reader["RequestNo"]) ?? string.Empty,
                    Selected = selectedPrId.HasValue && value == selectedPrId.Value.ToString(CultureInfo.InvariantCulture)
                });
            }
        }

        if (selectedPrId.HasValue && selectedPrId.Value > 0 && rows.All(x => x.Value != selectedPrId.Value.ToString(CultureInfo.InvariantCulture)))
        {
            using var selectedCmd = new SqlCommand(@"
            SELECT TOP 1
                PRID,
                ISNULL(RequestNo, '') AS RequestNo
            FROM dbo.PC_PR
            WHERE PRID = @PRID", conn);
            selectedCmd.Parameters.Add("@PRID", SqlDbType.Int).Value = selectedPrId.Value;
            using var selectedReader = selectedCmd.ExecuteReader();
            if (selectedReader.Read())
            {
                rows.Add(new SelectListItem
                {
                    Value = Convert.ToString(selectedReader["PRID"]) ?? string.Empty,
                    Text = Convert.ToString(selectedReader["RequestNo"]) ?? string.Empty,
                    Selected = true
                });
            }
        }

        return rows;
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

    private PurchaseOrderDetailReportModel LoadPurchaseOrderReport(SqlConnection conn, int id)
    {
        var report = new PurchaseOrderDetailReportModel();

        int? preparedEmployeeId = null;
        int? checkedEmployeeId = null;
        int? approvedEmployeeId = null;
        string preparedDateText = string.Empty;
        string checkedDateText = string.Empty;
        string approvedDateText = string.Empty;
        string notesText = string.Empty;

        using (var cmd = new SqlCommand(@"
        SELECT TOP 1
            p.POID,
            ISNULL(p.PONo, '') AS PONo,
            p.PODate,
            ISNULL(pr.RequestNo, '') AS RequestNo,
            ISNULL(s.SupplierCode, '') AS SupplierCode,
            ISNULL(s.SupplierName, '') AS SupplierName,
            ISNULL(s.Address, '') AS SupplierAddress,
            ISNULL(s.Phone, '') AS SupplierTel,
            ISNULL(s.Email, '') AS SupplierEmail,
            ISNULL(s.Contact, '') AS SupplierContact,
            ISNULL(c.CurrencyName, '') AS CurrencyName,
            ISNULL(p.POTerms, '') AS POTerms,
            ISNULL(p.Comment, '') AS Note,
            ISNULL(p.Remark, '') AS Remark,
            ISNULL(p.BeforeVAT, 0) AS BeforeVAT,
            ISNULL(p.PerVAT, 0) AS PerVAT,
            ISNULL(p.VAT, 0) AS VAT,
            ISNULL(p.AfterVAT, 0) AS AfterVAT,
            p.PurId,
            p.CAId,
            p.GDId,
            p.PurApproDate,
            p.CAApproDate,
            p.GDApproDate,
            ISNULL(param.CoName, '') AS CoName,
            ISNULL(param.CoAddress, '') AS CoAddress,
            ISNULL(param.CoPhone, '') AS CoPhone,
            ISNULL(param.CoEmail, '') AS CoEmail,
            ISNULL(param.CoVATCode, '') AS CoVATCode
        FROM dbo.PC_PO p
        LEFT JOIN dbo.PC_PR pr ON pr.PRID = p.PRID
        LEFT JOIN dbo.PC_Suppliers s ON s.SupplierID = p.SupplierID
        LEFT JOIN dbo.MS_CurrencyFL c ON c.CurrencyID = p.Currency
        OUTER APPLY (SELECT TOP 1 CoName, CoAddress, CoPhone, CoEmail, CoVATCode FROM dbo.MS_Parameters) param
        WHERE p.POID = @POID", conn))
        {
            cmd.Parameters.Add("@POID", SqlDbType.Int).Value = id;
            using var rd = cmd.ExecuteReader();
            if (!rd.Read())
            {
                return report;
            }

            var supplierCode = Convert.ToString(rd["SupplierCode"]) ?? string.Empty;
            var supplierName = Convert.ToString(rd["SupplierName"]) ?? string.Empty;
            var supplierDisplay = string.IsNullOrWhiteSpace(supplierName)
                ? supplierCode
                : string.IsNullOrWhiteSpace(supplierCode)
                    ? supplierName
                    : $"{supplierCode} / {supplierName}";

            report.PONo = Convert.ToString(rd["PONo"]) ?? string.Empty;
            report.PODate = rd.IsDBNull(rd.GetOrdinal("PODate")) ? null : Convert.ToDateTime(rd["PODate"]);
            report.RequestNo = Convert.ToString(rd["RequestNo"]) ?? string.Empty;
            report.SupplierDisplay = supplierDisplay;
            report.SupplierAddress = Convert.ToString(rd["SupplierAddress"]) ?? string.Empty;
            report.SupplierTel = Convert.ToString(rd["SupplierTel"]) ?? string.Empty;
            report.SupplierEmail = Convert.ToString(rd["SupplierEmail"]) ?? string.Empty;
            report.SupplierContact = Convert.ToString(rd["SupplierContact"]) ?? string.Empty;
            report.CurrencyText = Convert.ToString(rd["CurrencyName"]) ?? string.Empty;
            report.TermsAndConditions = Convert.ToString(rd["POTerms"]) ?? string.Empty;
            report.Remark = Convert.ToString(rd["Remark"]) ?? string.Empty;
            report.BeforeVAT = rd.IsDBNull(rd.GetOrdinal("BeforeVAT")) ? 0 : Convert.ToDecimal(rd["BeforeVAT"]);
            report.PerVAT = rd.IsDBNull(rd.GetOrdinal("PerVAT")) ? 0 : Convert.ToDecimal(rd["PerVAT"]);
            report.VAT = rd.IsDBNull(rd.GetOrdinal("VAT")) ? 0 : Convert.ToDecimal(rd["VAT"]);
            report.AfterVAT = rd.IsDBNull(rd.GetOrdinal("AfterVAT")) ? 0 : Convert.ToDecimal(rd["AfterVAT"]);

            report.CoName = Convert.ToString(rd["CoName"]) ?? string.Empty;
            report.CoAddress = Convert.ToString(rd["CoAddress"]) ?? string.Empty;
            report.CoPhone = Convert.ToString(rd["CoPhone"]) ?? string.Empty;
            report.CoEmail = Convert.ToString(rd["CoEmail"]) ?? string.Empty;
            report.CoVATCode = Convert.ToString(rd["CoVATCode"]) ?? string.Empty;

            preparedEmployeeId = rd.IsDBNull(rd.GetOrdinal("PurId")) ? null : Convert.ToInt32(rd["PurId"]);
            checkedEmployeeId = rd.IsDBNull(rd.GetOrdinal("CAId")) ? null : Convert.ToInt32(rd["CAId"]);
            approvedEmployeeId = rd.IsDBNull(rd.GetOrdinal("GDId")) ? null : Convert.ToInt32(rd["GDId"]);
            preparedDateText = NormalizeReportDateText(Convert.ToString(rd["PurApproDate"]));
            checkedDateText = NormalizeReportDateText(Convert.ToString(rd["CAApproDate"]));
            approvedDateText = NormalizeReportDateText(Convert.ToString(rd["GDApproDate"]));
            notesText = BuildNotesText(Convert.ToString(rd["Note"]) ?? string.Empty);
            rd.Close();
        }

        report.Notes = notesText;
        var preparedTitle = LoadEmployeeTitle(conn, preparedEmployeeId);
        var checkedTitle = LoadEmployeeTitle(conn, checkedEmployeeId);
        var approvedTitle = LoadEmployeeTitle(conn, approvedEmployeeId);

        report.Footer = new PurchaseOrderApprovalFooterModel
        {
            PreparedDate = preparedDateText,
            CheckedDate = checkedDateText,
            ApprovedDate = approvedDateText,
            PreparedName = LoadEmployeeFullName(conn, preparedEmployeeId),
            CheckedName = LoadEmployeeFullName(conn, checkedEmployeeId),
            ApprovedName = LoadEmployeeFullName(conn, approvedEmployeeId),
            PreparedTitle = string.IsNullOrWhiteSpace(preparedTitle) ? "Purchaser" : preparedTitle,
            CheckedTitle = string.IsNullOrWhiteSpace(checkedTitle) ? "Chief Accountant" : checkedTitle,
            ApprovedTitle = string.IsNullOrWhiteSpace(approvedTitle) ? "Tổng Giám Đốc" : approvedTitle,
            DeliveryDate = report.PODate.HasValue ? report.PODate.Value.ToString("MMMM yyyy", CultureInfo.InvariantCulture) : string.Empty,
            DeliveryPlace = "20 Le Thanh Ton st, Sai Gon Ward, HCMC, VN",
            Receiver = BuildReceiverText(report.SupplierContact, report.SupplierTel),
            PaymentTerm = report.TermsAndConditions,
            PreparedSignature = LoadEmployeeSignature(conn, preparedEmployeeId, true),
            CheckedSignature = LoadEmployeeSignature(conn, checkedEmployeeId, true),
            ApprovedSignature = LoadEmployeeSignature(conn, approvedEmployeeId, false),
            Notes = notesText
        };

        using var detailCmd = new SqlCommand(@"
        SELECT
            ROW_NUMBER() OVER (ORDER BY d.RecordID) AS No,
            ISNULL(i.ItemCode, '') AS ItemCode,
            ISNULL(i.ItemName, '') AS ItemName,
            ISNULL(i.Unit, '') AS Unit,
            ISNULL(d.Quantity, 0) AS Quantity,
            ISNULL(d.UnitPrice, 0) AS UnitPrice,
            ISNULL(d.POAmount, 0) AS Amount,
            ISNULL(d.Note, '') AS Remark
        FROM dbo.PC_PODetail d
        LEFT JOIN dbo.INV_ItemList i ON i.ItemID = d.ItemID
        WHERE d.POID = @POID
        ORDER BY d.RecordID", conn);
        detailCmd.Parameters.Add("@POID", SqlDbType.Int).Value = id;

        using var detailReader = detailCmd.ExecuteReader();
        while (detailReader.Read())
        {
            report.Items.Add(new PurchaseOrderDetailReportItem
            {
                No = detailReader.IsDBNull(detailReader.GetOrdinal("No")) ? 0 : Convert.ToInt32(detailReader["No"]),
                ItemCode = Convert.ToString(detailReader["ItemCode"]) ?? string.Empty,
                ItemName = Convert.ToString(detailReader["ItemName"]) ?? string.Empty,
                Unit = Convert.ToString(detailReader["Unit"]) ?? string.Empty,
                Quantity = detailReader.IsDBNull(detailReader.GetOrdinal("Quantity")) ? 0 : Convert.ToDecimal(detailReader["Quantity"]),
                UnitPrice = detailReader.IsDBNull(detailReader.GetOrdinal("UnitPrice")) ? 0 : Convert.ToDecimal(detailReader["UnitPrice"]),
                Amount = detailReader.IsDBNull(detailReader.GetOrdinal("Amount")) ? 0 : Convert.ToDecimal(detailReader["Amount"]),
                Remark = Convert.ToString(detailReader["Remark"]) ?? string.Empty
            });
        }

        return report;
    }

    private string LoadEmployeeFullName(SqlConnection conn, int? employeeId)
    {
        if (!employeeId.HasValue || employeeId.Value <= 0)
        {
            return string.Empty;
        }

        using var cmd = new SqlCommand(@"
        SELECT TOP 1 ISNULL(EmployeeName, '')
        FROM dbo.MS_Employee
        WHERE EmployeeID = @EmployeeID", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId.Value;
        var value = cmd.ExecuteScalar();
        return value == null || value == DBNull.Value ? string.Empty : Convert.ToString(value)?.Trim() ?? string.Empty;
    }

    private string LoadEmployeeTitle(SqlConnection conn, int? employeeId)
    {
        if (!employeeId.HasValue || employeeId.Value <= 0)
        {
            return string.Empty;
        }

        if (!EmployeePositionTitleColumnExists(conn))
        {
            return string.Empty;
        }

        using var cmd = new SqlCommand(@"
        SELECT TOP 1 ISNULL(PositionTitle, '')
        FROM dbo.MS_Employee
        WHERE EmployeeID = @EmployeeID", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId.Value;
        var value = cmd.ExecuteScalar();
        return value == null || value == DBNull.Value ? string.Empty : Convert.ToString(value)?.Trim() ?? string.Empty;
    }

    private static bool EmployeePositionTitleColumnExists(SqlConnection conn)
    {
        using var cmd = new SqlCommand(@"
        SELECT CASE
            WHEN COL_LENGTH('dbo.MS_Employee', 'PositionTitle') IS NULL THEN 0
            ELSE 1
        END", conn);
        var value = cmd.ExecuteScalar();
        return value != null && value != DBNull.Value && Convert.ToInt32(value) == 1;
    }

    private byte[]? LoadEmployeeSignature(SqlConnection conn, int? employeeId, bool useSmallSignature)
    {
        if (!employeeId.HasValue || employeeId.Value <= 0)
        {
            return null;
        }

        var signatureColumn = useSmallSignature ? "UrlSmallSign" : "UrlNomalSign";
        using var cmd = new SqlCommand(@"
        SELECT
            ISNULL(" + signatureColumn + @", '') AS SignatureFileName
        FROM dbo.MS_Employee
        WHERE EmployeeID = @EmployeeID", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId.Value;

        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        var signatureFileName = Convert.ToString(reader["SignatureFileName"])?.Trim();
        var signaturePath = ResolveEmployeeSignaturePathValue(signatureFileName);
        if (!string.IsNullOrWhiteSpace(signaturePath) && System.IO.File.Exists(signaturePath))
        {
            return System.IO.File.ReadAllBytes(signaturePath);
        }

        return null;
    }

    private string ResolveEmployeeSignaturePathValue(string? signatureFileName)
    {
        if (string.IsNullOrWhiteSpace(signatureFileName))
        {
            return string.Empty;
        }

        var signatureReference = signatureFileName.Trim();
        if (System.IO.File.Exists(signatureReference))
        {
            return signatureReference;
        }

        var signatureSegments = signatureReference
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (signatureSegments.Length == 0 || signatureSegments.Any(x => x == "." || x == ".."))
        {
            return string.Empty;
        }

        var contentRootCandidate = Path.Combine([_webHostEnvironment.ContentRootPath, .. signatureSegments]);
        if (System.IO.File.Exists(contentRootCandidate))
        {
            return contentRootCandidate;
        }

        var basePath = _config.GetValue<string>("FileUploads:BasePath");
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return string.Empty;
        }

        var rootPath = Path.IsPathRooted(basePath)
            ? basePath
            : Path.Combine(_webHostEnvironment.ContentRootPath, basePath);

        var uploadRootCandidate = Path.Combine([rootPath, .. signatureSegments]);
        if (System.IO.File.Exists(uploadRootCandidate))
        {
            return uploadRootCandidate;
        }

        var employeeSignatureCandidate = Path.Combine([rootPath, "Admin", "Employee", .. signatureSegments]);
        if (System.IO.File.Exists(employeeSignatureCandidate))
        {
            return employeeSignatureCandidate;
        }

        return string.Empty;
    }

    private static string NormalizeReportDateText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();
        return DateTime.TryParse(trimmed, out var parsed)
            ? parsed.ToString("d/M/yyyy", CultureInfo.InvariantCulture)
            : trimmed;
    }

    private static string BuildNotesText(string note)
    {
        return string.IsNullOrWhiteSpace(note) ? string.Empty : note.Trim();
    }

    private static string BuildReceiverText(string contact, string tel)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(contact))
        {
            parts.Add(contact.Trim());
        }

        if (!string.IsNullOrWhiteSpace(tel))
        {
            parts.Add($"Tel: {tel.Trim()}");
        }

        return string.Join(" - ", parts);
    }

    private static string BuildDefaultPurchaseOrderTerms(DateTime poDate)
    {
        var deliveryDate = poDate.ToString("MMMM yyyy", CultureInfo.InvariantCulture);

        return string.Join(Environment.NewLine, new[]
        {
            $"Thá»i gian giao nháº­n (Delivery date): {deliveryDate}",
            "Äá»‹a Ä‘iá»ƒm giao nháº­n (Deliver place): 20 Le Thanh Ton st, Sai Gon Ward, HCMC, VN",
            "NgÆ°á»i nháº­n hÃ ng (Receiver): ",
            "PhÆ°Æ¡ng thá»©c thanh toÃ¡n (Payment term): "
        });
    }

    private List<object> LoadSupplierLookup(string? term)
    {
        var rows = new List<object>();
        var searchTerm = term?.Trim();
        if (string.IsNullOrWhiteSpace(searchTerm) || searchTerm.Length < 3)
        {
            return rows;
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
        SELECT TOP 20
            SupplierID,
            ISNULL(SupplierCode, '') AS SupplierCode,
            ISNULL(SupplierName, '') AS SupplierName
        FROM dbo.PC_Suppliers
        WHERE Status >= 0
          AND Status < 5
          AND (
                @term IS NULL
                OR SupplierCode LIKE '%' + @term + '%'
                OR SupplierName LIKE '%' + @term + '%'
          )
        ORDER BY
            CASE
                WHEN SupplierCode = @term OR SupplierName = @term THEN 0
                WHEN SupplierCode LIKE @term + '%' THEN 1
                WHEN SupplierName LIKE @term + '%' THEN 2
                ELSE 3
            END,
            SupplierCode", conn);
        cmd.Parameters.Add("@term", SqlDbType.NVarChar, 100).Value = searchTerm;
        conn.Open();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var code = Convert.ToString(reader["SupplierCode"]) ?? string.Empty;
            var name = Convert.ToString(reader["SupplierName"]) ?? string.Empty;
            rows.Add(new
            {
                id = Convert.ToString(reader["SupplierID"]) ?? string.Empty,
                text = code,
                supplierCode = code,
                supplierName = name
            });
        }

        return rows.Cast<object>().ToList();
    }

    private List<object> LoadPrLookup(string? term)
    {
        var rows = new List<object>();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
        SELECT TOP 20
            PRID,
            ISNULL(RequestNo, '') AS RequestNo
        FROM dbo.PC_PR
        WHERE ISNULL(Status, 0) <> 4
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
        AttachmentList = new List<PurchaseOrderAttachmentViewModel>();
        CurrentEstimatePoint = 0;
        EvaluateOptions = new List<PurchaseOrderEvaluateOptionViewModel>();
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
            ISNULL(Comment, '') AS Note,
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
                        Note = Convert.ToString(rd["Note"]) ?? string.Empty,
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
            d.RecordID AS DetailID,
            d.ItemID,
            ISNULL(d.MRDetailID, 0) AS PrDetailId,
            ISNULL(i.ItemCode, '') AS ItemCode,
            ISNULL(i.ItemName, '') AS ItemName,
            ISNULL(i.Unit, '') AS Unit,
            ISNULL(d.Quantity, 0) AS Quantity,
            ISNULL(d.UnitPrice, 0) AS UnitPrice,
            ISNULL(d.POAmount, 0) AS POAmount,
            d.RecDept,
            ISNULL(dep.DeptCode, '') AS RecDeptName,
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

        using (var detailReader = detailCmd.ExecuteReader())
        {
            while (detailReader.Read())
            {
                Details.Add(new PurchaseOrderDetailInput
                {
                    TempKey = Guid.NewGuid().ToString("N"),
                    IsPersisted = true,
                    DetailID = detailReader.IsDBNull(detailReader.GetOrdinal("DetailID")) ? 0 : Convert.ToInt32(detailReader["DetailID"]),
                    ItemID = Convert.ToInt32(detailReader["ItemID"]),
                    PrDetailId = detailReader.IsDBNull(detailReader.GetOrdinal("PrDetailId")) ? 0 : Convert.ToInt64(detailReader["PrDetailId"]),
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

        using (var estimateCmd = new SqlCommand(@"
        SELECT TOP 1
            ISNULL(Point, 0) AS Point
        FROM dbo.PO_Estimate
        WHERE POID = @POID
          AND PO_IndexDetailID IN (13, 14, 15, 16)
        ORDER BY TheDate DESC, PO_IndexDetailID DESC", conn))
        {
            estimateCmd.Parameters.Add("@POID", SqlDbType.Int).Value = id;
            var estimateValue = estimateCmd.ExecuteScalar();
            CurrentEstimatePoint = estimateValue == null || estimateValue == DBNull.Value
                ? 0
                : Convert.ToInt32(estimateValue);
        }

        EvaluateOptions = LoadEvaluateOptions(conn);

        AttachmentList = Header.Id > 0 ? LoadAttachmentRows() : new List<PurchaseOrderAttachmentViewModel>();
    }

    private static List<PurchaseOrderEvaluateOptionViewModel> LoadEvaluateOptions(SqlConnection conn)
    {
        var rows = new List<PurchaseOrderEvaluateOptionViewModel>();

        using var cmd = new SqlCommand(@"
        SELECT
            PO_IndexDetailID,
            ISNULL(PO_IndexDetailName, '') AS PO_IndexDetailName,
            ISNULL(Point, 0) AS Point
        FROM dbo.PO_IndexDetail
        WHERE PO_IndexDetailID IN (13, 14, 15, 16)
        ORDER BY
            CASE PO_IndexDetailID
                WHEN 16 THEN 1
                WHEN 15 THEN 2
                WHEN 14 THEN 3
                WHEN 13 THEN 4
                ELSE 99
            END", conn);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var detailId = reader.IsDBNull(reader.GetOrdinal("PO_IndexDetailID")) ? 0 : Convert.ToInt32(reader["PO_IndexDetailID"]);
            var estimatePoint = detailId switch
            {
                16 => 1,
                15 => 2,
                14 => 3,
                13 => 4,
                _ => 0
            };

            if (estimatePoint <= 0)
            {
                continue;
            }

            rows.Add(new PurchaseOrderEvaluateOptionViewModel
            {
                POIndexDetailId = detailId,
                EstimatePoint = estimatePoint,
                Name = Convert.ToString(reader["PO_IndexDetailName"]) ?? string.Empty,
                Point = reader.IsDBNull(reader.GetOrdinal("Point")) ? 0 : Convert.ToInt32(reader["Point"])
            });
        }

        return rows;
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
        Header.Note = currentInput.Note;
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

    private List<PurchaseOrderPrLineLookup> LoadPrDetailRows(int prId, int currentPoId)
    {
        var rows = new List<PurchaseOrderPrLineLookup>();
        if (prId <= 0)
        {
            return rows;
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
        ;WITH UsedQuantity AS (
            SELECT
                poDetail.MRDetailID,
                SUM(ISNULL(poDetail.Quantity, 0)) AS UsedQuantity
            FROM dbo.PC_PODetail poDetail
            INNER JOIN dbo.PC_PO po ON po.POID = poDetail.POID
            WHERE po.PRID = @PRID
              AND poDetail.MRDetailID IS NOT NULL
              AND po.POID <> @CurrentPOID
            GROUP BY poDetail.MRDetailID
        )
        SELECT
            d.RecordID,
            d.ItemID,
            ISNULL(i.ItemCode, '') AS ItemCode,
            ISNULL(i.ItemName, '') AS ItemName,
            ISNULL(i.Unit, '') AS Unit,
            CASE
                WHEN ISNULL(d.Quantity, 0) - ISNULL(u.UsedQuantity, 0) < 0 THEN 0
                ELSE ISNULL(d.Quantity, 0) - ISNULL(u.UsedQuantity, 0)
            END AS Quantity,
            ISNULL(d.UnitPrice, 0) AS UnitPrice,
            ISNULL(d.Remark, '') AS Remark,
            d.SupplierID,
            CASE WHEN s.SupplierID IS NULL THEN '' ELSE ISNULL(s.SupplierCode, '') + ' / ' + ISNULL(s.SupplierName, '') END AS SupplierText
        FROM dbo.PC_PRDetail d
        LEFT JOIN dbo.INV_ItemList i ON i.ItemID = d.ItemID
        LEFT JOIN dbo.PC_Suppliers s ON s.SupplierID = d.SupplierID
        LEFT JOIN UsedQuantity u ON u.MRDetailID = d.RecordID
        WHERE d.PRID = @PRID
        ORDER BY d.RecordID", conn);
        cmd.Parameters.Add("@PRID", SqlDbType.Int).Value = prId;
        cmd.Parameters.Add("@CurrentPOID", SqlDbType.Int).Value = currentPoId > 0 ? currentPoId : 0;
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
                (POID, ItemID, MRDetailID, Quantity, UnitPrice, POAmount, RecDept, Note, RecQty, RecAmount, RecDate, MRRequestNO)
                VALUES
                (@POID, @ItemID, @MRDetailID, @Quantity, @UnitPrice, @POAmount, @RecDept, @Note, @RecQty, @RecAmount, @RecDate, @MRRequestNO)", conn, trans);
                insertDetailCmd.Parameters.Add("@POID", SqlDbType.Int).Value = Header.Id;
                insertDetailCmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = detail.ItemID;
                insertDetailCmd.Parameters.Add("@MRDetailID", SqlDbType.Int).Value = detail.PrDetailId > 0 ? detail.PrDetailId : DBNull.Value;
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
        cmd.Parameters.Add("@Comment", SqlDbType.NVarChar, 2000).Value = string.IsNullOrWhiteSpace(Header.Note) ? DBNull.Value : Header.Note;
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

        if (!string.IsNullOrWhiteSpace(Header.Note) && Header.Note.Length > 100)
        {
            ModelState.AddModelError("Header.Note", "Note must not exceed 100 characters.");
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
        CanEvaluate = Header.Id > 0;
        CanEditEvaluate = Header.Id > 0 && _workflowUser.IsPurchaser;
        CanApprove = Header.Id > 0 && Header.StatusId == 2 && (CanApproveAsCfo() || CanApproveAsBod());
        CanBackToProcessing = Header.Id > 0
            && Header.StatusId == 2
            && effectivePermissions.Contains(PermissionBackToProcessing)
            && (_workflowUser.IsPurchaser || _workflowUser.IsCFO || _workflowUser.IsBOD || IsAdminRole());
        CanManageAttachments = Header.Id > 0
            && Header.StatusId == 1
            && effectivePermissions.Contains(PermissionEdit);
    }

    private bool CanApproveAsCfo()
    {
        return Header.Id > 0 && Header.StatusId == 2 && !Header.CAId.HasValue && (_workflowUser.IsCFO || IsAdminRole());
    }

    private bool CanApproveAsBod()
    {
        return Header.Id > 0 && Header.StatusId == 2 && Header.CAId.HasValue && !Header.GDId.HasValue && (_workflowUser.IsBOD || IsAdminRole());
    }

    private static List<PurchaseOrderNotifyRecipientViewModel> GetWorkflowRecipientsByFlag(SqlConnection conn, SqlTransaction trans, string flagColumn)
    {
        var rows = new List<PurchaseOrderNotifyRecipientViewModel>();

        using var cmd = new SqlCommand($@"
SELECT DISTINCT
    LTRIM(RTRIM(TheEmail)) AS TheEmail,
    LTRIM(RTRIM(EmployeeCode)) AS EmployeeCode,
    LTRIM(RTRIM(EmployeeName)) AS EmployeeName,
    LTRIM(RTRIM(ISNULL(Title, ''))) AS Title
FROM dbo.MS_Employee
WHERE ISNULL({flagColumn}, 0) = 1
  AND ISNULL(LTRIM(RTRIM(TheEmail)), '') <> ''
  AND ISNULL(IsActive, 0) = 1", conn, trans);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var email = Convert.ToString(reader["TheEmail"]) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(email))
            {
                continue;
            }

            rows.Add(new PurchaseOrderNotifyRecipientViewModel
            {
                Email = email.Trim(),
                EmployeeCode = Convert.ToString(reader["EmployeeCode"])?.Trim() ?? string.Empty,
                EmployeeName = Convert.ToString(reader["EmployeeName"])?.Trim() ?? string.Empty,
                Title = Convert.ToString(reader["Title"])?.Trim() ?? string.Empty
            });
        }

        return rows;
    }

    private static List<PurchaseOrderNotifyRecipientViewModel> GetPurchaserRecipient(SqlConnection conn, SqlTransaction trans, int? employeeId)
    {
        var rows = new List<PurchaseOrderNotifyRecipientViewModel>();
        if (!employeeId.HasValue || employeeId.Value <= 0)
        {
            return rows;
        }

        using var cmd = new SqlCommand(@"
SELECT TOP 1
    LTRIM(RTRIM(TheEmail)) AS TheEmail,
    LTRIM(RTRIM(EmployeeCode)) AS EmployeeCode,
    LTRIM(RTRIM(EmployeeName)) AS EmployeeName,
    LTRIM(RTRIM(ISNULL(Title, ''))) AS Title
FROM dbo.MS_Employee
WHERE EmployeeID = @EmployeeID
  AND ISNULL(LTRIM(RTRIM(TheEmail)), '') <> ''
  AND ISNULL(IsActive, 0) = 1", conn, trans);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId.Value;

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var email = Convert.ToString(reader["TheEmail"]) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(email))
            {
                continue;
            }

            rows.Add(new PurchaseOrderNotifyRecipientViewModel
            {
                Email = email.Trim(),
                EmployeeCode = Convert.ToString(reader["EmployeeCode"])?.Trim() ?? string.Empty,
                EmployeeName = Convert.ToString(reader["EmployeeName"])?.Trim() ?? string.Empty,
                Title = Convert.ToString(reader["Title"])?.Trim() ?? string.Empty
            });
        }

        return rows;
    }

    private string BuildWorkflowNotifyBody(SqlConnection conn, SqlTransaction trans, string title, string color, string actionText, bool showApproveLink)
    {
        var detailUrl = Url.Page("/Purchasing/PurchaseOrder/PurchaseOrderDetail", values: new
        {
            id = Header.Id,
            mode = "view"
        });

        var absoluteUrl = string.IsNullOrWhiteSpace(detailUrl)
            ? string.Empty
            : $"{Request.Scheme}://{Request.Host}{detailUrl}";
        var remarkText = string.IsNullOrWhiteSpace(Header.Remark) ? string.Empty : Header.Remark.Trim();
        var totalAmount = GetCurrentTotalAmount(conn, trans);

        var body = $@"
<p>Dear {{RECIPIENT_LABEL}},</p>
<p>Purchase Order <b>{WebUtility.HtmlEncode(Header.PONo)}</b> {WebUtility.HtmlEncode(actionText)}</p>
<ul>
  <li>Date: <b>{Header.PODate:MMM dd, yyyy}</b></li>
  <li>Remark: <b>{WebUtility.HtmlEncode(remarkText)}</b></li>
  <li>Total Amount: <b>{FormatAmountForView(totalAmount)} {WebUtility.HtmlEncode(GetCurrencyDisplayText(Header.Currency))}</b></li>
</ul>
{(showApproveLink && !string.IsNullOrWhiteSpace(absoluteUrl) ? $"<p>Click Here to Approve: <a href=\"{WebUtility.HtmlEncode(absoluteUrl)}\">Purchase Order Approve</a></p>" : string.Empty)}
<p>SmartSam System</p>";

        return EmailTemplateHelper.WrapInNotifyTemplate(title, color, DateTime.Now, body);
    }

    private decimal GetCurrentTotalAmount(SqlConnection conn, SqlTransaction trans)
    {
        using var cmd = new SqlCommand(@"
SELECT ISNULL(AfterVAT, 0)
FROM dbo.PC_PO
WHERE POID = @POID", conn, trans);
        cmd.Parameters.Add("@POID", SqlDbType.Int).Value = Header.Id;

        var value = cmd.ExecuteScalar();
        return value == null || value == DBNull.Value ? 0M : Convert.ToDecimal(value);
    }

    private static string FormatAmountForView(decimal amount)
    {
        return amount.ToString("#,##0.###");
    }

    private string GetCurrencyDisplayText(int currencyId)
    {
        var selected = CurrencyOptions.FirstOrDefault(x => string.Equals(x.Value, currencyId.ToString(), StringComparison.OrdinalIgnoreCase));
        if (selected != null && !string.IsNullOrWhiteSpace(selected.Text))
        {
            return selected.Text;
        }

        return currencyId switch
        {
            1 => "VND",
            _ => currencyId.ToString()
        };
    }

    private void TryQueueWorkflowNotifyEmail(List<PurchaseOrderNotifyRecipientViewModel> recipients, string subject, string htmlBody)
    {
        var senderEmail = _config.GetValue<string>("EmailSettings:SenderEmail");
        var password = _config.GetValue<string>("EmailSettings:Password");
        var mailServer = _config.GetValue<string>("EmailSettings:MailServer");
        var mailPort = _config.GetValue<int?>("EmailSettings:MailPort") ?? 0;

        if (recipients.Count == 0 ||
            string.IsNullOrWhiteSpace(senderEmail) ||
            string.IsNullOrWhiteSpace(password) ||
            string.IsNullOrWhiteSpace(mailServer) ||
            mailPort <= 0)
        {
            return;
        }

        _ = SendNotifyEmailAsync(new PurchaseOrderWorkflowNotifyRequestViewModel
        {
            SenderEmail = senderEmail,
            Password = password,
            MailServer = mailServer,
            MailPort = mailPort,
            Subject = ApplyMailSubjectPrefix(subject),
            HtmlBody = htmlBody,
            Recipients = recipients
        });
    }

    private string ApplyMailSubjectPrefix(string subject)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return subject;
        }

        var prefix = _config.GetValue<string>("EmailSettings:PrefixSubject")?.Trim();
        if (string.IsNullOrWhiteSpace(prefix) || !ShouldApplyTestSubjectPrefix())
        {
            return subject;
        }

        return $"{prefix} - {subject}";
    }

    private bool ShouldApplyTestSubjectPrefix()
    {
        var configuredIds = _config.GetValue<string>("EmailSettings:TestFunctionIDs");
        if (string.IsNullOrWhiteSpace(configuredIds))
        {
            return false;
        }

        return configuredIds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(value => int.TryParse(value, out var id) && id == FUNCTION_ID);
    }

    private async Task SendNotifyEmailAsync(PurchaseOrderWorkflowNotifyRequestViewModel notifyRequest)
    {
        try
        {
            foreach (var recipient in notifyRequest.Recipients)
            {
                if (string.IsNullOrWhiteSpace(recipient.Email))
                {
                    continue;
                }

                using var mail = new MailMessage
                {
                    From = new MailAddress(notifyRequest.SenderEmail, "SmartSam System"),
                    Subject = notifyRequest.Subject,
                    Body = notifyRequest.HtmlBody.Replace("{RECIPIENT_LABEL}", WebUtility.HtmlEncode(BuildRecipientLabel(recipient)), StringComparison.Ordinal),
                    IsBodyHtml = true
                };

                mail.To.Add(recipient.Email);
                mail.CC.Add(NotifyCcEmail);

                using var smtp = new SmtpClient(notifyRequest.MailServer, notifyRequest.MailPort)
                {
                    EnableSsl = true,
                    Credentials = new NetworkCredential(notifyRequest.SenderEmail, notifyRequest.Password)
                };

                await smtp.SendMailAsync(mail);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot send workflow email for purchase order {PoId}.", Header.Id);
        }
    }

    private static string BuildRecipientLabel(PurchaseOrderNotifyRecipientViewModel recipient)
    {
        var normalizedTitle = NormalizeGreetingTitle(recipient.Title);
        var employeeName = (recipient.EmployeeName ?? string.Empty).Trim();
        var employeeCode = (recipient.EmployeeCode ?? string.Empty).Trim();
        var email = (recipient.Email ?? string.Empty).Trim();

        if (!string.IsNullOrWhiteSpace(employeeName))
        {
            return string.IsNullOrWhiteSpace(normalizedTitle)
                ? (string.IsNullOrWhiteSpace(employeeCode) ? employeeName : $"{employeeName}({employeeCode})")
                : (string.IsNullOrWhiteSpace(employeeCode) ? $"{normalizedTitle} {employeeName}" : $"{normalizedTitle} {employeeName}({employeeCode})");
        }

        if (!string.IsNullOrWhiteSpace(employeeCode))
        {
            return employeeCode;
        }

        return email;
    }

    private static string NormalizeGreetingTitle(string? title)
    {
        var trimmed = (title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        return trimmed.EndsWith(".", StringComparison.Ordinal) ? trimmed : $"{trimmed}.";
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

    private List<PurchaseOrderAttachmentViewModel> LoadAttachmentRows()
    {
        var rows = new List<PurchaseOrderAttachmentViewModel>();
        var folderPath = ResolveAttachmentFolder();
        if (!Directory.Exists(folderPath))
        {
            return rows;
        }

        foreach (var filePath in Directory.GetFiles(folderPath).OrderByDescending(System.IO.File.GetLastWriteTimeUtc))
        {
            var fileInfo = new FileInfo(filePath);
            rows.Add(new PurchaseOrderAttachmentViewModel
            {
                FileName = fileInfo.Name,
                ModifiedDate = fileInfo.LastWriteTime,
                SizeBytes = fileInfo.Length,
                SizeText = FormatFileSize(fileInfo.Length)
            });
        }

        return rows;
    }

    private string? ValidateAttachment(IFormFile file)
    {
        var fileName = Path.GetFileName(file.FileName?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "Attached file name is invalid.";
        }

        var allowedExtensions = AllowedAttachmentExtensionsText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.StartsWith('.') ? x.ToLowerInvariant() : $".{x.ToLowerInvariant()}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var extension = Path.GetExtension(fileName) ?? string.Empty;
        if (!allowedExtensions.Contains(extension))
        {
            return $"Attached file extension is invalid. Allowed: {AllowedAttachmentExtensionsText}";
        }

        var maxBytes = MaxAttachmentSizeMb * 1024L * 1024L;
        if (file.Length > maxBytes)
        {
            return $"Attached file size must not exceed {MaxAttachmentSizeMb} MB.";
        }

        return null;
    }

    private void SaveAttachment(IFormFile file, List<string> savedFilePaths)
    {
        var uploadFolder = ResolveAttachmentFolder();
        Directory.CreateDirectory(uploadFolder);

        var savedFileName = Path.GetFileName(file.FileName?.Trim() ?? string.Empty);
        if (string.IsNullOrWhiteSpace(savedFileName))
        {
            throw new InvalidOperationException("Attached file name is invalid.");
        }

        var fullPath = Path.Combine(uploadFolder, savedFileName);
        if (System.IO.File.Exists(fullPath))
        {
            throw new InvalidOperationException($"Attachment file '{savedFileName}' already exists.");
        }

        using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            file.CopyTo(stream);
        }

        savedFilePaths.Add(fullPath);
    }

    private string ResolveAttachmentFolder()
    {
        var basePath = _config.GetValue<string>("FileUploads:BasePath");
        if (string.IsNullOrWhiteSpace(basePath))
        {
            throw new InvalidOperationException("FileUploads:BasePath is missing in appsettings.json.");
        }

        var rootPath = Path.IsPathRooted(basePath)
            ? basePath
            : Path.Combine(Directory.GetCurrentDirectory(), basePath);

        var configuredFunctionPath = _config.GetValue<string>($"FileUploads:Funtions:{FUNCTION_ID}");
        if (string.IsNullOrWhiteSpace(configuredFunctionPath))
        {
            throw new InvalidOperationException($"FileUploads:Funtions:{FUNCTION_ID} is missing in appsettings.json.");
        }

        var functionSegments = configuredFunctionPath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var folderSegments = new List<string> { rootPath };
        folderSegments.AddRange(functionSegments);
        folderSegments.Add(Header.PODate.Year.ToString(CultureInfo.InvariantCulture));
        folderSegments.Add(BuildAttachmentFolderName());

        return Path.Combine(folderSegments.ToArray());
    }

    private string BuildAttachmentFolderName()
    {
        var poNo = Header.PONo?.Trim() ?? string.Empty;
        return poNo.Replace("/", string.Empty, StringComparison.Ordinal);
    }

    private static string FormatFileSize(long sizeBytes)
    {
        if (sizeBytes <= 0)
        {
            return "0 B";
        }

        var units = new[] { "B", "KB", "MB", "GB", "TB" };
        var value = (double)sizeBytes;
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return unitIndex == 0
            ? $"{sizeBytes} {units[unitIndex]}"
            : $"{value:0.##} {units[unitIndex]}";
    }

    private static void RemoveSavedFiles(IEnumerable<string> savedFilePaths)
    {
        foreach (var path in savedFilePaths)
        {
            try
            {
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                }
            }
            catch
            {
                // No-op for cleanup.
            }
        }
    }

    private IActionResult RedirectToCurrentDetail(string mode = "view")
    {
        return RedirectToPage("./PurchaseOrderDetail", BuildDetailRouteValues(mode));
    }

    private RouteValueDictionary BuildDetailRouteValues(string mode, bool openConvertModal = false)
    {
        return new RouteValueDictionary
        {
            { "id", Header.Id },
            { "mode", mode },
            { "returnUrl", ReturnUrl },
            { "openConvertModal", openConvertModal }
        };
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

    private static string BuildReportPdfFileName(string poNo, string suffix)
    {
        var safeName = string.IsNullOrWhiteSpace(poNo) ? "purchase_order" : poNo.Trim();
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            safeName = safeName.Replace(invalidChar, '_');
        }

        return $"PurchaseOrder_No_{safeName}.pdf";
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
    [StringLength(100)]
    public string? Note { get; set; }
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
    public int DetailID { get; set; }
    public bool IsPersisted { get; set; }
    public int ItemID { get; set; }
    public long PrDetailId { get; set; }
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

public class PurchaseOrderAttachmentViewModel
{
    public string FileName { get; set; } = string.Empty;
    public DateTime? ModifiedDate { get; set; }
    public long SizeBytes { get; set; }
    public string SizeText { get; set; } = string.Empty;
}

public class PurchaseOrderEvaluateOptionViewModel
{
    public int POIndexDetailId { get; set; }
    public int EstimatePoint { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Point { get; set; }
    public string DisplayText => string.IsNullOrWhiteSpace(Name) ? string.Empty : $"{Name} ({Point} Points)";
}

public class PurchaseOrderNotifyRecipientViewModel
{
    public string Email { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}

public class PurchaseOrderWorkflowNotifyRequestViewModel
{
    public string SenderEmail { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MailServer { get; set; } = string.Empty;
    public int MailPort { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
    public List<PurchaseOrderNotifyRecipientViewModel> Recipients { get; set; } = new List<PurchaseOrderNotifyRecipientViewModel>();
}

public class LookupOptionDto
{
    public string? Value { get; set; }
    public string? Text { get; set; }
}

public class ApiResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class PrLinesResponse : ApiResponse
{
    public List<PurchaseOrderPrLineLookup> Data { get; set; } = new List<PurchaseOrderPrLineLookup>();
}
