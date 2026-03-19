namespace SmartSam.Models
{
    public class SixMonthsStayReviewVM
    {
        public long ContractID { get; set; }
        public string ContractNo { get; set; }
        public string CurrentApartmentNo { get; set; }
        public string Title { get; set; } // Mr., Ms.,...
        public string CustomerName { get; set; }
        public string CompanyName { get; set; }
        public DateTime ContractFromDate { get; set; }
        public DateTime ContractToDate { get; set; }

        // Thêm 2 trường mới
        public bool SentReview { get; set; }
        public bool ReceivedReview { get; set; }
    }
}
