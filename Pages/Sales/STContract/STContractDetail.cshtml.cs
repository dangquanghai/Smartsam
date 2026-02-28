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

namespace SmartSam.Pages.Sales.STContract
{
    // 1. QUAN TRỌNG: Đổi PageModel thành BasePageModel
    public class STContractDetailModel : BasePageModel
    {
        // Constructor truyền config vào BasePageModel
        public STContractDetailModel(IConfiguration config) : base(config) { }

        
        [BindProperty(SupportsGet = true)]
        public string Mode { get; set; } // Sẽ nhận "add", "edit", hoặc "view" từ URL

        [BindProperty]
        public ContractViewModel Contract { get; set; } = new ContractViewModel();

        public CM_STInfoViewModel STInfor { get; set; } = new CM_STInfoViewModel();
        public ServiceViewModel CT_Services { get; set; } = new ServiceViewModel();
        public TenantViewModel CT_Tenants { get; set; } = new TenantViewModel();
        
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

        public void OnGet(int? id, string mode)
        {
            // Cập nhật Mode từ query string (nếu có)
            Mode = mode ?? Mode;

            if (id.HasValue && id > 0)
            {
                // Nếu có ID, ưu tiên mode view nếu không được chỉ định
                if (string.IsNullOrEmpty(Mode) || Mode == "add") Mode = "view";

                // GỌI HÀM LOAD DỮ LIỆU THỰC TẾ Ở ĐÂY
                LoadContractData(id.Value);
            }
            else
            {
                Mode = "add";
                Contract = new ContractViewModel
                {
                    ContractNo = GetNewSTContractNo(),
                    ContractDate = DateTime.Now,
                    ContractStatus = 1,
                    PerVAT = 10,
                    
                    ContractFromDate = DateTime.Now.Date,
                    ContractToDate = DateTime.Now.Date.AddMonths(1)
                };
                STInfor = new CM_STInfoViewModel();
            }

            // 2. Load Dropdowns
            LoadAllDropdowns(isNew: (Mode == "add"));

            // 3. Load danh sách Agent Person dựa trên Company đã lưu
            if (STInfor != null && STInfor.AgentCompany.HasValue)
            {
                AgentPersonList = FetchAgentPersons(STInfor.AgentCompany.Value);
            }
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
        public IActionResult OnPost()
        {
            if (!ModelState.IsValid)
            {
                LoadAllDropdowns(isNew: (Contract.ContractID == 0));
                return Page();
            }

            string connString = _config.GetConnectionString("DefaultConnection");

            using (var conn = new SqlConnection(connString))
            {
                conn.Open();
                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        bool isNewAtStart = (Contract.ContractID == 0); // Đánh dấu trạng thái lúc bắt đầu bấm nút Save
                        // 1. Thực hiện lưu (SaveContract sẽ gán ID mới vào Contract.ContractID nếu là Insert)
                        this.SavedContractID = SaveContract(conn, trans);
                        SaveContractApmt(Contract.ContractID, conn, trans);
                        SaveSTInfo(conn, trans);

                        trans.Commit();
                        Mode = "edit";
                        TempData["SuccessMessage"] = "Save Contract already.You can update.";
                        TempData.Keep("SuccessMessage");
                        LoadAllDropdowns(isNew: isNewAtStart);
                        if (STInfor.AgentCompany.HasValue)
                        {
                            AgentPersonList = FetchAgentPersons(STInfor.AgentCompany.Value);
                        }

                        // Trả về trang hiện tại với dữ liệu cũ + ID mới vừa sinh ra
                        return Page();
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        ModelState.AddModelError("", "Error: " + ex.Message);
                        LoadAllDropdowns(isNew: (Contract.ContractID == 0));
                        return Page();
                    }
                }
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
                Remarks, CreatedBy, CreatedDate
            )
            VALUES (
                @ContractNo, @ContractDate, @ApmtID, @ApmtNo, @ApmtNo,
                @CurrentRentRateNVD, @PerVAT, @TotalPriceExcVATVND, @DisPlayVATFromID,
                @ContractStatus, @CompanyID,
                @ContractFromDate, @ContractToDate, @PlanCheckinDate, @PlanCheckoutDate,
                @IsRepeater, @Occupy, @ContractSourceID, @ReceivedByID,
                @Remarks, @User, GETDATE()
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
                UpdatedBy = @User, 
                UpdatedDate = GETDATE()
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
            SELECT cs.ContractServiceID, sv.ServiceName, 
            cs.ServiceFromDate, cs.ServiceToDate, 
            itv.ChargeIntervalName, ct.ChargeTypeName, 
            cs.MaxQuantity, cs.Notes
            FROM CM_ContractService cs 
            INNER JOIN SV_ServiceList sv ON cs.ServiceID = sv.ServiceID
            INNER JOIN SV_ChargeIntervalFL itv ON cs.ChargeInterval = itv.ChargeIntervalID 
            INNER JOIN SV_ChargeTypeFL ct ON cs.ChargeType = ct.ChargeTypeID 
            WHERE cs.ContractID = @ContractID ";

            using (var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection")))
            {
                var services = await conn.QueryAsync(sql, new { ContractID = contractId });
                return new JsonResult(services);
            }
        }
        private async Task LoadContractServices(long contractId)
        {
            string sql = @"
            SELECT cs.ContractServiceID, cs.ContractID, sv.ServiceName, 
            cs.ServiceFromDate, cs.ServiceToDate, 
            itv.ChargeIntervalName, ct.ChargeTypeName, 
            cs.MaxQuantity, cs.Notes 
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
            (ContractID, ServiceID, ServiceFromDate, ServiceToDate, ChargeInterval, ChargeType, MaxQuantity, Notes)
            VALUES 
            (@ContractID, @ServiceID, @ServiceFromDate, @ServiceToDate, @ChargeInterval, @ChargeType, @MaxQuantity,  @Notes)";

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
        public class ServiceViewModel
        {
            public int ServiceID { get; set; }
            public string? ServiceName { get; set; }
            public DateTime? ServiceFromDate { get; set; }
            public DateTime? ServiceToDate { get; set; }
            public string? ChargeIntervalName { get; set; }
            public string? ChargeTypeName { get; set; }
            public decimal ChargeAmount { get; set; }
            public int MaxQuantity { get; set; }
            public string? Notes { get; set; }
        }

        // Vùng 3: CM_ContractTenant
        public class TenantViewModel
        {
            public int TenantID { get; set; }
            public string? CustomerName { get; set; }
            public bool Male { get; set; }
            public DateTime? Birthday { get; set; }
            public string? Nationality { get; set; }
            public string? IDPassportNo { get; set; }
            public string? PositionName { get; set; }
            public bool IsMoveOut { get; set; }
            public string? VisaNo { get; set; }
            public DateTime? VisaDate { get; set; }
            public string? EntryDate { get; set; }
            public string? Notes { get; set; }
        }
        public class ContractServiceViewModel
        {
            // Các trường ánh xạ từ bảng CM_ContractService
            public long ContractServiceID { get; set; }
            public long ContractID { get; set; }
            public int ServiceID { get; set; }

            // Các trường để hiển thị (Join từ các bảng FL)
            public string ServiceName { get; set; }
            public string ChargeIntervalName { get; set; }
            public string ChargeTypeName { get; set; }

            // Các trường dữ liệu ngày tháng và giá trị
            public DateTime? ServiceFromDate { get; set; }
            public DateTime? ServiceToDate { get; set; }
            public int ChargeInterval { get; set; }
            public int ChargeType { get; set; }
            public double MaxQuantity { get; set; }
            public string Notes { get; set; }
        }
    }
}