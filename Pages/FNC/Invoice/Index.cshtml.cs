using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;
using System.Text;
using SmartSam.Helpers;
using SmartSam.Services.Interfaces;
using SmartSam.Services;
using Microsoft.AspNetCore.Mvc.Rendering;
using com.ehoadondientu;
using System.Linq;
using DocumentFormat.OpenXml.Spreadsheet;

namespace SmartSam.Pages.FNC.Invoice
{
    public class IndexModel : BasePageModel
    {
        private readonly ISecurityService _securityService;
        private readonly ILogger<IndexModel> _logger;

        private const int FUNCTION_ID = 149;

        public IndexModel(ISecurityService securityService, IConfiguration config, ILogger<IndexModel> logger)
            : base(config)
        {
            _securityService = securityService;
            _logger = logger;
        }

        // --- PROPERTIES ĐỒNG BỘ 100% VỚI INDEX.CSHTML ---
        [BindProperty(SupportsGet = true)]
        public FilterCriteria Filter { get; set; } = new FilterCriteria();

        public List<SelectListItem> StatusList { get; set; } = new List<SelectListItem>();

        // Danh sách dữ liệu chính hiển thị lên Table (.cshtml dùng Model.DataList)
        public List<EInvoiceRow> DataList { get; set; } = new List<EInvoiceRow>();

        // Shortcut trỏ về DataList để sửa lỗi biên dịch dòng 76 (@Model.Contracts.Count)
        public List<EInvoiceRow> Contracts => DataList;

        public int TotalRecords { get; set; }
        public string Message { get; set; }
        public PagePermissions PagePerm { get; private set; } = new PagePermissions();


        // --- EVENT HANDLERS ---
        public void OnGet()
        {
            int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");
           // PagePerm.AllowedNos = _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, 0);

            // Khởi tạo dropdown danh sách trạng thái để tránh lỗi render select component
            //LoadStatusDropdown();

            // Load dữ liệu đổ vào Table
            LoadData();
        }

        // Thêm tham số [FromForm] List<string> uids để nhận dữ liệu từ AJAX
        public async Task<IActionResult> OnPostPostToVinhHyMoi([FromForm] List<string> uids)
        {
            try
            {
                // Kiểm tra nếu không có UID nào được chọn từ giao diện thì báo lỗi ngay
                if (uids == null || !uids.Any())
                {
                    return new JsonResult(new { success = false, message = "Vui lòng chọn ít nhất một hóa đơn từ giao diện!" });
                }

                var service = new com.ehoadondientu.MyServiceSoapClient(
                    com.ehoadondientu.MyServiceSoapClient.EndpointConfiguration.MyServiceSoap
                );

                // Lấy tất cả dữ liệu từ DB lên giống như cũ của anh
                var (allData, _) = FetchInvoicesFromDb();

                // LỌC LẠI: Chỉ lấy những dòng hóa đơn nằm trong danh sách uids được chọn từ giao diện
                var data = allData.Where(x => x.Uid != null && uids.Any(id => id.Trim() == x.Uid.ToString().Trim())).ToList();

                StringBuilder log = new StringBuilder();
                int successCount = 0;

                foreach (var row in data)
                {
                    string tenCty = Vni2Unicode(row.B_ten_cty);
                    string diaChi = Vni2Unicode(row.B_dia_chi);
                    string nguoiMua = Vni2Unicode(row.B_nguoi_mua_hang);

                    // GIỮ NGUYÊN BẢN CHUỖI CHI TIẾT NHƯ HÀM CŨ, KHÔNG CẮT TÁCH BẰNG DẤU '@' NỮA
                    string chiTiet = Vni2Unicode(row.C_chitiethoadon);

                    // Đảm bảo định dạng ngày tháng lấy đúng Date như hàm cũ
                    DateTime date = Convert.ToDateTime(row.A_ngay_ct);
                    DateTime ngayChungTu = new DateTime(date.Year, date.Month, date.Day);

                    string result = await service.importHoadon_skyAsync(
                        "0300713227_import",
                        "Import@435466",
                        "", // strHoadonthaythe giống cũ
                        ngayChungTu,
                        row.A_ky_hieu_mau,
                        row.A_so_serial,
                        "", // strTaikhoanNganhang giống cũ
                        row.B_ma_so_thue,
                        tenCty,
                        diaChi,
                        row.B_email,
                        row.B_tel,
                        "", // strNganHangMua giống cũ
                        nguoiMua,
                        "TM/CK",
                        row.B_ma_KH,           // Truyền đúng Mã KH cũ
                        row.A_officialReceipt_no, // Truyền đúng Số chứng từ cũ
                        Convert.ToDouble(row.C_tratruoc), // Truyền đúng giá trị cũ
                        Convert.ToDouble(row.C_conlai),   // Truyền đúng giá trị cũ
                        chiTiet // Chuỗi chi tiết nguyên bản
                    );


                    if (result.Contains("thành công") || result.ToLower() == "ok")
                    {
                        successCount++;
                        UpdateStatus(row.Uid, 1);
                        log.Append($"<div class='text-success'>[Dòng {row.Uid}]: {result}</div>");
                    }
                    else
                    {
                        // ĐẶT BREAKPOINT TẠI ĐÂY: Để xem nội dung lỗi chính xác từ Vĩnh Hy trả về cho dòng này
                        log.Append($"<div class='text-danger'>[Dòng {row.Uid}]: Lỗi - {result}</div>");
                    }

                }

                return new JsonResult(new
                {
                    success = true,
                    message = $"Đã xử lý xong. Thành công: {successCount}/{data.Count}",
                    log = log.ToString()
                });
            }
            catch (Exception ex)
            {
                return new JsonResult(new
                {
                    success = false,
                    message = "Lỗi hệ thống: " + ex.Message
                });
            }
        }
        public IActionResult OnPostClearAll()
        {
            UpdateStatus(null, 1, clearAll: true);
            return RedirectToPage();
        }


