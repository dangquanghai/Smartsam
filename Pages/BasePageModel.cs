using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Helpers;
using System.Data;
using Dapper;
using System.Text;

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
        protected string ConverterTCVN3(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";

            // Bảng mã ký tự TCVN3 (Font .VnTime)
            string[] tcvnChars = new string[]
            {
        "à", "á", "ả", "ã", "ạ", "ầ", "ấ", "ẩ", "ẫ", "ậ", "ề", "ế", "ể", "ễ", "ệ", "ì", "í", "ỉ", "ĩ", "ị", "ò", "ó", "ỏ", "õ", "ọ", "ồ", "ố", "ổ", "ỗ", "ộ", "ờ", "ớ", "ở", "ỡ", "ợ", "ù", "ú", "ủ", "ũ", "ụ", "ừ", "ứ", "ử", "ữ", "ự", "ỳ", "ý", "ỷ", "ỹ", "ỵ", "ă", "â", "đ", "ê", "ô", "ơ", "ư",
        "À", "Á", "Ả", "Ã", "Ạ", "Ầ", "Ấ", "Ẩ", "Ẫ", "Ậ", "Ề", "Ế", "Ể", "Ễ", "Ệ", "Ì", "Í", "Ỉ", "Ĩ", "Ị", "Ò", "Ó", "Ỏ", "Õ", "Ọ", "Ồ", "Ố", "Ổ", "Ỗ", "Ộ", "Ờ", "Ớ", "Ở", "Ỡ", "Ợ", "Ù", "Ú", "Ủ", "Ũ", "Ụ", "Ừ", "Ứ", "Ử", "Ữ", "Ự", "Ỳ", "Ý", "Ỷ", "Ỹ", "Ỵ", "Ă", "Â", "Đ", "Ê", "Ô", "Ơ", "Ư"
            };

            // Bảng mã ký tự Unicode tương ứng
            string[] unichars = new string[]
            {
        "à", "á", "ả", "ã", "ạ", "ầ", "ấ", "ẩ", "ẫ", "ậ", "ề", "ế", "ể", "ễ", "ệ", "ì", "í", "ỉ", "ĩ", "ị", "ò", "ó", "ỏ", "õ", "ọ", "ồ", "ố", "ổ", "ỗ", "ộ", "ờ", "ớ", "ở", "ỡ", "ợ", "ù", "ú", "ủ", "ũ", "ụ", "ừ", "ứ", "ử", "ữ", "ự", "ỳ", "ý", "ỷ", "ỹ", "ỵ", "ă", "â", "đ", "ê", "ô", "ơ", "ư",
        "À", "Á", "Ả", "Ã", "Ạ", "Ầ", "Ấ", "Ẩ", "Ẫ", "Ậ", "Ề", "Ế", "Ể", "Ễ", "Ệ", "Ì", "Í", "Ỉ", "Ĩ", "Ị", "Ò", "Ó", "Ỏ", "Õ", "Ọ", "Ồ", "Ố", "Ổ", "Ỗ", "Ộ", "Ờ", "Ớ", "Ở", "Ỡ", "Ợ", "Ù", "Ú", "Ủ", "Ũ", "Ụ", "Ừ", "Ứ", "Ử", "Ữ", "Ự", "Ỳ", "Ý", "Ỷ", "Ỹ", "Ỵ", "Ă", "Â", "Đ", "Ê", "Ô", "Ơ", "Ư"
            };

            // Một số ký tự đặc biệt của TCVN3 dạng font 1 byte cần map tay nhanh
            StringBuilder sb = new StringBuilder(input);
            sb.Replace("¸", "á").Replace("µ", "à").Replace("¶", "ả").Replace("·", "ã").Replace("¹", "ạ")
              .Replace("¾", "é").Replace("½", "è").Replace("¼", "ẻ").Replace("Æ", "ẽ").Replace("Ç", "ẹ")
              .Replace("Ý", "í").Replace("Ì", "ì").Replace("Î", "ỉ").Replace("Ï", "ĩ").Replace("Ñ", "ị")
              .Replace("ã", "ó").Replace("ò", "ò").Replace("ó", "ỏ").Replace("õ", "õ").Replace("ä", "ọ")
              .Replace("ó", "ú").Replace("ï", "ù").Replace("ñ", "ủ").Replace("ò", "ũ").Replace("ù", "ụ")
              .Replace("ý", "ý").Replace("ú", "ỳ").Replace("û", "ỷ").Replace("ü", "ỹ").Replace("þ", "ỵ")
              .Replace("®", "đ").Replace("§", "Đ").Replace("¢", "â").Replace(" ", " ");

            string result = sb.ToString();
            for (int i = 0; i < tcvnChars.Length; i++)
            {
                result = result.Replace(tcvnChars[i], unichars[i]);
            }

            return result;
        }
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