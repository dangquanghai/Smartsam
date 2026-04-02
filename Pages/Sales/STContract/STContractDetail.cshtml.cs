using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using SmartSam.Helpers;
using Microsoft.Data.SqlClient;
using System.Data;
using System.ComponentModel.DataAnnotations;
using Dapper;
using SmartSam.Services;
using SmartSam.Services.Interfaces; 


namespace SmartSam.Pages.Sales.STContract
{
    public class STContractDetailModel : BasePageModel
    {
        private readonly ISecurityService _securityService;
        private readonly PermissionService _permissionService;
        // Constructor truyền config vào BasePageModel
        public STContractDetailModel(ISecurityService securityService, PermissionService permissionService, IConfiguration config) : base(config) 
        {
            _securityService = securityService;
            _permissionService = permissionService; 
        }
        
       
        
        private const int FUNCTION_ID = 5;
        public bool CanChangeStatus { get; set; }
        public bool CanAdjustDate { get; set; }
        public bool CanSave { get; set; } // Quyền Edit/Add thực tế


        [BindProperty(SupportsGet = true)]
        public string Mode { get; set; } // Sẽ nhận "add", "edit", hoặc "view" từ URL

        [BindProperty]
        public PagePermissions PagePerm { get; set; } 

        [BindProperty]
        public ContractViewModel Contract { get; set; } = new ContractViewModel();

        public CM_STInfoViewModel? STInfor { get; set; } = new CM_STInfoViewModel();
        public ServiceViewModel? CT_Services { get; set; } = new ServiceViewModel();
        public TenantViewModel? CT_Tenants { get; set; } = new TenantViewModel();
        
        [BindProperty]
        public List<ContractServiceViewModel> ContractServices { get; set; } = new List<ContractServiceViewModel>();
        


        // 3. Đổi SelectList thành List<SelectListItem> để khớp với hàm LoadSelect2
        public List<SelectListItem> StatusList { get; set; }
        public List<SelectListItem> VATFromList { get; set; }
        public List<SelectListItem> SourceList { get; set; }
        public List<SelectListItem> ApmtList { get; set; }
        public List<SelectListItem> CompanyList { get; set; }
        public List<SelectListItem> AgentCompanyList { get; set; }
        public List<SelectListItem> AgentPersonList  { get; set; }
        public List<SelectListItem> RepresentatorList { get; set; }
        public List<SelectListItem> ReceiverList { get; set; }
        public List<SelectListItem> PaymnetByList { get; set; }

        // Danh sách nạp cho Dropdown trong Popup
        public List<SelectListItem> ServiceList { get; set; }
        public List<SelectListItem> ChargeIntervalList { get; set; }
        public List<SelectListItem> ChargeTypeList { get; set; }
        public List<SelectListItem> ListNations { get; set; }
        public List<SelectListItem> ListPositions { get; set; }
        public List<SelectListItem> ListArrivalPorts { get; set; }
        public List<SelectListItem> ListTenantTypes { get; set; }

   



        [BindProperty]
        public long SavedContractID { get; set; }
        [HttpGet]
        public IActionResult OnGetAgentPersons(int companyId)
        {
            string connString = _config.GetConnectionString("DefaultConnection");
            string sql = $" select AgentID, AgentName  from CM_AgentPerson where  CompanyID = {companyId} ORDER BY AgentName";
            var data = Helper.ExecuteQuery(sql, connString);
            // Nếu data đã là List<T> thì bỏ qua bước này
            var personList = data.AsEnumerable().Select(row => new {
                PersonID = Convert.ToInt32(row["AgentID"]),
                PersonName = row["AgentName"].ToString()
            }).ToList();

            // Dùng Helper của bạn
            var selectList = Helper.BuildIntSelectList(personList, x => x.PersonID, x => x.PersonName);

            return new JsonResult(selectList);
        }
        private void LoadContractData(int id)
        {
            string connString = _config.GetConnectionString("DefaultConnection");
            using (var conn = new SqlConnection(connString))
            {
                // 1. Load bảng chính CM_Contract
                string sqlContract = "SELECT * FROM CM_Contract WHERE ContractID = @Id";
                Contract = conn.QueryFirstOrDefault<ContractViewModel>(sqlContract, new { Id = id });

                if (Contract != null)
                {
                    // 2. Load bảng CM_STInfo (Sử dụng STContractID làm khóa ngoại)
                    string sqlST = "SELECT AgentCompany, AgentPerson as AgentPersonId, CancellationCharge, PaymentInfor, DepositInfor, SpecialReq " +
                                   "FROM CM_STInfo WHERE STContractID = @Id";
                    STInfor = conn.QueryFirstOrDefault<CM_STInfoViewModel>(sqlST, new { Id = id }) ?? new CM_STInfoViewModel();

                    // 3. Load bổ sung ngày tháng từ CM_ContractApmt (Nếu CM_Contract chưa có hoặc muốn lấy từ bảng phụ)
                    // Lưu ý: Nếu bạn đã lưu FromDate/ToDate vào CM_Contract rồi thì bước này có thể bỏ qua
                    string sqlApmt = "SELECT FromDate, ToDate FROM CM_ContractApmt WHERE ContractID = @Id";
                    var apmtDates = conn.QueryFirstOrDefault(sqlApmt, new { Id = id });
                    if (apmtDates != null)
                    {
                        Contract.ContractFromDate = apmtDates.FromDate;
                        Contract.ContractToDate = apmtDates.ToDate;
                    }
                }
            }
        }
        public async Task<IActionResult> OnGetAsync(int? id, string mode = "view")
        {
            // 1. Lấy thông tin Role (Dùng -1 để an toàn hơn 0)
            int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");

            // Khởi tạo đối tượng quyền để dùng ngoài giao diện .cshtml
            PagePerm = new PagePermissions();
            Mode = mode ?? "view";

            if (id.HasValue && id > 0)
            {
                // 2. Load dữ liệu bản ghi hiện tại
                LoadContractData(id.Value);
                if (Contract == null) return NotFound();

                // 3. LẤY QUYỀN THỰC TẾ dựa trên trạng thái của Hợp đồng
                // Sử dụng toán tử ?? để đảm bảo có Status truyền vào (mặc định 1 nếu null)
                PagePerm.AllowedNos = _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, Contract.ContractStatus ?? 1);

                // --- KIỂM TRA BẢO MẬT ---

                // Bước A: Nếu không có cả quyền Xem (2) và Sửa (4) -> Đuổi ra Index
                if (!PagePerm.AllowedNos.Contains(2) && !PagePerm.AllowedNos.Contains(4))
                {
                    return RedirectToPage("/Index", new { msg = "Bạn không có quyền truy cập bản ghi này." });
                }

                // Bước B: Nếu yêu cầu sửa (mode=edit) nhưng trạng thái hiện tại KHÔNG cho sửa (không có mã 4)
                // Ví dụ: ad035 vào hợp đồng Living -> SecurityService đã gọt mất mã 4 -> Ép về view
                if (Mode == "edit" && !PagePerm.AllowedNos.Contains(4))
                {
                    Mode = "view";
                }

                // Gán các cờ hiệu để UI đóng/mở các nút chức năng đặc biệt
                CanChangeStatus = PagePerm.AllowedNos.Contains(7);
                CanAdjustDate = PagePerm.AllowedNos.Contains(8);
            }
            else
            {
                // 4. TRƯỜNG HỢP ADD MỚI (id = 0 hoặc null)
                // Check mã quyền 3 (Add) với status mặc định là 0
                PagePerm.AllowedNos = _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, 0);

