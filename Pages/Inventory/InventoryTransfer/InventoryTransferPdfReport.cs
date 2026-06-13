using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SmartSam.Pages.Inventory.InventoryTransfer;

internal static class InventoryTransferPdfReport
{
    public static byte[] BuildPdf(InventoryTransferReportModel model)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(22);
                page.DefaultTextStyle(x => x.FontFamily("VNI-Times").FontSize(9));
                page.Content().Column(column =>
                {
                    column.Item().Element(c => ComposeHeader(c, model));
                    column.Item().PaddingTop(12).Element(c => ComposeDetails(c, model));
                });
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, InventoryTransferReportModel model)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
            });

            table.Cell().Text(model.FlowNo).Bold();
            table.Cell().AlignCenter().Column(c =>
            {
                c.Item().AlignCenter().Text("ITEM TRANSFER VOUCHER").FontFamily("Times New Roman").FontSize(16).Bold();
                c.Item().PaddingTop(2).AlignCenter().Text("PHIẾU CHUYỂN KHO").FontFamily("Times New Roman").FontSize(16).Bold();
            });
            table.Cell().AlignRight().Text(model.FlowDateText).Bold();
            table.Cell().PaddingTop(8).Text($"Issue House: {model.FromStoreName}");
            table.Cell().PaddingTop(8).AlignCenter().Text($"Enter House: {model.ToStoreName}");
            table.Cell().PaddingTop(8).AlignRight().Text($"Operator: {model.OperatorName}");
            table.Cell().ColumnSpan(3).PaddingTop(6).Text($"According To: {model.According}");
        });
    }

    private static void ComposeDetails(IContainer container, InventoryTransferReportModel model)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(28);
                columns.ConstantColumn(95);
                columns.RelativeColumn();
                columns.ConstantColumn(55);
                columns.ConstantColumn(70);
                columns.ConstantColumn(70);
                columns.ConstantColumn(80);
                columns.ConstantColumn(90);
            });

            static IContainer Header(IContainer c) => c.Border(1).Background(Colors.Grey.Lighten3).Padding(3).AlignMiddle();
            static IContainer Body(IContainer c) => c.Border(1).Padding(3).AlignMiddle();

            table.Header(header =>
            {
                header.Cell().Element(Header).AlignCenter().Text("No").Bold();
                header.Cell().Element(Header).Text("Item Code").Bold();
                header.Cell().Element(Header).Text("Item Name").Bold();
                header.Cell().Element(Header).AlignCenter().Text("Unit").Bold();
                header.Cell().Element(Header).AlignRight().Text("Doc Qty").Bold();
                header.Cell().Element(Header).AlignRight().Text("Act Qty").Bold();
                header.Cell().Element(Header).AlignRight().Text("Price").Bold();
                header.Cell().Element(Header).AlignRight().Text("Amount").Bold();
            });

            for (var i = 0; i < model.Items.Count; i++)
            {
                var item = model.Items[i];
                table.Cell().Element(Body).AlignCenter().Text((i + 1).ToString(CultureInfo.InvariantCulture));
                table.Cell().Element(Body).Text(item.ItemCode);
                table.Cell().Element(Body).Text(x => x.Span(item.ItemName).FontFamily(".VnTime"));
                table.Cell().Element(Body).AlignCenter().Text(item.Unit);
                table.Cell().Element(Body).AlignRight().Text(item.DocQty.ToString("#,##0", CultureInfo.InvariantCulture));
                table.Cell().Element(Body).AlignRight().Text(item.ActQty.ToString("#,##0", CultureInfo.InvariantCulture));
                table.Cell().Element(Body).AlignRight().Text(item.UnitPrice.ToString("#,##0", CultureInfo.InvariantCulture));
                table.Cell().Element(Body).AlignRight().Text(item.Amount.ToString("#,##0", CultureInfo.InvariantCulture));
            }
        });
    }
}

internal sealed class InventoryTransferReportModel
{
    public string FlowNo { get; set; } = string.Empty;
    public string FlowDateText { get; set; } = string.Empty;
    public string FromStoreName { get; set; } = string.Empty;
    public string ToStoreName { get; set; } = string.Empty;
    public string According { get; set; } = string.Empty;
    public string OperatorName { get; set; } = string.Empty;
    public List<InventoryTransferReportItem> Items { get; set; } = new();
}

internal sealed class InventoryTransferReportItem
{
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal DocQty { get; set; }
    public decimal ActQty { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
}
