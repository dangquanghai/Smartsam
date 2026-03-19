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
            if (showAll) list.Add(new SelectListItem { Value = "", Text = "--- All ---" });

            using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                // Dapper: Query trực tiếp ra Dynamic object, cực kỳ nhẹ
                var data = conn.Query(sql);

                foreach (var item in data)
                {
                    // Ép kiểu dynamic sang IDictionary để truy cập theo tên cột truyền vào
                    var row = (IDictionary<string, object>)item;
                    list.Add(new SelectListItem
                    {
                        Value = row[valueField]?.ToString(),
                        Text = row[textField]?.ToString()
                    });
                }
            }
            return list;
        }

    }
}