        // --- DATA ACCESS & HELPERS ---
        private void LoadData()
        {
            var (data, total) = FetchInvoicesFromDb();
            DataList = data;
            TotalRecords = total;
        }

        private void LoadStatusDropdown()
        {
            StatusList = new List<SelectListItem>
            {
                new SelectListItem { Value = "0", Text = "Chưa xử lý (Mới tạo)" },
                new SelectListItem { Value = "1", Text = "Đã xử lý (Đã gửi)" }
            };
        }

        private (List<EInvoiceRow> list, int total) FetchInvoicesFromDb()
        {
            var list = new List<EInvoiceRow>();

            // Lấy đúng UserID (chuỗi số như "644") từ Claims của user đang đăng nhập
            string userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                            ?? User.FindFirst("UserID")?.Value
                            ?? "-1";

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));

            // Câu lệnh SQL chỉ giữ duy nhất bộ lọc CreateUser
            string sql = @"
            SELECT uid, A_ngay_ct, A_officialReceipt_no, B_nguoi_mua_hang, 
            B_ten_cty, B_dia_chi, B_ma_so_thue, B_email, B_tel, B_ma_KH,
            A_ky_hieu_mau, A_so_serial, C_tratruoc, C_conlai, C_chitiethoadon,
            C_tongtienthanhtoan 
            FROM AC_EInvoice 
            WHERE CreateUser = @userID and status = 0
            ORDER BY uid";

            //WHERE CreateUser = @userID and status = 0

            using var cmd = new SqlCommand(sql, conn);

            // Nạp duy nhất tham số userCode vào Command
            cmd.Parameters.AddWithValue("@userID", userId);

