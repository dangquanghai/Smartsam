using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Http;
using System.Data.SqlClient;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SmartSam.Services;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Net.Mail;
using System.Net;

namespace SmartSam.Pages.Cus
{
    public class MeterCheckModel : PageModel
    {
        private readonly IConfiguration _config;
        private readonly IWebHostEnvironment _env;
        private readonly IHttpClientFactory _httpClientFactory;

        public MeterCheckModel(IConfiguration config, IWebHostEnvironment env, IHttpClientFactory httpClientFactory)
        {
            _config = config;
            _env = env;
            _httpClientFactory = httpClientFactory;
        }

        [BindProperty]
        public string AccessMessage { get; set; }


        // dữ liệu hiển thị
        public List<MeterRecord> Records { get; set; } = new();

        public int TheMonth;
        public int TheYear;

        private string CurrentUserCode =>
            User?.FindFirst("EmployeeCode")?.Value
            ?? User?.Identity?.Name
            ?? "unknown";

        public IActionResult OnGet()
        {
            if (!CanReconize())
            {
                AccessMessage = "Bạn không có quyền nhận dạng hoặc cập nhật dữ liệu.";
            }

            LoadData();
            return Page();
        }

        // -------------------------------
        // AJAX handler: Recognize selected ids
        // -------------------------------
        public async Task<IActionResult> OnPostRecognizeAjax([FromBody] List<int> ids)
        {
            if (!CanReconize())
            {
                return new JsonResult(new { success = false, message = "Bạn không có quyền nhận dạng." })
                {
                    StatusCode = StatusCodes.Status401Unauthorized
                };
            }

            string apiKey = _config.GetValue<string>("GoogleVisionApiKey:ApiKey");
            if (string.IsNullOrEmpty(apiKey))
                return BadRequest(new { success = false, message = "Missing Google Vision API Key." });

            LoadData();
            var targetRecords = Records.Where(r => ids.Contains(r.Id)).ToList();

            // process in batches to avoid huge requests
            const int batchSize = 16;
            var updatedRecords = new List<MeterRecord>();

            for (int i = 0; i < targetRecords.Count; i += batchSize)
            {
                var batch = targetRecords.Skip(i).Take(batchSize).ToList();
                var base64List = new List<string>();
                var batchRecordsWithPaths = new List<(MeterRecord rec, string path)>();

                foreach (var r in batch)
                {
                    if (string.IsNullOrWhiteSpace(r.FileName)) continue;

                    string fullPath = Path.Combine(_env.WebRootPath, "uploads", $"{TheYear}-{TheMonth:D2}", r.FileName);

                    if (!System.IO.File.Exists(fullPath))
                    {
                        Console.WriteLine("File not found: " + fullPath);
                        continue;
                    }


                    if (System.IO.File.Exists(fullPath))
                    {
                        // load, auto-orient, crop, convert to jpeg bytes
                        using var img = Image.Load(fullPath);

                        img.Mutate(x => x.AutoOrient());
                        int cropHeight = (int)(img.Height * 0.75);
                        img.Mutate(x => x.Crop(new Rectangle(0, 0, img.Width, cropHeight)));

                        using var ms = new MemoryStream();
                        await img.SaveAsJpegAsync(ms);
                        var bytes = ms.ToArray();
                        base64List.Add(Convert.ToBase64String(bytes));
                        batchRecordsWithPaths.Add((r, fullPath));
                    }
                    else { return new JsonResult(new { success = false, message = "File không tồn tại." }); }

                }

                // call google in batch
                var ocrResults = await CallGoogleVisionBatch(base64List, apiKey);

                // map results back to batch records (assume same order)
                for (int j = 0; j < ocrResults.Count && j < batchRecordsWithPaths.Count; j++)
                {
                    var rec = batchRecordsWithPaths[j].rec;
                    var text = ocrResults[j] ?? "";
                    rec.RawText = text;

                    var (apartment, electricIndex) = ExtractApartmentAndIndex(text);
                    rec.ApartmentCode = apartment;
                    rec.ElectricIndex = electricIndex;

                    updatedRecords.Add(rec);
                }

                // small delay to be gentle with API
                await Task.Delay(200);
            }

            // persist results
            if (updatedRecords.Any())
            {
                SaveRecords(updatedRecords);
            }

            return new JsonResult(new { success = true, message = "Đã nhận dạng xong.", records = updatedRecords });
        }

