using System.Net;
using System.Net.Mail;
using ClosedXML.Excel;
using Dapper;
using Microsoft.Data.SqlClient;
using SmartSam.Helpers;

namespace SmartSam.Services
{
    // Housekeeping Status Report - gửi 2 lần/ngày cho team Housekeeping.
    //  * 16:00 (ngày D): 3 danh sách Short-term (Living hiện tại / Check In ngày D+1 / Check Out ngày D+1),
    //    đồng thời lưu snapshot vào CM_ContractSTSnapshot.
    //  * 07:35 (sáng D+1): dựng lại từ snapshot ngày D, đối chiếu hiện trạng và đánh dấu thay đổi
    //    (Mới vào / Đã ra / Đã check in / Đổi số người) + 2 cột Occ (16:00 và 07:35).
    // Học theo ApartmentStatusReportService. Chỉ xét hợp đồng IsShortTerm = 1.
    public class HousekeepingStatusReportService
    {
        private readonly IConfiguration _config;

        // Section trong bảng snapshot
        private const int SEC_LIVING = 1;
        private const int SEC_CHECKOUT = 2;
        private const int SEC_CHECKIN = 3;

        public HousekeepingStatusReportService(IConfiguration config)
        {
            _config = config;
        }

        // Câu SELECT gốc (chỉ Short-term) - dùng chung cho cả 2 job.
        private const string BaseSelect = @"
            SELECT
                c.ContractID,
                c.CurrentApartmentNo,
                cus.CustomerName,
                c.Occupy,
                c.PlanCheckinDate,
                c.PlanCheckoutDate,
                pb.PaymentByName,
                c.ContractStatus,
                c.IsSTWTR,
                c.ActCheckinDate,
                c.ActCheckoutDate
            FROM dbo.CM_Contract c
            INNER JOIN dbo.CM_Customer cus ON c.Representator = cus.CustomerID
            INNER JOIN dbo.CM_ContractPaymentBy pb ON c.PaymentByID = pb.PaymentByID
            WHERE c.IsShortTerm = 1 ";

        // ============================ JOB 16:00 ============================
        public async Task ProcessAfternoonReport()
        {
            DateTime runDate = DateTime.Today;          // D
            DateTime nextStart = runDate.AddDays(1);    // D+1 00:00
            DateTime nextEnd = runDate.AddDays(2);      // D+2 00:00 (biên trên loại trừ)

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            await conn.OpenAsync();

            // Living: đang ở tại thời điểm chạy (status = 2)
            var living = (await conn.QueryAsync<ContractRow>(
                BaseSelect + " AND c.ContractStatus = 2 ORDER BY c.CurrentApartmentNo;")).ToList();

            // Will be checked In: status = 1, PlanCheckinDate = ngày hôm sau (D+1)
            var checkIn = (await conn.QueryAsync<ContractRow>(
                BaseSelect + @" AND c.ContractStatus = 1
                                AND c.PlanCheckinDate >= @NextStart AND c.PlanCheckinDate < @NextEnd
                                ORDER BY c.CurrentApartmentNo;",
                new { NextStart = nextStart, NextEnd = nextEnd })).ToList();

            // Will be checked Out: status = 2, PlanCheckoutDate = ngày hôm sau (D+1)
            var checkOut = (await conn.QueryAsync<ContractRow>(
                BaseSelect + @" AND c.ContractStatus = 2
                                AND c.PlanCheckoutDate >= @NextStart AND c.PlanCheckoutDate < @NextEnd
                                ORDER BY c.CurrentApartmentNo;",
                new { NextStart = nextStart, NextEnd = nextEnd })).ToList();

            // Lưu snapshot ngày D (ghi đè nếu chạy lại cùng ngày -> idempotent)
            await SaveSnapshot(conn, runDate, living, checkIn, checkOut);

            // Excel bản 16:00: format gọn, 1 cột Occ, không cột Ghi chú
            byte[] excel = BuildAfternoonExcel(runDate, living, checkIn, checkOut);

            string message = $@"
                <p style='font-weight: bold; font-size: 16px; color: #333;'>Dear Housekeeping Team,</p>
                <p>Please find attached the <b>Apartment Status Report</b> (Short-term) as of
                   <b>{runDate:MMM d, yyyy} 16:00</b>.</p>
                <div style='background-color: #f9f9f9; border-left: 4px solid #16a085; padding: 15px; margin: 15px 0; color: #555;'>
                    <ul style='margin: 0; padding-left: 18px;'>
                        <li>Short-term Living: <b>{living.Count}</b> apartment(s)</li>
                        <li>Short-term Will be checked Out (next day): <b>{checkOut.Count}</b> apartment(s)</li>
                        <li>Short-term Will be checked In (next day): <b>{checkIn.Count}</b> apartment(s)</li>
                    </ul>
                </div>
                <p>An updated version with overnight changes will be sent tomorrow at 07:35.</p>";

            string html = EmailTemplateHelper.WrapInNotifyTemplate(
                "APARTMENT STATUS REPORT (16:00)", "#16a085", runDate, message);

            await SendEmail(html, excel,
                $"[Housekeeping] APARTMENT STATUS REPORT (16:00) - {runDate:dd/MM/yyyy}",
                $"HousekeepingStatusReport_16h_{runDate:yyyyMMdd}.xlsx");
        }

