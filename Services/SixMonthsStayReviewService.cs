
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
                    <p>The system filters out long-term contracts which have been living at Saigon Sky Garden for 6 months or more and merging it into a Word file for CSD to print out> Please Check it before sending to the residents.</p>
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

                        SectionProperties sectionProps = new SectionProperties();
                        // 1. CẬP NHẬT LỀ: Left/Right tăng lên 1080 (rộng gấp 1.5 lần mức 720 cũ)
                        // Giữ Top/Bottom ở mức 500 để không bị tràn trang
                        PageMargin pageMargin = new PageMargin() { Top = 500, Bottom = 500, Left = 1080, Right = 1080 };
                        sectionProps.Append(pageMargin);
                        body.Append(sectionProps);

                        for (int i = 0; i < tenants.Count; i++)
                        {
                            var t = tenants[i];

                            // 1. Ngày tháng
                            Paragraph pDate = CreateCompactParagraph(JustificationValues.Right);
                            pDate.AppendChild(new Run(new Text(DateTime.Now.ToString("MMMM dd, yyyy"))) { RunProperties = new RunProperties(new FontSize { Val = "24" }) });
                            body.Append(pDate);

                            // 2. Tiêu đề QUESTIONNAIRE
                            Paragraph pTitle = CreateCompactParagraph(JustificationValues.Center);
                            pTitle.AppendChild(new Run(new Text("QUESTIONNAIRE")) { RunProperties = new RunProperties(new Bold(), new FontSize { Val = "28" }) });
                            body.Append(pTitle);

                            body.AppendChild(new Paragraph()); // Dòng trắng dưới tiêu đề

                            // 3. Bảng Tên khách & Số phòng
                            Table infoTable = new Table();
                            infoTable.AppendChild(new TableProperties(new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
                                new TableBorders(new TopBorder { Val = BorderValues.None }, new BottomBorder { Val = BorderValues.None },
                                new LeftBorder { Val = BorderValues.None }, new RightBorder { Val = BorderValues.None },
                                new InsideHorizontalBorder { Val = BorderValues.None }, new InsideVerticalBorder { Val = BorderValues.None })));

                            TableRow infoRow = new TableRow();
                            infoRow.Append(
                                new TableCell(new Paragraph(new Run(new Text($"Dear {t.Title} {t.CustomerName},")) { RunProperties = new RunProperties(new Bold(), new FontSize { Val = "24" }) })),
                                new TableCell(new Paragraph(new Run(new Text($"Apartment: {t.CurrentApartmentNo}")) { RunProperties = new RunProperties(new Bold(), new FontSize { Val = "24" }) })
                                { ParagraphProperties = new ParagraphProperties(new Justification { Val = JustificationValues.Right }) })
                            );
                            infoTable.Append(infoRow);
                            body.Append(infoTable);

                            // --- ĐÃ XÓA DÒNG TRẮNG Ở ĐÂY ĐỂ BÙ LẠI PHẦN LỀ ---

                            // 4. Nội dung chính - Nối tiếp ngay sau bảng thông tin
                            AddCompactBilingualSection(body,
                                "We would like to express our hearty thanks for your choosing Saigon Sky Garden as your home during your stay in Ho Chi Minh City.",
                                "ホーチミン市ご滞在中にサイゴン スカイ ガーデンをお選びいただき、心より感謝申し上げます。");

                            AddCompactBilingualSection(body,
                                "We, all staffs of Saigon Sky Garden, always do our best to make your stay more comfortable and happy day by day.",
                                "サイゴン スカイ ガーデンのスタッフ一同、お客様に日々より快適なご滞在をお届けできるよう、常に最善を尽くしております。");

                            AddCompactBilingualSection(body,
                                "In order for us to achieve this objective, we also need to know your comments on our services.",
                                "この目標を達成するため、当館のサービスに関するお客様のご意見をぜひお聞かせください。");

                            AddCompactBilingualSection(body,
                                "We are very grateful if you can spend your time to read, fill in the questionnaire and submit to us as following QR code:",
                                "お時間のある方は、アンケートにご記入のうえ、下記のQRコードより送信していただけますと幸いです。");

                            // 5. Bảng QR Code
                            Table qrTable = new Table();
                            qrTable.AppendChild(new TableProperties(new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" }));
                            TableRow rowQr = new TableRow();
                            TableCell cellEng = new TableCell();
                            if (qrEng[i] != null) InsertImage(mainPart, cellEng, qrEng[i]);
                            cellEng.AppendChild(new Paragraph(new Run(new Text("English Version")) { RunProperties = new RunProperties(new Bold(), new FontSize { Val = "20" }) }) { ParagraphProperties = new ParagraphProperties(new Justification { Val = JustificationValues.Center }) });
                            TableCell cellJap = new TableCell();
                            if (qrJap[i] != null) InsertImage(mainPart, cellJap, qrJap[i]);
                            cellJap.AppendChild(new Paragraph(new Run(new Text("Japanese Version")) { RunProperties = new RunProperties(new Bold(), new FontSize { Val = "20" }) }) { ParagraphProperties = new ParagraphProperties(new Justification { Val = JustificationValues.Center }) });
                            rowQr.Append(cellEng, cellJap);
                            qrTable.Append(rowQr);
                            body.Append(qrTable);

                            // 6. Lưu ý & Bảo mật tách dòng
                            AddCompactBilingualSection(body,
                                "You do not have to fill in this questionnaire in case you do not have any special thing to mention.",
                                "特にご指摘のない場合は、本アンケートへのご回答は不要でございます。");

                            AddCompactBilingualSection(body,
                                "You also do not have to reply all items if you do not know them clearly. Please just reply the items you know or write your comments if you think that you need to inform us.",
                                "また、ご不明な点やご回答が難しい項目につきましては、すべての項目にご回答いただく必要はございません。気になる点やお知らせいただきたい内容がございましたら、その項目のみご回答ください。");

                            Paragraph pPrivEng = CreateCompactParagraph(JustificationValues.Left);
                            pPrivEng.AppendChild(new Run(new Text("Please note: personal information (email/phone) is NOT required for this questionnaire.")) { RunProperties = new RunProperties(new Bold(), new FontSize { Val = "22" }) });
                            body.Append(pPrivEng);

                            Paragraph pPrivJap = CreateCompactParagraph(JustificationValues.Left);
                            pPrivJap.AppendChild(new Run(new Text("このアンケートでは、個人情報をご提供いただく必要はありません。")) { RunProperties = new RunProperties(new Bold(), new FontSize { Val = "20" }) });
                            pPrivJap.ParagraphProperties.Append(new SpacingBetweenLines() { After = "120" });
                            body.Append(pPrivJap);

                            // 7. Lời kết và Chữ ký
                            body.AppendChild(new Paragraph(new Run(new Text("Thank you very much for your kind attention and co-operation,")) { RunProperties = new RunProperties(new FontSize { Val = "24" }) }) { ParagraphProperties = new ParagraphProperties(new SpacingBetweenLines() { After = "0", Line = "240" }) });
                            body.AppendChild(new Paragraph(new Run(new Text("ご協力いただき、誠にありがとうございます。")) { RunProperties = new RunProperties(new FontSize { Val = "22" }) }) { ParagraphProperties = new ParagraphProperties(new SpacingBetweenLines() { After = "80", Line = "240" }) });

                            body.AppendChild(new Paragraph(new Run(new Text("Sincerely yours,")) { RunProperties = new RunProperties(new FontSize { Val = "24" }) }) { ParagraphProperties = new ParagraphProperties(new SpacingBetweenLines() { After = "0" }) });

                            body.AppendChild(new Paragraph());
                            body.AppendChild(new Paragraph());

                            Paragraph pBoss = CreateCompactParagraph(null);
                            pBoss.AppendChild(new Run(new Text("TATSUYA FUKUZAWA")) { RunProperties = new RunProperties(new Bold(), new FontSize { Val = "26" }) });
                            body.Append(pBoss);
                            body.AppendChild(new Paragraph(new Run(new Text("General Director")) { RunProperties = new RunProperties(new FontSize { Val = "22" }) }));

                            if (i < tenants.Count - 1)
                                body.AppendChild(new Paragraph(new Run(new Break() { Type = BreakValues.Page })));
                        }
                        mainPart.Document.Save();
                    }
                    return mem.ToArray();
                }
            }
            catch (Exception ex) { throw new Exception($"[Lỗi OpenXML] {ex.Message}"); }
        }

        private Paragraph CreateCompactParagraph(JustificationValues? align)
        {
            Paragraph p = new Paragraph();
            ParagraphProperties pp = new ParagraphProperties(new SpacingBetweenLines() { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto });
            if (align != null) pp.Append(new Justification { Val = align.Value });
            p.ParagraphProperties = pp;
            return p;
        }

        private void AddCompactBilingualSection(Body body, string eng, string jap)
        {
            Paragraph pEng = body.AppendChild(new Paragraph(new Run(new Text(eng)) { RunProperties = new RunProperties(new FontSize { Val = "24" }) }));
            pEng.ParagraphProperties = new ParagraphProperties(new SpacingBetweenLines() { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto });

            Paragraph pJap = body.AppendChild(new Paragraph(new Run(new Text(jap)) { RunProperties = new RunProperties(new FontSize { Val = "22" }) }));
            pJap.ParagraphProperties = new ParagraphProperties(new SpacingBetweenLines() { After = "80", Line = "240", LineRule = LineSpacingRuleValues.Auto });
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
                mail.CC.Add("hai.dq@saigonskygarden.com.vn");

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