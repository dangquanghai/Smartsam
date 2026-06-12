
using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Data.SqlClient;
using SmartSam.Helpers;
using SmartSam.Pages;
using SmartSam.Services;
using SmartSam.Services.Interfaces;

namespace SmartSam.Pages.Purchasing.PurchaseRequisition;

public class PurchaseRequisitionDetailModel : BasePageModel
{
    private static readonly HashSet<int> AllowedPageSizes = [10, 20, 50, 100, 200];
    // private const string NotifyCcEmail = "maiquangvinhi4@gmail.com";
    private const string NotifyCcEmail = "hai.dq@saigonskygarden.com.vn";
    private const int FUNCTION_ID = 72;
    private const int PermissionViewDetail = 2;
    private const int PermissionAdd = 3;
    private const int PermissionEdit = 4;
    private const int PermissionChangeStatus = 6;
    private const int PermissionUploadAttachment = 7;
    private readonly PermissionService _permissionService;
    private readonly ISecurityService _securityService;
    private readonly ILogger<PurchaseRequisitionDetailModel> _logger;
    private PurchaseRequisitionWorkflowUserInfo _workflowUser = new PurchaseRequisitionWorkflowUserInfo();

    // Kh?i t?o c�c service v� th�nh ph?n c?n d�ng cho m�n h�nh chi ti?t phi?u d? ngh? mua h�ng.
    public PurchaseRequisitionDetailModel(IConfiguration config, PermissionService permissionService, ISecurityService securityService, ILogger<PurchaseRequisitionDetailModel> logger) : base(config)
    {
        _permissionService = permissionService;
        _securityService = securityService;
        _logger = logger;
    }

    public PagePermissions PagePerm { get; private set; } = new PagePermissions();
    public bool CanSave { get; private set; }
    public bool CanApproveWorkflow { get; private set; }
    public bool CanDisapproveWorkflow { get; private set; }
    public bool CanResetToNew { get; private set; }
    public bool CanMoveToMr { get; private set; }
    public bool CanManageAttachments { get; private set; }
    public bool CanEditExistingDetailFields { get; private set; }
    public bool CanOpenViewDetailDialog => PagePerm.HasPermission(PermissionViewDetail);
    public bool IsViewMode => string.Equals(Mode, "view", StringComparison.OrdinalIgnoreCase);
    public bool IsStatusReadOnlyMode => Mode == "edit" && Requisition.Status != 1;
    public bool IsReadOnlyMode => IsViewMode || IsStatusReadOnlyMode;
    public decimal TotalAmount { get; private set; }
    public string AllowedAttachmentExtensionsText => _config.GetValue<string>("FileUploads:AllowedExtensions") ?? ".doc,.docx,.xls,.xlsx,.pdf,.jpg,.jpeg,.png";
    public int MaxAttachmentSizeMb => _config.GetValue<int?>("FileUploads:MaxFileSizeMb") ?? 10;

    [BindProperty(SupportsGet = true)]
    public string Mode { get; set; } = "add";

    [BindProperty]
    public PurchaseRequisitionHeader Requisition { get; set; } = new PurchaseRequisitionHeader();

    [BindProperty]
    public string DetailsJson { get; set; } = "[]";

    [BindProperty]
    public long SelectedDetailId { get; set; }

    [BindProperty]
    public List<IFormFile> AttachmentUploads { get; set; } = new List<IFormFile>();

    [BindProperty]
    public List<int> SelectedAttachmentDocIds { get; set; } = new List<int>();

    [TempData]
    public string? Message { get; set; }

    [TempData]
    public string MessageType { get; set; } = "info";

    public List<SelectListItem> StatusList { get; set; } = new List<SelectListItem>();
    public List<PurchaseRequisitionItemLookup> ItemList { get; set; } = new List<PurchaseRequisitionItemLookup>();
    public List<PurchaseRequisitionSupplierLookup> SupplierList { get; set; } = new List<PurchaseRequisitionSupplierLookup>();
    public List<PurchaseRequisitionAttachmentViewModel> AttachmentList { get; set; } = new List<PurchaseRequisitionAttachmentViewModel>();

    // T?i d? li?u ban d?u c?a m�n h�nh chi ti?t theo ch? d? add, edit ho?c view.
    public IActionResult OnGet(int? id, string mode = "view")
    {
        PagePerm = GetUserPermissions();
        Mode = string.IsNullOrWhiteSpace(mode) ? "view" : mode.Trim().ToLowerInvariant();
        if (Mode != "edit" && Mode != "view" && Mode != "add")
        {
            Mode = "view";
        }
        LoadAllDropdowns();
        LoadCurrentWorkflowUser();

        if (id.HasValue && id.Value > 0)
        {
            LoadPurchaseRequisition(id.Value);
            if (Requisition.Id <= 0)
            {
                return NotFound();
            }

            var effectivePermissions = GetEffectivePermissionsByStatus(Requisition.Status);
            if (!effectivePermissions.Contains(PermissionViewDetail))
            {
                TempData["Message"] = "You have no permission to access this requisition.";
                TempData["MessageType"] = "warning";
                return RedirectToPage("./Index");
            }

            if (Mode == "edit" && !CanOpenEditMode(effectivePermissions))
            {
                TempData["Message"] = "You have no permission to edit this requisition.";
                TempData["MessageType"] = "warning";
                return RedirectToPage("./Index");
            }

        }
        else
        {
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
                RequestNo = GetSuggestedRequestNo(),
                RequestDate = DateTime.Today,
                Currency = 1,
                Status = 1
            };
            DetailsJson = "[]";
            TotalAmount = 0;
        }

