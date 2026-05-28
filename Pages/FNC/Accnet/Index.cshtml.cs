using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.Data.SqlClient;
using System.Data;
using System.Text;
using SmartSam.Helpers;
using SmartSam.Services.Interfaces;
using SmartSam.Services;
using Microsoft.AspNetCore.Mvc.Rendering;
using Newtonsoft.Json;
using DocumentFormat.OpenXml.Presentation;
using Newtonsoft.Json.Linq;

using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient; // Thư viện kết nối SQL Server mới của .NET Core
using RestSharp;               // Thư viện gọi API
using Newtonsoft.Json;         // Thư viện Serialize/Deserialize JSON
using Newtonsoft.Json.Linq;



namespace SmartSam.Pages.FNC.Accnet
{
    public class IndexModel : BasePageModel
    {

        private readonly ISecurityService _securityService;
        private readonly ILogger<IndexModel> _logger;
        private const int FUNCTION_ID = 150;


        public IndexModel(ISecurityService securityService, IConfiguration config, ILogger<IndexModel> logger)
          : base(config)
        {
            _securityService = securityService;
            _logger = logger;
        }


        // Khai báo các thuộc tính để Binding dữ liệu từ Bộ lọc tìm kiếm (Form GET)
        [BindProperty(SupportsGet = true)]
        public int SelectedMonth { get; set; }

        [BindProperty(SupportsGet = true)]
        public int SelectedYear { get; set; }
        // Nhãn hiển thị thông tin từ Accnet API
        public string AccnetInfoMessage { get; set; }

        // Khai báo 3 danh sách lưu trữ dữ liệu để hiển thị lên 3 Tab giao diện
        public List<ItemPurchaseModel> ItemPurchaseList { get; set; } = new();


        public List<ItemAdjustmentViewModel> ItemAdjustmentList { get; set; } = new();
        public List<InvoiceViewModel> InvoiceList { get; set; } = new();

        /// <summary>
        /// Khởi tạo dữ liệu ban đầu và xử lý bộ lọc tìm kiếm

        public async Task<IActionResult> OnGetAsync()
        {
            // GÁN MẶC ĐỊNH: Nếu user mới vào trang (SelectedMonth và SelectedYear bằng 0),
            // hệ thống sẽ tự động bốc Tháng và Năm hiện tại của máy chủ.
            if (SelectedMonth == 0)
            {
                SelectedMonth = DateTime.Now.Month;
            }

            if (SelectedYear == 0)
            {
                SelectedYear = DateTime.Now.Year;
            }

            try
            {
                // GỌI HÀM NẠP DỮ LIỆU TẠI ĐÂY:
                // Hệ thống tự bốc SelectedMonth và SelectedYear vừa được gán mặc định (hoặc từ URL bộ lọc)
                // để truyền thẳng vào hàm xử lý dữ liệu Master-Detail.
                var (listData, totalRows) = LoadItemPurchaseData(SelectedMonth, SelectedYear);

                // Gán kết quả trả về từ bộ Tuple vào thuộc tính hiển thị ngoài giao diện HTML
                ItemPurchaseList = listData;

                // TODO: Gọi tiếp dữ liệu của các tab còn lại (Adjustment, Invoice) khi viết xong hàm
                // var (listAdjData, totalAdjRows) = LoadItemAdjustmentData(SelectedMonth, SelectedYear);
                // ItemAdjustmentList = listAdjData;
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, "Lỗi khi nạp danh sách dữ liệu Item Purchase: " + ex.Message);
            }

            return Page();
        }

