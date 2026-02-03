using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Data;
using Microsoft.Data.SqlClient; // Hoặc System.Data.SqlClient tùy project của bạn
using SmartSam.Helpers;
using SmartSam.Services;
using Microsoft.AspNetCore.Authorization;
using static System.Runtime.InteropServices.JavaScript.JSType;


namespace SmartSam.Pages.Infor.PeriodOccupied
{

    public class IndexModel : PageModel
    {
        private readonly IConfiguration _configuration;
        // Thêm service phân quyền của bạn
        private readonly PermissionService _permissionService;
        public IndexModel(IConfiguration configuration, PermissionService permissionService)
        {
            _configuration = configuration;
            _permissionService = permissionService;
        }
        // Khai báo thuộc tính quyền để dùng ngoài HTML (ẩn hiện nút nếu cần)
        public PagePermissions PagePerm { get; set; }

        // Các thuộc tính dùng cho Binding và hiển thị UI
        [BindProperty(SupportsGet = true)]
        public string DateRange { get; set; }

        [BindProperty(SupportsGet = true)]
        public string[] SelectedStatuses { get; set; }

        public List<DateTime> DaysList { get; set; } = new List<DateTime>();
        public List<RoomStatusViewModel> Rooms { get; set; } = new();

        // Thống kê (Tương đương lblStat trong VB6)
        public long TotalRoomDays { get; set; }
        public long OccupiedDays { get; set; }

        // Danh sách để đổ vào Combo box
        public List<OccTypeViewModel> OccTypeList { get; set; } = new();

