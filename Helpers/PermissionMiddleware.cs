using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Data.SqlClient;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

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

            // 1. Bỏ qua các trang công khai, tài nguyên tĩnh và API Lookup
            if (path.StartsWith("/Login") || path.StartsWith("/Logout") ||
                path.StartsWith("/dist") || path.StartsWith("/plugins") || path.StartsWith("/hangfire") ||
                path == "/" || path.Equals("/Index", StringComparison.OrdinalIgnoreCase) ||
                path == "/AccessDenied" ||
                path.StartsWith("/api/Lookup", StringComparison.OrdinalIgnoreCase)) // THÊM DÒNG NÀY
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
        // Hàm này chỉ giữ lại: a-z, A-Z, 0-9, dấu gạch chéo /, dấu gạch ngang -, và dấu gạch dưới _
        private bool HasAccessNew(string empCode, string url)
        {
            if (string.IsNullOrEmpty(url)) return false;

            // 1. Làm sạch URL đang truy cập (Cắt query, xóa ký tự ẩn, xóa / ở cuối)
            var cleanUrl = CleanSpecials(url.Split('?')[0]);
            string cacheKey = $"Perm_{empCode}";

            // 2. Lấy danh sách URL được cấp phép từ Cache hoặc DB
            if (!_cache.TryGetValue(cacheKey, out List<string> allowedUrls))
            {
                // Làm sạch TOÀN BỘ danh sách từ DB trước khi nạp vào Cache
                allowedUrls = GetPermissionsFromDb(empCode)
                                .Select(u => CleanSpecials(u))
                                .Where(u => !string.IsNullOrEmpty(u)) // Loại bỏ dòng rỗng nếu có
                                .ToList();

                _cache.Set(cacheKey, allowedUrls, TimeSpan.FromMinutes(1));
            }

            // TRƯỜNG HỢP A: Khớp tuyệt đối (Dùng cho Index, Cancel, Delete...)
            // Lúc này cả 2 bên đều đã sạch tinh tươm, Contains chắc chắn sẽ chạy đúng
            if (allowedUrls.Contains(cleanUrl)) return true;


            // TRƯỜNG HỢP B: Logic "Chung một mái nhà" (Dùng cho các trang Detail gộp)
            // Lấy Folder cha của URL đang truy cập (Cắt bỏ phần tên file/action cuối cùng)
            var lastSlashIndex = cleanUrl.LastIndexOf('/');
            if (lastSlashIndex > 0)
            {
                string parentFolder = cleanUrl.Substring(0, lastSlashIndex); // Ví dụ: sales/stcontract/stcontract

                // Kiểm tra xem trong DB, User có bất kỳ quyền nào bắt đầu bằng Folder này không
                // Vì dữ liệu đã ToLower() lúc CleanSpecials nên có thể so sánh trực tiếp
                return allowedUrls.Any(u => u.StartsWith(parentFolder + "/"));
            }

            return false;
        }

        /// <summary>
        /// Hàm chuyên dụng để xóa sạch khoảng trắng, ký tự ẩn (\r, \n, \0, BOM) 
        /// và chỉ giữ lại các ký tự hợp lệ của một URL
        /// </summary>
        private string CleanSpecials(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;

            // 1. Loại bỏ khoảng trắng và dấu gạch chéo ở đầu/cuối chuỗi
            string trimmed = input.Trim().Trim('/', ' ');

            // 2. Dùng Regex giữ lại: chữ, số, gạch chéo (/), gạch ngang (-), gạch dưới (_)
            // Tự động chuyển về chữ thường để đồng bộ
            return Regex.Replace(trimmed, @"[^a-zA-Z0-9/_-]", "").ToLower();
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
                AND fp.Url IS NOT NULL AND fp.Url <> ''";


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
