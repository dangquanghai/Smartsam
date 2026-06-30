using System.Net;
using System.Net.Mail;
using ClosedXML.Excel;
using Dapper;
using Microsoft.Data.SqlClient;
using SmartSam.Helpers;

namespace SmartSam.Services
{
    // Job hàng ngày (1h sáng): tạo file Excel "APARTMENT STATUS REPORT" gồm 3 phần
    // (Living / Will be checked Out / Will be checked In) cho hợp đồng Short-term
    // rồi gửi mail cho Reception, CC Bao & Hai. Mẫu học theo SixMonthsStayReviewService.
    public class ApartmentStatusReportService
    {
        private readonly IConfiguration _config;

        public ApartmentStatusReportService(IConfiguration config)
        {
            _config = config;
        }

        public async Task ProcessApartmentStatusReport()
        {
            // X = ngày job chạy (hôm nay)
            DateTime reportDate = DateTime.Today;
            DateTime generatedAt = DateTime.Now;     // thời điểm thực tế job chạy/tạo báo cáo
            DateTime dayStart = reportDate;          // đầu ngày X
            DateTime nextDay = reportDate.AddDays(1); // dùng < nextDay = cuối ngày X (gồm cả giờ)

            using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                await conn.OpenAsync();

                // SELECT gốc theo SQL người dùng cung cấp, chỉ short-term.
                // 3 phần khác nhau ở phần WHERE: Living / Check Out / Check In.
                const string baseSelect = @"
                    SELECT
                        c.CurrentApartmentNo,
                        cus.CustomerName,
                        c.Occupy,
                        c.PlanCheckinDate,
                        c.PlanCheckoutDate,
                        pb.PaymentByName
                    FROM dbo.CM_Contract c
                    INNER JOIN dbo.CM_Customer cus ON c.Representator = cus.CustomerID
                    INNER JOIN dbo.CM_ContractPaymentBy pb ON c.PaymentByID = pb.PaymentByID
                    WHERE c.IsShortTerm = 1 ";

                // Phần 1: Living => ContractStatus = 2
                string sqlLiving = baseSelect + @"
                    AND c.ContractStatus = 2
                    ORDER BY c.CurrentApartmentNo;";

                // Phần 2: Will be checked Out => ContractStatus = 2 và PlanCheckoutDate trong ngày X
                string sqlCheckOut = baseSelect + @"
                    AND c.ContractStatus = 2
                    AND c.PlanCheckoutDate >= @DayStart
                    AND c.PlanCheckoutDate < @NextDay
                    ORDER BY c.CurrentApartmentNo;";

                // Phần 3: Will be checked In => ContractStatus = 1 và PlanCheckinDate trong ngày X
                string sqlCheckIn = baseSelect + @"
                    AND c.ContractStatus = 1
                    AND c.PlanCheckinDate >= @DayStart
                    AND c.PlanCheckinDate < @NextDay
                    ORDER BY c.CurrentApartmentNo;";

                var pars = new { DayStart = dayStart, NextDay = nextDay };

                var living = (await conn.QueryAsync<ApartmentStatusRow>(sqlLiving)).ToList();
                var willCheckOut = (await conn.QueryAsync<ApartmentStatusRow>(sqlCheckOut, pars)).ToList();
                var willCheckIn = (await conn.QueryAsync<ApartmentStatusRow>(sqlCheckIn, pars)).ToList();

                // Tạo file Excel
                byte[] excelFile = GenerateExcel(reportDate, living, willCheckOut, willCheckIn);

                // Nội dung email (theo mẫu WrapInNotifyTemplate)
                string message = $@"
                    <p style='font-weight: bold; font-size: 16px; color: #333;'>Dear Reception Team,</p>

                    <p>Please find attached the <b>Apartment Status Report</b> for short-term contracts as of
                       <b>{reportDate:MMM d, yyyy}</b>.</p>

                    <p style='color: #555;'>This report is generated automatically every day at <b>01:00 AM (GMT+7)</b>.
                       Generated at: <b>{generatedAt:dd/MM/yyyy HH:mm}</b>.</p>

                    <div style='background-color: #f9f9f9; border-left: 4px solid #16a085; padding: 15px; margin: 15px 0; color: #555;'>
                        <p style='margin: 0 0 6px 0;'>The report contains 3 sections:</p>
                        <ul style='margin: 0; padding-left: 18px;'>
                            <li>Short-term Living: <b>{living.Count}</b> apartment(s)</li>
                            <li>Short-term Will be checked Out (today): <b>{willCheckOut.Count}</b> apartment(s)</li>
                            <li>Short-term Will be checked In (today): <b>{willCheckIn.Count}</b> apartment(s)</li>
                        </ul>
                    </div>

                    <p>The full detail is attached as an Excel file.</p>";

                string htmlBody = EmailTemplateHelper.WrapInNotifyTemplate(
                    "APARTMENT STATUS REPORT", "#16a085", reportDate, message);

                await SendEmailWithAttachment(htmlBody, excelFile, reportDate);
            }
        }

