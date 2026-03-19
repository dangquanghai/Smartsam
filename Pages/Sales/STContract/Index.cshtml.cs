using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;
using SmartSam.Helpers;
using SmartSam.Services.Interfaces; 
using SmartSam.Services;
using Microsoft.AspNetCore.Mvc.Rendering;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace SmartSam.Pages.Sales.STContract
{
    public class IndexModel : BasePageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly ISecurityService _securityService; 
        private readonly PermissionService _permissionService;

        // ID của chức năng trong bảng SYS_Function (Bạn có thể điều chỉnh số 1 này cho đúng DB)
        private const int FUNCTION_ID = 5;

        public IndexModel(ISecurityService securityService, IConfiguration config, ILogger<IndexModel> logger, PermissionService permissionService) : base(config)
        {
            _securityService = securityService;
            _logger = logger;
            _permissionService = permissionService;
        }

        // Đối tượng chứa quyền của trang này (Dùng cho Giai đoạn 2)
        // public PagePermissions PagePerm { get; set; }
        public PagePermissions PagePerm { get; private set; } = new();

        [BindProperty(SupportsGet = true)]
        public STContractFilter Filter { get; set; } = new();
        public List<ContractRow> Contracts { get; set; } = new List<ContractRow>();
        public List<SelectListItem> ApartmentList { get; set; }
        public List<SelectListItem> ContractStatusList { get; set; }
        public List<SelectListItem> CompanyList { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> AgentCompanyList { get; set; } = new List<SelectListItem>();

        public int DefaultPageSize => _config.GetValue<int>("AppSettings:DefaultPageSize", 25);

        // ==========================================
        // 1. GIAI ĐOẠN LOAD TRANG (GET)
        // ==========================================
        
        public void OnGet()
        {
            int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");
            PagePerm.AllowedNos = _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, 1);// mặc định trạng thái Reser

            // Khởi tạo các giá trị mặc định cho Filter
            Filter.StatusID ??= 1;

            // Load dữ liệu cho các Dropdown
            LoadContractStatus();
            LoadApartments();
            CompanyList = LoadSelect2("CM_Company", "CompanyID", "CompanyName");
            AgentCompanyList = LoadSelect2("CM_Company", "CompanyID", "CompanyName");

            ViewData["DefaultPageSize"] = DefaultPageSize;
        }

        // ==========================================
        // 2. XỬ LÝ SEARCH AJAX (POST)
        // ==========================================

        public IActionResult OnPostSearch([FromBody] SearchRequest request)
        {
            try
            {
                // 1. Lấy thông tin Role của người dùng hiện tại
                int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");

                // 2. Gọi Database lấy dữ liệu thô
                var (contracts, totalRecords) = SearchContracts(request);

                // 3. Sử dụng SecurityService để "phán quyết" quyền cho từng dòng dữ liệu
                var dataWithActions = contracts.Select(c => {

                    // Lấy tập quyền thực tế cho bản ghi này (đã giao thoa Quyền + Trạng thái)
                    var effectivePerms = _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, c.StatusID);

                    return new
                    {
                        data = c,
                        actions = new
                        {
                            // Chế độ truy cập khi click vào link (Dán link mode=edit cũng vô hiệu nếu không có mã 4)
                            canAccess = effectivePerms.Contains(2), // Chỉ cần có quyền View (2)
                            accessMode = effectivePerms.Contains(4) ? "edit" : "view",

                            // Các nút chức năng dựa trên danh sách mã quyền thực tế trả về từ Service
                            canView = effectivePerms.Contains(2),
                            canEdit = effectivePerms.Contains(4),
                            canEditMember = effectivePerms.Contains(5),
                            canCancel = effectivePerms.Contains(6),
                            canChangeStatus = effectivePerms.Contains(7),
                            canAdjustDate = effectivePerms.Contains(8),
                            canCopy = effectivePerms.Contains(9),
                            canCreateDeposit = effectivePerms.Contains(10), // Thêm dòng này
                            canCheckIn = effectivePerms.Contains(11),      // Thêm dòng này
                            canCheckOut = effectivePerms.Contains(12)      // Thêm dòng này
                        }
                    };
                });

                return new JsonResult(new
                {
                    success = true,
                    data = dataWithActions,
                    total = totalRecords,
                    page = request.Page,
                    pageSize = request.PageSize,
                    totalPages = (int)Math.Ceiling((double)totalRecords / request.PageSize)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnPostSearch");
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }
        // ==========================================
        // 3. CÁC HÀM BỔ TRỢ (HELPER)
        // ==========================================
        private PagePermissions GetUserPermissions()
        {
            bool isAdmin = User.FindFirst("IsAdminRole")?.Value == "True";
            int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "0");

            // 1. Khởi tạo đối tượng PagePermissions mới
            var permsObj = new PagePermissions();

            if (isAdmin)
            {
                // Admin: Gán danh sách quyền giả lập
                permsObj.AllowedNos = Enumerable.Range(1, 20).ToList();
            }
            else
            {
                // 2. Lấy danh sách List<int> từ Service và gán vào thuộc tính AllowedNos của Object
                permsObj.AllowedNos = _permissionService.GetPermissionsForPage(roleId, FUNCTION_ID);
            }

            // 3. Trả về đối tượng (Object) chứa danh sách đó
            return permsObj;
        }

        private (List<ContractRow> contracts, int totalRecords) SearchContracts(SearchRequest request)
        {
            var (CheckInFrom, CheckInTo) = Helper.ParseDateRange(request.DateRangeIn);
            var (CheckOutFrom, CheckOutTo) = Helper.ParseDateRange(request.DateRangeOut);

            var contracts = new List<ContractRow>();
            int totalRecords = 0;

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand("SAL_SearchSTContract", conn);
            cmd.CommandType = CommandType.StoredProcedure;

            // Parameters
            cmd.Parameters.Add("@StatusID", SqlDbType.Int).Value = request.StatusID ?? (object)DBNull.Value;
            cmd.Parameters.Add("@ApartmentId", SqlDbType.Int).Value = request.ApartmentId ?? (object)DBNull.Value;
            cmd.Parameters.Add("@CompanyId", SqlDbType.Int).Value = request.CompanyId ?? (object)DBNull.Value;
            cmd.Parameters.Add("@AgentCompanyId", SqlDbType.Int).Value = request.AgentCompanyId ?? (object)DBNull.Value;
            cmd.Parameters.Add("@CheckInFrom", SqlDbType.Date).Value = CheckInFrom ?? (object)DBNull.Value;
            cmd.Parameters.Add("@CheckInTo", SqlDbType.Date).Value = CheckInTo ?? (object)DBNull.Value;
            cmd.Parameters.Add("@CheckOutFrom", SqlDbType.Date).Value = CheckOutFrom ?? (object)DBNull.Value;
            cmd.Parameters.Add("@CheckOutTo", SqlDbType.Date).Value = CheckOutTo ?? (object)DBNull.Value;
            cmd.Parameters.Add("@ContractNo", SqlDbType.NVarChar, 50).Value = (object)request.ContractNo ?? DBNull.Value;
            cmd.Parameters.Add("@CustomerName", SqlDbType.NVarChar, 100).Value = (object)request.CustomerName ?? DBNull.Value;
            cmd.Parameters.AddWithValue("@PageNumber", request.Page);
            cmd.Parameters.AddWithValue("@PageSize", request.PageSize);

            var totalParam = cmd.Parameters.Add("@TotalRecords", SqlDbType.Int);
            totalParam.Direction = ParameterDirection.Output;

            conn.Open();
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                contracts.Add(new ContractRow
                {
                    ContractID = Convert.ToInt32(reader["ContractID"]),
                    ContractNo = reader["ContractNo"].ToString(),
                    ApartmentNo = reader["ApartmentNo"].ToString(),
                    CustomerName = reader["CustomerName"].ToString(),
                    CompanyName = reader["CompanyName"].ToString(),
                    ContractFromDate = Convert.ToDateTime(reader["ContractFromDate"]),
                    ContractToDate = Convert.ToDateTime(reader["ContractToDate"]),
                    StatusID = Convert.ToInt32(reader["StatusID"]),
                    StatusName = reader["StatusName"].ToString()
                });
            }
            reader.Close();

            totalRecords = totalParam.Value != DBNull.Value ? Convert.ToInt32(totalParam.Value) : 0;
            return (contracts, totalRecords);
        }

        public IActionResult OnPostCancel([FromBody] CancelRequest req)
        {
            // Thực hiện gọi SP Cancel tại đây
            return new JsonResult(new { success = true, message = "Contract cancelled successfully" });
        }

        private void LoadContractStatus()
        {
            var list = new List<(int Id, string Name)>();
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand("SELECT StatusID, StatusName FROM CM_ContractStatus ORDER BY StatusID", conn);
            conn.Open();
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) { list.Add((Convert.ToInt32(rd[0]), rd[1].ToString())); }

            ContractStatusList = Helper.BuildIntSelectList(list, x => x.Id, x => x.Name, showAll: false);
        }

        private void LoadApartments()
        {
            var list = new List<(int Id, string Name)>();
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand("SELECT ApmtID, ApartmentNo FROM AM_Apmt WHERE ExistFrom <= GETDATE() AND ExistTo >= GETDATE() ORDER BY ApartmentNo", conn);
            conn.Open();
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) { list.Add((Convert.ToInt32(rd[0]), rd[1].ToString())); }

            ApartmentList = Helper.BuildIntSelectList(list, x => x.Id, x => x.Name, showAll: true);
        }
    }

    // --- CÁC CLASS DỮ LIỆU ---
    public class SearchRequest
    {
        public int? StatusID { get; set; }
        public int? ApartmentId { get; set; }
        public string? DateRangeIn { get; set; }
        public string? DateRangeOut { get; set; }
        public int? CompanyId { get; set; }
        public int? AgentCompanyId { get; set; }
        public string? ContractNo { get; set; }
        public string? CustomerName { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
    }

    public class ContractRow
    {
        public int ContractID { get; set; }
        public string ContractNo { get; set; }
        public string ApartmentNo { get; set; }
        public string CustomerName { get; set; }
        public string CompanyName { get; set; }
        public DateTime ContractFromDate { get; set; }
        public DateTime ContractToDate { get; set; }
        public int StatusID { get; set; }
        public string StatusName { get; set; }
        public string ContractFromDateDisplay => ContractFromDate.ToString("dd/MM/yyyy");
        public string ContractToDateDisplay => ContractToDate.ToString("dd/MM/yyyy");
    }

    public class STContractFilter
    {
        public int? StatusID { get; set; }
        public int? ApartmentId { get; set; }

        // Đảm bảo có 2 dòng này bên trong class Filter
        public string? DateRangeIn { get; set; }
        public string? DateRangeOut { get; set; }

        public int? CompanyId { get; set; }
        public int? AgentCompanyId { get; set; }

        public string? ContractNo { get; set; }
        public string? CustomerName { get; set; }
    }

    public class CancelRequest { public int ContractId { get; set; } }
}