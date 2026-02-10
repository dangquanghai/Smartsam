using Microsoft.AspNetCore.Authentication.Cookies;
using SmartSam.Pages;
using SmartSam.Services; // Kiểm tra namespace này cho chuẩn
using Microsoft.AspNetCore.Http.Features;
using SmartSam.Helpers;
using SmartSam.Services.Purchasing.Supplier.Abstractions;
using SmartSam.Services.Purchasing.Supplier.Implementations;

var builder = WebApplication.CreateBuilder(args);

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

builder.Services.AddScoped<MenuService>();
builder.Services.AddScoped<PermissionService>();
builder.Services.AddScoped<ISupplierRepository, SupplierRepository>();
builder.Services.AddScoped<ISupplierService, SupplierService>();

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

var app = builder.Build();

// --- CẤU HÌNH PIPELINE (THỨ TỰ RẤT QUAN TRỌNG) ---

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

// Middleware phân quyền của bạn nên nằm sau Authorization
app.UseMiddleware<PermissionMiddleware>();

app.MapRazorPages();

// 2. KÍCH HOẠT ROUTE CHO API (BẠN ĐÃ CÓ - GIỮ NGUYÊN)
app.MapControllers();

app.Run();