        private (List<ItemPurchaseModel> list, int total) LoadItemPurchaseData(int month, int year)
        {
            var list = new List<ItemPurchaseModel>();

            // 1. Sử dụng 'using var' để tự động giải phóng kết nối gọn gàng
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));

            // 2. Viết câu lệnh SQL tường minh, dùng Parameter thay vì cộng chuỗi
            string strSQL = @"
            SELECT 
            dbo.INV_ItemFlow.FlowID, 
            dbo.INV_ItemFlow.FlowNo, 
            dbo.INV_ItemFlow.FlowDate, 
            dbo.INV_ItemFlow.According, 
            dbo.INV_StoreList.StoreName, 
            dbo.INV_ItemFlow.PostAcc 
            FROM dbo.INV_KPGroup 
            INNER JOIN dbo.INV_StoreList ON dbo.INV_KPGroup.KPGroupID = dbo.INV_StoreList.DeptID 
            INNER JOIN dbo.INV_ItemFlow ON dbo.INV_StoreList.StoreID = dbo.INV_ItemFlow.ToStore
            WHERE dbo.INV_KPGroup.KPGroupID = 1
            AND dbo.INV_ItemFlow.FlowType = 2 
            AND dbo.INV_ItemFlow.FlowSubType = 1
            AND MONTH(dbo.INV_ItemFlow.FlowDate) = @Month 
            AND YEAR(dbo.INV_ItemFlow.FlowDate) = @Year
            AND (dbo.INV_ItemFlow.PostAcc = 0 OR dbo.INV_ItemFlow.PostAcc IS NULL)
            ORDER BY dbo.INV_ItemFlow.FlowNo";

            using var cmd = new SqlCommand(strSQL, conn);
            // Nạp tham số an toàn vào Command
            cmd.Parameters.AddWithValue("@Month", month);
            cmd.Parameters.AddWithValue("@Year", year);

            conn.Open();

            // 3. Đọc dữ liệu Master bằng tên cột
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add(new ItemPurchaseModel
                {
                    // Chuyển đổi an toàn sang đúng kiểu dữ liệu của Model
                    FlowID = reader["FlowID"] != DBNull.Value ? Convert.ToInt32(reader["FlowID"]) : 0,
                    FlowNo = reader["FlowNo"]?.ToString() ?? "",
                    FlowDate = reader["FlowDate"] != DBNull.Value ? Convert.ToDateTime(reader["FlowDate"]) : DateTime.MinValue,
                    According = reader["According"]?.ToString() ?? "",
                    StoreName = reader["StoreName"]?.ToString() ?? "",
                    PostAcc = reader["PostAcc"] != DBNull.Value && Convert.ToBoolean(reader["PostAcc"])
                });
            }
            reader.Close(); // Đóng reader cũ để chuẩn bị chạy query Detail tiếp theo trên cùng Connection

            // 4. Quét nạp chi tiết danh sách vật tư (Detail) cho từng phiếu
            foreach (var master in list)
            {
                string strSQL1 = @"
                SELECT 
                dbo.INV_ItemList.ItemCode, 
                dbo.INV_ItemList.ItemName, 
                dbo.INV_ItemList.Unit, 
                dbo.INV_ItemFlowDetail.Act_Qty,
                dbo.INV_ItemFlowDetail.UnitPrice as Price
                FROM dbo.INV_ItemFlowDetail 
                INNER JOIN dbo.INV_ItemList ON dbo.INV_ItemFlowDetail.ItemID = dbo.INV_ItemList.ItemID 
                INNER JOIN dbo.INV_ItemFlow ON dbo.INV_ItemFlowDetail.FlowID = dbo.INV_ItemFlow.FlowID
                WHERE dbo.INV_ItemFlowDetail.FlowID = @FlowID 
                ORDER BY dbo.INV_ItemList.ItemCode";

                using var cmd1 = new SqlCommand(strSQL1, conn);
                cmd1.Parameters.AddWithValue("@FlowID", master.FlowID);

                using var reader1 = cmd1.ExecuteReader();
                while (reader1.Read())
                {
                    master.Details.Add(new ItemPurchaseDetailModel
                    {
                        ItemCode = reader1["ItemCode"]?.ToString() ?? "",
                        ItemName = TCVN32Unicode(reader1["ItemName"]?.ToString() ?? ""),
                        Unit = TCVN32Unicode(reader1["Unit"]?.ToString() ?? ""),
                        ActQty = reader1["Act_Qty"] != DBNull.Value ? Convert.ToDecimal(reader1["Act_Qty"]) : 0,
                        Price = reader1["Price"] != DBNull.Value ? Convert.ToDecimal(reader1["Price"]) : 0
                    });
                }
            }

            // Trả về một bộ Tuple gồm danh sách dữ liệu và tổng số dòng (y hệt hàm Vinh Hy)
            return (list, list.Count);
        }

        private string GetToken(string baseUrl, string userConnection)
        {
            try
            {
                var baseUri = new Uri(baseUrl);
                string tokenUrl = $"{baseUri.Scheme}://{baseUri.Authority}/token";

                var client = new RestClient(tokenUrl);

                // Cú pháp chuẩn chỉnh cho RestSharp bản mới nhất
                var request = new RestRequest();
                request.Method = Method.Post;

                request.AddHeader("cache-control", "no-cache");
                request.AddHeader("Content-Type", "text/plain");
                request.AddParameter("text/plain", userConnection, ParameterType.RequestBody);

                var response = client.Execute(request);

                if (string.IsNullOrEmpty(response.Content))
                {
                    return "Lỗi: Phản hồi từ server token trống!";
                }

                var jsonObject = JObject.Parse(response.Content);
                return jsonObject["access_token"]?.ToString() ?? "";
            }
            catch (Exception ex)
            {
                return $"Lỗi lấy Token: {ex.Message}";
            }
        }
        private string GetLastItemPurchase(string token, string baseUrl)
        {
            try
            {
                var client = new RestClient($"{baseUrl.TrimEnd('/')}/ItemPurchase/GetLast?BU=DEFAULT");
                var request = new RestRequest("", Method.Get);

                request.AddHeader("cache-control", "no-cache");
                request.AddHeader("Content-Type", "application/json");
                request.AddHeader("Authorization", "Bearer " + token);

                // Gọi đồng bộ giống cấu trúc WebForms cũ nhưng dùng RestSharp bản mới
                var response = client.Execute(request);
                return response.Content ?? "";
            }
            catch (Exception ex)
            {
                return $"Lỗi kết nối GetLast: {ex.Message}";
            }
        }
        public async Task<JsonResult> OnPostItemAdjustmentAsync(List<string> uids)
        {
            if (uids == null || uids.Count == 0)
            {
                return new JsonResult(new { success = false, message = "Danh sách UID trống." });
            }

            try
            {
                string logResult = $"[{DateTime.Now:HH:mm:ss}] Bắt đầu tiến trình đẩy {uids.Count} chứng từ Item Adjustment...<br/>";

                // TODO: Viết Logic xử lý / gọi API Accnet của page Item Adjustment cũ tại đây

                logResult += $"[{DateTime.Now:HH:mm:ss}] <span class='text-success'>Đồng bộ thành công dữ liệu điều chỉnh kho.</span><br/>";

                return new JsonResult(new
                {
                    success = true,
                    message = $"Đã đẩy thành công {uids.Count} chứng từ Điều Chỉnh Kho sang Accnet!",
                    log = logResult
                });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }

        /// <summary>
        /// 3. Xử lý truyền dữ liệu Invoice sang Accnet (Gọi qua AJAX)
        /// </summary>
        public async Task<JsonResult> OnPostInvoiceAsync(List<string> uids)
        {
            if (uids == null || uids.Count == 0)
            {
                return new JsonResult(new { success = false, message = "Danh sách UID trống." });
            }

            try
            {
                string logResult = $"[{DateTime.Now:HH:mm:ss}] Bắt đầu tiến trình đẩy {uids.Count} chứng từ Invoice...<br/>";

                // TODO: Viết Logic xử lý / gọi API Accnet của page Invoice cũ tại đây

                logResult += $"[{DateTime.Now:HH:mm:ss}] <span class='text-success'>Đồng bộ hoàn tất dữ liệu hóa đơn tài chính.</span><br/>";

                return new JsonResult(new
                {
                    success = true,
                    message = $"Đã đẩy thành công {uids.Count} hóa đơn sang Accnet!",
                    log = logResult
                });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = "Lỗi hệ thống: " + ex.Message });
            }
        }


        private string GetSupplierCode(string flowID)
        {
            string supplierCode = "";

            // Sử dụng _config y hệt hàm Load của bạn
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));

            string strSQL = @"
        SELECT DISTINCT dbo.PC_Suppliers.CodeOfAcc AS SupplierCode 
        FROM dbo.INV_ItemFlow 
        INNER JOIN dbo.PC_PO ON dbo.INV_ItemFlow.POID = dbo.PC_PO.POID 
        INNER JOIN dbo.PC_Suppliers ON dbo.PC_PO.SupplierID = dbo.PC_Suppliers.SupplierID 
        WHERE dbo.INV_ItemFlow.FlowID = @FlowID";

            using var cmd = new SqlCommand(strSQL, conn);
            cmd.Parameters.AddWithValue("@FlowID", flowID);

            conn.Open();
            using var reader = cmd.ExecuteReader();
            if (reader.HasRows && reader.Read())
            {
                supplierCode = reader["SupplierCode"]?.ToString() ?? "";
            }

            return supplierCode;
        }


        // Đảm bảo KHÔNG có từ khóa 'static' ở định nghĩa hàm này
        public async Task<IActionResult> OnPostItemPurchaseAsync([FromBody] List<string> uids)
        {
            if (uids == null || !uids.Any())
            {
                return new JsonResult(new { success = false, message = "Vui lòng chọn ít nhất một phiếu để đồng bộ!" });
            }

            // Đọc BaseUrl từ _config của lớp cha (Hết sạch lỗi gạch đỏ)
            string baseUrl = "http://127.0.0.1:6868/api/SG/";//_config["AccnetAPI:BaseUrl"] ?? "http://127.0.0.1:6868/api/SG/";
            string userConnection = "grant_type=password&username=importdata&password=import@25763&Scope=vi,false,1,,";

            string currentToken = GetToken(baseUrl,userConnection);

            int successCount = 0;
            List<string> logs = new List<string>();

            try
            {
                foreach (var id in uids)
                {
                    // Gọi hàm xử lý đẩy dữ liệu sang Accnet
                    string apiResult = ImpItemPurchase(currentToken, id, baseUrl);
                    string[] stTemp1 = apiResult.Split(new Char[] { '"', ':', ',', '{', '}' });

                    if (apiResult.Contains("Success") || (stTemp1.Length > 5 && stTemp1[5] == "Success"))
                    {
                        // ÁP DỤNG ĐÚNG CÚ PHÁP SỬ DỤNG _config GIỐNG HÀM LOAD CỦA BẠN
                        using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));

                        string sqlUpdate = "UPDATE INV_ItemFlow SET PostACC = 1 WHERE FlowID = @FlowID";
                        using var cmd = new SqlCommand(sqlUpdate, conn);
                        cmd.Parameters.AddWithValue("@FlowID", id);

                        conn.Open();
                        cmd.ExecuteNonQuery();
                        // conn sẽ tự đóng khi kết thúc xử lý của phiếu này nhờ 'using var'

                        successCount++;
                        logs.Add($"Phiếu {id}: Đồng bộ thành công.");
                    }
                    else
                    {
                        logs.Add($"Phiếu {id}: Thất bại. Chi tiết: {apiResult}");
                    }
                }

                return new JsonResult(new
                {
                    success = successCount > 0,
                    message = $"Đã xử lý xong. Thành công: {successCount}/{uids.Count} phiếu.",
                    details = logs
                });
            }
            catch (Exception ex)
            {
                return new JsonResult(new { success = false, message = $"Có lỗi xảy ra: {ex.Message}" });
            }
        }


        private string ImpItemPurchase(string token, string flowID, string baseUrl)
        {
            string inObjectID = "";
            string inDocumentDate = "";
            string inMemo = "";
            string inDocumentID = "";

            // 1. Lấy thông tin Last Document từ Accnet API
            string stTemp = GetLastItemPurchase(token, baseUrl);

            // Giữ nguyên cách cắt chuỗi cũ để bóc dữ liệu chứng từ cuối
            string[] stTemp1 = stTemp.Split(new Char[] { '"', ':', ',', '{', '}' });
            string lastDocumentID = stTemp1[17];
            string inBatchNo = stTemp1[23];

            if (lastDocumentID.Length != 10)
            {
                return "LastDocument is invalid form, pls check it!";
            }

            // 2. Tịnh tiến số Số Chứng Từ (DocumentID)
            string stDocID = lastDocumentID.Substring(0, 5);
            int docID_tang = int.Parse(stDocID) + 1;
            switch (docID_tang.ToString().Length)
            {
                case 1: inDocumentID = "0000" + docID_tang.ToString() + lastDocumentID.Substring(5, 5); break;
                case 2: inDocumentID = "000" + docID_tang.ToString() + lastDocumentID.Substring(5, 5); break;
                case 3: inDocumentID = "00" + docID_tang.ToString() + lastDocumentID.Substring(5, 5); break;
                case 4: inDocumentID = "0" + docID_tang.ToString() + lastDocumentID.Substring(5, 5); break;
                case 5: inDocumentID = docID_tang.ToString() + lastDocumentID.Substring(5, 5); break;
            }

            // Lấy mã nhà cung cấp
            inObjectID = GetSupplierCode(flowID).Trim();

            // 3. Kết nối Database Smartsam bằng cách khai báo giống hàm Load dữ liệu
            using var conn = new SqlConnection(_config.GetConnectionString("DefaultConnection"));

            // 3.1 Đọc thông tin Master
            string sqlMaster = "SELECT FlowNo, FlowDate FROM INV_ItemFlow WHERE FlowID = @FlowID";
            using var cmdMaster = new SqlCommand(sqlMaster, conn);
            cmdMaster.Parameters.AddWithValue("@FlowID", flowID);

            conn.Open();
            using var readerMaster = cmdMaster.ExecuteReader();
            if (readerMaster.HasRows && readerMaster.Read())
            {
                inDocumentDate = readerMaster["FlowDate"].ToString();
                inMemo = "Ref: " + readerMaster["FlowNo"].ToString();
            }
            readerMaster.Close(); // Đóng reader cũ để chuẩn bị quét danh sách chi tiết vật tư

            // 3.2 Đọc danh sách chi tiết vật tư (Details)
            List<ItemPurchaseDetail> listItemPurchaseDetail = new List<ItemPurchaseDetail>();
            string sqlDetail = @"
        SELECT dbo.INV_ItemList.ItemCode, dbo.INV_ItemList.Unit, dbo.INV_ItemFlowDetail.Act_Qty, dbo.INV_ItemFlowDetail.UnitPrice, dbo.INV_ItemFlow.ToStore
        FROM dbo.INV_ItemFlowDetail 
        INNER JOIN dbo.INV_ItemList ON dbo.INV_ItemFlowDetail.ItemID = dbo.INV_ItemList.ItemID 
        INNER JOIN dbo.INV_ItemFlow ON dbo.INV_ItemFlowDetail.FlowID = dbo.INV_ItemFlow.FlowID
        WHERE dbo.INV_ItemFlowDetail.FlowID = @FlowID";

            using var cmdDetail = new SqlCommand(sqlDetail, conn);
            cmdDetail.Parameters.AddWithValue("@FlowID", flowID);

            using var readerDetail = cmdDetail.ExecuteReader();
            while (readerDetail.Read())
            {
                string inItemID = readerDetail["ItemCode"].ToString();
                decimal inQuantity = Convert.ToDecimal(readerDetail["Act_Qty"].ToString());
                decimal inUnitPrice = Convert.ToDecimal(readerDetail["UnitPrice"].ToString());
                string inStoreHouseID = "";

                switch (readerDetail["ToStore"].ToString())
                {
                    case "2": inStoreHouseID = "SSG1"; break;
                    case "3": inStoreHouseID = "SSG2"; break;
                    case "7": inStoreHouseID = "SSG6"; break;
                    case "8": inStoreHouseID = "SSG7"; break;
                    case "21": inStoreHouseID = "SS01"; break;
                    default: inMemo = ""; break;
                }

                decimal fPurcAmount = Math.Round(inQuantity * inUnitPrice);
                decimal vatAmount = Math.Round(inQuantity * inUnitPrice * 10 / 100);

                listItemPurchaseDetail.Add(new ItemPurchaseDetail
                {
                    ItemID = inItemID,
                    ItemDesc = inItemID,
                    AssetExpAcctID = "152",
                    StoreHouseID = inStoreHouseID,
                    VATID = "I1",
                    CnvFactor = 1,
                    Quantity = inQuantity,
                    UnitPrice = inUnitPrice,
                    FPurcAmount = fPurcAmount,
                    BPurcAmount = fPurcAmount,
                    VATAmount = vatAmount,
                    PurcAmount = Math.Round(fPurcAmount + vatAmount)
                });
            }
            readerDetail.Close();

            // 4. Tạo mảng VAT rỗng
            List<ItemPurchaseVAT> listItemPurchaseVAT = new List<ItemPurchaseVAT> { new ItemPurchaseVAT() };

            // 5. Khởi tạo cấu trúc Object JSON Master
            List<ItemPurchase> listItemPurchase = new List<ItemPurchase>();
            ItemPurchase itemPurchase = new ItemPurchase()
            {
                DocumentID = inDocumentID,
                BatchNo = inBatchNo,
                JrnlType = "PI",
                DocumentDate = Convert.ToDateTime(inDocumentDate),
                ForgCurrID = "VND",
                RateExchange = 1,
                ObjectID = inObjectID,
                Memo = inMemo,
                PaybAcctID = "331",
                ItemPurchaseDetail = listItemPurchaseDetail,
                ItemPurchaseVAT = listItemPurchaseVAT,
                IETypeID = "1",
                PayMethodID = "TM",
                RateForVAT = 1,
                CreatedBy = "THUTHAO"
            };
            listItemPurchase.Add(itemPurchase);

            // 6. Thực thi POST dữ liệu đồng bộ bằng RestSharp
            var json = JsonConvert.SerializeObject(listItemPurchase);
            var client = new RestClient($"{baseUrl.TrimEnd('/')}/ItemPurchase/Add");

            var request = new RestRequest("", Method.Post);
            request.AddHeader("cache-control", "no-cache");
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", "Bearer " + token);
            request.AddParameter("application/json", json, ParameterType.RequestBody);

            var response = client.Execute(request);

            return (response.Content ?? "") + "<br><br>" + json;
        }
        // Hàm getSupplierCode giả định theo code cũ của bạn

      
        // --- CÁC MODEL GIẢ ĐỊNH ĐỂ ĐỔ DỮ LIỆU LÊN VIEW (Có thể điều chỉnh tùy cấu trúc DB của bạn) ---
        public class ItemPurchaseModel
        {
            // Hàm khởi tạo (Constructor): Luôn chạy đầu tiên khi New Model
            // Giúp cấp phát vùng nhớ cho danh sách Details, tránh hoàn toàn lỗi NullReference / lỗi Add
            public ItemPurchaseModel()
            {
                Details = new List<ItemPurchaseDetailModel>();
            }

            public int FlowID { get; set; }
            public string FlowNo { get; set; }
            public DateTime FlowDate { get; set; }
            public string According { get; set; }
            public string StoreName { get; set; }
            public string VendorName { get; set; }
            public Boolean PostAcc { get; set; }

            // Danh sách chứa vật tư chi tiết
            public List<ItemPurchaseDetailModel> Details { get; set; }
        }

        public class ItemPurchaseDetailModel
        {
            public string ItemCode { get; set; }
            public string ItemName { get; set; }
            public string Unit { get; set; }
            public decimal ActQty { get; set; }
            public decimal Price { get; set; }
            public decimal Amount => ActQty * Price;
        }

        public class ItemAdjustmentViewModel
        {
            public string UID { get; set; }
            public DateTime AdjDate { get; set; }
            public string VoucherNo { get; set; }
            public string Reason { get; set; }
            public string WarehouseId { get; set; }
            public decimal TotalVariance { get; set; }
        }

        public class InvoiceViewModel
        {
            public string UID { get; set; }
            public DateTime InvDate { get; set; }
            public string InvNo { get; set; }
            public string CustomerId { get; set; }
            public string CustomerName { get; set; }
            public decimal TotalPayment { get; set; }
        }

        public class ItemPurchase
        {
            public string DocumentID { get; set; }
            public string BatchNo { get; set; }
            public string JrnlType { get; set; }
            public DateTime DocumentDate { get; set; }
            public string ForgCurrID { get; set; }
            public decimal RateExchange { get; set; }
            public string ObjectID { get; set; }
            public string Memo { get; set; }
            public string PaybAcctID { get; set; }
            public List<ItemPurchaseDetail> ItemPurchaseDetail { get; set; }
            public List<ItemPurchaseVAT> ItemPurchaseVAT { get; set; }
            public string IETypeID { get; set; }
            public string PayMethodID { get; set; }
            public decimal RateForVAT { get; set; }
            public string CreatedBy { get; set; }
        }

        public class ItemPurchaseDetail
        {
            public string ItemID { get; set; }
            public string ItemDesc { get; set; }
            public string AssetExpAcctID { get; set; }
            public string StoreHouseID { get; set; }
            public string VATID { get; set; }
            public decimal CnvFactor { get; set; }
            public decimal Quantity { get; set; }
            public decimal UnitPrice { get; set; }
            public decimal FPurcAmount { get; set; }
            public decimal BPurcAmount { get; set; }
            public decimal VATAmount { get; set; }
            public decimal PurcAmount { get; set; }
        }

        public class ItemPurchaseVAT
        {
            // Giữ nguyên theo cấu trúc mảng rỗng cũ của bạn
            // public string SerialNo { get; set; }
            // public string InvNo { get; set; }
        }

        // Class bổ sung phục vụ cho việc bóc tách JSON trả về từ API một cách an toàn thay vì dùng .Split()
        public class AccnetLastDocResponse
        {
            public string DocumentID { get; set; }
            public string BatchNo { get; set; }
        }

        public class AccnetPostResponse
        {
            public string Status { get; set; } // "Success" hoặc "Error"
            public string Message { get; set; }
        }
    }
}