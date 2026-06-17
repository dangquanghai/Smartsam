using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SmartSam.Pages.Inventory.InventoryReport;

internal static class InventoryStockReportPdf
{
    public static byte[] BuildPdf(InventoryStockReportModel model)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Portrait());
                page.Margin(18);
                page.DefaultTextStyle(x => x.FontFamily("Times New Roman").FontSize(8));
                page.Content().Column(column =>
                {
                    column.Item().Element(c => ComposeHeader(c, model));
                    column.Item().PaddingTop(14).Element(c => ComposePeriod(c, model));
                    column.Item().PaddingTop(14).Element(c => ComposeStore(c, model));
                    column.Item().PaddingTop(18).Element(c => ComposeDetails(c, model));
                    column.Item().PaddingTop(26).Element(c => ComposeSignatures(c, model));
                });
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, InventoryStockReportModel model)
    {
        container.Border(1).Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(150);
                columns.RelativeColumn();
                columns.ConstantColumn(150);
            });

            table.Cell().BorderRight(1).PaddingVertical(10).AlignCenter().AlignMiddle().Text("SAIGON\nSKY\nGARDEN").FontSize(16).Bold().LineHeight(1.2f);
            table.Cell().BorderRight(1).PaddingVertical(12).AlignCenter().AlignMiddle().Column(column =>
            {
                column.Item().AlignCenter().Text("INVENTORY REPORT").FontSize(16).Bold();
                column.Item().PaddingTop(4).AlignCenter().Text("BÁO CÁO TỒN KHO").FontSize(16).Bold();
            });
            table.Cell().PaddingVertical(12).AlignCenter().AlignMiddle().Column(column =>
            {
                column.Item().AlignCenter().Text("No.: QF15-4").FontSize(11);
                column.Item().PaddingTop(4).AlignCenter().Text("Rev.: 0.0").FontSize(11);
            });
        });
    }

    private static void ComposePeriod(IContainer container, InventoryStockReportModel model)
    {
        container.Row(row =>
        {
            row.RelativeItem().AlignCenter().Text(text =>
            {
                text.Span("From   ").Bold().FontSize(11);
                text.Span(model.FromDateText).Bold().FontSize(11);
            });
            row.RelativeItem().AlignCenter().Text(text =>
            {
                text.Span("To   ").Bold().FontSize(11);
                text.Span(model.ToDateText).Bold().FontSize(11);
            });
            row.ConstantItem(120).AlignRight().Text(model.PrintDateText).FontSize(9);
        });
    }

    private static void ComposeStore(IContainer container, InventoryStockReportModel model)
    {
        container.Text(text =>
        {
            text.Span("Store Hourse:   ").Italic().Bold().Underline().FontSize(10);
            text.Span(model.StoreName).Italic().Bold().Underline().FontSize(10);
        });
    }

    private static void ComposeDetails(IContainer container, InventoryStockReportModel model)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(34);
                columns.ConstantColumn(96);
                columns.RelativeColumn();
                columns.ConstantColumn(52);
                columns.ConstantColumn(52);
                columns.ConstantColumn(52);
                columns.ConstantColumn(52);
                columns.ConstantColumn(42);
                columns.ConstantColumn(28);
            });

            static IContainer Header(IContainer c) => c.Border(1).PaddingVertical(5).PaddingHorizontal(3).AlignMiddle();
            static IContainer Body(IContainer c) => c.Border(1).BorderColor(Colors.Grey.Darken2).PaddingVertical(2).PaddingHorizontal(3).AlignMiddle();

            table.Header(header =>
            {
                header.Cell().Element(Header).AlignCenter().Text("No").Bold().FontSize(9);
                header.Cell().Element(Header).Text("Item Code").Bold().FontSize(9);
                header.Cell().Element(Header).Text("Item Name").Bold().FontSize(9);
                header.Cell().Element(Header).AlignRight().Text("B Qty").Bold().FontSize(9);
                header.Cell().Element(Header).AlignRight().Text("ReQy").Bold().FontSize(9);
                header.Cell().Element(Header).AlignRight().Text("Iss Qty").Bold().FontSize(9);
                header.Cell().Element(Header).AlignRight().Text("EQty").Bold().FontSize(9);
                header.Cell().Element(Header).AlignCenter().Text("Fact").Bold().FontSize(9);
                header.Cell().Element(Header).AlignCenter().Text("#").Bold().FontSize(9);
            });

            for (var i = 0; i < model.Items.Count; i++)
            {
                var item = model.Items[i];
                table.Cell().Element(Body).AlignRight().Text((i + 1).ToString(CultureInfo.InvariantCulture));
                table.Cell().Element(Body).Text(item.ItemCode);
                table.Cell().Element(Body).Text(x => x.Span(item.ItemName).FontFamily(".VnTime"));
                table.Cell().Element(Body).AlignRight().Text(FormatQty(item.BeginQuantity));
                table.Cell().Element(Body).AlignRight().Text(FormatQty(item.ReceiveQuantity));
                table.Cell().Element(Body).AlignRight().Text(FormatQty(item.IssueQuantity));
                table.Cell().Element(Body).AlignRight().Text(FormatQty(item.EndQuantity));
                table.Cell().Element(Body).Text(string.Empty);
                table.Cell().Element(Body).Text(string.Empty);
            }
        });
    }

    private static void ComposeSignatures(IContainer container, InventoryStockReportModel model)
    {
        container.Row(row =>
        {
            row.RelativeItem().Element(c => ComposeSignatureBlock(c, "Prepared By", model.PreparedSignature));
            row.RelativeItem().Element(c => ComposeSignatureBlock(c, "Checked By", model.CheckedSignature));
        });
    }

    private static void ComposeSignatureBlock(IContainer container, string title, byte[]? signature)
    {
        container.AlignCenter().Width(220).Column(column =>
        {
            column.Item().Height(18).AlignCenter().AlignMiddle().Text(title).FontSize(9);
            column.Item().Height(70).AlignCenter().AlignMiddle().Element(c => DrawSignature(c, signature));
            column.Item().Height(18);
        });
    }

    private static void DrawSignature(IContainer container, byte[]? signature)
    {
        if (signature is not { Length: > 0 }) return;
        container.AlignCenter().AlignMiddle().MaxWidth(190).Height(62).Image(signature).FitArea();
    }

    private static string FormatQty(decimal value) => value.ToString("#,##0.00", CultureInfo.InvariantCulture);
}

internal sealed class InventoryStockReportModel
{
    public string FromDateText { get; set; } = string.Empty;
    public string ToDateText { get; set; } = string.Empty;
    public string PrintDateText { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
    public byte[]? PreparedSignature { get; set; }
    public byte[]? CheckedSignature { get; set; }
    public List<InventoryStockReportItem> Items { get; set; } = new();
}

internal sealed class InventoryStockReportItem
{
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public decimal BeginQuantity { get; set; }
    public decimal ReceiveQuantity { get; set; }
    public decimal IssueQuantity { get; set; }
    public decimal EndQuantity { get; set; }
}