        // ============================ JOB 07:35 ============================
        public Task ProcessMorningReport()
        {
            DateTime morningDate = DateTime.Today;             // D+1 (hôm nay khi job sáng chạy)
            DateTime snapshotDate = morningDate.AddDays(-1);   // D (ngày snapshot 16:00)
            DateTime wStart = snapshotDate.AddHours(16);       // 16:00 hôm qua
            DateTime wEnd = DateTime.Now;                      // ~07:35 hôm nay
            return RunMorningReport(morningDate, snapshotDate, wStart, wEnd);
        }

        // Test hook: chỉ định snapshotDate + cửa sổ so sánh để chạy thử ngay trong ngày (không dùng ở production).
        public Task ProcessMorningReportTest(DateTime snapshotDate, DateTime wStart, DateTime wEnd)
            => RunMorningReport(DateTime.Today, snapshotDate, wStart, wEnd);

        private async Task RunMorningReport(DateTime morningDate, DateTime snapshotDate, DateTime wStart, DateTime wEnd)
        {
            DateTime todayStart = morningDate;                 // 00:00 ngày báo cáo
            DateTime todayEnd = morningDate.AddDays(1);        // 00:00 ngày kế tiếp

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            await conn.OpenAsync();

            // Đọc snapshot ngày D
            var snap = (await conn.QueryAsync<SnapshotRow>(
                "SELECT * FROM dbo.CM_ContractSTSnapshot WHERE SnapshotDate = @D;",
                new { D = snapshotDate })).ToList();

            // Trạng thái hiện tại của tất cả hợp đồng short-term (đối chiếu trong bộ nhớ)
            var currentList = (await conn.QueryAsync<ContractRow>(BaseSelect + ";")).ToList();
            var current = currentList
                .GroupBy(r => r.ContractID)
                .ToDictionary(g => g.Key, g => g.First());

            // Fallback: không có snapshot -> gửi báo cáo hiện trạng như bản 16:00 (không đánh dấu)
            if (snap.Count == 0)
            {
                var livingNow = currentList.Where(r => r.ContractStatus == 2)
                    .OrderBy(r => r.CurrentApartmentNo).ToList();
                var checkInToday = currentList.Where(r => r.ContractStatus == 1
                        && r.PlanCheckinDate >= todayStart && r.PlanCheckinDate < todayEnd)
                    .OrderBy(r => r.CurrentApartmentNo).ToList();
                var checkOutToday = currentList.Where(r => r.ContractStatus == 2
                        && r.PlanCheckoutDate >= todayStart && r.PlanCheckoutDate < todayEnd)
                    .OrderBy(r => r.CurrentApartmentNo).ToList();

                byte[] fbExcel = BuildAfternoonExcel(morningDate, livingNow, checkInToday, checkOutToday);
                string fbMsg = $@"
                    <p style='font-weight: bold; font-size: 16px; color: #333;'>Dear Housekeeping Team,</p>
                    <p>Attached is the current <b>Apartment Status Report</b> (Short-term) as of
                       <b>{morningDate:MMM d, yyyy} 07:35</b>.</p>
                    <p style='color:#c0392b;'>Note: no 16:00 snapshot from {snapshotDate:dd/MM/yyyy} was found,
                       so overnight change marks are not available this time.</p>";
                string fbHtml = EmailTemplateHelper.WrapInNotifyTemplate(
                    "APARTMENT STATUS REPORT (07:35)", "#2980b9", morningDate, fbMsg);
                await SendEmail(fbHtml, fbExcel,
                    $"[Housekeeping] APARTMENT STATUS REPORT (07:35) - {morningDate:dd/MM/yyyy}",
                    $"HousekeepingStatusReport_0735_{morningDate:yyyyMMdd}.xlsx");
                return;
            }

            var snapLiving = snap.Where(s => s.Section == SEC_LIVING).ToDictionary(s => s.ContractID);
            var snapCheckIn = snap.Where(s => s.Section == SEC_CHECKIN).ToDictionary(s => s.ContractID);
            var snapCheckOut = snap.Where(s => s.Section == SEC_CHECKOUT).ToDictionary(s => s.ContractID);

            var livingRows = BuildMorningLiving(snapLiving, current, wStart, wEnd);
            var checkInRows = BuildMorningCheckIn(snapCheckIn, current, wStart, wEnd, todayStart, todayEnd);
            var checkOutRows = BuildMorningCheckOut(snapCheckOut, current, wStart, wEnd, todayStart, todayEnd);

            byte[] excel = BuildMorningExcel(morningDate, snapshotDate, livingRows, checkInRows, checkOutRows);

            string message = $@"
                <p style='font-weight: bold; font-size: 16px; color: #333;'>Dear Housekeeping Team,</p>
                <p>Attached is the updated <b>Apartment Status Report</b> (Short-term) as of
                   <b>{morningDate:MMM d, yyyy} 07:35</b>, compared with the 16:00 list of {snapshotDate:dd/MM/yyyy}.</p>
                <div style='background-color: #f9f9f9; border-left: 4px solid #2980b9; padding: 15px; margin: 15px 0; color: #555;'>
                    <p style='margin:0 0 6px 0;'>The <b>Note</b> column shows overnight changes:</p>
                    <ul style='margin: 0; padding-left: 18px;'>
                        <li><b>Mới vào</b> = newly checked in with reservation (16:00 → 07:35)</li>
                        <li><b>Sudden Check In</b> = checked in without reservation (IsSTWTR)</li>
                        <li><b>Đã ra</b> = checked out (16:00 → 07:35)</li>
                        <li><b>Đã check in</b> = moved into Living</li>
                        <li><b>Đổi số người</b> = occupancy changed (see Occ 16:00 vs Occ 07:35)</li>
                    </ul>
                </div>";
            string html = EmailTemplateHelper.WrapInNotifyTemplate(
                "APARTMENT STATUS REPORT (07:35)", "#2980b9", morningDate, message);

            await SendEmail(html, excel,
                $"[Housekeeping] APARTMENT STATUS REPORT (07:35) - {morningDate:dd/MM/yyyy}",
                $"HousekeepingStatusReport_0735_{morningDate:yyyyMMdd}.xlsx");
        }

