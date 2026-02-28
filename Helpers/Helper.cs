using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Threading;
using System.Threading.Tasks;

namespace SmartSam.Helpers
{
    public static class Helper
    {
        #region Security & UI Helpers
        public static string EncryptPassword(string plainPassword)
        {
            string encrypted = "";
            for (int i = plainPassword.Length - 1; i >= 0; i--)
            {
                char letter = plainPassword[i];
                int code = (int)letter + 28;
                encrypted += code.ToString() + "-";
            }
            encrypted = DateTime.Now.ToOADate().ToString() + "-" + encrypted + DateTime.Now.AddDays(1).ToOADate().ToString();
            return encrypted;
        }

        public static bool CompareEncrypted(string inputPlain, string storedEncrypted)
        {
            string[] storedParts = storedEncrypted.Split('-');
            if (storedParts.Length < 3) return false;

            string[] encryptedInputParts = EncryptPassword(inputPlain).Split('-');
            if (encryptedInputParts.Length < 3) return false;

            string midInput = string.Join('-', encryptedInputParts[1..^1]);
            string midStored = string.Join('-', storedParts[1..^1]);

            return midInput == midStored;
        }

        public static string TranslateWin32ColorToHex(object win32Color)
        {
            try
            {
                if (win32Color == null || win32Color == DBNull.Value) return "#ffffff";
                long color = Convert.ToInt64(win32Color);
                int r = (int)(color & 0xFF);
                int g = (int)((color >> 8) & 0xFF);
                int b = (int)((color >> 16) & 0xFF);
                return string.Format("#{0:X2}{1:X2}{2:X2}", r, g, b);
            }
            catch { return "#ffffff"; }
        }
        public static (DateTime? FromDate, DateTime? ToDate) ParseDateRange(string? dateRange)
        {
            if (string.IsNullOrWhiteSpace(dateRange))
                return (null, null);

            // Tách chuỗi theo dấu "-"
            var parts = dateRange.Split('-', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 2)
            {
                string format = "dd/MM/yyyy";

                bool startOk = DateTime.TryParseExact(parts[0].Trim(), format, null,
                                System.Globalization.DateTimeStyles.None, out var start);

                bool endOk = DateTime.TryParseExact(parts[1].Trim(), format, null,
                              System.Globalization.DateTimeStyles.None, out var end);

                return (startOk ? start : null, endOk ? end : null);
            }

            return (null, null);
        }

        #endregion

        #region Lookup Helpers (ADO.NET)

        /// <summary>
        /// Truy vấn dữ liệu từ bảng bất kỳ phục vụ Select2
        /// </summary>
        public static List<(object Id, string Text)> LoadLookup(
            IConfiguration config,
            string table,
            string idField,
            string textField,
            string? keyword,
            int top = 20)
        {
            var list = new List<(object, string)>();
            var connStr = config.GetConnectionString("DefaultConnection");

            // Chống lỗi SQL Injection cho tên bảng/cột bằng cách dùng ngoặc vuông []
            string sql = $@"
                SELECT TOP {top} [{idField}], [{textField}]
                FROM {QuoteSqlIdentifier(table)}
                WHERE (@term IS NULL OR [{textField}] LIKE '%' + @term + '%')
                ORDER BY [{textField}]";

            using var conn = new SqlConnection(connStr);
            using var cmd = new SqlCommand(sql, conn);

            // Gán giá trị keyword vào tham số @term
            cmd.Parameters.Add("@term", SqlDbType.NVarChar, 100).Value = (object?)keyword ?? DBNull.Value;

            conn.Open();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add((
                    rd[0],
                    rd[1]?.ToString() ?? ""
                ));
            }
            return list;
        }

