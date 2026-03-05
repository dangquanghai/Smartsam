
using System.Text;
using Dapper;
using System.Net;
using System.Net.Mail;
using System.Data;
using System.Data.SqlClient;
using SmartSam.Models;
using QRCoder;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;


namespace SmartSam.Services
{
    public class SixMonthsStayReviewService
    {
        private readonly IConfiguration _config;

        public SixMonthsStayReviewService(IConfiguration config)
        {
            _config = config;
        }

        public async Task Process6MonthsStayReview()
        {
            using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                await conn.OpenAsync();

                // 1. Lấy thông tin Manager CSS
                var manager = await conn.QueryFirstOrDefaultAsync("SELECT TOP 1 EmployeeName, PositionTitle FROM MS_Employee WHERE IsCDManager = 1");

                // 2. Lấy Link mẫu
                var configLinks = await conn.QueryFirstOrDefaultAsync("SELECT TOP 1 EnglishLink, JapaneseLink FROM CS_SixMonthsStayReviewLink");

                // 3. Lấy danh sách khách hàng (Theo SQL bạn cung cấp)
                string sqlList = @"
                SELECT 
                c.ContractID, 
                c.ContractNo, 
                -- Nếu ApmtContract NULL thì lấy CurrentApartmentNo, ngược lại lấy ApmtContract
                COALESCE(c.ApmtContract, c.CurrentApartmentNo) AS CurrentApartmentNo,
                c.ApmtContract, 
                c.CurrentApartmentNo,
                cus.CustomerName, 
                cus.Title, 
                com.CompanyName, 
                c.ContractFromDate, 
                c.ContractToDate, 
                c.SentReview, 
                c.ReceivedReview
                FROM CM_Contract c 
                INNER JOIN CM_Customer cus ON c.Representator = cus.CustomerID
                LEFT JOIN CM_Company com ON c.CompanyID = com.CompanyID
                WHERE IsShortTerm = 0 
                AND IsOtherContract = 0 
                AND ContractStatus = 2 
                AND (c.SentReview = 0 OR c.SentReview IS NULL)
                AND DATEDIFF(MONTH, c.ContractFromDate, GETDATE()) >= 6";

                var tenants = (await conn.QueryAsync<SixMonthsStayReviewVM>(sqlList)).ToList();
                if (tenants.Count == 0) return;

                // 4. Khởi tạo danh sách chuẩn bị cho file Word và Email
                List<byte[]> allQrEng = new List<byte[]>();
                List<byte[]> allQrJap = new List<byte[]>();
                StringBuilder tableRows = new StringBuilder();
                List<LinkedResource> qrResources = new List<LinkedResource>();

                foreach (var item in tenants)
                {
                    string urlEng = $"{configLinks.EnglishLink}={item.ContractID}";
                    string urlJap = $"{configLinks.JapaneseLink}={item.ContractID}";

                    byte[] qrEngBytes = GenerateQRCode(urlEng);
                    byte[] qrJapBytes = GenerateQRCode(urlJap);

                    allQrEng.Add(qrEngBytes);
                    allQrJap.Add(qrJapBytes);

                    // Gom dữ liệu vào bảng HTML cho Email (giữ nguyên logic cũ của bạn)
                    string cidEng = $"qr_eng_{item.ContractID}";
                    string cidJap = $"qr_jap_{item.ContractID}";
                    qrResources.Add(CreateLinkedResource(qrEngBytes, cidEng));
                    qrResources.Add(CreateLinkedResource(qrJapBytes, cidJap));

                }

                // 5. Tạo file Word (Mỗi khách 1 trang Letter)
                byte[] wordFile = GenerateSurveyLetters(tenants, allQrEng, allQrJap, manager);

                // 6. Gửi Email đính kèm file Word
                string htmlBody = $@"
                <html>
                <body style='font-family: Arial, sans-serif; line-height: 1.6;'>
                    <p>Dear CSD,</p>
                    <p>The system is filtering the list of long-term residents who have been living at Saigon Sky Garden for 6 months or more and merging it into a Word file for CSD to print out> Please Check it before sending to the residents.</p>
                    <p>Please double-check the information before sending it.</p>
                    <p>Sincerely,</p>
                    <hr style='border:none; border-top:1px solid #eee; margin-top:20px;' />
                    <p style='font-size: 11px; color: #888;'>*This is an automated email, please do not reply to this email.</p>
                </body>
                </html>";
                await SendEmailWithAttachment(htmlBody, wordFile);

                // 7. CHỈ KHI GỬI MAIL THÀNH CÔNG MỚI CHẠY ĐẾN ĐÂY ĐỂ ĐÁNH DẤU
                // Chúng ta cập nhật hàng loạt cho tất cả khách hàng có trong danh sách vừa gửi
                string sqlUpdate = "UPDATE CM_Contract SET SentReview = 1 WHERE ContractID IN @Ids";
                var ids = tenants.Select(t => t.ContractID).ToList();

                await conn.ExecuteAsync(sqlUpdate, new { Ids = ids });

            }
        }
        private byte[] GenerateSurveyLetters(List<SixMonthsStayReviewVM> tenants, List<byte[]> qrEng, List<byte[]> qrJap, dynamic manager)
        {
            try
            {
                using (MemoryStream mem = new MemoryStream())
                {
                    using (WordprocessingDocument wordDoc = WordprocessingDocument.Create(mem, WordprocessingDocumentType.Document))
                    {
                        MainDocumentPart mainPart = wordDoc.AddMainDocumentPart();
                        mainPart.Document = new Document(new Body());
                        Body body = mainPart.Document.Body;

                        for (int i = 0; i < tenants.Count; i++)
                        {
                            var t = tenants[i];

                            // 1. 5 dòng trống đầu trang
                            for (int j = 0; j < 5; j++) { body.AppendChild(new Paragraph()); }

                            // 2. Dear khách hàng
                            Paragraph pDear = body.AppendChild(new Paragraph());
                            Run rDear = pDear.AppendChild(new Run());
                            rDear.RunProperties = new DocumentFormat.OpenXml.Wordprocessing.RunProperties(
                                new DocumentFormat.OpenXml.Wordprocessing.Bold(),
                                new DocumentFormat.OpenXml.Wordprocessing.FontSize { Val = "26" }
                            );
                            rDear.AppendChild(new Text($"Dear {t.Title} {t.CustomerName},"));

                            Paragraph pApmt = body.AppendChild(new Paragraph());
                            Run rApmt = pApmt.AppendChild(new Run());
                            rApmt.RunProperties = new DocumentFormat.OpenXml.Wordprocessing.RunProperties(
                                new DocumentFormat.OpenXml.Wordprocessing.Bold()
                            );
                            rApmt.AppendChild(new Text($"Apartment No: {t.CurrentApartmentNo}"));

                            // 3. Nội dung văn bản

                            body.AppendChild(new Paragraph(new Run(new Text("We greatly appreciate you choosing our apartment."))));
                            body.AppendChild(new Paragraph(new Run(new Text("To maintain and continuously improve the quality of our services, we would like to request your feedback on the details of the services we provide."))));
                            body.AppendChild(new Paragraph(new Run(new Text("Please scan one of the two QR codes below, which version you prefer."))));

                            // 4. 1 dòng trống trước bảng QR
                            body.AppendChild(new Paragraph());

                            // 5. Bảng QR
                            DocumentFormat.OpenXml.Wordprocessing.Table table = new DocumentFormat.OpenXml.Wordprocessing.Table();
                            table.AppendChild(new TableProperties(new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" }));

                            TableRow row = new TableRow();

                            // Ô English (Căn giữa)
                            TableCell cellEng = new TableCell();
                            if (qrEng[i] != null) InsertImage(mainPart, cellEng, qrEng[i]);
                            Paragraph pLabelEng = cellEng.AppendChild(new Paragraph(new Run(new Text("English Version"))
                            {
                                RunProperties = new DocumentFormat.OpenXml.Wordprocessing.RunProperties(new DocumentFormat.OpenXml.Wordprocessing.Bold())
                            }));
                            pLabelEng.ParagraphProperties = new ParagraphProperties(new Justification { Val = JustificationValues.Center });
                            row.Append(cellEng);

                            // Ô Japanese (Căn giữa)
                            TableCell cellJap = new TableCell();
                            if (qrJap[i] != null) InsertImage(mainPart, cellJap, qrJap[i]);
                            Paragraph pLabelJap = cellJap.AppendChild(new Paragraph(new Run(new Text("Japanese Version"))
                            {
                                RunProperties = new DocumentFormat.OpenXml.Wordprocessing.RunProperties(new DocumentFormat.OpenXml.Wordprocessing.Bold())
                            }));
                            pLabelJap.ParagraphProperties = new ParagraphProperties(new Justification { Val = JustificationValues.Center });
                            row.Append(cellJap);

                            table.Append(row);
                            body.Append(table);

                            // 6. 1 dòng trống trước chữ ký
                            body.AppendChild(new Paragraph());
                            body.AppendChild(new Paragraph(new Run(new Text("Thanks and Best Regard"))));

                            Paragraph pPos = body.AppendChild(new Paragraph());
                            pPos.AppendChild(new Run(new Text(manager?.PositionTitle ?? "Manager"))
                            {
                                RunProperties = new DocumentFormat.OpenXml.Wordprocessing.RunProperties(new DocumentFormat.OpenXml.Wordprocessing.Italic())
                            });

                            Paragraph pManager = body.AppendChild(new Paragraph());
                            pManager.AppendChild(new Run(new Text(manager?.EmployeeName ?? "CSS Team"))
                            {
                                RunProperties = new DocumentFormat.OpenXml.Wordprocessing.RunProperties(new DocumentFormat.OpenXml.Wordprocessing.Bold())
                            });

                            if (i < tenants.Count - 1)
                                body.AppendChild(new Paragraph(new Run(new Break() { Type = BreakValues.Page })));
                        }
                        mainPart.Document.Save();
                    }
                    return mem.ToArray();
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"[Lỗi OpenXML Final] {ex.Message}");
            }
        }
        private void InsertImage(MainDocumentPart mainPart, TableCell cell, byte[] imageBytes)
        {
            ImagePart imagePart = mainPart.AddImagePart(ImagePartType.Png);
            using (MemoryStream stream = new MemoryStream(imageBytes)) { imagePart.FeedData(stream); }
            string rId = mainPart.GetIdOfPart(imagePart);

            var element = new Drawing(
                new DocumentFormat.OpenXml.Drawing.Wordprocessing.Inline(
                    new DocumentFormat.OpenXml.Drawing.Wordprocessing.Extent() { Cx = 1270000L, Cy = 1270000L },
                    new DocumentFormat.OpenXml.Drawing.Wordprocessing.DocProperties()
                    {
                        Id = new UInt32Value(1U), // Sửa tại đây
                        Name = "QR Code"
                    },
                    new DocumentFormat.OpenXml.Drawing.Graphic(
                        new DocumentFormat.OpenXml.Drawing.GraphicData(
                            new DocumentFormat.OpenXml.Drawing.Pictures.Picture(
                                new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualPictureProperties(
                                    new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualDrawingProperties()
                                    {
                                        Id = new UInt32Value(0U), // Sửa tại đây
                                        Name = "QR.png"
                                    },
                                    new DocumentFormat.OpenXml.Drawing.Pictures.NonVisualPictureDrawingProperties()),
                                new DocumentFormat.OpenXml.Drawing.Pictures.BlipFill(
                                    new DocumentFormat.OpenXml.Drawing.Blip() { Embed = rId },
                                    new DocumentFormat.OpenXml.Drawing.Stretch(new DocumentFormat.OpenXml.Drawing.FillRectangle())),
                                new DocumentFormat.OpenXml.Drawing.Pictures.ShapeProperties(
                                    new DocumentFormat.OpenXml.Drawing.Transform2D(
                                        new DocumentFormat.OpenXml.Drawing.Offset() { X = 0L, Y = 0L },
                                        new DocumentFormat.OpenXml.Drawing.Extents() { Cx = 1270000L, Cy = 1270000L }),
                                    new DocumentFormat.OpenXml.Drawing.PresetGeometry() { Preset = DocumentFormat.OpenXml.Drawing.ShapeTypeValues.Rectangle }))
                        )
                        { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" })
                )
            );

            // Căn giữa ảnh trong ô
            Paragraph p = new Paragraph(new Run(element));
            p.ParagraphProperties = new ParagraphProperties(new Justification { Val = JustificationValues.Center });
            cell.AppendChild(p);
        }
        private byte[] GenerateQRCode(string url)
        {
            using var qrGenerator = new QRCodeGenerator();
            var qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new PngByteQRCode(qrCodeData);
            return qrCode.GetGraphic(15);
        }

        private LinkedResource CreateLinkedResource(byte[] imageBytes, string contentId)
        {
            var res = new LinkedResource(new MemoryStream(imageBytes), "image/png");
            res.ContentId = contentId;
            res.TransferEncoding = System.Net.Mime.TransferEncoding.Base64;
            return res;
        }

        private async Task SendEmailWithAttachment(string htmlBody, byte[] wordFile)
        {
            string senderEmail = _config.GetValue<string>("EmailSettings:SenderEmail");
            string mailPass = _config.GetValue<string>("EmailSettings:Password");
            string mailServer = _config.GetValue<string>("EmailSettings:MailServer");
            int mailPort = _config.GetValue<int>("EmailSettings:MailPort");

            try
            {
                var mail = new MailMessage
                {
                    From = new MailAddress(senderEmail, "Smartsam System"),
                    Subject = $"[Survey 6 Months] Resident Survey Letters - {DateTime.Now:MM/yyyy}",
                    Body = htmlBody,
                    IsBodyHtml = true
                };

                //mail.To.Add("hai.dq@saigonskygarden.com.vn");
                mail.To.Add("bao.q@saigonskygarden.com.vn");
                mail.CC.Add("phung.ctm@saigonskygarden.com.vn");
                mail.CC.Add("lan.dtm@saigonskygarden.com.vn");

                // Đính kèm file Word duy nhất
                if (wordFile != null && wordFile.Length > 0)
                {
                    MemoryStream ms = new MemoryStream(wordFile);
                    Attachment attachment = new Attachment(ms, $"Survey_Letters_{DateTime.Now:yyyyMMdd}.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
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
                Console.WriteLine($"Lỗi gửi mail: {ex.Message}");
                throw;
            }
        }
    }
}