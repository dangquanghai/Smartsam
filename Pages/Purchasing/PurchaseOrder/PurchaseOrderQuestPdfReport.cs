using System.Globalization;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SmartSam.Pages.Purchasing.PurchaseOrder;

internal static class PurchaseOrderQuestPdfReport
{
    public static byte[] BuildDetailPdf(PurchaseOrderDetailReportModel model)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(18);
                page.DefaultTextStyle(style => style.FontFamily("Times New Roman").FontSize(8));

                page.Content().Column(column =>
                {
                    column.Item().Element(content => ComposeHeader(content, model));
                    column.Item().PaddingTop(18).Element(content => ComposeMeta(content, model));
                    column.Item().PaddingTop(14).Element(content => ComposeSupplierInfo(content, model));
                    column.Item().PaddingTop(22).Element(ComposeSubject);
                    column.Item().PaddingTop(18).Text("Chúng tôi xin đặt các mặt hàng sau với các điều kiện dưới đây: We hereby order the following item(s) by under mentioned conditions:").FontSize(7);
                    column.Item().PaddingTop(2).Text("1. Mặt hàng/Items").Bold().FontSize(7);
                    column.Item().PaddingTop(2).Element(content => ComposeItems(content, model));
                    column.Item().PaddingTop(4).Element(content => ComposeTotals(content, model));
                    column.Item().PaddingTop(10).Element(content => ComposeTermsAndNotes(content, model));
                });

