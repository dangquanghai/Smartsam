using System.Collections;
using System.Globalization;
using System.Reflection;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace SmartSam.Pages.Inventory.LinenReport;

public static class LinenReportQuestPdfReport
{
    private const string DefaultPdfFontFamily = "VNI-Times";
    private const float PantryNameColumnWidth = 42;
    private const float PantryShiftColumnWidth = 18;
    private const float PantryValueColumnWidth = 20;

    public static byte[] BuildPdf(object preview, byte[]? companyLogo)
    {
        var reportType = GetString(preview, "reportType");
        var isLandscape = reportType is LinenReportTypes.Pantry or LinenReportTypes.LaundryRecord or LinenReportTypes.NotReceive;

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(isLandscape ? PageSizes.A4.Landscape() : PageSizes.A4);
                page.Margin(18);
                page.DefaultTextStyle(x => x.FontFamily(DefaultPdfFontFamily).FontSize(8));

                page.Content().Column(column =>
                {
                    column.Spacing(8);
                    ComposeHeader(column.Item(), preview, companyLogo, reportType);

                    switch (reportType)
                    {
                        case LinenReportTypes.Pantry:
                            ComposePantry(column.Item(), preview);
                            break;
                        case LinenReportTypes.Delivery:
                            ComposeDelivery(column.Item(), preview);
                            break;
                        case LinenReportTypes.Receive:
                            ComposeReceive(column.Item(), preview);
                            break;
                        case LinenReportTypes.LaundryRecord:
                            ComposeLaundryRecord(column.Item(), preview);
                            break;
                        case LinenReportTypes.NotReceive:
                            ComposeNotReceive(column.Item(), preview);
                            break;
                        case LinenReportTypes.LaundryBalance:
                            ComposeLaundryBalance(column.Item(), preview);
                            break;
                        case LinenReportTypes.ApmtBalance:
                            ComposeApartmentBalance(column.Item(), preview);
                            break;
                        default:
                            column.Item().Text("Unknown report type.");
                            break;
                    }
                });
            });
        }).GeneratePdf();
    }

    private static void ComposeHeader(IContainer container, object preview, byte[]? companyLogo, string reportType)
    {
        var columns = GetItems(GetValue(preview, "columns")).ToList();
        var headerWidth = reportType == LinenReportTypes.Pantry ? GetPantryTableWidth(columns.Count) : (float?)null;
        var title = reportType switch
        {
            LinenReportTypes.Pantry => "DAILY NOTE LINEN CONTROL",
            LinenReportTypes.Delivery => "LINEN DELIVERY",
            LinenReportTypes.Receive => "LINEN RECEIVE",
            LinenReportTypes.LaundryRecord => "LINEN-LAUDRY RECORD",
            LinenReportTypes.NotReceive => "LINEN NOT RECEIVE",
            LinenReportTypes.LaundryBalance => "LAUNDRY ROOM BALANCE",
            LinenReportTypes.ApmtBalance => "APARTMENT BALANCE",
            _ => "LINEN REPORT"
        };

        var rightDate = reportType switch
        {
            LinenReportTypes.Pantry => FormatSlashDate(GetString(preview, "dateText")),
            LinenReportTypes.Delivery => FormatShortDate(GetString(preview, "dateText")),
            LinenReportTypes.Receive => FormatShortDate(GetString(preview, "dateText")),
            _ => string.Empty
        };

        var headerContainer = headerWidth.HasValue
            ? container.AlignCenter().Width(headerWidth.Value)
            : container;

        headerContainer.Row(row =>
        {
            row.ConstantItem(90).Element(left =>
            {
                if (companyLogo != null && companyLogo.Length > 0)
                {
                    left.Height(44).AlignLeft().AlignMiddle().Image(companyLogo).FitArea();
                    return;
                }

                left.Column(textColumn =>
                {
                    textColumn.Item().PaddingTop(10).Text("S A I G O N").FontSize(6).FontColor("#1b64a5");
                    textColumn.Item().Text("SKYGARDEN").FontSize(9).Bold().FontColor("#1b64a5");
                });
            });

            row.RelativeItem().AlignCenter().PaddingTop(2).Text(title).Bold().FontSize(14);
            row.ConstantItem(90).AlignRight().Text(rightDate);
        });
    }

    private static void ComposePantry(IContainer container, object preview)
    {
        var columns = GetItems(GetValue(preview, "columns")).ToList();
        var rows = GetItems(GetValue(preview, "rows")).ToList();
        var tableWidth = GetPantryTableWidth(columns.Count);

        container.Column(column =>
        {
            column.Spacing(6);
            column.Item().AlignCenter().Width(tableWidth).Table(info =>
            {
                info.ColumnsDefinition(def =>
                {
                    def.ConstantColumn(70);
                    def.ConstantColumn(170);
                    def.ConstantColumn(60);
                    def.RelativeColumn();
                });
                info.Cell().Text("Pickup ID:");
                info.Cell().Text(GetString(preview, "descriptionId"));
                info.Cell().Text("Date:");
                info.Cell().Text(FormatSlashDate(GetString(preview, "dateText")));
                info.Cell().Text("Des:");
                info.Cell().ColumnSpan(3).Text(GetString(preview, "description"));
            });

            column.Item().AlignCenter().Width(tableWidth).Table(table =>
            {
                table.ColumnsDefinition(def =>
                {
                    def.ConstantColumn(PantryNameColumnWidth);
                    def.ConstantColumn(PantryShiftColumnWidth);
                    foreach (var _ in columns)
                    {
                        def.ConstantColumn(PantryValueColumnWidth);
                        def.ConstantColumn(PantryValueColumnWidth);
                        def.ConstantColumn(PantryValueColumnWidth);
                    }
                });

                table.Header(header =>
                {
                    header.Cell().RowSpan(2).Element(HeaderCell).AlignLeft().Text("NAME").Bold();
                    header.Cell().RowSpan(2).Element(HeaderCell).Text(string.Empty);
                    foreach (var item in columns)
                    {
                        header.Cell().ColumnSpan(3).Element(HeaderCell).AlignCenter().Text(GetString(item, "title")).Bold();
                    }
                    foreach (var _ in columns)
                    {
                        header.Cell().Element(HeaderCell).AlignCenter().Text("B").Bold();
                        header.Cell().Element(HeaderCell).AlignCenter().Text("R").Bold();
                        header.Cell().Element(HeaderCell).AlignCenter().Text("D").Bold();
                    }
                });

                foreach (var rowGroup in rows.GroupBy(x => GetInt(x, "pentry")).OrderBy(x => x.Key))
                {
                    var groupRows = rowGroup.OrderBy(x => GetInt(x, "timeSection")).ToList();
                    for (var rowIndex = 0; rowIndex < groupRows.Count; rowIndex++)
                    {
                        var row = groupRows[rowIndex];
                        if (rowIndex == 0)
                        {
                            table.Cell().RowSpan((uint)groupRows.Count).Element(x => BodyCell(x, Colors.White)).Text(GetString(row, "pentryName"));
                        }

                        var isA = GetInt(row, "timeSection") == 1;
                        table.Cell().Element(x => BodyCell(x, isA ? Colors.White : "#ececec")).AlignCenter().Text(isA ? "A" : "P").Bold();
                        foreach (var reportColumn in columns)
                        {
                            table.Cell().Element(x => BodyCell(x, Colors.White)).AlignRight().Text(FormatBlankZero(GetDecimal(row, GetString(reportColumn, "beField"))));
                            table.Cell().Element(x => BodyCell(x, Colors.White)).AlignRight().Text(FormatBlankZero(GetDecimal(row, GetString(reportColumn, "reField"))));
                            table.Cell().Element(x => BodyCell(x, Colors.White)).AlignRight().Text(FormatBlankZero(GetDecimal(row, GetString(reportColumn, "deField"))));
                        }
                    }
                }
            });
        });
    }

    private static void ComposeDelivery(IContainer container, object preview)
    {
        container.Column(column =>
        {
            column.Spacing(8);
            column.Item().Element(x => ComposeInfo(x, new[]
            {
                ("DeliveryID", GetString(preview, "deliveryId")),
                ("DeliveryDate", FormatShortDate(GetString(preview, "dateText"))),
                ("SupplierName", GetString(preview, "supplierName")),
                ("LaundryTypeName", GetString(preview, "deliveryTypeName")),
                ("Des", GetString(preview, "description"))
            }));
            column.Item().Element(x => ComposeDetailTable(x, GetItems(GetValue(preview, "rows")), "Deliverer", "Receiver"));
        });
    }

    private static void ComposeReceive(IContainer container, object preview)
    {
        container.Column(column =>
        {
            column.Spacing(8);
            column.Item().Element(x => ComposeInfo(x, new[]
            {
                ("ReceiveID", GetString(preview, "receiveId")),
                ("Receive Date", FormatShortDate(GetString(preview, "dateText"))),
                ("SupplierName", GetString(preview, "supplierName")),
                ("Des", GetString(preview, "description")),
                ("Ref Delivery", GetString(preview, "refDeliveryDescription"))
            }));
            column.Item().Element(x => ComposeDetailTable(x, GetItems(GetValue(preview, "rows")), "Deliverer", "Receiver"));
        });
    }

    private static void ComposeInfo(IContainer container, IEnumerable<(string Label, string Value)> rows)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(def =>
            {
                def.ConstantColumn(95);
                def.RelativeColumn();
            });

            foreach (var row in rows)
            {
                table.Cell().PaddingVertical(1).Text(row.Label).Bold();
                table.Cell().PaddingVertical(1).Text(row.Value);
            }
        });
    }

    private static void ComposeDetailTable(IContainer container, IEnumerable<object> rows, string leftSign, string rightSign)
    {
        container.Column(column =>
        {
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(def =>
                {
                    def.ConstantColumn(24);
                    def.RelativeColumn(1.3f);
                    def.ConstantColumn(70);
                    def.ConstantColumn(55);
                    def.ConstantColumn(55);
                    def.ConstantColumn(65);
                    def.RelativeColumn();
                });

                foreach (var label in new[] { "No.", "Location", "LinenCode", "Quantity", "Price", "Amount", "Note" })
                {
                    table.Cell().Element(HeaderCell).AlignCenter().Text(label).Bold();
                }

                var index = 1;
                decimal total = 0;
                foreach (var row in rows)
                {
                    total += GetDecimal(row, "amount");
                    table.Cell().Element(BodyCell).AlignCenter().Text(index++.ToString(CultureInfo.InvariantCulture));
                    table.Cell().Element(BodyCell).Text(GetString(row, "location"));
                    table.Cell().Element(BodyCell).Text(GetString(row, "linenCode"));
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatNumber(GetDecimal(row, "quantity")));
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatNumber(GetDecimal(row, "price")));
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatNumber(GetDecimal(row, "amount")));
                    table.Cell().Element(BodyCell).Text(GetString(row, "note"));
                }

                table.Cell().ColumnSpan(5).Element(BodyCell).Text(string.Empty);
                table.Cell().Element(BodyCell).AlignRight().Text(FormatNumber(total)).Bold();
                table.Cell().Element(BodyCell).Text(string.Empty);
            });

            column.Item().PaddingTop(28).Row(row =>
            {
                row.RelativeItem().AlignCenter().Text(leftSign).Bold();
                row.RelativeItem().AlignCenter().Text(rightSign).Bold();
            });
        });
    }

    private static void ComposeLaundryRecord(IContainer container, object preview)
    {
        var groups = GetItems(GetValue(preview, "groups")).ToList();
        container.Column(column =>
        {
            column.Spacing(6);
            column.Item().Text($"From Dat: {FormatShortDate(GetString(preview, "fromDate"))}    To Date: {FormatShortDate(GetString(preview, "toDate"))}");
            column.Item().Table(table =>
            {
                table.ColumnsDefinition(def =>
                {
                    def.ConstantColumn(62);
                    def.ConstantColumn(36);
                    for (var day = 1; day <= 31; day++)
                    {
                        def.ConstantColumn(16);
                    }
                    def.ConstantColumn(40);
                });

                table.Header(header =>
                {
                    header.Cell().Element(HeaderCell).Text("LinenCode").Bold();
                    header.Cell().Element(HeaderCell).AlignRight().Text("Price").Bold();
                    for (var day = 1; day <= 31; day++)
                    {
                        header.Cell().Element(HeaderCell).AlignCenter().Text(day.ToString("00", CultureInfo.InvariantCulture)).Bold();
                    }
                    header.Cell().Element(HeaderCell).AlignRight().Text("Total QT").Bold();
                });

                foreach (var group in groups)
                {
                    table.Cell().ColumnSpan(34).Element(x => BodyCell(x, "#f7f7f7")).Text(GetString(group, "supplierName")).Bold();
                    table.Cell().ColumnSpan(34).Element(x => BodyCell(x, "#fbfbfb")).Text(GetString(group, "groupName")).Bold();

                    foreach (var row in GetItems(GetValue(group, "rows")))
                    {
                        table.Cell().Element(BodyCell).Text(GetString(row, "linenCode"));
                        table.Cell().Element(BodyCell).AlignRight().Text(FormatNumber(GetDecimal(row, "price")));
                        foreach (var day in GetItems(GetValue(row, "days")).Take(31))
                        {
                            table.Cell().Element(BodyCell).AlignRight().Text(FormatBlankZero(GetDecimal(day)));
                        }
                        table.Cell().Element(BodyCell).AlignRight().Text(FormatNumber(GetDecimal(row, "total")));
                    }
                }
            });
        });
    }

    private static void ComposeNotReceive(IContainer container, object preview)
    {
        container.Table(table =>
        {
            table.ColumnsDefinition(def =>
            {
                def.ConstantColumn(55);
                def.ConstantColumn(58);
                def.RelativeColumn(1.2f);
                def.RelativeColumn();
                def.ConstantColumn(70);
                def.ConstantColumn(48);
                def.ConstantColumn(48);
                def.ConstantColumn(55);
            });

            foreach (var label in new[] { "DeliveryID", "Date", "Description", "Supplier", "Linnen", "De", "Re", "Remain" })
            {
                table.Cell().Element(HeaderCell).AlignCenter().Text(label).Bold();
            }
            foreach (var row in GetItems(GetValue(preview, "rows")))
            {
                table.Cell().Element(BodyCell).AlignRight().Text(GetString(row, "deliveryId"));
                table.Cell().Element(BodyCell).AlignCenter().Text(FormatShortDate(GetString(row, "deliveryDate")));
                table.Cell().Element(BodyCell).Text(GetString(row, "description"));
                table.Cell().Element(BodyCell).Text(GetString(row, "supplierName"));
                table.Cell().Element(BodyCell).Text(GetString(row, "linenCode"));
                table.Cell().Element(BodyCell).AlignRight().Text(FormatNumber(GetDecimal(row, "quantityDe")));
                table.Cell().Element(BodyCell).AlignRight().Text(FormatNumber(GetDecimal(row, "quantityRe")));
                table.Cell().Element(BodyCell).AlignRight().Text(FormatNumber(GetDecimal(row, "remain")));
            }
        });
    }

    private static void ComposeLaundryBalance(IContainer container, object preview)
    {
        container.Column(column =>
        {
            column.Spacing(6);
            column.Item().Text($"From Dat: {FormatShortDate(GetString(preview, "fromDate"))}    To Date: {FormatShortDate(GetString(preview, "toDate"))}");
            column.Item().AlignCenter().Table(table =>
            {
                table.ColumnsDefinition(def =>
                {
                    def.RelativeColumn();
                    def.ConstantColumn(55);
                    def.ConstantColumn(55);
                    def.ConstantColumn(55);
                    def.ConstantColumn(55);
                    def.ConstantColumn(55);
                    def.ConstantColumn(55);
                });
                foreach (var label in new[] { "LinenCode", "Begin", "R Apmt", "R Supplier", "D Apmt", "D Supplier", "End" })
                {
                    table.Cell().Element(HeaderCell).AlignCenter().Text(label).Bold();
                }
                foreach (var row in GetItems(GetValue(preview, "rows")))
                {
                    table.Cell().Element(BodyCell).Text(GetString(row, "linenCode"));
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatNumber(GetDecimal(row, "begin")));
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatNumber(GetDecimal(row, "receiveApartment")));
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatNumber(GetDecimal(row, "receiveSupplier")));
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatNumber(GetDecimal(row, "deliveryApartment")));
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatNumber(GetDecimal(row, "deliverySupplier")));
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatNumber(GetDecimal(row, "end")));
                }
            });
        });
    }

    private static void ComposeApartmentBalance(IContainer container, object preview)
    {
        container.Column(column =>
        {
            column.Spacing(6);
            column.Item().Text($"Apartment No: {GetString(preview, "apartmentNo")}    From Dat: {FormatShortDate(GetString(preview, "fromDate"))}    To Dat: {FormatShortDate(GetString(preview, "toDate"))}");
            column.Item().AlignCenter().Table(table =>
            {
                table.ColumnsDefinition(def =>
                {
                    def.RelativeColumn();
                    def.ConstantColumn(70);
                    def.ConstantColumn(90);
                    def.ConstantColumn(90);
                    def.ConstantColumn(70);
                });
                foreach (var label in new[] { "Linen", "TonDau", "NhapVaoCanH", "XuatRaTuCanH", "TonCuoi" })
                {
                    table.Cell().Element(HeaderCell).AlignCenter().Text(label).Bold();
                }
                foreach (var row in GetItems(GetValue(preview, "rows")))
                {
                    table.Cell().Element(BodyCell).Text(GetString(row, "linenCode"));
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatNumber(GetDecimal(row, "begin")));
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatNumber(GetDecimal(row, "receiveApartment")));
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatNumber(GetDecimal(row, "deliveryApartment")));
                    table.Cell().Element(BodyCell).AlignRight().Text(FormatNumber(GetDecimal(row, "end")));
                }
            });
        });
    }

    private static IContainer HeaderCell(IContainer container)
    {
        return container.Border(1).BorderColor("#555555").PaddingVertical(2).PaddingHorizontal(2);
    }

    private static IContainer BodyCell(IContainer container)
    {
        return BodyCell(container, Colors.White);
    }

    private static IContainer BodyCell(IContainer container, string backgroundColor)
    {
        return container.Border(1).BorderColor("#555555").Background(backgroundColor).PaddingVertical(1).PaddingHorizontal(2);
    }

    private static object? GetValue(object? source, string propertyName)
    {
        if (source == null || string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        if (source is string or decimal or int or long or double or float)
        {
            return source;
        }

        var property = source.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        return property?.GetValue(source);
    }

    private static IEnumerable<object> GetItems(object? value)
    {
        if (value is IEnumerable items && value is not string)
        {
            foreach (var item in items)
            {
                if (item != null)
                {
                    yield return item;
                }
            }
        }
    }

    private static string GetString(object? source, string propertyName)
    {
        return Convert.ToString(GetValue(source, propertyName), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static int GetInt(object? source, string propertyName)
    {
        var value = GetValue(source, propertyName);
        return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;
    }

    private static decimal GetDecimal(object? source, string propertyName)
    {
        return GetDecimal(GetValue(source, propertyName));
    }

    private static decimal GetDecimal(object? value)
    {
        if (value == null)
        {
            return 0;
        }

        if (value is decimal decimalValue)
        {
            return decimalValue;
        }

        return decimal.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;
    }

    private static string FormatNumber(decimal value)
    {
        return value == decimal.Truncate(value)
            ? value.ToString("0", CultureInfo.InvariantCulture)
            : value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string FormatBlankZero(decimal value)
    {
        return value == 0 ? string.Empty : FormatNumber(value);
    }

    private static float GetPantryTableWidth(int columnCount)
    {
        return PantryNameColumnWidth + PantryShiftColumnWidth + (columnCount * PantryValueColumnWidth * 3);
    }

    private static string FormatShortDate(string value)
    {
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return value;
        }

        return date.ToString("dd-MMM-yy", CultureInfo.InvariantCulture);
    }

    private static string FormatSlashDate(string value)
    {
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return value;
        }

        return date.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture);
    }
}
