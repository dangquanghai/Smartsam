using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SmartSam.Models;

namespace SmartSam.Services
{
    public class MenuService
    {
        private readonly string _connectionString;

        public MenuService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        public List<UserMenuDto> GetMenuForUser(string employeeCode)
        {
            var menus = new List<UserMenuDto>();

            using (SqlConnection conn = new SqlConnection(_connectionString))
            {
                conn.Open();

                // 1. KIỂM TRA QUYỀN ADMIN (Sử dụng IsAdminRole = 1)
                string checkAdminSql = @"SELECT TOP 1 r.IsAdminRole FROM SYS_RoleMember rm 
                                         INNER JOIN SYS_Role r ON rm.RoleID = r.RoleID
                                         INNER JOIN MS_Employee e ON rm.Operator = e.EmployeeID
                                         WHERE e.EmployeeCode = @EmpCode";

                SqlCommand cmdCheck = new SqlCommand(checkAdminSql, conn);
                cmdCheck.Parameters.AddWithValue("@EmpCode", employeeCode);

                var adminValue = cmdCheck.ExecuteScalar();
                // Nếu kết quả trả về không null và bằng 1 (hoặc True nếu là bit) thì là Admin
                bool isAdmin = adminValue != null && Convert.ToInt32(adminValue) == 1;

                // 2. CHUẨN BỊ CÂU SQL LẤY MENU
                string finalSql = "";

                if (isAdmin)
                {
                    // Nếu là Admin: Lấy tất cả Function có URL, nối với Module
                    finalSql = @"SELECT m.ModuleName, f.FunctionName, f.Url, f.FunctionID
                                 FROM SYS_Function f
                                 INNER JOIN SYS_Module m ON f.ModuleID = m.ModuleID
                                 WHERE f.Url <> ''
                                 ORDER BY  m.ModuleID , f.FunctionID ";
                }
                else
                {
                    // Nếu là User thường: Chạy câu SQL phân quyền gốc của bạn
                    finalSql = @"SELECT m.ModuleName, f.FunctionName, f.Url, f.FunctionID
                                 FROM SYS_RoleMember rm
                                 INNER JOIN SYS_Role r ON rm.RoleID = r.RoleID 
                                 INNER JOIN SYS_RolePermission rp ON r.RoleID = rp.RoleID 
                                 INNER JOIN SYS_FuncPermission fp ON rp.FunctionID = fp.FunctionID 
                                 INNER JOIN SYS_Function f ON fp.FunctionID = f.FunctionID 
                                 INNER JOIN SYS_Module m ON f.ModuleID = m.ModuleID 
                                 INNER JOIN MS_Employee e ON rm.Operator = e.EmployeeID
                                 WHERE fp.PermissionNo = 1 
                                   AND f.Url <> '' 
                                   AND e.EmployeeCode = @EmpCode
                                 ORDER BY m.ModuleName, f.FunctionName";
                }

                // 3. THỰC THI LẤY DỮ LIỆU MENU
                SqlCommand cmd = new SqlCommand(finalSql, conn);
                cmd.Parameters.AddWithValue("@EmpCode", employeeCode);

                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        menus.Add(new UserMenuDto
                        {
                            ModuleName = reader["ModuleName"].ToString(),
                            FunctionName = reader["FunctionName"].ToString(),
                            Url = reader["Url"].ToString(),
                            FunctionID = reader["FunctionID"].ToString()
                        });
                    }
                }
            }
            return menus;
        }
    }
}