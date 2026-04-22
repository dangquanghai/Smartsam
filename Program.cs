using Microsoft.AspNetCore.Authentication.Cookies;
using SmartSam.Pages;
using SmartSam.Services;// Kiểm tra namespace này cho chuẩn 
using Microsoft.AspNetCore.Http.Features;
using SmartSam.Helpers;

using Hangfire;
using Hangfire.SqlServer;
using QuestPDF.Drawing;
using QuestPDF.Infrastructure;


var builder = WebApplication.CreateBuilder(args);
QuestPDF.Settings.License = LicenseType.Community;

// --- ĐĂNG KÝ SERVICES ---
builder.Services.AddRazorPages();

// 1. THÊM DÒNG NÀY ĐỂ CHẠY ĐƯỢC API CONTROLLER (CHO SELECT2)
builder.Services.AddControllers();

builder.Services.AddHttpClient();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options => { options.LoginPath = "/Login"; });

builder.Services.AddAuthorization();
builder.Services.AddSingleton<IConfiguration>(builder.Configuration);
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession();

builder.Services.AddScoped<MenuService>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<SmartSam.Services.Interfaces.ISecurityService, SmartSam.Services.Implementations.SecurityService>();

// Đăng ký Service xử lý Review của bạn
builder.Services.AddScoped<SixMonthsStayReviewService>();



builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 800_000_000;
    options.MultipartHeadersLengthLimit = 800_000_000;
    options.ValueLengthLimit = 800_000_000;
});

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 800_000_000;
});

builder.Services.AddHangfire(configuration => configuration
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSqlServerStorage(builder.Configuration.GetConnectionString("DefaultConnection"), new SqlServerStorageOptions
    {
        CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
        SlidingInvisibilityTimeout = TimeSpan.FromMinutes(5),
        QueuePollInterval = TimeSpan.Zero,
        UseRecommendedIsolationLevel = true,
        DisableGlobalLocks = true
    }));

builder.Services.AddHangfireServer();

var app = builder.Build();

var pdfFontDirectory = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "fonts", "pdf");
Directory.CreateDirectory(pdfFontDirectory);
RegisterPdfFontIfExists("VNI-Times", "VNI-Times.ttf");
RegisterPdfFontIfExists("VNI-Helve", "VNI-Helve.ttf");
RegisterPdfFontIfExists(".VnTime", "VnTime.ttf");
RegisterPdfFontIfExists(".VnArial", "VnArial.ttf");
RegisterPdfFontIfExists(".VnHelvetica", "VnHelvetica.ttf");
RegisterPdfFontFamilyIfExists("Lato-Regular.ttf");
RegisterPdfFontFamilyIfExists("Lato-Bold.ttf");

// --- CẤU HÌNH PIPELINE (THỨ TỰ RẤT QUAN TRỌNG) ---

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();
app.UseSession();

app.UseAuthentication();
app.UseAuthorization();

// Middleware phân quyền của bạn nên nằm sau Authorization
app.UseMiddleware<PermissionMiddleware>();

app.MapRazorPages();

// 2. KÍCH HOẠT ROUTE CHO API (BẠN ĐÃ CÓ - GIỮ NGUYÊN)
app.MapControllers();

// 3. Sau khi Build xong (sau app = builder.Build())
app.UseHangfireDashboard(); // Cho phép truy cập link /hangfire để quản lý
// Đăng ký Job chạy tự động
// "SixMonthsReview" là ID định danh cho Job
RecurringJob.AddOrUpdate<SixMonthsStayReviewService>(
    "SixMonthsReview",
    service => service.Process6MonthsStayReview(),
    Cron.Monthly(1, 6),//ngay 1 luc 6 gio sang
    //Cron.Daily(11,36), //Chạy vào lúc 14:30 chiều nay.
    new RecurringJobOptions
    {
        TimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time")
    }
);

// Job thông báo chứng từ (Receiving & Issue Vouchers)
RecurringJob.AddOrUpdate<VoucherNotifyService>(
    "DailyVoucherNotification",
    service => service.ProcessVoucherNotification(),
    Cron.Daily(5, 0), // Chạy định kỳ vào lúc 5:00 sáng hàng ngày
    new RecurringJobOptions
    {
        TimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time")
    }
);

app.Run();

void RegisterPdfFontIfExists(string fontAlias, string fileName)
{
    var fontPath = Path.Combine(pdfFontDirectory, fileName);
    if (!File.Exists(fontPath))
    {
        return;
    }

    using var stream = File.OpenRead(fontPath);
    FontManager.RegisterFontWithCustomName(fontAlias, stream);
}

void RegisterPdfFontFamilyIfExists(string fileName)
{
    var fontPath = Path.Combine(pdfFontDirectory, fileName);
    if (!File.Exists(fontPath))
    {
        return;
    }

    using var stream = File.OpenRead(fontPath);
    FontManager.RegisterFont(stream);
}