        SetActionFlags();
        return Page();
    }

    // T?i d? li?u popup View Detail b?ng ajax d? t�m ki?m v� ph�n trang m� kh�ng reload trang ch�nh.
    public IActionResult OnGetViewDetailRows([FromQuery] PurchaseRequisitionViewDetailFilterRequest request)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewDetail))
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return new JsonResult(new { message = "You have no permission to view purchase requisition details." });
        }

        request.PageNumber = request.PageNumber <= 0 ? 1 : request.PageNumber;
        request.PageSize = AllowedPageSizes.Contains(request.PageSize) ? request.PageSize : 10;

        var allowedStatuses = GetAllowedViewStatuses();
        if (allowedStatuses.Count == 0)
        {
            return new JsonResult(new PurchaseRequisitionViewDetailResponse());
        }

        var rows = new List<PurchaseRequisitionViewDetailRow>();
        var totalRecords = 0;

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        var whereBuilder = new List<string>
        {
            $"p.[Status] IN ({string.Join(",", allowedStatuses)})"
        };

        if (!string.IsNullOrWhiteSpace(request.RequestNo))
        {
            whereBuilder.Add("p.RequestNo LIKE '%' + @RequestNo + '%'");
        }

        if (!string.IsNullOrWhiteSpace(request.Description))
        {
            whereBuilder.Add("p.[Description] LIKE '%' + @Description + '%'");
        }

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
        {
            whereBuilder.Add("(i.ItemCode LIKE '%' + @ItemCode + '%' OR i.ItemName LIKE '%' + @ItemCode + '%')");
        }

        if (request.UseDateRange)
        {
            if (request.FromDate.HasValue)
            {
                whereBuilder.Add("CAST(p.RequestDate AS date) >= @FromDate");
            }

            if (request.ToDate.HasValue)
            {
                whereBuilder.Add("CAST(p.RequestDate AS date) <= @ToDate");
            }
        }

        if (request.RecQty.HasValue)
        {
            var recQtyCondition = request.RecQtyOperator switch
            {
                ">" => "ISNULL(d.RecQty, 0) > @RecQty",
                ">=" => "ISNULL(d.RecQty, 0) >= @RecQty",
                "<" => "ISNULL(d.RecQty, 0) < @RecQty",
                "<=" => "ISNULL(d.RecQty, 0) <= @RecQty",
                "=" => "ISNULL(d.RecQty, 0) = @RecQty",
                _ => string.Empty
            };

            if (!string.IsNullOrWhiteSpace(recQtyCondition))
            {
                whereBuilder.Add(recQtyCondition);
            }
        }

        var whereSql = whereBuilder.Count == 0
            ? string.Empty
            : $"WHERE {string.Join(" AND ", whereBuilder)}";

        using (var countCmd = new SqlCommand($@"
SELECT COUNT(1)
FROM dbo.PC_PR p
INNER JOIN dbo.PC_PRDetail d ON p.PRID = d.PRID
LEFT JOIN dbo.INV_ItemList i ON d.ItemID = i.ItemID
{whereSql}", conn))
        {
            BindViewDetailFilterParams(countCmd, request);
            totalRecords = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);
        }

        using (var dataCmd = new SqlCommand($@"
SELECT
    p.PRID,
    ISNULL(p.RequestNo, '') AS RequestNo,
    p.RequestDate,
    ISNULL(p.[Description], '') AS [Description],
    ISNULL(i.ItemCode, '') AS ItemCode,
    ISNULL(i.ItemName, '') AS ItemName,
    ISNULL(d.Quantity, 0) AS PrQty,
    ISNULL(d.RecQty, 0) AS RecQty
FROM dbo.PC_PR p
INNER JOIN dbo.PC_PRDetail d ON p.PRID = d.PRID
LEFT JOIN dbo.INV_ItemList i ON d.ItemID = i.ItemID
{whereSql}
ORDER BY p.RequestDate DESC, p.RequestNo DESC, d.RecordID DESC
OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY", conn))
        {
            BindViewDetailFilterParams(dataCmd, request);
            dataCmd.Parameters.Add("@Offset", SqlDbType.Int).Value = (request.PageNumber - 1) * request.PageSize;
            dataCmd.Parameters.Add("@PageSize", SqlDbType.Int).Value = request.PageSize;

            using var rd = dataCmd.ExecuteReader();
            while (rd.Read())
            {
                rows.Add(new PurchaseRequisitionViewDetailRow
                {
                    PrId = Convert.ToInt32(rd["PRID"]),
                    RequestNo = Convert.ToString(rd["RequestNo"]) ?? string.Empty,
                    RequestDate = rd["RequestDate"] == DBNull.Value ? null : Convert.ToDateTime(rd["RequestDate"]),
                    RequestDateText = rd["RequestDate"] == DBNull.Value ? string.Empty : Convert.ToDateTime(rd["RequestDate"]).ToString("dd-MMM-yyyy"),
                    Description = Convert.ToString(rd["Description"]) ?? string.Empty,
                    ItemCode = Convert.ToString(rd["ItemCode"]) ?? string.Empty,
                    ItemName = Convert.ToString(rd["ItemName"]) ?? string.Empty,
                    PrQty = Convert.ToDecimal(rd["PrQty"]),
                    RecQty = Convert.ToDecimal(rd["RecQty"])
                });
            }
        }

        var totalPages = request.PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(totalRecords / (double)request.PageSize));
        if (request.PageNumber > totalPages)
        {
            request.PageNumber = totalPages;
        }

        return new JsonResult(new PurchaseRequisitionViewDetailResponse
        {
            Rows = rows,
            PageNumber = request.PageNumber,
            PageSize = request.PageSize,
            TotalRecords = totalRecords,
            TotalPages = totalPages
        });
    }

    public IActionResult OnGetReport(int id)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewDetail))
        {
            return RedirectToPage("./Index");
        }

        if (id <= 0)
        {
            return RedirectToPage("./Index");
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand("SELECT TOP 1 ISNULL(RequestNo, '') FROM dbo.PC_PR WHERE PRID = @PRID", conn);
        cmd.Parameters.Add("@PRID", SqlDbType.Int).Value = id;
        conn.Open();
        var requestNo = Convert.ToString(cmd.ExecuteScalar())?.Trim();
        if (string.IsNullOrWhiteSpace(requestNo))
        {
            TempData["Message"] = "Purchase requisition is not found.";
            TempData["MessageType"] = "warning";
            return RedirectToPage("./Index");
        }

        return RedirectToPage("./Index", "ViewDetailReport", new { RequestNo = requestNo });
    }

    // Luu d? li?u header v� detail c?a phi?u d? ngh? mua h�ng.
    public IActionResult OnPost()
    {
        PagePerm = GetUserPermissions();
        LoadAllDropdowns();
        LoadCurrentWorkflowUser();

        var isNew = Requisition.Id <= 0;
        Mode = isNew ? "add" : (string.IsNullOrWhiteSpace(Mode) ? "edit" : Mode.Trim().ToLowerInvariant());

        if (!isNew)
        {
            var requestNo = Requisition.RequestNo;
            var requestDate = Requisition.RequestDate;
            var description = Requisition.Description;
            var currency = Requisition.Currency;
            LoadExistingWorkflowData(Requisition.Id);
            Requisition.RequestNo = requestNo;
            Requisition.RequestDate = requestDate;
            Requisition.Description = description;
            Requisition.Currency = currency;
        }
        else
        {
            // Status dropdown is disabled in UI, so keep the initial "New" status on add.
            Requisition.Status = 1;
        }

        var effectivePermissions = isNew
            ? GetEffectivePermissionsByStatus(1)
            : GetEffectivePermissionsByStatus(Requisition.Status);
        var canSaveExistingDetailFields = !isNew
            && string.Equals(Mode, "edit", StringComparison.OrdinalIgnoreCase)
            && _workflowUser.IsPurchaser;
        if (isNew && !effectivePermissions.Contains(PermissionAdd) || !isNew && !effectivePermissions.Contains(PermissionEdit) && !canSaveExistingDetailFields)
        {
            ModelState.AddModelError(string.Empty, "You have no permission to save requisition.");
            SetActionFlags();
            return Page();
        }

        var details = ParseDetails();
        TotalAmount = details.Sum(x => x.Amount);

        if (string.IsNullOrWhiteSpace(Requisition.RequestNo))
        {
            ModelState.AddModelError("Requisition.RequestNo", "No. is required.");
        }
        if (string.IsNullOrWhiteSpace(Requisition.Description))
        {
            ModelState.AddModelError("Requisition.Description", "Description is required.");
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
            AttachmentList = Requisition.Id > 0 ? LoadAttachmentRows(Requisition.Id) : new List<PurchaseRequisitionAttachmentViewModel>();
            Message = string.Join(" ", ModelState.Values
                .SelectMany(x => x.Errors)
                .Select(x => x.ErrorMessage)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct());
            MessageType = "error";
            SetActionFlags();
            return Page();
        }

        try
        {
            SavePurchaseRequisition(details);
            TempData["Message"] = "Purchase requisition saved successfully.";
            TempData["MessageType"] = "success";
            return RedirectToPage("./PurchaseRequisitionDetail", new { id = Requisition.Id, mode = "edit" });
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            Message = ex.Message;
            MessageType = "error";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot save purchase requisition.");
            ModelState.AddModelError(string.Empty, $"Cannot save purchase requisition. {ex.Message}");
            Message = $"Cannot save purchase requisition. {ex.Message}";
            MessageType = "error";
        }

        AttachmentList = Requisition.Id > 0 ? LoadAttachmentRows(Requisition.Id) : new List<PurchaseRequisitionAttachmentViewModel>();
        SetActionFlags();
        return Page();
    }

    // Chuy?n m?t d�ng item t? PR tr? l?i MR theo d�ng li�n k?t MRDetailID c?a d�ng chi ti?t.
    public IActionResult OnPostToMr()
    {
        var prepareResult = PrepareExistingRecordForPost();
        if (prepareResult != null)
        {
            return prepareResult;
        }

        if (!CanMoveToMr)
        {
            TempData["Message"] = "You have no permission to move item back to MR.";
            TempData["MessageType"] = "warning";
            return RedirectToCurrentDetail();
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        using (var countCmd = new SqlCommand(@"
SELECT COUNT(1)
FROM dbo.PC_PRDetail
WHERE PRID = @PRID", conn))
        {
            countCmd.Parameters.Add("@PRID", SqlDbType.Int).Value = Requisition.Id;
            var detailCount = Convert.ToInt32(countCmd.ExecuteScalar() ?? 0);
            if (detailCount <= 1)
            {
                TempData["Message"] = "Cannot move item back to MR because purchase requisition must contain at least 1 item after To MR.";
                TempData["MessageType"] = "error";
                return RedirectToCurrentDetail("edit");
            }
        }

        if (SelectedDetailId <= 0)
        {
            TempData["Message"] = "Please select one detail row to move back to MR.";
            TempData["MessageType"] = "warning";
            return RedirectToCurrentDetail();
        }
        using var trans = conn.BeginTransaction();

        try
        {
            int itemId;
            int mrDetailId;
            decimal quantity;
            decimal sugQty;

            using (var loadCmd = new SqlCommand(@"
SELECT ItemID,
       MRDetailID,
       ISNULL(Quantity, 0) AS Quantity,
       ISNULL(SugQty, 0) AS SugQty
FROM dbo.PC_PRDetail
WHERE PRID = @PRID
  AND RecordID = @RecordID", conn, trans))
            {
                loadCmd.Parameters.Add("@PRID", SqlDbType.Int).Value = Requisition.Id;
                loadCmd.Parameters.Add("@RecordID", SqlDbType.BigInt).Value = SelectedDetailId;

                using var rd = loadCmd.ExecuteReader();
                if (!rd.Read())
                {
                    throw new InvalidOperationException("Detail row not found.");
                }

                itemId = Convert.ToInt32(rd["ItemID"]);
                if (rd.IsDBNull(rd.GetOrdinal("MRDetailID")))
                {
                    throw new InvalidOperationException("Selected detail row has no MR link.");
                }

                mrDetailId = Convert.ToInt32(rd["MRDetailID"]);
                quantity = Convert.ToDecimal(rd["Quantity"]);
                sugQty = Convert.ToDecimal(rd["SugQty"]);
            }

            using (var updateCmd = new SqlCommand(quantity < sugQty
                ? @"
UPDATE dbo.MATERIAL_REQUEST_DETAIL
SET BUY = ISNULL(BUY, 0) + @Quantity,
    PostedPR = 0
WHERE ID = @MRDetailID"
                : @"
UPDATE dbo.MATERIAL_REQUEST_DETAIL
SET PostedPR = 0
WHERE ID = @MRDetailID", conn, trans))
            {
                updateCmd.Parameters.Add("@MRDetailID", SqlDbType.Int).Value = mrDetailId;
                if (quantity < sugQty)
                {
                    updateCmd.Parameters.Add("@Quantity", SqlDbType.Decimal).Value = quantity;
                }
                updateCmd.ExecuteNonQuery();
            }

            using (var deleteCmd = new SqlCommand(@"
DELETE FROM dbo.PC_PRDetail
WHERE PRID = @PRID
  AND ItemID = @ItemID
  AND MRDetailID = @MRDetailID", conn, trans))
            {
                deleteCmd.Parameters.Add("@PRID", SqlDbType.Int).Value = Requisition.Id;
                deleteCmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = itemId;
                deleteCmd.Parameters.Add("@MRDetailID", SqlDbType.Int).Value = mrDetailId;
                deleteCmd.ExecuteNonQuery();
            }

            UpdateDescriptionFromMrRequestNos(conn, trans, Requisition.Id);

            trans.Commit();
            TempData["Message"] = "Selected item has been moved back to MR.";
            TempData["MessageType"] = "success";
        }
        catch (Exception ex)
        {
            trans.Rollback();
            TempData["Message"] = ex.Message;
            TempData["MessageType"] = "error";
        }

        return RedirectToPage("./PurchaseRequisitionDetail", new { id = Requisition.Id, mode = "edit" });
    }

    // �ua ch?ng t? v? tr?ng th�i New d? b?t d?u l?i quy tr�nh duy?t.
    public IActionResult OnPostCstNew()
    {
        var prepareResult = PrepareExistingRecordForPost();
        if (prepareResult != null)
        {
            return prepareResult;
        }

        if (!CanResetToNew)
        {
            TempData["Message"] = "You have no permission to reset this requisition to New.";
            TempData["MessageType"] = "warning";
            return RedirectToCurrentDetail();
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
UPDATE dbo.PC_PR
SET [Status] = 1,
    CAId = NULL,
    CAApproDate = NULL,
    GDId = NULL,
    GDApproDate = NULL,
    edited = 1
WHERE PRID = @PRID", conn);

        cmd.Parameters.Add("@PRID", SqlDbType.Int).Value = Requisition.Id;
        conn.Open();
        cmd.ExecuteNonQuery();

        TempData["Message"] = "Purchase requisition has been reset to New.";
        TempData["MessageType"] = "success";
        return RedirectToPage("./PurchaseRequisitionDetail", new { id = Requisition.Id, mode = "edit" });
    }

    // Duy?t ch?ng t? theo d�ng vai tr� PU, CFO ho?c BOD ? bu?c workflow hi?n t?i.
    public IActionResult OnPostApprove()
    {
        var prepareResult = PrepareExistingRecordForPost();
        if (prepareResult != null)
        {
            return prepareResult;
        }

        if (!CanApproveWorkflow)
        {
            TempData["Message"] = "You have no permission to approve this requisition.";
            TempData["MessageType"] = "warning";
            return RedirectToCurrentDetail();
        }

        string? notifySubject = null;
        string? notifyBody = null;
        List<PurchaseRequisitionNotifyRecipientViewModel> recipients = new List<PurchaseRequisitionNotifyRecipientViewModel>();
        var approveDate = DateTime.Now.ToString("dd/MM/yyyy");

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var trans = conn.BeginTransaction();

        try
        {
            if (CanApproveAsPurchaser())
            {
                using var cmd = new SqlCommand(@"
UPDATE dbo.PC_PR
SET [Status] = 2,
    PurId = @EmployeeID,
    PurApproDate = @ApproveDate,
    edited = 1
WHERE PRID = @PRID
  AND ISNULL([Status], 1) = 1", conn, trans);

                cmd.Parameters.Add("@PRID", SqlDbType.Int).Value = Requisition.Id;
                cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = _workflowUser.EmployeeId;
                cmd.Parameters.Add("@ApproveDate", SqlDbType.VarChar, 12).Value = approveDate;
                if (cmd.ExecuteNonQuery() <= 0)
                {
                    throw new InvalidOperationException("Approve failed because status changed.");
                }

                recipients = GetEmailsByWorkflowRole(conn, trans, "CFO");
                notifySubject = "[Purchase Requisition] Waiting for CFO approve";
                notifyBody = BuildWorkflowNotifyBody(conn, trans, "PURCHASE REQUISITION", "#007bff", "waiting for your approval.");
            }
            else if (CanApproveAsCfo())
            {
                using var cmd = new SqlCommand(@"
UPDATE dbo.PC_PR
SET CAId = @EmployeeID,
    CAApproDate = @ApproveDate,
    edited = 1
WHERE PRID = @PRID
  AND ISNULL([Status], 0) = 2
  AND CAId IS NULL", conn, trans);

                cmd.Parameters.Add("@PRID", SqlDbType.Int).Value = Requisition.Id;
                cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = _workflowUser.EmployeeId;
                cmd.Parameters.Add("@ApproveDate", SqlDbType.VarChar, 12).Value = approveDate;
                if (cmd.ExecuteNonQuery() <= 0)
                {
                    throw new InvalidOperationException("Approve failed because CFO step has been processed.");
                }

                recipients = GetEmailsByWorkflowRole(conn, trans, "BOD");
                notifySubject = "[Purchase Requisition] Waiting for BOD approve";
                notifyBody = BuildWorkflowNotifyBody(conn, trans, "PURCHASE REQUISITION", "#17a2b8", "waiting for your approval.");
            }
            else if (CanApproveAsBod())
            {
                using var cmd = new SqlCommand(@"
UPDATE dbo.PC_PR
SET GDId = @EmployeeID,
    GDApproDate = @ApproveDate,
    [Status] = 4,
    edited = 1
WHERE PRID = @PRID
  AND ISNULL([Status], 0) = 2
  AND CAId IS NOT NULL
  AND GDId IS NULL", conn, trans);

                cmd.Parameters.Add("@PRID", SqlDbType.Int).Value = Requisition.Id;
                cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = _workflowUser.EmployeeId;
                cmd.Parameters.Add("@ApproveDate", SqlDbType.VarChar, 12).Value = approveDate;
                if (cmd.ExecuteNonQuery() <= 0)
                {
                    throw new InvalidOperationException("Approve failed because BOD step has been processed.");
                }

                recipients = GetPurchaserEmails(conn, trans);
                notifySubject = "[Purchase Requisition] Approved by BOD";
                notifyBody = BuildWorkflowResultNotifyBody(conn, trans, "#28a745", "Approved");
            }
            else
            {
                throw new InvalidOperationException("Current user cannot approve at this step.");
            }

            trans.Commit();
        }
        catch (Exception ex)
        {
            trans.Rollback();
            TempData["Message"] = ex.Message;
            TempData["MessageType"] = "error";
            return RedirectToCurrentDetail();
        }

        if (recipients.Count > 0 && !string.IsNullOrWhiteSpace(notifySubject) && !string.IsNullOrWhiteSpace(notifyBody))
        {
            TryQueueWorkflowNotifyEmail(recipients, notifySubject, notifyBody);
        }

        TempData["Message"] = recipients.Count == 0 && !string.IsNullOrWhiteSpace(notifySubject)
            ? "Purchase requisition approved successfully, but recipient email was not found."
            : "Purchase requisition approved successfully.";
        TempData["MessageType"] = "success";
        return RedirectToPage("./Index");
    }

    // T? ch?i duy?t ch?ng t? v� chuy?n tr?ng th�i sang Pending theo d�ng bu?c dang x? l�.
    public IActionResult OnPostDisapprove()
    {
        var prepareResult = PrepareExistingRecordForPost();
        if (prepareResult != null)
        {
            return prepareResult;
        }

        if (!CanDisapproveWorkflow)
        {
            TempData["Message"] = "You have no permission to disapprove this requisition.";
            TempData["MessageType"] = "warning";
            return RedirectToCurrentDetail();
        }

        var approveDate = DateTime.Now.ToString("dd/MM/yyyy");
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        List<PurchaseRequisitionNotifyRecipientViewModel> recipients = new List<PurchaseRequisitionNotifyRecipientViewModel>();
        string? notifySubject = null;
        string? notifyBody = null;

        if (CanApproveAsCfo())
        {
            using var cmd = new SqlCommand(@"
UPDATE dbo.PC_PR
SET [Status] = 3,
    CAId = @EmployeeID,
    CAApproDate = @ApproveDate,
    edited = 1
WHERE PRID = @PRID
  AND ISNULL([Status], 0) = 2
  AND CAId IS NULL", conn);

            cmd.Parameters.Add("@PRID", SqlDbType.Int).Value = Requisition.Id;
            cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = _workflowUser.EmployeeId;
            cmd.Parameters.Add("@ApproveDate", SqlDbType.VarChar, 12).Value = approveDate;
            cmd.ExecuteNonQuery();

            recipients = GetPurchaserEmails(conn, null);
            notifySubject = "[Purchase Requisition] Disapproved by CFO";
            notifyBody = BuildWorkflowResultNotifyBody(conn, null, "#dc3545", "Pending");
        }
        else if (CanApproveAsBod())
        {
            using var cmd = new SqlCommand(@"
UPDATE dbo.PC_PR
SET [Status] = 3,
    GDId = @EmployeeID,
    GDApproDate = @ApproveDate,
    edited = 1
WHERE PRID = @PRID
  AND ISNULL([Status], 0) = 2
  AND CAId IS NOT NULL
  AND GDId IS NULL", conn);

            cmd.Parameters.Add("@PRID", SqlDbType.Int).Value = Requisition.Id;
            cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = _workflowUser.EmployeeId;
            cmd.Parameters.Add("@ApproveDate", SqlDbType.VarChar, 12).Value = approveDate;
            cmd.ExecuteNonQuery();

            recipients = GetPurchaserEmails(conn, null);
            notifySubject = "[Purchase Requisition] Disapproved by BOD";
            notifyBody = BuildWorkflowResultNotifyBody(conn, null, "#dc3545", "Pending");
        }

        if (recipients.Count > 0 && !string.IsNullOrWhiteSpace(notifySubject) && !string.IsNullOrWhiteSpace(notifyBody))
        {
            TryQueueWorkflowNotifyEmail(recipients, notifySubject, notifyBody);
        }

        TempData["Message"] = recipients.Count == 0 && !string.IsNullOrWhiteSpace(notifySubject)
            ? "Purchase requisition has been set to Pending successfully, but recipient email was not found."
            : "Purchase requisition has been set to Pending.";
        TempData["MessageType"] = "success";
        return RedirectToPage("./Index");
    }

    // Upload file d�nh k�m cho phi?u PR v� ghi t�n file v�o b?ng PC_PR_Doc.
    public IActionResult OnPostUploadAttachment()
    {
        var prepareResult = PrepareExistingRecordForPost();
        if (prepareResult != null)
        {
            return prepareResult;
        }

        if (!CanManageAttachments)
        {
            TempData["Message"] = "You have no permission to upload attachment.";
            TempData["MessageType"] = "warning";
            return RedirectToCurrentDetail("edit");
        }

        if (AttachmentUploads == null || AttachmentUploads.Count == 0)
        {
            TempData["Message"] = "Please select at least one file to upload.";
            TempData["MessageType"] = "warning";
            return RedirectToCurrentDetail("edit");
        }

        var validationMessages = AttachmentUploads
            .Where(file => file != null && file.Length > 0)
            .Select(file => ValidateAttachment(file))
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToList();
        if (validationMessages.Count > 0)
        {
            TempData["Message"] = string.Join(" ", validationMessages);
            TempData["MessageType"] = "error";
            return RedirectToCurrentDetail("edit");
        }

        var savedFilePaths = new List<string>();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var trans = conn.BeginTransaction();

        try
        {
            foreach (var attachment in AttachmentUploads.Where(file => file != null && file.Length > 0))
            {
                SaveAttachment(conn, trans, Requisition.Id, _workflowUser.EmployeeId, attachment, savedFilePaths);
            }
            trans.Commit();
            TempData["Message"] = "Attachments uploaded successfully.";
            TempData["MessageType"] = "success";
        }
        catch (Exception ex)
        {
            trans.Rollback();
            RemoveSavedFiles(savedFilePaths);
            TempData["Message"] = ex.Message;
            TempData["MessageType"] = "error";
        }

        return RedirectToCurrentDetail();
    }

    // X�a c�c file d�nh k�m d� ch?n c?a PR kh?i c? b?ng PC_PR_Doc v� thu m?c luu file v?t l�.
    public IActionResult OnPostDeleteAttachments()
    {
        var prepareResult = PrepareExistingRecordForPost();
        if (prepareResult != null)
        {
            return prepareResult;
        }

        if (!CanManageAttachments)
        {
            TempData["Message"] = "You have no permission to delete attachment.";
            TempData["MessageType"] = "warning";
            return RedirectToCurrentDetail();
        }

        if (SelectedAttachmentDocIds == null || SelectedAttachmentDocIds.Count == 0)
        {
            TempData["Message"] = "Please select at least one attachment to delete.";
            TempData["MessageType"] = "warning";
            return RedirectToCurrentDetail();
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var trans = conn.BeginTransaction();
        var deletedFileNames = new List<string>();

        try
        {
            foreach (var docId in SelectedAttachmentDocIds.Distinct())
            {
                string? fileName = null;

                using (var loadCmd = new SqlCommand(@"
SELECT FilePath
FROM dbo.PC_PR_Doc
WHERE DocID = @DocID
  AND PRID = @PRID", conn, trans))
                {
                    loadCmd.Parameters.Add("@DocID", SqlDbType.Int).Value = docId;
                    loadCmd.Parameters.Add("@PRID", SqlDbType.Int).Value = Requisition.Id;
                    fileName = Convert.ToString(loadCmd.ExecuteScalar());
                }

                if (string.IsNullOrWhiteSpace(fileName))
                {
                    continue;
                }

                using (var deleteCmd = new SqlCommand(@"
DELETE FROM dbo.PC_PR_Doc
WHERE DocID = @DocID
  AND PRID = @PRID", conn, trans))
                {
                    deleteCmd.Parameters.Add("@DocID", SqlDbType.Int).Value = docId;
                    deleteCmd.Parameters.Add("@PRID", SqlDbType.Int).Value = Requisition.Id;
                    deleteCmd.ExecuteNonQuery();
                }

                deletedFileNames.Add(fileName);
            }

            trans.Commit();

            foreach (var fileName in deletedFileNames)
            {
                var fullPath = Path.Combine(ResolveUploadFolder(), fileName);
                if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                }
            }

            TempData["Message"] = deletedFileNames.Count > 0
                ? "Selected attachments deleted successfully."
                : "No attachment was deleted.";
            TempData["MessageType"] = "success";
        }
        catch (Exception ex)
        {
            trans.Rollback();
            TempData["Message"] = ex.Message;
            TempData["MessageType"] = "error";
        }

        return RedirectToCurrentDetail();
    }

    // T?i file d�nh k�m d� luu c?a PR theo DocID.
    public IActionResult OnGetDownloadAttachment(int docId, int id)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.AllowedNos.Contains(PermissionViewDetail))
        {
            return RedirectToPage("./Index");
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
SELECT FilePath
FROM dbo.PC_PR_Doc
WHERE DocID = @DocID
  AND PRID = @PRID", conn);

        cmd.Parameters.Add("@DocID", SqlDbType.Int).Value = docId;
        cmd.Parameters.Add("@PRID", SqlDbType.Int).Value = id;
        conn.Open();

        var fileName = Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return NotFound();
        }

        var fullPath = Path.Combine(ResolveUploadFolder(), fileName);
        if (!System.IO.File.Exists(fullPath))
        {
            return NotFound();
        }

        var provider = new FileExtensionContentTypeProvider();
        if (!provider.TryGetContentType(fullPath, out var contentType))
        {
            contentType = "application/octet-stream";
        }

        return File(System.IO.File.ReadAllBytes(fullPath), contentType, fileName);
    }

    // N?p to�n b? dropdown d�ng chung cho form header, detail v� file d�nh k�m.
    private void LoadAllDropdowns()
    {
        LoadStatusList();
        LoadItemList();
        LoadSupplierList();
    }

    // N?p d?y d? d? li?u header, detail, file d�nh k�m v� t?ng ti?n c?a m?t PR hi?n c�.
    private void LoadPurchaseRequisition(int id)
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        using (var cmd = new SqlCommand(@"
SELECT PRID,
       ISNULL(RequestNo, '') AS RequestNo,
       RequestDate,
       ISNULL([Description], '') AS [Description],
       ISNULL(Currency, 1) AS Currency,
       ISNULL([Status], 1) AS [Status],
       PurId,
       ISNULL(PurApproDate, '') AS PurApproDate,
       CAId,
       ISNULL(CAApproDate, '') AS CAApproDate,
       GDId,
       ISNULL(GDApproDate, '') AS GDApproDate
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
                    Description = Convert.ToString(rd["Description"]),
                    Currency = Convert.ToInt32(rd["Currency"]),
                    Status = Convert.ToByte(rd["Status"]),
                    PurId = rd.IsDBNull(rd.GetOrdinal("PurId")) ? null : Convert.ToInt32(rd["PurId"]),
                    PurApproveDate = Convert.ToString(rd["PurApproDate"]) ?? string.Empty,
                    CAId = rd.IsDBNull(rd.GetOrdinal("CAId")) ? null : Convert.ToInt32(rd["CAId"]),
                    CAApproveDate = Convert.ToString(rd["CAApproDate"]) ?? string.Empty,
                    GDId = rd.IsDBNull(rd.GetOrdinal("GDId")) ? null : Convert.ToInt32(rd["GDId"]),
                    GDApproveDate = Convert.ToString(rd["GDApproDate"]) ?? string.Empty
                };
            }
        }

        if (Requisition.Id > 0)
        {
            var details = LoadDetailRows(conn, Requisition.Id);
            DetailsJson = JsonSerializer.Serialize(details);
            TotalAmount = details.Sum(x => x.Amount);
            AttachmentList = LoadAttachmentRows(Requisition.Id);
        }
    }

    // N?p d? li?u workflow hi?n t?i c?a ch?ng t? d? d�ng cho c�c handler post.
    private void LoadExistingWorkflowData(int prId)
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
SELECT PRID,
       ISNULL([Status], 1) AS [Status],
       PurId,
       ISNULL(PurApproDate, '') AS PurApproDate,
       CAId,
       ISNULL(CAApproDate, '') AS CAApproDate,
       GDId,
       ISNULL(GDApproDate, '') AS GDApproDate,
       ISNULL(RequestNo, '') AS RequestNo,
       ISNULL([Description], '') AS [Description],
       RequestDate,
       ISNULL(Currency, 1) AS Currency
FROM dbo.PC_PR
WHERE PRID = @PRID", conn);

        cmd.Parameters.Add("@PRID", SqlDbType.Int).Value = prId;
        conn.Open();
        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
        {
            throw new InvalidOperationException("Purchase requisition not found.");
        }

        Requisition = new PurchaseRequisitionHeader
        {
            Id = Convert.ToInt32(rd["PRID"]),
            Status = Convert.ToByte(rd["Status"]),
            PurId = rd.IsDBNull(rd.GetOrdinal("PurId")) ? null : Convert.ToInt32(rd["PurId"]),
            PurApproveDate = Convert.ToString(rd["PurApproDate"]) ?? string.Empty,
            CAId = rd.IsDBNull(rd.GetOrdinal("CAId")) ? null : Convert.ToInt32(rd["CAId"]),
            CAApproveDate = Convert.ToString(rd["CAApproDate"]) ?? string.Empty,
            GDId = rd.IsDBNull(rd.GetOrdinal("GDId")) ? null : Convert.ToInt32(rd["GDId"]),
            GDApproveDate = Convert.ToString(rd["GDApproDate"]) ?? string.Empty,
            RequestNo = Convert.ToString(rd["RequestNo"]) ?? string.Empty,
            Description = Convert.ToString(rd["Description"]),
            RequestDate = rd.IsDBNull(rd.GetOrdinal("RequestDate")) ? DateTime.Today : Convert.ToDateTime(rd["RequestDate"]),
            Currency = Convert.ToInt32(rd["Currency"])
        };
    }

    // N?p to�n b? d�ng item chi ti?t, bao g?m c? li�n k?t ngu?c v? MR d? ph?c v? thao t�c To MR.
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
       ISNULL(d.OrdAmount, ISNULL(d.Quantity, 0) * ISNULL(d.UnitPrice, 0)) AS Amount,
       ISNULL(d.Remark, '') AS Remark,
       d.SupplierID,
       CASE WHEN s.SupplierID IS NULL THEN '' ELSE ISNULL(s.SupplierCode, '') + ' - ' + ISNULL(s.SupplierName, '') END AS SupplierText,
       ISNULL(d.MRRequestNO, '') AS MRRequestNO,
       d.MRDetailID
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
                Amount = Convert.ToDecimal(rd["Amount"]),
                Remark = Convert.ToString(rd["Remark"]),
                SupplierId = rd.IsDBNull(rd.GetOrdinal("SupplierID")) ? null : Convert.ToInt32(rd["SupplierID"]),
                SupplierText = Convert.ToString(rd["SupplierText"]) ?? string.Empty,
                MrRequestNo = Convert.ToString(rd["MRRequestNO"]) ?? string.Empty,
                MrDetailId = rd.IsDBNull(rd.GetOrdinal("MRDetailID")) ? null : Convert.ToInt32(rd["MRDetailID"])
            });
        }

        return rows;
    }

    // N?p danh s�ch file d�nh k�m hi?n c� c?a PR d? hi?n th? trong modal Attached Files.
    private List<PurchaseRequisitionAttachmentViewModel> LoadAttachmentRows(int prId)
    {
        var rows = new List<PurchaseRequisitionAttachmentViewModel>();

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
SELECT d.DocID,
       d.PRID,
       ISNULL(d.FilePath, '') AS FilePath,
       d.UploadDate,
       d.UserID,
       ISNULL(e.EmployeeCode, '') AS EmployeeCode
FROM dbo.PC_PR_Doc d
LEFT JOIN dbo.MS_Employee e ON d.UserID = e.EmployeeID
WHERE d.PRID = @PRID
ORDER BY d.UploadDate DESC, d.DocID DESC", conn);

        cmd.Parameters.Add("@PRID", SqlDbType.Int).Value = prId;
        conn.Open();

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(new PurchaseRequisitionAttachmentViewModel
            {
                DocId = Convert.ToInt32(rd["DocID"]),
                PrId = Convert.ToInt32(rd["PRID"]),
                FilePath = Convert.ToString(rd["FilePath"]) ?? string.Empty,
                UploadDate = rd.IsDBNull(rd.GetOrdinal("UploadDate")) ? null : Convert.ToDateTime(rd["UploadDate"]),
                UserCode = Convert.ToString(rd["EmployeeCode"]) ?? string.Empty
            });
        }

        return rows;
    }

    // N?p danh s�ch tr?ng th�i PR t? b?ng danh m?c tr?ng th�i.
    private void LoadStatusList()
    {
        StatusList = LoadListFromSql(
            "SELECT PRStatusID, PRStatusName FROM dbo.PC_PRStatus ORDER BY PRStatusID",
            "PRStatusID",
            "PRStatusName");
    }

    // N?p danh s�ch d�ng MR d? di?u ki?n d? modal Add Detail d�ng c�ng ngu?n d? li?u v?i Add AT.
    private void LoadItemList()
    {
        ItemList = new List<PurchaseRequisitionItemLookup>();
        const string sql = @"
SELECT
       CONVERT(varchar(50), d.REQUEST_NO) AS RequestNo,
       i.ItemID,
       ISNULL(i.ItemCode, '') AS ItemCode,
       ISNULL(i.ItemName, '') AS ItemName,
       ISNULL(i.Unit, '') AS Unit,
       ISNULL(d.BUY, 0) AS BUY,
       ISNULL(i.UnitPrice, 0) AS UnitPrice,
       ISNULL(i.Specification, '') AS Specification,
       ISNULL(d.NOTE, '') AS Note,
       d.ID AS MRDetailID
FROM dbo.MATERIAL_REQUEST m
INNER JOIN dbo.MATERIAL_REQUEST_DETAIL d ON m.REQUEST_NO = d.REQUEST_NO
INNER JOIN dbo.INV_ItemList i ON d.ITEMCODE = i.ItemCode
WHERE ISNULL(d.BUY, 0) > 0
  AND (d.PostedPR = 0 OR d.PostedPR IS NULL)
  AND
  (
      (m.MATERIALSTATUSID = 3 AND ISNULL(d.BUY, 0) > 0)
      OR
      (m.MATERIALSTATUSID = 2 AND ISNULL(m.IS_AUTO, 0) = 1)
  )
ORDER BY d.REQUEST_NO, i.ItemCode";

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(sql, conn);
        conn.Open();

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            ItemList.Add(new PurchaseRequisitionItemLookup
            {
                Id = Convert.ToInt32(rd["ItemID"]),
                RequestNo = Convert.ToString(rd["RequestNo"]) ?? string.Empty,
                ItemCode = Convert.ToString(rd["ItemCode"]) ?? string.Empty,
                ItemName = Convert.ToString(rd["ItemName"]) ?? string.Empty,
                Unit = Convert.ToString(rd["Unit"]) ?? string.Empty,
                Buy = Convert.ToDecimal(rd["BUY"]),
                UnitPrice = Convert.ToDecimal(rd["UnitPrice"]),
                Specification = Convert.ToString(rd["Specification"]) ?? string.Empty,
                Note = Convert.ToString(rd["Note"]) ?? string.Empty,
                MrDetailId = Convert.ToInt32(rd["MRDetailID"])
            });
        }
    }

    // N?p danh s�ch supplier c�n hi?u l?c d? ch?n cho t?ng item chi ti?t n?u c?n.
    private void LoadSupplierList()
    {
        SupplierList = new List<PurchaseRequisitionSupplierLookup>();
        const string sql = @"
SELECT TOP 200 SupplierID, ISNULL(SupplierCode, '') AS SupplierCode, ISNULL(SupplierName, '') AS SupplierName
FROM dbo.PC_Suppliers
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

    // Sinh s? PR m?i theo stored procedure HaiAutoNumPR d? d?ng nh?t v?i h? th?ng cu.
    private string GetSuggestedRequestNo()
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        return GetSuggestedRequestNo(conn, null);
    }

    // Sinh s? PR m?i trong transaction hi?n t?i d? tr�nh l?ch s? khi nhi?u ngu?i thao t�c c�ng l�c.
    private static string GetSuggestedRequestNo(SqlConnection conn, SqlTransaction? trans)
    {
        using var cmd = new SqlCommand("EXEC dbo.HaiAutoNumPR NULL", conn, trans);
        var result = Convert.ToString(cmd.ExecuteScalar());
        return string.IsNullOrWhiteSpace(result) ? $"PR{DateTime.Now:ddMMyy}" : result.Trim();
    }

    // Luu header v� to�n b? detail hi?n t?i c?a PR trong c�ng m?t transaction.
    private void SavePurchaseRequisition(IReadOnlyList<PurchaseRequisitionDetailInput> details)
    {
        var operatorCode = User.Identity?.Name?.Trim() ?? string.Empty;
        var isNew = Requisition.Id <= 0;
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var trans = conn.BeginTransaction();

        try
        {
            string requestNo;
            if (isNew)
            {
                requestNo = string.IsNullOrWhiteSpace(Requisition.RequestNo)
                    ? GetSuggestedRequestNo(conn, trans)
                    : Requisition.RequestNo.Trim();
                Requisition.RequestNo = requestNo;
            }
            else
            {
                requestNo = Requisition.RequestNo?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(requestNo))
                {
                    throw new InvalidOperationException("Request No. is not found.");
                }

                Requisition.RequestNo = requestNo;
            }

            using (var checkCmd = new SqlCommand(@"
SELECT COUNT(1)
FROM dbo.PC_PR
WHERE RequestNo = @RequestNo
  AND (@PRID = 0 OR PRID <> @PRID)", conn, trans))
            {
                checkCmd.Parameters.Add("@RequestNo", SqlDbType.VarChar, 20).Value = requestNo.Trim();
                checkCmd.Parameters.Add("@PRID", SqlDbType.Int).Value = Requisition.Id;
                var exists = Convert.ToInt32(checkCmd.ExecuteScalar() ?? 0);
                if (exists > 0)
                {
                    throw new InvalidOperationException("Request No. already exists.");
                }
            }

            if (isNew)
            {
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

                headerCmd.Parameters.Add("@RequestNo", SqlDbType.VarChar, 20).Value = requestNo.Trim();
                headerCmd.Parameters.Add("@RequestDate", SqlDbType.DateTime).Value = Requisition.RequestDate.Date;
                headerCmd.Parameters.Add("@Description", SqlDbType.NVarChar, 500).Value = Requisition.Description!.Trim();
                headerCmd.Parameters.Add("@Currency", SqlDbType.Int).Value = Requisition.Currency;
                headerCmd.Parameters.Add("@Status", SqlDbType.TinyInt).Value = Requisition.Status;
                headerCmd.Parameters.Add("@PurId", SqlDbType.Int).Value = purId.HasValue ? purId.Value : DBNull.Value;
                Requisition.Id = Convert.ToInt32(headerCmd.ExecuteScalar());
            }
            else
            {
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
                headerCmd.Parameters.Add("@RequestNo", SqlDbType.VarChar, 20).Value = requestNo.Trim();
                headerCmd.Parameters.Add("@RequestDate", SqlDbType.DateTime).Value = Requisition.RequestDate.Date;
                headerCmd.Parameters.Add("@Description", SqlDbType.NVarChar, 500).Value = Requisition.Description!.Trim();
                headerCmd.Parameters.Add("@Currency", SqlDbType.Int).Value = Requisition.Currency;
                headerCmd.Parameters.Add("@Status", SqlDbType.TinyInt).Value = Requisition.Status;
                headerCmd.ExecuteNonQuery();
            }

            if (isNew)
            {
                var consolidatedDetails = ConsolidateDetailsForSave(details);
                ApplyMaterialRequestBuy(conn, trans, consolidatedDetails);

                foreach (var detail in consolidatedDetails)
                {
                    IndexModel.InsertDetail(conn, trans, Requisition.Id, detail);
                }
            }
            else
            {
                var existingDetails = details
                    .Where(x => x.DetailId > 0)
                    .ToList();
                var newDetails = ConsolidateDetailsForSave(details
                    .Where(x => x.DetailId <= 0)
                    .ToList());

                UpdateExistingPrDetails(conn, trans, Requisition.Id, existingDetails);

                if (newDetails.Count > 0)
                {
                    ApplyMaterialRequestBuy(conn, trans, newDetails);

                    foreach (var detail in newDetails)
                    {
                        IndexModel.InsertDetail(conn, trans, Requisition.Id, detail);
                    }
                }
            }

            trans.Commit();
        }
        catch
        {
            trans.Rollback();
            throw;
        }
    }

    // G?p c�c d�ng tr�ng tru?c khi luu d? DB ch? gi? m?t d�ng cho c�ng item ho?c c�ng d�ng MR.
    private static List<PurchaseRequisitionDetailInput> ConsolidateDetailsForSave(IReadOnlyList<PurchaseRequisitionDetailInput> details)
    {
        var consolidated = new List<PurchaseRequisitionDetailInput>();

        foreach (var detail in details)
        {
            PurchaseRequisitionDetailInput? target;
            if (detail.MrDetailId.HasValue && detail.MrDetailId.Value > 0)
            {
                target = consolidated.FirstOrDefault(x => x.MrDetailId == detail.MrDetailId);
            }
            else
            {
                target = consolidated.FirstOrDefault(x =>
                    !x.MrDetailId.HasValue &&
                    x.ItemId == detail.ItemId &&
                    x.SupplierId == detail.SupplierId);
            }

            if (target == null)
            {
                consolidated.Add(new PurchaseRequisitionDetailInput
                {
                    DetailId = detail.DetailId,
                    ItemId = detail.ItemId,
                    ItemCode = detail.ItemCode,
                    ItemName = detail.ItemName,
                    Unit = detail.Unit,
                    QtyFromM = detail.QtyFromM,
                    QtyPur = detail.QtyPur,
                    UnitPrice = detail.UnitPrice,
                    Amount = detail.Amount,
                    Remark = detail.Remark,
                    SupplierId = detail.SupplierId,
                    SupplierText = detail.SupplierText,
                    MrRequestNo = detail.MrRequestNo,
                    MrDetailId = detail.MrDetailId
                });
                continue;
            }

            target.QtyPur += detail.QtyPur;
            target.QtyFromM = Math.Max(target.QtyFromM, detail.QtyFromM);
            target.UnitPrice = detail.UnitPrice;
            target.Amount += detail.Amount;

            if (string.IsNullOrWhiteSpace(target.Remark) && !string.IsNullOrWhiteSpace(detail.Remark))
            {
                target.Remark = detail.Remark;
            }

            if (string.IsNullOrWhiteSpace(target.MrRequestNo) && !string.IsNullOrWhiteSpace(detail.MrRequestNo))
            {
                target.MrRequestNo = detail.MrRequestNo;
            }
        }

        return consolidated;
    }

    // X�c d?nh EmployeeID c?a Purchaser d? g?n cho PR khi t?o m?i ch?ng t?.
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

    // V?i d�ng d� c� s?n trong PR, ch? cho ph�p ch?nh U.Price v� Remark tr?c ti?p tr�n PC_PRDetail.
    private static void UpdateExistingPrDetails(SqlConnection conn, SqlTransaction trans, int prId, IReadOnlyList<PurchaseRequisitionDetailInput> details)
    {
        foreach (var detail in details)
        {
            using var cmd = new SqlCommand(@"
UPDATE dbo.PC_PRDetail
SET UnitPrice = @UnitPrice,
    Remark = @Remark,
    OrdAmount = @OrdAmount
WHERE PRID = @PRID
  AND RecordID = @RecordID", conn, trans);

            cmd.Parameters.Add("@PRID", SqlDbType.Int).Value = prId;
            cmd.Parameters.Add("@RecordID", SqlDbType.BigInt).Value = detail.DetailId;
            cmd.Parameters.Add("@UnitPrice", SqlDbType.Decimal).Value = detail.UnitPrice;
            cmd.Parameters.Add("@Remark", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(detail.Remark) ? DBNull.Value : detail.Remark.Trim();
            cmd.Parameters.Add("@OrdAmount", SqlDbType.Decimal).Value = detail.Amount;
            cmd.ExecuteNonQuery();
        }
    }

    // C?p nh?t l?i MR theo d�ng logic cu: n?u SugBuy < BUY th� tr? BUY, ngu?c l?i ch? set PostedPR = 1.
    private static void ApplyMaterialRequestBuy(SqlConnection conn, SqlTransaction trans, IReadOnlyList<PurchaseRequisitionDetailInput> details)
    {
        var allocations = details
            .Where(x => x.MrDetailId.HasValue && x.MrDetailId.Value > 0)
            .GroupBy(x => x.MrDetailId!.Value)
            .Select(x => new PurchaseRequisitionMrAllocationRow
            {
                MrDetailId = x.Key,
                Quantity = x.Sum(y => y.QtyPur)
            })
            .ToList();

        foreach (var allocation in allocations)
        {
            decimal currentBuy;

            using (var loadCmd = new SqlCommand(@"
SELECT ISNULL(BUY, 0)
FROM dbo.MATERIAL_REQUEST_DETAIL
WHERE ID = @MRDetailID", conn, trans))
            {
                loadCmd.Parameters.Add("@MRDetailID", SqlDbType.Int).Value = allocation.MrDetailId;
                currentBuy = Convert.ToDecimal(loadCmd.ExecuteScalar() ?? 0);
            }

            if (allocation.Quantity < currentBuy)
            {
                using var updateCmd = new SqlCommand(@"
UPDATE dbo.MATERIAL_REQUEST_DETAIL
SET BUY = @RemainBuy
WHERE ID = @MRDetailID", conn, trans);

                updateCmd.Parameters.Add("@RemainBuy", SqlDbType.Decimal).Value = currentBuy - allocation.Quantity;
                updateCmd.Parameters.Add("@MRDetailID", SqlDbType.Int).Value = allocation.MrDetailId;
                updateCmd.ExecuteNonQuery();
                continue;
            }

            using var postedCmd = new SqlCommand(@"
UPDATE dbo.MATERIAL_REQUEST_DETAIL
SET PostedPR = 1
WHERE ID = @MRDetailID", conn, trans);

            postedCmd.Parameters.Add("@MRDetailID", SqlDbType.Int).Value = allocation.MrDetailId;
            postedCmd.ExecuteNonQuery();
        }
    }

    private static void UpdateDescriptionFromMrRequestNos(SqlConnection conn, SqlTransaction trans, int prId)
    {
        using var cmd = new SqlCommand(@"
UPDATE dbo.PC_PR
SET [Description] = ISNULL(src.[Description], '')
FROM dbo.PC_PR pr
OUTER APPLY
(
    SELECT CASE
        WHEN ISNULL(src.RequestNos, '') = '' THEN ''
        ELSE 'MR ' + src.RequestNos
    END AS [Description]
    FROM
    (
        SELECT STUFF((
            SELECT DISTINCT ', ' + LTRIM(RTRIM(ISNULL(d.MRRequestNO, '')))
            FROM dbo.PC_PRDetail d
            WHERE d.PRID = pr.PRID
              AND ISNULL(LTRIM(RTRIM(d.MRRequestNO)), '') <> ''
            FOR XML PATH(''), TYPE
        ).value('.', 'nvarchar(max)'), 1, 2, '') AS RequestNos
    ) src
) src
WHERE pr.PRID = @PRID", conn, trans);

        cmd.Parameters.Add("@PRID", SqlDbType.Int).Value = prId;
        cmd.ExecuteNonQuery();
    }

    // Parse JSON detail do frontend g?i l�n th�nh danh s�ch object d? backend ki?m tra v� luu.
    private List<PurchaseRequisitionDetailInput> ParseDetails()
    {
        if (string.IsNullOrWhiteSpace(DetailsJson))
        {
            return new List<PurchaseRequisitionDetailInput>();
        }

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

    // Ki?m tra d? li?u c?a t?ng d�ng item tru?c khi luu.
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

    private List<int> GetAllowedViewStatuses()
    {
        var statuses = new List<int>();
        foreach (var status in new[] { 1, 2, 3, 4 })
        {
            if (GetEffectivePermissionsByStatus(status).Contains(PermissionViewDetail))
            {
                statuses.Add(status);
            }
        }

        return statuses;
    }

    private static void BindViewDetailFilterParams(SqlCommand cmd, PurchaseRequisitionViewDetailFilterRequest request)
    {
        cmd.Parameters.Add("@RequestNo", SqlDbType.VarChar, 50).Value = string.IsNullOrWhiteSpace(request.RequestNo) ? DBNull.Value : request.RequestNo.Trim();
        cmd.Parameters.Add("@Description", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(request.Description) ? DBNull.Value : request.Description.Trim();
        cmd.Parameters.Add("@ItemCode", SqlDbType.VarChar, 100).Value = string.IsNullOrWhiteSpace(request.ItemCode) ? DBNull.Value : request.ItemCode.Trim();
        cmd.Parameters.Add("@RecQty", SqlDbType.Decimal).Value = request.RecQty.HasValue ? request.RecQty.Value : DBNull.Value;
        cmd.Parameters.Add("@FromDate", SqlDbType.Date).Value = request.FromDate.HasValue ? request.FromDate.Value.Date : DBNull.Value;
        cmd.Parameters.Add("@ToDate", SqlDbType.Date).Value = request.ToDate.HasValue ? request.ToDate.Value.Date : DBNull.Value;
    }

    // N?p th�ng tin vai tr� workflow c?a user hi?n t?i t? b?ng MS_Employee.
    private void LoadCurrentWorkflowUser()
    {
        _workflowUser = new PurchaseRequisitionWorkflowUserInfo();
        var employeeCode = User.Identity?.Name?.Trim();
        if (string.IsNullOrWhiteSpace(employeeCode))
        {
            return;
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"
SELECT TOP 1 EmployeeID,
       ISNULL(EmployeeCode, '') AS EmployeeCode,
       ISNULL(EmployeeName, '') AS EmployeeName,
       ISNULL(DeptID, 0) AS DeptID,
       ISNULL(IsPurchaser, 0) AS IsPurchaser,
       ISNULL(IsCFO, 0) AS IsCFO,
       ISNULL(IsBOD, 0) AS IsBOD
FROM dbo.MS_Employee
WHERE EmployeeCode = @EmployeeCode", conn);

        cmd.Parameters.Add("@EmployeeCode", SqlDbType.VarChar, 50).Value = employeeCode;
        conn.Open();
        using var rd = cmd.ExecuteReader();
        if (!rd.Read())
        {
            return;
        }

        _workflowUser = new PurchaseRequisitionWorkflowUserInfo
        {
            EmployeeId = Convert.ToInt32(rd["EmployeeID"]),
            EmployeeCode = Convert.ToString(rd["EmployeeCode"]) ?? string.Empty,
            EmployeeName = Convert.ToString(rd["EmployeeName"]) ?? string.Empty,
            DeptId = Convert.ToInt32(rd["DeptID"]),
            IsPurchaser = Convert.ToBoolean(rd["IsPurchaser"]),
            IsCFO = Convert.ToBoolean(rd["IsCFO"]),
            IsBOD = Convert.ToBoolean(rd["IsBOD"])
        };
    }

    // X�c d?nh c�c n�t du?c ph�p hi?n th? theo tr?ng th�i ch?ng t? v� vai tr� workflow hi?n t?i.
    private void SetActionFlags()
    {
        var canUploadByPermission = Requisition.Id > 0 && (IsAdminRole() || PagePerm.HasPermission(PermissionUploadAttachment));

        if (IsViewMode)
        {
            CanSave = false;
            CanApproveWorkflow = CanApproveCurrentStep();
            CanDisapproveWorkflow = CanApproveAsCfo() || CanApproveAsBod();
            CanResetToNew = false;
            CanMoveToMr = false;
            CanManageAttachments = canUploadByPermission;
            CanEditExistingDetailFields = false;
            return;
        }

        var effectivePermissions = GetEffectivePermissionsByStatus(Requisition.Status <= 0 ? 1 : Requisition.Status);

        CanSave = Mode == "add"
            ? effectivePermissions.Contains(PermissionAdd)
            : Mode == "edit" && CanEditDocument(effectivePermissions);
        CanEditExistingDetailFields = Mode == "edit" && _workflowUser.IsPurchaser && Requisition.Id > 0;
        CanApproveWorkflow = CanApproveCurrentStep();
        CanDisapproveWorkflow = CanApproveAsCfo() || CanApproveAsBod();
        CanResetToNew = Requisition.Id > 0
            && Requisition.Status == 3
            && effectivePermissions.Contains(PermissionChangeStatus)
            && (_workflowUser.IsPurchaser || _workflowUser.IsCFO || _workflowUser.IsBOD || IsAdminRole());
        CanMoveToMr = Requisition.Id > 0 && CanSave && Requisition.Status == 1;
        CanManageAttachments = canUploadByPermission;
    }

    // X�c d?nh PU/CFO/BOD/Admin c� dang thu?c nh�m workflow du?c ph�p d�ng CST New ? tr?ng th�i Pending hay kh�ng.
    private bool IsWorkflowActor()
    {
        return _workflowUser.IsPurchaser || _workflowUser.IsCFO || _workflowUser.IsBOD || IsAdminRole();
    }

    // X�c d?nh c� cho ph�p m? mode=edit hay kh�ng theo d�ng rule tr?ng th�i c?a Purchase Requisition.
    private bool CanOpenEditMode(List<int> effectivePermissions)
    {
        if (Mode != "edit")
        {
            return false;
        }

        return Requisition.Status switch
        {
            1 => effectivePermissions.Contains(PermissionEdit),
            3 => effectivePermissions.Contains(PermissionChangeStatus) && IsWorkflowActor(),
            _ => false
        };
    }

    private bool CanViewCurrentWorkflowStep()
    {
        if (IsAdminRole() || _workflowUser.IsPurchaser)
        {
            return true;
        }

        if (_workflowUser.IsCFO)
        {
            return Requisition.Status == 2
                && Requisition.PurId.HasValue
                && !Requisition.CAId.HasValue;
        }

        if (_workflowUser.IsBOD)
        {
            return Requisition.Status == 2
                && Requisition.CAId.HasValue
                && !Requisition.GDId.HasValue;
        }

        return false;
    }
    // X�c d?nh edit th?t s? d? li?u ch?ng t? ch? du?c ph�p ? tr?ng th�i New.
    private bool CanEditDocument(List<int> effectivePermissions)
    {
        return Requisition.Status == 1 && effectivePermissions.Contains(PermissionEdit);
    }

    // X�c d?nh user hi?n t?i c� dang ? bu?c PU duy?t d?u ti�n hay kh�ng.
    private bool CanApproveAsPurchaser()
    {
        return Requisition.Id > 0 && Requisition.Status == 1 && (_workflowUser.IsPurchaser || IsAdminRole());
    }

    // X�c d?nh user hi?n t?i c� dang ? bu?c CFO duy?t hay kh�ng.
    private bool CanApproveAsCfo()
    {
        return Requisition.Id > 0
            && Requisition.Status == 2
            && !Requisition.CAId.HasValue
            && (_workflowUser.IsCFO || IsAdminRole());
    }

    // X�c d?nh user hi?n t?i c� dang ? bu?c BOD duy?t hay kh�ng.
    private bool CanApproveAsBod()
    {
        return Requisition.Id > 0
            && Requisition.Status == 2
            && Requisition.CAId.HasValue
            && !Requisition.GDId.HasValue
            && (_workflowUser.IsBOD || IsAdminRole());
    }

    // X�c d?nh c� du?c b?t n�t Approve trong bu?c workflow hi?n t?i hay kh�ng.
    private bool CanApproveCurrentStep()
    {
        return CanApproveAsPurchaser() || CanApproveAsCfo() || CanApproveAsBod();
    }

    // Chu?n b? d? li?u chung cho c�c handler post l�m vi?c v?i ch?ng t? hi?n h?u.
    private IActionResult? PrepareExistingRecordForPost()
    {
        PagePerm = GetUserPermissions();
        LoadAllDropdowns();
        LoadCurrentWorkflowUser();

        if (Requisition.Id <= 0)
        {
            TempData["Message"] = "Purchase requisition is not found.";
            TempData["MessageType"] = "warning";
            return RedirectToPage("./Index");
        }

        try
        {
            LoadExistingWorkflowData(Requisition.Id);
            AttachmentList = LoadAttachmentRows(Requisition.Id);
            SetActionFlags();
        }
        catch (Exception ex)
        {
            TempData["Message"] = ex.Message;
            TempData["MessageType"] = "error";
            return RedirectToPage("./Index");
        }

        return null;
    }

    // �i?u hu?ng quay l?i d�ng record hi?n t?i sau khi x? l� workflow ho?c file d�nh k�m.
    private IActionResult RedirectToCurrentDetail(string? mode = null)
    {
        var currentMode = string.IsNullOrWhiteSpace(mode) ? "view" : mode.Trim().ToLowerInvariant();
        return RedirectToPage("./PurchaseRequisitionDetail", new { id = Requisition.Id, mode = currentMode });
    }

    // L?y t?p quy?n tinh c?a user tr�n function Purchase Requisition Detail.
    private PagePermissions GetUserPermissions()
    {
        var isAdmin = IsAdminRole();
        var roleId = GetCurrentRoleId();
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

    // L?y quy?n hi?u l?c theo tr?ng th�i PR hi?n t?i d? quy?t d?nh add, edit hay view.
    private List<int> GetEffectivePermissionsByStatus(int status)
    {
        var isAdmin = IsAdminRole();
        var roleId = GetCurrentRoleId();

        if (isAdmin)
        {
            return Enumerable.Range(1, 20).ToList();
        }

        return _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, status);
    }

    // L?y RoleID hi?n t?i t? claim dang nh?p.
    private int GetCurrentRoleId()
    {
        return int.Parse(User.FindFirst("RoleID")?.Value ?? "0");
    }

    // X�c d?nh user hi?n t?i c� ph?i admin role hay kh�ng.
    private bool IsAdminRole()
    {
        return User.FindFirst("IsAdminRole")?.Value == "True";
    }

    // L?y danh s�ch ngu?i nh?n mail theo vai tr� workflow hi?n t?i.
    private List<PurchaseRequisitionNotifyRecipientViewModel> GetEmailsByWorkflowRole(SqlConnection conn, SqlTransaction trans, string workflowRole)
    {
        if (string.Equals(workflowRole, "CFO", StringComparison.OrdinalIgnoreCase))
        {
            return GetEmailsByFlag(conn, trans, "IsCFO");
        }

        if (string.Equals(workflowRole, "BOD", StringComparison.OrdinalIgnoreCase))
        {
            return GetBodEmails(conn, trans);
        }

        return new List<PurchaseRequisitionNotifyRecipientViewModel>();
    }

    private List<PurchaseRequisitionNotifyRecipientViewModel> GetPurchaserEmails(SqlConnection conn, SqlTransaction? trans)
    {
        var rows = new List<PurchaseRequisitionNotifyRecipientViewModel>();
        if (!Requisition.PurId.HasValue)
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
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = Requisition.PurId.Value;

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(new PurchaseRequisitionNotifyRecipientViewModel
            {
                Email = Convert.ToString(rd["TheEmail"])?.Trim() ?? string.Empty,
                EmployeeCode = Convert.ToString(rd["EmployeeCode"])?.Trim() ?? string.Empty,
                EmployeeName = Convert.ToString(rd["EmployeeName"])?.Trim() ?? string.Empty,
                Title = Convert.ToString(rd["Title"])?.Trim() ?? string.Empty
            });
        }

        return rows;
    }

    // L?y danh s�ch ngu?i nh?n mail theo m?t c? bool trong MS_Employee nhu IsCFO ho?c IsBOD.
    private static List<PurchaseRequisitionNotifyRecipientViewModel> GetEmailsByFlag(SqlConnection conn, SqlTransaction trans, string flagColumn)
    {
        var rows = new List<PurchaseRequisitionNotifyRecipientViewModel>();

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

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var email = Convert.ToString(rd["TheEmail"]) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(email))
            {
                rows.Add(new PurchaseRequisitionNotifyRecipientViewModel
                {
                    Email = email.Trim(),
                    EmployeeCode = Convert.ToString(rd["EmployeeCode"])?.Trim() ?? string.Empty,
                    EmployeeName = Convert.ToString(rd["EmployeeName"])?.Trim() ?? string.Empty,
                    Title = Convert.ToString(rd["Title"])?.Trim() ?? string.Empty
                });
            }
        }

        return rows;
    }

    // L?y danh s�ch ngu?i nh?n mail c?a BOD, uu ti�n b?ng c?u h�nh SYS_Funtion_LevelProcess n?u h? th?ng c� b?ng n�y.
    private List<PurchaseRequisitionNotifyRecipientViewModel> GetBodEmails(SqlConnection conn, SqlTransaction trans)
    {
        var rows = new List<PurchaseRequisitionNotifyRecipientViewModel>();

        using var cmd = new SqlCommand(@"
IF OBJECT_ID('dbo.SYS_Funtion_LevelProcess', 'U') IS NOT NULL
BEGIN
    IF EXISTS
    (
        SELECT 1
        FROM sys.columns
        WHERE object_id = OBJECT_ID('dbo.SYS_Funtion_LevelProcess')
          AND name = 'Level'
    )
    BEGIN
        SELECT DISTINCT
            LTRIM(RTRIM(e.TheEmail)) AS TheEmail,
            LTRIM(RTRIM(e.EmployeeCode)) AS EmployeeCode,
            LTRIM(RTRIM(e.EmployeeName)) AS EmployeeName,
            LTRIM(RTRIM(ISNULL(e.Title, ''))) AS Title
        FROM dbo.MS_Employee e
        WHERE ISNULL(e.IsBOD, 0) = 1
          AND ISNULL(LTRIM(RTRIM(e.TheEmail)), '') <> ''
          AND ISNULL(e.IsActive, 0) = 1;
        RETURN;
    END
END

SELECT DISTINCT
    LTRIM(RTRIM(TheEmail)) AS TheEmail,
    LTRIM(RTRIM(EmployeeCode)) AS EmployeeCode,
    LTRIM(RTRIM(EmployeeName)) AS EmployeeName,
    LTRIM(RTRIM(ISNULL(Title, ''))) AS Title
FROM dbo.MS_Employee
WHERE ISNULL(IsBOD, 0) = 1
  AND ISNULL(LTRIM(RTRIM(TheEmail)), '') <> ''
  AND ISNULL(IsActive, 0) = 1", conn, trans);

        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            var email = Convert.ToString(rd["TheEmail"]) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(email))
            {
                rows.Add(new PurchaseRequisitionNotifyRecipientViewModel
                {
                    Email = email.Trim(),
                    EmployeeCode = Convert.ToString(rd["EmployeeCode"])?.Trim() ?? string.Empty,
                    EmployeeName = Convert.ToString(rd["EmployeeName"])?.Trim() ?? string.Empty,
                    Title = Convert.ToString(rd["Title"])?.Trim() ?? string.Empty
                });
            }
        }

        return rows;
    }

    // T�nh t?ng ti?n hi?n t?i c?a PR t? b?ng chi ti?t d? mail lu�n d�ng d�ng d? li?u m?i nh?t.
    private decimal GetCurrentTotalAmount(SqlConnection conn, SqlTransaction? trans)
    {
        using var cmd = new SqlCommand(@"
SELECT ISNULL(SUM(ISNULL(Quantity, 0) * ISNULL(UnitPrice, 0)), 0)
FROM dbo.PC_PRDetail
WHERE PRID = @PRID", conn, trans);

        cmd.Parameters.Add("@PRID", SqlDbType.Int).Value = Requisition.Id;
        var value = cmd.ExecuteScalar();
        return value == null || value == DBNull.Value ? 0M : Convert.ToDecimal(value);
    }

    // Format s? ti?n theo d�ng c�ch hi?n th? hi?n t?i c?a m�n h�nh: c� d?u ph?y h�ng ngh�n v� b? s? 0 du cu?i.
    private static string FormatAmountForView(decimal amount)
    {
        return amount.ToString("#,##0");
    }

    // L?y t�n currency hi?n th? cho mail theo m� ti?n t? dang luu tr�n ch?ng t?.
    private string GetCurrencyDisplayText(int currencyId)
    {
        return currencyId switch
        {
            1 => "VND",
            _ => currencyId.ToString()
        };
    }

    // T?o n?i dung mail th�ng b�o workflow cho bu?c duy?t k? ti?p.
    private string BuildWorkflowNotifyBody(SqlConnection conn, SqlTransaction trans, string title, string color, string actionText)
    {
        var detailUrl = Url.Page("/Purchasing/PurchaseRequisition/PurchaseRequisitionDetail", values: new
        {
            id = Requisition.Id,
            mode = "view"
        });

        var absoluteUrl = string.IsNullOrWhiteSpace(detailUrl)
            ? string.Empty
            : $"{Request.Scheme}://{Request.Host}{detailUrl}";
        var totalAmount = GetCurrentTotalAmount(conn, trans);

        var body = $@"
<p>Dear {{{{RECIPIENT_LABEL}}}},</p>
<p>Purchase Requisition <b>{WebUtility.HtmlEncode(Requisition.RequestNo)}</b> is {WebUtility.HtmlEncode(actionText)}</p>
<ul>
  <li>Date: <b>{Requisition.RequestDate:dd/MM/yyyy}</b></li>
  <li>Description: <b>{WebUtility.HtmlEncode(Requisition.Description ?? string.Empty)}</b></li>
  <li>Total Amount: <b>{FormatAmountForView(totalAmount)} {WebUtility.HtmlEncode(GetCurrencyDisplayText(Requisition.Currency))}</b></li>
</ul>
{(string.IsNullOrWhiteSpace(absoluteUrl) ? string.Empty : $"<p>Open Approval Page: <a href=\"{WebUtility.HtmlEncode(absoluteUrl)}\">Purchase Requisition Approve</a></p>")}
<p>SmartSam System</p>";

        return EmailTemplateHelper.WrapInNotifyTemplate(title, color, DateTime.Now, body);
    }

    private string BuildWorkflowResultNotifyBody(SqlConnection conn, SqlTransaction? trans, string color, string statusText)
    {
        var totalAmount = GetCurrentTotalAmount(conn, trans);
        var approvedBy = string.IsNullOrWhiteSpace(_workflowUser.EmployeeName)
            ? _workflowUser.EmployeeCode
            : _workflowUser.EmployeeName;
        var body = $@"
<p>Dear {{{{RECIPIENT_LABEL}}}},</p>
<ul>
  <li>Date: <b>{Requisition.RequestDate:dd/MM/yyyy}</b></li>
  <li>Description: <b>{WebUtility.HtmlEncode(Requisition.Description ?? string.Empty)}</b></li>
  <li>Total Amount: <b>{FormatAmountForView(totalAmount)} {WebUtility.HtmlEncode(GetCurrencyDisplayText(Requisition.Currency))}</b></li>
  <li>Status: <b>{WebUtility.HtmlEncode(statusText)}</b></li>
  <li>Approved By: <b>{WebUtility.HtmlEncode(approvedBy)}</b></li>
</ul>
<p>SmartSam System</p>";

        return EmailTemplateHelper.WrapInNotifyTemplate("PURCHASE REQUISITION", color, DateTime.Now, body);
    }

    // �?y t�c v? g?i mail ra n?n d? ngu?i d�ng kh�ng ph?i ch? k?t qu? SMTP.
    private void TryQueueWorkflowNotifyEmail(List<PurchaseRequisitionNotifyRecipientViewModel> recipients, string subject, string htmlBody)
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

        _ = SendNotifyEmailAsync(new PurchaseRequisitionWorkflowNotifyRequestViewModel
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

    // �p ti?n t? subject theo c?u h�nh EmailSettings khi FunctionID c?a Purchase Requisition n?m trong danh s�ch test.
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

    // Ki?m tra c?u h�nh TestFunctionIDs d? quy?t d?nh c� th�m ti?n t? test v�o subject hay kh�ng.
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

    // G?i mail workflow b?ng SMTP v?i body HTML theo template chung.
    private async Task SendNotifyEmailAsync(PurchaseRequisitionWorkflowNotifyRequestViewModel notifyRequest)
    {
        try
        {
            foreach (var recipient in notifyRequest.Recipients)
            {
                if (string.IsNullOrWhiteSpace(recipient.Email))
                {
                    continue;
                }

                var recipientName = string.IsNullOrWhiteSpace(recipient.EmployeeName) ? recipient.Email : recipient.EmployeeName;
                var recipientTitle = (recipient.Title ?? string.Empty).Trim();
                var recipientLabel = string.IsNullOrWhiteSpace(recipientTitle)
                    ? (string.IsNullOrWhiteSpace(recipient.EmployeeCode)
                        ? recipientName
                        : $"{recipientName} ({recipient.EmployeeCode})")
                    : (string.IsNullOrWhiteSpace(recipient.EmployeeCode)
                        ? $"{recipientTitle} {recipientName}"
                        : $"{recipientTitle} {recipientName}({recipient.EmployeeCode})");

                using var mail = new MailMessage
                {
                    From = new MailAddress(notifyRequest.SenderEmail, "SmartSam System"),
                    Subject = notifyRequest.Subject,
                    Body = notifyRequest.HtmlBody.Replace("{{RECIPIENT_LABEL}}", WebUtility.HtmlEncode(recipientLabel)),
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
            _logger.LogError(ex, "Cannot send workflow email for purchase requisition {PrId}.", Requisition.Id);
        }
    }

    // Ki?m tra file upload theo danh s�ch extension v� gi?i h?n dung lu?ng c?u h�nh trong appsettings.json.
    private string? ValidateAttachment(IFormFile file)
    {
        var allowedExtensions = AllowedAttachmentExtensionsText
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => x.StartsWith('.') ? x.ToLowerInvariant() : $".{x.ToLowerInvariant()}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var extension = Path.GetExtension(file.FileName) ?? string.Empty;
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

    // Luu file d�nh k�m v?t l� v� ghi t�n file v�o b?ng PC_PR_Doc.
    private void SaveAttachment(SqlConnection conn, SqlTransaction trans, int prId, int? userId, IFormFile file, List<string> savedFilePaths)
    {
        var uploadFolder = ResolveUploadFolder();
        Directory.CreateDirectory(uploadFolder);

        var savedFileName = BuildAttachmentFileName(file.FileName);
        var fullPath = Path.Combine(uploadFolder, savedFileName);

        using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            file.CopyTo(stream);
        }

        savedFilePaths.Add(fullPath);

        using var cmd = new SqlCommand(@"
INSERT INTO dbo.PC_PR_Doc
(
    PRID,
    FilePath,
    UploadDate,
    UserID
)
VALUES
(
    @PRID,
    @FilePath,
    GETDATE(),
    @UserID
)", conn, trans);

        cmd.Parameters.Add("@PRID", SqlDbType.Int).Value = prId;
        cmd.Parameters.Add("@FilePath", SqlDbType.NVarChar, 1000).Value = savedFileName;
        cmd.Parameters.Add("@UserID", SqlDbType.Int).Value = userId.HasValue ? userId.Value : DBNull.Value;
        cmd.ExecuteNonQuery();
    }

    // X�c d?nh thu m?c luu file d�nh k�m t? c?u h�nh FileUploads:FilePath.
    private string ResolveUploadFolder()
    {
        var basePath = _config.GetValue<string>("FileUploads:BasePath");
        if (string.IsNullOrWhiteSpace(basePath))
        {
            var legacyPath = _config.GetValue<string>("FileUploads:FilePath");
            if (string.IsNullOrWhiteSpace(legacyPath))
            {
                throw new InvalidOperationException("FileUploads:BasePath or FileUploads:FilePath is missing in appsettings.json.");
            }

            return Path.IsPathRooted(legacyPath)
                ? legacyPath
                : Path.Combine(Directory.GetCurrentDirectory(), legacyPath);
        }

        var rootPath = Path.IsPathRooted(basePath)
            ? basePath
            : Path.Combine(Directory.GetCurrentDirectory(), basePath);

        var configuredFunctionPath = _config.GetValue<string>($"FileUploads:Funtions:{FUNCTION_ID}");
        if (string.IsNullOrWhiteSpace(configuredFunctionPath))
        {
            return rootPath;
        }

        var relativeSegments = configuredFunctionPath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return relativeSegments.Length == 0
            ? rootPath
            : Path.Combine([rootPath, .. relativeSegments]);
    }

    // Sinh t�n file m?i c� th�m ticks d? tr�nh tr�ng t�n khi upload.
    private static string BuildAttachmentFileName(string originalFileName)
    {
        var sourceName = Path.GetFileName(originalFileName);
        var nameOnly = Path.GetFileNameWithoutExtension(sourceName);
        var extension = Path.GetExtension(sourceName);
        var safeName = string.Concat(nameOnly.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries)).Trim();
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "attachment";
        }

        return $"{safeName}_{DateTime.UtcNow.Ticks}{extension}";
    }

    // X�a file v?t l� d� luu n?u transaction insert file d�nh k�m b? l?i v� ph?i rollback.
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
                // Kh�ng ch?n lu?ng ch�nh n?u d?n file t?m b? l?i.
            }
        }
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

    [Required(ErrorMessage = "Description is required.")]
    [StringLength(500)]
    public string? Description { get; set; }

    public int Currency { get; set; } = 1;
    public byte Status { get; set; }
    public int? PurId { get; set; }
    public string PurApproveDate { get; set; } = string.Empty;
    public int? CAId { get; set; }
    public string CAApproveDate { get; set; } = string.Empty;
    public int? GDId { get; set; }
    public string GDApproveDate { get; set; } = string.Empty;
}

public class PurchaseRequisitionWorkflowUserInfo
{
    public int EmployeeId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public int DeptId { get; set; }
    public bool IsPurchaser { get; set; }
    public bool IsCFO { get; set; }
    public bool IsBOD { get; set; }
}

public class PurchaseRequisitionMrAllocationRow
{
    public int MrDetailId { get; set; }
    public decimal Quantity { get; set; }
}

public class PurchaseRequisitionAttachmentViewModel
{
    public int DocId { get; set; }
    public int PrId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public DateTime? UploadDate { get; set; }
    public string UserCode { get; set; } = string.Empty;
}

public class PurchaseRequisitionWorkflowNotifyRequestViewModel
{
    public string SenderEmail { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string MailServer { get; set; } = string.Empty;
    public int MailPort { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string HtmlBody { get; set; } = string.Empty;
    public List<PurchaseRequisitionNotifyRecipientViewModel> Recipients { get; set; } = new List<PurchaseRequisitionNotifyRecipientViewModel>();
}

public class PurchaseRequisitionNotifyRecipientViewModel
{
    public string Email { get; set; } = string.Empty;
    public string EmployeeCode { get; set; } = string.Empty;
    public string EmployeeName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
}

public class PurchaseRequisitionViewDetailFilterRequest
{
    public string? RequestNo { get; set; }
    public string? Description { get; set; }
    public string? ItemCode { get; set; }
    public string RecQtyOperator { get; set; } = "=";
    public decimal? RecQty { get; set; }
    public bool UseDateRange { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class PurchaseRequisitionViewDetailRow
{
    public int PrId { get; set; }
    public string RequestNo { get; set; } = string.Empty;
    public DateTime? RequestDate { get; set; }
    public string RequestDateText { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public decimal PrQty { get; set; }
    public decimal RecQty { get; set; }
}

public class PurchaseRequisitionViewDetailResponse
{
    public List<PurchaseRequisitionViewDetailRow> Rows { get; set; } = new List<PurchaseRequisitionViewDetailRow>();
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalRecords { get; set; }
    public int TotalPages { get; set; } = 1;
}