        private byte[] GenerateExcel(
            DateTime reportDate,
            List<ApartmentStatusRow> living,
            List<ApartmentStatusRow> willCheckOut,
            List<ApartmentStatusRow> willCheckIn)
        {
            using var workbook = new XLWorkbook();
            var ws = workbook.Worksheets.Add("Apartment Status");

            const int lastCol = 7; // Order, Apartment, Customer Name, Occ, CheckIn Plan, Check Out Plan, Payment By

            // Tiêu đề chính
            ws.Cell(1, 1).Value = "APARTMENT STATUS REPORT";
            ws.Range(1, 1, 1, lastCol).Merge();
            ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(1, 1).Style.Font.FontSize = 20;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontColor = XLColor.Blue;
            ws.Row(1).Height = 36;

            ws.Cell(2, 1).Value = $"Report date: {reportDate:dd/MM/yyyy}";
            ws.Range(2, 1, 2, lastCol).Merge();
            ws.Cell(2, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(2, 1).Style.Font.Italic = true;
            ws.Cell(2, 1).Style.Font.FontColor = XLColor.Gray;

            int currentRow = 4;
            currentRow = WriteSection(ws, currentRow, "1. Short-term Living", living, lastCol);
            currentRow += 1;
            currentRow = WriteSection(ws, currentRow, "2. Short-term Will be checked Out", willCheckOut, lastCol);
            currentRow += 1;
            WriteSection(ws, currentRow, "3. Short-term Will be checked In", willCheckIn, lastCol);

            ws.Columns().AdjustToContents();

            using var stream = new MemoryStream();
            workbook.SaveAs(stream);
            return stream.ToArray();
        }

        // Ghi 1 phần: dòng tiêu đề phần + dòng header 6 cột + dữ liệu. Trả về dòng kế tiếp.
        private int WriteSection(IXLWorksheet ws, int startRow, string sectionTitle, List<ApartmentStatusRow> rows, int lastCol)
        {
            int row = startRow;

            // Tiêu đề phần
            ws.Cell(row, 1).Value = sectionTitle;
            ws.Range(row, 1, row, lastCol).Merge();
            ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
            ws.Cell(row, 1).Style.Font.FontSize = 13;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#16a085");
            row++;

            // Header cột
            string[] headers = { "Order", "Apartment", "Customer Name", "Occ", "CheckIn Plan", "Check Out Plan", "Payment By" };
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.Blue;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#eaf2f8");
                cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            }
            row++;

            if (rows.Count == 0)
            {
                ws.Cell(row, 1).Value = "No data";
                ws.Range(row, 1, row, lastCol).Merge();
                ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(row, 1).Style.Font.Italic = true;
                ws.Cell(row, 1).Style.Font.FontColor = XLColor.Gray;
                return row + 1;
            }

            int order = 1;
            foreach (var r in rows)
            {
                ws.Cell(row, 1).Value = order;
                ws.Cell(row, 2).Value = r.CurrentApartmentNo;
                ws.Cell(row, 3).Value = r.CustomerName;
                ws.Cell(row, 4).Value = r.Occupy;

                if (r.PlanCheckinDate.HasValue)
                {
                    ws.Cell(row, 5).Value = r.PlanCheckinDate.Value;
                    ws.Cell(row, 5).Style.NumberFormat.Format = "dd/MM/yyyy";
                }
                if (r.PlanCheckoutDate.HasValue)
                {
                    ws.Cell(row, 6).Value = r.PlanCheckoutDate.Value;
                    ws.Cell(row, 6).Style.NumberFormat.Format = "dd/MM/yyyy";
                }

                ws.Cell(row, 7).Value = r.PaymentByName;

                for (int i = 1; i <= lastCol; i++)
                {
                    ws.Cell(row, i).Style.Font.FontSize = 11;
                    ws.Cell(row, i).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                }
                // Customer Name & Payment By căn trái cho dễ đọc
                ws.Cell(row, 3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
                ws.Cell(row, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;

                order++;
                row++;
            }

            return row;
        }

        private async Task SendEmailWithAttachment(string htmlBody, byte[] excelFile, DateTime reportDate)
        {
            string senderEmail = _config.GetValue<string>("EmailSettings:SenderEmail");
            string mailPass = _config.GetValue<string>("EmailSettings:Password");
            string mailServer = _config.GetValue<string>("EmailSettings:MailServer");
            int mailPort = _config.GetValue<int>("EmailSettings:MailPort");

            try
            {
                var mail = new MailMessage
                {
                    From = new MailAddress(senderEmail, "New Smartsam System"),
                    Subject = $"[Apartment Status] APARTMENT STATUS REPORT - {reportDate:dd/MM/yyyy}",
                    Body = htmlBody,
                    IsBodyHtml = true
                };

                mail.To.Add("reception@saigonskygarden.com.vn");
                mail.CC.Add("bao.q@saigonskygarden.com.vn");
                mail.CC.Add("hai.dq@saigonskygarden.com.vn");

                if (excelFile != null && excelFile.Length > 0)
                {
                    var ms = new MemoryStream(excelFile);
                    var attachment = new Attachment(
                        ms,
                        $"ApartmentStatusReport_{reportDate:yyyyMMdd}.xlsx",
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet");
                    mail.Attachments.Add(attachment);
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
                Console.WriteLine($"[ApartmentStatusReport] Lỗi gửi mail: {ex.Message}");
                throw;
            }
        }
    }

    public class ApartmentStatusRow
    {
        public string CurrentApartmentNo { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public int? Occupy { get; set; }
        public DateTime? PlanCheckinDate { get; set; }
        public DateTime? PlanCheckoutDate { get; set; }
        public string PaymentByName { get; set; } = string.Empty;
    }
}
