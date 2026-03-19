using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using SmartSam.Helpers;
using Microsoft.Data.SqlClient;
using System.Security.Claims;
using System.Data;
using System.Threading.Tasks;

namespace SmartSam.Pages
{
    public class LoginModel : PageModel
    {
        private readonly IConfiguration _config;
        public LoginModel(IConfiguration config) => _config = config;

        [BindProperty] public string? Username { get; set; }
        [BindProperty] public string? Password { get; set; }
        public string? Message { get; set; }

        public async Task<IActionResult> OnPostAsync()
        {
            var username = (Username ?? string.Empty).Trim();
            var password = Password ?? string.Empty;
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                Message = "Vui lòng nhập tài khoản và mật khẩu.";
                return Page();
            }

            string connStr = _config.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Missing connection string: DefaultConnection");
            using var conn = new SqlConnection(connStr);
            await conn.OpenAsync();

            // SQL: Lấy thông tin User và RoleID từ bảng SYS_RoleMember
            // Lưu ý: Một user có thể có nhiều Role, ở đây ta lấy Role quan trọng nhất hoặc đầu tiên
            string sql = @"
                SELECT TOP 1 e.EmployeeCode, e.EmployeeName, e.NewPassword, rm.RoleID , r.IsAdminRole
                FROM MS_Employee e
                INNER JOIN SYS_RoleMember rm ON e.EmployeeID = rm.Operator
                INNER JOIN SYS_Role r on rm.RoleID = r.RoleID
                WHERE e.EmployeeCode = @User AND e.IsActive = 1";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.Add("@User", SqlDbType.NVarChar, 50).Value = username;

            using var reader = await cmd.ExecuteReaderAsync();
            if (reader.Read())
            {
                var dbPass = reader["NewPassword"].ToString();
                if (Helper.CompareEncrypted(password, dbPass ?? string.Empty))
                {
                    string empCode = reader["EmployeeCode"].ToString() ?? string.Empty;
                    string empName = reader["EmployeeName"].ToString() ?? string.Empty;
                    string roleId = reader["RoleID"] != DBNull.Value ? reader["RoleID"].ToString() ?? "0" : "0";
                    bool isAdminRole = reader["IsAdminRole"] != DBNull.Value && Convert.ToBoolean(reader["IsAdminRole"]);

                    var claims = new List<Claim>
                    {
                        new Claim(ClaimTypes.Name, empCode), // Lưu Mã NV làm định danh
                        new Claim("FullName", empName),      // Lưu Tên để hiển thị Layout
                        new Claim("RoleID", roleId),          // QUAN TRỌNG: Lưu RoleID để PermissionService dùng
                        new Claim("IsAdminRole", isAdminRole.ToString()) // // Chuyển bool thành "True" hoặc "False"

                    };

                    var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                    var principal = new ClaimsPrincipal(identity);

                    await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, principal);

                    return RedirectToPage("/Index");
                }
            }

            Message = "Tài khoản hoặc mật khẩu không đúng.";
            return Page();
        }
    }
}
