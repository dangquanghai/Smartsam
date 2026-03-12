using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SmartSam.Services
{
    // Class này giữ lại để dùng làm "helper" hoặc chứa quyền
    public class PagePermissions
    {
        public List<int> AllowedNos { get; set; } = new List<int>();

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

        // SỬA LẠI Ở ĐÂY: Trả về List<int> để khớp với tất cả các trang cũ
        public List<int> GetPermissionsForPage(int roleId, int functionId)
        {
            var result = new List<int>();
            using var conn = new SqlConnection(_connectionString);

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
                    string permString = scalar.ToString();
                    result = permString.Split(',', StringSplitOptions.RemoveEmptyEntries)
                                       .Select(s => int.TryParse(s.Trim(), out int n) ? n : (int?)null)
                                       .Where(n => n.HasValue)
                                       .Select(n => n.Value)
                                       .ToList();
                }
            }
            catch (Exception)
            {
                // Log lỗi nếu cần
            }

            return result;
        }
    }
}