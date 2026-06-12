using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SmartSam.Pages.Inventory.InventoryIssue;

internal static class InventoryIssuePdfReport
{
    public static byte[] BuildPdf(InventoryIssueReportModel model)
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
                    column.Item().PaddingTop(24).Element(c => ComposeSignatures(c, model));
                });
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, InventoryIssueReportModel model)
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
                c.Item().AlignCenter().Text("ITEM ISSUE VOUCHER").FontFamily("Lato").FontSize(16).Bold();
                c.Item().PaddingTop(2).AlignCenter().Text("PHI?U XU?T HÀNG").FontFamily("Lato").FontSize(16).Bold();
            });
            table.Cell().AlignRight().Text(model.FlowDateText).Bold();
            table.Cell().PaddingTop(8).Text($"Issue House: {model.StoreName}");
            table.Cell().PaddingTop(8).AlignCenter().Text($"Department: {model.DepartmentName}");
            table.Cell().PaddingTop(8).AlignRight().Text($"Location: {model.LocationName}");
            table.Cell().ColumnSpan(3).PaddingTop(6).Text($"According To: {model.According}");
            table.Cell().ColumnSpan(3).PaddingTop(2).Text($"Reason: {model.Reason}");
        });
    }

    private static void ComposeDetails(IContainer container, InventoryIssueReportModel model)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(28);
                columns.ConstantColumn(95);
                columns.RelativeColumn();
                columns.ConstantColumn(55);
                columns.ConstantColumn(65);
                columns.ConstantColumn(65);
                columns.ConstantColumn(80);
                columns.ConstantColumn(100);
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
                header.Cell().Element(Header).AlignRight().Text("Amount").Bold();
                header.Cell().Element(Header).Text("Location").Bold();
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
                table.Cell().Element(Body).AlignRight().Text(item.Amount.ToString("#,##0", CultureInfo.InvariantCulture));
                table.Cell().Element(Body).Text(item.LocationName);
            }
        });
    }

    private static void ComposeSignatures(IContainer container, InventoryIssueReportModel model)
    {
        container.Row(row =>
        {
            row.Spacing(24);
            row.RelativeItem().Element(c => ComposeSignatureBlock(c, "Storeman", model.StoremanSignature, model.StoremanName));
            row.RelativeItem().Element(c => ComposeSignatureBlock(c, "Confirmed by", model.ReceiverSignature, model.ReceiverName));
        });
    }

    private static void ComposeSignatureBlock(IContainer container, string title, byte[]? signature, string signerName)
    {
        container.AlignCenter().Width(250).Column(column =>
        {
            column.Item().Height(22).AlignCenter().AlignMiddle().Text(title).Bold();
            column.Item().Height(70).AlignCenter().AlignMiddle().Element(c => DrawSignature(c, signature));
            column.Item().Height(22);
        });
    }

    private static void DrawSignature(IContainer container, byte[]? signature)
    {
        if (signature is not { Length: > 0 })
            return;

        container
            .AlignCenter()
            .AlignMiddle()
            .MaxWidth(210)
            .Height(62)
            .Image(signature)
            .FitArea();
    }
}

internal sealed class InventoryIssueReportModel
{
    public string FlowNo { get; set; } = string.Empty;
    public string FlowDateText { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
    public string DepartmentName { get; set; } = string.Empty;
    public string LocationName { get; set; } = string.Empty;
    public string According { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string StoremanName { get; set; } = string.Empty;
    public string ReceiverName { get; set; } = string.Empty;
    public byte[]? StoremanSignature { get; set; }
    public byte[]? ReceiverSignature { get; set; }
    public List<InventoryIssueReportItem> Items { get; set; } = new();
}

internal sealed class InventoryIssueReportItem
{
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal DocQty { get; set; }
    public decimal ActQty { get; set; }
    public decimal Amount { get; set; }
    public string LocationName { get; set; } = string.Empty;
}



