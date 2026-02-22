using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SmartSam.Helpers
{
    public class PermissionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IConfiguration _config;
        private readonly IMemoryCache _cache;

        public PermissionMiddleware(RequestDelegate next, IConfiguration config, IMemoryCache cache)
        {
            _next = next;
            _config = config;
            _cache = cache;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            string path = context.Request.Path.Value ?? "";

            // 1. Bỏ qua các trang công khai và tài nguyên tĩnh
            if (path.StartsWith("/Login") || path.StartsWith("/Logout") ||
                path.StartsWith("/dist") || path.StartsWith("/plugins") ||
                path == "/" || path.Equals("/Index", StringComparison.OrdinalIgnoreCase) ||
                path == "/AccessDenied")
            {
                await _next(context);
                return;
            }

            // 2. Kiểm tra đăng nhập (BẮT BUỘC CHO CẢ TRANG WEB VÀ API)
            if (context.User.Identity == null || !context.User.Identity.IsAuthenticated)
            {
                // Nếu là API mà chưa đăng nhập, trả về lỗi 401 thay vì Redirect 302
                if (path.StartsWith("/api/"))
                {
                    context.Response.StatusCode = 401; // Unauthorized
                    return;
                }

                context.Response.Redirect("/Login");
                return;
            }

            // --- BỔ SUNG TẠI ĐÂY ---
            // 2.1. Nếu đã đăng nhập và là yêu cầu API, cho phép đi tiếp luôn 
            // (Vì API Lookup thường dùng chung, không cần phân quyền chi tiết như trang)
            if (path.StartsWith("/api/"))
            {
                await _next(context);
                return;
            }
            // -----------------------

            // 3. KIỂM TRA ADMIN (Ưu tiên số 1)
            var isAdminClaim = context.User.FindFirst("IsAdminRole")?.Value;
            if (isAdminClaim == "True")
            {
                await _next(context);
                return;
            }

            // 4. KIỂM TRA QUYỀN USER THƯỜNG (Chỉ áp dụng cho các trang .cshtml)
            string employeeCode = context.User.Identity.Name;

            // Danh sách các trang mới áp dụng cơ chế Detail gộp (Add/Edit/View)
            // Sau này có trang nào mới bạn chỉ cần thêm vào mảng này
            var newMechanismPages = new[] { "STContractDetail" };

            bool useNewAccessLogic = newMechanismPages.Any(p => path.Contains(p, StringComparison.OrdinalIgnoreCase));

            bool accessGranted = false;

            if (useNewAccessLogic)
            {
                // Gọi hàm kiểm tra theo Module (chỉ cần có quyền trong folder là được vào)
                accessGranted = HasAccessNew(employeeCode, path);
            }
            else
            {
                // Gọi hàm kiểm tra cũ (khớp URL tuyệt đối)
                accessGranted = HasAccess(employeeCode, path);
            }

            if (!accessGranted)
            {
                context.Response.Redirect("/AccessDenied");
                return;
            }

            await _next(context);
        }

        private bool HasAccess(string empCode, string url)
        {
            // Chuẩn hóa URL hiện tại
            var cleanUrl = url.Split('?')[0].ToLower().TrimEnd('/');
            if (string.IsNullOrEmpty(cleanUrl)) cleanUrl = "/index";

            string cacheKey = $"Perm_{empCode}";

            if (!_cache.TryGetValue(cacheKey, out List<string> allowedUrls))
            {
                allowedUrls = GetPermissionsFromDb(empCode);

                // Chuẩn hóa danh sách URL từ DB (Chuyển thường, cắt dấu / cuối)
                allowedUrls = allowedUrls.Select(u => u.ToLower().TrimEnd('/')).ToList();

                var cacheOptions = new MemoryCacheEntryOptions().SetSlidingExpiration(TimeSpan.FromMinutes(20));
                _cache.Set(cacheKey, allowedUrls, cacheOptions);
            }

            // Khớp tuyệt đối hoặc khớp cùng module path (ví dụ có quyền /purchasing/supplier/index
            // thì được vào /purchasing/supplier/detail/1)
            return allowedUrls.Any(u => IsUrlMatch(cleanUrl, u));
        }

        private static bool IsUrlMatch(string requestUrl, string allowedUrl)
        {
            if (requestUrl == allowedUrl)
            {
                return true;
            }

            if (allowedUrl.EndsWith("/index", StringComparison.OrdinalIgnoreCase))
            {
                var basePath = allowedUrl[..^"/index".Length];
                if (requestUrl == basePath)
                {
                    return true;
                }

                if (requestUrl.StartsWith(basePath + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

       // Hàm kiểm tra mới: Chỉ cần User có quyền bất kỳ trong Module cha
    private bool HasAccessNew(string empCode, string url)
        {
            var cleanUrl = url.Split('?')[0].ToLower().TrimEnd('/');
            string cacheKey = $"Perm_{empCode}";

            if (!_cache.TryGetValue(cacheKey, out List<string> allowedUrls))
            {
                allowedUrls = GetPermissionsFromDb(empCode).Select(u => u.ToLower().TrimEnd('/')).ToList();
                _cache.Set(cacheKey, allowedUrls, TimeSpan.FromMinutes(20));
            }

            // Xác định folder cha (Ví dụ: từ /Sales/STContract/STContractDetail lấy ra /sales/stcontract)
            // Logic: Nếu danh sách quyền của User chứa bất kỳ URL nào thuộc folder này -> Cho phép vào Detail
            string folderPath = "/sales/stcontract";

            return allowedUrls.Any(u => u.Contains(folderPath, StringComparison.OrdinalIgnoreCase));
        }

        private List<string> GetPermissionsFromDb(string empCode)
        {
            var permissions = new List<string>();
            string connStr = _config.GetConnectionString("DefaultConnection");

            using var conn = new SqlConnection(connStr);
            conn.Open();

            // SQL MỚI: 
            // 1. Lấy chuỗi rp.Permission (ví dụ '1,3,167') từ RolePermission
            // 2. So khớp với fp.PermissionNo trong bảng SYS_FuncPermission để lấy đúng URL hành động
            string sql = @"
            SELECT DISTINCT fp.Url
            FROM SYS_RoleMember rm
            INNER JOIN SYS_RolePermission rp ON rm.RoleID = rp.RoleID
            INNER JOIN SYS_FuncPermission fp ON rp.FunctionID = fp.FunctionID
            INNER JOIN MS_Employee e ON rm.Operator = e.EmployeeID
            WHERE e.EmployeeCode = @EmpCode 
            AND fp.Url IS NOT NULL AND fp.Url <> ''
            -- Logic: Kiểm tra xem PermissionNo của hành động có nằm trong chuỗi Permission của Role không
            AND (',' + rp.Permission + ',') LIKE ('%,' + CAST(fp.PermissionNo AS VARCHAR) + ',%')";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@EmpCode", empCode);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                permissions.Add(reader["Url"].ToString());
            }

            return permissions;
        }
    }
}
