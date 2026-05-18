using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SmartSam.Pages.Inventory.LinnenNoteDaily;

public static class LinnenNoteDailyQuestPdfReport
{
    private const string DefaultPdfFontFamily = "VNI-Times";
    private const float PantryColumnWidth = 42;
    private const float ShiftColumnWidth = 18;
    private const float LinenValueColumnWidth = 20;

    public static byte[] BuildPdf(LinnenNoteDailyPdfReport report)
    {
        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(18);
                page.DefaultTextStyle(x => x.FontFamily(DefaultPdfFontFamily).FontSize(7));

                page.Content().Column(column =>
                {
                    column.Spacing(6);
                    column.Item().Element(x => ComposeHeader(x, report));
                    column.Item().Element(x => ComposeTable(x, report));
                });
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, LinnenNoteDailyPdfReport report)
    {
        var tableWidth = GetTableWidth(report.Columns);

        container.AlignCenter().Width(tableWidth).Column(column =>
        {
            column.Item().Row(row =>
            {
                row.ConstantItem(86).Element(left =>
                {
                    if (report.CompanyLogo != null && report.CompanyLogo.Length > 0)
                    {
                        left.PaddingLeft(2).Height(44).AlignLeft().AlignMiddle().Image(report.CompanyLogo).FitArea();
                    }
                    else
                    {
                        left.Column(textColumn =>
                        {
                            textColumn.Item().PaddingTop(10).Text("S A I G O N").FontSize(6).FontColor("#1b64a5");
                            textColumn.Item().Text("SKYGARDEN").FontSize(9).Bold().FontColor("#1b64a5");
                        });
                    }
                });

                row.RelativeItem().Column(center =>
                {
                    center.Item().AlignCenter().PaddingTop(2).Text("DAILY NOTE LINEN CONTROL").Bold().FontSize(13);
                    center.Item().PaddingTop(10).Row(info =>
                    {
                        info.ConstantItem(220).Text($"Pickup ID: {report.NoteId}");
                        info.RelativeItem().Text($"Date: {report.DateCreate:MM/dd/yyyy}");
                    });
                    center.Item().PaddingTop(4).Text($"Des: {report.Description ?? string.Empty}");
                });

                row.ConstantItem(90).AlignRight().Text($"{report.DateCreate:MM/dd/yyyy}");
            });
        });
    }

    private static void ComposeTable(IContainer container, LinnenNoteDailyPdfReport report)
    {
        var columns = report.Columns ?? new List<LinnenReportPreviewColumn>();
        var rows = report.Rows ?? new List<LinnenReportPreviewRow>();
        var tableWidth = GetTableWidth(columns);

        container.AlignCenter().Width(tableWidth).Table(table =>
        {
            table.ColumnsDefinition(definition =>
            {
                definition.ConstantColumn(PantryColumnWidth);
                definition.ConstantColumn(ShiftColumnWidth);
                foreach (var _ in columns)
                {
                    definition.ConstantColumn(LinenValueColumnWidth);
                    definition.ConstantColumn(LinenValueColumnWidth);
                    definition.ConstantColumn(LinenValueColumnWidth);
                }
            });

            table.Header(header =>
            {
                header.Cell().RowSpan(2).Element(HeaderCell).AlignLeft().Text("NAME").Bold();
                header.Cell().RowSpan(2).Element(HeaderCell).Text(string.Empty);

                foreach (var column in columns)
                {
                    header.Cell().ColumnSpan(3).Element(HeaderCell).AlignCenter().Text(column.Title).Bold();
                }

                foreach (var _ in columns)
                {
                    header.Cell().Element(HeaderCell).AlignCenter().Text("B").Bold();
                    header.Cell().Element(HeaderCell).AlignCenter().Text("R").Bold();
                    header.Cell().Element(HeaderCell).AlignCenter().Text("D").Bold();
                }
            });

            foreach (var pantryGroup in rows.OrderBy(x => x.Pentry).ThenBy(x => x.TimeSection).GroupBy(x => x.Pentry))
            {
                var groupRows = pantryGroup.OrderBy(x => x.TimeSection).ToList();
                for (var rowIndex = 0; rowIndex < groupRows.Count; rowIndex++)
                {
                    var row = groupRows[rowIndex];

                    if (rowIndex == 0)
                    {
                        table.Cell()
                            .RowSpan((uint)groupRows.Count)
                            .Element(x => BodyCell(x, Colors.White))
                            .Text(row.PentryName);
                    }

                    table.Cell()
                        .Element(x => BodyCell(x, row.TimeSection == 1 ? Colors.White : "#ececec"))
                        .AlignCenter()
                        .Text(row.TimeSection == 1 ? "A" : "P")
                        .Bold();

                    foreach (var column in columns)
                    {
                        table.Cell().Element(x => BodyCell(x, Colors.White)).AlignRight().Text(FormatValue(GetValue(row, column.BeField)));
                        table.Cell().Element(x => BodyCell(x, Colors.White)).AlignRight().Text(FormatValue(GetValue(row, column.ReField)));
                        table.Cell().Element(x => BodyCell(x, Colors.White)).AlignRight().Text(FormatValue(GetValue(row, column.DeField)));
                    }
                }
            }
        });
    }

    private static IContainer HeaderCell(IContainer container)
    {
        return container
            .Border(1)
            .BorderColor("#555555")
            .PaddingVertical(2)
            .PaddingHorizontal(2);
    }

    private static IContainer BodyCell(IContainer container, string backgroundColor)
    {
        return container
            .Border(1)
            .BorderColor("#555555")
            .Background(backgroundColor)
            .PaddingVertical(1)
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

    private static float GetTableWidth(IReadOnlyCollection<LinnenReportPreviewColumn>? columns)
    {
        return PantryColumnWidth + ShiftColumnWidth + ((columns?.Count ?? 0) * LinenValueColumnWidth * 3);
    }
}

public class LinnenNoteDailyPdfReport
{
    public int NoteId { get; set; }
    public string Description { get; set; } = string.Empty;
    public DateTime DateCreate { get; set; }
    public byte[]? CompanyLogo { get; set; }
    public List<LinnenReportPreviewColumn> Columns { get; set; } = new List<LinnenReportPreviewColumn>();
    public List<LinnenReportPreviewRow> Rows { get; set; } = new List<LinnenReportPreviewRow>();
}
