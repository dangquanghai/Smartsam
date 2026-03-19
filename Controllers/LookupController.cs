using Microsoft.AspNetCore.Mvc;
using SmartSam.Helpers; // Đảm bảo namespace này khớp với file Helper.cs của bạn

namespace SmartSam.Controllers
{
    [Route("api/[controller]")]
    //[ApiController]
    
    public class LookupController : ControllerBase
    {
        private readonly IConfiguration _config;

        public LookupController(IConfiguration config)
        {
            _config = config;
        }

        /// <summary>
        /// Endpoint: GET /api/Lookup/{type}?term={keyword}
        /// </summary>
        [HttpGet("{type}")]
        public IActionResult GetData(string type, [FromQuery] string? term)
        {
            // 1. Khai báo các thông tin DB tương ứng với từ khóa (Key)
            // Việc này giúp bảo mật: Client không biết tên bảng và tên cột thật
            string table = "";
            string idField = "";
            string textField = "";

            switch (type.ToLower())
            {
                case "company":
                    table = "CM_Company";
                    idField = "CompanyID";
                    textField = "CompanyName";
                    break;

                case "apartment":
                    table = "AM_Apartment";
                    idField = "ApartmentId";
                    textField = "ApartmentNo";
                    break;

                case "ContractStatus":
                    table = "CM_ContractStatus";
                    idField = "StatusID";
                    textField = "StatusName";
                    break;

                // Bạn có thể thêm các case khác tại đây (như staff, customer,...)
                default:
                    return BadRequest(new { message = "Loại dữ liệu tìm kiếm không hợp lệ." });
            }

            try
            {
                // 2. Gọi hàm static LoadLookup từ Helper của bạn
                // Kết quả trả về là List<(object Id, string Text)>
                var rawData = Helper.LoadLookup(_config, table, idField, textField, term);

                // 3. Chuyển đổi sang format Select2 { id, text } bằng Helper
                var result = Helper.ToSelect2Result(rawData);

                return Ok(result);
            }
            catch (Exception ex)
            {
                // Ghi log lỗi nếu cần
                return StatusCode(500, new { message = "Lỗi hệ thống khi truy vấn dữ liệu.", detail = ex.Message });
            }
        }
    }
}