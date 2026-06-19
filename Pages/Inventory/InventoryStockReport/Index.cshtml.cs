using System.Globalization;
using System.Net;
using System.Net.Mail;
using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Services;
using SmartSam.Helpers;

namespace SmartSam.Pages.Inventory.InventoryReport;

public class IndexModel : BasePageModel
{
    private const int FunctionId = 70;
    private const int PermissionViewList = 1;
    private const string NotifyCcEmail = "maiquangvinhi4@gmail.com";
    // private const string NotifyCcEmail = "hai.dq@saigonskygarden.com.vn";

    private readonly PermissionService _permissionService;

    public IndexModel(IConfiguration config, PermissionService permissionService) : base(config)
    {
        _permissionService = permissionService;
    }

    public PagePermissions PagePerm { get; private set; } = new();

    [BindProperty(SupportsGet = true)]
    public InventoryReportFilter Filter { get; set; } = new();

    public List<SelectListItem> KpGroups { get; set; } = new();
    public List<SelectListItem> Stores { get; set; } = new();
    public List<InventoryReportRow> Rows { get; set; } = new();
    public string? ErrorMessage { get; set; }
    [TempData]
    public string? SubmitMessage { get; set; }
    [TempData]
    public string? SubmitMessageType { get; set; }
    public int TotalRecords { get; set; }
    public int DefaultPageSize => _config.GetValue<int?>("AppSettings:DefaultPageSize") ?? 13;
    public IReadOnlyList<int> PageSizeOptions => GetConfiguredPageSizeOptions();
    public int TotalPages => Filter.PageSize <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling(TotalRecords / (double)Filter.PageSize));
    public int PageStart => TotalRecords == 0 ? 0 : ((Filter.Page - 1) * Filter.PageSize) + 1;
    public int PageEnd => TotalRecords == 0 ? 0 : Math.Min(Filter.Page * Filter.PageSize, TotalRecords);
    public bool HasPreviousPage => Filter.Page > 1;
    public bool HasNextPage => Filter.Page < TotalPages;
    public bool CanSubmitReport { get; private set; }
    public bool CanApproveReport { get; private set; }

    public IActionResult OnGet()
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewList)) return Redirect("/");

        NormalizeFilter();
        CanSubmitReport = CanCurrentUserSubmitReport();
        CanApproveReport = CanCurrentUserApproveReport();
        LoadLookups();
        LoadRows();
        return Page();
    }


    public IActionResult OnPostSubmitReport()
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewList)) return Redirect("/");

        NormalizeFilter();
        CanSubmitReport = CanCurrentUserSubmitReport();
        CanApproveReport = CanCurrentUserApproveReport();
        LoadLookups();
        var result = SubmitInventoryReport();
        SubmitMessage = result.Message;
        SubmitMessageType = result.Success ? "success" : "error";

        return RedirectToPage("./Index", new
        {
            FromDate = Filter.FromDate?.ToString("yyyy-MM-dd"),
            ToDate = Filter.ToDate?.ToString("yyyy-MM-dd"),
            ItemCode = Filter.ItemCode,
            ItemName = Filter.ItemName,
            KpGroupId = Filter.KpGroupId,
            StoreId = Filter.StoreId,
            ViewType = 0,
            Page = Filter.Page,
            PageSize = Filter.PageSize
        });
    }

    public IActionResult OnPostApproveReport()
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewList)) return Redirect("/");

        NormalizeFilter();
        LoadLookups();
        var result = ApproveInventoryReport();
        SubmitMessage = result.Message;
        SubmitMessageType = result.Success ? "success" : "error";

        return RedirectToPage("./Index", new
        {
            FromDate = Filter.FromDate?.ToString("yyyy-MM-dd"),
            ToDate = Filter.ToDate?.ToString("yyyy-MM-dd"),
            ItemCode = Filter.ItemCode,
            ItemName = Filter.ItemName,
            KpGroupId = Filter.KpGroupId,
            StoreId = Filter.StoreId,
            ViewType = 0,
            Page = Filter.Page,
            PageSize = Filter.PageSize
        });
    }

    private (bool Success, string Message) ApproveInventoryReport()
    {
        if (!Filter.KpGroupId.HasValue || Filter.StoreId <= 0 || !Filter.FromDate.HasValue || !Filter.ToDate.HasValue)
        {
            return (false, "Please select one store and a valid report period.");
        }

        var employeeId = GetCurrentEmployeeId();
        if (employeeId <= 0)
        {
            return (false, "Cannot identify current employee.");
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        if (!IsHeadDept(conn, employeeId))
        {
            return (false, "You have no right to approve Item Balance Report.");
        }

        var submitId = GetUncheckedReportSubmitId(conn, Filter.FromDate.Value.Year, Filter.FromDate.Value.Month, Filter.KpGroupId.Value, Filter.StoreId);
        if (submitId <= 0)
        {
            return (false, "Cannot find unchecked submitted report for selected filter.");
        }

        using (var cmd = new SqlCommand(@"UPDATE dbo.inv_ReportSubmit
SET CheckedBy=@CheckedBy, CheckedDate=@CheckedDate
WHERE ID=@ID AND CheckedBy IS NULL AND CheckedDate IS NULL", conn))
        {
            cmd.Parameters.Add("@CheckedBy", SqlDbType.Int).Value = employeeId;
            cmd.Parameters.Add("@CheckedDate", SqlDbType.DateTime).Value = DateTime.Now;
            cmd.Parameters.Add("@ID", SqlDbType.BigInt).Value = submitId;
            if (cmd.ExecuteNonQuery() == 0)
            {
                return (false, "This report was already checked.");
            }
        }

        var mailResult = TryQueueCheckedReportMail(conn, employeeId, submitId);
        if (!mailResult.Success)
        {
            return (true, $"Approve Item Balance Report Successful. {mailResult.Message}");
        }

        return (true, "Approve Item Balance Report Successful.");
    }

    private (bool Success, string Message) SubmitInventoryReport()
    {
        if (!Filter.KpGroupId.HasValue || Filter.StoreId <= 0 || !Filter.FromDate.HasValue || !Filter.ToDate.HasValue)
        {
            return (false, "Please select one store and a valid report period.");
        }

        var fromDate = Filter.FromDate.Value.Date;
        var toDate = Filter.ToDate.Value.Date;
        var lastDateOfMonth = new DateTime(fromDate.Year, fromDate.Month, DateTime.DaysInMonth(fromDate.Year, fromDate.Month));
        if (fromDate.Year != toDate.Year || fromDate.Month != toDate.Month || fromDate.Day != 1 || toDate != lastDateOfMonth)
        {
            return (false, $"From date and to date must cover a full month. Selected From: {fromDate:dd/MM/yyyy}, To: {toDate:dd/MM/yyyy}.");
        }

        var employeeId = GetCurrentEmployeeId();
        if (employeeId <= 0)
        {
            return (false, "Cannot identify current employee.");
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        if (!HaveRightToSubmitReport(conn, employeeId))
        {
            return (false, "You have no right to submit Item Balance Report.");
        }

        if (ReportSubmitExists(conn, fromDate.Year, fromDate.Month, Filter.KpGroupId.Value, Filter.StoreId))
        {
            return (false, "This report has already been submitted.");
        }

        long submitId;
        using (var cmd = new SqlCommand(@"INSERT INTO dbo.inv_ReportSubmit(TheYear, TheMonth, StoreGroupID, StoreID, StatusID, PreparedBy, PreparedDate)
OUTPUT INSERTED.ID
VALUES(@TheYear, @TheMonth, @StoreGroupID, @StoreID, 1, @PreparedBy, @PreparedDate)", conn))
        {
            cmd.Parameters.Add("@TheYear", SqlDbType.Int).Value = fromDate.Year;
            cmd.Parameters.Add("@TheMonth", SqlDbType.Int).Value = fromDate.Month;
            cmd.Parameters.Add("@StoreGroupID", SqlDbType.Int).Value = Filter.KpGroupId.Value;
            cmd.Parameters.Add("@StoreID", SqlDbType.Int).Value = Filter.StoreId;
            cmd.Parameters.Add("@PreparedBy", SqlDbType.Int).Value = employeeId;
            cmd.Parameters.Add("@PreparedDate", SqlDbType.DateTime).Value = DateTime.Now;
            submitId = Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
        }

        var mailResult = TryQueueSubmitReportMail(conn, employeeId, submitId);
        if (!mailResult.Success)
        {
            return (true, $"Submit Item Balance Report Successful. {mailResult.Message}");
        }

        return (true, "Submit Item Balance Report Successful.");
    }

    private bool HaveRightToSubmitReport(SqlConnection conn, int employeeId)
    {
        using var cmd = new SqlCommand("SELECT ISNULL(SubmitINVReport, 0) FROM dbo.MS_Employee WHERE EmployeeID=@EmployeeID", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId;
        var value = cmd.ExecuteScalar();
        return value != null && value != DBNull.Value && Convert.ToBoolean(value);
    }

    private bool CanCurrentUserSubmitReport()
    {
        var employeeId = GetCurrentEmployeeId();
        if (employeeId <= 0) return false;

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        return HaveRightToSubmitReport(conn, employeeId);
    }

    private bool CanCurrentUserApproveReport()
    {
        var employeeId = GetCurrentEmployeeId();
        if (employeeId <= 0 || !Filter.KpGroupId.HasValue || Filter.StoreId <= 0 || !Filter.FromDate.HasValue) return false;

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        return IsHeadDept(conn, employeeId)
            && GetUncheckedReportSubmitId(conn, Filter.FromDate.Value.Year, Filter.FromDate.Value.Month, Filter.KpGroupId.Value, Filter.StoreId) > 0;
    }

    private bool IsHeadDept(SqlConnection conn, int employeeId)
    {
        using var cmd = new SqlCommand("SELECT ISNULL(HeadDept, 0) FROM dbo.MS_Employee WHERE EmployeeID=@EmployeeID", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId;
        var value = cmd.ExecuteScalar();
        return value != null && value != DBNull.Value && Convert.ToBoolean(value);
    }

    private long GetUncheckedReportSubmitId(SqlConnection conn, int year, int month, int storeGroupId, int storeId)
    {
        using var cmd = new SqlCommand(@"SELECT TOP 1 ID FROM dbo.inv_ReportSubmit
WHERE StoreGroupID=@StoreGroupID AND StoreID=@StoreID AND TheYear=@TheYear AND TheMonth=@TheMonth
  AND CheckedBy IS NULL AND CheckedDate IS NULL
ORDER BY ID DESC", conn);
        cmd.Parameters.Add("@StoreGroupID", SqlDbType.Int).Value = storeGroupId;
        cmd.Parameters.Add("@StoreID", SqlDbType.Int).Value = storeId;
        cmd.Parameters.Add("@TheYear", SqlDbType.Int).Value = year;
        cmd.Parameters.Add("@TheMonth", SqlDbType.Int).Value = month;
        return Convert.ToInt64(cmd.ExecuteScalar() ?? 0);
    }

    private bool ReportSubmitExists(SqlConnection conn, int year, int month, int storeGroupId, int storeId)
    {
        using var cmd = new SqlCommand(@"SELECT TOP 1 ID FROM dbo.inv_ReportSubmit
WHERE StoreGroupID=@StoreGroupID AND StoreID=@StoreID AND TheYear=@TheYear AND TheMonth=@TheMonth", conn);
        cmd.Parameters.Add("@StoreGroupID", SqlDbType.Int).Value = storeGroupId;
        cmd.Parameters.Add("@StoreID", SqlDbType.Int).Value = storeId;
        cmd.Parameters.Add("@TheYear", SqlDbType.Int).Value = year;
        cmd.Parameters.Add("@TheMonth", SqlDbType.Int).Value = month;
        var value = cmd.ExecuteScalar();
        return value != null && value != DBNull.Value;
    }

    private (bool Success, string Message) TryQueueSubmitReportMail(SqlConnection conn, int employeeId, long submitId)
    {
        try
        {
            var deptId = GetEmployeeDeptId(conn, employeeId);
            if (deptId <= 0)
            {
                return (false, "Cannot find department of current employee, email was not sent.");
            }

            using var emailCmd = new SqlCommand(@"SELECT TheEmail, EmployeeCode, EmployeeName, ISNULL(Title,'') AS Title
FROM dbo.MS_Employee
WHERE HeadDept = 1 AND IsActive = 1 AND ISNULL(TheEmail,'') <> '' AND DeptID=@DeptID
ORDER BY EmployeeName, EmployeeCode", conn);
            emailCmd.Parameters.Add("@DeptID", SqlDbType.Int).Value = deptId;
            using var reader = emailCmd.ExecuteReader();
            var recipients = new List<(string Email, string Label)>();
            while (reader.Read())
            {
                var email = Convert.ToString(reader["TheEmail"]) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(email)) continue;
                var employeeCode = Convert.ToString(reader["EmployeeCode"]) ?? string.Empty;
                var employeeName = Convert.ToString(reader["EmployeeName"]) ?? string.Empty;
                var title = Convert.ToString(reader["Title"]) ?? string.Empty;
                recipients.Add((email.Trim(), BuildRecipientDisplayName(title, employeeName, employeeCode, email)));
            }
            reader.Close();

            if (recipients.Count == 0)
            {
                return (false, $"Cannot find email for HeadDept in DeptID={deptId}, email was not sent.");
            }

            var senderEmail = _config.GetValue<string>("EmailSettings:SenderEmail") ?? string.Empty;
            var password = _config.GetValue<string>("EmailSettings:Password") ?? string.Empty;
            var mailServer = _config.GetValue<string>("EmailSettings:MailServer") ?? string.Empty;
            var mailPort = _config.GetValue<int?>("EmailSettings:MailPort") ?? 0;
            if (string.IsNullOrWhiteSpace(senderEmail) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(mailServer) || mailPort <= 0)
            {
                return (false, "Email settings are incomplete, email was not sent.");
            }

            var storeName = GetStoreName(conn, Filter.StoreId);
            var submittedBy = GetCurrentEmployeeDisplayName(conn, employeeId);
            var subject = ApplyMailSubjectPrefix($"Please login Smartsam and check Item Balance Report no: {submitId} at reminder form ");
            var detailUrl = Url.Page("/Inventory/InventoryStockReport/Index", values: new
            {
                FromDate = Filter.FromDate?.ToString("yyyy-MM-dd"),
                ToDate = Filter.ToDate?.ToString("yyyy-MM-dd"),
                KpGroupId = Filter.KpGroupId,
                StoreId = Filter.StoreId,
                ViewType = 0,
                Page = 1,
                PageSize = Filter.PageSize
            });
            var absoluteUrl = string.IsNullOrWhiteSpace(detailUrl) ? string.Empty : $"{Request.Scheme}://{Request.Host}{detailUrl}";
            var bodyContent = $@"<p>Dear {{RECIPIENT_LABEL}},</p>
<p>An Item Balance Report has been <b>submitted</b> and is waiting for your checking.</p>
<ul>
    <li>Report No: <b>{submitId}</b></li>
    <li>Store Name: <b>{WebUtility.HtmlEncode(storeName)}</b></li>
    <li>Period: <b>{Filter.FromDate:dd/MM/yyyy}</b> to <b>{Filter.ToDate:dd/MM/yyyy}</b></li>
    <li>Submitted by: <b>{WebUtility.HtmlEncode(submittedBy)}</b></li>
    <li>Submit time: <b>{DateTime.Now.ToString("MMM d, yyyy HH:mm:ss", CultureInfo.InvariantCulture)}</b></li>
</ul>
<p><b>Click Here to Approve:</b> <a href='{WebUtility.HtmlEncode(absoluteUrl)}'>Open Item Balance Report</a></p>
<p>Best regards,<br/>SmartSam System</p>";

            _ = Task.Run(async () =>
            {
                try
                {
                    await SendSubmitReportMailAsync(recipients, subject, bodyContent, senderEmail, password, mailServer, mailPort);
                }
                catch
                {
                }
            });

            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, $"Email was not sent: {ex.Message}");
        }
    }

    private string GetCurrentEmployeeDisplayName(SqlConnection conn, int employeeId)
    {
        using var cmd = new SqlCommand("SELECT ISNULL(EmployeeName, '') FROM dbo.MS_Employee WHERE EmployeeID=@EmployeeID", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId;
        var name = Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
        return string.IsNullOrWhiteSpace(name) ? User.FindFirst("FullName")?.Value ?? string.Empty : name;
    }

    private int GetEmployeeDeptId(SqlConnection conn, int employeeId)
    {
        using var cmd = new SqlCommand("SELECT ISNULL(DeptID, 0) FROM dbo.MS_Employee WHERE EmployeeID=@EmployeeID", conn);
        cmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId;
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private string GetStoreName(SqlConnection conn, int storeId)
    {
        using var cmd = new SqlCommand("SELECT TOP 1 ISNULL(StoreName, '') FROM dbo.INV_StoreList WHERE StoreID=@StoreID", conn);
        cmd.Parameters.Add("@StoreID", SqlDbType.Int).Value = storeId;
        return Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
    }

    private async Task SendSubmitReportMailAsync(List<(string Email, string Label)> recipients, string subject, string bodyContent, string senderEmail, string password, string mailServer, int mailPort)
    {
        using var smtp = new SmtpClient(mailServer, mailPort)
        {
            EnableSsl = true,
            Credentials = new NetworkCredential(senderEmail, password)
        };

        foreach (var recipient in recipients)
        {
            var body = EmailTemplateHelper.WrapInNotifyTemplate("ITEM BALANCE REPORT SUBMITTED", "#17a2b8", DateTime.Now, bodyContent)
                .Replace("{RECIPIENT_LABEL}", WebUtility.HtmlEncode(recipient.Label));

            using var mail = new MailMessage
            {
                From = new MailAddress(senderEmail, "SmartSam System"),
                Subject = subject,
                Body = body,
                IsBodyHtml = true
            };
            mail.To.Add(recipient.Email);
            if (!string.Equals(recipient.Email, NotifyCcEmail, StringComparison.OrdinalIgnoreCase))
            {
                mail.CC.Add(NotifyCcEmail);
            }

            await smtp.SendMailAsync(mail);
        }
    }

    private (bool Success, string Message) TryQueueCheckedReportMail(SqlConnection conn, int employeeId, long submitId)
    {
        try
        {
            var deptId = GetEmployeeDeptId(conn, employeeId);
            if (deptId <= 0)
            {
                return (false, "Cannot find department of current employee, email was not sent.");
            }

            using var emailCmd = new SqlCommand(@"SELECT TheEmail, EmployeeCode, EmployeeName, ISNULL(Title,'') AS Title
FROM dbo.MS_Employee
WHERE IsCFO = 1 AND IsActive = 1 AND ISNULL(TheEmail,'') <> '' AND DeptID=@DeptID
ORDER BY EmployeeName, EmployeeCode", conn);
            emailCmd.Parameters.Add("@DeptID", SqlDbType.Int).Value = deptId;
            using var reader = emailCmd.ExecuteReader();
            var recipients = new List<(string Email, string Label)>();
            while (reader.Read())
            {
                var email = Convert.ToString(reader["TheEmail"]) ?? string.Empty;
                if (string.IsNullOrWhiteSpace(email)) continue;
                var employeeCode = Convert.ToString(reader["EmployeeCode"]) ?? string.Empty;
                var employeeName = Convert.ToString(reader["EmployeeName"]) ?? string.Empty;
                var title = Convert.ToString(reader["Title"]) ?? string.Empty;
                recipients.Add((email.Trim(), BuildRecipientDisplayName(title, employeeName, employeeCode, email)));
            }
            reader.Close();

            if (recipients.Count == 0)
            {
                return (false, $"Cannot find email for CFO in DeptID={deptId}, email was not sent.");
            }

            var senderEmail = _config.GetValue<string>("EmailSettings:SenderEmail") ?? string.Empty;
            var password = _config.GetValue<string>("EmailSettings:Password") ?? string.Empty;
            var mailServer = _config.GetValue<string>("EmailSettings:MailServer") ?? string.Empty;
            var mailPort = _config.GetValue<int?>("EmailSettings:MailPort") ?? 0;
            if (string.IsNullOrWhiteSpace(senderEmail) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(mailServer) || mailPort <= 0)
            {
                return (false, "Email settings are incomplete, email was not sent.");
            }

            var storeName = GetStoreName(conn, Filter.StoreId);
            var checkedBy = GetCurrentEmployeeDisplayName(conn, employeeId);
            var subject = ApplyMailSubjectPrefix($"Item Balance Report no: {submitId} has been checked");
            var detailUrl = Url.Page("/Inventory/InventoryStockReport/Index", values: new
            {
                FromDate = Filter.FromDate?.ToString("yyyy-MM-dd"),
                ToDate = Filter.ToDate?.ToString("yyyy-MM-dd"),
                KpGroupId = Filter.KpGroupId,
                StoreId = Filter.StoreId,
                ViewType = 0,
                Page = 1,
                PageSize = Filter.PageSize
            });
            var absoluteUrl = string.IsNullOrWhiteSpace(detailUrl) ? string.Empty : $"{Request.Scheme}://{Request.Host}{detailUrl}";
            var bodyContent = $@"<p>Dear {{RECIPIENT_LABEL}},</p>
<p>An Item Balance Report has been <b>checked</b>.</p>
<ul>
    <li>Report No: <b>{submitId}</b></li>
    <li>Store Name: <b>{WebUtility.HtmlEncode(storeName)}</b></li>
    <li>Period: <b>{Filter.FromDate:dd/MM/yyyy}</b> to <b>{Filter.ToDate:dd/MM/yyyy}</b></li>
    <li>Checked by: <b>{WebUtility.HtmlEncode(checkedBy)}</b></li>
    <li>Checked time: <b>{DateTime.Now.ToString("MMM d, yyyy HH:mm:ss", CultureInfo.InvariantCulture)}</b></li>
</ul>
<p><b>Click Here to View:</b> <a href='{WebUtility.HtmlEncode(absoluteUrl)}'>Open Item Balance Report</a></p>
<p>Best regards,<br/>SmartSam System</p>";

            _ = Task.Run(async () =>
            {
                try
                {
                    await SendSubmitReportMailAsync(recipients, subject, bodyContent, senderEmail, password, mailServer, mailPort);
                }
                catch
                {
                }
            });

            return (true, string.Empty);
        }
        catch (Exception ex)
        {
            return (false, $"Email was not sent: {ex.Message}");
        }
    }

    private static string BuildRecipientDisplayName(string title, string employeeName, string employeeCode, string email)
    {
        var titleTrim = (title ?? string.Empty).Trim();
        var nameTrim = string.IsNullOrWhiteSpace(employeeName) ? email : employeeName.Trim();
        var displayName = !string.IsNullOrWhiteSpace(titleTrim)
            ? $"{titleTrim} {nameTrim}".Trim()
            : nameTrim;
        var codeTrim = (employeeCode ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(displayName) && !string.IsNullOrWhiteSpace(codeTrim))
        {
            return $"{displayName} ({codeTrim})";
        }
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName;
        }
        return string.IsNullOrWhiteSpace(codeTrim) ? email : codeTrim;
    }

    private static string FormatEmployeeLabel(string? employeeName, string? employeeCode)
    {
        var name = (employeeName ?? string.Empty).Trim();
        var code = (employeeCode ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(code)) return $"{name} ({code})";
        if (!string.IsNullOrWhiteSpace(name)) return name;
        return code;
    }

    private string ApplyMailSubjectPrefix(string subject)
    {
        var prefix = _config.GetValue<string>("EmailSettings:PrefixSubject")?.Trim();
        if (string.IsNullOrWhiteSpace(prefix)) return subject;
        return $"{prefix} - {subject}";
    }

    public IActionResult OnGetReport(bool inline = false)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewList)) return Redirect("/");

        NormalizeFilter();
        LoadLookups();
        if (!Filter.KpGroupId.HasValue) return BadRequest("Please select inventory group.");

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        var rows = ExecuteLegacyReportProcedure(conn);
        var selectedStoreName = Stores.FirstOrDefault(x => x.Value == Filter.StoreId.ToString())?.Text ?? "[All Stores]";
        var kpGroupName = KpGroups.FirstOrDefault(x => x.Value == Filter.KpGroupId.Value.ToString())?.Text ?? string.Empty;
        var storeName = Filter.StoreId > 0 ? selectedStoreName : string.IsNullOrWhiteSpace(kpGroupName) ? "[All Stores]" : $"[All Stores] - {kpGroupName}";
        var model = new InventoryStockReportModel
        {
            FromDateText = Filter.FromDate!.Value.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
            ToDateText = Filter.ToDate!.Value.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture),
            PrintDateText = DateTime.Today.ToString("dd - MMM - yyyy", CultureInfo.InvariantCulture),
            StoreName = storeName,
            Items = rows.Select(x => new InventoryStockReportItem
            {
                ItemCode = x.ItemCode,
                ItemName = x.ItemName,
                BeginQuantity = x.BeginQuantity,
                ReceiveQuantity = x.ReceiveQuantity,
                IssueQuantity = x.IssueQuantity,
                EndQuantity = x.EndQuantity
            }).ToList()
        };

        var reportSubmit = LoadReportSubmitInfo(conn, Filter.FromDate.Value.Year, Filter.FromDate.Value.Month, Filter.KpGroupId.Value, Filter.StoreId);
        if (reportSubmit != null)
        {
            model.PreparedSignature = LoadEmployeeSignature(conn, reportSubmit.PreparedBy);
            model.CheckedSignature = LoadEmployeeSignature(conn, reportSubmit.CheckedBy);
        }

        var pdf = InventoryStockReportPdf.BuildPdf(model);
        var fileName = $"inventory_report_{Filter.FromDate:yyyyMMdd}_{Filter.ToDate:yyyyMMdd}.pdf";
        if (inline)
        {
            Response.Headers["Content-Disposition"] = $"inline; filename={fileName}";
            return File(pdf, "application/pdf");
        }

        return File(pdf, "application/pdf", fileName);
    }
    private void NormalizeFilter()
    {
        if (!Filter.FromDate.HasValue) Filter.FromDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        if (!Filter.ToDate.HasValue) Filter.ToDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.DaysInMonth(DateTime.Today.Year, DateTime.Today.Month));

        Filter.ViewType = Filter.ViewType is 0 or 1 or 2 ? Filter.ViewType : 0;
        Filter.Page = Filter.Page <= 0 ? 1 : Filter.Page;
        Filter.PageSize = PageSizeOptions.Contains(Filter.PageSize) ? Filter.PageSize : DefaultPageSize;

        if (!IsAdminRole())
        {
            Filter.KpGroupId = GetEffectiveKpGroupId();
        }
    }

    public IActionResult OnGetStoresByGroup(int kpGroupId)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewList))
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return new JsonResult(new { message = "No permission." });
        }

        if (!IsAdminRole() && kpGroupId != GetEffectiveKpGroupId())
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return new JsonResult(new { message = "Invalid inventory group." });
        }

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();
        var stores = LoadStores(conn, kpGroupId)
            .Select(x => new { value = x.Value, text = x.Text })
            .ToList();
        return new JsonResult(stores);
    }

    public IActionResult OnGetSubmittedReports(int year)
    {
        PagePerm = GetUserPermissions();
        if (!PagePerm.HasPermission(PermissionViewList))
        {
            Response.StatusCode = StatusCodes.Status403Forbidden;
            return new JsonResult(new { message = "No permission." });
        }

        year = year <= 0 ? DateTime.Today.Year : year;
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        var sql = @"SELECT rs.ID, rs.TheYear, rs.TheMonth, rs.StoreGroupID, ISNULL(kp.KPGroupName, '') AS KPGroupName,
       rs.StoreID, ISNULL(st.StoreName, '') AS StoreName,
       rs.PreparedBy, rs.PreparedDate, ISNULL(prep.EmployeeCode, '') AS PreparedCode, ISNULL(prep.EmployeeName, '') AS PreparedName,
       rs.CheckedBy, rs.CheckedDate, ISNULL(chk.EmployeeCode, '') AS CheckedCode, ISNULL(chk.EmployeeName, '') AS CheckedName
FROM dbo.inv_ReportSubmit rs
LEFT JOIN dbo.INV_KPGroup kp ON kp.KPGroupID = rs.StoreGroupID
LEFT JOIN dbo.INV_StoreList st ON st.StoreID = rs.StoreID
LEFT JOIN dbo.MS_Employee prep ON prep.EmployeeID = rs.PreparedBy
LEFT JOIN dbo.MS_Employee chk ON chk.EmployeeID = rs.CheckedBy
WHERE rs.TheYear = @TheYear";
        if (!IsAdminRole())
        {
            sql += " AND rs.StoreGroupID = @StoreGroupID";
        }
        sql += " ORDER BY rs.TheMonth DESC, rs.ID DESC";

        using var cmd = new SqlCommand(sql, conn);
        cmd.Parameters.Add("@TheYear", SqlDbType.Int).Value = year;
        if (!IsAdminRole()) cmd.Parameters.Add("@StoreGroupID", SqlDbType.Int).Value = GetEffectiveKpGroupId() ?? 0;

        var reports = new List<object>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var reportYear = Convert.ToInt32(reader["TheYear"] ?? year);
            var reportMonth = Convert.ToInt32(reader["TheMonth"] ?? 1);
            var fromDate = new DateTime(reportYear, reportMonth, 1);
            var toDate = new DateTime(reportYear, reportMonth, DateTime.DaysInMonth(reportYear, reportMonth));
            var preparedDate = reader["PreparedDate"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["PreparedDate"]);
            var checkedDate = reader["CheckedDate"] == DBNull.Value ? (DateTime?)null : Convert.ToDateTime(reader["CheckedDate"]);
            reports.Add(new
            {
                id = Convert.ToInt64(reader["ID"]),
                year = reportYear,
                month = reportMonth,
                period = $"{fromDate:dd/MM/yyyy} - {toDate:dd/MM/yyyy}",
                fromDate = fromDate.ToString("yyyy-MM-dd"),
                toDate = toDate.ToString("yyyy-MM-dd"),
                kpGroupId = reader["StoreGroupID"] == DBNull.Value ? 0 : Convert.ToInt32(reader["StoreGroupID"]),
                kpGroupName = Convert.ToString(reader["KPGroupName"]) ?? string.Empty,
                storeId = reader["StoreID"] == DBNull.Value ? 0 : Convert.ToInt32(reader["StoreID"]),
                storeName = Convert.ToString(reader["StoreName"]) ?? string.Empty,
                preparedBy = FormatEmployeeLabel(Convert.ToString(reader["PreparedName"]), Convert.ToString(reader["PreparedCode"])),
                preparedDate = preparedDate?.ToString("dd/MM/yyyy") ?? string.Empty,
                checkedBy = FormatEmployeeLabel(Convert.ToString(reader["CheckedName"]), Convert.ToString(reader["CheckedCode"])),
                checkedDate = checkedDate?.ToString("dd/MM/yyyy") ?? string.Empty,
                status = checkedDate.HasValue ? "Checked" : "Submitted"
            });
        }

        return new JsonResult(reports);
    }
    private IReadOnlyList<int> GetConfiguredPageSizeOptions()
    {
        var configured = _config.GetSection("AppSettings:PageSizeOptions").Get<int[]>() ?? Array.Empty<int>();
        var options = configured.Where(x => x > 0).Distinct().OrderBy(x => x).ToList();
        if (options.Count == 0)
        {
            options = new List<int> { DefaultPageSize };
        }
        if (!options.Contains(DefaultPageSize))
        {
            options.Insert(0, DefaultPageSize);
        }
        return options;
    }

    private ReportSubmitInfo? LoadReportSubmitInfo(SqlConnection conn, int year, int month, int storeGroupId, int storeId)
    {
        using var cmd = new SqlCommand(@"SELECT TOP 1 PreparedBy, CheckedBy
FROM dbo.inv_ReportSubmit
WHERE TheYear=@TheYear AND TheMonth=@TheMonth AND StoreGroupID=@StoreGroupID AND StoreID=@StoreID
ORDER BY ID DESC", conn);
        cmd.Parameters.Add("@TheYear", SqlDbType.Int).Value = year;
        cmd.Parameters.Add("@TheMonth", SqlDbType.Int).Value = month;
        cmd.Parameters.Add("@StoreGroupID", SqlDbType.Int).Value = storeGroupId;
        cmd.Parameters.Add("@StoreID", SqlDbType.Int).Value = storeId;
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new ReportSubmitInfo
        {
            PreparedBy = reader["PreparedBy"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["PreparedBy"]),
            CheckedBy = reader["CheckedBy"] == DBNull.Value ? (int?)null : Convert.ToInt32(reader["CheckedBy"])
        };
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
    private void LoadLookups()
    {
        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        var kpSql = IsAdminRole()
            ? "SELECT KPGroupID, KPGroupName FROM dbo.INV_KPGroup ORDER BY KPGroupName"
            : "SELECT KPGroupID, KPGroupName FROM dbo.INV_KPGroup WHERE KPGroupID=@KPGroupID ORDER BY KPGroupName";
        using (var cmd = new SqlCommand(kpSql, conn))
        {
            if (!IsAdminRole()) cmd.Parameters.Add("@KPGroupID", SqlDbType.Int).Value = GetEffectiveKpGroupId() ?? 0;
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                KpGroups.Add(new SelectListItem
                {
                    Value = Convert.ToString(rd["KPGroupID"]),
                    Text = Convert.ToString(rd["KPGroupName"]) ?? string.Empty
                });
            }
        }

        if (!Filter.KpGroupId.HasValue && KpGroups.Count > 0)
        {
            Filter.KpGroupId = int.TryParse(KpGroups[0].Value, out var firstKpGroupId) ? firstKpGroupId : null;
        }

        Stores = Filter.KpGroupId.HasValue ? LoadStores(conn, Filter.KpGroupId.Value) : new List<SelectListItem> { new SelectListItem { Value = "-1", Text = "[All Stores]" } };


        if (!Stores.Any(x => x.Value == Convert.ToString(Filter.StoreId))) Filter.StoreId = -1;
    }

    private List<SelectListItem> LoadStores(SqlConnection conn, int kpGroupId)
    {
        var stores = new List<SelectListItem>
        {
            new SelectListItem { Value = "-1", Text = "[All Stores]" }
        };

        using var storeCmd = new SqlCommand("SELECT StoreID, StoreName FROM dbo.INV_StoreList WHERE DeptID=@DeptID ORDER BY StoreName", conn);
        storeCmd.Parameters.Add("@DeptID", SqlDbType.Int).Value = kpGroupId;
        using var storeReader = storeCmd.ExecuteReader();
        while (storeReader.Read())
        {
            stores.Add(new SelectListItem
            {
                Value = Convert.ToString(storeReader["StoreID"]) ?? string.Empty,
                Text = Convert.ToString(storeReader["StoreName"]) ?? string.Empty
            });
        }

        return stores;
    }
    private void LoadRows()
    {
        if (!Filter.KpGroupId.HasValue) return;

        try
        {
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            conn.Open();
            var rows = ExecuteLegacyReportProcedure(conn);
            TotalRecords = rows.Count;
            Rows = rows
                .Skip((Filter.Page - 1) * Filter.PageSize)
                .Take(Filter.PageSize)
                .ToList();
        }
        catch (SqlException ex)
        {
            ErrorMessage = "Cannot load inventory report list. Please check legacy stored procedure/data mapping: " + ex.Message;
            Rows = new List<InventoryReportRow>();
            TotalRecords = 0;
        }
    }

    private List<InventoryReportRow> ExecuteLegacyReportProcedure(SqlConnection conn)
    {
        var procedureName = Filter.StoreId > 0 ? "dbo.sp_ItemRpt" : "dbo.sp_ItemRptAll";
        var sql = $"EXEC {procedureName} @FromDate, @ToDate";
        using var cmd = new SqlCommand
        {
            Connection = conn,
            CommandType = CommandType.Text,
            CommandTimeout = 120
        };

        cmd.Parameters.Add("@FromDate", SqlDbType.VarChar, 10).Value = Filter.FromDate!.Value.ToString("yyyy-MM-dd");
        cmd.Parameters.Add("@ToDate", SqlDbType.VarChar, 10).Value = Filter.ToDate!.Value.ToString("yyyy-MM-dd");
        if (Filter.StoreId > 0)
        {
            sql += ", @StoreID";
            cmd.Parameters.Add("@StoreID", SqlDbType.Int).Value = Filter.StoreId;
            if (Filter.ViewType == 2)
            {
                sql += ", @KPGroupID";
                cmd.Parameters.Add("@KPGroupID", SqlDbType.Int).Value = Filter.KpGroupId!.Value;
            }
        }
        else
        {
            sql += ", @KPGroupID";
            cmd.Parameters.Add("@KPGroupID", SqlDbType.Int).Value = Filter.KpGroupId!.Value;
        }

        var filterClause = BuildLegacyItemFilter();
        if (!string.IsNullOrWhiteSpace(filterClause))
        {
            sql += ", @Filter";
            cmd.Parameters.Add("@Filter", SqlDbType.NVarChar, 1000).Value = filterClause;
        }
        cmd.CommandText = sql;

        var result = new List<InventoryReportRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var row = new InventoryReportRow
            {
                ItemId = GetInt64(reader, "ItemID"),
                ItemCode = GetString(reader, "ItemCode"),
                ItemName = GetString(reader, "ItemName"),
                Unit = GetString(reader, "Unit", "UnitName"),
                ReorderPoint = GetDecimal(reader, "ReorderPo", "ReorderPoint", "ReOrderPoint"),
                BeginQuantity = GetDecimal(reader, "BegQuanti", "BeginQ", "BGQuantity", "BeginQuantity"),
                ReceiveQuantity = GetDecimal(reader, "RecQuanti", "RecQty", "ReceiveQuantity"),
                IssueQuantity = GetDecimal(reader, "IssQuanti", "IssQty", "IssueQuantity"),
                EndQuantity = GetDecimal(reader, "EndQuanti", "EndQ", "EndQuantity")
            };
            if (row.EndQuantity == 0 && (row.BeginQuantity != 0 || row.ReceiveQuantity != 0 || row.IssueQuantity != 0))
            {
                row.EndQuantity = row.BeginQuantity + row.ReceiveQuantity - row.IssueQuantity;
            }
            result.Add(row);
        }

        return result;
    }

    private string BuildLegacyItemFilter()
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(Filter.ItemCode))
        {
            parts.Add($"ItemCode Like ''%{Filter.ItemCode.Trim().Replace("'", "''")}%''");
        }
        if (!string.IsNullOrWhiteSpace(Filter.ItemName))
        {
            parts.Add($"ItemName Like ''%{Filter.ItemName.Trim().Replace("'", "''")}%''");
        }
        return string.Join(" AND ", parts);
    }

    private static string GetString(SqlDataReader reader, params string[] names)
    {
        foreach (var name in names)
        {
            var ordinal = TryGetOrdinal(reader, name);
            if (ordinal >= 0 && !reader.IsDBNull(ordinal)) return Convert.ToString(reader.GetValue(ordinal)) ?? string.Empty;
        }
        return string.Empty;
    }

    private static long GetInt64(SqlDataReader reader, params string[] names)
    {
        foreach (var name in names)
        {
            var ordinal = TryGetOrdinal(reader, name);
            if (ordinal >= 0 && !reader.IsDBNull(ordinal)) return Convert.ToInt64(reader.GetValue(ordinal));
        }
        return 0;
    }

    private static decimal GetDecimal(SqlDataReader reader, params string[] names)
    {
        foreach (var name in names)
        {
            var ordinal = TryGetOrdinal(reader, name);
            if (ordinal >= 0 && !reader.IsDBNull(ordinal)) return Convert.ToDecimal(reader.GetValue(ordinal));
        }
        return 0;
    }

    private static int TryGetOrdinal(SqlDataReader reader, string name)
    {
        for (var i = 0; i < reader.FieldCount; i++)
        {
            if (string.Equals(reader.GetName(i), name, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    private PagePermissions GetUserPermissions()
    {
        var perms = new PagePermissions();
        if (IsAdminRole())
        {
            perms.AllowedNos = Enumerable.Range(1, 20).ToList();
        }
        else
        {
            perms.AllowedNos = _permissionService.GetPermissionsForPage(GetCurrentRoleId(), FunctionId);
        }
        return perms;
    }

    private int GetCurrentEmployeeId()
    {
        var value = User.FindFirst("EmployeeID")?.Value;
        return int.TryParse(value, out var employeeId) ? employeeId : 0;
    }

    private int? GetEffectiveKpGroupId()
    {
        var employeeId = GetCurrentEmployeeId();
        if (employeeId <= 0) return null;

        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
        conn.Open();

        var storeGr = GetCurrentStoreGr();
        if (storeGr > 0)
        {
            using var storeCmd = new SqlCommand("SELECT TOP 1 ISNULL(DeptID, 0) FROM dbo.INV_StoreList WHERE StoreID=@StoreID", conn);
            storeCmd.Parameters.Add("@StoreID", SqlDbType.Int).Value = storeGr;
            var storeDeptId = Convert.ToInt32(storeCmd.ExecuteScalar() ?? 0);
            if (storeDeptId > 0) return storeDeptId;
        }

        using var empCmd = new SqlCommand("SELECT TOP 1 ISNULL(DeptID, 0) FROM dbo.MS_Employee WHERE EmployeeID=@EmployeeID", conn);
        empCmd.Parameters.Add("@EmployeeID", SqlDbType.Int).Value = employeeId;
        var employeeDeptId = Convert.ToInt32(empCmd.ExecuteScalar() ?? 0);
        return employeeDeptId > 0 ? employeeDeptId : null;
    }

    private int GetCurrentRoleId()
    {
        var value = User.FindFirst("RoleID")?.Value;
        return int.TryParse(value, out var roleId) ? roleId : 0;
    }

    private bool IsAdminRole() => string.Equals(User.FindFirst("IsAdminRole")?.Value, "True", StringComparison.OrdinalIgnoreCase);

    private int GetCurrentKpGroupId()
    {
        var value = User.FindFirst("KPGroupID")?.Value;
        return int.TryParse(value, out var kpGroupId) ? kpGroupId : 0;
    }

    private int GetCurrentStoreGr()
    {
        var value = User.FindFirst("StoreGR")?.Value;
        return int.TryParse(value, out var storeGr) ? storeGr : 0;
    }
}

public class InventoryReportFilter
{
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? ItemCode { get; set; }
    public string? ItemName { get; set; }
    public int? KpGroupId { get; set; }
    public int StoreId { get; set; } = -1;
    public int ViewType { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
}

public class InventoryReportRow
{
    public long ItemId { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal ReorderPoint { get; set; }
    public decimal BeginQuantity { get; set; }
    public decimal ReceiveQuantity { get; set; }
    public decimal IssueQuantity { get; set; }
    public decimal EndQuantity { get; set; }
}

internal sealed class ReportSubmitInfo
{
    public int? PreparedBy { get; set; }
    public int? CheckedBy { get; set; }
}