        // -------------------------------
        // AJAX handler: Update all records (client sends updated objects)
        // -------------------------------
        public IActionResult OnPostUpdateAllAjax([FromBody] List<UpdateRecordDto> records)
        {
            if (!CanConfirm())
                return Unauthorized();

            string connStr = _config.GetConnectionString("DefaultConnection");
            string uploadsFolder = Path.Combine(_env.WebRootPath!, "uploads");
            (int month, int year) = GeneralServices.GetDefaultMonthYear();
            string monthFolder = Path.Combine(uploadsFolder, $"{year}-{month:00}");

            using var conn = new SqlConnection(connStr);
            conn.Open();

            foreach (var r in records)
            {
                // --- ONLY RENAME WHEN ApartmentCode HAS VALUE ---
                if (!string.IsNullOrEmpty(r.ApartmentCode) && !string.IsNullOrEmpty(r.FileName))
                {
                    string ext = Path.GetExtension(r.FileName);

                    // Tên file đúng theo mã căn hộ mong muốn
                    string expectedFileName = $"{r.ApartmentCode}{ext}";
                    string oldFilePath = Path.Combine(monthFolder, r.FileName);
                    string newFilePath = Path.Combine(monthFolder, expectedFileName);

                    // Nếu file đã đúng tên → KHÔNG rename, KHÔNG đụng gì vào file
                    if (!r.FileName.Equals(expectedFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            // Chỉ move nếu file gốc tồn tại
                            if (System.IO.File.Exists(oldFilePath))
                            {
                                // Không bao giờ xóa newFilePath (tránh mất file)
                                // Nếu newFilePath tồn tại → move sẽ thất bại, nhưng ta bỏ qua

                                System.IO.File.Move(oldFilePath, newFilePath, overwrite: true);
                            }

                            // Cập nhật tên mới vào object để lưu DB
                            r.FileName = expectedFileName;
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Rename error {r.FileName} → {expectedFileName}: {ex.Message}");
                        }
                    }
                    // Ngược lại: file đã đúng tên → giữ nguyên r.FileName
                }

                // ----- UPDATE DATABASE -----
                string sql = @"UPDATE PW_MeterReading
                SET ApartmentCode=@ApartmentCode,
                    ElectricIndex=@ElectricIndex,
                    RawText=@RawText,
                    FileName=@FileName
                WHERE Id=@Id";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@ApartmentCode", string.IsNullOrEmpty(r.ApartmentCode) ? DBNull.Value : r.ApartmentCode);
                cmd.Parameters.AddWithValue("@ElectricIndex", r.ElectricIndex ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@RawText", string.IsNullOrEmpty(r.RawText) ? DBNull.Value : r.RawText);
                cmd.Parameters.AddWithValue("@FileName", r.FileName ?? (object)DBNull.Value);
                cmd.Parameters.AddWithValue("@Id", r.Id);
                cmd.ExecuteNonQuery();
            }

            return new JsonResult(new { success = true, message = $"Đã cập nhật {records.Count} bản ghi." });
        }






        // -------------------------------
        // Helpers: Save to DB
        // -------------------------------
        private void SaveRecords(List<MeterRecord> records)
        {
            if (records == null || records.Count == 0) return;

            string connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            conn.Open();

            foreach (var r in records)
            {
                string sql = @"UPDATE PW_MeterReading
                               SET ApartmentCode=@ApartmentCode, ElectricIndex=@ElectricIndex, RawText=@RawText
                               WHERE Id=@Id";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@ApartmentCode", string.IsNullOrEmpty(r.ApartmentCode) ? DBNull.Value : r.ApartmentCode);
                cmd.Parameters.AddWithValue("@ElectricIndex", r.ElectricIndex.HasValue ? r.ElectricIndex.Value : DBNull.Value);
                cmd.Parameters.AddWithValue("@RawText", string.IsNullOrEmpty(r.RawText) ? DBNull.Value : r.RawText);
                cmd.Parameters.AddWithValue("@Id", r.Id);
                cmd.ExecuteNonQuery();
            }
        }

