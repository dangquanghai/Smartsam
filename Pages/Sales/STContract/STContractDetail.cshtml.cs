using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using System;
using System.Collections.Generic;
using SmartSam.Helpers;
using Microsoft.Data.SqlClient;
using System.Data;
using System.ComponentModel.DataAnnotations;
using Google.Api;

namespace SmartSam.Pages.Sales.STContract
{
    // 1. QUAN TRỌNG: Đổi PageModel thành BasePageModel
    public class STContractDetailModel : BasePageModel
    {
        // Constructor truyền config vào BasePageModel
        public STContractDetailModel(IConfiguration config) : base(config) { }

        [BindProperty]
        public ContractViewModel Contract { get; set; } = new ContractViewModel();

        [BindProperty]
        public CM_STInfoViewModel STInfor { get; set; } = new CM_STInfoViewModel();

        public ServiceViewModel CT_Services { get; set; } = new ServiceViewModel();
        public TenantViewModel CT_Tenants { get; set; } = new TenantViewModel();
        


        // 3. Đổi SelectList thành List<SelectListItem> để khớp với hàm LoadSelect2
        public List<SelectListItem> StatusList { get; set; }
        public List<SelectListItem> VATFromList { get; set; }
        public List<SelectListItem> SourceList { get; set; }
        public List<SelectListItem> ApmtList { get; set; }
        public List<SelectListItem> CompanyList { get; set; }
        public List<SelectListItem> AgentCompanyList { get; set; }
        public List<SelectListItem> AgentPerson { get; set; }
        public List<SelectListItem> RepresentatorList { get; set; }
        public List<SelectListItem> ReceiverList { get; set; }
        public List<SelectListItem> PaymnetByList { get; set; }


        // Biến điều khiển trạng thái giao diện
        public bool IsEditMode { get; set; }

        [HttpGet]
        public IActionResult GetAgentPersons(int companyId)
        {
            string connString = _config.GetConnectionString("DefaultConnection");
            // Sử dụng câu lệnh SQL thuần nếu bạn không dùng Entity Framework (EF)
            // Thay 'YourDatabaseHelper' bằng class bạn hay dùng để truy vấn SQL
            string sql = $" select AgentID, AgentName  from CM_AgentPerson where  CompanyID = {companyId} ORDER BY AgentName";

            // Giả sử hàm này trả về DataTable hoặc List các Object
            var data = Helper.ExecuteQuery(sql, connString);

            // Chuyển đổi dữ liệu sang List để dùng được với Helper BuildIntSelectList
            // Nếu data đã là List<T> thì bỏ qua bước này
            var personList = data.AsEnumerable().Select(row => new {
                PersonID = Convert.ToInt32(row["PersonID"]),
                PersonName = row["PersonName"].ToString()
            }).ToList();

            // Dùng Helper của bạn
            var selectList = Helper.BuildIntSelectList(personList, x => x.PersonID, x => x.PersonName);

            return new JsonResult(selectList);
        }
        public void OnGet(int? id, bool edit = false)
        {
            IsEditMode = edit;

            if (id.HasValue)
            {
                // TODO: Viết hàm Load dữ liệu từ DB cho Contract và STInfo tại đây
                LoadAllDropdowns(isNew: false);
            }
            else
            {
                IsEditMode = true;
                Contract = new ContractViewModel
                {
                    ContractNo = GetNewSTContractNo(),
                    ContractDate = DateTime.Now,
                    ContractStatus = 1,
                    PerVAT = 10
                };
                STInfor = new CM_STInfoViewModel();
                LoadAllDropdowns(isNew: true);
            }
        }

