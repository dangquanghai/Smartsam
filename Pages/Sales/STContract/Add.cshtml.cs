using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.ComponentModel.DataAnnotations;
using System;
using System.Collections.Generic;
using SmartSam.Helpers;
using Google.Api;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using System.Data;
using SmartSam.Services;


namespace SmartSam.Pages.Sales.STContract
{
    public class AddModel : PageModel
    {
        // Thuộc tính BindProperty giúp tự động map dữ liệu từ Form vào Object này
        [BindProperty]
        public ContractViewModel Contract { get; set; } = new ContractViewModel();

        [BindProperty]
        public List<ServiceViewModel> Services { get; set; } = new List<ServiceViewModel>();

        [BindProperty]
        public List<TenantViewModel> Tenants { get; set; } = new List<TenantViewModel>();

        [BindProperty]
       

        // Properties hỗ trợ SelectList
        public SelectList StatusList { get; set; }
        public SelectList ReceiverList { get; set; }

        public SelectList VATFromList { get; set; }// danh sách hiển thị thông tin VAT từ Company/Agent Company/Customer
        public SelectList SourceList { get; set; }
        public SelectList ApmtList { get; set; }

        public SelectList CompanyList { get; set; }
        public SelectList AgentCompanyList { get; set; }
        


        public void OnGet(int? id)
        {
            // Nếu có ID, đây là trường hợp Edit/Copy, bạn sẽ thực hiện SQL Query tại đây
            // Nếu không có ID, các Object đã được khởi tạo mới ở trên
        }

        public IActionResult OnPost()
        {
            if (!ModelState.IsValid)
            {
                return Page();
            }

            // Logic lưu dữ liệu vào Database cho 3 vùng sẽ nằm ở đây
            // 1. Lưu CM_Contract -> Lấy ID mới
            // 2. Lưu CM_ContractService dựa trên ID mới
            // 3. Lưu CM_ContractTenant dựa trên ID mới

            return RedirectToPage("./Index");
        }

        // --- CÁC ĐỊNH NGHĨA MODEL KHỚP VỚI SQL CỦA BẠN ---

        // Vùng 1: CM_Contract
        public class ContractViewModel
        {
            public int ContractID { get; set; }
            public string? ContractNo { get; set; }
            public DateTime? ContractDate { get; set; } = DateTime.Now;
            public int? ApmtID { get; set; }
            public decimal? CurrentRentRateVND { get; set; }
            
            public double? PerVAT { get; set; }
            public decimal? TotalPriceExcVATVND { get; set; }
            public string? ReceivedBy { get; set; }
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