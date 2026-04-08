using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using SmartSam.Helpers;
using Microsoft.Data.SqlClient;
using System.Data;
using Dapper;
using SmartSam.Services;
using SmartSam.Services.Interfaces;
using System.Security.Claims;
using DocumentFormat.OpenXml.Spreadsheet;
using SmartSam.Services.Implementations;
using System.ComponentModel.DataAnnotations;


namespace SmartSam.Pages.Admin.Employee
{
    public class EmployeeDetailModel : BasePageModel
    {
        private readonly ISecurityService _securityService;
        private readonly PermissionService _permissionService;

        public EmployeeDetailModel(ISecurityService securityService, PermissionService permissionService, IConfiguration config) : base(config)
        {
            _securityService = securityService;
            _permissionService = permissionService;
        }


        private const int FUNCTION_ID = 18;

        [BindProperty(SupportsGet = true)]
        public string Mode { get; set; }

        [BindProperty]
        public PagePermissions PagePerm { get; set; }

        [BindProperty]
        public EmployeeViewModel Employee { get; set; } = new EmployeeViewModel();

        // Danh sách Dropdown
        public List<SelectListItem> DepartmentList { get; set; }
        public List<SelectListItem> StoreGroupList { get; set; }

        public async Task<IActionResult> OnGetAsync(int? id, string mode = "view")
        {
            int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");
            PagePerm = new PagePermissions();
            Mode = mode ?? "view";

            if (id.HasValue && id > 0)
            {
                LoadEmployeeData(id.Value);
                if (Employee == null) return NotFound();

                // Lấy quyền dựa trên trạng thái IsActive (1: Active, 0: Inactive)
                PagePerm.AllowedNos = _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, Employee.IsActive ? 1 : 0);

                if (!PagePerm.AllowedNos.Contains(2) && !PagePerm.AllowedNos.Contains(4))
                {
                    return RedirectToPage("/Index", new { msg = "Bạn không có quyền xem thông tin nhân viên này." });
                }

                if (Mode == "edit" && !PagePerm.AllowedNos.Contains(4)) Mode = "view";
            }
            else
            {
                // Quyền Add (mã 3)
                PagePerm.AllowedNos = _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, 1);
                if (!PagePerm.AllowedNos.Contains(3))
                {
                    return RedirectToPage("/Index", new { msg = "Bạn không có quyền tạo mới nhân viên." });
                }

                Mode = "add";
                Employee = new EmployeeViewModel
                {
                    IsActive = true,
                    EmployeeCode = GetNewEmployeeCode()
                };
            }

