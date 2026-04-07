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

        public IActionResult OnPost()
        {
            int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");
            Mode = (Mode ?? "view").ToLower();
            bool isActuallyAdd = (Employee.EmployeeID == 0 || Mode == "add");

            // Kiểm tra quyền Save
            var currentPerms = _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, isActuallyAdd ? 1 : (Employee.IsActive ? 1 : 0));
            if (!(isActuallyAdd ? currentPerms.Contains(3) : currentPerms.Contains(4)))
            {
                ModelState.AddModelError("", "Bạn không có quyền lưu dữ liệu này.");
                LoadAllDropdowns();
                return Page();
            }

            if (!ModelState.IsValid)
            {
                LoadAllDropdowns();
                return Page();
            }

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            conn.Open();
            using var trans = conn.BeginTransaction();

            try
            {
                string sql;
                if (isActuallyAdd)
                {
                    sql = @"INSERT INTO MS_Employee (EmployeeCode, EmployeeName, Title,DeptID, IsActive, TheEmail ,
                    IsSystemUser, IsActive , IsCTReceiver , Approval , HeadDept ,StoreGR,LevelCheckSupplier, 
                    IsSupervisor, ISOLevel ,IsPurchaser,IsCFO,IsBOD ,UrlNomalSign,UrlSmallSign)
                            VALUES (@EmployeeCode, @EmployeeName, @Title,@DeptID, @IsActive, @TheEmail ,
                    @IsSystemUser, @IsActive , @IsCTReceiver , @Approval , @HeadDept ,@StoreGR,@LevelCheckSupplier, 
                    @IsSupervisor, @ISOLevel ,@IsPurchaser,@IsCFO,@IsBOD ,@UrlNomalSign,@UrlSmallSign );
                            SELECT CAST(SCOPE_IDENTITY() as int);";
                    Employee.EmployeeID = conn.ExecuteScalar<int>(sql, Employee, transaction: trans);
                }
                else
                {
                    sql = @"UPDATE MS_Employee SET 
                    EmployeeCode = @EmployeeCode, EmployeeName=EmployeeName, Title = @Title,DeptID =DeptID, IsActive=@IsActive, TheEmail= @TheEmail ,
                    IsSystemUser=@IsSystemUser, IsActive =@IsActive , IsCTReceiver =@IsCTReceiver , Approval=@Approval , HeadDept = @HeadDept ,
                    StoreGR =@StoreGR ,LevelCheckSupplier = @LevelCheckSupplier, IsSupervisor=@IsSupervisor, ISOLevel = @ISOLevel ,
                    IsPurchaser =@IsPurchaser,IsCFO =@IsCFO,IsBOD=@IsBOD ,UrlNomalSign =@UrlNomalSign, UrlSmallSign=@UrlSmallSign
                    WHERE EmployeeID = @EmployeeID";
                    conn.Execute(sql, Employee, transaction: trans);
                }

                trans.Commit();
                TempData["SuccessMessage"] = "Lưu thông tin nhân viên thành công.";
                return RedirectToPage(new { id = Employee.EmployeeID, mode = "edit" });
            }
            catch (Exception ex)
            {
                trans.Rollback();
                ModelState.AddModelError("", "Lỗi: " + ex.Message);
                LoadAllDropdowns();
                return Page();
            }
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
    }

    public class EmployeeViewModel
    {
        public int EmployeeID { get; set; }
        public string EmployeeCode { get; set; }
        public string EmployeeName { get; set; }
        public string Title { get; set; }
        public bool IsSystemUser { get; set; }
        public bool IsActive { get; set; }
        public bool IsCTReceiver { get; set; }
        public bool Approval { get; set; }
        public bool HeadDept { get; set; }
        public int? StoreGR { get; set; }
        public int? LevelCheckSupplier { get; set; }
        public string TheEmail { get; set; }
        public bool IsSupervisor { get; set; }
        public int? ISOLevel { get; set; }
        public bool IsPurchaser { get; set; }
        public bool IsCFO { get; set; }
        public bool IsBOD { get; set; }
        public string UrlNomalSign { get; set; }
        public string UrlSmallSign { get; set; }
    }
}