        public IActionResult OnPost()
        {
            if (!ModelState.IsValid)
            {
                LoadAllDropdowns(isNew: (Contract.ContractID == 0));
                return Page();
            }

            // SỬA LỖI: Dùng _config từ BasePageModel
            string connString = _config.GetConnectionString("DefaultConnection");

            using (var conn = new SqlConnection(connString))
            {
                conn.Open();
                using (var trans = conn.BeginTransaction())
                {
                    try
                    {
                        // 1. LƯU BẢNG CM_Contract (Vùng 1.1)
                        if (Contract.ContractID == 0)
                        {
                            string sqlInsert = @"
                                INSERT INTO CM_Contract (ContractNo, ContractDate, ContractStatus, CreatedBy, CreatedDate, ApmtID, CompanyID, Remarks)
                                VALUES (@ContractNo, @ContractDate, @ContractStatus, @User, GETDATE(), @ApmtID, @CompanyID, @Remarks);
                                SELECT SCOPE_IDENTITY();";

                            var cmd = new SqlCommand(sqlInsert, conn, trans);
                            cmd.Parameters.AddWithValue("@ContractNo", Contract.ContractNo ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@ContractDate", Contract.ContractDate ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@ContractStatus", Contract.ContractStatus ?? 1);
                            cmd.Parameters.AddWithValue("@ApmtID", Contract.ApmtID ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@CompanyID", Contract.CompanyID ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@Remarks", Contract.Remarks ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@User", User.Identity.Name ?? "System");

                            Contract.ContractID = Convert.ToInt32(cmd.ExecuteScalar());
                        }
                        else
                        {
                            string sqlUpdate = @"
                                UPDATE CM_Contract SET 
                                    ContractDate = @ContractDate, 
                                    ContractStatus = @ContractStatus,
                                    ApmtID = @ApmtID,
                                    CompanyID = @CompanyID,
                                    Remarks = @Remarks,
                                    UpdatedBy = @User, 
                                    UpdatedDate = GETDATE()
                                WHERE ContractID = @ContractID";

                            var cmd = new SqlCommand(sqlUpdate, conn, trans);
                            cmd.Parameters.AddWithValue("@ContractID", Contract.ContractID);
                            cmd.Parameters.AddWithValue("@ContractDate", Contract.ContractDate ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@ContractStatus", Contract.ContractStatus ?? 1);
                            cmd.Parameters.AddWithValue("@ApmtID", Contract.ApmtID ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@CompanyID", Contract.CompanyID ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@Remarks", Contract.Remarks ?? (object)DBNull.Value);
                            cmd.Parameters.AddWithValue("@User", User.Identity.Name ?? "System");
                            cmd.ExecuteNonQuery();
                        }

                        // 2. LƯU BẢNG CM_STInfo (Vùng 1.2) - Sử dụng Upsert logic
                        string sqlCheck = "SELECT COUNT(1) FROM CM_STInfo WHERE STContractID = @ID";
                        var cmdCheck = new SqlCommand(sqlCheck, conn, trans);
                        cmdCheck.Parameters.AddWithValue("@ID", Contract.ContractID);
                        bool exists = (int)cmdCheck.ExecuteScalar() > 0;

                        if (!exists)
                        {
                            string sqlInsertST = @"
                                INSERT INTO CM_STInfo (STContractID, AgentCompany, AgentPerson, CancellationCharge, PaymentInfor, DepositInfor, SpecialReq)
                                VALUES (@STContractID, @AgentCompany, @AgentPerson, @CancellationCharge, @PaymentInfor, @DepositInfor, @SpecialReq)";
                            var cmdST = new SqlCommand(sqlInsertST, conn, trans);
                            AddSTParameters(cmdST);
                            cmdST.ExecuteNonQuery();
                        }
                        else
                        {
                            string sqlUpdateST = @"
                                UPDATE CM_STInfo SET 
                                    AgentCompany = @AgentCompany, AgentPerson = @AgentPerson,
                                    CancellationCharge = @CancellationCharge, PaymentInfor = @PaymentInfor,
                                    DepositInfor = @DepositInfor, SpecialReq = @SpecialReq
                                WHERE STContractID = @STContractID";
                            var cmdST = new SqlCommand(sqlUpdateST, conn, trans);
                            AddSTParameters(cmdST);
                            cmdST.ExecuteNonQuery();
                        }

                        trans.Commit();
                        return RedirectToPage(new { id = Contract.ContractID, edit = true });
                    }
                    catch (Exception ex)
                    {
                        trans.Rollback();
                        ModelState.AddModelError("", "Lỗi lưu: " + ex.Message);
                        LoadAllDropdowns(isNew: (Contract.ContractID == 0));
                        return Page();
                    }
                }
            }
        }

        private void AddSTParameters(SqlCommand cmd)
        {
            cmd.Parameters.AddWithValue("@STContractID", Contract.ContractID);
            cmd.Parameters.AddWithValue("@AgentCompany", (object)STInfor.AgentCompany ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@AgentPerson", (object)STInfor.AgentPerson ?? DBNull.Value);
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


        // Vùng 1.1: CM_Contract
        public class ContractViewModel
        {
            public int ContractID { get; set; }
            public string? ContractNo { get; set; }
            public DateTime? ContractDate { get; set; } = DateTime.Now;
            public int? ApmtID { get; set; }
            public decimal? CurrentRentRateVND { get; set; }
            
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

            public int? AgentPerson { get; set; }

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
    }
}