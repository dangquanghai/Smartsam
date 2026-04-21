using System.Globalization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SmartSam.Pages.Purchasing.AnalyzingSuppliers;

internal static class AnalyzingSuppliersPdfReport
{
    public static byte[] BuildPdf(AnalyzingSuppliersReportModel model)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Portrait());
                page.Margin(18);
                page.DefaultTextStyle(x => x.FontFamily("Times New Roman").FontSize(8));

                page.Header().Element(header => ComposeHeader(header, model));
                page.Content().PaddingTop(8).Element(content => ComposeContent(content, model));
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

    private static void ComposeHeader(IContainer container, AnalyzingSuppliersReportModel model)
    {
        container.Column(column =>
        {
            column.Item().Row(row =>
            {
                row.RelativeItem().Text("Saigon Sky Garden").Bold();
                row.ConstantItem(140).AlignRight().Text($"Date: {model.GeneratedDate:dd - MMM - yyyy}");
            });

            column.Item().PaddingTop(8).AlignCenter().Text("Supplier Report").Bold().FontSize(16);

            column.Item().PaddingTop(10).Row(row =>
            {
                row.RelativeItem().Text(text =>
                {
                    text.Span("Supplier: ").Bold();
                    text.Span($"{model.SupplierCode} - {model.SupplierName}");
                });
                row.ConstantItem(75).Text(text =>
                {
                    text.Span("Good ").Bold();
                    text.Span(model.GoodCount.ToString(CultureInfo.InvariantCulture));
                });
                row.ConstantItem(90).Text(text =>
                {
                    text.Span("Not Good ").Bold();
                    text.Span(model.NotGoodCount.ToString(CultureInfo.InvariantCulture));
                });
            });
        });
    }

    private static void ComposeContent(IContainer container, AnalyzingSuppliersReportModel model)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(columns =>
            {
                columns.ConstantColumn(58);
                columns.ConstantColumn(54);
                columns.ConstantColumn(58);
                columns.RelativeColumn(2.2f);
                columns.ConstantColumn(56);
                columns.ConstantColumn(64);
                columns.RelativeColumn(2.2f);
            });

            static IContainer HeaderCenterCell(IContainer cell) => cell.Border(1).Background(Colors.Grey.Lighten3).Padding(4).AlignCenter().AlignMiddle();
            static IContainer HeaderCenterCompactCell(IContainer cell) => cell.Border(1).Background(Colors.Grey.Lighten3).PaddingVertical(4).PaddingHorizontal(2).AlignCenter().AlignMiddle();
            static IContainer HeaderLeftCell(IContainer cell) => cell.Border(1).Background(Colors.Grey.Lighten3).Padding(4).AlignLeft().AlignMiddle();
            static IContainer BodyCell(IContainer cell) => cell.Border(1).Padding(3).AlignTop();
            static IContainer BodyCompactCell(IContainer cell) => cell.Border(1).PaddingVertical(3).PaddingHorizontal(2).AlignTop();

            table.Header(header =>
            {
                header.Cell().Element(HeaderLeftCell).Text("PO No.").Bold();
                header.Cell().Element(HeaderLeftCell).Text("PR No.").Bold();
                header.Cell().Element(HeaderCenterCell).Text("Date").Bold();
                header.Cell().Element(HeaderLeftCell).Text("Remark").Bold();
                header.Cell().Element(HeaderCenterCompactCell).Text("Status").Bold();
                header.Cell().Element(HeaderCenterCompactCell).Text("Assess Level").Bold();
                header.Cell().Element(HeaderLeftCell).Text("Comment").Bold();
            });

            if (model.Rows.Count == 0)
            {
                table.Cell().ColumnSpan(7).Element(BodyCell).AlignCenter().Text("No data");
                return;
            }

            foreach (var row in model.Rows)
            {
                table.Cell().Element(BodyCell).AlignLeft().Text(row.PONo);
                table.Cell().Element(BodyCell).AlignLeft().Text(row.PRNo);
                table.Cell().Element(BodyCell).AlignCenter().Text(row.PODate?.ToString("dd-MMM-yyyy", CultureInfo.InvariantCulture) ?? string.Empty);
                table.Cell().Element(BodyCell).AlignLeft().Text(row.Remark);
                table.Cell().Element(BodyCompactCell).AlignCenter().Text(row.StatusName).FontSize(7.5f);
                table.Cell().Element(BodyCompactCell).AlignCenter().Text(row.AssessLevelName).FontSize(7.5f);
                table.Cell().Element(BodyCell).AlignLeft().Text(row.Comment);
            }
        });
    }
}

internal sealed class AnalyzingSuppliersReportModel
{
    public string SupplierCode { get; set; } = string.Empty;
    public string SupplierName { get; set; } = string.Empty;
    public DateTime GeneratedDate { get; set; } = DateTime.Today;
    public int GoodCount { get; set; }
    public int NotGoodCount { get; set; }
    public List<AnalyzingSuppliersReportRow> Rows { get; set; } = new();
}

internal sealed class AnalyzingSuppliersReportRow
{
    public string PONo { get; set; } = string.Empty;
    public string PRNo { get; set; } = string.Empty;
    public DateTime? PODate { get; set; }
    public string Remark { get; set; } = string.Empty;
    public string StatusName { get; set; } = string.Empty;
    public string AssessLevelName { get; set; } = string.Empty;
    public string Comment { get; set; } = string.Empty;
}