        // ---------- Reconciliation 07:35 ----------

        private List<MorningRow> BuildMorningLiving(
            Dictionary<long, SnapshotRow> snapLiving, Dictionary<long, ContractRow> current,
            DateTime wStart, DateTime wEnd)
        {
            var rows = new List<MorningRow>();
            var seen = new HashSet<long>();

            // 1) Từ danh sách Living lúc 16:00
            foreach (var s in snapLiving.Values.OrderBy(x => x.CurrentApartmentNo))
            {
                seen.Add(s.ContractID);
                current.TryGetValue(s.ContractID, out var cur);
                var row = MorningRowFromSnapshot(s, cur);
                var notes = new List<string>();

                bool departed = cur != null && cur.ContractStatus == 3
                    && InWindow(cur.ActCheckoutDate, wStart, wEnd);
                if (departed) notes.Add("Đã ra");

                AddOccChangeNote(notes, s.Occupy, cur?.Occupy);
                row.Note = string.Join("; ", notes);
                rows.Add(row);
            }

            // 2) Khách mới vào Living trong khoảng W (chưa có trong snapshot Living)
            foreach (var cur in current.Values
                         .Where(c => c.ContractStatus == 2 && InWindow(c.ActCheckinDate, wStart, wEnd)
                                     && !seen.Contains(c.ContractID))
                         .OrderBy(c => c.CurrentApartmentNo))
            {
                var row = MorningRowFromCurrent(cur);
                row.Occ16 = null;                 // chưa có lúc 16:00
                // IsSTWTR = Short Term Without Reservation -> khách vào đột xuất, không đặt trước
                row.Note = cur.IsSTWTR ? "Sudden Check In" : "Mới vào";
                rows.Add(row);
            }

            return rows;
        }