        public IActionResult OnGet() // Chuyển sang IActionResult để có thể Redirect nếu không có quyền
        {
            DateTime startDate;
            DateTime toDate;
            string occTypeParam;
            // 1. PHÂN QUYỀN (FunctionID trang này giả sử là 99)
            bool isAdmin = User.FindFirst("IsAdminRole")?.Value == "True";
            var roleClaim = User.FindFirst("RoleID")?.Value;
            int roleId = int.Parse(roleClaim ?? "0");
            int functionID = 53;

            PagePerm = new PagePermissions();
            if (isAdmin) PagePerm.AllowedNos = Enumerable.Range(1, 10).ToList();
            else PagePerm.AllowedNos = _permissionService.GetPermissionsForPage(roleId, functionID);

            if (PagePerm.AllowedNos == null || !PagePerm.AllowedNos.Any()) return RedirectToPage("/AccessDenied");


            var rawNos = _permissionService.GetPermissionsForPage(roleId, functionID);
            bool canMakeSTRS = rawNos.Contains(3); // Giả định ID 3 tương ứng với quyền MakeSTRS

            // Đẩy vào ViewData để dùng ở bất cứ đâu trong View
            ViewData["CanMakeSTRS"] = canMakeSTRS;


            // 2. XỬ LÝ NGÀY THÁNG
            if (string.IsNullOrEmpty(DateRange))
            {
                // Mặc định 30 ngày từ hôm nay
                startDate = DateTime.Today;
                toDate = startDate.AddDays(30);
                DateRange = $"{startDate:dd/MM/yyyy} - {toDate:dd/MM/yyyy}";
            }
            else
            {
                try
                {
                    var dates = DateRange.Split(" - ");
                    startDate = DateTime.ParseExact(dates[0], "dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
                    toDate = DateTime.ParseExact(dates[1], "dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture);
                }
                catch
                {
                    startDate = DateTime.Today;
                    toDate = startDate.AddDays(30);
                }
            }

            // 1. Khởi tạo danh sách cho Dropdown và xử lý mặc định cho SelectedStatuses (như đã làm ở bước trước)
            LoadOccTypes();

            // 2. Biện luận để tạo chuỗi occTypeParam

            if (SelectedStatuses == null || !SelectedStatuses.Any())
            {
                // Trường hợp lần đầu vào trang (null) hoặc người dùng chọn "Tất cả" (0)
                occTypeParam = "0123456789";
            }
            else
            {
                // Trường hợp người dùng chọn các mục cụ thể (ví dụ: chọn 1 và 2)
                // Kết quả sẽ là chuỗi "1,2" (hoặc tùy định dạng hàm LoadGridData của bạn yêu cầu)
                occTypeParam = string.Join(",", SelectedStatuses);
            }

            // TẠO DANH SÁCH NGÀY ĐỂ VẼ CỘT TRÊN HTML
            DaysList.Clear();
            for (var date = startDate; date <= toDate; date = date.AddDays(1))
            {
                DaysList.Add(date);
                if (DaysList.Count > 100) break; // Giới hạn an toàn để không treo trình duyệt
            }
            // GỌI HÀM LOAD DỮ LIỆU (Đảm bảo tham số truyền vào là startDate, toDate)
            LoadGridData(startDate, toDate, occTypeParam);
            return Page();
        }
        private void LoadOccTypes()
        {
            string connectionString = _configuration.GetConnectionString("DefaultConnection");
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                // Sửa SQL: Đảm bảo dòng UNION đầu tiên có cùng số lượng và kiểu dữ liệu với bảng chính
                // Thay 'white' và 'black' bằng mã màu bạn muốn cho phòng trống
            string sql = @"
            SELECT 
            CAST(0 AS int) as OccTypeID, 
            CAST('NO' AS varchar(2)) as StatusName, 
            CAST('Not Occupied' AS varchar(15)) as OccTypeName, 
            CAST('#ffffff' AS varchar(20)) as BackColor, 
            CAST('#000000' AS varchar(20)) as ForeColor
            UNION ALL
            SELECT 
            OccTypeID, 
            StatusName, 
            OccTypeName, 
            CAST(BackColor AS varchar(20)), 
            CAST(ForeColor AS varchar(20)) 
            FROM AM_OccType";


                SqlCommand cmd = new SqlCommand(sql, conn);
                conn.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    OccTypeList = new List<OccTypeViewModel>(); // Khởi tạo list
                    while (reader.Read())
                    {
                        OccTypeList.Add(new OccTypeViewModel
                        {
                            ID = reader["OccTypeID"].ToString(),
                            Name = reader["OccTypeName"].ToString(),
                            StatusName = reader["StatusName"].ToString(),
                            // Kiểm tra nếu DB lưu mã màu trực tiếp (ví dụ #FF0000) thì dùng luôn
                            BackColor = TranslateWin32ColorToHex(reader["BackColor"]),
                            ForeColor = TranslateWin32ColorToHex(reader["ForeColor"])
                        });
                    }
                }
            }
        }
        private void LoadGridData(DateTime fromDate, DateTime toDate, string occType)
        {
            string connectionString = _configuration.GetConnectionString("DefaultConnection");
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                SqlCommand cmd = new SqlCommand("SAL_ApmtOCCPeriod_New", conn);
                cmd.CommandType = CommandType.StoredProcedure;
                cmd.Parameters.AddWithValue("@FromDate", fromDate);
                cmd.Parameters.AddWithValue("@ToDate", toDate);
                cmd.Parameters.AddWithValue("@OccType", occType); // Truyền chuỗi ví dụ "012"

                conn.Open();
                using (SqlDataReader reader = cmd.ExecuteReader())
                {
                    // Tương đương logic Do While Not StatusSet.EOF trong VB6
                    var tempRooms = new List<RawRoomData>();
                    while (reader.Read())
                    {
                        tempRooms.Add(new RawRoomData
                        {
                            ApartmentNo = reader["ApartmentNo"].ToString(),
                            DoubleBeds = reader["DoubleBeds"] != DBNull.Value ? Convert.ToInt32(reader["DoubleBeds"]) : 0,
                            IsRemodeling = reader["IsRemodeling"] != DBNull.Value && Convert.ToBoolean(reader["IsRemodeling"]),
                            IsRefresh = reader["IsRefresh"] != DBNull.Value && Convert.ToBoolean(reader["IsRefresh"]),
                            TeamberFloor = reader["TeamberFloor"] != DBNull.Value && Convert.ToBoolean(reader["TeamberFloor"]),
                            // Kiểm tra null cẩn thận trước khi gán
                            FromDate = reader["FromDate"] != DBNull.Value ? Convert.ToDateTime(reader["FromDate"]) : null,
                            ToDate = reader["ToDate"] != DBNull.Value ? Convert.ToDateTime(reader["ToDate"]) : null,
                            StatusName = reader["StatusName"]?.ToString(),
                            BackColor = reader["BackColor"],
                            ForeColor = reader["ForeColor"]
                        });
                    }
                    // Nhóm dữ liệu theo phòng (Group By ApartmentNo)
                    // 2. Bây giờ GroupBy sẽ không còn lỗi .Value nữa
                    Rooms = tempRooms.GroupBy(x => x.ApartmentNo).Select(g => new RoomStatusViewModel
                    {
                        ApartmentNo = g.Key,
                        DoubleBeds = g.First().DoubleBeds,
                        IsRemodeling = g.First().IsRemodeling,
                        IsRefresh = g.First().IsRefresh,
                        TeamberFloor = g.First().TeamberFloor,
                        Occupancies = g.Where(o => o.FromDate.HasValue).Select(o => new OccupancyDetail
                        {
                            FromDate = o.FromDate.Value,
                            ToDate = o.ToDate.Value,
                            StatusName = o.StatusName,
                            BackColorHex = TranslateWin32ColorToHex(o.BackColor),
                            ForeColorHex = TranslateWin32ColorToHex(o.ForeColor)
                        }).ToList()
                    }).OrderBy(x => x.ApartmentNo).ToList();
                }
            }