            LoadAllDropdowns();
            return Page();
        }

        private void LoadEmployeeData(int id)
        {
            string connString = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connString);
            string sql = "SELECT * FROM MS_Employee WHERE EmployeeID = @Id";
            Employee = conn.QueryFirstOrDefault<EmployeeViewModel>(sql, new { Id = id });
        }


        private void LoadAllDropdowns()
        {
            DepartmentList = LoadListFromSql("select DeptID , DeptCode from MS_Department ORDER BY DeptCode", "DeptID", "DeptCode");
            StoreGroupList = LoadListFromSql("select KPGroupID, KPGroupName from INV_KPGroup ORDER BY KPGroupID", "KPGroupID", "KPGroupName");
        }

        private string GetNewEmployeeCode()
        {
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            var maxId = conn.ExecuteScalar<int?>("SELECT MAX(EmployeeID) FROM MS_Employee") ?? 0;
            return "EMP" + (maxId + 1).ToString("D5");
        }

        public async Task<IActionResult> OnPostSave()
        {
            // 1. Lấy RoleID của người dùng hiện tại
            int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");

            // 2. Định nghĩa logic "Thêm mới" (Khóa kép: ID = 0 VÀ Mode là add)
            // Chuyển Mode về chữ thường để so sánh cho chính xác
            string currentMode = (Mode ?? "view").ToLower();
            bool isActuallyAdd = (Employee.EmployeeID == 0 && currentMode == "add");

            // 3. Lấy danh sách quyền từ Service (Trả về các số 1, 2, 3, 4, 5)
            // Trạng thái: 1 là Active (hoặc đang thêm mới), 0 là Inactive
            var currentPerms = _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, isActuallyAdd ? 1 : (Employee.IsActive ? 1 : 0));

            // Trước đoạn string sql;
            // Xử lý lưu file vật lý nếu có dữ liệu Base64 gửi lên
            string folderPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "Admin", "Employee");
            if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);

            // Xử lý hình Normal
            if (!string.IsNullOrEmpty(Employee.UrlNomalSign) && Employee.UrlNomalSign.Contains("base64"))
            {
                string fileName = $"{Employee.EmployeeCode}_NormalSign.png";
                string filePath = Path.Combine(folderPath, fileName);

                // Tách phần Header của Base64 (data:image/png;base64,)
                var parts = Employee.UrlNomalSign.Split(',');
                if (parts.Length > 1)
                {
                    byte[] imageBytes = Convert.FromBase64String(parts[1]);
                    System.IO.File.WriteAllBytes(filePath, imageBytes);

                    // QUAN TRỌNG: Gán lại đường dẫn ngắn để lưu vào DB
                    // Sau dòng này, Employee.UrlNomalSign sẽ không còn là đống dữ liệu nữa 
                    // mà trở thành "/uploads/Admin/Employee/EMP001_NormalSign.png"
                    Employee.UrlNomalSign = $"/uploads/Admin/Employee/{fileName}";
                }
            }

            // --- XỬ LÝ HÌNH SMALL ---
            if (!string.IsNullOrEmpty(Employee.UrlSmallSign) && Employee.UrlSmallSign.Contains("base64"))
            {
                string fileName = $"{Employee.EmployeeCode}_SmallSign.png";
                string filePath = Path.Combine(folderPath, fileName);

                var base64Parts = Employee.UrlSmallSign.Split(',');
                if (base64Parts.Length > 1)
                {
                    byte[] imageBytes = Convert.FromBase64String(base64Parts[1]);
                    System.IO.File.WriteAllBytes(filePath, imageBytes);

                    // Cập nhật lại đường dẫn ngắn để lưu vào SQL Server
                    Employee.UrlSmallSign = $"/uploads/Admin/Employee/{fileName}";
                }
            }



            // 4. BIỆN LUẬN QUYỀN VÀ THỰC HIỆN LƯU
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            conn.Open();
            using var trans = conn.BeginTransaction();

            try
            {
                if (isActuallyAdd)
                {
                    // --- TRƯỜNG HỢP THÊM MỚI ---
                    if (!currentPerms.Contains(3)) // Kiểm tra quyền mã số 3 (Add)
                    {
                        return new JsonResult(new { success = false, message = "Tài khoản không có quyền tạo mới (Quyền số 3)." });
                    }
                    string sqlInsert = @"INSERT INTO MS_Employee (
                    EmployeeCode, EmployeeName, Title, DeptID, IsActive, TheEmail,
                    IsSystemUser, IsCTReceiver, Approval, HeadDept, StoreGR, 
                    LevelCheckSupplier, IsSupervisor, ISOLevel, IsPurchaser, 
                    IsCFO, IsBOD, UrlNomalSign, UrlSmallSign, Password
                    )
                    VALUES (
                    @EmployeeCode, @EmployeeName, @Title, @DeptID, @IsActive, @TheEmail,
                    @IsSystemUser, @IsCTReceiver, @Approval, @HeadDept, @StoreGR, 
                    @LevelCheckSupplier, @IsSupervisor, @ISOLevel, @IsPurchaser, 
                    @IsCFO, @IsBOD, @UrlNomalSign, @UrlSmallSign, @EncryptedPassword
                    );
                    SELECT CAST(SCOPE_IDENTITY() as int);";

                    // Mã hóa mật khẩu khi thêm mới
                    string encryptedPass = Helpers.Helper.EncryptPassword(Employee.NewPassword ?? "123456"); // Default pass nếu để trống

                    var parameters = new DynamicParameters(Employee);
                    parameters.Add("EncryptedPassword", encryptedPass);

                    Employee.EmployeeID = await conn.ExecuteScalarAsync<int>(sqlInsert, parameters, transaction: trans);
                }
                else
                {
                    // --- TRƯỜNG HỢP CẬP NHẬT ---
                    if (!currentPerms.Contains(4)) // Kiểm tra quyền mã số 4 (Edit)
                    {
                        return new JsonResult(new { success = false, message = "Tài khoản không có quyền chỉnh sửa (Quyền số 4)." });
                    }

                    // Xử lý mật khẩu (Chỉ update nếu anh có nhập mật khẩu mới)
                    string passUpdateSql = "";
                    var parameters = new DynamicParameters(Employee);

                    if (!string.IsNullOrEmpty(Employee.NewPassword))
                    {
                        passUpdateSql = ", Password = @EncryptedPassword";
                        parameters.Add("EncryptedPassword", Helpers.Helper.EncryptPassword(Employee.NewPassword));
                    }

                    string sqlUpdate  = $@"UPDATE MS_Employee SET 
                    EmployeeCode = @EmployeeCode, 
                    EmployeeName = @EmployeeName, 
                    Title = @Title, 
                    DeptID = @DeptID, 
                    IsActive = @IsActive, 
                    TheEmail = @TheEmail,
                    IsSystemUser = @IsSystemUser, 
                    IsCTReceiver = @IsCTReceiver, 
                    Approval = @Approval, 
                    HeadDept = @HeadDept, 
                    StoreGR = @StoreGR, 
                    LevelCheckSupplier = @LevelCheckSupplier, 
                    IsSupervisor = @IsSupervisor, 
                    ISOLevel = @ISOLevel, 
                    IsPurchaser = @IsPurchaser, 
                    IsCFO = @IsCFO, 
                    IsBOD = @IsBOD, 
                    UrlNomalSign = @UrlNomalSign, 
                    UrlSmallSign = @UrlSmallSign
                    {passUpdateSql}
                    WHERE EmployeeID = @EmployeeID";

                    await conn.ExecuteAsync(sqlUpdate, parameters, transaction: trans);
                }

                trans.Commit();
                TempData["SuccessMessage"] = "Lưu thông tin nhân viên thành công.";
                return new JsonResult(new { success = true, id = Employee.EmployeeID });
            }
            catch (Exception ex)
            {
                trans.Rollback();
                return new JsonResult(new { success = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }

        public class EmployeeViewModel
        {
            public int EmployeeID { get; set; }
            public string EmployeeCode { get; set; }
            public string EmployeeName { get; set; }
            public string Title { get; set; }
            public bool IsSystemUser { get; set; }
            public string? NewPassword { get; set; } // Trường không bắt buộc (chỉ nhập khi muốn đổi)
            public bool IsActive { get; set; }
            public bool IsCTReceiver { get; set; }
            public bool Approval { get; set; }
            public bool HeadDept { get; set; }
            public int? StoreGR { get; set; }
            public int? DeptID { get; set; }
            public int? LevelCheckSupplier { get; set; }
            public string TheEmail { get; set; }
            public bool IsSupervisor { get; set; }
            public int? ISOLevel { get; set; }
            public bool IsPurchaser { get; set; }
            public bool IsCFO { get; set; }
            public bool IsBOD { get; set; }
            public string UrlNomalSign { get; set; }
            public string UrlSmallSign { get; set; }
            
            // Thêm 2 trường này để nhận chuỗi Base64 từ JS gửi lên
            public string? NormalSignBase64 { get; set; }
            public string? SmallSignBase64 { get; set; }
        }
    }
}