        // -------------------------------
        // LoadData: lấy toàn bộ record cho tháng/năm mặc định
        // -------------------------------
        private void LoadData()
        {
            (TheMonth, TheYear) = GeneralServices.GetDefaultMonthYear();

            string connStr = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connStr);
            conn.Open();

            string sql = @"
                SELECT r.Id, r.FileName, r.ApartmentCode, r.ElectricIndex, r.RawText, r.UserCode, r.CreatedAt
                FROM PW_MeterReading r left join AM_Apmt a on r.ApartmentCode = a.ApartmentNo 
                WHERE r.TheYear = @TheYear AND r.TheMonth = @TheMonth
                order by a.FloorNo , a.BlockNo ";
            //ORDER BY r.Id";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@TheYear", TheYear);
            cmd.Parameters.AddWithValue("@TheMonth", TheMonth);

            using var reader = cmd.ExecuteReader();
            Records.Clear();
            while (reader.Read())
            {
                Records.Add(new MeterRecord
                {
                    Id = (int)reader.GetInt64(0),
                    FileName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    ApartmentCode = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    ElectricIndex = reader.IsDBNull(3) ? null : reader.GetInt64(3),
                    RawText = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    UserCode = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    CreatedAt = reader.IsDBNull(6) ? DateTime.MinValue : reader.GetDateTime(6)
                });
            }
        }

        // -------------------------------
        // Google Vision batch call (kept from original)
        // -------------------------------
        public async Task<List<string>> CallGoogleVisionBatch(List<string> base64Images, string apiKey)
        {

            var requests = new List<object>();

            foreach (var base64 in base64Images)
            {
                requests.Add(new
                {
                    image = new { content = base64 },
                    features = new[] { new { type = "DOCUMENT_TEXT_DETECTION" } }
                });
            }

            var payload = new { requests };
            var json = JsonSerializer.Serialize(payload);

            // sử dụng IHttpClientFactory
            var client = _httpClientFactory.CreateClient();
            var url = $"https://vision.googleapis.com/v1/images:annotate?key={apiKey}";
            using var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(url, content);
            var responseJson = await response.Content.ReadAsStringAsync();

            var results = new List<string>();

            using var doc = JsonDocument.Parse(responseJson);
            if (doc.RootElement.TryGetProperty("responses", out var responses))
            {
                foreach (var item in responses.EnumerateArray())
                {
                    if (item.TryGetProperty("fullTextAnnotation", out var textNode) &&
                        textNode.TryGetProperty("text", out var textProp))
                    {
                        results.Add(textProp.GetString() ?? "");
                    }
                    else
                    {
                        results.Add("");
                    }
                }
            }

            return results;
        }

        // -------------------------------
        // Extract apartment and index 
        // -------------------------------
        private (string Apartment, long? ElectricIndex) ExtractApartmentAndIndex(string ocrText)
        {
            if (string.IsNullOrWhiteSpace(ocrText))
                return (string.Empty, null);

            string apartment = "";
            long? electricIndex = null;

            var lines = ocrText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i].Trim();

                //var match = Regex.Match(ln, @"#\s*([A-Za-z0-9\-.]+)\s*#?|([A-Za-z0-9\-.]+)\s*#", RegexOptions.IgnoreCase);

                // chỉ cần lấy chuỗi bên trong 2 ký tự # và loại bỏ hết khoảng trắng
                var match = Regex.Match(ln, @"#\s*(.*?)\s*#");


                if (match.Success)
                {
                    apartment = !string.IsNullOrEmpty(match.Groups[1].Value) ? match.Groups[1].Value.Trim()
                                                                             : match.Groups[2].Value.Trim();

                    // tìm giá trị chỉ số ở các dòng tiếp theo trong 1..3 dòng
                    for (int j = 1; j <= 3; j++)
                    {
                        int nextLineIndex = i + j;
                        if (nextLineIndex >= lines.Length) break;

                        string raw = lines[nextLineIndex];
                        string digits = new string(raw.Where(char.IsDigit).ToArray());
                        digits = digits.TrimStart('0');

                        if (!string.IsNullOrEmpty(digits) && long.TryParse(digits, out long val))
                        {
                            electricIndex = val;
                            return (apartment, electricIndex);
                        }
                    }

                    // nếu không tìm thấy chỉ số thì trả apartment với null index
                    return (apartment, electricIndex);
                }
            }

            // fallback: cố gắng tìm cặp số lớn nhất trong toàn text (nếu không có apartment)
            var allDigits = Regex.Matches(ocrText, @"\d{3,}");
            if (allDigits.Count > 0)
            {
                var biggest = allDigits.Select(m => m.Value).OrderByDescending(s => s.Length).FirstOrDefault();
                if (long.TryParse(biggest, out long v))
                {
                    electricIndex = v;
                }
            }

            return (apartment, electricIndex);
        }

        public record EmailSendRequest(string Email, string FileName, string Apartment);

        public async Task<IActionResult> OnPostSendEmail([FromBody] EmailSendRequest req)
        {
            if (string.IsNullOrEmpty(req.Email))
                return new JsonResult(new { success = false, message = "Email is required" });

            (int month, int year) = GeneralServices.GetDefaultMonthYear();

            string folder = Path.Combine(_env.WebRootPath, "uploads", $"{year}-{month:00}");
            string filePath = Path.Combine(folder, req.FileName);

            if (!System.IO.File.Exists(filePath))
                return new JsonResult(new { success = false, message = "Attachment file not found" });

            string body = $@"
            Dear Sir/Madam,

            This is the end-of-month meter reading.
            Month: {month}
            Year: {year}

            Apartment: {req.Apartment}

            Best regards.
            TD Department
            Saigon Sky Garden";

            try
            {
                using var mail = new MailMessage
                {
                    From = new MailAddress("system@saigonskygarden.com.vn"),
                    Subject = $"Meter Reading {req.Apartment} - {month}/{year}",
                    Body = body
                };

                mail.To.Add(req.Email);
                mail.CC.Add("trung.tk@saigonskygarden.com.vn");

                // Attach file safely
                using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var attachment = new Attachment(stream, Path.GetFileName(filePath));
                    mail.Attachments.Add(attachment);

                    using var smtp = new SmtpClient("smtp.gmail.com", 587)
                    {
                        EnableSsl = true,
                        UseDefaultCredentials = false,
                        Credentials = new NetworkCredential("system@saigonskygarden.com.vn", "eevl nwql jdhb lbsx"),
                        DeliveryMethod = SmtpDeliveryMethod.Network
                    };

                    await smtp.SendMailAsync(mail);
                }

                return new JsonResult(new { success = true });
            }
            catch (SmtpException smtpEx)
            {
                return new JsonResult(new { success = false, message = $"SMTP Error: {smtpEx.Message}" });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = $"General Error: {ex.Message}" });
            }
        }




        // -------------------------------
        // Permissions
        // -------------------------------
        private bool IsAllowedUser() => new[] { "ADMIN", "TD055", "TD019" }.Contains(CurrentUserCode.ToUpper());
        private bool CanReconize() => new[] { "ADMIN", "TD019" }.Contains(CurrentUserCode.ToUpper());
        private bool CanConfirm() => new[] { "ADMIN", "TD019", "TD055" }.Contains(CurrentUserCode.ToUpper());

        // -------------------------------
        // Model
        // -------------------------------
        public class MeterRecord
        {
            public int Id { get; set; }
            public string FileName { get; set; } = "";
            public string ApartmentCode { get; set; } = "";
            public long? ElectricIndex { get; set; } = null;
            public string RawText { get; set; } = "";
            public string UserCode { get; set; } = "";
            public DateTime CreatedAt { get; set; }
        }
        public class UpdateRecordDto
        {
            public int Id { get; set; }
            public string ApartmentCode { get; set; } = "";
            public long? ElectricIndex { get; set; }
            public string? FileName { get; set; }
            public string RawText { get; set; } = "";
        }
    }
}
