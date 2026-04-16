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
                page.Size(PageSizes.A4.Landscape());
                page.Margin(15);
                page.DefaultTextStyle(style => style.FontFamily("Times New Roman").FontSize(10));

                page.Content().Column(column =>
                {
                    column.Item().Element(content => ComposeHeader(content, model));
                    column.Item().PaddingTop(4).Element(content => ComposeMeta(content, model));
                    column.Item().PaddingTop(4).Element(content => ComposeInfo(content, model));
                    column.Item().PaddingTop(8).AlignLeft().Text(text =>
                    {
                        text.Span("Chúng tôi xin đặt các mặt hàng sau với các điều kiện dưới đây:We hereby order the following item(s) by under mentioned conditions:");
                    });
                    column.Item().PaddingTop(4).Text("1. Mặt hàng/Items").Bold();
                    column.Item().PaddingTop(2).Element(content => ComposeItems(content, model));
                    column.Item().PaddingTop(4).Element(content => ComposeTotals(content, model));
                    column.Item().PaddingTop(8).Element(content => ComposeFooter(content, model));
                });
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, PurchaseOrderDetailReportModel model)
    {
        container.Column(column =>
        {
            column.Item().AlignCenter().Text("Saigon Sky Garden Co., LTD").Bold().FontSize(14);
            column.Item().AlignCenter().Text("20 Lê Thánh Tôn, Phường Bến Nghé, Quận 1, Tp. Hồ Chí Minh").FontSize(8);
            column.Item().AlignCenter().Text("Tel: 84.8.38220002 Email: sales@saigonskygarden.com.vn VAT Code: 0300713227").FontSize(8);
            column.Item().AlignCenter().Text("-").FontSize(10);
            column.Item().PaddingTop(4).AlignCenter().Text(text =>
            {
                text.Span("ĐƠN ĐẶT HÀNG ").Bold().FontSize(18);
                text.Span("(Purchase Order)").Bold().FontSize(14);
            });
        });
    }

    private static void ComposeMeta(IContainer container, PurchaseOrderDetailReportModel model)
    {
        container.Row(row =>
        {
            row.RelativeItem().Text(text =>
            {
                text.Span("PR No.: ").Bold();
                text.Span(model.RequestNo);
            });

            row.RelativeItem().AlignCenter().Text(text =>
            {
                text.Span("PO No.:   ").Bold();
                text.Span(model.PONo);
            });

            row.RelativeItem().AlignRight().Text(text =>
            {
                text.Span("PO Date: ").Bold();
                text.Span(FormatDate(model.PODate, "d-MMM-yy"));
            });
        });
    }

    private static void ComposeInfo(IContainer container, PurchaseOrderDetailReportModel model)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Column(left =>
                {
                    left.Spacing(4);
                    left.Item().Row(info => ComposeInfoRow(info, "Nhà cung cấp/Supplier", NormalizeVniText(model.SupplierDisplay)));
                    left.Item().Row(info => ComposeThreeValueRow(info, "Tel", NormalizeVniText(model.SupplierTel), "Fax", string.Empty, "Email", NormalizeVniText(model.SupplierEmail)));
                    left.Item().Row(info => ComposeTwoValueRow(info, "Người liên hệ/Contact", NormalizeVniText(model.SupplierContact), "Đơn vị tiền tệ/Currency", NormalizeVniText(model.CurrencyText)));
                });
            });

            column.Item().PaddingTop(4).Text("Term & Condition:").Bold();
            column.Item().PaddingLeft(10).Text(text =>
            {
                var terms = SplitNotes(NormalizeLegacyText(model.TermsAndConditions)).ToList();
                if (terms.Count == 0)
                {
                    text.Span(string.Empty);
                    return;
                }

                for (var i = 0; i < terms.Count; i++)
                {
                    if (i > 0)
                    {
                        text.Line(string.Empty);
                    }

                    text.Span(terms[i]);
                }
            });
        });
    }

    private static void ComposeInfoRow(RowDescriptor row, string label, string value)
    {
        row.ConstantItem(160).Text(label + ":").Bold();
        row.RelativeItem().Text($"{value}");
    }

    private static void ComposeTwoValueRow(RowDescriptor row, string leftLabel, string leftValue, string rightLabel, string rightValue)
    {
        row.RelativeItem(1).Row(left =>
        {
            left.ConstantItem(160).Text(leftLabel + ":").Bold();
            left.RelativeItem().Text($" {leftValue}");
        });

        row.RelativeItem(1).Row(right =>
        {
            right.ConstantItem(160).Text(rightLabel + ":").Bold();
            right.RelativeItem().Text($" {rightValue}");
        });
    }

    private static void ComposeThreeValueRow(RowDescriptor row, string leftLabel, string leftValue, string middleLabel, string middleValue, string rightLabel, string rightValue)
    {
        row.RelativeItem(1).Row(left =>
        {
            left.ConstantItem(50).Text(leftLabel + ":").Bold();
            left.RelativeItem().Text($" {leftValue}");
        });

        row.RelativeItem(1).Row(middle =>
        {
            middle.ConstantItem(45).Text(middleLabel + ":").Bold();
            middle.RelativeItem().Text($" {middleValue}");
        });

        row.RelativeItem(1).Row(right =>
        {
            right.ConstantItem(55).Text(rightLabel + ":").Bold();
            right.RelativeItem().Text($" {rightValue}");
        });
    }

    private static void ComposeItems(IContainer container, PurchaseOrderDetailReportModel model)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(34);
                columns.ConstantColumn(110);
                columns.RelativeColumn(2.8f);
                columns.ConstantColumn(54);
                columns.ConstantColumn(64);
                columns.ConstantColumn(92);
                columns.ConstantColumn(92);
                columns.ConstantColumn(120);
            });

            static IContainer HeaderCell(IContainer cell) => cell.Border(1).Background(Colors.Grey.Lighten3).Padding(4).AlignCenter().AlignMiddle();
            static IContainer BodyCell(IContainer cell) => cell.Border(1).Padding(3).AlignTop();

            table.Header(header =>
            {
                header.Cell().Element(HeaderCell).Text("STT\n(No)").Bold();
                header.Cell().Element(HeaderCell).Text("Mã hàng\n(Item code)").Bold();
                header.Cell().Element(HeaderCell).Text("Mô tả mặt hàng\n(Description)").Bold();
                header.Cell().Element(HeaderCell).Text("ĐVT\n(Unit)").Bold();
                header.Cell().Element(HeaderCell).Text("S.Lượng\n(Quantity)").Bold();
                header.Cell().Element(HeaderCell).Text("Đơn giá\n(Unit Price)").Bold();
                header.Cell().Element(HeaderCell).Text("Thành tiền\n(Amount)").Bold();
                header.Cell().Element(HeaderCell).Text("Ghi chú\n(Remarks)").Bold();
            });

            if (model.Items.Count == 0)
            {
                table.Cell().ColumnSpan(8).Border(1).Padding(8).AlignCenter().Text("No data");
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
                columns.ConstantColumn(40);
                columns.ConstantColumn(80);
                columns.ConstantColumn(220);
                columns.ConstantColumn(98);
                columns.ConstantColumn(60);
                columns.ConstantColumn(100);
                columns.ConstantColumn(100);
                columns.ConstantColumn(100);
            });

            static IContainer LeftCell(IContainer cell) => cell.Border(0).PaddingVertical(2).PaddingHorizontal(8);
            static IContainer RightCell(IContainer cell) => cell.Border(0).PaddingVertical(2).PaddingHorizontal(8).AlignRight();

            table.Cell().ColumnSpan(3).Element(LeftCell);
            table.Cell().ColumnSpan(3).Element(RightCell).Text("Tổng trước thuế (Before VAT):").Bold();
            table.Cell().Element(RightCell).Text(FormatMoney(model.BeforeVAT)).Bold();
            table.Cell().Element(LeftCell);

            table.Cell().ColumnSpan(3).Element(RightCell).Text($"%VAT: {FormatPercent(model.PerVAT)}").Bold();
            table.Cell().ColumnSpan(3).Element(RightCell).Text("Tiền thuế (VAT):").Bold();
            table.Cell().Element(RightCell).Text(FormatMoney(model.VAT)).Bold();
            table.Cell().Element(LeftCell);

            table.Cell().ColumnSpan(3).Element(LeftCell);
            table.Cell().ColumnSpan(3).Element(RightCell).Text("Tổng cộng (Total):").Bold();
            table.Cell().Element(RightCell).Text(FormatMoney(model.AfterVAT)).Bold();
            table.Cell().Element(LeftCell);
        });
    }

    private static void ComposeFooter(IContainer container, PurchaseOrderDetailReportModel model)
    {
        const string DefaultDirectorName = "TATSUYA FUKUZAWA";
        const string DefaultDirectorTitle = "Tổng Giám Đốc";
        var signer = ResolveSigner(model.Footer);

        container.Row(row =>
        {
            row.RelativeItem(1f).Column(left =>
            {
                left.Item().AlignCenter().Text("Công ty TNHH Vườn Thiên Đàng Sài Gòn").Bold().FontSize(9);
                var signatureWidth = signer.IsFinal ? 220 : 150;
                var signatureHeight = signer.IsFinal ? 80 : 50;

                left.Item().PaddingTop(34).Width(signatureWidth).Height(signatureHeight).AlignCenter().AlignMiddle().Element(cell =>
                {
                    if (signer.Signature != null && signer.Signature.Length > 0)
                    {
                        cell.Image(signer.Signature).FitArea();
                    }
                });

                left.Item().PaddingTop(2).AlignCenter().Column(box =>
                {
                    box.Item().Width(signer.IsFinal ? 190 : 150).AlignCenter().BorderTop(1).PaddingTop(4).Column(nameBox =>
                    {
                        nameBox.Item().AlignCenter().Text(string.IsNullOrWhiteSpace(signer.Name) ? DefaultDirectorName : signer.Name).Bold();
                        nameBox.Item().AlignCenter().Text(string.IsNullOrWhiteSpace(signer.Title) ? DefaultDirectorTitle : signer.Title).Italic();
                    });
                });
            });

            row.RelativeItem(1.3f).PaddingLeft(12).Column(right =>
            {
                right.Item().Text("NOTED:").Bold();
                foreach (var line in SplitNotes(model.Footer?.Notes ?? model.Notes ?? string.Empty))
                {
                    right.Item().Text($"+ {NormalizeLegacyText(line)}");
                }
            });
        });
    }

    private static (byte[]? Signature, string Name, string Title, bool IsFinal) ResolveSigner(PurchaseOrderApprovalFooterModel? footer)
    {
        if (footer?.ApprovedSignature is { Length: > 0 })
        {
            return (footer.ApprovedSignature, footer.ApprovedName, footer.ApprovedTitle, true);
        }

        if (footer?.CheckedSignature is { Length: > 0 })
        {
            return (footer.CheckedSignature, footer.CheckedName, footer.CheckedTitle, false);
        }

        if (footer?.PreparedSignature is { Length: > 0 })
        {
            return (footer.PreparedSignature, footer.PreparedName, footer.PreparedTitle, false);
        }

        return (null, string.Empty, string.Empty, false);
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