        /// <summary>
        /// Bọc tên schema/bảng/cột động theo chuẩn SQL Server, ví dụ:
        /// dbo.MS_Department -> [dbo].[MS_Department].
        /// Mục đích: tránh lỗi khi trùng keyword và giảm rủi ro SQL injection ở phần identifier.
        /// </summary>
        private static string QuoteSqlIdentifier(string identifier)
        {
            var parts = identifier
                .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(x => $"[{x.Replace("]", "]]")}]");

            return string.Join(".", parts);
        }

        /// <summary>
        /// Chuyển đổi List Tuple sang Json Object mà Select2 yêu cầu
        /// </summary>
        public static object ToSelect2Result(IEnumerable<(object Id, string Text)> data)
        {
            return data.Select(x => new
            {
                id = x.Id.ToString(), // Chuyển sang string để Select2 xử lý đồng nhất
                text = x.Text
            });
        }
        public static DataTable ExecuteQuery(string query, string connectionString)
        {
            DataTable dataTable = new DataTable();
            try
            {
                using (SqlConnection connection = new SqlConnection(connectionString))
                {
                    connection.Open();
                    using (SqlCommand command = new SqlCommand(query, connection))
                    {
                        using (SqlDataAdapter adapter = new SqlDataAdapter(command))
                        {
                            adapter.Fill(dataTable);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log lỗi tại đây
                Console.WriteLine("Lỗi SQL: " + ex.Message);
            }
            return dataTable;
        }

        public static void AddParameter(SqlCommand cmd, string name, object? value, SqlDbType? dbType = null, int? size = null)
        {
            SqlParameter p;
            if (dbType.HasValue)
            {
                p = size.HasValue
                    ? cmd.Parameters.Add(name, dbType.Value, size.Value)
                    : cmd.Parameters.Add(name, dbType.Value);
            }
            else
            {
                p = cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
            }

            p.Value = value ?? DBNull.Value;
        }

        public static async Task<List<T>> QueryAsync<T>(
            string connectionString,
            string sql,
            Func<SqlDataReader, T> map,
            Action<SqlCommand>? setup = null,
            CancellationToken cancellationToken = default)
        {
            var result = new List<T>();
            await using var conn = new SqlConnection(connectionString);
            await using var cmd = new SqlCommand(sql, conn);
            setup?.Invoke(cmd);

            await conn.OpenAsync(cancellationToken);
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await rd.ReadAsync(cancellationToken))
            {
                result.Add(map(rd));
            }

            return result;
        }

        public static async Task<T?> QuerySingleOrDefaultAsync<T>(
            string connectionString,
            string sql,
            Func<SqlDataReader, T> map,
            Action<SqlCommand>? setup = null,
            CancellationToken cancellationToken = default)
        {
            await using var conn = new SqlConnection(connectionString);
            await using var cmd = new SqlCommand(sql, conn);
            setup?.Invoke(cmd);

            await conn.OpenAsync(cancellationToken);
            await using var rd = await cmd.ExecuteReaderAsync(cancellationToken);
            if (!await rd.ReadAsync(cancellationToken))
            {
                return default;
            }

            return map(rd);
        }

        public static async Task<int> ExecuteNonQueryAsync(
            string connectionString,
            string sql,
            Action<SqlCommand>? setup = null,
            CancellationToken cancellationToken = default)
        {
            await using var conn = new SqlConnection(connectionString);
            await using var cmd = new SqlCommand(sql, conn);
            setup?.Invoke(cmd);

            await conn.OpenAsync(cancellationToken);
            return await cmd.ExecuteNonQueryAsync(cancellationToken);
        }

        public static async Task<object?> ExecuteScalarAsync(
            string connectionString,
            string sql,
            Action<SqlCommand>? setup = null,
            CancellationToken cancellationToken = default)
        {
            await using var conn = new SqlConnection(connectionString);
            await using var cmd = new SqlCommand(sql, conn);
            setup?.Invoke(cmd);

            await conn.OpenAsync(cancellationToken);
            return await cmd.ExecuteScalarAsync(cancellationToken);
        }

        /// <summary>
        /// Dùng cho các danh sách tĩnh load ngay khi vào trang (OnGet)
        /// </summary>
        public static List<SelectListItem> BuildIntSelectList<T>(
            IEnumerable<T> data,
            Func<T, int> idSelector,
            Func<T, string> nameSelector,
            bool showAll = false)
        {
            var list = data.Select(x => new SelectListItem
            {
                Value = idSelector(x).ToString(),
                Text = nameSelector(x)
            }).ToList();

            if (showAll)
            {
                list.Insert(0, new SelectListItem { Value = "", Text = "-- All --" });
            }
            return list;
        }
        #endregion
    }
}