                if (!PagePerm.AllowedNos.Contains(3))
                {
                    return RedirectToPage("/Index", new { msg = "Bạn không có quyền tạo mới dữ liệu." });
                }

                Mode = "add";
                // Khởi tạo object mặc định như cũ của anh
                Contract = new ContractViewModel
                {
                    ContractNo = GetNewSTContractNo(),
                    ContractDate = DateTime.Now,
                    ContractStatus = 1, // Mặc định là Reser
                    PerVAT = 10,
                    ContractFromDate = DateTime.Now.Date,
                    ContractToDate = DateTime.Now.Date.AddMonths(1)
                };
                STInfor = new CM_STInfoViewModel();
            }

            // 5. Load các dữ liệu danh mục
            LoadAllDropdowns(isNew: (Mode == "add"));

            if (STInfor != null && STInfor.AgentCompany.HasValue)
            {
                AgentPersonList = FetchAgentPersons(STInfor.AgentCompany.Value);
            }

            return Page();
        }
       
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
        private List<SelectListItem> FetchAgentPersons(int companyId)
        {
            // 1. Lấy chuỗi kết nối
            string connString = _config.GetConnectionString("DefaultConnection");

            // 2. Viết câu lệnh SQL (Nên dùng Parameter để an toàn)
            // Lưu ý: Tên bảng và cột phải khớp với DB của bạn (ở đây mình dùng theo hàm OnGetAgentPersons cũ của bạn)
            string sql = "SELECT AgentID, AgentName FROM CM_AgentPerson WHERE CompanyID = @CompanyID ORDER BY AgentName";

            // 3. Thực thi truy vấn (Giả sử Helper của bạn hỗ trợ truyền parameter)
            // Nếu Helper.ExecuteQuery của bạn chỉ nhận string, bạn có thể dùng tạm: 
            // string sql = $"SELECT AgentID, AgentName FROM CM_AgentPerson WHERE CompanyID = {companyId} ORDER BY AgentName";

            var data = Helper.ExecuteQuery(sql, connString);

            // 4. Chuyển đổi DataTable/Enumerable sang List các đối tượng nặc danh
            var personList = data.AsEnumerable().Select(row => new {
                PersonID = Convert.ToInt32(row["AgentID"]),
                PersonName = row["AgentName"].ToString()
            }).ToList();

            // 5. Dùng Helper BuildIntSelectList để tạo danh sách SelectListItem cho Dropdown
            var selectList = Helper.BuildIntSelectList(
                personList,
                x => x.PersonID,
                x => x.PersonName
            );

            return selectList;
        }
        // Hàm bổ trợ lấy status nhanh từ DB
        private int GetCurrentStatusFromDb(int id)
        {
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            return conn.ExecuteScalar<int?>("SELECT ContractStatus FROM ST_Contract WHERE ContractID = @id", new { id }) ?? -1;
        }

        public IActionResult OnPost()
        {
            // 1. Thu thập thông tin định danh
            int roleId = int.Parse(User.FindFirst("RoleID")?.Value ?? "-1");
            int currentEmpId = int.Parse(User.FindFirst("EmployeeID")?.Value ?? "0");



            // Xác định Mode thực tế dựa trên dữ liệu gửi lên
            Mode = (Mode ?? "view").ToLower();
            bool isActuallyAdd = (Contract.ContractID == 0 || Mode == "add");

            // 2. Kiểm tra quyền dựa trên Mode
            int statusToCheck = isActuallyAdd ? 0 : GetCurrentStatusFromDb(Contract.ContractID);
            if (!isActuallyAdd && statusToCheck == -1) return NotFound();

            var currentPerms = _securityService.GetEffectivePermissions(FUNCTION_ID, roleId, statusToCheck);
            bool hasPermission = isActuallyAdd ? currentPerms.Contains(3) : currentPerms.Contains(4);

            if (!hasPermission)
            {
                ModelState.AddModelError("", "Bạn không có quyền thực hiện thao tác này ở trạng thái hiện tại.");
                LoadAllDropdowns(isNew: isActuallyAdd);
                return Page();
            }

            if (!ModelState.IsValid)
            {
                LoadAllDropdowns(isNew: isActuallyAdd);
                return Page();
            }

            // 3. Thực hiện lưu dữ liệu (Transaction)
            string connString = _config.GetConnectionString("DefaultConnection");
            using var conn = new SqlConnection(connString);
            conn.Open();
            using var trans = conn.BeginTransaction();

            try
            {
                // Lưu thông tin chính
                this.SavedContractID = SaveContract(conn, trans);
                // Lưu các bảng phụ liên quan
                SaveContractApmt(this.SavedContractID, conn, trans);
                SaveSTInfo(conn, trans);
                string logDesc = isActuallyAdd ? "Add new contract" : $"Update contract (Mode: {Mode})";
                SaveRecord(this.SavedContractID, logDesc, conn, trans);

                trans.Commit();

                // 4. Cập nhật trạng thái UI sau khi lưu thành công
                TempData["SuccessMessage"] = isActuallyAdd ? "Tạo hợp đồng thành công." : "Cập nhật thành công.";
                Mode = "edit"; // Luôn chuyển về edit để người dùng có thể sửa tiếp
                this.Contract.ContractID = (int)this.SavedContractID;

                // QUAN TRỌNG: Xóa ModelState để Razor Pages buộc phải render lại từ Property C# 
                // thay vì dùng giá trị cũ trong Request gửi lên
                ModelState.Clear();

                LoadAllDropdowns(isNew: false);
                if (STInfor.AgentCompany.HasValue)
                    AgentPersonList = FetchAgentPersons(STInfor.AgentCompany.Value);

                return Page();
            }
            catch (Exception ex)
            {
                trans.Rollback();
                ModelState.AddModelError("", "Lỗi hệ thống: " + ex.Message);
                LoadAllDropdowns(isNew: isActuallyAdd);
                return Page();
            }
        }

   
        public async Task<JsonResult> OnGetCheckApmtAvail(string apartmentNo, string fromDate, string toDate, long contractId)
        {
            try
            {
                // 1. Parse ngày tháng (đảm bảo định dạng chuẩn để truyền vào SQL)
                DateTime dFrom = DateTime.Parse(fromDate);
                DateTime dTo = DateTime.Parse(toDate);
                byte isShortTerm = 1; // Theo code VB6 bạn cung cấp cố định là 1

                string connString = _config.GetConnectionString("DefaultConnection");

                using (var conn = new SqlConnection(connString))
                {
                    // 2. Gọi Store Procedure
                    // Dapper sử dụng tham số commandType: CommandType.StoredProcedure
                    var result = await conn.ExecuteScalarAsync<int>(
                        "sp_CheckApmtAvail",
                        new
                        {
                            @ApartmentNo = apartmentNo, // Đầu vào là Apartment No như bạn yêu cầu
                            @FromDate = dFrom,
                            @ToDate = dTo,
                            @isShortTerm = isShortTerm,
                            @ContractID = contractId
                        },
                        commandType: CommandType.StoredProcedure
                    );

                    // Trả về kết quả (0, 1, hoặc 2) cho JavaScript xử lý
                    return new JsonResult(new { status = result });
                }
            }
            catch (Exception ex)
            {
                // Log lỗi nếu cần thiết
                return new JsonResult(new { status = 0, message = ex.Message });
            }
        }

        private void SaveContractApmt(long contractId, SqlConnection conn, SqlTransaction trans)
        {
            // Lấy ApartmentNo dựa trên ApmtID được chọn từ Dropdown
            string apartmentNo = GetApartmentNoById(Contract.ApmtID, conn, trans);

            if (string.IsNullOrEmpty(apartmentNo)) return;

            // Sử dụng logic Upsert (Update if exists, else Insert)
            string sql = @"
            IF EXISTS (SELECT 1 FROM CM_ContractApmt WHERE ContractID = @ContractID)
            BEGIN
            UPDATE CM_ContractApmt 
            SET ApartmentNo = @ApmtNo,
                FromDate = @FromDate,
                ToDate = @ToDate
            WHERE ContractID = @ContractID
            END
            ELSE
            BEGIN
            INSERT INTO CM_ContractApmt (ContractID, ApartmentNo, FromDate, ToDate)
            VALUES (@ContractID, @ApmtNo, @FromDate, @ToDate)
            END";

            // Dapper giúp truyền tham số gọn gàng và tự động xử lý DBNull cho ngày tháng
            conn.Execute(sql, new
            {
                ContractID = contractId,
                ApmtNo = apartmentNo,
                FromDate = Contract.ContractFromDate,
                ToDate = Contract.ContractToDate
            }, transaction: trans);
        }

        private string GetApartmentNoById(int? apmtId, SqlConnection conn, SqlTransaction trans)
        {
            if (!apmtId.HasValue) return null;

            string sql = "SELECT TOP 1 ApartmentNo FROM AM_Apmt WHERE ApmtID = @ApmtID";
            using (var cmd = new SqlCommand(sql, conn, trans))
            {
                cmd.Parameters.AddWithValue("@ApmtID", apmtId.Value);
                var result = cmd.ExecuteScalar();
                return result?.ToString();
            }
        }
        private int SaveContract(SqlConnection conn, SqlTransaction trans)
        {
            bool isNew = (Contract.ContractID == 0);
            string apartmentNo = GetApartmentNoById(Contract.ApmtID, conn, trans);

            var planCheckinDate = Contract.ContractFromDate;
            var planCheckoutDate = Contract.ContractToDate;

            string sql;
            if (isNew)
            {
                sql = @"
            INSERT INTO CM_Contract (
                ContractNo, ContractDate, ApmtID, ContractApartmentNo, CurrentApartmentNo, 
                CurrentRentRateNVD, PerVAT, TotalPriceExcVATVND, DisPlayVATFromID,
                ContractStatus, CompanyID,
                ContractFromDate, ContractToDate, PlanCheckinDate, PlanCheckoutDate,
                IsRepeater, Occupy, ContractSourceID, ReceivedByID,
                Remarks,IsShortTerm
            )
            VALUES (
                @ContractNo, @ContractDate, @ApmtID, @ApmtNo, @ApmtNo,
                @CurrentRentRateNVD, @PerVAT, @TotalPriceExcVATVND, @DisPlayVATFromID,
                @ContractStatus, @CompanyID,
                @ContractFromDate, @ContractToDate, @PlanCheckinDate, @PlanCheckoutDate,
                @IsRepeater, @Occupy, @ContractSourceID, @ReceivedByID,
                @Remarks,@IsShortTerm
            );
            SELECT CAST(SCOPE_IDENTITY() as int);";
            }
            else
            {
                sql = @"
            UPDATE CM_Contract SET  
                ContractDate = @ContractDate,  
                ApmtID = @ApmtID,
                ContractApartmentNo = @ApmtNo, 
                CurrentApartmentNo = @ApmtNo,
                CurrentRentRateNVD = @CurrentRentRateNVD,
                PerVAT = @PerVAT,
                TotalPriceExcVATVND = @TotalPriceExcVATVND,
                DisPlayVATFromID = @DisPlayVATFromID,
                ContractStatus = @ContractStatus,
                CompanyID = @CompanyID,
                ContractFromDate = @ContractFromDate,
                ContractToDate = @ContractToDate,
                PlanCheckinDate = @PlanCheckinDate,
                PlanCheckoutDate = @PlanCheckoutDate,
                IsRepeater = @IsRepeater,
                Occupy = @Occupy,
                Remarks = @Remarks,
                IsShortTerm = @IsShortTerm
            WHERE ContractID = @ContractID";
            }

            var parameters = new
            {
                Contract.ContractID,
                Contract.ContractNo,
                Contract.ContractDate,
                Contract.ApmtID,
                ApmtNo = apartmentNo,
                Contract.CurrentRentRateNVD,
                Contract.PerVAT,
                Contract.TotalPriceExcVATVND,
                Contract.DisPlayVATFromID,
                ContractStatus = Contract.ContractStatus ?? 1,
                Contract.CompanyID,
                Contract.ContractFromDate,
                Contract.ContractToDate,
                PlanCheckinDate = planCheckinDate,
                PlanCheckoutDate = planCheckoutDate,
                Contract.IsRepeater,
                Contract.Occupy,
                Contract.ContractSourceID,
                Contract.ReceivedByID,
                Contract.Remarks,
                Contract.IsShortTerm, 
                User = User.Identity.Name ?? "System"
            };

            if (isNew)
            {
                // Lấy ID mới từ DB và gán vào Object
                Contract.ContractID = conn.ExecuteScalar<int>(sql, parameters, transaction: trans);
            }
            else
            {
                // Với Update, ID không đổi, ta chỉ thực thi lệnh Update
                conn.Execute(sql, parameters, transaction: trans);
            }

            // Trả về ID (dù là mới tạo hay là ID cũ đang edit)
            return Contract.ContractID;
        }
        private void SaveRecord(long contractId, string Description, SqlConnection conn, SqlTransaction trans)
        {
            int EmpId = int.Parse(User.FindFirst("EmployeeID")?.Value ?? "0");
            string sql = @"
            Insert CM_CTRecord(ContractID,Description,Operator)
            VALUES (@ContractID, @Description,@EmpId)";

            using var cmd = new SqlCommand(sql, conn, trans);
            cmd.Parameters.AddWithValue("@ContractID", contractId);
            cmd.Parameters.AddWithValue("@Description", Description);
            cmd.Parameters.AddWithValue("@EmpId", EmpId);
            cmd.ExecuteNonQuery();
        }

        private void SaveSTInfo(SqlConnection conn, SqlTransaction trans)
        {
            string sqlUpsert = @"
            IF EXISTS (SELECT 1 FROM CM_STInfo WHERE STContractID = @STContractID)
            BEGIN
                UPDATE CM_STInfo SET 
                    AgentCompany = @AgentCompany, AgentPerson = @AgentPersonId,
                    CancellationCharge = @CancellationCharge, PaymentInfor = @PaymentInfor,
                    DepositInfor = @DepositInfor, SpecialReq = @SpecialReq
                WHERE STContractID = @STContractID
            END
            ELSE
            BEGIN
                INSERT INTO CM_STInfo (STContractID, AgentCompany, AgentPerson, CancellationCharge, PaymentInfor, DepositInfor, SpecialReq)
                VALUES (@STContractID, @AgentCompany, @AgentPersonId, @CancellationCharge, @PaymentInfor, @DepositInfor, @SpecialReq)
            END";

            using var cmd = new SqlCommand(sqlUpsert, conn, trans);
            cmd.Parameters.AddWithValue("@STContractID", Contract.ContractID);
            cmd.Parameters.AddWithValue("@AgentCompany", (object)STInfor.AgentCompany ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AgentPersonId", (object)STInfor.AgentPersonId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CancellationCharge", (object)STInfor.CancellationCharge ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PaymentInfor", (object)STInfor.PaymentInfor ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DepositInfor", (object)STInfor.DepositInfor ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SpecialReq", (object)STInfor.SpecialReq ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
        private void AddSTParameters(SqlCommand cmd)
        {
            cmd.Parameters.AddWithValue("@STContractID", Contract.ContractID);
            cmd.Parameters.AddWithValue("@AgentCompany", (object)STInfor.AgentCompany ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AgentPerson", (object)STInfor.AgentPersonId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@CancellationCharge", (object)STInfor.CancellationCharge ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@PaymentInfor", (object)STInfor.PaymentInfor ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@DepositInfor", (object)STInfor.DepositInfor ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SpecialReq", (object)STInfor.SpecialReq ?? DBNull.Value);
        }

        private void LoadAllDropdowns(bool isNew)
        {
            
            // 1. Load Apartment (Có điều kiện ngày tháng)
            string sqlApmt = @"SELECT ApmtID, ApartmentNo FROM AM_Apmt 
                    WHERE ExistFrom <= GETDATE() AND ExistTo >= GETDATE() 
                    ORDER BY ApartmentNo";
            ApmtList = LoadListFromSql(sqlApmt, "ApmtID", "ApartmentNo");

            // 2. Load Status (Thay đổi chuỗi IN dựa trên isNew)
            string statusIds = isNew ? "1,9" : "1,2,3,4,5,6,7,8,9";
            string sqlStatus = $"SELECT StatusID, StatusName FROM CM_ContractStatus WHERE StatusID IN ({statusIds}) ORDER BY StatusID";

            StatusList = LoadListFromSql(sqlStatus, "StatusID", "StatusName");

            SourceList = LoadListFromSql("SELECT ContractSourceID, ContacrtSourceName  FROM CM_Source order by ContacrtSourceName  ", "ContractSourceID", "ContacrtSourceName");
            
            ReceiverList = LoadListFromSql("select EmployeeID , EmployeeName from MS_Employee where IsActive =1 and IsCTReceiver=1 and EmployeeCode like '%MD%' and LEN(EmployeeCode) =5  order by EmployeeName  ", "EmployeeID", "EmployeeName");

            VATFromList = LoadListFromSql("select DisPlayVATFromID, DisPlayVATFromName from CM_DisplayVATOnReservation  order by DisPlayVATFromName  ", "DisPlayVATFromID", "DisPlayVATFromName");

            PaymnetByList = LoadListFromSql("SELECT PaymentByID, PaymentByName FROM dbo.CM_ContractPaymentBy  order by PaymentByName ", "PaymentByID", "PaymentByName");

            CompanyList = LoadSelect2("CM_Company", "CompanyID", "CompanyName");
            AgentCompanyList = LoadSelect2("CM_Company", "CompanyID", "CompanyName");

            // load các danh sách trong popup
            string sql = " select  ServiceID , ServiceName  from SV_ServiceList where IsContractService=1 and IsActive =1 order by ServiceName";
            ServiceList = LoadListFromSql(sql, "ServiceID", "ServiceName");

            sql = "  select ChargeIntervalID, ChargeIntervalName  from SV_ChargeIntervalFL order by ChargeIntervalName ";
            ChargeIntervalList = LoadListFromSql(sql, "ChargeIntervalID", "ChargeIntervalName");

            sql = "  select ChargeTypeID, ChargeTypeName   from SV_ChargeTypeFL  where ChargeTypeID <>2 order by ChargeTypeName ";
            ChargeTypeList = LoadListFromSql(sql, "ChargeTypeID", "ChargeTypeName");

            sql = " select NationID, NationName from MS_Nation order by NationName ";
            ListNations = LoadListFromSql(sql, "NationID", "NationName");

            sql = " select PositionID, PositionName from CM_TenantPosition order by PositionName ";
            ListPositions = LoadListFromSql(sql, "PositionID", "PositionName");

            sql = " SELECT PortID, PortName FROM MS_ArrivalPort ORDER BY PortName ";
            ListArrivalPorts = LoadListFromSql(sql, "PortID", "PortName");

            sql = " select TenantTypeID, TenantTypeName  from CM_TenantType ";
            ListTenantTypes= LoadListFromSql(sql, "TenantTypeID", "TenantTypeName");

        }

        public string GetNewSTContractNo()
        {
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            // Tìm số lớn nhất có định dạng ST + con số (bỏ qua các hậu tố -xx)
            string sql = @"
            SELECT MAX(CAST(SUBSTRING(ContractNo, 3, 
                CASE 
                    WHEN CHARINDEX('-', ContractNo) > 0 THEN CHARINDEX('-', ContractNo) - 3 
                    ELSE LEN(ContractNo) 
                END) AS INT)) 
            FROM CM_Contract 
            WHERE ContractNo LIKE 'ST[0-9]%'";

            conn.Open();
            using var cmd = new SqlCommand(sql, conn);
            var maxNumber = cmd.ExecuteScalar();

            int nextNumber = (maxNumber == DBNull.Value) ? 10001 : Convert.ToInt32(maxNumber) + 1;

            return $"ST{nextNumber}";
        }

        // 1. Handler Load lại danh sách dịch vụ (Grid)
        public async Task<JsonResult> OnGetContractServices(long contractId)
        {
            // Câu SQL theo yêu cầu của bạn
            string sql = @"
            SELECT cs.ContractServiceID, cs.ContractID, sv.ServiceName, 
            cs.ServiceFromDate, cs.ServiceToDate, 
            itv.ChargeIntervalName, ct.ChargeTypeName, 
            cs.ChargeAmount, cs.MaxQuantity, cs.Notes 
            FROM CM_ContractService cs 
            INNER JOIN SV_ServiceList sv ON cs.ServiceID = sv.ServiceID
            INNER JOIN SV_ChargeIntervalFL itv ON cs.ChargeInterval = itv.ChargeIntervalID 
            INNER JOIN SV_ChargeTypeFL ct ON cs.ChargeType = ct.ChargeTypeID 
            WHERE cs.ContractID = @ContractID";

            using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                var services = await conn.QueryAsync(sql, new { ContractID = contractId });
                return new JsonResult(services);
            }
        }
        private async Task LoadContractServices(long contractId)
        {
            string sql = @"
            SELECT cs.ContractServiceID,
            cs.ContractID,
            sv.ServiceName, 
            cs.ServiceFromDate,
            cs.ServiceToDate, 
            itv.ChargeIntervalName,
            ct.ChargeTypeName, 
            cs.ChargeAmount,
            cs.MaxQuantity,
            cs.Notes 
            FROM CM_ContractService cs 
            INNER JOIN SV_ServiceList sv ON cs.ServiceID = sv.ServiceID
            INNER JOIN SV_ChargeIntervalFL itv ON cs.ChargeInterval = itv.ChargeIntervalID 
            INNER JOIN SV_ChargeTypeFL ct ON cs.ChargeType = ct.ChargeTypeID 
            WHERE cs.ContractID = @ContractID ";
            

            using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                var result = await conn.QueryAsync<ContractServiceViewModel>(sql, new { ContractID = contractId });
                ContractServices = result.ToList();
            }
        }

        // 2. Handler Lưu dịch vụ từ Popup
        public async Task<JsonResult> OnPostSaveService([FromBody] ContractServiceViewModel model)
        {
            if (model == null) return new JsonResult(new { success = false, message = "Dữ liệu trống" });

            try
            {
                string sql = @"
            INSERT INTO CM_ContractService 
            (ContractID, ServiceID, ServiceFromDate, ServiceToDate, ChargeInterval, ChargeType,ChargeAmount, MaxQuantity, Notes)
            VALUES 
            (@ContractID, @ServiceID, @ServiceFromDate, @ServiceToDate, @ChargeInterval, @ChargeType,@ChargeAmount, @MaxQuantity,  @Notes)";

                using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
                {
                    await conn.ExecuteAsync(sql, model);
                }
                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }

        public async Task<JsonResult> OnPostDeleteServiceAsync(long id)
        {
            try
            {
                string connString = _config.GetConnectionString("DefaultConnection");

                using (var conn = new SqlConnection(connString))
                {
                    await conn.OpenAsync();

                    // 1. Kiểm tra trạng thái hợp đồng trước khi cho phép xóa
                    // Chúng ta Join bảng Service với bảng Contract để lấy ContractStatus
                    string checkSql = @"
                SELECT c.ContractStatus 
                FROM CM_ContractService cs
                INNER JOIN CM_Contract c ON cs.ContractID = c.ContractID
                WHERE cs.ContractServiceID = @SvcID";

                    var status = await conn.QueryFirstOrDefaultAsync<int?>(checkSql, new { SvcID = id });

                    if (status == null)
                    {
                        return new JsonResult(new { success = false, message = "Dịch vụ không tồn tại hoặc đã bị xóa trước đó." });
                    }

                    if (status != 1)
                    {
                        return new JsonResult(new { success = false, message = "Hợp đồng đã chốt hoặc đang thực hiện (Status != 1). Không thể xóa dịch vụ!" });
                    }

                    // 2. Thực hiện xóa nếu thỏa mãn điều kiện
                    string deleteSql = "DELETE FROM CM_ContractService WHERE ContractServiceID = @SvcID";
                    int affectedRows = await conn.ExecuteAsync(deleteSql, new { SvcID = id });

                    if (affectedRows > 0)
                    {
                        return new JsonResult(new { success = true, message = "Đã xóa dịch vụ thành công." });
                    }
                    else
                    {
                        return new JsonResult(new { success = false, message = "Không thể xóa dữ liệu vào lúc này." });
                    }
                }
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }
        public JsonResult OnGetHistory(long contractId)
        {
            try
            {
                string sql = @"
            SELECT e.EmployeeName, c.Description, c.RecordTime 
            FROM CM_CTRecord c 
            INNER JOIN MS_Employee e ON c.Operator = e.EmployeeID 
            WHERE c.ContractID = @ContractID 
            ORDER BY c.RecordTime DESC";

                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
                var history = conn.Query(sql, new { ContractID = contractId }).ToList();

                return new JsonResult(history);
            }
            catch (Exception ex)
            {
                return new JsonResult(new { error = ex.Message });
            }
        }
        public async Task<JsonResult> OnGetSearchTenants(string name, int currentId)
        {
            int cid = (currentId > 0) ? currentId : (this.Contract?.ContractID ?? 0);

            // Bổ sung FamilyPos vào danh sách chọn
            string sql = @"
        SELECT TenantID, CustomerName, Male, IDPassportNo, Birthday, NationName, FamilyPos
        FROM VIEW_ContractMember 
        WHERE ContractID <> @CID 
        AND CustomerName LIKE @Name  order by CustomerName";

            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            var data = await conn.QueryAsync(sql, new { CID = cid, Name = "%" + name + "%" });

            return new JsonResult(data);
        }
        public async Task<JsonResult> OnPostImportTenant(int tenantId, int contractId, int familyPos)
        {
            try
            {
                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));

                // Kiểm tra trùng
                string checkSql = "SELECT COUNT(1) FROM CM_ContractTenant WHERE ContractID = @CTID AND TenantID = @TID";
                int exists = await conn.ExecuteScalarAsync<int>(checkSql, new { CTID = contractId, TID = tenantId });

                if (exists > 0)
                    return new JsonResult(new { success = false, message = "Người này đã có trong hợp đồng." });

                // Thực hiện Insert với 3 trường anh yêu cầu + các trường NOT NULL bắt buộc khác
                string insertSql = @"
                INSERT INTO CM_ContractTenant (ContractID, TenantID, FamilyPos) VALUES (@CTID, @TID, @FPos)";
                await conn.ExecuteAsync(insertSql, new
                {
                    CTID = contractId,
                    TID = tenantId,
                    FPos = familyPos
                });

                return new JsonResult(new { success = true });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = "Lỗi: " + ex.Message });
            }
        }
        public async Task<JsonResult> OnGetLoadContractTenants(int contractId)
        {
            try
            {
                using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));

                string sql = @"
                SELECT ct.ContractTenantID, ct.ContractID, ct.TenantID,t.Title, t.CustomerName, 
                t.Male, t.Birthday, n.NationName, t.IDPassportNo, ct.TenantType, 
                tp.PositionName, ct.IsMoveOut, ct.VisaNo,
                ct.VisaDate, ct.EntryDate, ct.A_DCardNo, ct.MoveinDate, 
                p.PortName, ct.ProposeExpDate,
                ct.PermitExpDate, ct.Sponsor, ct.LastRegDate, ct.Notes 
                FROM CM_ContractTenant ct
                JOIN CM_Customer t ON ct.TenantID = t.CustomerID
                LEFT JOIN MS_Nation n ON t.Nationality = n.NationID
                LEFT JOIN CM_TenantPosition tp ON ct.FamilyPos = tp.PositionID
                LEFT JOIN MS_ArrivalPort p ON ct.ArrivalPort = p.PortID 
                WHERE ct.ContractID = @CID";

                var data = await conn.QueryAsync(sql, new { CID = contractId });
                return new JsonResult(data);
            }
            catch (Exception ex)
            {
                // Trả về lỗi chi tiết để anh xem ở tab Response trong F12
                return new JsonResult(new { success = false, message = ex.Message });
            }
        }
        public async Task<JsonResult> OnGetGetFullTenantDetail(int contractTenantId)
        {
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));

            // 1. TRƯỜNG HỢP TẠO MỚI (ID = 0)
            if (contractTenantId == 0)
            {
                return new JsonResult(new
                {
                    info = new
                    {
                        ContractTenantID = 0,
                        TenantID = 0,
                        Male = true,
                        Nationality = 0, 
                        ArrivalPort = 1
                    },
                    passports = new List<dynamic>(),
                    police = new List<dynamic>()
                });
            }

            // 2. TRƯỜNG HỢP VIEW/EDIT (Lấy dữ liệu từ SQL)
            // Join với MS_Nation để lấy NationName hiển thị nếu cần
            string sqlInfo = @"
            SELECT t.Title, t.CustomerName, t.IDPassportNo, t.Birthday, t.Male, t.Nationality,ct.FamilyPos,
            n.NationName, ct.* FROM CM_ContractTenant ct 
            JOIN CM_Customer t ON ct.TenantID = t.CustomerID 
            LEFT JOIN MS_Nation n ON t.Nationality = n.NationID
            WHERE ct.ContractTenantID = @ID";

            string sqlDocs = "SELECT * FROM CM_ContractTenant_Doc WHERE ContractTenantID = @ID";

            var info = await conn.QueryFirstOrDefaultAsync<dynamic>(sqlInfo, new { ID = contractTenantId });
            var allDocs = await conn.QueryAsync<dynamic>(sqlDocs, new { ID = contractTenantId });

            return new JsonResult(new
            {
                info,
                passports = allDocs.Where(x => x.DocType == 1).ToList(),
                police = allDocs.Where(x => x.DocType == 2).ToList()
            });
        }
        public async Task<JsonResult> OnPostSaveTenantDetail([FromBody] TenantViewModel dto)
        {
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));
            await conn.OpenAsync();
            using var trans = conn.BeginTransaction();

            using var reader = new StreamReader(Request.Body);
            string json = await reader.ReadToEndAsync();

            try
            {
                // --- 1. XỬ LÝ BẢNG CM_Customer (Thông tin định danh) ---
                int finalCustomerId = dto.TenantID;

                if (finalCustomerId == 0)
                {
                    // Thêm mới khách hàng
                    string sqlInsertCust = @"
                    INSERT INTO CM_Customer (CustomerName, Title, Male, Birthday, Nationality, IDPassportNo, IsTenant, Address, Company, VATCode) 
                    OUTPUT INSERTED.CustomerID
                    VALUES (@CustomerName, @Title, @Male, @Birthday, @Nationality, @IDPassportNo, 1, @Address, @Company, @VATCode)";

                    finalCustomerId = await conn.QuerySingleAsync<int>(sqlInsertCust, dto, trans);
                    dto.TenantID = finalCustomerId; // Gán lại ID mới sinh ra cho model
                }
                else
                {
                    // Cập nhật thông tin khách hàng hiện tại (Master Data)
                    string sqlUpdateCust = @"
                    UPDATE CM_Customer SET 
                    CustomerName = @CustomerName, Title = @Title, Male = @Male, 
                    Birthday = @Birthday, Nationality = @Nationality, 
                    IDPassportNo = @IDPassportNo, Address = @Address, 
                    Company = @Company, VATCode = @VATCode
                    WHERE CustomerID = @TenantID";

                    await conn.ExecuteAsync(sqlUpdateCust, dto, trans);
                }

                // --- 2. XỬ LÝ BẢNG CM_ContractTenant (Thông tin lưu trú trong hợp đồng) ---

                // Kiểm tra xem Tenant này đã có trong Hợp đồng này chưa (dựa vào ContractTenantID)
                if (dto.ContractTenantID > 0)
                {
                    // TRƯỜNG HỢP UPDATE: Đã tồn tại bản ghi liên kết
                    string sqlUpdateContract = @"
                    UPDATE CM_ContractTenant SET 
                    FamilyPos = @FamilyPos, 
                    IsMoveOut = @IsMoveOut, 
                    VisaNo = @VisaNo, 
                    VisaDate = @VisaDate, 
                    VisaExpDate = @VisaExpDate, 
                    EntryDate = @EntryDate, 
                    ArrivalPort = @ArrivalPort, 
                    PermitExpDate = @PermitExpDate,
                    ProposeExpDate = @ProposeExpDate,
                    Notes = @Notes, 
                    Sponsor = @Sponsor,
                    TenantType = @TenantType
                    WHERE ContractTenantID = @ContractTenantID";

                    await conn.ExecuteAsync(sqlUpdateContract, dto, trans);
                }
                else
                {
                    // TRƯỜNG HỢP INSERT: Lần đầu add khách này vào hợp đồng
                    string sqlInsertContract = @"
                    INSERT INTO CM_ContractTenant 
                    (ContractID, TenantID, FamilyPos, IsMoveOut, VisaNo, VisaDate, VisaExpDate, EntryDate, ArrivalPort, PermitExpDate,ProposeExpDate,Notes, Sponsor,TenantType)
                    VALUES (@ContractID, @TenantID, @FamilyPos, @IsMoveOut, @VisaNo, @VisaDate, @VisaExpDate, @EntryDate, @ArrivalPort,@PermitExpDate,@ProposeExpDate, @Notes, @Sponsor,@TenantType)";

                    await conn.ExecuteAsync(sqlInsertContract, dto, trans);
                }

                trans.Commit();
                return new JsonResult(new { success = true, message = "Save successfull!" });
            }
            catch (Exception ex)
            {
                trans.Rollback();
                // Log lỗi chi tiết tại đây nếu cần
                return new JsonResult(new { success = false, message = "System error: " + ex.Message });
            }
        }

        // Vùng 1.1: CM_Contract
        public class ContractViewModel
        {
            public int ContractID { get; set; }
            public string? ContractNo { get; set; }
            public DateTime? ContractDate { get; set; } = DateTime.Now;
            public int? ApmtID { get; set; }

            public string? ContractApartmentNo { get; set; }
            public string? CurrentApartmentNo { get; set; }
            public decimal? CurrentRentRateNVD { get; set; }
            
            public double? PerVAT { get; set; }
            public decimal? TotalPriceExcVATVND { get; set; }
            public string? ReceivedByID { get; set; }
            public string? ApmtContract { get; set; }
            public int? ContractStatus { get; set; }
            public int? CompanyID { get; set; }
            public string? Representator { get; set; }
            public DateTime? ContractFromDate { get; set; }
            public DateTime? ContractToDate { get; set; }
            public bool DisplayRemind { get; set; } = false;
            public bool IsRepeater { get; set; } = false;
            public int? Occupy { get; set; }
            public int ? DisPlayVATFromID { get; set; }
            public int? ContractSourceID { get; set; }
            public decimal? ElecVND_Amount { get; set; }
            public string? Remarks { get; set; }
            public bool IsShortTerm { get; set; } = true;
        }
        //vùng 1/2
        public class CM_STInfoViewModel
        {
            [Key]
            public int STContractID { get; set; }

            public int? AgentCompany { get; set; }

            public int? AgentPersonId { get; set; }
            

            public int? PaidBy { get; set; }

            public int? PMIncl { get; set; }

            public int? PMMethod { get; set; }

            public int? PMTime { get; set; }

            [StringLength(50)]
            public string? PMTimeOther { get; set; }

            [StringLength(400)]
            public string? RentalCharge { get; set; }

            [StringLength(400)]
            public string? SpecialReq { get; set; }

            [StringLength(100)]
            public string? ApmtNote { get; set; }

            public int? BankTranfer { get; set; }

            public int? PaymentByID { get; set; }

            [StringLength(254)]
            public string? PaymentInfor { get; set; }

            [StringLength(254)]
            public string? DepositInfor { get; set; }

            [StringLength(254)]
            public string? CancellationCharge { get; set; }

            [StringLength(254)]
            public string? CheckoutSuddenly { get; set; }

            [StringLength(254)]
            public string? DRP { get; set; }
        }

        // Vùng 2: CM_ContractService
        public class ContractServiceViewModel
        {
            // Các trường ánh xạ từ bảng CM_ContractService
            public long ContractServiceID { get; set; }
            public long ContractID { get; set; }
            public int ServiceID { get; set; }

            public string ServiceName { get; set; }

            public DateTime? ServiceFromDate { get; set; }
            public DateTime? ServiceToDate { get; set; }

            public int ChargeInterval { get; set; }
            public string ChargeIntervalName { get; set; }
            public int ChargeType { get; set; }
            public string ChargeTypeName { get; set; }

            public double ChargeAmount { get; set; }
            public double MaxQuantity { get; set; }
            public string Notes { get; set; }
        }
        public class ServiceViewModel
        {
            public int ServiceID { get; set; }
            public string? ServiceName { get; set; }
            public DateTime? ServiceFromDate { get; set; }
            public DateTime? ServiceToDate { get; set; }
            
            public int ChargeInterval { get; set; }
            public string? ChargeIntervalName { get; set; }

            public int ChargeType { get; set; }
            public string? ChargeTypeName { get; set; }

            public double ChargeAmount { get; set; }
            public double MaxQuantity { get; set; }
            public string Notes { get; set; }

        }

        // Vùng 3: CM_ContractTenant
        /*
           SELECT ct.ContractTenantID, ct.ContractID, ct.TenantID,t.Title, t.CustomerName, 
                t.Male, t.Birthday, n.NationName, t.IDPassportNo, ct.TenantType, 
                tp.PositionName, ct.IsMoveOut, ct.VisaNo,
                ct.VisaDate, ct.EntryDate, ct.A_DCardNo, ct.MoveinDate, 
                p.PortName, ct.ProposeExpDate,
                ct.PermitExpDate, ct.Sponsor, ct.LastRegDate, ct.Notes 
                FROM CM_ContractTenant ct
                JOIN CM_Customer t ON ct.TenantID = t.CustomerID
                LEFT JOIN MS_Nation n ON t.Nationality = n.NationID
                LEFT JOIN CM_TenantPosition tp ON ct.FamilyPos = tp.PositionID
                LEFT JOIN MS_ArrivalPort p ON ct.ArrivalPort = p.PortID 
                WHERE ct.ContractID = @CID";
         */
        public class TenantViewModel
        {
            // Các trường định danh (Dùng để Update/Delete)
            public int id { get; set; } // Chính là ID của bảng CM_ContractTenant_Doc hoặc CM_ContractTenant
            public int ContractTenantID { get; set; }
            public int TenantID { get; set; } // CustomerID
            public int ContractID { get; set; }

            // Thông tin chi tiết trong Modal (Tab 1)
            public string? Title { get; set; }
            public string? CustomerName { get; set; }
            public bool? Male { get; set; }
            public DateTime? Birthday { get; set; }
            public int? TenantType { get; set; } // ID lưu loại Tenant(Freigner,VN-Oversea,... )
            public int? Nationality { get; set; } // ID quốc gia để lưu
            public string? NationName { get; set; } // Tên quốc gia để hiện trên Grid
            public string? IDPassportNo { get; set; }
            public DateTime? PassportUntilDate { get; set; }

            public string? PositionName { get; set; } // Tên vị trí (Position) để hiện trên Grid
            public DateTime? MoveinDate { get; set; }


            public string? Address { get; set; }
            public string? Company { get; set; }
            public string? VATCode { get; set; }


            public int? FamilyPos { get; set; } // ID vị trí để lưu
            public bool? IsMoveOut { get; set; }
            public string? VisaNo { get; set; }
            public DateTime? VisaDate { get; set; }
            public DateTime? VisaExpDate { get; set; }

            public DateTime? EntryDate { get; set; }
            public int? ArrivalPort { get; set; }

            public DateTime? ProposeExpDate { get; set; }
            public DateTime? PermitExpDate { get; set; }// 
            

            public string? A_DCardNo { get; set; } // Phải có gạch dưới y hệt JSON
            public DateTime? LastRegDate { get; set; }
            
            public string? Notes { get; set; }
            public string? Sponsor { get; set; }

          
            
        }
        public class ContractHistoryViewModel
        {
            public string EmployeeName { get; set; }
            public string Description { get; set; }
            public DateTime RecordTime { get; set; }
        }

    }
}