        private List<MorningRow> BuildMorningCheckIn(
            Dictionary<long, SnapshotRow> snapCheckIn, Dictionary<long, ContractRow> current,
            DateTime wStart, DateTime wEnd, DateTime todayStart, DateTime todayEnd)
        {
            var rows = new List<MorningRow>();
            var seen = new HashSet<long>();

            // 1) Từ danh sách Check In lúc 16:00
            foreach (var s in snapCheckIn.Values.OrderBy(x => x.CurrentApartmentNo))
            {
                seen.Add(s.ContractID);
                current.TryGetValue(s.ContractID, out var cur);
                var row = MorningRowFromSnapshot(s, cur);
                var notes = new List<string>();

                bool movedToLiving = cur != null && cur.ContractStatus == 2
                    && InWindow(cur.ActCheckinDate, wStart, wEnd);
                if (movedToLiving) notes.Add("Đã check in");

                AddOccChangeNote(notes, s.Occupy, cur?.Occupy);
                row.Note = string.Join("; ", notes);
                rows.Add(row);
            }

            // 2) Booking mới nhận sau 16:00 cho ngày hôm nay (status=1, PlanCheckinDate = hôm nay)
            foreach (var cur in current.Values
                         .Where(c => c.ContractStatus == 1
                                     && c.PlanCheckinDate >= todayStart && c.PlanCheckinDate < todayEnd
                                     && !seen.Contains(c.ContractID))
                         .OrderBy(c => c.CurrentApartmentNo))
            {
                var row = MorningRowFromCurrent(cur);
                row.Occ16 = null;
                row.Note = "Mới";
                rows.Add(row);
            }

            return rows;
        }

        private List<MorningRow> BuildMorningCheckOut(
            Dictionary<long, SnapshotRow> snapCheckOut, Dictionary<long, ContractRow> current,
            DateTime wStart, DateTime wEnd, DateTime todayStart, DateTime todayEnd)
        {
            var rows = new List<MorningRow>();
            var seen = new HashSet<long>();

            // 1) Từ danh sách Check Out lúc 16:00
            foreach (var s in snapCheckOut.Values.OrderBy(x => x.CurrentApartmentNo))
            {
                seen.Add(s.ContractID);
                current.TryGetValue(s.ContractID, out var cur);
                var row = MorningRowFromSnapshot(s, cur);
                var notes = new List<string>();

                bool departed = cur != null && cur.ContractStatus == 3
                    && InWindow(cur.ActCheckoutDate, wStart, wEnd);
                if (departed) notes.Add("Đã ra");

                AddOccChangeNote(notes, s.Occupy, cur?.Occupy);
                row.Note = string.Join("; ", notes);
                rows.Add(row);
            }

            // 2) Khách đã check out trong W nhưng chưa có trong snapshot Check Out
            foreach (var cur in current.Values
                         .Where(c => c.ContractStatus == 3 && InWindow(c.ActCheckoutDate, wStart, wEnd)
                                     && !seen.Contains(c.ContractID))
                         .OrderBy(c => c.CurrentApartmentNo))
            {
                var row = MorningRowFromCurrent(cur);
                row.Occ16 = null;
                row.Note = "Đã ra (mới)";
                rows.Add(row);
            }

            return rows;
        }

        private static bool InWindow(DateTime? d, DateTime start, DateTime end)
            => d.HasValue && d.Value >= start && d.Value <= end;

        private static void AddOccChangeNote(List<string> notes, int? oldOcc, int? newOcc)
        {
            if (oldOcc.HasValue && newOcc.HasValue && oldOcc.Value != newOcc.Value)
                notes.Add($"Đổi số người: {oldOcc.Value}→{newOcc.Value}");
        }

        private static MorningRow MorningRowFromSnapshot(SnapshotRow s, ContractRow? cur) => new MorningRow
        {
            ApartmentNo = s.CurrentApartmentNo,
            CustomerName = s.CustomerName,
            Occ16 = s.Occupy,
            Occ0735 = cur?.Occupy ?? s.Occupy,
            PlanCheckinDate = s.PlanCheckinDate,
            PlanCheckoutDate = s.PlanCheckoutDate,
            PaymentByName = s.PaymentByName
        };

