using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Net;
using System.Net.Mail;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Helpers;
using SmartSam.Services;

namespace SmartSam.Pages.Inventory.InventoryReceived;

public class InventoryReceivedDetailModel : BasePageModel
{
    private const int FunctionId = 67;
    private const int PermissionViewDetail = 2;
    private const int PermissionAdd = 3;
    private const int PermissionEdit = 4;
    private const int PermissionApproved = 5;
    // private const string NotifyCcEmail = "maiquangvinhi4@gmail.com";
    // private const string ReturnMoveToCpnNotifyEmail = "maiquangvinhi4@gmail.com";
    private const string NotifyCcEmail = "hai.dq@saigonskygarden.com.vn";
    private const string ReturnMoveToCpnNotifyEmail = "hai.dq@saigonskygarden.com.vn";
    private readonly PermissionService _permissionService;

    public InventoryReceivedDetailModel(IConfiguration config, PermissionService permissionService) : base(config) { _permissionService = permissionService; }

    public PagePermissions PagePerm { get; private set; } = new();
    [BindProperty(SupportsGet = true)] public long? Id { get; set; }
    [BindProperty(SupportsGet = true)] public string Mode { get; set; } = "view";
    [BindProperty] public InventoryReceivedHeader Header { get; set; } = new();
    [BindProperty] public string DetailsJson { get; set; } = "[]";
    [BindProperty] public long? SelectedIssueFlowId { get; set; }
    [BindProperty] public string DeletedAttachmentDocIdsJson { get; set; } = "[]";
    [BindProperty] public List<IFormFile> AttachmentUploads { get; set; } = new();
    [BindProperty] public bool ConfirmAfterSave { get; set; }
    public string? Message { get; set; }
    public string MessageType { get; set; } = "info";
    public string AllowedAttachmentExtensionsText => _config.GetValue<string>("FileUploads:AllowedExtensions") ?? ".doc,.docx,.xls,.xlsx,.pdf,.jpg,.jpeg,.png";
    public int MaxAttachmentSizeMb => _config.GetValue<int?>("FileUploads:MaxFileSizeMb") ?? 10;
    public string CurrentEmployeeCode => GetCurrentEmployeeCode();
    public int CurrentKpGroupIdValue => GetCurrentKpGroupId();

    public List<SelectListItem> ReceiveTypes { get; set; } = new();
    public List<SelectListItem> Stores { get; set; } = new();
    public List<SelectListItem> Departments { get; set; } = new();
    public List<SelectListItem> Locations { get; set; } = new();
    public List<SelectListItem> PONos { get; set; } = new();
    public List<SelectListItem> Statuses { get; set; } = new();
    public List<SelectListItem> ItemOptions { get; set; } = new();
    public List<SelectListItem> MoveToCPNStores { get; set; } = new();
    public List<InventoryReceivedItemOption> ItemCatalog { get; set; } = new();
    public List<InventoryReceivedDetailRow> Details { get; set; } = new();
    public List<InventoryReceivedAttachmentViewModel> AttachmentList { get; set; } = new();

    public bool IsViewMode => string.Equals(Mode, "view", StringComparison.OrdinalIgnoreCase);
    public bool IsEditMode => string.Equals(Mode, "edit", StringComparison.OrdinalIgnoreCase);
    public bool IsApprovedMode => string.Equals(Mode, "approved", StringComparison.OrdinalIgnoreCase);
    public bool IsAddMode => string.Equals(Mode, "add", StringComparison.OrdinalIgnoreCase);
    public bool CanSave => IsAddMode ? PagePerm.HasPermission(PermissionAdd) : IsEditMode && PagePerm.HasPermission(PermissionEdit);
    public bool CanEditVoucherBusiness => EvaluateCanEditVoucherBusiness();
    public bool CanConfirmVoucherBusiness => EvaluateCanConfirmVoucherBusiness();

    public IActionResult OnGet(long? id, string mode = "view", string? message = null, string? messageType = null)
    {
        PagePerm = GetUserPermissions();
        Mode = string.IsNullOrWhiteSpace(mode) ? "view" : mode.Trim().ToLowerInvariant();
        Message = message;
        MessageType = string.IsNullOrWhiteSpace(messageType) ? "info" : messageType;
        if (id.HasValue && IsViewMode && !PagePerm.HasPermission(PermissionViewDetail)) return Redirect("/");
        if (!id.HasValue && !PagePerm.HasPermission(PermissionAdd)) return Redirect("/");
        if (IsApprovedMode && !PagePerm.HasPermission(PermissionApproved)) return Redirect("/");
        if (IsEditMode && !PagePerm.HasPermission(PermissionEdit)) return Redirect("/");

        if (id.HasValue)
        {
            Id = id;
            LoadHeader(id.Value);
            LoadLookups();
            LoadDetails(id.Value);
            AttachmentList = LoadAttachmentRows(id.Value);
            DetailsJson = JsonSerializer.Serialize(Details);
        }
        else
        {
            LoadLookups();
            Header.FlowDate = DateTime.Today;
            Header.StatusID = 1;
            Header.FlowNo = GetNextVoucherNo(Header.FlowSubType);
            Header.StatusName = GetStatusName(Header.StatusID, Header.FlowSubType);
            DetailsJson = "[]";
        }
        return Page();
    }

    public IActionResult OnPostSave(long? id, string mode)
    {
        PagePerm = GetUserPermissions();
        Mode = string.IsNullOrWhiteSpace(mode) ? "view" : mode.Trim().ToLowerInvariant();
        if ((id.HasValue && !PagePerm.HasPermission(PermissionEdit)) || (!id.HasValue && !PagePerm.HasPermission(PermissionAdd))) return Redirect("/");
        if (IsApprovedMode) return Redirect("/");

        if (id.HasValue && !EvaluateCanEditVoucherBusiness())
        {
            TempData["Message"] = "This voucher can no longer be edited by business rule.";
            TempData["MessageType"] = "warning";
            return RedirectToPage(new { id, mode = "edit", message = "This voucher can no longer be edited by business rule.", messageType = "warning" });
        }

        LoadLookups();
        Details = ParseDetails(DetailsJson);
        Header.StatusID = GetDefaultStatusId(Header.FlowSubType);
        if (!id.HasValue)
        {
            Header.FlowNo = GetNextVoucherNo(Header.FlowSubType);
        }
        Header.StatusName = GetStatusName(Header.StatusID, Header.FlowSubType);
        var attachmentValidationMessages = AttachmentUploads
            .Where(file => file != null && file.Length > 0)
            .Select(file => ValidateAttachment(file))
            .Where(message => !string.IsNullOrWhiteSpace(message))
            .ToList();
        foreach (var message in attachmentValidationMessages)
        {
            ModelState.AddModelError(string.Empty, message!);
        }
        ValidateBusinessRules(id);
        if (!ModelState.IsValid)
        {
            Message = string.Join(" ",
                ModelState
                    .SelectMany(kv => kv.Value.Errors)
                    .Select(e => e.ErrorMessage)
                    .Where(e => !string.IsNullOrWhiteSpace(e))
                    .Distinct());
            if (id.HasValue && id.Value > 0)
            {
                AttachmentList = LoadAttachmentRows(id.Value);
            }
            MessageType = "error";
            return Page();
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var tran = conn.BeginTransaction();
        var savedFilePaths = new List<string>();

        try
        {
            long flowId;
            if (!id.HasValue)
            {
                using var cmd = new SqlCommand(@"INSERT INTO dbo.INV_ItemFlow(FlowType,KPGroup,OperatorID,FlowNo,FlowDate,ToStore,FlowSubType,RetDept,RetAssetLocation,POID,MoveToCPNStore,According,Reason,StatusID)
VALUES(2,@KPGroup,@OperatorId,@FlowNo,@FlowDate,@ToStore,@FlowSubType,@RetDept,@RetLocation,@POID,@MoveToCPNStore,@According,@Reason,@StatusID);
SELECT CAST(SCOPE_IDENTITY() AS bigint);", conn, tran);
                BindHeaderParams(cmd);
                flowId = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);

                using var actionCmd = new SqlCommand(@"INSERT INTO dbo.INV_ItemFlowAction(FlowID,UserID,TheDateTime,ActionTypeID,ComputerName,Des)
VALUES(@FlowID,@UserID,GETDATE(),1,@ComputerName,'Created Voucher')", conn, tran);
                actionCmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
                actionCmd.Parameters.Add("@UserID", SqlDbType.Int).Value = GetCurrentEmployeeId();
                actionCmd.Parameters.Add("@ComputerName", SqlDbType.NVarChar, 100).Value = Environment.MachineName;
                actionCmd.ExecuteNonQuery();
            }
            else
            {
                flowId = id.Value;

                var currentPoId = GetCurrentPoId(conn, tran, flowId);
                Header.POID = currentPoId;

                var oldDetails = LoadDetailsForTransaction(conn, tran, flowId);
                RollbackDetailEffects(conn, tran, currentPoId, oldDetails);

                using var cmd = new SqlCommand(@"UPDATE dbo.INV_ItemFlow
SET FlowDate=@FlowDate, ToStore=@ToStore, FlowSubType=@FlowSubType, RetDept=@RetDept, RetAssetLocation=@RetLocation, POID=@POID, MoveToCPNStore=@MoveToCPNStore, According=@According, Reason=@Reason, StatusID=@StatusID
WHERE FlowID=@FlowID", conn, tran);
                BindHeaderParams(cmd);
                cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
                cmd.ExecuteNonQuery();

                using var del = new SqlCommand("DELETE FROM dbo.INV_ItemFlowDetail WHERE FlowID=@FlowID", conn, tran);
                del.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
                del.ExecuteNonQuery();
            }

            foreach (var d in Details)
            {
                InsertDetail(conn, tran, flowId, d);
                ApplyDetailEffects(conn, tran, Header.POID, d);
            }

            if (!id.HasValue && SelectedIssueFlowId.HasValue && SelectedIssueFlowId.Value > 0)
            {
                ReceiveFromInventoryIssue(conn, tran, flowId, SelectedIssueFlowId.Value);
            }

            UpdatePriceByDetails(conn, tran, Details);

            foreach (var attachment in AttachmentUploads.Where(file => file != null && file.Length > 0))
            {
                SaveAttachment(conn, tran, flowId, GetCurrentEmployeeId(), Header.FlowNo, Header.FlowDate, attachment, savedFilePaths);
            }

            DeleteAttachmentsInTransaction(conn, tran, flowId, ParseDeletedAttachmentDocIds(DeletedAttachmentDocIdsJson));

            UpdatePoStatus(conn, tran, Header.POID);

            tran.Commit();

            if (ConfirmAfterSave)
            {
                if (Header.FlowSubType == 6)
                {
                    TempData["Message"] = "This Receive Voucher no need to confirm.";
                    TempData["MessageType"] = "info";
                    return RedirectToPage("./Index");
                }
                return ExecuteConfirm(flowId, "edit");
            }

            return RedirectToPage("./Index");
        }
        catch
        {
            tran.Rollback();
            RemoveSavedFiles(savedFilePaths);
            throw;
        }
    }

    public IActionResult OnPost(long? id, string mode)
    {
        return OnPostSave(id, mode);
    }

    public IActionResult OnPostSaveAndConfirm(long? id, string mode)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionApproved)) return Redirect("/");
        ConfirmAfterSave = true;
        return OnPostSave(id, mode);
    }

    public async Task<IActionResult> OnPostConfirm(long id, string mode)
    {
        PagePerm = GetUserPermissions();
        Mode = string.IsNullOrWhiteSpace(mode) ? "edit" : mode.Trim().ToLowerInvariant();
        if (!PagePerm.HasPermission(PermissionApproved)) return Redirect("/");
        if (!EvaluateCanConfirmVoucherBusiness(id))
        {
            TempData["Message"] = "You have no right to confirm this voucher at current status.";
            TempData["MessageType"] = "warning";
            return RedirectToPage("./Index");
        }

        return await ExecuteConfirmAsync(id, Mode);
    }

