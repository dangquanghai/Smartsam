using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Linq;

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
                path.StartsWith("/dist") || path.StartsWith("/plugins") || path.StartsWith("/hangfire") ||
                path == "/" || path.Equals("/Index", StringComparison.OrdinalIgnoreCase) ||
                path == "/AccessDenied")
            {
                await _next(context);
                return;
            }

            // 2. Kiểm tra đăng nhập
            if (context.User.Identity == null || !context.User.Identity.IsAuthenticated)
            {
                if (path.StartsWith("/api/"))
                {
                    context.Response.StatusCode = 401;
                    return;
                }
                context.Response.Redirect("/Login");
                return;
            }

            // 3. KIỂM TRA ADMIN
            var isAdminClaim = context.User.FindFirst("IsAdminRole")?.Value;
            if (isAdminClaim == "True" || isAdminClaim == "1")
            {
                await _next(context);
                return;
            }

            // 4. KIỂM TRA QUYỀN USER THƯỜNG (GIAI ĐOẠN 1)
            string employeeCode = context.User.Identity.Name;

            // Middleware giờ đây chỉ cần một logic duy nhất: HasAccessNew
            // Logic này đã bao gồm: Khớp tuyệt đối, Khớp Folder cha cho Detail, và Khớp quyền tại chỗ (Cancel)
            if (!HasAccessNew(employeeCode, path))
            {
                context.Response.Redirect("/AccessDenied");
                return;
            }

            await _next(context);
        }

        private bool HasAccessNew(string empCode, string url)
        {
            // 1. Làm sạch URL đang truy cập
            var cleanUrl = url.Split('?')[0].ToLower().TrimEnd('/');
            string cacheKey = $"Perm_{empCode}";

            // 2. Lấy danh sách URL được cấp phép từ Cache hoặc DB
            if (!_cache.TryGetValue(cacheKey, out List<string> allowedUrls))
            {
                allowedUrls = GetPermissionsFromDb(empCode)
                                .Select(u => u.ToLower().TrimEnd('/'))
                                .ToList();
                _cache.Set(cacheKey, allowedUrls, TimeSpan.FromMinutes(20));
            }

            // TRƯỜNG HỢP A: Khớp tuyệt đối (Dùng cho Index, Cancel, Delete...)
            if (allowedUrls.Contains(cleanUrl)) return true;

            // TRƯỜNG HỢP B: Logic "Chung một mái nhà" (Dùng cho các trang Detail gộp)
            // Lấy Folder cha của URL đang truy cập (Cắt bỏ phần tên file/action cuối cùng)
            var lastSlashIndex = cleanUrl.LastIndexOf('/');
            if (lastSlashIndex > 0)
            {
                string parentFolder = cleanUrl.Substring(0, lastSlashIndex); // Ví dụ: /sales/stcontract/stcontract

                // Kiểm tra xem trong DB, User có bất kỳ quyền nào bắt đầu bằng Folder này không
                // Ví dụ DB có: /sales/stcontract/stcontract/add -> Sẽ cho phép vào /sales/stcontract/stcontract/STContractDetail
                return allowedUrls.Any(u => u.StartsWith(parentFolder + "/", StringComparison.OrdinalIgnoreCase));
            }

            return false;
        }

        private List<string> GetPermissionsFromDb(string empCode)
        {
            var permissions = new List<string>();
            string connStr = _config.GetConnectionString("DefaultConnection");

            using var conn = new SqlConnection(connStr);
            conn.Open();

            // SQL lấy tất cả URL hành động mà Role của User này được phép
            string sql = @"
                SELECT DISTINCT fp.Url
                FROM SYS_RoleMember rm
                INNER JOIN SYS_RolePermission rp ON rm.RoleID = rp.RoleID
                INNER JOIN SYS_FuncPermission fp ON rp.FunctionID = fp.FunctionID
                INNER JOIN MS_Employee e ON rm.Operator = e.EmployeeID
                WHERE e.EmployeeCode = @EmpCode 
                AND fp.Url IS NOT NULL AND fp.Url <> ''
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