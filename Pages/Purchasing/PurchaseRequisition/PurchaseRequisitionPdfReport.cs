using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SmartSam.Pages.Purchasing.PurchaseRequisition;

internal static class PurchaseRequisitionPdfReport
{
    public static byte[] BuildDetailPdf(PurchaseRequisitionDetailReportModel model)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(18);
                page.DefaultTextStyle(x => x.FontFamily("Times New Roman").FontSize(9));

                page.Header().Element(header => ComposeDetailHeader(header, model));
                page.Content().PaddingTop(8).Element(content => ComposeDetailContent(content, model));
                page.Footer().AlignRight().Text(text =>
                {
                    text.Span("Page ");
                    text.CurrentPageNumber();
                    text.Span(" of ");
                    text.TotalPages();
                });
            });
        }).GeneratePdf();
    }

    public static byte[] BuildSummaryPdf(PurchaseRequisitionSummaryReportModel model)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(18);
                page.DefaultTextStyle(x => x.FontFamily("Times New Roman").FontSize(9));

                page.Header().Element(header => ComposeSummaryHeader(header, model));
                page.Content().PaddingTop(8).Element(content => ComposeSummaryContent(content, model));
                page.Footer().Row(row =>
                {
                    row.RelativeItem().AlignLeft().Text(model.GeneratedDate.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture));
                    row.RelativeItem().AlignRight().Text(text =>
                    {
                        text.Span("Page ");
                        text.CurrentPageNumber();
                        text.Span(" of ");
                        text.TotalPages();
                    });
                });
            });
        }).GeneratePdf();
    }

    private static void ComposeDetailHeader(IContainer container, PurchaseRequisitionDetailReportModel model)
    {
        container.Column(column =>
        {
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(95);
                    columns.RelativeColumn();
                    columns.ConstantColumn(75);
                });

                table.Cell().Border(1).Padding(6).AlignMiddle().Text("Saigon\nSky\nGarden").Bold().FontSize(12);
                table.Cell().BorderTop(1).BorderBottom(1).Padding(6).AlignCenter().Column(inner =>
                {
                    inner.Item().Text("PURCHASE REQUISITION").Bold().FontSize(16);
                    inner.Item().Text("PHIẾU ĐỀ NGHỊ MUA HÀNG").Bold().FontSize(15);
                });
                table.Cell().Border(1).Padding(6).AlignMiddle().Column(inner =>
                {
                    inner.Item().Text($"No.: {model.NoIso}").Bold();
                    inner.Item().Text($"Rev.: {model.Rev}").Bold();
                });
            });

            column.Item().PaddingTop(6).Column(info =>
            {
                info.Item().Row(row =>
                {
                    row.ConstantItem(180).Text($"Purchase Requisition Number: {model.RequestNo}").Bold();
                    row.RelativeItem().Text($"Date:  {FormatDate(model.RequestDate, "dd - MMM - yyyy")}").Bold();
                });
                info.Item().Text($"Purchase Purpose: {model.Description}");
                info.Item().Text($"Currency Unit: {model.CurrencyText}");
            });
        });
    }

    private static void ComposeDetailContent(IContainer container, PurchaseRequisitionDetailReportModel model)
    {
        container.Column(column =>
        {
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(28);
                    columns.ConstantColumn(95);
                    columns.RelativeColumn(2.6f);
                    columns.ConstantColumn(42);
                    columns.ConstantColumn(58);
                    columns.ConstantColumn(58);
                    columns.ConstantColumn(88);
                    columns.ConstantColumn(95);
                    columns.RelativeColumn(1.8f);
                });

                static IContainer HeaderCell(IContainer cell) => cell.Border(1).Background(Colors.Grey.Lighten3).Padding(4).AlignCenter().AlignMiddle();
                static IContainer HeaderLeftCell(IContainer cell) => cell.Border(1).Background(Colors.Grey.Lighten3).Padding(4).AlignLeft().AlignMiddle();
                static IContainer BodyCell(IContainer cell) => cell.Border(1).Padding(3).AlignTop();

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("No").Bold();
                    header.Cell().Element(HeaderLeftCell).Text("Item Code").Bold();
                    header.Cell().Element(HeaderLeftCell).Text("Item Description").Bold();
                    header.Cell().Element(HeaderCell).Text("Unit").Bold();
                    header.Cell().Element(HeaderCell).Text("Qty MR").Bold();
                    header.Cell().Element(HeaderCell).Text("Qty Pur.").Bold();
                    header.Cell().Element(HeaderCell).Text("U.Price").Bold();
                    header.Cell().Element(HeaderCell).Text("Amount").Bold();
                    header.Cell().Element(HeaderLeftCell).Text("Remarks/Purpose").Bold();
                });

                for (var index = 0; index < model.Items.Count; index++)
                {
                    var item = model.Items[index];
                    table.Cell().Element(BodyCell).AlignCenter().Text((index + 1).ToString(CultureInfo.InvariantCulture));
                    table.Cell().Element(BodyCell).AlignLeft().Text(item.ItemCode);
                    table.Cell().Element(BodyCell).AlignLeft().Text(item.ItemDescription);
                    table.Cell().Element(BodyCell).AlignCenter().Text(item.Unit);
                    table.Cell().Element(BodyCell).AlignCenter().Text(FormatQuantity(item.QtyMr));
                    table.Cell().Element(BodyCell).AlignCenter().Text(FormatQuantity(item.QtyPur));
                    table.Cell().Element(BodyCell).AlignCenter().Text(FormatAmount(item.UnitPrice));
                    table.Cell().Element(BodyCell).AlignCenter().Text(FormatAmount(item.Amount));
                    table.Cell().Element(BodyCell).AlignLeft().Text(item.Remark);
                }

                table.Cell().ColumnSpan(6).Border(0).PaddingTop(2);
                table.Cell().Border(0).Padding(3).AlignRight().Text("Total:").Bold();
                table.Cell().Border(1).Padding(3).AlignRight().Text(FormatAmount(model.TotalAmount)).Bold();
                table.Cell().Border(0).PaddingTop(2);
            });

            if (model.Footer != null)
            {
                column.Item().PaddingTop(18).Element(footer => ComposeDetailApprovalFooter(footer, model.Footer));
            }
        });
    }

    private static void ComposeDetailApprovalFooter(IContainer container, PurchaseRequisitionApprovalFooterModel footer)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.RelativeColumn();
                columns.RelativeColumn();
                columns.RelativeColumn();
            });

            ComposeApprovalBox(table, "Prepared by", footer.PreparedDate, footer.PreparedTitle, footer.PreparedSignature);
            ComposeApprovalBox(table, "Checked by", footer.CheckedDate, footer.CheckedTitle, footer.CheckedSignature);
            ComposeApprovalBox(table, "Approved by", footer.ApprovedDate, footer.ApprovedTitle, footer.ApprovedSignature);
        });
    }

    private static void ComposeApprovalBox(TableDescriptor table, string header, string dateText, string title, byte[]? signature)
    {
        table.Cell().Border(1).Padding(4).Column(column =>
        {
            column.Item().AlignCenter().Text(header).Bold();
            column.Item().PaddingTop(2).Text($"Date:   {dateText}");
            column.Item().Height(58).AlignCenter().AlignMiddle().Element(cell =>
            {
                if (signature != null && signature.Length > 0)
                {
                    cell.Image(signature).FitArea();
                }
            });
            column.Item().AlignCenter().Text(title).Bold();
        });
    }

    private static void ComposeSummaryHeader(IContainer container, PurchaseRequisitionSummaryReportModel model)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(85);
                columns.RelativeColumn();
                columns.ConstantColumn(70);
            });

            table.Cell().Border(1).Padding(4).AlignCenter().AlignMiddle().Text("SAIGON\nSKY\nGARDEN").Bold().FontSize(11);
            table.Cell().BorderTop(1).BorderBottom(1).Padding(6).AlignCenter().Column(inner =>
            {
                inner.Item().Text("PURCHASE REPORT").Bold().FontSize(16);
                inner.Item().Text("BÁO CÁO MUA HÀNG").Bold().FontSize(15);
            });
            table.Cell().Border(1).Padding(6).AlignMiddle().Column(inner =>
            {
                inner.Item().Text($"No.: {model.NoIso}").Bold();
            });
        });
    }

    private static void ComposeSummaryContent(IContainer container, PurchaseRequisitionSummaryReportModel model)
    {
        container.Column(column =>
        {
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(columns =>
                {
                    columns.ConstantColumn(95);
                    columns.RelativeColumn(2.5f);
                    columns.ConstantColumn(60);
                    columns.ConstantColumn(60);
                    columns.ConstantColumn(60);
                    columns.ConstantColumn(72);
                    columns.ConstantColumn(82);
                    columns.RelativeColumn(1.2f);
                });

                static IContainer HeaderCell(IContainer cell) => cell.Border(1).Background(Colors.Grey.Lighten3).Padding(4).AlignCenter().AlignMiddle();
                static IContainer HeaderLeftCell(IContainer cell) => cell.Border(1).Background(Colors.Grey.Lighten3).Padding(4).AlignLeft().AlignMiddle();
                static IContainer GroupCell(IContainer cell) => cell.Border(1).Background("#F7F7F7").Padding(3).AlignTop();

                table.Header(header =>
                {
                    header.Cell().Element(HeaderLeftCell).Text("Item Code").Bold();
                    header.Cell().Element(HeaderLeftCell).Text("Item Name").Bold();
                    header.Cell().Element(HeaderCell).Text("PR Qty").Bold();
                    header.Cell().Element(HeaderCell).Text("Rec Qty").Bold();
                    header.Cell().Element(HeaderCell).Text("Dif Qty").Bold();
                    header.Cell().Element(HeaderCell).Text("Rec Date").Bold();
                    header.Cell().Element(HeaderCell).Text("PO No").Bold();
                    header.Cell().Element(HeaderLeftCell).Text("Remark").Bold();
                });

                foreach (var group in model.Groups)
                {
                    table.Cell().ColumnSpan(8).Element(GroupCell).Row(row =>
                    {
                        row.ConstantItem(95).Text($"PR No.: {group.RequestNo}").Bold();
                        row.ConstantItem(120).Text($"PR Date: {FormatDate(group.RequestDate, "d - MMM - yyyy")}").Bold();
                        row.RelativeItem().Text($"Description: {group.Description}");
                    });

                    for (var itemIndex = 0; itemIndex < group.Items.Count; itemIndex++)
                    {
                        var item = group.Items[itemIndex];
                        var isLastItem = itemIndex == group.Items.Count - 1;

                        IContainer BodyCell(IContainer cell) => cell
                            .BorderLeft(1)
                            .BorderRight(1)
                            .BorderBottom(isLastItem ? 1 : 0)
                            .Padding(3)
                            .AlignTop();

                        table.Cell().Element(BodyCell).AlignLeft().Text(item.ItemCode);
                        table.Cell().Element(BodyCell).AlignLeft().Text(item.ItemName);
                        table.Cell().Element(BodyCell).AlignCenter().Text(FormatQuantity(item.PrQty));
                        table.Cell().Element(BodyCell).AlignCenter().Text(FormatQuantity(item.RecQty));
                        table.Cell().Element(BodyCell).AlignCenter().Text(FormatQuantity(item.DiffQty));
                        table.Cell().Element(BodyCell).AlignCenter().Text(item.RecDateText);
                        table.Cell().Element(BodyCell).AlignCenter().Text(item.PoNo);
                        table.Cell().Element(BodyCell).AlignLeft().Text(item.Remark);
                    }
                }

                if (model.Groups.Count == 0)
                {
                    table.Cell().ColumnSpan(8).Border(1).Padding(8).AlignCenter().Text("No data");
                }
            });

            if (model.Footer != null)
            {
                column.Item().PaddingTop(18).Element(footer => ComposeDetailApprovalFooter(footer, model.Footer));
            }
        });
    }

    private static string FormatDate(DateTime? date, string format)
    {
        return date.HasValue ? date.Value.ToString(format, CultureInfo.InvariantCulture) : string.Empty;
    }

    private static string FormatQuantity(decimal value)
    {
        return value.ToString("#,##0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatAmount(decimal value)
    {
        return value.ToString("#,##0.00", CultureInfo.InvariantCulture);
    }
}

internal sealed class PurchaseRequisitionDetailReportModel
{
    public string NoIso { get; set; } = string.Empty;
    public string Rev { get; set; } = string.Empty;
    public string RequestNo { get; set; } = string.Empty;
    public DateTime? RequestDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public string CurrencyText { get; set; } = "VND";
    public decimal TotalAmount { get; set; }
    public List<PurchaseRequisitionDetailReportItem> Items { get; set; } = new();
    public PurchaseRequisitionApprovalFooterModel? Footer { get; set; }
}

internal sealed class PurchaseRequisitionDetailReportItem
{
    public string ItemCode { get; set; } = string.Empty;
    public string ItemDescription { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public decimal QtyMr { get; set; }
    public decimal QtyPur { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal Amount { get; set; }
    public string Remark { get; set; } = string.Empty;
}

internal sealed class PurchaseRequisitionApprovalFooterModel
{
    public string PreparedDate { get; set; } = string.Empty;
    public string CheckedDate { get; set; } = string.Empty;
    public string ApprovedDate { get; set; } = string.Empty;
    public string PreparedTitle { get; set; } = "Purchase Officer";
    public string CheckedTitle { get; set; } = "Chief Accountant";
    public string ApprovedTitle { get; set; } = "General Director";
    public byte[]? PreparedSignature { get; set; }
    public byte[]? CheckedSignature { get; set; }
    public byte[]? ApprovedSignature { get; set; }
}

internal sealed class PurchaseRequisitionSummaryReportModel
{
    public string NoIso { get; set; } = string.Empty;
    public string Rev { get; set; } = string.Empty;
    public DateTime GeneratedDate { get; set; } = DateTime.Today;
    public List<PurchaseRequisitionSummaryGroup> Groups { get; set; } = new();
    public PurchaseRequisitionApprovalFooterModel? Footer { get; set; }
}

internal sealed class PurchaseRequisitionSummaryGroup
{
    public string RequestNo { get; set; } = string.Empty;
    public DateTime? RequestDate { get; set; }
    public string Description { get; set; } = string.Empty;
    public List<PurchaseRequisitionSummaryItem> Items { get; set; } = new();
}

internal sealed class PurchaseRequisitionSummaryItem
{
    public string ItemCode { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public decimal PrQty { get; set; }
    public decimal RecQty { get; set; }
    public decimal DiffQty { get; set; }
    public string RecDateText { get; set; } = string.Empty;
    public string PoNo { get; set; } = string.Empty;
    public string Remark { get; set; } = string.Empty;
}