    private IActionResult ExecuteConfirm(long id, string mode)
        => ExecuteConfirmAsync(id, mode).GetAwaiter().GetResult();

    public IActionResult OnGetReport(long id, bool inline = false)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewDetail)) return Redirect("/");

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        var report = LoadReceivedReport(conn, id);
        if (report == null) return NotFound();

        var pdf = InventoryReceivedPdfReport.BuildPdf(report);
        if (inline)
        {
            Response.Headers["Content-Disposition"] = $"inline; filename={report.FlowNo}.pdf";
            return File(pdf, "application/pdf");
        }

        return File(pdf, "application/pdf", $"{report.FlowNo}.pdf");
    }

    private InventoryReceivedReportModel? LoadReceivedReport(SqlConnection conn, long flowId)
    {
        using var cmd = new SqlCommand(@"SELECT h.FlowNo,h.FlowDate,ISNULL(h.According,'') AS According,ISNULL(h.Reason,'') AS Reason,
       ISNULL(s.StoreName,'') AS StoreName,ISNULL(po.PONo,'') AS PONo,ISNULL(sp.SupplierName,'') AS SupplierName,
       ISNULL(h.StatusID,1) AS StatusID,h.OperatorID
FROM dbo.INV_ItemFlow h
LEFT JOIN dbo.INV_StoreList s ON s.StoreID=h.ToStore
LEFT JOIN dbo.PC_PO po ON po.POID=h.POID
LEFT JOIN dbo.PC_Suppliers sp ON sp.SupplierID=po.SupplierID
WHERE h.FlowID=@FlowID", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;

        var statusId = Convert.ToInt32(rd["StatusID"] == DBNull.Value ? 1 : rd["StatusID"]);
        var operatorId = rd["OperatorID"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["OperatorID"]);
        var report = new InventoryReceivedReportModel
        {
            FlowNo = Convert.ToString(rd["FlowNo"]) ?? string.Empty,
            FlowDateText = Convert.ToDateTime(rd["FlowDate"]).ToString("dd/MM/yyyy"),
            According = Convert.ToString(rd["According"]) ?? string.Empty,
            Reason = Convert.ToString(rd["Reason"]) ?? string.Empty,
            StoreName = Convert.ToString(rd["StoreName"]) ?? string.Empty,
            PONo = Convert.ToString(rd["PONo"]) ?? string.Empty,
            SupplierName = Convert.ToString(rd["SupplierName"]) ?? string.Empty
        };
        rd.Close();

        if (statusId >= 2)
        {
            report.Level2Signature = LoadConfirmSignatureByActionType(conn, flowId, 2) ?? LoadEmployeeSignature(conn, operatorId);
        }

        if (statusId >= 3)
        {
            report.Level3Signature = LoadConfirmSignatureByActionType(conn, flowId, 3);
        }

        if (statusId >= 4)
        {
            report.Level4Signature = LoadConfirmSignatureByActionType(conn, flowId, 4);
        }

        using var detailCmd = new SqlCommand(@"SELECT ISNULL(i.ItemCode,'') AS ItemCode,ISNULL(i.ItemName,'') AS ItemName,ISNULL(dt.Unit,'') AS Unit,
       ISNULL(dt.Doc_Qty,0) AS DocQty,ISNULL(dt.Act_Qty,0) AS ActQty,ISNULL(dt.Amount,0) AS Amount,ISNULL(l.LocationName,'') AS LocationName
FROM dbo.INV_ItemFlowDetail dt
INNER JOIN dbo.INV_ItemList i ON i.ItemID=dt.ItemID
LEFT JOIN dbo.MS_CoLocation l ON l.LocationID=dt.LocationID
WHERE dt.FlowID=@FlowID
ORDER BY i.ItemCode", conn);
        detailCmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        using var detailRd = detailCmd.ExecuteReader();
        while (detailRd.Read())
        {
            report.Items.Add(new InventoryReceivedReportItem
            {
                ItemCode = Convert.ToString(detailRd["ItemCode"]) ?? string.Empty,
                ItemName = Convert.ToString(detailRd["ItemName"]) ?? string.Empty,
                Unit = Convert.ToString(detailRd["Unit"]) ?? string.Empty,
                DocQty = Convert.ToDecimal(detailRd["DocQty"]),
                ActQty = Convert.ToDecimal(detailRd["ActQty"]),
                Amount = Convert.ToDecimal(detailRd["Amount"]),
                LocationName = Convert.ToString(detailRd["LocationName"]) ?? string.Empty
            });
        }

        return report;
    }

    private byte[]? LoadConfirmSignatureByActionType(SqlConnection conn, long flowId, int actionTypeId)
    {
        using var cmd = new SqlCommand(@"SELECT TOP 1 UserID
FROM dbo.INV_ItemFlowAction
WHERE FlowID=@FlowID AND ActionTypeID=@ActionTypeID
ORDER BY TheDateTime DESC", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        cmd.Parameters.Add("@ActionTypeID", SqlDbType.Int).Value = actionTypeId;
        var employeeId = cmd.ExecuteScalar();
        if (employeeId == null || employeeId == DBNull.Value) return null;
        return LoadEmployeeSignature(conn, Convert.ToInt32(employeeId));
    }
    private byte[]? LoadEmployeeSignature(SqlConnection conn, int? employeeId)
    {
        if (!employeeId.HasValue || employeeId.Value <= 0) return null;
        using var cmd = new SqlCommand("SELECT TOP 1 ISNULL(UrlNomalSign,'') FROM dbo.MS_Employee WHERE EmployeeID=@EmployeeID", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId.Value;
        var fileName = Convert.ToString(cmd.ExecuteScalar())?.Trim();
        if (string.IsNullOrWhiteSpace(fileName)) return null;
        var path = ResolveEmployeeSignaturePath(fileName);
        return string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path) ? null : System.IO.File.ReadAllBytes(path);
    }

    private string ResolveEmployeeSignaturePath(string fileName)
    {
        var cleanedFileName = Path.GetFileName(fileName.Trim());
        if (string.IsNullOrWhiteSpace(cleanedFileName)) return string.Empty;
        var basePath = _config.GetValue<string>("FileUploads:BasePath");
        if (string.IsNullOrWhiteSpace(basePath)) return string.Empty;
        var rootPath = Path.IsPathRooted(basePath) ? basePath : Path.Combine(Directory.GetCurrentDirectory(), basePath);
        var functionPath = _config.GetValue<string>("FileUploads:Funtions:18") ?? "Admin/Employee";
        var relativeSegments = functionPath.Replace('\\', '/').Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return Path.Combine(new[] { rootPath }.Concat(relativeSegments).Concat(new[] { cleanedFileName }).ToArray());
    }

    private async Task<IActionResult> ExecuteConfirmAsync(long id, string mode)
    {
        Mode = string.IsNullOrWhiteSpace(mode) ? "edit" : mode.Trim().ToLowerInvariant();

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        var flow = LoadConfirmFlow(conn, id);
        if (flow == null) return RedirectToPage("./Index");

        var receiveLevel = GetEmployeeReceiveVoucherLevel(conn, GetCurrentEmployeeId());
        var returnLevel = GetEmployeeReturnVoucherLevel(conn, GetCurrentEmployeeId());
        var isAdmin = IsCurrentEmployeeAdmin(conn, GetCurrentEmployeeId());
        var nextLevel = flow.StatusID + 1;

        if (flow.KPGroup == 1 && flow.FlowSubType == 1)
        {
            if (flow.StatusID >= 4) return RedirectWithMessage(id, Mode, "This Receive Voucher was Confirmed by Purchaser already", "warning");
            if (!isAdmin && receiveLevel != nextLevel) return RedirectWithMessage(id, Mode, $"You have no right to confirm this voucher at status: {flow.StatusID}", "warning");
            UpdateFlowStatus(conn, id, nextLevel);
            InsertFlowAction(conn, id, nextLevel, GetCurrentEmployeeId(), GetStatusDescriptionForSupplier(nextLevel));
            if (nextLevel < 4)
            {
                NotifyNextByReceiveVoucherLevel(conn, flow.KPGroup, nextLevel + 1, flow.FlowNo);
            }
            TempData["Message"] = "Confirm successfully.";
            TempData["MessageType"] = "success";
            return RedirectToPage("./Index");
        }

        if (flow.FlowSubType == 6)
        {
            TempData["Message"] = flow.KPGroup != 1 && !isAdmin
                ? "This Receive Voucher no need to confirm, It was confirmed on Issue Voucher Company Store Group"
                : "This Receive Voucher no need to confirm.";
            TempData["MessageType"] = "info";
            return RedirectToPage("./Index");
        }

        if (flow.StatusID > 4) return RedirectWithMessage(id, Mode, "This Receive Voucher was Confirmed & Convert by Storeman already", "warning");
        if (!isAdmin && !(returnLevel == nextLevel || (flow.KPGroup == 1 && returnLevel * 2 == nextLevel)))
            return RedirectWithMessage(id, Mode, $"You have no right to confirm this voucher at status: {flow.StatusID}", "warning");

        UpdateFlowStatus(conn, id, nextLevel);
        InsertFlowAction(conn, id, nextLevel, GetCurrentEmployeeId(), null);

        if (nextLevel == 3 && flow.MoveToCPNStore.HasValue && flow.MoveToCPNStore.Value > 0)
        {
            var newFlowNo = GenerateMoveToCpnFlowNo(conn, flow.FlowNo);
            using var moveCmd = new SqlCommand("UPDATE dbo.INV_ItemFlow SET KPGroup=1, FlowNo=@FlowNo, ToStore=@ToStore, Reason=@Reason WHERE FlowID=@FlowID", conn);
            moveCmd.Parameters.Add("@FlowNo", SqlDbType.VarChar, 30).Value = newFlowNo;
            moveCmd.Parameters.Add("@ToStore", SqlDbType.Int).Value = flow.MoveToCPNStore.Value;
            moveCmd.Parameters.Add("@Reason", SqlDbType.NVarChar, 500).Value = $"({flow.FlowNo})";
            moveCmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = id;
            moveCmd.ExecuteNonQuery();
            NotifyNextByReturnVoucherLevel(conn, flow.KPGroup, nextLevel + 1, newFlowNo);
            TempData["Message"] = "Confirm and move to CPN Store successfully.";
            TempData["MessageType"] = "success";
            return RedirectToPage("./Index");
        }

        if (nextLevel < 4)
        {
            NotifyNextByReturnVoucherLevel(conn, GetCurrentKpGroupId(), nextLevel + 1, flow.FlowNo);
        }

        TempData["Message"] = "Confirm successfully.";
        TempData["MessageType"] = "success";
        return RedirectToPage("./Index");
    }

    private ConfirmFlowInfo? LoadConfirmFlow(SqlConnection conn, long flowId)
    {
        using var cmd = new SqlCommand(@"SELECT FlowID, FlowNo, ISNULL(FlowSubType,0) AS FlowSubType, ISNULL(StatusID,1) AS StatusID, ISNULL(KPGroup,0) AS KPGroup, MoveToCPNStore, RetDept
FROM dbo.INV_ItemFlow
WHERE FlowID=@FlowID", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;
        return new ConfirmFlowInfo
        {
            FlowID = Convert.ToInt64(rd["FlowID"]),
            FlowNo = Convert.ToString(rd["FlowNo"]) ?? string.Empty,
            FlowSubType = Convert.ToInt32(rd["FlowSubType"]),
            StatusID = Convert.ToInt32(rd["StatusID"]),
            KPGroup = Convert.ToInt32(rd["KPGroup"]),
            MoveToCPNStore = rd["MoveToCPNStore"] == DBNull.Value ? null : Convert.ToInt32(rd["MoveToCPNStore"]),
            RetDept = rd["RetDept"] == DBNull.Value ? null : Convert.ToInt32(rd["RetDept"])
        };
    }

    private int GetEmployeeReceiveVoucherLevel(SqlConnection conn, int employeeId)
    {
        using var cmd = new SqlCommand("SELECT ISNULL(ReceiveVoucher,0) FROM dbo.MS_Employee WHERE EmployeeID=@EmployeeID", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private int GetEmployeeReturnVoucherLevel(SqlConnection conn, int employeeId)
    {
        using var cmd = new SqlCommand("SELECT ISNULL(ReturnVoucher,0) FROM dbo.MS_Employee WHERE EmployeeID=@EmployeeID", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private bool IsCurrentEmployeeAdmin(SqlConnection conn, int employeeId)
    {
        using var cmd = new SqlCommand("SELECT ISNULL(IsAdminUser,0) FROM dbo.MS_Employee WHERE EmployeeID=@EmployeeID", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) == 1;
    }

    private IActionResult RedirectWithMessage(long id, string mode, string message, string type)
    {
        TempData["Message"] = message;
        TempData["MessageType"] = type;
        return RedirectToPage("./Index");
    }

    private void UpdateFlowStatus(SqlConnection conn, long flowId, int statusId)
    {
        using var cmd = new SqlCommand("UPDATE dbo.INV_ItemFlow SET StatusID=@StatusID WHERE FlowID=@FlowID", conn);
        cmd.Parameters.Add("@StatusID", SqlDbType.Int).Value = statusId;
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        cmd.ExecuteNonQuery();
    }

    private void InsertFlowAction(SqlConnection conn, long flowId, int actionTypeId, int userId, string? des)
    {
        using var cmd = new SqlCommand(@"INSERT INTO dbo.INV_ItemFlowAction(FlowID,UserID,TheDateTime,ActionTypeID,ComputerName,Des)
VALUES(@FlowID,@UserID,GETDATE(),@ActionTypeID,@ComputerName,@Des)", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        cmd.Parameters.Add("@UserID", SqlDbType.Int).Value = userId;
        cmd.Parameters.Add("@ActionTypeID", SqlDbType.Int).Value = actionTypeId;
        cmd.Parameters.Add("@ComputerName", SqlDbType.NVarChar, 128).Value = Environment.MachineName;
        cmd.Parameters.Add("@Des", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(des) ? DBNull.Value : des;
        cmd.ExecuteNonQuery();
    }

    private static string GetStatusDescriptionForSupplier(int level) => level switch
    {
        2 => "Storeman Confirmed",
        3 => "AD Head Checked & Confirmed",
        4 => "Purchaser Checked & Confirmed",
        _ => "Confirmed"
    };

    private void NotifyNextByReceiveVoucherLevel(SqlConnection conn, int storeGr, int level, string flowNo)
    {
        using var cmd = new SqlCommand(@"SELECT TheEmail, EmployeeCode, EmployeeName, ISNULL(Title,'') AS Title FROM dbo.MS_Employee
WHERE ISNULL(TheEmail,'')<>'' AND StoreGR=@StoreGR AND ReceiveVoucher=@Level
ORDER BY ISNULL(IsAdminUser,0) ASC, EmployeeID ASC", conn);
        cmd.Parameters.Add("@StoreGR", SqlDbType.Int).Value = storeGr;
        cmd.Parameters.Add("@Level", SqlDbType.Int).Value = level;
        using var rd = cmd.ExecuteReader();
        var hasRecipient = false;
        while (rd.Read())
        {
            var email = Convert.ToString(rd["TheEmail"]) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(email)) continue;
            hasRecipient = true;
            var code = Convert.ToString(rd["EmployeeCode"]) ?? string.Empty;
            var name = Convert.ToString(rd["EmployeeName"]) ?? string.Empty;
            var title = Convert.ToString(rd["Title"]) ?? string.Empty;
            var recipientLabel = BuildRecipientDisplayName(title, name, code, email);
            var statusName = GetStatusName(level - 1, 1);
            QueueConfirmMail(email, ApplyMailSubjectPrefix($"[Inventory Item Checking] Please approve voucher {flowNo}"), flowNo, "Receive Voucher", true, recipientLabel, code, statusName);
        }
        if (!hasRecipient)
        {
            var fallbackStatusName = GetStatusName(level - 1, 1);
            QueueConfirmMail(NotifyCcEmail, ApplyMailSubjectPrefix($"[Inventory Item Checking] Please approve voucher {flowNo}"), flowNo, "Receive Voucher", false, statusName: fallbackStatusName);
        }
    }

    private void NotifyNextByReturnVoucherLevel(SqlConnection conn, int storeGr, int level, string flowNo)
    {
        using var cmd = new SqlCommand(@"SELECT TheEmail, EmployeeCode, EmployeeName, ISNULL(Title,'') AS Title FROM dbo.MS_Employee
WHERE ISNULL(TheEmail,'')<>'' AND StoreGR=@StoreGR AND ReturnVoucher=@Level
ORDER BY ISNULL(IsAdminUser,0) ASC, EmployeeID ASC", conn);
        cmd.Parameters.Add("@StoreGR", SqlDbType.Int).Value = storeGr;
        cmd.Parameters.Add("@Level", SqlDbType.Int).Value = level;
        using var rd = cmd.ExecuteReader();
        var hasRecipient = false;
        while (rd.Read())
        {
            var email = Convert.ToString(rd["TheEmail"]) ?? string.Empty;
            if (string.IsNullOrWhiteSpace(email)) continue;
            hasRecipient = true;
            var code = Convert.ToString(rd["EmployeeCode"]) ?? string.Empty;
            var name = Convert.ToString(rd["EmployeeName"]) ?? string.Empty;
            var title = Convert.ToString(rd["Title"]) ?? string.Empty;
            var recipientLabel = BuildRecipientDisplayName(title, name, code, email);
            var statusName = GetStatusName(level - 1, 2);
            QueueConfirmMail(email, ApplyMailSubjectPrefix($"[Inventory Item Checking] Please approve voucher {flowNo}"), flowNo, "Return Voucher", true, recipientLabel, code, statusName);
        }
        if (!hasRecipient)
        {
            var fallbackStatusName = GetStatusName(level - 1, 2);
            QueueConfirmMail(NotifyCcEmail, ApplyMailSubjectPrefix($"[Inventory Item Checking] Please approve voucher {flowNo}"), flowNo, "Return Voucher", false, statusName: fallbackStatusName);
        }
    }

    private string GenerateMoveToCpnFlowNo(SqlConnection conn, string oldFlowNo)
    {
        var head = (oldFlowNo ?? string.Empty).Trim();
        if (head.Length < 5) head = DateTime.Now.ToString("yyMM-");
        var prefix = head[..Math.Min(5, head.Length)] + "XRT";
        using var cmd = new SqlCommand("SELECT ISNULL(MAX(CAST(RIGHT(FlowNo,3) AS INT)),0) FROM dbo.INV_ItemFlow WHERE LEFT(FlowNo,8)=@Prefix", conn);
        cmd.Parameters.Add("@Prefix", SqlDbType.VarChar, 8).Value = prefix;
        var next = Convert.ToInt32(cmd.ExecuteScalar() ?? 0) + 1;
        return $"{prefix}-{next:000}";
    }

    private void QueueConfirmMail(string toEmail, string subject, string voucherNo, string voucherType, bool addDefaultCc = true, string recipientLabel = "", string employeeCode = "", string statusName = "")
    {
        var flowId = GetFlowIdByNo(voucherNo);
        var detailUrl = Url.Page("/Inventory/InventoryReceived/InventoryReceivedDetail", values: new { id = flowId, mode = "edit" });
        var absoluteUrl = string.IsNullOrWhiteSpace(detailUrl) ? string.Empty : $"{Request.Scheme}://{Request.Host}{detailUrl}";
        var submittedBy = GetCurrentEmployeeDisplayName();

        _ = Task.Run(async () =>
        {
            try
            {
                await SendConfirmMailAsync(toEmail, subject, voucherNo, voucherType, absoluteUrl, submittedBy, addDefaultCc, recipientLabel, employeeCode, statusName);
            }
            catch
            {
            }
        });
    }

    private async Task SendConfirmMailAsync(string toEmail, string subject, string voucherNo, string voucherType, string absoluteUrl, string submittedBy, bool addDefaultCc = true, string recipientLabel = "", string employeeCode = "", string statusName = "")
    {
        var senderEmail = _config.GetValue<string>("EmailSettings:SenderEmail") ?? string.Empty;
        var password = _config.GetValue<string>("EmailSettings:Password") ?? string.Empty;
        var mailServer = _config.GetValue<string>("EmailSettings:MailServer") ?? string.Empty;
        var mailPort = _config.GetValue<int?>("EmailSettings:MailPort") ?? 0;
        if (string.IsNullOrWhiteSpace(senderEmail) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(mailServer) || mailPort <= 0 || string.IsNullOrWhiteSpace(toEmail))
            return;
        var recipientDisplay = string.IsNullOrWhiteSpace(recipientLabel)
            ? (string.IsNullOrWhiteSpace(employeeCode) ? toEmail : employeeCode)
            : recipientLabel;
        var bodyContent = $@"
<p>Dear {{RECIPIENT_LABEL}},</p>
<p>A {WebUtility.HtmlEncode(voucherType)} has been <b>submitted</b> and is waiting for your approval.</p>
<ul>
    <li>Voucher Type: <b>{WebUtility.HtmlEncode(voucherType)}</b></li>
    <li>Voucher No: <b>{WebUtility.HtmlEncode(voucherNo)}</b></li>
    <li>Status: <b>{WebUtility.HtmlEncode(statusName)}</b></li>
    <li>Submitted by: <b>{WebUtility.HtmlEncode(submittedBy)}</b></li>
    <li>Submit time: <b>{DateTime.Now.ToString("MMM d, yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)}</b></li>
</ul>
<p><b>Click Here to Approve:</b> <a href='{WebUtility.HtmlEncode(absoluteUrl)}'>Open {WebUtility.HtmlEncode(voucherType)}</a></p>
<p>Best regards,<br/>SmartSam System</p>";
        bodyContent = WrapNotifyMessageBody(bodyContent);
        var mailHeaderTitle = voucherType.Contains("Return", StringComparison.OrdinalIgnoreCase)
            ? "APPROVE RETURN VOUCHER"
            : "APPROVE INVENTORY RECEIVED";
        var body = EmailTemplateHelper.WrapInNotifyTemplate(mailHeaderTitle, "#17a2b8", DateTime.Now, bodyContent)
            .Replace("{RECIPIENT_LABEL}", WebUtility.HtmlEncode(recipientDisplay));
        using var mail = new MailMessage
        {
            From = new MailAddress(senderEmail, "SmartSam System"),
            Subject = subject,
            Body = body,
            IsBodyHtml = true
        };
        mail.To.Add(toEmail);
        if (addDefaultCc && !string.Equals(toEmail, NotifyCcEmail, StringComparison.OrdinalIgnoreCase))
        {
            mail.CC.Add(NotifyCcEmail);
        }
        using var smtp = new SmtpClient(mailServer, mailPort)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(senderEmail, password)
        };
        await smtp.SendMailAsync(mail);
    }

    private long GetFlowIdByNo(string flowNo)
    {
        if (string.IsNullOrWhiteSpace(flowNo)) return 0;
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand("SELECT TOP 1 FlowID FROM dbo.INV_ItemFlow WHERE FlowNo=@FlowNo ORDER BY FlowID DESC", conn);
        cmd.Parameters.Add("@FlowNo", SqlDbType.VarChar, 30).Value = flowNo.Trim();
        conn.Open();
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
    }

    private static string WrapNotifyMessageBody(string messageBody) => $"<div style='font-family:Segoe UI,Arial,sans-serif;'>{messageBody}</div>";

    private static string BuildRecipientDisplayName(string title, string employeeName, string employeeCode, string email)
    {
        var titleTrim = (title ?? string.Empty).Trim();
        var nameTrim = string.IsNullOrWhiteSpace(employeeName) ? email : employeeName.Trim();
        if (!string.IsNullOrWhiteSpace(titleTrim))
        {
            return string.IsNullOrWhiteSpace(employeeCode)
                ? $"{titleTrim} {nameTrim}"
                : $"{titleTrim} {nameTrim}({employeeCode})";
        }

        return string.IsNullOrWhiteSpace(employeeCode)
            ? nameTrim
            : $"{nameTrim}({employeeCode})";
    }

    private string GetCurrentEmployeeDisplayName()
    {
        var employeeId = GetCurrentEmployeeId();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand("SELECT TOP 1 ISNULL(Title,'') AS Title, ISNULL(EmployeeName,'') AS EmployeeName, ISNULL(EmployeeCode,'') AS EmployeeCode, ISNULL(TheEmail,'') AS TheEmail FROM dbo.MS_Employee WHERE EmployeeID=@EmployeeID", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId;
        conn.Open();
        using var rd = cmd.ExecuteReader();
        if (rd.Read())
        {
            var title = Convert.ToString(rd["Title"]) ?? string.Empty;
            var employeeName = Convert.ToString(rd["EmployeeName"]) ?? string.Empty;
            var employeeCode = Convert.ToString(rd["EmployeeCode"]) ?? string.Empty;
            var email = Convert.ToString(rd["TheEmail"]) ?? string.Empty;
            return BuildRecipientDisplayName(title, employeeName, employeeCode, email);
        }

        var fallbackCode = GetCurrentEmployeeCode();
        if (!string.IsNullOrWhiteSpace(fallbackCode)) return fallbackCode;
        var fallbackName = User?.Identity?.Name ?? string.Empty;
        return string.IsNullOrWhiteSpace(fallbackName) ? "System" : fallbackName;
    }

    private bool EvaluateCanEditVoucherBusiness()
    {
        if (IsAddMode) return PagePerm.HasPermission(PermissionAdd);
        if (!IsEditMode || !Id.HasValue || !PagePerm.HasPermission(PermissionEdit)) return false;
        if (Header.StatusID > 1) return false;
        return true;
    }

    private bool EvaluateCanConfirmVoucherBusiness(long? flowId = null)
    {
        if (!PagePerm.HasPermission(PermissionApproved)) return false;
        var targetId = flowId ?? Id;
        if (!targetId.HasValue || targetId.Value <= 0) return false;
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        var flow = LoadConfirmFlow(conn, targetId.Value);
        if (flow == null) return false;
        if (flow.KPGroup == 1 && flow.FlowSubType == 1 && flow.StatusID >= 4) return false;
        if (flow.StatusID > 4) return false;
        var isAdmin = IsCurrentEmployeeAdmin(conn, GetCurrentEmployeeId());
        if (flow.FlowSubType == 6) return false;
        var nextLevel = flow.StatusID + 1;
        if (isAdmin) return true;
        if (flow.KPGroup == 1 && flow.FlowSubType == 1)
        {
            return GetEmployeeReceiveVoucherLevel(conn, GetCurrentEmployeeId()) == nextLevel;
        }

        var returnLevel = GetEmployeeReturnVoucherLevel(conn, GetCurrentEmployeeId());
        return returnLevel == nextLevel || (flow.KPGroup == 1 && returnLevel * 2 == nextLevel);
    }

    private string ApplyMailSubjectPrefix(string subject)
    {
        var prefix = _config.GetValue<string>("EmailSettings:PrefixSubject")?.Trim();
        if (string.IsNullOrWhiteSpace(prefix) || !ShouldApplyTestSubjectPrefix()) return subject;
        return $"{prefix} - {subject}";
    }

    private bool ShouldApplyTestSubjectPrefix()
    {
        var configuredIds = _config.GetValue<string>("EmailSettings:TestFunctionIDs");
        if (string.IsNullOrWhiteSpace(configuredIds)) return false;
        return configuredIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Any(x => int.TryParse(x, out var parsed) && parsed == FunctionId);
    }

    public IActionResult OnGetDownloadAttachment(long id, int docId)
    {
        var fileName = GetAttachmentFileName(id, docId);
        if (string.IsNullOrWhiteSpace(fileName)) return NotFound();

        var flow = GetFlowHeader(id);
        if (flow == null) return NotFound();
        var fullPath = Path.Combine(ResolveAttachmentUploadFolder(flow.FlowNo, flow.FlowDate, "ReceiveVoucher"), fileName);
        if (!System.IO.File.Exists(fullPath)) return NotFound();

        return PhysicalFile(fullPath, "application/octet-stream", fileName);
    }

    public IActionResult OnPostDeleteAttachment(long id, int docId)
    {
        return RedirectToPage(new { id, mode = "edit" });
    }

    public JsonResult OnGetReceiveContext(int? flowSubType)
    {
        if (!flowSubType.HasValue || flowSubType.Value <= 0)
        {
            return new JsonResult(new
            {
                statusId = 0,
                statusName = string.Empty,
                flowNo = string.Empty
            });
        }

        var statusId = GetDefaultStatusId(flowSubType);
        var statusName = GetStatusName(statusId, flowSubType);
        var flowNo = GetNextVoucherNo(flowSubType);
        return new JsonResult(new
        {
            statusId,
            statusName,
            flowNo
        });
    }

    public JsonResult OnGetPoItems(int? poId, long? flowId)
    {
        var items = poId.HasValue && poId.Value > 0
            ? LoadItemCatalogByPo(poId.Value, flowId)
            : LoadAllItems();

        return new JsonResult(items.Select(x => new
        {
            id = x.ItemId,
            itemCode = x.ItemCode,
            itemName = x.ItemName,
            unit = x.Unit,
            checkingId = x.ChekingID,
            checkingDtId = x.ChekingDTID,
            mrDetailId = x.MRDetailID,
            remainingQty = x.RemainingQty,
            price = x.Price,
            text = $"{x.ItemCode} - {x.ItemName}"
        }).ToList());
    }

    public JsonResult OnGetIssueReceiveList(string? fromDate, string? toDate)
    {
        var deptId = GetCurrentDeptId();
        var from = DateTime.TryParse(fromDate, out var fd) ? fd.Date : DateTime.Today.AddMonths(-1).Date;
        var to = DateTime.TryParse(toDate, out var td) ? td.Date : DateTime.Today.Date;
        var results = new List<object>();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand(@"SELECT a1.flowID,a1.flowNo,a1.flowDate,a1.According,a2.StoreName,a3.deptName,a4.locationName,a1.FromStore
FROM dbo.INV_ItemFlow a1
LEFT JOIN dbo.INV_StoreList a2 ON a1.FromStore = a2.StoreID
LEFT JOIN dbo.MS_Department a3 ON a1.RecDept = a3.deptID
LEFT JOIN dbo.MS_CoLocation a4 ON a1.AssetLocation = a4.locationID
WHERE a1.flowType = 1 AND a1.FlowSubType = 1 AND a1.DeptRecDocID IS NULL
  AND a1.RecDept = @DeptID
  AND DATEDIFF(DAY, a1.FlowDate, @FromDate) <= 0
  AND DATEDIFF(DAY, a1.FlowDate, @ToDate) >= 0
ORDER BY a1.FlowDate DESC", conn);
        cmd.Parameters.Add("@DeptID", SqlDbType.Int).Value = deptId;
        cmd.Parameters.Add("@FromDate", SqlDbType.Date).Value = from;
        cmd.Parameters.Add("@ToDate", SqlDbType.Date).Value = to;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            results.Add(new
            {
                flowId = Convert.ToInt64(rd["flowID"]),
                flowNo = Convert.ToString(rd["flowNo"]) ?? string.Empty,
                flowDate = Convert.ToDateTime(rd["flowDate"]).ToString("dd/MM/yyyy"),
                according = Convert.ToString(rd["According"]) ?? string.Empty,
                storeName = Convert.ToString(rd["StoreName"]) ?? string.Empty,
                deptName = Convert.ToString(rd["deptName"]) ?? string.Empty,
                locationName = Convert.ToString(rd["locationName"]) ?? string.Empty,
                fromStore = rd["FromStore"] == DBNull.Value ? (int?)null : Convert.ToInt32(rd["FromStore"])
            });
        }
        return new JsonResult(results);
    }

    public JsonResult OnGetIssueReceiveDetails(long flowId)
    {
        var results = new List<object>();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand(@"
SELECT
    ifd.ItemID,
    ISNULL(kp.ItemCode, il.ItemCode) AS ItemCode,
    ISNULL(kp.ItemName, il.ItemName) AS ItemName,
    ifd.Unit,
    ifd.Doc_Qty,
    ifd.Act_Qty,
    ifd.UnitPrice,
    ifd.Amount
FROM dbo.INV_ItemFlowDetail ifd
INNER JOIN dbo.INV_ItemList il ON ifd.ItemID = il.ItemID
LEFT JOIN dbo.INV_KPGroupIndex kp ON ifd.ItemID = kp.ItemID AND kp.KPGroupID = @KPGroupID
WHERE ifd.FlowID = @FlowID", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        cmd.Parameters.Add("@KPGroupID", SqlDbType.Int).Value = GetCurrentKpGroupId();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            results.Add(new
            {
                itemId = rd["ItemID"] == DBNull.Value ? 0 : Convert.ToInt32(rd["ItemID"]),
                itemCode = Convert.ToString(rd["ItemCode"]) ?? string.Empty,
                itemName = Convert.ToString(rd["ItemName"]) ?? string.Empty,
                unit = Convert.ToString(rd["Unit"]) ?? string.Empty,
                docQty = Convert.ToDecimal(rd["Doc_Qty"] == DBNull.Value ? 0 : rd["Doc_Qty"]),
                actQty = Convert.ToDecimal(rd["Act_Qty"] == DBNull.Value ? 0 : rd["Act_Qty"]),
                unitPrice = Convert.ToDecimal(rd["UnitPrice"] == DBNull.Value ? 0 : rd["UnitPrice"]),
                amount = Convert.ToDecimal(rd["Amount"] == DBNull.Value ? 0 : rd["Amount"])
            });
        }
        return new JsonResult(results);
    }

    private int GetCurrentDeptId()
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand("SELECT ISNULL(DeptID,0) FROM dbo.MS_Employee WHERE EmployeeID=@EmployeeID", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = GetCurrentEmployeeId();
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private List<InventoryReceivedDetailRow> ParseDetails(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<InventoryReceivedDetailRow>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new();
        }
        catch { return new(); }
    }

    private void LoadLookups()
    {
        ReceiveTypes = LoadListFromSql("SELECT FlowSubTypeID, FlowSubTypeName FROM dbo.INV_FlowSubType WHERE FlowTypeID=2 ORDER BY FlowSubTypeID", "FlowSubTypeID", "FlowSubTypeName");
        Departments = LoadListFromSql("SELECT DeptID, DeptName FROM dbo.MS_Department ORDER BY DeptName", "DeptID", "DeptName");
        Locations = LoadListFromSql("SELECT LocationID, LocationName FROM dbo.MS_CoLocation ORDER BY LocationName", "LocationID", "LocationName");
        PONos = LoadListFromSql(@"SELECT DISTINCT po.POID, po.PONo
FROM dbo.PC_PO po
INNER JOIN dbo.INV_RecevingChekingVoucher h ON h.POID = po.POID AND h.StatusID = 3
INNER JOIN dbo.INV_ReceivingChekingVoucherDT dt ON dt.CheckingID = h.CheckingID AND ISNULL(dt.QuantityPassed,0) > ISNULL(dt.QuantityRec,0)
ORDER BY po.PONo DESC", "POID", "PONo");
        if (Header.POID.HasValue && Header.POID.Value > 0 && PONos.All(x => x.Value != Header.POID.Value.ToString()))
        {
            var poNo = GetPoNo(Header.POID.Value);
            if (!string.IsNullOrWhiteSpace(poNo))
            {
                PONos.Add(new SelectListItem(poNo, Header.POID.Value.ToString()));
            }
        }
        LoadItemCatalog();
        ItemOptions = ItemCatalog.Select(x => new SelectListItem($"{x.ItemCode} - {x.ItemName}", x.ItemId.ToString())).ToList();
        Stores = BuildStoreTreeOptions();
        Statuses = LoadVoucherStatuses(Header.FlowSubType);
        MoveToCPNStores = LoadListFromSql("select StoreID, StoreName from INV_StoreList where IsCoStore =1 and StoreID in (24,25)", "StoreID", "StoreName");
        Header.StatusName = GetStatusName(Header.StatusID, Header.FlowSubType);

        ReceiveTypes.Insert(0, new SelectListItem("--- Select ---", ""));
        Departments.Insert(0, new SelectListItem("--- Select ---", ""));
        Locations.Insert(0, new SelectListItem("--- Select ---", ""));
        PONos.Insert(0, new SelectListItem("--- Select ---", ""));
        if (Statuses.Count == 0)
        {
            Statuses.Add(new SelectListItem("--- Select ---", ""));
            Statuses.Add(new SelectListItem("1", "1"));
        }
        else
        {
            Statuses.Insert(0, new SelectListItem("--- Select ---", ""));
        }
        ItemOptions.Insert(0, new SelectListItem("--- Select Item ---", ""));
    }

    private List<SelectListItem> LoadVoucherStatuses(int? flowSubType)
    {
        var receiveFrom = GetReceiveFrom(flowSubType, GetCurrentKpGroupId());
        return receiveFrom switch
        {
            "SUPPLIER" => LoadListFromSql("SELECT StatusID, StatusName FROM dbo.INV_ItemFlowReceiveStatus ORDER BY StatusID", "StatusID", "StatusName"),
            "MAIN_STORE" => LoadListFromSql("SELECT StatusID, StatusName FROM dbo.INV_ItemFlowReceiveStatus WHERE StatusID = 1 ORDER BY StatusID", "StatusID", "StatusName"),
            _ => LoadListFromSql("SELECT ID, Name FROM dbo.INV_ItemReturnStatus ORDER BY ID", "ID", "Name")
        };
    }

    private void LoadItemCatalog()
    {
        ItemCatalog = Header.POID.HasValue && Header.POID.Value > 0
            ? LoadItemCatalogByPo(Header.POID.Value, Id)
            : LoadAllItems();
    }

    private List<InventoryReceivedItemOption> LoadAllItems()
    {
        var results = new List<InventoryReceivedItemOption>();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand(@"SELECT ItemID, ItemCode, ItemName, ISNULL(Unit,'') AS Unit
FROM dbo.INV_ItemList
WHERE ISNULL(IsActive,0)=1
ORDER BY ItemCode", conn);
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            results.Add(new InventoryReceivedItemOption
            {
                ItemId = Convert.ToInt32(rd["ItemID"]),
                ItemCode = Convert.ToString(rd["ItemCode"]) ?? string.Empty,
                ItemName = Convert.ToString(rd["ItemName"]) ?? string.Empty,
                Unit = Convert.ToString(rd["Unit"]) ?? string.Empty
            });
        }

        return results;
    }

    private List<InventoryReceivedItemOption> LoadItemCatalogByPo(int poId, long? flowId = null)
    {
        var results = new List<InventoryReceivedItemOption>();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand(@"SELECT h.CheckingID, dt.ChekingDTID, dt.MRDetailID, dt.ItemID, i.ItemCode, i.ItemName,
       ISNULL(i.Unit,'') AS Unit,
       (ISNULL(dt.QuantityPassed, 0) - ISNULL(dt.QuantityRec, 0)) AS RemainingQty,
       ISNULL(dt.Price, 0) AS Price
FROM dbo.INV_RecevingChekingVoucher h
INNER JOIN dbo.INV_ReceivingChekingVoucherDT dt ON h.CheckingID = dt.CheckingID
INNER JOIN dbo.INV_ItemList i ON i.ItemID = dt.ItemID
WHERE h.POID = @POID
  AND h.StatusID = 3
ORDER BY i.ItemCode, dt.ChekingDTID", conn);
        cmd.Parameters.Add("@POID", SqlDbType.Int).Value = poId;
        using (var rd = cmd.ExecuteReader())
        {
            while (rd.Read())
            {
                results.Add(new InventoryReceivedItemOption
                {
                    ItemId = Convert.ToInt32(rd["ItemID"]),
                    ItemCode = Convert.ToString(rd["ItemCode"]) ?? string.Empty,
                    ItemName = Convert.ToString(rd["ItemName"]) ?? string.Empty,
                    Unit = Convert.ToString(rd["Unit"]) ?? string.Empty,
                    ChekingID = rd["CheckingID"] == DBNull.Value ? null : Convert.ToInt64(rd["CheckingID"]),
                    ChekingDTID = rd["ChekingDTID"] == DBNull.Value ? null : Convert.ToInt64(rd["ChekingDTID"]),
                    MRDetailID = rd["MRDetailID"] == DBNull.Value ? null : Convert.ToInt64(rd["MRDetailID"]),
                    RemainingQty = Convert.ToDecimal(rd["RemainingQty"] == DBNull.Value ? 0 : rd["RemainingQty"]),
                    Price = Convert.ToDecimal(rd["Price"] == DBNull.Value ? 0 : rd["Price"])
                });
            }
        }

        if (flowId.HasValue && flowId.Value > 0)
        {
            using var usedCmd = new SqlCommand(@"SELECT ChekingDTID, ISNULL(SUM(ISNULL(Act_Qty,0)),0) AS UsedQty
FROM dbo.INV_ItemFlowDetail
WHERE FlowID = @FlowID AND ChekingDTID IS NOT NULL
GROUP BY ChekingDTID", conn);
            usedCmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId.Value;
            using var usedRd = usedCmd.ExecuteReader();
            var usedMap = new Dictionary<long, decimal>();
            while (usedRd.Read())
            {
                var dtId = Convert.ToInt64(usedRd["ChekingDTID"]);
                var qty = Convert.ToDecimal(usedRd["UsedQty"] == DBNull.Value ? 0 : usedRd["UsedQty"]);
                usedMap[dtId] = qty;
            }

            foreach (var item in results)
            {
                if (item.ChekingDTID.HasValue && usedMap.TryGetValue(item.ChekingDTID.Value, out var usedQty))
                {
                    item.RemainingQty += usedQty;
                }
            }
        }

        return results
            .Where(x => x.RemainingQty > 0)
            .ToList();
    }

    private List<SelectListItem> BuildStoreTreeOptions()
    {
        var results = new List<SelectListItem>();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand(@"SELECT g.KPGroupID, g.KPGroupName, s.StoreID, s.StoreName
FROM dbo.INV_KPGroup g
INNER JOIN dbo.INV_StoreList s ON s.DeptID = g.KPGroupID
ORDER BY g.KPGroupName, s.StoreName", conn);
        using var rd = cmd.ExecuteReader();
        var groupMap = new Dictionary<int, SelectListGroup>();
        while (rd.Read())
        {
            var groupId = Convert.ToInt32(rd["KPGroupID"]);
            if (!groupMap.ContainsKey(groupId)) groupMap[groupId] = new SelectListGroup { Name = Convert.ToString(rd["KPGroupName"]) ?? "" };
            results.Add(new SelectListItem
            {
                Value = Convert.ToString(rd["StoreID"]),
                Text = Convert.ToString(rd["StoreName"]) ?? "",
                Group = groupMap[groupId]
            });
        }
        results.Insert(0, new SelectListItem("--- Select ---", ""));
        return results;
    }

    private void LoadHeader(long id)
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand("SELECT FlowID,FlowNo,FlowDate,ToStore,FlowSubType,RetDept,RetAssetLocation,POID,MoveToCPNStore,According,Reason,StatusID FROM dbo.INV_ItemFlow WHERE FlowID=@FlowID", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = id;
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return;
        Header = new InventoryReceivedHeader
        {
            FlowID = Convert.ToInt64(rd["FlowID"]),
            FlowNo = Convert.ToString(rd["FlowNo"]) ?? string.Empty,
            FlowDate = Convert.ToDateTime(rd["FlowDate"]),
            ToStore = rd["ToStore"] == DBNull.Value ? null : Convert.ToInt32(rd["ToStore"]),
            FlowSubType = rd["FlowSubType"] == DBNull.Value ? null : Convert.ToInt32(rd["FlowSubType"]),
            RetDept = rd["RetDept"] == DBNull.Value ? null : Convert.ToInt32(rd["RetDept"]),
            RetAssetLocation = rd["RetAssetLocation"] == DBNull.Value ? null : Convert.ToInt32(rd["RetAssetLocation"]),
            POID = rd["POID"] == DBNull.Value ? null : Convert.ToInt32(rd["POID"]),
            MoveToCPNStore = rd["MoveToCPNStore"] == DBNull.Value ? null : Convert.ToInt32(rd["MoveToCPNStore"]),
            According = Convert.ToString(rd["According"]) ?? string.Empty,
            Reason = Convert.ToString(rd["Reason"]) ?? string.Empty,
            StatusID = rd["StatusID"] == DBNull.Value ? 1 : Convert.ToInt32(rd["StatusID"])
        };
        Header.StatusName = GetStatusName(Header.StatusID, Header.FlowSubType);
    }

    private string GetStatusName(int statusId, int? flowSubType)
    {
        if (statusId <= 0) return string.Empty;
        var statuses = LoadVoucherStatuses(flowSubType);
        return statuses.FirstOrDefault(x => x.Value == statusId.ToString())?.Text ?? string.Empty;
    }

    private int GetDefaultStatusId(int? flowSubType)
    {
        var statuses = LoadVoucherStatuses(flowSubType);
        if (statuses.Count == 0) return 1;
        return int.TryParse(statuses[0].Value, out var statusId) && statusId > 0 ? statusId : 1;
    }

    private void LoadDetails(long id)
    {
        Details.Clear();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand(@"SELECT d.ItemID,d.ChekingID,d.ChekingDTID,d.MRDetailID,i.ItemCode,i.ItemName,d.Unit,d.Doc_Qty,d.Act_Qty,d.UnitPrice,d.Amount,d.LocationID,l.LocationName
FROM dbo.INV_ItemFlowDetail d
INNER JOIN dbo.INV_ItemList i ON i.ItemID=d.ItemID
LEFT JOIN dbo.MS_CoLocation l ON l.LocationID=d.LocationID
WHERE d.FlowID=@FlowID
ORDER BY i.ItemCode", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = id;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            Details.Add(new InventoryReceivedDetailRow
            {
                ItemId = Convert.ToInt32(rd["ItemID"]),
                ChekingID = rd["ChekingID"] == DBNull.Value ? null : Convert.ToInt64(rd["ChekingID"]),
                ChekingDTID = rd["ChekingDTID"] == DBNull.Value ? null : Convert.ToInt64(rd["ChekingDTID"]),
                MRDetailID = rd["MRDetailID"] == DBNull.Value ? null : Convert.ToInt64(rd["MRDetailID"]),
                ItemCode = Convert.ToString(rd["ItemCode"]) ?? "",
                ItemName = Convert.ToString(rd["ItemName"]) ?? "",
                Unit = Convert.ToString(rd["Unit"]) ?? "",
                DocQty = Convert.ToDecimal(rd["Doc_Qty"] == DBNull.Value ? 0 : rd["Doc_Qty"]),
                ActQty = Convert.ToDecimal(rd["Act_Qty"] == DBNull.Value ? 0 : rd["Act_Qty"]),
                UnitPrice = Convert.ToDecimal(rd["UnitPrice"] == DBNull.Value ? 0 : rd["UnitPrice"]),
                Amount = Convert.ToDecimal(rd["Amount"] == DBNull.Value ? 0 : rd["Amount"]),
                LocationId = rd["LocationID"] == DBNull.Value ? null : Convert.ToInt32(rd["LocationID"]),
                LocationName = Convert.ToString(rd["LocationName"]) ?? ""
            });
        }
    }

    private void ValidateBusinessRules(long? flowId)
    {
        if (string.IsNullOrWhiteSpace(Header.FlowNo))
        {
            ModelState.AddModelError("Header.FlowNo", "Doc No. is required.");
        }

        if (!Header.FlowSubType.HasValue || Header.FlowSubType.Value <= 0)
        {
            ModelState.AddModelError("Header.FlowSubType", "Received Type is required.");
        }

        if (!Header.ToStore.HasValue || Header.ToStore.Value <= 0)
        {
            ModelState.AddModelError("Header.ToStore", "Enter To is required.");
        }

        if (!IsPoAllowedForFlowSubType(Header.FlowSubType))
        {
            Header.POID = null;
        }

        if (Header.FlowSubType == 1)
        {
            Header.MoveToCPNStore = null;
        }
        else if (Header.FlowSubType.HasValue && Header.FlowSubType.Value > 0 && Header.FlowSubType.Value != 6)
        {
            Header.POID = null;
            Header.RetDept = null;
            Header.RetAssetLocation = null;

            if (!Header.MoveToCPNStore.HasValue || Header.MoveToCPNStore.Value <= 0)
            {
                ModelState.AddModelError("Header.MoveToCPNStore", "Move to Company Store is required.");
            }
        }

        if (Details == null || Details.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Please add at least one detail item.");
            return;
        }

        var sourceKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (Header.POID.HasValue && Header.POID.Value > 0)
        {
            foreach (var detail in Details)
            {
                var key = detail.ChekingDTID.HasValue && detail.ChekingDTID.Value > 0
                    ? $"checking:{detail.ChekingDTID.Value}"
                    : $"item:{detail.ItemId}";
                if (!sourceKeys.Add(key))
                {
                    ModelState.AddModelError(string.Empty, $"Item '{detail.ItemCode}' is duplicated.");
                }

                if (!detail.ChekingDTID.HasValue || detail.ChekingDTID.Value <= 0)
                {
                    ModelState.AddModelError(string.Empty, $"Item '{detail.ItemCode}' is missing checking detail link.");
                }

                if (!detail.MRDetailID.HasValue || detail.MRDetailID.Value <= 0)
                {
                    ModelState.AddModelError(string.Empty, $"Item '{detail.ItemCode}' is missing MR detail link.");
                }

                if (detail.ActQty <= 0)
                {
                    ModelState.AddModelError(string.Empty, $"Item '{detail.ItemCode}' must have Act. Qty > 0.");
                }

                if (detail.DocQty < detail.ActQty)
                {
                    ModelState.AddModelError(string.Empty, $"Item '{detail.ItemCode}' Doc. Qty must be greater than or equal Act. Qty.");
                }

                var remainingQty = GetRemainingQty(detail.ChekingDTID);
                if (flowId.HasValue)
                {
                    remainingQty += GetCurrentReceivedQty(flowId.Value, detail.ChekingDTID);
                }
                if (detail.ActQty > remainingQty)
                {
                    ModelState.AddModelError(string.Empty, $"Item '{detail.ItemCode}' Act. Qty exceeds remaining qty ({remainingQty:0}).");
                }
            }
        }
        else
        {
            foreach (var detail in Details)
            {
                if (detail.ActQty <= 0)
                {
                    ModelState.AddModelError(string.Empty, $"Item '{detail.ItemCode}' must have Act. Qty > 0.");
                }
                if (detail.DocQty < detail.ActQty)
                {
                    ModelState.AddModelError(string.Empty, $"Item '{detail.ItemCode}' Doc. Qty must be greater than or equal Act. Qty.");
                }
            }
        }
    }

    private static bool IsPoAllowedForFlowSubType(int? flowSubType)
    {
        return flowSubType.HasValue && flowSubType.Value == 1;
    }

    private decimal GetRemainingQty(long? chekingDtId)
    {
        if (!chekingDtId.HasValue || chekingDtId.Value <= 0)
        {
            return 0;
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand(@"SELECT ISNULL(QuantityPassed,0) - ISNULL(QuantityRec,0)
FROM dbo.INV_ReceivingChekingVoucherDT
WHERE ChekingDTID = @ChekingDTID", conn);
        cmd.Parameters.Add("@ChekingDTID", SqlDbType.BigInt).Value = chekingDtId.Value;
        return Convert.ToDecimal(cmd.ExecuteScalar() ?? 0);
    }

    private decimal GetCurrentReceivedQty(long flowId, long? chekingDtId)
    {
        if (!chekingDtId.HasValue || chekingDtId.Value <= 0)
        {
            return 0;
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand(@"SELECT ISNULL(SUM(ISNULL(Act_Qty,0)),0)
FROM dbo.INV_ItemFlowDetail
WHERE FlowID = @FlowID AND ChekingDTID = @ChekingDTID", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        cmd.Parameters.Add("@ChekingDTID", SqlDbType.BigInt).Value = chekingDtId.Value;
        return Convert.ToDecimal(cmd.ExecuteScalar() ?? 0);
    }

    private string GetPoNo(int poId)
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand("SELECT PONo FROM dbo.PC_PO WHERE POID=@POID", conn);
        cmd.Parameters.Add("@POID", SqlDbType.Int).Value = poId;
        return Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
    }

    private int? GetCurrentPoId(SqlConnection conn, SqlTransaction tran, long flowId)
    {
        using var cmd = new SqlCommand("SELECT POID FROM dbo.INV_ItemFlow WHERE FlowID=@FlowID", conn, tran);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        var value = cmd.ExecuteScalar();
        return value == null || value == DBNull.Value ? null : Convert.ToInt32(value);
    }

    private List<InventoryReceivedDetailRow> LoadDetailsForTransaction(SqlConnection conn, SqlTransaction tran, long flowId)
    {
        var results = new List<InventoryReceivedDetailRow>();
        using var cmd = new SqlCommand(@"SELECT ItemID, ChekingID, ChekingDTID, MRDetailID, Doc_Qty, Act_Qty, UnitPrice, Amount, LocationID
FROM dbo.INV_ItemFlowDetail
WHERE FlowID = @FlowID", conn, tran);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            results.Add(new InventoryReceivedDetailRow
            {
                ItemId = Convert.ToInt32(rd["ItemID"]),
                ChekingID = rd["ChekingID"] == DBNull.Value ? null : Convert.ToInt64(rd["ChekingID"]),
                ChekingDTID = rd["ChekingDTID"] == DBNull.Value ? null : Convert.ToInt64(rd["ChekingDTID"]),
                MRDetailID = rd["MRDetailID"] == DBNull.Value ? null : Convert.ToInt64(rd["MRDetailID"]),
                DocQty = Convert.ToDecimal(rd["Doc_Qty"] == DBNull.Value ? 0 : rd["Doc_Qty"]),
                ActQty = Convert.ToDecimal(rd["Act_Qty"] == DBNull.Value ? 0 : rd["Act_Qty"]),
                UnitPrice = Convert.ToDecimal(rd["UnitPrice"] == DBNull.Value ? 0 : rd["UnitPrice"]),
                Amount = Convert.ToDecimal(rd["Amount"] == DBNull.Value ? 0 : rd["Amount"]),
                LocationId = rd["LocationID"] == DBNull.Value ? null : Convert.ToInt32(rd["LocationID"])
            });
        }
        return results;
    }

    private void InsertDetail(SqlConnection conn, SqlTransaction tran, long flowId, InventoryReceivedDetailRow d)
    {
        using var ins = new SqlCommand(@"INSERT INTO dbo.INV_ItemFlowDetail(ItemID,Unit,Doc_Qty,Act_Qty,UnitPrice,Amount,FlowID,LocationID,ChekingID,ChekingDTID,MRDetailID)
SELECT @ItemID, ISNULL(i.Unit,''), @DocQty, @ActQty, @UnitPrice, @Amount, @FlowID, @LocationID, @ChekingID, @ChekingDTID, @MRDetailID
FROM dbo.INV_ItemList i WHERE i.ItemID=@ItemID", conn, tran);
        ins.Parameters.Add("@ItemID", SqlDbType.Int).Value = d.ItemId;
        ins.Parameters.Add("@DocQty", SqlDbType.Decimal).Value = d.DocQty;
        ins.Parameters.Add("@ActQty", SqlDbType.Decimal).Value = d.ActQty;
        ins.Parameters.Add("@UnitPrice", SqlDbType.Decimal).Value = d.UnitPrice;
        ins.Parameters.Add("@Amount", SqlDbType.Decimal).Value = d.ActQty * d.UnitPrice;
        ins.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        ins.Parameters.Add("@LocationID", SqlDbType.Int).Value = d.LocationId > 0 ? d.LocationId.Value : DBNull.Value;
        ins.Parameters.Add("@ChekingID", SqlDbType.BigInt).Value = d.ChekingID.HasValue ? d.ChekingID.Value : DBNull.Value;
        ins.Parameters.Add("@ChekingDTID", SqlDbType.BigInt).Value = d.ChekingDTID.HasValue ? d.ChekingDTID.Value : DBNull.Value;
        ins.Parameters.Add("@MRDetailID", SqlDbType.BigInt).Value = d.MRDetailID.HasValue ? d.MRDetailID.Value : DBNull.Value;
        ins.ExecuteNonQuery();
    }

    private void ApplyDetailEffects(SqlConnection conn, SqlTransaction tran, int? poId, InventoryReceivedDetailRow d)
    {
        if (!d.ChekingDTID.HasValue || d.ChekingDTID.Value <= 0)
        {
            return;
        }

        using (var updateChecking = new SqlCommand(@"UPDATE dbo.INV_ReceivingChekingVoucherDT
SET QuantityRec = ISNULL(QuantityRec,0) + @RecQty
WHERE ChekingDTID = @ChekingDTID", conn, tran))
        {
            updateChecking.Parameters.Add("@RecQty", SqlDbType.Decimal).Value = d.ActQty;
            updateChecking.Parameters.Add("@ChekingDTID", SqlDbType.BigInt).Value = d.ChekingDTID.Value;
            updateChecking.ExecuteNonQuery();
        }

        if (poId.HasValue && poId.Value > 0 && d.MRDetailID.HasValue && d.MRDetailID.Value > 0)
        {
            using var updatePoDetail = new SqlCommand(@"UPDATE dbo.PC_PODetail
SET RecQty = ISNULL(RecQty,0) + @RecQty,
    RecAmount = ISNULL(RecAmount,0) + @RecAmount,
    IsReceived = 1
WHERE POID = @POID AND ItemID = @ItemID AND MRDetailID = @MRDetailID", conn, tran);
            updatePoDetail.Parameters.Add("@RecQty", SqlDbType.Decimal).Value = d.ActQty;
            updatePoDetail.Parameters.Add("@RecAmount", SqlDbType.Decimal).Value = d.ActQty * d.UnitPrice;
            updatePoDetail.Parameters.Add("@POID", SqlDbType.Int).Value = poId.Value;
            updatePoDetail.Parameters.Add("@ItemID", SqlDbType.Int).Value = d.ItemId;
            updatePoDetail.Parameters.Add("@MRDetailID", SqlDbType.BigInt).Value = d.MRDetailID.Value;
            updatePoDetail.ExecuteNonQuery();
        }
    }

    private void RollbackDetailEffects(SqlConnection conn, SqlTransaction tran, int? poId, List<InventoryReceivedDetailRow> details)
    {
        foreach (var d in details)
        {
            if (d.ChekingDTID.HasValue && d.ChekingDTID.Value > 0)
            {
                using var updateChecking = new SqlCommand(@"UPDATE dbo.INV_ReceivingChekingVoucherDT
SET QuantityRec = CASE WHEN ISNULL(QuantityRec,0) - @RecQty < 0 THEN 0 ELSE ISNULL(QuantityRec,0) - @RecQty END
WHERE ChekingDTID = @ChekingDTID", conn, tran);
                updateChecking.Parameters.Add("@RecQty", SqlDbType.Decimal).Value = d.ActQty;
                updateChecking.Parameters.Add("@ChekingDTID", SqlDbType.BigInt).Value = d.ChekingDTID.Value;
                updateChecking.ExecuteNonQuery();
            }

            if (poId.HasValue && poId.Value > 0 && d.MRDetailID.HasValue && d.MRDetailID.Value > 0)
            {
                using var updatePoDetail = new SqlCommand(@"UPDATE dbo.PC_PODetail
SET RecQty = CASE WHEN ISNULL(RecQty,0) - @RecQty < 0 THEN 0 ELSE ISNULL(RecQty,0) - @RecQty END,
    RecAmount = CASE WHEN ISNULL(RecAmount,0) - @RecAmount < 0 THEN 0 ELSE ISNULL(RecAmount,0) - @RecAmount END
WHERE POID = @POID AND ItemID = @ItemID AND MRDetailID = @MRDetailID", conn, tran);
                updatePoDetail.Parameters.Add("@RecQty", SqlDbType.Decimal).Value = d.ActQty;
                updatePoDetail.Parameters.Add("@RecAmount", SqlDbType.Decimal).Value = d.ActQty * d.UnitPrice;
                updatePoDetail.Parameters.Add("@POID", SqlDbType.Int).Value = poId.Value;
                updatePoDetail.Parameters.Add("@ItemID", SqlDbType.Int).Value = d.ItemId;
                updatePoDetail.Parameters.Add("@MRDetailID", SqlDbType.BigInt).Value = d.MRDetailID.Value;
                updatePoDetail.ExecuteNonQuery();

                using var syncReceived = new SqlCommand(@"UPDATE dbo.PC_PODetail
SET IsReceived = CASE WHEN ISNULL(RecQty,0) > 0 THEN 1 ELSE 0 END
WHERE POID = @POID AND ItemID = @ItemID AND MRDetailID = @MRDetailID", conn, tran);
                syncReceived.Parameters.Add("@POID", SqlDbType.Int).Value = poId.Value;
                syncReceived.Parameters.Add("@ItemID", SqlDbType.Int).Value = d.ItemId;
                syncReceived.Parameters.Add("@MRDetailID", SqlDbType.BigInt).Value = d.MRDetailID.Value;
                syncReceived.ExecuteNonQuery();
            }
        }

        UpdatePoStatus(conn, tran, poId);
    }

    private void UpdatePoStatus(SqlConnection conn, SqlTransaction tran, int? poId)
    {
        if (!poId.HasValue || poId.Value <= 0)
        {
            return;
        }

        decimal totalQty;
        decimal recQty;

        using (var cmd = new SqlCommand(@"SELECT ISNULL(SUM(ISNULL(Quantity,0)),0), ISNULL(SUM(ISNULL(RecQty,0)),0)
FROM dbo.PC_PODetail
WHERE POID = @POID", conn, tran))
        {
            cmd.Parameters.Add("@POID", SqlDbType.Int).Value = poId.Value;
            using var rd = cmd.ExecuteReader();
            rd.Read();
            totalQty = rd.IsDBNull(0) ? 0 : Convert.ToDecimal(rd.GetValue(0));
            recQty = rd.IsDBNull(1) ? 0 : Convert.ToDecimal(rd.GetValue(1));
        }

        var statusId = recQty >= totalQty && totalQty > 0 ? 7 : recQty > 0 ? 6 : 5;
        using var update = new SqlCommand("UPDATE dbo.PC_PO SET StatusID = @StatusID WHERE POID = @POID", conn, tran);
        update.Parameters.Add("@StatusID", SqlDbType.Int).Value = statusId;
        update.Parameters.Add("@POID", SqlDbType.Int).Value = poId.Value;
        update.ExecuteNonQuery();
    }

    private void ReceiveFromInventoryIssue(SqlConnection conn, SqlTransaction tran, long flowReceiveId, long flowIssueId)
    {
        using (var lockIssue = new SqlCommand("SELECT ISNULL(DeptRecDocID,0) FROM dbo.INV_ItemFlow WHERE FlowID=@FlowID", conn, tran))
        {
            lockIssue.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowIssueId;
            var linkedId = Convert.ToInt64(lockIssue.ExecuteScalar() ?? 0L);
            if (linkedId > 0)
            {
                throw new InvalidOperationException("Selected Inventory Issue has already been received.");
            }
        }

        using (var cmd = new SqlCommand("UPDATE dbo.INV_ItemFlow SET DeptRecDocID = @FlowReceiveID, StatusID = 3 WHERE FlowID = @FlowIssueID", conn, tran))
        {
            cmd.Parameters.Add("@FlowReceiveID", SqlDbType.BigInt).Value = flowReceiveId;
            cmd.Parameters.Add("@FlowIssueID", SqlDbType.BigInt).Value = flowIssueId;
            cmd.ExecuteNonQuery();
        }

        using (var cmd = new SqlCommand(@"INSERT INTO dbo.INV_ItemFlowAction(FlowID,UserID,TheDateTime,ActionTypeID,ComputerName,Des)
VALUES(@FlowID,@UserID,GETDATE(),3,@ComputerName,'Receiver Confirmed')", conn, tran))
        {
            cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowIssueId;
            cmd.Parameters.Add("@UserID", SqlDbType.Int).Value = GetCurrentEmployeeId();
            cmd.Parameters.Add("@ComputerName", SqlDbType.NVarChar, 128).Value = Environment.MachineName;
            cmd.ExecuteNonQuery();
        }
    }

    private void BindHeaderParams(SqlCommand cmd)
    {
        cmd.Parameters.Add("@KPGroup", SqlDbType.Int).Value = GetCurrentKpGroupId();
        cmd.Parameters.Add("@OperatorId", SqlDbType.Int).Value = GetCurrentEmployeeId();
        cmd.Parameters.Add("@FlowNo", SqlDbType.NVarChar, 30).Value = Header.FlowNo;
        cmd.Parameters.Add("@FlowDate", SqlDbType.Date).Value = Header.FlowDate.Date;
        cmd.Parameters.Add("@ToStore", SqlDbType.Int).Value = Header.ToStore.HasValue && Header.ToStore > 0 ? Header.ToStore.Value : DBNull.Value;
        cmd.Parameters.Add("@FlowSubType", SqlDbType.Int).Value = Header.FlowSubType.HasValue && Header.FlowSubType > 0 ? Header.FlowSubType.Value : DBNull.Value;
        cmd.Parameters.Add("@RetDept", SqlDbType.Int).Value = Header.RetDept.HasValue && Header.RetDept > 0 ? Header.RetDept.Value : DBNull.Value;
        cmd.Parameters.Add("@RetLocation", SqlDbType.Int).Value = Header.RetAssetLocation.HasValue && Header.RetAssetLocation > 0 ? Header.RetAssetLocation.Value : DBNull.Value;
        cmd.Parameters.Add("@POID", SqlDbType.Int).Value = Header.POID.HasValue && Header.POID > 0 ? Header.POID.Value : DBNull.Value;
        cmd.Parameters.Add("@MoveToCPNStore", SqlDbType.Int).Value = Header.MoveToCPNStore.HasValue && Header.MoveToCPNStore > 0 ? Header.MoveToCPNStore.Value : DBNull.Value;
        cmd.Parameters.Add("@According", SqlDbType.NVarChar, 250).Value = string.IsNullOrWhiteSpace(Header.According) ? DBNull.Value : Header.According.Trim();
        cmd.Parameters.Add("@Reason", SqlDbType.NVarChar, 500).Value = string.IsNullOrWhiteSpace(Header.Reason) ? DBNull.Value : Header.Reason.Trim();
        cmd.Parameters.Add("@StatusID", SqlDbType.Int).Value = Header.StatusID > 0 ? Header.StatusID : 1;
    }

    private string GetNextVoucherNo(int? flowSubType)
    {
        var now = DateTime.Now;
        var receiveFrom = GetReceiveFrom(flowSubType, GetCurrentKpGroupId());
        var prefix = BuildVoucherPrefix(now, GetCurrentKpGroupId(), receiveFrom);
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        using var cmd = new SqlCommand("SELECT ISNULL(MAX(RIGHT(FlowNo,3)),'000') FROM dbo.INV_ItemFlow WHERE MONTH(FlowDate)=@Month AND YEAR(FlowDate)=@Year AND LEFT(FlowNo,8)=@Prefix", conn);
        cmd.Parameters.Add("@Month", SqlDbType.Int).Value = now.Month;
        cmd.Parameters.Add("@Year", SqlDbType.Int).Value = now.Year;
        cmd.Parameters.Add("@Prefix", SqlDbType.NVarChar, 8).Value = prefix;
        var seq = Convert.ToInt32(cmd.ExecuteScalar() ?? 0) + 1;
        return $"{prefix}-{seq:000}";
    }

    private static string GetReceiveFrom(int? flowSubType, int kpGroupId)
    {
        if (!flowSubType.HasValue) return string.Empty;
        return flowSubType.Value switch
        {
            1 => "SUPPLIER",
            6 when kpGroupId != 1 => "MAIN_STORE",
            _ => string.Empty
        };
    }

    private static string BuildVoucherPrefix(DateTime now, int kpGroupId, string receiveFrom)
    {
        var basePrefix = now.ToString("yyMM");
        if (kpGroupId == 1)
        {
            return receiveFrom == "SUPPLIER" ? $"{basePrefix}-XRC" : $"{basePrefix}-XRT";
        }

        var groupCode = kpGroupId switch
        {
            2 => "T",
            4 => "H",
            6 => "A",
            12 => "C",
            13 => "F",
            14 => "M",
            16 => "S",
            _ => "T"
        };
        var flowCode = receiveFrom == "MAIN_STORE" ? "RC" : "RT";
        return $"{basePrefix}-{groupCode}{flowCode}";
    }

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

    private void SaveAttachment(SqlConnection conn, SqlTransaction tran, long flowId, int? userId, string voucherNo, DateTime voucherDate, IFormFile file, List<string> savedFilePaths)
    {
        var uploadFolder = ResolveAttachmentUploadFolder(voucherNo, voucherDate, "ReceiveVoucher");
        Directory.CreateDirectory(uploadFolder);

        var savedFileName = BuildAttachmentFileName(file.FileName);
        var fullPath = Path.Combine(uploadFolder, savedFileName);
        using (var stream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            file.CopyTo(stream);
        }

        savedFilePaths.Add(fullPath);

        using var cmd = new SqlCommand(@"
INSERT INTO dbo.INV_ItemFlow_Doc
(
    FlowID,
    FilePath,
    UploadDate,
    UserID
)
VALUES
(
    @FlowID,
    @FilePath,
    GETDATE(),
    @UserID
)", conn, tran);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        cmd.Parameters.Add("@FilePath", SqlDbType.NVarChar, 1000).Value = savedFileName;
        cmd.Parameters.Add("@UserID", SqlDbType.Int).Value = userId.HasValue ? userId.Value : DBNull.Value;
        cmd.ExecuteNonQuery();
    }

    private string ResolveAttachmentUploadFolder(string voucherNo, DateTime voucherDate, string voucherFolder)
    {
        var basePath = _config.GetValue<string>("FileUploads:BasePath");
        if (string.IsNullOrWhiteSpace(basePath))
        {
            throw new InvalidOperationException("FileUploads:BasePath is missing in appsettings.json.");
        }

        var rootPath = Path.IsPathRooted(basePath)
            ? basePath
            : Path.Combine(Directory.GetCurrentDirectory(), basePath);
        var configuredFunctionPath = _config.GetValue<string>($"FileUploads:Funtions:{FunctionId}");
        if (string.IsNullOrWhiteSpace(configuredFunctionPath))
        {
            throw new InvalidOperationException($"FileUploads:Funtions:{FunctionId} is missing in appsettings.json.");
        }

        var relativeSegments = configuredFunctionPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var voucherNoClean = (voucherNo ?? string.Empty).Trim().Replace("/", string.Empty);
        return Path.Combine([rootPath, .. relativeSegments, voucherFolder, voucherDate.Year.ToString(), voucherNoClean]);
    }

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
            }
        }
    }

    private List<InventoryReceivedAttachmentViewModel> LoadAttachmentRows(long flowId)
    {
        var rows = new List<InventoryReceivedAttachmentViewModel>();
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand(@"SELECT d.DocID,d.FlowID,d.FilePath,d.UploadDate,d.UserID,ISNULL(e.EmployeeCode,'') AS EmployeeCode
FROM dbo.INV_ItemFlow_Doc d
LEFT JOIN dbo.MS_Employee e ON d.UserID = e.EmployeeID
WHERE d.FlowID = @FlowID
ORDER BY d.UploadDate DESC, d.DocID DESC", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        conn.Open();
        using var rd = cmd.ExecuteReader();
        while (rd.Read())
        {
            rows.Add(new InventoryReceivedAttachmentViewModel
            {
                DocId = Convert.ToInt32(rd["DocID"]),
                FlowId = Convert.ToInt64(rd["FlowID"]),
                FilePath = Convert.ToString(rd["FilePath"]) ?? string.Empty,
                UploadDate = rd["UploadDate"] == DBNull.Value ? null : Convert.ToDateTime(rd["UploadDate"]),
                UserId = rd["UserID"] == DBNull.Value ? null : Convert.ToInt32(rd["UserID"]),
                EmployeeCode = Convert.ToString(rd["EmployeeCode"]) ?? string.Empty
            });
        }
        return rows;
    }

    private string? GetAttachmentFileName(long flowId, int docId)
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand("SELECT FilePath FROM dbo.INV_ItemFlow_Doc WHERE DocID=@DocID AND FlowID=@FlowID", conn);
        cmd.Parameters.Add("@DocID", SqlDbType.Int).Value = docId;
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        conn.Open();
        return Convert.ToString(cmd.ExecuteScalar());
    }

    private List<int> ParseDeletedAttachmentDocIds(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try
        {
            return JsonSerializer.Deserialize<List<int>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private void DeleteAttachmentsInTransaction(SqlConnection conn, SqlTransaction tran, long flowId, List<int> docIds)
    {
        if (docIds.Count == 0) return;
        var flow = GetFlowHeader(flowId);
        if (flow == null) return;
        var folder = ResolveAttachmentUploadFolder(flow.FlowNo, flow.FlowDate, "ReceiveVoucher");

        foreach (var docId in docIds.Distinct())
        {
            string? fileName;
            using (var load = new SqlCommand("SELECT FilePath FROM dbo.INV_ItemFlow_Doc WHERE DocID=@DocID AND FlowID=@FlowID", conn, tran))
            {
                load.Parameters.Add("@DocID", SqlDbType.Int).Value = docId;
                load.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
                fileName = Convert.ToString(load.ExecuteScalar());
            }

            using (var del = new SqlCommand("DELETE FROM dbo.INV_ItemFlow_Doc WHERE DocID=@DocID AND FlowID=@FlowID", conn, tran))
            {
                del.Parameters.Add("@DocID", SqlDbType.Int).Value = docId;
                del.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
                del.ExecuteNonQuery();
            }

            if (!string.IsNullOrWhiteSpace(fileName))
            {
                var fullPath = Path.Combine(folder, fileName);
                if (System.IO.File.Exists(fullPath)) System.IO.File.Delete(fullPath);
            }
        }
    }

    private InventoryReceivedHeader? GetFlowHeader(long flowId)
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand("SELECT FlowNo, FlowDate FROM dbo.INV_ItemFlow WHERE FlowID=@FlowID", conn);
        cmd.Parameters.Add("@FlowID", SqlDbType.BigInt).Value = flowId;
        conn.Open();
        using var rd = cmd.ExecuteReader();
        if (!rd.Read()) return null;
        return new InventoryReceivedHeader
        {
            FlowID = flowId,
            FlowNo = Convert.ToString(rd["FlowNo"]) ?? string.Empty,
            FlowDate = Convert.ToDateTime(rd["FlowDate"])
        };
    }

    private void UpdatePriceByDetails(SqlConnection conn, SqlTransaction tran, List<InventoryReceivedDetailRow> details)
    {
        foreach (var detail in details.Where(x => x.ItemId > 0 && x.UnitPrice > 0).GroupBy(x => x.ItemId).Select(x => x.First()))
        {
            using var getCmd = new SqlCommand("SELECT ISNULL(UnitPrice,0) FROM dbo.INV_ItemList WHERE ItemID = @ItemID", conn, tran);
            getCmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = detail.ItemId;
            var oldPrice = Convert.ToDecimal(getCmd.ExecuteScalar() ?? 0m);
            if (oldPrice == detail.UnitPrice) continue;

            using var updateCmd = new SqlCommand("UPDATE dbo.INV_ItemList SET UnitPrice = @NewPrice WHERE ItemID = @ItemID AND ISNULL(IsIrregular,0) = 0", conn, tran);
            updateCmd.Parameters.Add("@NewPrice", SqlDbType.Decimal).Value = detail.UnitPrice;
            updateCmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = detail.ItemId;
            var affected = updateCmd.ExecuteNonQuery();
            if (affected <= 0) continue;

            using var logCmd = new SqlCommand(@"INSERT INTO dbo.INV_UpdatePriceOfItemHis(ItemID, UserID, TheDateTime, OldPrice, NewPrice)
VALUES(@ItemID, @UserID, GETDATE(), @OldPrice, @NewPrice)", conn, tran);
            logCmd.Parameters.Add("@ItemID", SqlDbType.Int).Value = detail.ItemId;
            logCmd.Parameters.Add("@UserID", SqlDbType.Int).Value = GetCurrentEmployeeId();
            logCmd.Parameters.Add("@OldPrice", SqlDbType.Decimal).Value = oldPrice;
            logCmd.Parameters.Add("@NewPrice", SqlDbType.Decimal).Value = detail.UnitPrice;
            logCmd.ExecuteNonQuery();
        }
    }

    private int GetCurrentRoleId() => int.Parse(User.FindFirst("RoleID")?.Value ?? "0");
    private bool IsAdminRole() => User.FindFirst("IsAdminRole")?.Value == "True";
    private int GetCurrentKpGroupId()
    {
        if (int.TryParse(User.FindFirst("KPGroupID")?.Value, out var kpGroupFromClaim) && kpGroupFromClaim > 0)
        {
            return kpGroupFromClaim;
        }

        var employeeId = GetCurrentEmployeeId();
        using var connEmployee = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmdEmployee = new SqlCommand("SELECT TOP 1 StoreGR FROM dbo.MS_Employee WHERE EmployeeID=@EmployeeID", connEmployee);
        cmdEmployee.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId;
        connEmployee.Open();
        var kpGroupFromEmployee = Convert.ToInt32(cmdEmployee.ExecuteScalar() ?? 0);
        if (kpGroupFromEmployee > 0) return kpGroupFromEmployee;

        var employeeCode = GetCurrentEmployeeCode();
        if (!string.IsNullOrWhiteSpace(employeeCode))
        {
            using var cmdEmployeeCode = new SqlCommand("SELECT TOP 1 StoreGR FROM dbo.MS_Employee WHERE EmployeeCode=@EmployeeCode", connEmployee);
            cmdEmployeeCode.Parameters.Add("@EmployeeCode", SqlDbType.VarChar, 50).Value = employeeCode.Trim();
            var kpGroupFromCode = Convert.ToInt32(cmdEmployeeCode.ExecuteScalar() ?? 0);
            if (kpGroupFromCode > 0) return kpGroupFromCode;
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand("SELECT TOP 1 KPGroupID FROM dbo.INV_KPGroupMember WHERE EmployeeID=@EmployeeID ORDER BY KPGroupID", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId;
        conn.Open();
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }
    private int GetCurrentEmployeeId() => int.Parse(User.FindFirst("EmployeeID")?.Value ?? User.FindFirst("EmpID")?.Value ?? "0");
    private PagePermissions GetUserPermissions() => IsAdminRole() ? new PagePermissions { AllowedNos = Enumerable.Range(1, 20).ToList() } : new PagePermissions { AllowedNos = _permissionService.GetPermissionsForPage(GetCurrentRoleId(), FunctionId) };

    private string GetCurrentEmployeeCode()
    {
        var claimCode = User.FindFirst("EmployeeCode")?.Value;
        if (!string.IsNullOrWhiteSpace(claimCode)) return claimCode;

        var employeeId = GetCurrentEmployeeId();
        if (employeeId <= 0) return string.Empty;
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        using var cmd = new SqlCommand("SELECT EmployeeCode FROM dbo.MS_Employee WHERE EmployeeID=@EmployeeID", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId;
        conn.Open();
        return Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
    }
}

public class InventoryReceivedHeader
{
    public long FlowID { get; set; }
    [Required] public string FlowNo { get; set; } = string.Empty;
    [DataType(DataType.Date)] public DateTime FlowDate { get; set; } = DateTime.Today;
    public int? ToStore { get; set; }
    public int? FlowSubType { get; set; }
    public int? RetDept { get; set; }
    public int? RetAssetLocation { get; set; }
    public int? POID { get; set; }
    public int? MoveToCPNStore { get; set; }
    public string? According { get; set; }
    public string? Reason { get; set; }
    public int StatusID { get; set; } = 1;
    public string StatusName { get; set; } = string.Empty;
}

public class InventoryReceivedAttachmentViewModel
{
    public int DocId { get; set; }
    public long FlowId { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public DateTime? UploadDate { get; set; }
    public int? UserId { get; set; }
    public string EmployeeCode { get; set; } = string.Empty;
}

public class InventoryReceivedDetailRow
{
    public int ItemId { get; set; }
    public long? ChekingID { get; set; }
    public long? ChekingDTID { get; set; }
    public long? MRDetailID { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal DocQty { get; set; }
    public decimal ActQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public int? LocationId { get; set; }
    public string LocationName { get; set; } = string.Empty;
}

public class InventoryReceivedItemOption
{
    public int ItemId { get; set; }
    public long? ChekingID { get; set; }
    public long? ChekingDTID { get; set; }
    public long? MRDetailID { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal RemainingQty { get; set; }
    public decimal Price { get; set; }
}

public class ConfirmFlowInfo
{
    public long FlowID { get; set; }
    public string FlowNo { get; set; } = string.Empty;
    public int FlowSubType { get; set; }
    public int StatusID { get; set; }
    public int KPGroup { get; set; }
    public int? MoveToCPNStore { get; set; }
    public int? RetDept { get; set; }
}




