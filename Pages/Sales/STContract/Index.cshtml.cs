using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.Collections.Generic;
using SmartSam.Helpers;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Data;
using SmartSam.Services;
using System.Data.SqlTypes;
namespace SmartSam.Pages.Sales.STContract
{
    public class IndexModel : BasePageModel
    {
        
        private readonly ILogger<IndexModel> _logger;
        
        private readonly PermissionService _permissionService;
        public IndexModel(IConfiguration config, ILogger<IndexModel> logger, PermissionService permissionService): base(config)
        {
            _logger = logger;
            _permissionService = permissionService;
        }
        // Đối tượng chứa quyền của trang này
        public PagePermissions PagePerm { get; set; }

        [BindProperty(SupportsGet = true)]
        public string DateRangeIn { get; set; }

        [BindProperty(SupportsGet = true)]
        public string DateRangeOut { get; set; }
        public int DefaultPageSize => _config.GetValue<int>("AppSettings:DefaultPageSize", 25);
        public List<SelectListItem> ApartmentList { get; set; }
        public List<SelectListItem> ContractStatusList { get; set; }
        public List<SelectListItem> CompanyList { get; set; } = new List<SelectListItem>();
        public List<SelectListItem> AgentCompanyList { get; set; } = new List<SelectListItem>();

        // 🔹 SEARCH CONDITION
        [BindProperty(SupportsGet = true)]
        public STContractFilter Filter { get; set; } = new();

        public List<ContractRow> Contracts { get; set; } = new();



        public void OnGet()
        {
            // --- BẮT ĐẦU PHẦN PHÂN QUYỀN MỚI ---

            // 1. Kiểm tra quyền Admin từ Claim trước
            bool isAdmin = User.FindFirst("IsAdminRole")?.Value == "True";

            // 2. Lấy RoleID từ Claims
            var roleClaim = User.FindFirst("RoleID")?.Value;
            int roleId = int.Parse(roleClaim ?? "0");

            // Giả sử ID của phân hệ Long Term Contract trong bảng SYS_Function là 1
            int functionID = 1;

            PagePerm = new PagePermissions();

            if (isAdmin)
            {
                // Nếu là Admin, gán một danh sách số quyền giả lập đủ lớn (ví dụ từ 1-10) 
                // để tất cả các nút Add, Edit, Delete trên giao diện đều hiện ra
                PagePerm.AllowedNos = Enumerable.Range(1, 10).ToList();
            }
            else
            {
                // Nếu không phải Admin, lấy chuỗi quyền thực tế (2,3,4,5...) từ DB
                PagePerm.AllowedNos = _permissionService.GetPermissionsForPage(roleId, functionID);
            }

            // --- KẾT THÚC PHẦN PHÂN QUYỀN ---

            // Các logic load dữ liệu cũ của bạn
            Filter.StatusID ??= 1;
            LoadContractStatus();
            LoadApartments();

            CompanyList = LoadSelect2("CM_Company", "CompanyID", "CompanyName");
            AgentCompanyList = LoadSelect2("CM_Company", "CompanyID", "CompanyName");

            ViewData["DefaultPageSize"] = DefaultPageSize;
        }
        // AJAX Search Handler
        public IActionResult OnPostSearch([FromBody] SearchRequest request)
        {
            int functionId = 1;
            int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "0");
            var rawNos = _permissionService.GetPermissionsForPage(roleId, functionId);

            var (contracts, totalRecords) = SearchContracts(request);