        private static MorningRow MorningRowFromCurrent(ContractRow c) => new MorningRow
        {
            ApartmentNo = c.CurrentApartmentNo,
            CustomerName = c.CustomerName,
            Occ16 = c.Occupy,
            Occ0735 = c.Occupy,
            PlanCheckinDate = c.PlanCheckinDate,
            PlanCheckoutDate = c.PlanCheckoutDate,
            PaymentByName = c.PaymentByName
        };

        // ---------- Lưu snapshot ----------

        private async Task SaveSnapshot(SqlConnection conn, DateTime snapshotDate,
            List<ContractRow> living, List<ContractRow> checkIn, List<ContractRow> checkOut)
        {
            await conn.ExecuteAsync(
                "DELETE FROM dbo.CM_ContractSTSnapshot WHERE SnapshotDate = @D;",
                new { D = snapshotDate });

            const string insert = @"
                INSERT INTO dbo.CM_ContractSTSnapshot
                    (SnapshotDate, Section, ContractID, CurrentApartmentNo, CustomerName,
                     Occupy, PlanCheckinDate, PlanCheckoutDate, PaymentByName, ContractStatus)
                VALUES
                    (@SnapshotDate, @Section, @ContractID, @CurrentApartmentNo, @CustomerName,
                     @Occupy, @PlanCheckinDate, @PlanCheckoutDate, @PaymentByName, @ContractStatus);";

            var all = living.Select(r => ToSnapshotParam(snapshotDate, SEC_LIVING, r))
                .Concat(checkOut.Select(r => ToSnapshotParam(snapshotDate, SEC_CHECKOUT, r)))
                .Concat(checkIn.Select(r => ToSnapshotParam(snapshotDate, SEC_CHECKIN, r)))
                .ToList();

            if (all.Count > 0)
                await conn.ExecuteAsync(insert, all);
        }

        private static object ToSnapshotParam(DateTime snapshotDate, int section, ContractRow r) => new
        {
            SnapshotDate = snapshotDate,
            Section = section,
            r.ContractID,
            r.CurrentApartmentNo,
            r.CustomerName,
            r.Occupy,
            r.PlanCheckinDate,
            r.PlanCheckoutDate,
            r.PaymentByName,
            r.ContractStatus
        };

        // ---------- Excel 16:00 (gọn, 1 cột Occ) ----------

        private byte[] BuildAfternoonExcel(DateTime date, List<ContractRow> living,
            List<ContractRow> checkIn, List<ContractRow> checkOut)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Apartment Status");
            const int lastCol = 7;

            WriteTitle(ws, lastCol, date, "16:00");

            int row = 4;
            row = WriteAfternoonSection(ws, row, "1. Short-term Living", living, lastCol);
            row += 1;
            row = WriteAfternoonSection(ws, row, "2. Short-term Will be checked Out", checkOut, lastCol);
            row += 1;
            WriteAfternoonSection(ws, row, "3. Short-term Will be checked In", checkIn, lastCol);

            ws.Columns().AdjustToContents();
            return ToBytes(wb);
        }

        private int WriteAfternoonSection(IXLWorksheet ws, int startRow, string title,
            List<ContractRow> rows, int lastCol)
        {
            int row = WriteSectionTitle(ws, startRow, title, lastCol);
            string[] headers = { "Order", "Apartment", "Customer Name", "Occ", "CheckIn Plan", "Check Out Plan", "Payment By" };
            WriteHeader(ws, row, headers);
            row++;

            if (rows.Count == 0) return WriteNoData(ws, row, lastCol);

            int order = 1;
            foreach (var r in rows)
            {
                ws.Cell(row, 1).Value = order;
                ws.Cell(row, 2).Value = r.CurrentApartmentNo;
                ws.Cell(row, 3).Value = r.CustomerName;
                ws.Cell(row, 4).Value = r.Occupy;
                SetDate(ws, row, 5, r.PlanCheckinDate);
                SetDate(ws, row, 6, r.PlanCheckoutDate);
                ws.Cell(row, 7).Value = r.PaymentByName;
                StyleDataRow(ws, row, lastCol, new[] { 3, 7 });
                order++;
                row++;
            }
            return row;
        }

        // ---------- Excel 07:35 (2 cột Occ + cột Ghi chú) ----------

        private byte[] BuildMorningExcel(DateTime morningDate, DateTime snapshotDate,
            List<MorningRow> living, List<MorningRow> checkIn, List<MorningRow> checkOut)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Apartment Status");
            const int lastCol = 9;

