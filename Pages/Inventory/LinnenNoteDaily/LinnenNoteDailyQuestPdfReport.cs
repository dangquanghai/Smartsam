using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SmartSam.Pages.Inventory.LinnenNoteDaily;

public static class LinnenNoteDailyQuestPdfReport
{
    public static byte[] BuildPdf(LinnenNoteDailyPdfReport report)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(18);
                page.DefaultTextStyle(x => x.FontSize(8));

                page.Content().Column(column =>
                {
                    column.Spacing(8);

                    column.Item().AlignCenter().Text("DAILY NOTE LINEN CONTROL").Bold().FontSize(15);

                    column.Item().Row(row =>
                    {
                        row.RelativeItem().Text($"Pickup ID: {report.NoteId}");
                        row.RelativeItem().Text($"Date: {report.DateCreate:dd/MM/yyyy}").AlignRight();
                    });

                    column.Item().Text(text =>
                    {
                        text.Span("Des: ").Bold();
                        text.Span(report.Description ?? string.Empty);
                    });

                    column.Item().Element(x => ComposeTable(x, report));
                });
            });
        }).GeneratePdf();
    }

    private static void ComposeTable(IContainer container, LinnenNoteDailyPdfReport report)
    {
        var columns = report.Columns ?? new List<LinnenReportPreviewColumn>();
        var rows = report.Rows ?? new List<LinnenReportPreviewRow>();

        container.Table(table =>
        {
            table.ColumnsDefinition(definition =>
            {
                definition.ConstantColumn(52);
                definition.ConstantColumn(18);
                foreach (var _ in columns)
                {
                    definition.ConstantColumn(22);
                    definition.ConstantColumn(22);
                    definition.ConstantColumn(22);
                }
            });

            table.Header(header =>
            {
                header.Cell().RowSpan(2).ColumnSpan(2).Element(HeaderCell).AlignCenter().Text("Pantry\nNo").Bold();
                foreach (var column in columns)
                {
                    header.Cell().ColumnSpan(3).Element(HeaderCell).AlignCenter().Text(column.Title).Bold();
                }

                foreach (var _ in columns)
                {
                    header.Cell().Element(HeaderCell).AlignCenter().Text("Be").Bold();
                    header.Cell().Element(HeaderCell).AlignCenter().Text("De").Bold();
                    header.Cell().Element(HeaderCell).AlignCenter().Text("Re").Bold();
                }
            });

            foreach (var row in rows)
            {
                table.Cell().Element(BodyCell).Text(row.PentryName).Bold();
                table.Cell().Element(BodyCell).AlignCenter().Text(row.TimeSection == 1 ? "A" : "P").Bold();

                foreach (var column in columns)
                {
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatValue(GetValue(row, column.BeField)));
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatValue(GetValue(row, column.DeField)));
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatValue(GetValue(row, column.ReField)));
                }
            }
        });
    }

    private static IContainer HeaderCell(IContainer container)
    {
        return container
            .Border(1)
            .BorderColor(Colors.Grey.Darken1)
            .Background(Colors.Grey.Lighten3)
            .PaddingVertical(3)
            .PaddingHorizontal(2);
    }

    private static IContainer BodyCell(IContainer container)
    {
        return container
            .Border(1)
            .BorderColor(Colors.Grey.Darken1)
            .PaddingVertical(2)
            .PaddingHorizontal(2);
    }

    private static int GetValue(LinnenReportPreviewRow row, string propertyName)
    {
        var property = typeof(LinnenReportPreviewRow).GetProperty(propertyName);
        if (property == null)
        {
            return 0;
        }

        var value = property.GetValue(row);
        if (value == null)
        {
            return 0;
        }

        return Convert.ToInt32(value);
    }

    private static string FormatValue(int value)
    {
        return value == 0 ? string.Empty : value.ToString();
    }
}

public class LinnenNoteDailyPdfReport
{
    public int NoteId { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime DateCreate { get; set; }
    public List<LinnenReportPreviewColumn> Columns { get; set; } = new List<LinnenReportPreviewColumn>();
    public List<LinnenReportPreviewRow> Rows { get; set; } = new List<LinnenReportPreviewRow>();
}