            var dataWithActions = contracts.Select(c => {
                // Kiểm tra quyền Edit và View thô
                bool hasEditPerm = rawNos.Contains(4) && (c.StatusID == 1 || c.StatusID == 2);
                bool hasViewPerm = rawNos.Contains(2);

                return new
                {
                    data = c,
                    actions = new
                    {
                        // Logic mới: Có quyền vào chi tiết nếu có Edit HOẶC View
                        canAccess = hasEditPerm || hasViewPerm,
                        // Ưu tiên Edit nếu có cả 2 quyền
                        accessMode = hasEditPerm ? "edit" : "view",

                        canCancel = rawNos.Contains(6) && (c.StatusID == 1),
                        canToLiving = rawNos.Contains(7) && (c.StatusID == 2)
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

        private (List<ContractRow> contracts, int totalRecords) SearchContracts(SearchRequest request)
        {

            var (CheckInFrom, CheckInTo) = Helper.ParseDateRange(request.DateRangeIn);
            var (CheckOutFrom, CheckOutTo) = Helper.ParseDateRange(request.DateRangeOut);

            var contracts = new List<ContractRow>();
            int totalRecords = 0;

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand("SAL_SearchSTContract", conn);
            cmd.CommandType = CommandType.StoredProcedure;

            // Xử lý từng parameter rõ ràng
            // int? parameters
            cmd.Parameters.Add("@StatusID", SqlDbType.Int).Value =
                request.StatusID.HasValue ? (object)request.StatusID.Value : DBNull.Value;

            cmd.Parameters.Add("@ApartmentId", SqlDbType.Int).Value =
                request.ApartmentId.HasValue ? (object)request.ApartmentId.Value : DBNull.Value;

            cmd.Parameters.Add("@CompanyId", SqlDbType.Int).Value =
                request.CompanyId.HasValue ? (object)request.CompanyId.Value : DBNull.Value;

            cmd.Parameters.Add("@AgentCompnayId", SqlDbType.Int).Value =
                request.AgentCompanyId.HasValue ? (object)request.AgentCompanyId.Value : DBNull.Value;

            
            // DateTime? parameters  
            cmd.Parameters.Add("@CheckInFrom", SqlDbType.Date).Value =
               CheckInFrom.HasValue ? CheckInFrom.Value : DBNull.Value;

            cmd.Parameters.Add("@CheckInTo", SqlDbType.Date).Value =
               CheckInTo.HasValue ? CheckInTo.Value : DBNull.Value;

            cmd.Parameters.Add("@CheckOutFrom", SqlDbType.Date).Value =
              CheckOutFrom.HasValue ? CheckOutFrom.Value : DBNull.Value;

            cmd.Parameters.Add("@CheckOutTo", SqlDbType.Date).Value =
                CheckOutTo.HasValue ? CheckOutTo.Value : DBNull.Value;

            // string parameters
            cmd.Parameters.Add("@ContractNo", SqlDbType.NVarChar, 50).Value =
                !string.IsNullOrWhiteSpace(request.ContractNo) ? (object)request.ContractNo : DBNull.Value;

            cmd.Parameters.Add("@CustomerName", SqlDbType.NVarChar, 100).Value =
                !string.IsNullOrWhiteSpace(request.CustomerName) ? (object)request.CustomerName : DBNull.Value;

            // Non-nullable parameters
            cmd.Parameters.AddWithValue("@PageNumber", request.Page);
            cmd.Parameters.AddWithValue("@PageSize", request.PageSize);

            // Output parameter
            var totalParam = cmd.Parameters.Add("@TotalRecords", SqlDbType.Int);
            totalParam.Direction = ParameterDirection.Output;

            conn.Open();
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                contracts.Add(new ContractRow
                {
                    ContractID = Convert.ToInt32(reader[0]), // Dùng Convert
                    ContractNo = Convert.ToString(reader[1]),
                    ApartmentNo = Convert.ToString(reader[2]),
                    CustomerName = Convert.ToString(reader[3]),
                    CompanyName = Convert.ToString(reader[4]),
                    ContractFromDate = Convert.ToDateTime(reader[5]),
                    ContractToDate = Convert.ToDateTime(reader[6]),
                    StatusID = Convert.ToInt32(reader[7]),
                    StatusName = Convert.ToString(reader[8])
                });
            }

            reader.Close();

            if (totalParam.Value != DBNull.Value)
            {
                totalRecords = Convert.ToInt32(totalParam.Value);
            }

            return (contracts, totalRecords);
        }

        public IActionResult OnPostCancel([FromBody] CancelRequest req)
        {
            // TODO: call stored procedure CancelContract(@ContractID)
            return new JsonResult(new
            {
                success = true,
                message = "Contract cancelled successfully"
            });
        }

        private void LoadContractStatus()
        {
            var list = new List<(int Id, string Name)>();

            using var conn = new SqlConnection(
                _config.GetConnectionString("DefaultConnection"));

            using var cmd = new SqlCommand(@"
            select StatusID , StatusName  from CM_ContractStatus
            ORDER BY StatusID", conn);

            conn.Open();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add((
                    Convert.ToInt32(rd[0]),  // 👈 CHỐT Ở ĐÂY
                    Convert.ToString(rd[1])!
                ));
            }

            ContractStatusList = Helper.BuildIntSelectList(
                list,
                x => x.Id,
                x => x.Name,
                showAll: false
            );
        }
        private void LoadApartments()
        {
            var list = new List<(int Id, string Name)>();

            using var conn = new SqlConnection(
                _config.GetConnectionString("DefaultConnection"));

            using var cmd = new SqlCommand(@"
            SELECT ApmtID, ApartmentNo
            FROM AM_Apmt
            WHERE ExistFrom <= GETDATE()
            AND ExistTo >= GETDATE()
            ORDER BY ApartmentNo", conn);

            conn.Open();
            using var rd = cmd.ExecuteReader();
            while (rd.Read())
            {
                list.Add((
                    Convert.ToInt32(rd[0]),  // 👈 CHỐT Ở ĐÂY
                    Convert.ToString(rd[1])!
                ));
            }

            ApartmentList = Helper.BuildIntSelectList(
                list,
                x => x.Id,
                x => x.Name,
                showAll: true
            );
        }
    }

    // Request model cho AJAX search
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

    // Mở rộng ContractRow để chứa thêm thông tin
    public class ContractRow
    {
        public int ContractID { get; set; }
        public string ContractNo { get; set; }
        public string ApartmentNo { get; set; }
        public string CustomerName { get; set; }
        public string CompanyName { get; set; }
        public string AgentCompanyName { get; set; }
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

        public string? DateRangeIn { get; set; }
        public string? DateRangeOut { get; set; }

        public int? CompanyId { get; set; }
        public int? AgentCompanyId { get; set; }

        public string? ContractNo { get; set; }
        public string? CustomerName { get; set; }
    }
    public class CancelRequest
    {
        public int ContractId { get; set; }
    }
    

}
