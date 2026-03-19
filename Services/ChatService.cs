// === File: Services/ChatService.cs ===
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Smartsam.Services
{
    public class ChatResult
    {
        public string Answer { get; set; }
        public ChartInfo Chart { get; set; }
        public List<ChartPoint> ChartSeries { get; set; }
    }

    public class ChartInfo
    {
        public string Label1 { get; set; }
        public string Label2 { get; set; }
        public decimal Value1 { get; set; }
        public decimal Value2 { get; set; }
    }

    public class ChartPoint
    {
        public string Month { get; set; }
        public decimal Value { get; set; }
    }

    public class ChatService
    {
        private readonly IConfiguration _config;
        private readonly string _connectionString;

        public ChatService(IConfiguration config)
        {
            _config = config;
            _connectionString = _config.GetConnectionString("DefaultConnection");
        }

        public async Task<ChatResult> ProcessQuestionAsync(string question)
        {
            try
            {
                question = question.ToLower();

                string language = "unknown";
                bool isEnglish = Regex.IsMatch(question, @"\bfrom\b|\bto\b|\brevenue\b|\byear\b");
                bool isVietnamese = Regex.IsMatch(question, @"\btừ\b|\btháng\b|\bnăm\b|\bdoanh thu\b");

                if (isEnglish) language = "en";
                else if (isVietnamese) language = "vi";

                var monthsMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
                {
                    { "jan", 1 }, { "january", 1 },
                    { "feb", 2 }, { "february", 2 },
                    { "mar", 3 }, { "march", 3 },
                    { "apr", 4 }, { "april", 4 },
                    { "may", 5 },
                    { "jun", 6 }, { "june", 6 },
                    { "jul", 7 }, { "july", 7 },
                    { "aug", 8 }, { "august", 8 },
                    { "sep", 9 }, { "september", 9 },
                    { "oct", 10 }, { "october", 10 },
                    { "nov", 11 }, { "november", 11 },
                    { "dec", 12 }, { "december", 12 },
                };

                // Case 3: From month to month or from beginning of year to now
                var rangeMatch = Regex.Match(question, @"từ tháng (\d{1,2}) đến tháng (\d{1,2}) năm (\d{4})");
                if (rangeMatch.Success)
                {
                    int start = int.Parse(rangeMatch.Groups[1].Value);
                    int end = int.Parse(rangeMatch.Groups[2].Value);
                    string year = rangeMatch.Groups[3].Value;

                    List<ChartPoint> series = new();
                    decimal total = 0;

                    for (int m = start; m <= end; m++)
                    {
                        var rev = await GetRevenueAsync(m.ToString("D2"), year);
                        if (rev != null)
                        {
                            var (lt, st) = rev.Value;
                            decimal sum = lt + st;
                            total += sum;
                            series.Add(new ChartPoint { Month = $"{m:D2}/{year}", Value = sum });
                        }
                    }

                    return new ChatResult
                    {
                        Answer = $"✅ Tổng doanh thu từ tháng {start} đến tháng {end} năm {year}: {total:N0} VND.",
                        ChartSeries = series
                    };
                }

                var fullYearMatch = Regex.Match(question, @"từ đầu năm đến nay|from the beginning of the year");
                if (fullYearMatch.Success)
                {
                    int currentMonth = DateTime.Now.Month;
                    int currentYear = DateTime.Now.Year;

                    List<ChartPoint> series = new();
                    decimal total = 0;

                    for (int m = 1; m <= currentMonth; m++)
                    {
                        var rev = await GetRevenueAsync(m.ToString("D2"), currentYear.ToString());
                        if (rev != null)
                        {
                            var (lt, st) = rev.Value;
                            decimal sum = lt + st;
                            total += sum;
                            series.Add(new ChartPoint { Month = $"{m:D2}/{currentYear}", Value = sum });
                        }
                    }

                    return new ChatResult
                    {
                        Answer = $"✅ Doanh thu từ đầu năm đến nay ({series.Count} tháng): {total:N0} VND.",
                        ChartSeries = series
                    };
                }

                // Case 2: Compare 2 months same year
                var match = Regex.Match(question, @"tháng\s*(\d{1,2})\s*(với|và|vs|so với)\s*tháng\s*(\d{1,2})\s*năm\s*(\d{4})");
                if (match.Success)
                {
                    string month1 = int.Parse(match.Groups[1].Value).ToString("D2");
                    string month2 = int.Parse(match.Groups[3].Value).ToString("D2");
                    string yearStr = match.Groups[4].Value;

                    var rev1 = await GetRevenueAsync(month1, yearStr);
                    var rev2 = await GetRevenueAsync(month2, yearStr);

                    if (rev1 == null || rev2 == null)
                        return new ChatResult { Answer = "❌ Could not retrieve revenue data." };

                    var (lt1, st1) = rev1.Value;
                    var (lt2, st2) = rev2.Value;

                    string label1 = $"{month1}/{yearStr}";
                    string label2 = $"{month2}/{yearStr}";
                    decimal total1 = lt1 + st1;
                    decimal total2 = lt2 + st2;

                    string answer = "✅ So sánh doanh thu:" +
                                     $"\n- {label1}: {total1:N0} VND (LT: {lt1:N0}, ST: {st1:N0})" +
                                     $"\n- {label2}: {total2:N0} VND (LT: {lt2:N0}, ST: {st2:N0})";

                    return new ChatResult
                    {
                        Answer = answer,
                        Chart = new ChartInfo
                        {
                            Label1 = label1,
                            Label2 = label2,
                            Value1 = total1,
                            Value2 = total2
                        }
                    };
                }

                // Case 1: one month only
                var oneMonth = Regex.Match(question, @"tháng\s*(\d{1,2})\s*năm\s*(\d{4})");
                if (oneMonth.Success)
                {
                    string month = int.Parse(oneMonth.Groups[1].Value).ToString("D2");
                    string year = oneMonth.Groups[2].Value;

                    var rev = await GetRevenueAsync(month, year);
                    if (rev == null)
                        return new ChatResult { Answer = "❌ Không tìm được dữ liệu doanh thu." };

                    var (lt, st) = rev.Value;
                    decimal total = lt + st;

                    return new ChatResult
                    {
                        Answer = $"✅ Doanh thu tháng {month}/{year}: {total:N0} VND (LT: {lt:N0}, ST: {st:N0})"
                    };
                }

                return new ChatResult
                {
                    Answer = "❌ Không xác định được tháng và năm trong câu hỏi."
                };
            }
            catch (Exception ex)
            {
                return new ChatResult
                {
                    Answer = "❌ Lỗi: " + ex.Message
                };
            }
        }

        private async Task<(decimal, decimal)?> GetRevenueAsync(string month, string year)
        {
            string sql = $"EXEC sp_Revenue @Month = '{month}', @Year = '{year}', @AmptType = 'ALL'";
            using (var conn = new SqlConnection(_connectionString))
            using (var cmd = new SqlCommand(sql, conn))
            {
                await conn.OpenAsync();
                var reader = await cmd.ExecuteReaderAsync();
                if (await reader.ReadAsync())
                {
                    decimal ltVND = reader.IsDBNull("LTAmtVND") ? 0 : reader.GetDecimal(reader.GetOrdinal("LTAmtVND"));
                    decimal stVND = reader.IsDBNull("STAmtVND") ? 0 : reader.GetDecimal(reader.GetOrdinal("STAmtVND"));
                    return (ltVND, stVND);
                }
            }
            return null;
        }
    }
}
