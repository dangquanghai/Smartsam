
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
using SmartSam.Helpers;
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


                // 6. Tạo nội dung mail dựa trên mẫu VoucherNotify
                string message = @"
                <p style='font-weight: bold; font-size: 16px; color: #333;'>Dear CSD Team,</p>
    
                <p>The system has detected some long-term contracts which have been living for 6 months at <b>Saigon Sky Garden</b>.</p>
    
                <div style='background-color: #f9f9f9; border-left: 4px solid #0056b3; padding: 15px; margin: 15px 0; font-style: italic; color: #555;'>
                    It has combined the Questionnaire link you provided to create a QR code, 
                    merged it with the customer email content, and attached this Word file to this email.
                </div>
    
                <p>Please <b>print it out</b>, check it carefully, and send it to your long-term customers to request their feedback.</p>";

                //"#3498db": Màu xanh làm nền cho thông báo email
                string htmlBody = EmailTemplateHelper.WrapInNotifyTemplate("06 MONTHS STAY REVIEW"," #3498db",DateTime.Now,message);

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
                        // Lề cực chuẩn: Top/Bottom 450 (khoảng 0.8cm) để dành chỗ cho chữ ký phía dưới
                        PageMargin pageMargin = new PageMargin() { Top = 450, Bottom = 450, Left = 1080, Right = 1080 };
                        sectionProps.Append(pageMargin);
                        body.Append(sectionProps);

                        for (int i = 0; i < tenants.Count; i++)
                        {
                            var t = tenants[i];

                            // 1. Chừa 2 dòng đầu cho Letterhead
                            body.AppendChild(new Paragraph(new Run(new Text(""))));
                            body.AppendChild(new Paragraph(new Run(new Text(""))));

                            // 2. Ngày tháng (Times New Roman 12pt)
                            body.AppendChild(new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Right }),
                                new Run(new RunProperties(new RunFonts { Ascii = "Times New Roman" }, new FontSize { Val = "24" }),
                                new Text(DateTime.Now.ToString("MMMM dd, yyyy")))));

                            // 3. Tiêu đề
                            body.AppendChild(new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
                                new Run(new RunProperties(new RunFonts { Ascii = "Times New Roman" }, new Bold(), new FontSize { Val = "28" }),
                                new Text("QUESTIONNAIRE"))));

                            // 4. Bảng Tên khách & Số phòng
                            Table infoTable = new Table(new TableProperties(new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
                                new TableBorders(new TopBorder { Val = BorderValues.None }, new BottomBorder { Val = BorderValues.None },
                                                new LeftBorder { Val = BorderValues.None }, new RightBorder { Val = BorderValues.None },
                                                new InsideHorizontalBorder { Val = BorderValues.None }, new InsideVerticalBorder { Val = BorderValues.None })));
                            TableRow infoRow = new TableRow();
                            infoRow.Append(
                                new TableCell(new Paragraph(new Run(new RunProperties(new RunFonts { Ascii = "Times New Roman" }, new Bold(), new FontSize { Val = "24" }), new Text($"Dear {t.Title} {t.CustomerName},") { Space = SpaceProcessingModeValues.Preserve }))),
                                new TableCell(new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Right }),
                                              new Run(new RunProperties(new RunFonts { Ascii = "Times New Roman" }, new Bold(), new FontSize { Val = "24" }), new Text($"Apartment: {t.CurrentApartmentNo}") { Space = SpaceProcessingModeValues.Preserve })))
                            );
                            infoTable.Append(infoRow);
                            body.Append(infoTable);

                            // --- 5. Nội dung Bilingual ĐẦY ĐỦ (KHÔNG CẮT CHỮ) ---
                            string[,] contentGroups = {
                            { "We would like to express our hearty thanks for your choosing Saigon Sky Garden as your home during your stay in Ho Chi Minh City.", "ホーチミン市ご滞在中にサイゴン スカイ ガーデンをお選びいただき、心より感謝申し上げます。" },
                            { "We, all staffs of Saigon Sky Garden, always do our best to make your stay more comfortable and happy day by day.", "サイゴン スカイ ガーデンのスタッフ一同、お客様に日々より快適なご滞在をお届けできるよう、常に最善を尽くしております。" },
                            { "In order for us to achieve this objective, we also need to know your comments on our services.", "この目標 te達成するため、当館のサービスに関するお客様のご意見をぜひお聞かせください。" },
                            { "We are very grateful if you can spend your time to read, fill in the questionnaire and submit to us as following QR code:", "お時間のある方は、アンケートにご記入のうえ、下記のQRコードより送信していただけますと幸いです。" }
                            };

                            for (int j = 0; j < 4; j++)
                            {
                                // Dòng Anh: Giữ nguyên cỡ 24 (12pt), sát dòng (After = 0)
                                body.AppendChild(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "0", Line = "220" }),
                                    new Run(new RunProperties(new RunFonts { Ascii = "Times New Roman" }, new FontSize { Val = "24" }), new Text(contentGroups[j, 0]))));
                                // Dòng Nhật: Để cỡ 20 (10pt) để tiết kiệm diện tích, After cực nhỏ (30)
                                body.AppendChild(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "30", Line = "200" }),
                                    new Run(new RunProperties(new RunFonts { Ascii = "Times New Roman" }, new FontSize { Val = "20" }), new Text(contentGroups[j, 1]))));
                            }
                            // --- 6. Bảng QR Code 4 Cột (Căn giữa tuyệt đối: Ngang & Dọc) ---
                            Table qrTable = new Table();
                            TableProperties qrTblProp = new TableProperties(
                                new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" },
                                new TableBorders(new TopBorder { Val = BorderValues.None }, new BottomBorder { Val = BorderValues.None },
                                                new LeftBorder { Val = BorderValues.None }, new RightBorder { Val = BorderValues.None },
                                                new InsideHorizontalBorder { Val = BorderValues.None }, new InsideVerticalBorder { Val = BorderValues.None })
                            );
                            qrTable.AppendChild(qrTblProp);

                            TableRow rowQr = new TableRow();


                            // Cột 1: Nhãn English Version (Căn giữa dọc và ngang)
                            TableCell c1 = new TableCell();
                            c1.AppendChild(new TableCellProperties(
                                new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "1200" }, // Chiếm 24% độ rộng
                                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center } // CĂN GIỮA DỌC
                            ));
                            c1.AppendChild(new Paragraph(
                                new ParagraphProperties(new Justification { Val = JustificationValues.Center }, new SpacingBetweenLines { After = "0" }),
                                new Run(new RunProperties(new RunFonts { Ascii = "Times New Roman" }, new Bold(), new FontSize { Val = "18" }),
                                new Text("English Version"))
                            ));

                            // Cột 2: QR Code English
                            TableCell c2 = new TableCell();
                            c2.AppendChild(new TableCellProperties(
                                new TableCellWidth { Type = TableWidthUnitValues.Pct, Width = "1300" }, // Chiếm 26% độ rộng
                                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }
                            ));
                            if (qrEng != null && i < qrEng.Count && qrEng[i] != null)
                                InsertImage(mainPart, c2, qrEng[i]);

                            // Cột 3: Nhãn Japanese Version
                            TableCell c3 = new TableCell();
                            c3.AppendChild(new TableCellProperties(
                                new TableWidth { Type = TableWidthUnitValues.Pct, Width = "1200" },
                                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }
                            ));
                            c3.AppendChild(new Paragraph(
                                new ParagraphProperties(new Justification { Val = JustificationValues.Center }, new SpacingBetweenLines { After = "0" }),
                                new Run(new RunProperties(new RunFonts { Ascii = "Times New Roman" }, new Bold(), new FontSize { Val = "18" }),
                                new Text("Japanese Version"))
                            ));

                            // Cột 4: QR Code Japanese
                            TableCell c4 = new TableCell();
                            c4.AppendChild(new TableCellProperties(
                                new TableWidth { Type = TableWidthUnitValues.Pct, Width = "1300" },
                                new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Center }
                            ));
                            if (qrJap != null && i < qrJap.Count && qrJap[i] != null)
                                InsertImage(mainPart, c4, qrJap[i]);

                            rowQr.Append(c1, c2, c3, c4);
                            qrTable.Append(rowQr);
                            body.Append(qrTable);


                            // --- 7. Các dòng lưu ý SAU QR (KHÔNG CẮT CHỮ) ---
                            // Đoạn 1
                            body.AppendChild(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "0" }),
                                new Run(new RunProperties(new RunFonts { Ascii = "Times New Roman" }, new FontSize { Val = "22" }), new Text("You do not have to fill in this questionnaire in case you do not have any special thing to mention."))));
                            body.AppendChild(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "30" }),
                                new Run(new RunProperties(new RunFonts { Ascii = "Times New Roman" }, new FontSize { Val = "18" }), new Text("特にご指摘のない場合は、本アンケートへのご回答は不要でございます。"))));

                            // Đoạn 2
                            body.AppendChild(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "0" }),
                                new Run(new RunProperties(new RunFonts { Ascii = "Times New Roman" }, new FontSize { Val = "22" }), new Text("You also do not have to reply all items if you do not know them clearly. Please just reply the items you know or write your comments if you think that you need to inform us."))));
                            body.AppendChild(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "30" }),
                                new Run(new RunProperties(new RunFonts { Ascii = "Times New Roman" }, new FontSize { Val = "18" }), new Text("また、ご不明な点やご回答が難しい項目につきましては、すべての項目にご回答いただく必要はございません。気になる点やお知らせいただきたい内容がございましたら、その項目のみご回答ください。"))));

                            // Bảo mật (Bold)
                            body.AppendChild(new Paragraph(new Run(new RunProperties(new RunFonts { Ascii = "Times New Roman" }, new Bold(), new FontSize { Val = "22" }),
                                new Text("Please note: personal information (email/phone) is NOT required for this questionnaire."))));
                            body.AppendChild(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "40" }),
                                new Run(new RunProperties(new RunFonts { Ascii = "Times New Roman" }, new Bold(), new FontSize { Val = "20" }), new Text("このアンケートでは、個人情報をご提供いただく必要はありません。"))));

                            // Cảm ơn
                            body.AppendChild(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "0" }),
                                new Run(new RunProperties(new RunFonts { Ascii = "Times New Roman" }, new FontSize { Val = "24" }), new Text("Thank you very much for your kind attention and co-operation,"))));
                            body.AppendChild(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "60" }),
                                new Run(new RunProperties(new RunFonts { Ascii = "Times New Roman" }, new FontSize { Val = "22" }), new Text("ご協力いただき、誠にありがとうございます。"))));

                            // 8. Chữ ký (Sincerely và Tên boss)
                            body.AppendChild(new Paragraph(new Run(new RunProperties(new RunFonts { Ascii = "Times New Roman" }, new FontSize { Val = "24" }), new Text("Sincerely yours,"))));
                            body.AppendChild(new Paragraph(new Run(new Text("")))); // Chỉ để 1 dòng trống thay vì 2
                            body.AppendChild(new Paragraph(new Run(new RunProperties(new RunFonts { Ascii = "Times New Roman" }, new Bold(), new FontSize { Val = "26" }), new Text("TATSUYA FUKUZAWA"))));
                            body.AppendChild(new Paragraph(new Run(new RunProperties(new RunFonts { Ascii = "Times New Roman" }, new FontSize { Val = "22" }), new Text("General Director"))));

                            if (i < tenants.Count - 1) body.AppendChild(new Paragraph(new Run(new Break() { Type = BreakValues.Page })));
                        }
                        mainPart.Document.Save();
                    }
                    return mem.ToArray();
                }
            }
            catch (Exception ex) { throw new Exception($"[Lỗi] {ex.Message}"); }
        }
        
        // Hàm bổ trợ đã cập nhật Font
        private void AddCompactBilingualSection(Body body, RunFonts fonts, string eng, string jap)
        {
            Paragraph pEng = body.AppendChild(new Paragraph());
            Run rEng = pEng.AppendChild(new Run(fonts.CloneNode(true), new Text(eng)));
            rEng.RunProperties = new RunProperties(new FontSize { Val = "24" });
            pEng.ParagraphProperties = new ParagraphProperties(new SpacingBetweenLines() { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto });

            Paragraph pJap = body.AppendChild(new Paragraph());
            Run rJap = pJap.AppendChild(new Run(fonts.CloneNode(true), new Text(jap)));
            rJap.RunProperties = new RunProperties(new FontSize { Val = "22" });
            pJap.ParagraphProperties = new ParagraphProperties(new SpacingBetweenLines() { After = "80", Line = "240", LineRule = LineSpacingRuleValues.Auto });
        }
       

        private Paragraph CreateCompactParagraph(JustificationValues? align)
        {
            Paragraph p = new Paragraph();
            ParagraphProperties pp = new ParagraphProperties(new SpacingBetweenLines() { After = "0", Line = "240", LineRule = LineSpacingRuleValues.Auto });
            if (align != null) pp.Append(new Justification { Val = align.Value });
            p.ParagraphProperties = pp;
            return p;
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
                    From = new MailAddress(senderEmail, "New Smartsam System"),
                    Subject = $"[Survey 6 Months] Resident Survey Letters - {DateTime.Now:MM/yyyy}",
                    Body = htmlBody,
                    IsBodyHtml = true
                };

               // mail.To.Add("hai.dq@saigonskygarden.com.vn");
               mail.To.Add("bao.q@saigonskygarden.com.vn");
               mail.CC.Add("phung.ctm@saigonskygarden.com.vn");
               mail.CC.Add("lan.dtm@saigonskygarden.com.vn");
               mail.CC.Add("hai.dq@saigonskygarden.com.vn");
               mail.CC.Add("fukuzawa-t@saigonskygarden.com.vn");


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