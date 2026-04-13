using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SmartSam.Pages.Purchasing.PurchaseOrder;

internal static class PurchaseOrderViewDetailQuestPdfReport
{
    public static byte[] BuildPdf(IReadOnlyList<PurchaseOrderViewDetailRow> rows)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(8);
                page.DefaultTextStyle(style => style.FontFamily("Times New Roman").FontSize(10));

                page.Content().Column(column =>
                {
                    column.Item().Element(ComposeHeader);
                    column.Item().PaddingTop(8).Element(content => ComposeTable(content, rows));
                });
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(IContainer container)
    {
        container.Row(row =>
        {
            row.ConstantItem(60).Border(1).Padding(2).AlignCenter().AlignMiddle().Text(text =>
            {
                text.AlignCenter();
                text.Line("Saigon").Bold().FontSize(10);
                text.Line("Sky").Bold().FontSize(10);
                text.Line("Garden").Bold().FontSize(10);
            });

            row.RelativeItem().Border(1).PaddingHorizontal(10).AlignCenter().AlignMiddle().Column(column =>
            {
                column.Item().AlignCenter().Text("PURCHASE ORDER REPORT").Bold().FontSize(16);
                column.Item().AlignCenter().Text("BÁO CÁO MUA HÀNG").Bold().FontSize(15);
            });

            row.ConstantItem(56).Border(1).Padding(3).AlignCenter().AlignMiddle().Column(column =>
            {
                column.Item().AlignCenter().Text("QF6-6").FontSize(9);
                column.Item().AlignCenter().Text("Rev.: 0-0").FontSize(9);
            });
        });
    }

    private static void ComposeTable(IContainer container, IReadOnlyList<PurchaseOrderViewDetailRow> rows)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(52);
                columns.RelativeColumn(2.2f);
                columns.ConstantColumn(55);
                columns.ConstantColumn(52);
                columns.ConstantColumn(48);
                columns.ConstantColumn(48);
                columns.ConstantColumn(42);
                columns.ConstantColumn(52);
                columns.ConstantColumn(52);
                columns.ConstantColumn(42);
                columns.ConstantColumn(54);
                columns.ConstantColumn(54);
                columns.ConstantColumn(50);
                columns.RelativeColumn(1.1f);
                columns.RelativeColumn(1.5f);
            });

            static IContainer HeaderCell(IContainer cell) => cell.Border(1).Background(Colors.Grey.Lighten3).PaddingVertical(3).PaddingHorizontal(2).AlignCenter().AlignMiddle();
            static IContainer BodyCell(IContainer cell) => cell.Border(1).PaddingVertical(2).PaddingHorizontal(3).AlignMiddle();

            table.Header(header =>
            {
                header.Cell().RowSpan(2).Element(HeaderCell).Text("Item Code").Bold();
                header.Cell().RowSpan(2).Element(HeaderCell).Text("Item Name").Bold();
                header.Cell().RowSpan(2).Element(HeaderCell).Text("PO No").Bold();
                header.Cell().RowSpan(2).Element(HeaderCell).Text("PO Date").Bold();
                header.Cell().ColumnSpan(2).Element(HeaderCell).Text("U.Price").Bold();
                header.Cell().RowSpan(2).Element(HeaderCell).Text("O.Qty").Bold();
                header.Cell().ColumnSpan(2).Element(HeaderCell).Text("Ord. Amt").Bold();
                header.Cell().RowSpan(2).Element(HeaderCell).Text("R.Qty").Bold();
                header.Cell().ColumnSpan(2).Element(HeaderCell).Text("Rec.Amt").Bold();
                header.Cell().RowSpan(2).Element(HeaderCell).Text("Rec. Date").Bold();
                header.Cell().RowSpan(2).Element(HeaderCell).Text("Purpose").Bold();
                header.Cell().RowSpan(2).Element(HeaderCell).Text("Note").Bold();

                header.Cell().Element(HeaderCell).Text("VND").Bold();
                header.Cell().Element(HeaderCell).Text("USD").Bold();
                header.Cell().Element(HeaderCell).Text("VND").Bold();
                header.Cell().Element(HeaderCell).Text("USD").Bold();
                header.Cell().Element(HeaderCell).Text("VND").Bold();
                header.Cell().Element(HeaderCell).Text("USD").Bold();
            });

            if (rows.Count == 0)
            {
                table.Cell().ColumnSpan(15).Border(1).Padding(8).AlignCenter().Text("No data");
                return;
            }

            foreach (var row in rows)
            {
                var isUsd = IsUsdCurrency(row);

                table.Cell().Element(BodyCell).Text(Encode(row.ItemCode));
                table.Cell().Element(BodyCell).Text(NormalizeLegacyText(row.ItemName));
                table.Cell().Element(BodyCell).Text(Encode(row.PONo));
                table.Cell().Element(BodyCell).Text(FormatDate(row.PODate, "MM/dd/yy"));

                table.Cell().Element(BodyCell).AlignRight().Text(isUsd ? string.Empty : FormatMoney(row.UnitPrice, false));
                table.Cell().Element(BodyCell).AlignRight().Text(isUsd ? FormatMoney(row.UnitPrice, true) : string.Empty);
                table.Cell().Element(BodyCell).AlignRight().Text(FormatQuantity(row.Quantity));
                table.Cell().Element(BodyCell).AlignRight().Text(isUsd ? string.Empty : FormatMoney(row.POAmount, false));
                table.Cell().Element(BodyCell).AlignRight().Text(isUsd ? FormatMoney(row.POAmount, true) : string.Empty);
                table.Cell().Element(BodyCell).AlignRight().Text(FormatQuantity(row.RecQty));
                table.Cell().Element(BodyCell).AlignRight().Text(isUsd ? string.Empty : FormatMoney(row.RecAmount, false));
                table.Cell().Element(BodyCell).AlignRight().Text(isUsd ? FormatMoney(row.RecAmount, true) : string.Empty);
                table.Cell().Element(BodyCell).Text(FormatDate(row.RecDate, "MM/dd/yy"));
                table.Cell().Element(BodyCell).Text(NormalizeLegacyText(row.ForDepartment));
                table.Cell().Element(BodyCell).Text(NormalizeLegacyText(row.Note));
            }
        });
    }

    private static bool IsUsdCurrency(PurchaseOrderViewDetailRow row)
    {
        if (row.CurrencyId == 2)
        {
            return true;
        }

        return row.CurrencyName.Contains("USD", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeLegacyText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var text = PurchaseOrderQuestPdfReport.NormalizeTcvn3Text(value);
        text = PurchaseOrderQuestPdfReport.NormalizeVniText(text);
        return text;
    }

    private static string Encode(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    }

    private static string FormatDate(DateTime? date, string format)
    {
        return date.HasValue ? date.Value.ToString(format, CultureInfo.InvariantCulture) : string.Empty;
    }

    private static string FormatQuantity(decimal value)
    {
        return value == 0 ? string.Empty : value.ToString("#,##0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatMoney(decimal value, bool withDecimals)
    {
        return withDecimals
            ? value.ToString("#,##0.00", CultureInfo.InvariantCulture)
            : value.ToString("#,##0", CultureInfo.InvariantCulture);
    }
}