            conn.Open();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new EInvoiceRow
                {
                    Uid = Convert.ToDouble(reader["uid"]),
                    A_ngay_ct = Convert.ToDateTime(reader["A_ngay_ct"]),
                    A_officialReceipt_no = reader["A_officialReceipt_no"]?.ToString() ?? "",
                    B_nguoi_mua_hang = reader["B_nguoi_mua_hang"]?.ToString() ?? "",
                    B_ten_cty = reader["B_ten_cty"]?.ToString() ?? "",
                    B_dia_chi = reader["B_dia_chi"]?.ToString() ?? "",
                    B_ma_so_thue = reader["B_ma_so_thue"]?.ToString() ?? "",
                    B_email = reader["B_email"]?.ToString() ?? "",
                    B_tel = reader["B_tel"]?.ToString() ?? "",
                    B_ma_KH = reader["B_ma_KH"]?.ToString() ?? "",
                    A_ky_hieu_mau = reader["A_ky_hieu_mau"]?.ToString() ?? "",
                    A_so_serial = reader["A_so_serial"]?.ToString() ?? "",
                    C_tratruoc = reader["C_tratruoc"] != DBNull.Value ? Convert.ToDouble(reader["C_tratruoc"]) : 0,
                    C_conlai = reader["C_conlai"] != DBNull.Value ? Convert.ToDouble(reader["C_conlai"]) : 0,
                    C_chitiethoadon = reader["C_chitiethoadon"]?.ToString() ?? "",
                    C_tongtienthanhtoan = reader["C_tongtienthanhtoan"] != DBNull.Value ? Convert.ToDouble(reader["C_tongtienthanhtoan"]) : 0
                });
            }
            return (list, list.Count);
        }

        private void UpdateStatus(double? uid, int status, bool clearAll = false)
        {
            // ĐỔI TẠI ĐÂY: Lấy đúng UserID (chuỗi số như "644") từ Claims để khớp với CreateUser trong DB
            string userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                            ?? User.FindFirst("UserID")?.Value
                            ?? "-1";

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            string sql = clearAll
                ? "UPDATE AC_EInvoice SET status=@Status WHERE CreateUser=@UserId AND status=0"
                : "UPDATE AC_EInvoice SET status=@Status WHERE uid=@Uid";

            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Status", status);
            cmd.Parameters.AddWithValue("@UserId", userId);

            // Xử lý tham số @Uid an toàn
            if (uid.HasValue)
            {
                cmd.Parameters.AddWithValue("@Uid", uid.Value);
            }
            else
            {
                cmd.Parameters.AddWithValue("@Uid", DBNull.Value);
            }

            conn.Open();
            cmd.ExecuteNonQuery();
        }

        public static string Vni2Unicode(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            string kq = s;
            int[] u = { 7845, 7847, 7849, 7851, 7853, 226, 7843, 227, 7841, 7855, 7857, 7859, 7861, 7863, 259, 250, 249, 7911, 361, 7909, 7913, 7915, 7917, 7919, 7921, 432, 7871, 7873, 7875, 7877, 7879, 234, 233, 232, 7867, 7869, 7865, 7889, 7891, 7893, 7895, 7897, 7887, 245, 7885, 7899, 7901, 7903, 7905, 7907, 417, 237, 236, 7881, 297, 7883, 253, 7923, 7927, 7929, 7925, 273, 7844, 7846, 7848, 7850, 7852, 194, 7842, 195, 7840, 7854, 7856, 7858, 7860, 7862, 258, 218, 217, 7910, 360, 7908, 7912, 7914, 7916, 7918, 7920, 431, 7870, 7872, 7874, 7876, 7878, 202, 201, 200, 7866, 7868, 7864, 7888, 7890, 7892, 7894, 7896, 7886, 213, 7884, 7898, 7900, 7902, 7904, 7906, 416, 205, 204, 7880, 296, 7882, 221, 7922, 7926, 7928, 7924, 272, 225, 224, 244, 243, 242, 193, 192, 212, 211, 210 };
            string[] v = { "aá", "aà", "aå", "aã", "aä", "aâ", "aû", "aõ", "aï", "aé", "aè", "aú", "aü", "aë", "aê", "uù", "uø", "uû", "uõ", "uï", "öù", "öø", "öû", "öõ", "öï", "ö", "eá", "eà", "eå", "eã", "eä", "eâ", "eù", "eø", "eû", "eõ", "eï", "oá", "oà", "oå", "oã", "oä", "oû", "oõ", "oï", "ôù", "ôø", "ôû", "ôõ", "ôï", "ô", "í", "ì", "æ", "ó", "ò", "yù", "yø", "yû", "yõ", "î", "ñ", "AÁ", "AÀ", "AÅ", "AÃ", "AÄ", "AÂ", "AÛ", "AÕ", "AÏ", "AÉ", "AÈ", "AÚ", "AÜ", "AË", "AÊ", "UÙ", "UØ", "UÛ", "UÕ", "UÏ", "ÖÙ", "ÖØ", "ÖÛ", "ÖÕ", "ÖÏ", "Ö", "EÁ", "EÀ", "EÅ", "EÃ", "EÄ", "EÂ", "EÙ", "EØ", "EÛ", "EÕ", "EÏ", "OÁ", "OÀ", "OÅ", "OÃ", "OÄ", "OÛ", "OÕ", "OÏ", "ÔÙ", "ÔØ", "ÔÛ", "ÔÕ", "ÔÏ", "Ô", "Í", "Ì", "Æ", "Ó", "Ò", "YÙ", "YØ", "YÛ", "YÕ", "Î", "Ñ", "aù", "aø", "oâ", "où", "oø", "AÙ", "AØ", "OÂ", "OÙ", "OØ" };

            for (int i = 0; i < v.Length; i++)
                kq = kq.Replace(v[i], ((char)u[i]).ToString());
            return kq;
        }
    }

    // --- CLASS ĐIỀU KIỆN LỌC ---
    public class FilterCriteria
    {
        public string StatusID { get; set; }
        public string ObjectId { get; set; }
    }

    public class EInvoiceRow
    {
        public double Uid { get; set; }
        public DateTime A_ngay_ct { get; set; }
        public string A_officialReceipt_no { get; set; }
        public string B_nguoi_mua_hang { get; set; }
        public string B_ten_cty { get; set; }
        public string B_dia_chi { get; set; }
        public string B_ma_so_thue { get; set; }
        public string B_email { get; set; }
        public string B_tel { get; set; }
        public string B_ma_KH { get; set; }
        public string A_ky_hieu_mau { get; set; }
        public string A_so_serial { get; set; }
        public double C_tratruoc { get; set; }
        public double C_conlai { get; set; }
        public string C_chitiethoadon { get; set; }
        public double C_tongtienthanhtoan { get; set; }

        // Mở rộng các thuộc tính động để hiển thị map đúng data lên Table ở file Index.cshtml
        public string Code => A_officialReceipt_no;
        public string Name => B_ten_cty;
        public DateTime CreatedDate => A_ngay_ct;
        public string StatusName => "Chưa xử lý";
        public double ID => Uid;
    }
}