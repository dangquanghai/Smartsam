using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Helpers;

namespace SmartSam.Pages
{
    public class BasePageModel : PageModel
    {
        protected readonly IConfiguration _config;

        public BasePageModel(IConfiguration config)
        {
            _config = config;
        }

        // Hàm tổng quát 
        protected List<SelectListItem> LoadSelect2(string table, string idField, string textField, string? keyword = null)
        {
            var data = Helper.LoadLookup(_config, table, idField, textField, keyword);

            return data.Select(x => new SelectListItem
            {
                Value = x.Id?.ToString(),
                Text = x.Text
            }).ToList();
        }

        // Trong BasePageModel.cs
        protected List<SelectListItem> LoadListFromSql(string sql, string valueField, string textField, bool showAll = false)
        {
            var list = new List<SelectListItem>();

            // 1. Thêm tùy chọn "All" nếu cần (thường dùng cho trang Index/Search)
            if (showAll)
            {
                list.Add(new SelectListItem { Value = "", Text = "--- All ---" });
            }

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand(sql, conn);

            try
            {
                conn.Open();
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                {
                    list.Add(new SelectListItem
                    {
                        // Sử dụng tên cột hoặc index được truyền vào
                        Value = rd[valueField]?.ToString(),
                        Text = rd[textField]?.ToString()
                    });
                }
            }
            catch (Exception ex)
            {
                // Log lỗi ở đây nếu cần
                Console.WriteLine("Error LoadListFromSql: " + ex.Message);
            }

            return list;
        }

    }
}