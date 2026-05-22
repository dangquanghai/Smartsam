using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.SqlClient;
using System.Data;
using SmartSam.Helpers;
using SmartSam.Services.Interfaces;
using SmartSam.Services;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace SmartSam.Pages.Admin.Employee
{
    public class IndexModel : BasePageModel
    {
        private readonly ILogger<IndexModel> _logger;
        private readonly ISecurityService _securityService;
        private readonly PermissionService _permissionService;

        // ID của chức năng Employee trong bảng SYS_Function (Anh điều chỉnh lại ID cho đúng DB)
        private const int FUNCTION_ID = 18;

        public IndexModel(ISecurityService securityService, IConfiguration config, ILogger<IndexModel> logger, PermissionService permissionService) : base(config)
        {
            _securityService = securityService;
            _logger = logger;
            _permissionService = permissionService;
        }

        public PagePermissions PagePerm { get; private set; } = new();

        [BindProperty(SupportsGet = true)]
        public EmployeeFilter Filter { get; set; } = new();

        public List<SelectListItem> DepartmentList { get; set; }
        
        public List<EmployeeRow> Employees { get; set; } = new List<EmployeeRow>();
        public int DefaultPageSize => _config.GetValue<int>("AppSettings:DefaultPageSize", 25);

        // ==========================================
        // 1. GIAI ĐOẠN LOAD TRANG (GET)
        // ==========================================
        public void OnGet()
        {
            int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");

            // Lấy quyền mặc định cho trang khi load (ví dụ trạng thái 1 là Active)
            PagePerm.AllowedNos = _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, 1);

            // Load dữ liệu cho Dropdown Department
            LoadDepartments();

            ViewData["DefaultPageSize"] = DefaultPageSize;
        }

        // ==========================================
        // 2. XỬ LÝ SEARCH AJAX (POST)
        // ==========================================
        public IActionResult OnPostSearch([FromBody] EmpSearchRequest request)
        {
            try
            {
                int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");

                // Gọi Database lấy dữ liệu nhân viên
                var (employees, totalRecords) = SearchEmployees(request);

                // Phán quyết quyền cho từng dòng nhân viên
                var dataWithActions = employees.Select(e => {
                    // Trạng thái nhân viên: 1 (Active), 0 (Inactive)
                    var effectivePerms = _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, e.IsActive ? 1 : 0);

                    return new
                    {
                        data = e,
                        actions = new
                        {
                            canAccess = effectivePerms.Contains(2), // Quyền View
                            accessMode = effectivePerms.Contains(4) ? "edit" : "view",
                            canEdit = effectivePerms.Contains(4),
                            canDelete = effectivePerms.Contains(6),
                            canResetPassword = effectivePerms.Contains(15) // Ví dụ mã quyền Reset Pass
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
                _logger.LogError(ex, "Error in Employee OnPostSearch");
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        // ==========================================
        // 3. CÁC HÀM BỔ TRỢ (HELPER)
        // ==========================================
        private (List<EmployeeRow> employees, int totalRecords) SearchEmployees(EmpSearchRequest request)
        {
            var employees = new List<EmployeeRow>();
            int totalRecords = 0;

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));

            // 1. Xây dựng điều kiện lọc (Biện luận các biến đầu vào)
            string whereClause = " WHERE 1=1 ";

            if (!string.IsNullOrEmpty(request.EmpCode))
                whereClause += " AND em.EmployeeCode LIKE @EmpCode ";

            if (!string.IsNullOrEmpty(request.EmpName))
                whereClause += " AND em.EmployeeName LIKE @EmpName ";

            if (request.DepartmentId.HasValue)
                whereClause += " AND em.DeptID = @DeptID ";

            // Mặc định luôn lọc theo IsActive (Checkbox)
            whereClause += " AND em.IsActive = @IsActive ";

            if (request.IsHeadDept)
                whereClause += " AND em.HeadDept = 1 "; // Giả định cột này là IsHeadDept

            // 2. Câu lệnh lấy dữ liệu có phân trang (Cú pháp OFFSET...FETCH)
            string sqlData = $@"
            SELECT em.EmployeeID, em.EmployeeCode, em.EmployeeName, d.DeptCode, em.IsActive, em.IsSystemUser,em.HeadDept
            FROM MS_Employee em
            INNER JOIN MS_Department d ON em.DeptID = d.DeptID
            {whereClause}
            ORDER BY em.EmployeeCode
            OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY";

            // 3. Câu lệnh đếm tổng số bản ghi để phân trang
            string sqlCount = $@"SELECT COUNT(*) FROM MS_Employee em INNER JOIN MS_Department d ON em.DeptID = d.DeptID {whereClause}";

            // Gộp 2 câu lệnh để chạy 1 lần duy nhất
            using var cmd = new SqlCommand(sqlData + ";" + sqlCount, conn);

            // 4. Gán tham số chống SQL Injection
            cmd.Parameters.AddWithValue("@EmpCode", $"%{request.EmpCode}%");
            cmd.Parameters.AddWithValue("@EmpName", $"%{request.EmpName}%");
            cmd.Parameters.AddWithValue("@DeptID", (object)request.DepartmentId ?? DBNull.Value);

            cmd.Parameters.AddWithValue("@IsActive", request.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("@HeadDept", request.IsHeadDept ? 1 : 0);

            cmd.Parameters.AddWithValue("@Offset", (request.Page - 1) * request.PageSize);
            cmd.Parameters.AddWithValue("@PageSize", request.PageSize);

            try
            {
                conn.Open();
                using var reader = cmd.ExecuteReader();

                // Đọc kết quả danh sách nhân viên
                while (reader.Read())
                {
                    employees.Add(new EmployeeRow
                    {
                        EmployeeID = Convert.ToInt32(reader["EmployeeID"]),
                        EmpCode = reader["EmployeeCode"].ToString(),
                        EmpName = reader["EmployeeName"].ToString(),
                        DepartmentName = reader["DeptCode"].ToString(), // Lấy mã phòng ban theo SQL của anh
                        IsSystem = Convert.ToBoolean(reader["IsSystemUser"]),
                        HeadDept = (reader["HeadDept"] != DBNull.Value && Convert.ToInt32(reader["HeadDept"]) == 1) ? 1 : 0,
                        IsActive = Convert.ToBoolean(reader["IsActive"])
                    });
                }

                // Chuyển sang kết quả của câu lệnh COUNT(*)
                if (reader.NextResult() && reader.Read())
                {
                    totalRecords = Convert.ToInt32(reader[0]);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi thực thi SQL tại SearchEmployees");
                throw;
            }

            return (employees, totalRecords);
        }

        private void LoadDepartments()
        {
            var list = new List<(int Id, string Name)>();
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            using var cmd = new SqlCommand("select DeptID , DeptName  from MS_Department  order by DeptName", conn);
            conn.Open();
            using var rd = cmd.ExecuteReader();
            while (rd.Read()) { list.Add((Convert.ToInt32(rd[0]), rd[1].ToString())); }

            DepartmentList = Helper.BuildIntSelectList(list, x => x.Id, x => x.Name, showAll: true);
        }
    }

    // --- CLASS DỮ LIỆU ---
    public class EmpSearchRequest
    {
        public string? EmpCode { get; set; }
        public string? EmpName { get; set; }
        public int? DepartmentId { get; set; }
        public bool IsActive { get; set; }
        public bool IsHeadDept { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 25;
    }

    public class EmployeeRow
    {
        public int EmployeeID { get; set; }
        public string EmpCode { get; set; }
        public string EmpName { get; set; }
        public string DepartmentName { get; set; }
        public bool IsSystem { get; set; }
        public bool IsActive { get; set; }
        public int HeadDept { get; set; }
    }

    public class EmployeeFilter
    {
        public string? EmpCode { get; set; }
        public string? EmpName { get; set; }
        public int? DepartmentId { get; set; }
        public bool IsActive { get; set; } = true;
        public bool IsHeadDept { get; set; }
    }
}