            WriteTitle(ws, lastCol, morningDate, "07:35");
            ws.Cell(3, 1).Value = $"Compared with 16:00 snapshot of {snapshotDate:dd/MM/yyyy}";
            ws.Range(3, 1, 3, lastCol).Merge();
            ws.Cell(3, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(3, 1).Style.Font.Italic = true;
            ws.Cell(3, 1).Style.Font.FontColor = XLColor.Gray;

            int row = 5;
            row = WriteMorningSection(ws, row, "1. Short-term Living", living, lastCol);
            row += 1;
            row = WriteMorningSection(ws, row, "2. Short-term Will be checked Out", checkOut, lastCol);
            row += 1;
            WriteMorningSection(ws, row, "3. Short-term Will be checked In", checkIn, lastCol);

            ws.Columns().AdjustToContents();
            return ToBytes(wb);
        }

        private int WriteMorningSection(IXLWorksheet ws, int startRow, string title,
            List<MorningRow> rows, int lastCol)
        {
            int row = WriteSectionTitle(ws, startRow, title, lastCol);
            string[] headers = { "Order", "Apartment", "Customer Name", "Occ (16:00)", "Occ (07:35)",
                                 "CheckIn Plan", "Check Out Plan", "Payment By", "Note" };
            WriteHeader(ws, row, headers);
            row++;

            if (rows.Count == 0) return WriteNoData(ws, row, lastCol);

            int order = 1;
            foreach (var r in rows)
            {
                ws.Cell(row, 1).Value = order;
                ws.Cell(row, 2).Value = r.ApartmentNo;
                ws.Cell(row, 3).Value = r.CustomerName;
                ws.Cell(row, 4).Value = r.Occ16;
                ws.Cell(row, 5).Value = r.Occ0735;
                SetDate(ws, row, 6, r.PlanCheckinDate);
                SetDate(ws, row, 7, r.PlanCheckoutDate);
                ws.Cell(row, 8).Value = r.PaymentByName;
                ws.Cell(row, 9).Value = r.Note;
                StyleDataRow(ws, row, lastCol, new[] { 3, 8, 9 });

                // Tô màu dòng có thay đổi cho dễ nhận biết
                if (!string.IsNullOrEmpty(r.Note))
                {
                    var fill = r.Note.Contains("Đã ra") ? XLColor.FromHtml("#fdecea")               // đỏ nhạt
                             : (r.Note.Contains("Mới") || r.Note.Contains("Sudden")) ? XLColor.FromHtml("#eafaf1") // xanh nhạt (mới vào / sudden)
                             : XLColor.FromHtml("#fef9e7");                                          // vàng nhạt (đổi số người)
                    ws.Range(row, 1, row, lastCol).Style.Fill.BackgroundColor = fill;
                }
                order++;
                row++;
            }
            return row;
        }

        // ---------- Excel helpers ----------

        private void WriteTitle(IXLWorksheet ws, int lastCol, DateTime date, string timeTag)
        {
            ws.Cell(1, 1).Value = "APARTMENT STATUS REPORT";
            ws.Range(1, 1, 1, lastCol).Merge();
            ws.Cell(1, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Cell(1, 1).Style.Font.FontSize = 20;
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontColor = XLColor.Blue;
            ws.Row(1).Height = 36;

            ws.Cell(2, 1).Value = $"Housekeeping - {date:dd/MM/yyyy} {timeTag}";
            ws.Range(2, 1, 2, lastCol).Merge();
            ws.Cell(2, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(2, 1).Style.Font.Italic = true;
            ws.Cell(2, 1).Style.Font.FontColor = XLColor.Gray;
        }

        private int WriteSectionTitle(IXLWorksheet ws, int row, string title, int lastCol)
        {
            ws.Cell(row, 1).Value = title;
            ws.Range(row, 1, row, lastCol).Merge();
            ws.Cell(row, 1).Style.Font.FontSize = 13;
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Font.FontColor = XLColor.White;
            ws.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#16a085");
            return row + 1;
        }

        private void WriteHeader(IXLWorksheet ws, int row, string[] headers)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                var cell = ws.Cell(row, i + 1);
                cell.Value = headers[i];
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
                cell.Style.Font.Bold = true;
                cell.Style.Font.FontColor = XLColor.Blue;
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#eaf2f8");
                cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            }
        }