            // Tính toán thống kê
            TotalRoomDays = Rooms.Count * DaysList.Count;
            OccupiedDays = Rooms.Sum(r => r.Occupancies.Sum(o => (o.ToDate - o.FromDate).Days + 1));
        }

        // Hàm Helper chuyển đổi màu Win32 (VB6) sang HEX (Web)
        // Đã bỏ static để không báo lỗi trong PageModel
        public string TranslateWin32ColorToHex(object win32Color)
        {
            try
            {
                if (win32Color == null || win32Color == DBNull.Value) return "#ffffff";
                long color = Convert.ToInt64(win32Color);
                int r = (int)(color & 0xFF);
                int g = (int)(color >> 8 & 0xFF);
                int b = (int)(color >> 16 & 0xFF);
                return string.Format("#{0:X2}{1:X2}{2:X2}", r, g, b);
            }
            catch { return "#ffffff"; }
        }
    }

    // Các class bổ trợ dữ liệu
    public class RoomStatusViewModel
    {
        public string ApartmentNo { get; set; }
        public int DoubleBeds { get; set; }
        public bool IsRemodeling { get; set; }
        public bool IsRefresh { get; set; }
        public bool TeamberFloor { get; set; }
        public List<OccupancyDetail> Occupancies { get; set; } = new();

        // Logic đặc biệt từ VB6: underline cho một số phòng nhất định
        public bool IsUnderlined => (new[] { "B08-11", "B11-10", "B11-11", "B07-03" }).Contains(ApartmentNo);

        // Logic màu sắc text cho Unit No
        public string TextColorClass
        {
            get
            {
                var redRooms = new[] { "B10-10", "C12-09", "D03-13", "B08-03", "B09-06", "D11-01", "B09-04", "B13-01", "C05-05", "C06-09" };
                if (redRooms.Contains(ApartmentNo)) return "text-danger";
                return TeamberFloor ? "text-primary" : "text-muted";
            }
        }
    }

    public class OccupancyDetail
    {
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public string StatusName { get; set; }
        public string BackColorHex { get; set; }
        public string ForeColorHex { get; set; }
    }
    public class OccTypeViewModel
    {
        public string ID { get; set; }
        public string Name { get; set; } // Đây là OccTypeName
        public string StatusName { get; set; }
        public string BackColor { get; set; }
        public string ForeColor { get; set; }
    }
    public class RawRoomData
    {
        public string ApartmentNo { get; set; }
        public int DoubleBeds { get; set; }
        public bool IsRemodeling { get; set; }
        public bool IsRefresh { get; set; }
        public bool TeamberFloor { get; set; }
        public DateTime? FromDate { get; set; } // Cho phép null
        public DateTime? ToDate { get; set; }   // Cho phép null
        public string StatusName { get; set; }
        public object BackColor { get; set; }
        public object ForeColor { get; set; }
    }
}