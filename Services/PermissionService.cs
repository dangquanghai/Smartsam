using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;

namespace SmartSam.Services
{
    public class PagePermissions
    {
        public List<int> AllowedNos { get; set; } = new List<int>();

        // Hàm check quyền nhanh cho file .cshtml
        public bool HasPermission(int no)
        {
            return AllowedNos != null && AllowedNos.Contains(no);
        }
    }
    public class PermissionService
    {
        private readonly string _connectionString;
        public PermissionService(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection");
        }

        // Trả về danh sách PermissionNo thô từ SQL (ví dụ: 2, 3, 4, 6, 7)
        public List<int> GetPermissionsForPage(int roleId, int functionId)
        {
            var result = new List<int>();
            using var conn = new SqlConnection(_connectionString);

            // Câu SQL theo cấu trúc mới của bạn
            string sql = "SELECT Permission FROM SYS_RolePermission WHERE FunctionID = @FunctionID AND RoleID = @RoleID";

            try
            {
                using var cmd = new SqlCommand(sql, conn);
                cmd.Parameters.AddWithValue("@RoleID", roleId);
                cmd.Parameters.AddWithValue("@FunctionID", functionId);
                conn.Open();

                object scalar = cmd.ExecuteScalar();
                if (scalar != null && scalar != DBNull.Value)
                {
                    string permString = scalar.ToString(); // Ví dụ: "2,3,4,6"
                    result = permString.Split(',')
                                       .Select(s => int.Parse(s.Trim()))
                                       .ToList();
                }
            }
            catch (Exception ex)
            {
                // Log error if needed
            }

            return result;
        }
    }
}