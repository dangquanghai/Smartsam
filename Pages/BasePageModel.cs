using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Helpers;
using System.Data;
using Dapper;

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

        // Logic thực thi tách riêng để có thể gọi từ Code-behind nếu cần
        protected async Task<int> CheckApmtAvailLogic(string apartmentNo, string fromDate, string toDate, long contractId)
        {
            try
            {
                if (string.IsNullOrEmpty(apartmentNo) || string.IsNullOrEmpty(fromDate) || string.IsNullOrEmpty(toDate))
                    return -1;

                DateTime dFrom = DateTime.Parse(fromDate);
                DateTime dTo = DateTime.Parse(toDate);

                using (IDbConnection db = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
                {
                    var parameters = new
                    {
                        ApartmentNo = apartmentNo,
                        FromDate = dFrom,
                        ToDate = dTo,
                        isShortTerm = 1, // Theo logic VB6 của bạn
                        ContractID = contractId
                    };

                    // Gọi Stored Procedure
                    var status = await db.ExecuteScalarAsync<int>(
                        "sp_CheckApmtAvail",
                        parameters,
                        commandType: CommandType.StoredProcedure
                    );

                    return status;
                }
            }
            catch (Exception ex)
            {
                // Log lỗi ở đây
                return -1;
            }
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