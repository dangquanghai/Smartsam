using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp;
using SmartSam.Services;
using System.Data.SqlClient;

namespace SmartSam.Pages.Cus
{
    public class MeterUploadModel : PageModel
    {
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;

        public MeterUploadModel(IWebHostEnvironment env, IConfiguration config)
        {
            _env = env;
            _config = config;
        }

        // public int TheMonth { get; set; }
        // public int TheYear { get; set; }

        public static int TheMonth;
        public static int TheYear;

        private string CurrentUserCode =>
            User?.FindFirst("EmployeeCode")?.Value
            ?? User?.Identity?.Name
            ?? "unknown";

        public void OnGet()
        {
            (TheMonth, TheYear) = GeneralServices.GetDefaultMonthYear();
        }

        //       public async Task<IActionResult> OnPostUploadSingleAsync(IFormFile file)
        public async Task<IActionResult> OnPostUploadSingleAsync([FromForm(Name = "uploadImage")] IFormFile file)
        {
            if (file == null)
                return BadRequest("File not received");

            string folderName = $"{TheYear}-{TheMonth:00}";
            string uploadDir = Path.Combine(_env.WebRootPath!, "uploads", folderName);
            if (!Directory.Exists(uploadDir))
                Directory.CreateDirectory(uploadDir);

            string fileName = $"{Guid.NewGuid()}_{Path.GetFileName(file.FileName)}";
            string savedPath = Path.Combine(uploadDir, fileName);

            using (var img = Image.Load(file.OpenReadStream()))
            {
                if (img.Width > 512) img.Mutate(x => x.Resize(512, 0));
                img.Mutate(x => x.Grayscale());
                img.Mutate(x => x.AutoOrient());
                int cropHeight = (int)(img.Height * 2 / 3.0);
                img.Mutate(x => x.Crop(new Rectangle(0, 0, img.Width, cropHeight)));
                var encoder = new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 75 };
                await img.SaveAsJpegAsync(savedPath, encoder);
            }

            // --- Lưu thông tin vào DB ---
            string connStr = _config.GetConnectionString("DefaultConnection");
            using (var conn = new SqlConnection(connStr))
            {
                await conn.OpenAsync();

                string sql = @"INSERT INTO PW_MeterReading
                       (FileName, TheMonth, TheYear, UserCode, CreatedAt, IsReconize)
                       VALUES (@FileName, @TheMonth, @TheYear, @UserCode, GETDATE(), 0)";

                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@FileName", Path.Combine(fileName).Replace("\\", "/"));
                cmd.Parameters.AddWithValue("@TheMonth", TheMonth);
                cmd.Parameters.AddWithValue("@TheYear", TheYear);
                cmd.Parameters.AddWithValue("@UserCode", CurrentUserCode);

                await cmd.ExecuteNonQueryAsync();
            }

            return new JsonResult(new { fileName });
        }

    }
}
