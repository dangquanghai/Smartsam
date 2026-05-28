using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using SmartSam.Helpers;
using System.Data;
using Dapper;
using System.Text;
/*
class ConverterTCVN
{
    private static char[] tcvnchars = {
        'µ', '¸', '¶', '·', '¹',
        '¨', '»', '¾', '¼', '½', 'Æ',
        '©', 'Ç', 'Ê', 'È', 'É', 'Ë',
        '®', 'Ì', 'Ð', 'Î', 'Ï', 'Ñ',
        'ª', 'Ò', 'Õ', 'Ó', 'Ô', 'Ö',
        '×', 'Ý', 'Ø', 'Ü', 'Þ',
        'ß', 'ã', 'á', 'â', 'ä',
        '«', 'å', 'è', 'æ', 'ç', 'é',
        '¬', 'ê', 'í', 'ë', 'ì', 'î',
        'ï', 'ó', 'ñ', 'ò', 'ô',
        '­', 'õ', 'ø', 'ö', '÷', 'ù',
        'ú', 'ý', 'û', 'ü', 'þ',
        '¡', '¢', '§', '£', '¤', '¥', '¦'
        };

    private static char[] unichars = {
        'à', 'á', 'ả', 'ã', 'ạ',
        'ă', 'ằ', 'ắ', 'ẳ', 'ẵ', 'ặ',
        'â', 'ầ', 'ấ', 'ẩ', 'ẫ', 'ậ',
        'đ', 'è', 'é', 'ẻ', 'ẽ', 'ẹ',
        'ê', 'ề', 'ế', 'ể', 'ễ', 'ệ',
        'ì', 'í', 'ỉ', 'ĩ', 'ị',
        'ò', 'ó', 'ỏ', 'õ', 'ọ',
        'ô', 'ồ', 'ố', 'ổ', 'ỗ', 'ộ',
        'ơ', 'ờ', 'ớ', 'ở', 'ỡ', 'ợ',
        'ù', 'ú', 'ủ', 'ũ', 'ụ',
        'ư', 'ừ', 'ứ', 'ử', 'ữ', 'ự',
        'ỳ', 'ý', 'ỷ', 'ỹ', 'ỵ',
        'Ă', 'Â', 'Đ', 'Ê', 'Ô', 'Ơ', 'Ư'
        };



    private static char[] convertTable;

    static ConverterTCVN()
    {
        convertTable = new char[256];
        for (int i = 0; i < 256; i++)
            convertTable[i] = (char)i;
        for (int i = 0; i < tcvnchars.Length; i++)
            convertTable[tcvnchars[i]] = unichars[i];
    }

    public static string TCVN3ToUnicode(string value)
    {
        char[] chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (chars[i] < (char)256)
                chars[i] = convertTable[chars[i]];
        return new string(chars);
    }

}
*/

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
        protected string TCVN32Unicode(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";

            // 1. Tận dụng nguyên vẹn 2 mảng ký tự chuẩn từ class cũ của bạn
            char[] tcvnchars = {
        'µ', '¸', '¶', '·', '¹',
        '¨', '»', '¾', '¼', '½', 'Æ',
        '©', 'Ç', 'Ê', 'È', 'É', 'Ë',
        '®', 'Ì', 'Ð', 'Î', 'Ï', 'Ñ',
        'ª', 'Ò', 'Õ', 'Ó', 'Ô', 'Ö',
        '×', 'Ý', 'Ø', 'Ü', 'Þ',
        'ß', 'ã', 'á', 'â', 'ä',
        '«', 'å', 'è', 'æ', 'ç', 'é',
        '¬', 'ê', 'í', 'ë', 'ì', 'î',
        'ï', 'ó', 'ñ', 'ò', 'ô',
        '­', 'õ', 'ø', 'ö', '÷', 'ù',
        'ú', 'ý', 'û', 'ü', 'þ',
        '¡', '¢', '§', '£', '¤', '¥', '¦'
    };

            char[] unichars = {
        'à', 'á', 'ả', 'ã', 'ạ',
        'ă', 'ằ', 'ắ', 'ẳ', 'ẵ', 'ặ',
        'â', 'ầ', 'ấ', 'ẩ', 'ẫ', 'ậ',
        'đ', 'è', 'é', 'ẻ', 'ẽ', 'ẹ',
        'ê', 'ề', 'ế', 'ể', 'ễ', 'ệ',
        'ì', 'í', 'ỉ', 'ĩ', 'ị',
        'ò', 'ó', 'ỏ', 'õ', 'ọ',
        'ô', 'ồ', 'ố', 'ổ', 'ỗ', 'ộ',
        'ơ', 'ờ', 'ớ', 'ở', 'ỡ', 'ợ',
        'ù', 'ú', 'ủ', 'ũ', 'ụ',
        'ư', 'ừ', 'ứ', 'ử', 'ữ', 'ự',
        'ỳ', 'ý', 'ỷ', 'ỹ', 'ỵ',
        'Ă', 'Â', 'Đ', 'Ê', 'Ô', 'Ơ', 'Ư'
    };

            // 2. Tạo Dictionary để tra cứu (Lookup) nhanh với độ phức tạp O(1)
            // Thay vì dùng mảng 256 byte tĩnh, khởi tạo nhanh trong hàm vẫn cực kỳ tối ưu
            var convertMap = new Dictionary<char, char>();
            for (int i = 0; i < tcvnchars.Length; i++)
            {
                if (!convertMap.ContainsKey(tcvnchars[i]))
                {
                    convertMap.Add(tcvnchars[i], unichars[i]);
                }
            }

            // 3. Duyệt chuỗi 1 lần duy nhất bằng StringBuilder (Hiệu năng cực cao)
            StringBuilder sb = new StringBuilder(input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                // Nếu ký tự nằm trong bảng mã TCVN3 thì đổi sang Unicode, ngược lại giữ nguyên
                if (convertMap.TryGetValue(c, out char uniChar))
                {
                    sb.Append(uniChar);
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }

    protected string Vni2Unicode(string input)
    {
        if (string.IsNullOrEmpty(input)) return "";

        // 1. Giữ nguyên mảng số nguyên đại diện cho ký tự Unicode từ hàm cũ của bạn
        int[] u = new int[] {
        7845, 7847, 7849, 7851, 7853, 226, 7843, 227, 7841, 7855, 7857, 7859,
        7861, 7863, 259, 250, 249, 7911, 361, 7909, 7913, 7915, 7917, 7919, 7921, 432,
        7871, 7873, 7875, 7877, 7879, 234, 233, 232, 7867, 7869, 7865, 7889, 7891, 7893,
        7895, 7897, 7887, 245, 7885, 7899, 7901, 7903, 7905, 7907, 417,
        237, 236, 7881, 297, 7883, 253, 7923, 7927, 7929, 7925, 273, 7844, 7846, 7848,
        7850, 7852, 194, 7842, 195, 7840, 7854, 7856, 7858, 7860, 7862, 258,
        218, 217, 7910, 360, 7908, 7912, 7914, 7916, 7918, 7920, 431, 7870, 7872, 7874,
        7876, 7878, 202, 201, 200, 7866, 7868, 7864, 7888, 7890, 7892, 7894, 7896,
        7886, 213, 7884, 7898, 7900, 7902, 7904, 7906, 416, 205, 204, 7880, 296,
        7882, 221, 7922, 7926, 7928, 7924, 272, 225, 224, 244, 243, 242, 193, 192, 212, 211, 210
        };

        // 2. Giữ nguyên mảng chuỗi ký tự nguồn VNI từ hàm cũ của bạn
        string[] v = new string[] {
        "aá", "aà", "aå", "aã", "aä", "aâ", "aû", "aõ", "aï", "aé", "aè",
        "aú", "aü", "aë", "aê", "uù", "uø", "uû", "uõ", "uï", "öù", "öø", "öû", "öõ",
        "öï", "ö", "eá", "eà", "eå", "eã", "eä", "eâ", "eù", "eø", "eû", "eõ", "eï",
        "oá", "oà", "oå", "oã", "oä", "oû", "oõ", "oï", "ôù", "ôø",
        "ôû", "ôõ", "ôï", "ô", "í", "ì", "æ", "ó", "ò", "yù", "yø", "yû", "yõ", "î",
        "ñ", "AÁ", "AÀ", "AÅ", "AÃ", "AÄ", "AÂ", "AÛ", "AÕ",
        "AÏ", "AÉ", "AÈ", "AÚ", "AÜ", "AË", "AÊ", "UÙ", "UØ", "UÛ", "UÕ",
        "UÏ", "ÖÙ", "ÖØ", "ÖÛ", "ÖÕ", "ÖÏ", "Ö", "EÁ", "EÀ", "EÅ",
        "EÃ", "EÄ", "EÂ", "EÙ", "EØ", "EÛ", "EÕ", "EÏ", "OÁ", "OÀ", "OÅ",
        "OÃ", "OÄ", "OÛ", "OÕ", "OÏ", "ÔÙ", "ÔØ", "ÔÛ",
        "ÔÕ", "ÔÏ", "Ô", "Í", "Ì", "Æ", "Ó", "Ò", "YÙ", "YØ", "YÛ", "YÕ",
        "Î", "Ñ", "aù", "aø", "oâ", "où", "oø", "AÙ", "AØ", "OÂ", "OÙ", "OØ"
        };

        // 3. Tận dụng StringBuilder để thực hiện thay thế nội dung (In-place replacement)
        // Cách này giúp tối ưu hóa bộ nhớ, tăng tốc độ xử lý lên nhiều lần so với s = s.Replace()
        StringBuilder sb = new StringBuilder(input);

        for (int i = 0; i < v.Length; i++)
        {
            sb.Replace(v[i], ((char)u[i]).ToString());
        }

        return sb.ToString();
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