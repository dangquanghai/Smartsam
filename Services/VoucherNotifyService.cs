using System.Text;
using Dapper;
using System.Net;
using System.Net.Mail;
using System.Data.SqlClient;
using SmartSam.Helpers;

namespace SmartSam.Services
{
    public class VoucherNotifyService
    {
        private readonly IConfiguration _config;

        public VoucherNotifyService(IConfiguration config)
        {
            _config = config;
        }

        public async Task ProcessVoucherNotification()
        {
            using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                await conn.OpenAsync();

                var yesterday = (await conn.ExecuteScalarAsync<DateTime>("exec HaigetDate")).AddDays(-1);
                var lastCollect = await conn.ExecuteScalarAsync<DateTime>("SELECT LastDateToCollectItemFlow FROM INV_ItemFlowNotify");

                if (yesterday.Date > lastCollect.Date)
                {
                    // --- XỬ LÝ RECEIVING VOUCHER ---
                    string bodyReceive = await conn.ExecuteScalarAsync<string>("SELECT dbo.FNC_CollectReceiveVoucher()");
                    if (!string.IsNullOrEmpty(bodyReceive) && bodyReceive != "-")
                    {
                        string subject = $"[New-Smartsam] Receiving Vouchers Confirmed - {yesterday:dd/MM/yyyy}";
                        // Bọc template màu xanh dương cho Receiving
                        string finalHtml = EmailTemplateHelper.WrapInNotifyTemplate("Receiving Vouchers", "#3498db", yesterday, bodyReceive);
                        await SendNotifyEmail("thao.ltt@saigonskygarden.com.vn", subject, finalHtml, "hai.dq@saigonskygarden.com.vn", "nga.ctn@saigonskygarden.com.vn");
                    }

                    // --- XỬ LÝ ISSUE VOUCHER ---
                    string bodyIssue = await conn.ExecuteScalarAsync<string>("SELECT dbo.FNC_CollectIssueVoucher()");
                    if (!string.IsNullOrEmpty(bodyIssue) && bodyIssue != "-")
                    {
                        string subject = $"[New-Smartsam] Issue Vouchers Confirmed - {yesterday:dd/MM/yyyy}";
                        // Bọc template màu cam cho Issue
                        string finalHtml = EmailTemplateHelper.WrapInNotifyTemplate("Issue Vouchers", "#e67e22", yesterday, bodyIssue);
                        await SendNotifyEmail("trinh.thv@saigonskygarden.com.vn", subject, finalHtml, "hai.dq@saigonskygarden.com.vn", "nga.ctn@saigonskygarden.com.vn");
                    }

                    string sqlUpdate = "UPDATE INV_ItemFlowNotify SET LastDateToCollectItemFlow = @Yesterday";
                    await conn.ExecuteAsync(sqlUpdate, new { Yesterday = yesterday });
                    Console.WriteLine($"[VoucherNotify] Success for {yesterday:yyyy-MM-dd}");
                }
            }
        }

        // Hàm bổ trợ để trang trí HTML
        /*
        private string WrapInTemplate(string title, string color, DateTime date, string tableContent)
        {
            return $@"
    <div style='font-family: Segoe UI, Arial, sans-serif; max-width: 800px; margin: auto; border: 1px solid #eee; border-radius: 8px; overflow: hidden; box-shadow: 0 4px 10px rgba(0,0,0,0.1);'>
        <div style='background-color: {color}; color: white; padding: 20px; text-align: center;'>
            <h2 style='margin: 0; font-size: 24px; text-transform: uppercase;'>{title}</h2>
            <p style='margin: 5px 0 0 0; opacity: 0.9;'>Notification Date: {date:dd/MM/yyyy}</p>
        </div>
        <div style='padding: 20px;'>
            <p style='color: #333;'>Dear Team,</p>
            <p style='color: #555;'>The system has detected new confirmed vouchers for yesterday. Please find the details below:</p>
            
            <div style='margin-top: 20px;'>
                <style>
                    table {{ border-collapse: collapse; width: 100% !important; border: 1px solid #ddd; }}
                    th {{ background-color: #f8f9fa; color: #333; padding: 12px; text-align: left; border-bottom: 2px solid {color}; }}
                    td {{ padding: 10px; border: 1px solid #eee; color: #444; }}
                    tr:nth-child(even) {{ background-color: #fafafa; }}
                </style>
                {tableContent}
            </div>

            <div style='margin-top: 30px; padding-top: 15px; border-top: 1px solid #eee; font-size: 12px; color: #888; text-align: center;'>
                <p>This is an automated message from <b>SmartSam System</b>.<br/>
                Please do not reply to this email.</p>
            </div>
        </div>
    </div>";
        }
        */
        private async Task SendNotifyEmail(string to, string subject, string body, params string[] ccList)
        {
            string senderEmail = _config.GetValue<string>("EmailSettings:SenderEmail");
            string mailPass = _config.GetValue<string>("EmailSettings:Password");
            string mailServer = _config.GetValue<string>("EmailSettings:MailServer");
            int mailPort = _config.GetValue<int>("EmailSettings:MailPort");

            try
            {
                var mail = new MailMessage
                {
                    From = new MailAddress(senderEmail, "Smartsam System - INV Notify"),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };

                // Thêm người nhận chính (To)
                // Nếu có nhiều người nhận chính cách nhau dấu phẩy, dùng mail.To.Add(to)
                mail.To.Add(to);

                // Duyệt danh sách CC và thêm vào mail
                if (ccList != null && ccList.Length > 0)
                {
                    foreach (var ccEmail in ccList)
                    {
                        if (!string.IsNullOrWhiteSpace(ccEmail))
                        {
                            mail.CC.Add(ccEmail.Trim());
                        }
                    }
                }

                using (var smtp = new SmtpClient(mailServer, mailPort))
                {
                    smtp.EnableSsl = true;
                    smtp.UseDefaultCredentials = false;
                    smtp.Credentials = new NetworkCredential(senderEmail, mailPass);
                    await smtp.SendMailAsync(mail);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VoucherNotify] Lỗi gửi mail: {ex.Message}");
            }
        }
    }
}