                page.Footer().Element(content => ComposeFooter(content, model));
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, PurchaseOrderDetailReportModel model)
    {
        container.Column(column =>
        {
            column.Item().AlignCenter().Text(text =>
            {
                text.Span("Saigon Sky Garden Co.").Bold().FontSize(16);
                text.Span("Ltd").Bold().FontSize(16);
            });
            column.Item().AlignCenter().Text("20 Le Thanh Ton, Phuong Ben Nghe, Quan 1, Tp. Ho Chi Minh").FontSize(8);
            column.Item().AlignCenter().Text("Tel: 84.8.38220002 Email: sales@saigonskygarden.com.vn VAT Code: 0300713227").FontSize(8);
            column.Item().AlignCenter().Text("-").FontSize(9);
            column.Item().PaddingTop(18).AlignCenter().Text(text =>
            {
                text.Span("ĐƠN ĐẶT HÀNG ").Bold().FontSize(20);
                text.Span("(Purchase Order)").Bold().FontSize(14);
            });
        });
    }

    private static void ComposeMeta(IContainer container, PurchaseOrderDetailReportModel model)
    {
        container.Row(row =>
        {
            row.Spacing(10);
            row.RelativeItem(1).Row(item => ComposeInlineField(item, "PO No:", model.PONo, 40));
            row.RelativeItem(1).Row(item => ComposeInlineField(item, "PO Date:", FormatDate(model.PODate, "dd-MMM-yy"), 50));
            row.RelativeItem(1).Row(item => ComposeInlineField(item, "PR No:", model.RequestNo, 40));
            row.RelativeItem(1.2f).Row(item => ComposeInlineField(item, "Currency/Đơn vị tiền tệ:", NormalizeLegacyText(model.CurrencyText), 94));
        });
    }

    private static void ComposeSupplierInfo(IContainer container, PurchaseOrderDetailReportModel model)
    {
        container.Column(column =>
        {
            column.Spacing(2);
            column.Item().Row(row => ComposeInlineField(row, "Nhà cung cấp/Supplier:", NormalizeLegacyText(model.SupplierDisplay), 88));
            column.Item().Row(row => ComposeInlineField(row, "Địa chỉ/Address:", NormalizeLegacyText(model.SupplierAddress), 68));
            column.Item().Row(row =>
            {
                row.Spacing(14);
                row.RelativeItem(1).Row(item => ComposeInlineField(item, "Người liên hệ/Contact:", NormalizeLegacyText(model.SupplierContact), 95));
                row.RelativeItem(1.2f).Row(item => ComposeInlineField(item, "Phone/Mail:", BuildPhoneMailText(model), 58));
            });
        });
    }

    private static void ComposeSubject(IContainer container)
    {
        container.Row(row => ComposeInlineField(row, "Subject:", string.Empty, 44));
    }

    private static void ComposeInlineField(RowDescriptor row, string label, string value, float labelWidth)
    {
        row.ConstantItem(labelWidth).AlignBottom().Text(label).Bold();
        row.RelativeItem().BorderBottom(0.6f).PaddingBottom(1).Text(value);
    }

    private static void ComposeItems(IContainer container, PurchaseOrderDetailReportModel model)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(24);
                columns.ConstantColumn(58);
                columns.RelativeColumn(1);
                columns.ConstantColumn(34);
                columns.ConstantColumn(48);
                columns.ConstantColumn(64);
                columns.ConstantColumn(70);
                columns.ConstantColumn(66);
            });

            static IContainer HeaderCell(IContainer cell) => cell.Border(0.8f).Background(Colors.Grey.Lighten3).Padding(2).AlignCenter().AlignMiddle();
            static IContainer BodyCell(IContainer cell) => cell.Border(0.8f).Padding(2).AlignMiddle();

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("STT\n(No)").Bold().FontSize(6);
                header.Cell().Element(HeaderCell).Text("Mã hàng\n(Item code)").Bold().FontSize(6);
                header.Cell().Element(HeaderCell).Text("Mô tả mặt hàng\n(Description)").Bold().FontSize(6);
                header.Cell().Element(HeaderCell).Text("ĐVT\n(Unit)").Bold().FontSize(6);
                header.Cell().Element(HeaderCell).Text("S.Lượng\n(Quantity)").Bold().FontSize(6);
                header.Cell().Element(HeaderCell).Text("Đơn giá\n(Unit Price)").Bold().FontSize(6);
                header.Cell().Element(HeaderCell).Text("Thành tiền\n(Amount)").Bold().FontSize(6);
                header.Cell().Element(HeaderCell).Text("Ghi chú\n(Remarks)").Bold().FontSize(6);
            });

            if (model.Items.Count == 0)
            {
                table.Cell().ColumnSpan(8).Border(0.8f).Padding(8).AlignCenter().Text("No data");
                return;
            }

            foreach (var item in model.Items)
            {
                table.Cell().Element(BodyCell).AlignCenter().Text(item.No.ToString(CultureInfo.InvariantCulture));
                table.Cell().Element(BodyCell).Text(item.ItemCode);
                table.Cell().Element(BodyCell).Text(NormalizeTcvn3Text(item.ItemName));
                table.Cell().Element(BodyCell).AlignCenter().Text(item.Unit);
                table.Cell().Element(BodyCell).AlignCenter().Text(FormatQuantity(item.Quantity));
                table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(item.UnitPrice));
                table.Cell().Element(BodyCell).AlignRight().Text(FormatMoney(item.Amount));
                table.Cell().Element(BodyCell).Text(item.Remark);
            }
        });
    }

    private static void ComposeTotals(IContainer container, PurchaseOrderDetailReportModel model)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(14);
                columns.ConstantColumn(28);
                columns.RelativeColumn(1);
                columns.ConstantColumn(34);
                columns.ConstantColumn(48);
                columns.ConstantColumn(64);
                columns.ConstantColumn(70);
                columns.ConstantColumn(64);
            });

            static IContainer LeftCell(IContainer cell) => cell.PaddingVertical(1).PaddingHorizontal(4);
            static IContainer RightCell(IContainer cell) => cell.PaddingVertical(1).PaddingHorizontal(4).AlignRight();

            table.Cell().ColumnSpan(4).Element(LeftCell);
            table.Cell().ColumnSpan(2).Element(RightCell).Text("Tổng trước thuế (Before VAT):").Bold().FontSize(7);
            table.Cell().Element(RightCell).Text(FormatMoney(model.BeforeVAT)).Bold();
            table.Cell().Element(LeftCell);

            table.Cell().ColumnSpan(2).Element(LeftCell);
            table.Cell().ColumnSpan(2).Element(RightCell).Text($"% VAT: {FormatPercent(model.PerVAT)}").Bold().FontSize(7);
            table.Cell().ColumnSpan(2).Element(RightCell).Text("Tiền thuế (VAT):").Bold().FontSize(7);
            table.Cell().Element(RightCell).Text(FormatMoney(model.VAT)).Bold();
            table.Cell().Element(LeftCell);

            table.Cell().ColumnSpan(4).Element(LeftCell);
            table.Cell().ColumnSpan(2).Element(RightCell).Text("Tổng cộng (Total):").Bold().FontSize(7);
            table.Cell().Element(RightCell).Text(FormatMoney(model.AfterVAT)).Bold();
            table.Cell().Element(LeftCell);
        });
    }

    private static void ComposeTermsAndNotes(IContainer container, PurchaseOrderDetailReportModel model)
    {
        container.Column(column =>
        {
            column.Item().BorderTop(0.8f).PaddingTop(6).Text("TERMS & CONDITIONS:").Bold().FontSize(8);
            column.Item().MinHeight(58).PaddingTop(4).Text(text =>
            {
                var terms = SplitNotes(NormalizeLegacyText(model.TermsAndConditions)).ToList();
                foreach (var line in terms)
                {
                    text.Line(line);
                }
            });

            column.Item().BorderTop(0.8f).PaddingTop(4).Text("NOTES:").Bold().FontSize(8);
            column.Item().MinHeight(36).PaddingTop(4).Text(text =>
            {
                var notes = SplitNotes(NormalizeLegacyText(model.Notes)).ToList();
                foreach (var line in notes)
                {
                    text.Line(line);
                }
            });
        });
    }

    private static void ComposeFooter(IContainer container, PurchaseOrderDetailReportModel model)
    {
        const string DefaultDirectorName = "TATSUYA FUKUZAWA";
        const string DefaultDirectorTitle = "Tổng Giám Đốc";
        var footer = model.Footer;
        var directorName = string.IsNullOrWhiteSpace(footer?.ApprovedName) ? DefaultDirectorName : NormalizeLegacyText(footer.ApprovedName);
        var directorTitle = string.IsNullOrWhiteSpace(footer?.ApprovedTitle) ? DefaultDirectorTitle : NormalizeLegacyText(footer.ApprovedTitle);

        container.Height(92).AlignBottom().Row(row =>
        {
            row.ConstantItem(350).PaddingLeft(20).Column(left =>
            {
                left.Item().Width(320).Column(signatureBlock =>
                {
                    signatureBlock.Item().Row(top =>
                    {
                        top.ConstantItem(82).Text(string.Empty);
                        top.ConstantItem(156).TranslateY(-14).AlignCenter().AlignTop().Text("Công ty TNHH Vườn Thiên Đàng Sài Gòn").FontSize(8);
                        top.ConstantItem(82).TranslateX(-24).PaddingTop(1).PaddingLeft(1).Row(signatures =>
                        {
                            signatures.ConstantItem(34).Element(cell => ComposeSmallSignature(cell, footer?.PreparedSignature));
                            signatures.ConstantItem(34).Element(cell => ComposeSmallSignature(cell, footer?.CheckedSignature));
                        });
                    });

                    signatureBlock.Item().PaddingTop(2).TranslateX(74).Width(170).Height(44).AlignCenter().AlignMiddle().Element(cell =>
                    {
                        if (footer?.ApprovedSignature is { Length: > 0 })
                        {
                            cell.Image(footer.ApprovedSignature).FitArea();
                        }
                    });

                    signatureBlock.Item().PaddingTop(1).AlignCenter().Width(170).BorderTop(0.8f).PaddingTop(3).Column(name =>
                    {
                        name.Item().AlignCenter().Text(directorName).FontSize(8);
                        name.Item().AlignCenter().Text(directorTitle).FontSize(8);
                    });
                });
            });

            row.RelativeItem().PaddingRight(8).PaddingBottom(1).AlignBottom().Column(right =>
            {
                right.Item().AlignRight().Text(text =>
                {
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
                right.Item().PaddingTop(2).AlignRight().Text("QF6.3.Rev: 1");
            });
        });
    }

    private static void ComposeSmallSignature(IContainer container, byte[]? signature)
    {
        container.Height(20).PaddingHorizontal(2).AlignCenter().AlignMiddle().Element(cell =>
        {
            if (signature is { Length: > 0 })
            {
                cell.Image(signature).FitArea();
            }
        });
    }

    private static IEnumerable<string> SplitNotes(string notes)
    {
        return notes
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line));
    }

    private static string BuildPhoneMailText(PurchaseOrderDetailReportModel model)
    {
        var phone = NormalizeLegacyText(model.SupplierTel);
        var email = NormalizeLegacyText(model.SupplierEmail);
        if (string.IsNullOrWhiteSpace(phone) && string.IsNullOrWhiteSpace(email))
        {
            return string.Empty;
        }

        return $"{phone}/{email}";
    }

    private static string NormalizeLegacyText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = NormalizeTcvn3Text(value);
        text = NormalizeVniText(text);
        return text;
    }

    internal static string NormalizeVniText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var result = value.Trim();

        // Quy ước hiển thị của các trường legacy dùng VNI.
        // Bảng thay thế này giúp in PDF bằng Unicode font chuẩn mà không phụ thuộc font VNI cài trên runtime.
        var source1 = new[]
        {
            "AÁ", "aá",
            "AÀ", "aà", "AÅ", "aå", "AÃ", "aã", "AÄ", "aä", "AÉ", "aé",
            "AÈ", "aè", "AÚ", "aú", "AÜ", "aü", "AË", "aë", "EÁ", "eá", "EÀ", "eà", "EÅ", "eå",
            "EÃ", "eã", "EÄ", "eä", "OÁ", "oá", "OÀ", "oà", "OÅ", "oå", "OÃ", "oã",
            "OÄ", "oä", "ÔÙ", "ôù", "ÔØ", "ôø", "ÔÛ", "ôû", "ÔÕ", "ôõ",
            "ÔÏ", "ôï", "OÁ", "oá", "OÀ", "oà", "OÅ", "oå", "OÃ", "oã",
            "OÄ", "oä", "ÔÙ", "ôù", "ÔØ", "ôø", "ÔÛ", "ôû", "ÔÕ", "ôõ",
            "ÔÏ", "ôï", "ÖÙ", "öù", "ÖØ", "öø",
            "ÖÛ", "öû", "ÖÕ", "öõ", "ÖÏ", "öï"
        };

        var target1 = new[]
        {
            "Ấ", "ấ",
            "Ầ", "ầ", "Ẩ", "ẩ", "Ẫ", "ẫ", "Ậ", "ậ", "Ắ", "ắ",
            "Ằ", "ằ", "Ẳ", "ẳ", "Ẵ", "ẵ", "Ặ", "ặ", "Ế", "ế", "Ề", "ề", "Ể", "ể",
            "Ễ", "ễ", "Ệ", "ệ", "Ố", "ố", "Ồ", "ồ", "Ổ", "ổ", "Ỗ", "ỗ",
            "Ộ", "ộ", "Ớ", "ớ", "Ờ", "ờ", "Ở", "ở", "Ỡ", "ỡ",
            "Ợ", "ợ", "Ố", "ố", "Ồ", "ồ", "Ổ", "ổ", "Ỗ", "ỗ",
            "Ộ", "ộ", "Ớ", "ớ", "Ờ", "ờ", "Ở", "ở", "Ỡ", "ỡ",
            "Ợ", "ợ", "Ứ", "ứ", "Ừ", "ừ",
            "Ử", "ử", "Ữ", "ữ", "Ự", "ự"
        };

        var source2 = new[]
        {
            "Ô", "ô", "ó", "Ò", "ò",
            "AØ", "AÙ", "AÂ", "AÕ", "EØ", "EÙ", "EÂ", "Ì", "Í", "OØ",
            "OÙ", "OÂ", "OÕ", "UØ", "UÙ", "YÙ", "aø", "aù", "aâ", "aõ",
            "eø", "eù", "eâ", "ì", "í", "oø", "où", "oâ", "oõ", "uø",
            "uù", "yù", "AÊ", "aê", "Ñ", "ñ", "Ó", "UÕ", "uõ",
            "Ö", "ö", "AÏ", "aï", "AÛ", "aû", "EÏ", "eï",
            "EÛ", "eû", "EÕ", "eõ", "Æ", "æ", "OÏ", "oï",
            "OÛ", "oû", "UÏ", "uï", "UÛ", "uû", "YØ", "yø", "Î", "î",
            "YÛ", "yû", "YÕ", "yõ"
        };

        var target2 = new[]
        {
            "Ơ", "ơ", "ĩ", "Ị", "ị",
            "À", "Á", "Â", "Ã", "È", "É", "Ê", "Ì", "Í", "Ò",
            "Ó", "Ô", "Õ", "Ù", "Ú", "Ý", "à", "á", "â", "ã",
            "è", "é", "ê", "ì", "í", "ò", "ó", "ô", "õ", "ù",
            "ú", "ý", "Ă", "ă", "Đ", "đ", "Ĩ", "Ũ", "ũ",
            "Ư", "ư", "Ạ", "ạ", "Ả", "ả", "Ẹ", "ẹ",
            "Ẻ", "ẻ", "Ẽ", "ẽ", "Ỉ", "ỉ", "Ọ", "ọ",
            "Ỏ", "ỏ", "Ụ", "ụ", "Ủ", "ủ", "Ỳ", "ỳ", "Ỵ", "ỵ",
            "Ỷ", "ỷ", "Ỹ", "ỹ"
        };

        for (var i = 0; i < source1.Length; i++)
        {
            result = result.Replace(source1[i], target1[i], StringComparison.Ordinal);
        }

        for (var i = 0; i < source2.Length; i++)
        {
            result = result.Replace(source2[i], target2[i], StringComparison.Ordinal);
        }

        return result;
    }

    internal static string NormalizeTcvn3Text(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var result = value.Trim();

        var source = "µ¸¶·¹¨»¾¼½Æ©ÇÊÈÉË®ÌÐÎÏÑªÒÕÓÔÖ×ÝØÜÞßãáâä«åèæçé¬êíëìîïóñòô-õøö÷ùúýûüþ¡¢§£¤¥¦";
        var target = "àáảãạăằắẳẵặâầấẩẫậđèéẻẽẹêềếểễệìíỉĩịòóỏõọôồốổỗộơờớởỡợùúủũụưừứửữựỳýỷỹỵĂÂĐÊÔƠƯ";

        for (var i = 0; i < source.Length; i++)
        {
            result = result.Replace(source[i].ToString(), target[i].ToString(), StringComparison.Ordinal);
        }

        return result;
    }

    private static string FormatDate(DateTime? date, string format)
    {
        return date.HasValue ? date.Value.ToString(format, CultureInfo.InvariantCulture) : string.Empty;
    }

    private static string FormatQuantity(decimal value)
    {
        return value.ToString("#,##0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatMoney(decimal value)
    {
        return value.ToString("#,##0.00", CultureInfo.InvariantCulture);
    }

    private static string FormatPercent(decimal value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}

public sealed class PurchaseOrderDetailReportModel
{
    public string PONo { get; set; } = string.Empty;
    public DateTime? PODate { get; set; }
    public string RequestNo { get; set; } = string.Empty;
    public string SupplierDisplay { get; set; } = string.Empty;
    public string SupplierAddress { get; set; } = string.Empty;
    public string SupplierTel { get; set; } = string.Empty;
    public string SupplierEmail { get; set; } = string.Empty;
    public string SupplierContact { get; set; } = string.Empty;
    public string CurrencyText { get; set; } = string.Empty;
    public string TermsAndConditions { get; set; } = string.Empty;
    public decimal BeforeVAT { get; set; }
    public decimal PerVAT { get; set; }
    public decimal VAT { get; set; }
    public decimal AfterVAT { get; set; }
    public string Notes { get; set; } = string.Empty;
    public List<PurchaseOrderDetailReportItem> Items { get; set; } = new();
    public PurchaseOrderApprovalFooterModel? Footer { get; set; }
}

public sealed class PurchaseOrderDetailReportItem
{
    public int No { get; set; }
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public string Remark { get; set; } = string.Empty;
}

public sealed class PurchaseOrderApprovalFooterModel
{
    public string PreparedDate { get; set; } = string.Empty;
    public string CheckedDate { get; set; } = string.Empty;
    public string ApprovedDate { get; set; } = string.Empty;
    public string PreparedName { get; set; } = string.Empty;
    public string CheckedName { get; set; } = string.Empty;
    public string ApprovedName { get; set; } = string.Empty;
    public string PreparedTitle { get; set; } = "Purchaser";
    public string CheckedTitle { get; set; } = "Chief Accountant";
    public string ApprovedTitle { get; set; } = "General Director";
    public byte[]? PreparedSignature { get; set; }
    public byte[]? CheckedSignature { get; set; }
    public byte[]? ApprovedSignature { get; set; }
    public string DeliveryDate { get; set; } = string.Empty;
    public string DeliveryPlace { get; set; } = string.Empty;
    public string Receiver { get; set; } = string.Empty;
    public string PaymentTerm { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}