        private int WriteNoData(IXLWorksheet ws, int row, int lastCol)
        {
            ws.Cell(row, 1).Value = "No data";
            ws.Range(row, 1, row, lastCol).Merge();
            ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(row, 1).Style.Font.Italic = true;
            ws.Cell(row, 1).Style.Font.FontColor = XLColor.Gray;
            return row + 1;
        }

        private void SetDate(IXLWorksheet ws, int row, int col, DateTime? d)
        {
            if (d.HasValue)
            {
                ws.Cell(row, col).Value = d.Value;
                ws.Cell(row, col).Style.NumberFormat.Format = "dd/MM/yyyy";
            }
        }

        private void StyleDataRow(IXLWorksheet ws, int row, int lastCol, int[] leftAlignCols)
        {
            for (int i = 1; i <= lastCol; i++)
            {
                ws.Cell(row, i).Style.Font.FontSize = 11;
                ws.Cell(row, i).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }
            foreach (var c in leftAlignCols)
                ws.Cell(row, c).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Left;
        }

        private static byte[] ToBytes(XLWorkbook wb)
        {
            using var ms = new MemoryStream();
            wb.SaveAs(ms);
            return ms.ToArray();
        }

        // ---------- Email ----------

        private async Task SendEmail(string htmlBody, byte[] excelFile, string subject, string fileName)
        {
            string senderEmail = _config.GetValue<string>("EmailSettings:SenderEmail");
            string mailPass = _config.GetValue<string>("EmailSettings:Password");
            string mailServer = _config.GetValue<string>("EmailSettings:MailServer");
            int mailPort = _config.GetValue<int>("EmailSettings:MailPort");

            try
            {
                var mail = new MailMessage
                {
                    From = new MailAddress(senderEmail, "New Smartsam System"),
                    Subject = subject,
                    Body = htmlBody,
                    IsBodyHtml = true
                };

                mail.To.Add("hung.dt@saigonskygarden.com.vn");
                mail.To.Add("khanh.dm@saigonskygarden.com.vn");
                mail.To.Add("ngan.vk@saigonskygarden.com.vn");
                mail.CC.Add("hai.dq@saigonskygarden.com.vn");

                if (excelFile != null && excelFile.Length > 0)
                {
                    var ms = new MemoryStream(excelFile);
                    mail.Attachments.Add(new Attachment(ms, fileName,
                        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"));
                }

                using var smtp = new SmtpClient(mailServer, mailPort);
                smtp.EnableSsl = true;
                smtp.UseDefaultCredentials = false;
                smtp.Credentials = new NetworkCredential(senderEmail, mailPass);
                await smtp.SendMailAsync(mail);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HousekeepingStatusReport] Lỗi gửi mail: {ex.Message}");
                throw;
            }
        }

        // ---------- Row models ----------

        public class ContractRow
        {
            public long ContractID { get; set; }
            public string CurrentApartmentNo { get; set; } = string.Empty;
            public string CustomerName { get; set; } = string.Empty;
            public int? Occupy { get; set; }
            public DateTime? PlanCheckinDate { get; set; }
            public DateTime? PlanCheckoutDate { get; set; }
            public string PaymentByName { get; set; } = string.Empty;
            public int? ContractStatus { get; set; }
            public bool IsSTWTR { get; set; }
            public DateTime? ActCheckinDate { get; set; }
            public DateTime? ActCheckoutDate { get; set; }
        }

        public class SnapshotRow
        {
            public DateTime SnapshotDate { get; set; }
            public int Section { get; set; }
            public long ContractID { get; set; }
            public string CurrentApartmentNo { get; set; } = string.Empty;
            public string CustomerName { get; set; } = string.Empty;
            public int? Occupy { get; set; }
            public DateTime? PlanCheckinDate { get; set; }
            public DateTime? PlanCheckoutDate { get; set; }
            public string PaymentByName { get; set; } = string.Empty;
            public int? ContractStatus { get; set; }
        }

        public class MorningRow
        {
            public string ApartmentNo { get; set; } = string.Empty;
            public string CustomerName { get; set; } = string.Empty;
            public int? Occ16 { get; set; }
            public int? Occ0735 { get; set; }
            public DateTime? PlanCheckinDate { get; set; }
            public DateTime? PlanCheckoutDate { get; set; }
            public string PaymentByName { get; set; } = string.Empty;
            public string Note { get; set; } = string.Empty;
        }
    }
}
