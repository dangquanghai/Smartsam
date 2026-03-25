using System.Collections.Generic;
using System.Linq;
using SmartSam.Services; // QUAN TRỌNG: Namespace này chứa PermissionService
using SmartSam.Services.Interfaces; // Nếu anh để ISecurityService ở thư mục Interfaces

namespace SmartSam.Services.Implementations
{
    public class SecurityService : ISecurityService
    {
        private readonly PermissionService _rawPermService;

        // Constructor nhận vào PermissionService (đã đăng ký trong Program.cs)
        public SecurityService(PermissionService rawPermService)
        {
            _rawPermService = rawPermService;
        }
        // danh sách các page index mà quyền không phụ thuộc vào trạng thái dữ liệu
        private static readonly HashSet<int> StaticFunctionIds = new HashSet<int>
        {
            71,// Supplier list
            74,// Supplier-PO Report	
            75 // Analyzing Suppliers
            
        };

        public List<int> GetEffectivePermissions(int functionId, int roleId, int objectStatus)
        {
            // 1. Lấy quyền gốc (Admin hay User thường đã được xử lý xong ở đây)
            List<int> raw = _rawPermService.GetPermissionsForPage(roleId, functionId);

            if (raw == null || !raw.Any()) return new List<int>();

            // 2. Nếu là 1 page index mà các quyền không phụ thuộc vào trạng thái dữ liệu thì trả về chính raw đã có ở bước trên
            if (StaticFunctionIds.Contains(functionId))
            {
                return raw;
            }

            // 3. Nếu là 1 page index mà quyền được cấp phải giao với trạng thái dữ liệu thì vô đây
            switch (functionId)
            {
                case 5: // shorttem contract
                    return FilterSTContract(raw, objectStatus);

                case 145: // Approve Supplier
                    return FilterApproveSupplier(raw, objectStatus);

                case 104: // Material Request
                    return FilterMaterialRequest(raw, objectStatus);

                case 72: // Purchase Requisition
                    return FilterPurchaseRequisition(raw, objectStatus);

                case 73: // Purchase Order
                    return FilterPurchaseOrder(raw, objectStatus);

                default:
                    return raw;
            }
        }

        private List<int> FilterSTContract(List<int> raw, int status)
        {
            /* Mã quyền (Action):
                2: View, 3: Add, 4: Edit, 5: Edit Member, 6: Cancel, 
                7: ChangeStatus, 8: Adjust Out Date, 9: Copy
                10: Create Deposit, 11: Check In, 12: Check Out

                Trạng thái ST Contract: 
                1: Reser, 2: Living, 3: Done, 4: Cancelled, 9: Exception
            */

            switch (status)
            {
                case 1: // TRẠNG THÁI: RESER
                        // ĐƯỢC: Tất cả quyền cũ + Create Deposit(10), Check In(11)
                        // KHÔNG ĐƯỢC: Check Out(12)
                    return raw.Where(p => p != 12).ToList();

                case 2: // TRẠNG THÁI: LIVING
                        // ĐƯỢC: View(2), Edit Member(5), ChangeStatus(7), Adjust Out Date(8), Check Out(12)
                        // KHÔNG ĐƯỢC: Edit(4), Cancel(6), Create Deposit(10), Check In(11)
                    return raw.Where(p => p != 4 && p != 6 && p != 10 && p != 11).ToList();

                case 3: // TRẠNG THÁI: DONE
                        // CHỈ ĐƯỢC: View(2), Add(3), Copy(9)
                        // Khóa toàn bộ các nghiệp vụ thực thi (10, 11, 12)
                    return raw.Where(p => p == 2 || p == 3 || p == 9).ToList();

                case 4: // TRẠNG THÁI: CANCELLED
                        // ĐƯỢC: View(2), Add(3), Copy(9), ChangeStatus(7)
                    return raw.Where(p => p == 2 || p == 3 || p == 7 || p == 9).ToList();

                case 9: // TRẠNG THÁI: EXCEPTION
                        // Giống Reser: Cho phép xử lý Deposit/Check-in để đưa về luồng chuẩn
                    return raw.Where(p => p != 12).ToList();

                default:
                    // An toàn: Chỉ cho Xem/Thêm
                    return raw.Where(p => p == 2 || p == 3).ToList();
            }
        }

        private List<int> FilterPurchaseOrder(List<int> raw, int status)
        {
            /* Trạng thái PO thực tế: 
               1: Processing(Purchaser đánh giá thì nó chuyển sang 2)
                Nó gửi cập nhật thông tin Purchaser đánh giá vào bảng PO_Estimate và thông tin Purchaser approve vào bảng PC_PO rồi gửi mail cho CFO
               2: Waiting for approval
                CFO approve nó cập nhật thông tin approve vào PC_PO rồi gửi cho BOD
                BOD approve nó cập nhật thông tin approve vào PC_PO rồi gửi cho Purchaser 

               Mã hành động:
               2: View, 3: Add, 4: Edit, 6: Back to Processing, 7: Purchaser Evaluate
            */

            // Các quyền luôn có: Xem và Thêm mới
            var effective = raw.Where(p => p == 2 || p == 3).ToList();

            switch (status)
            {
                case 1: // PROCESSING
                        // Được Sửa (4)
                    if (raw.Contains(4)) effective.Add(4);

                    // Được Đánh giá (7) -> Hành động này sẽ kiêm luôn việc đổi Status sang 2
                    if (raw.Contains(7)) effective.Add(7);
                    break;

                case 2: // WAITING FOR APPROVAL
                        // Khóa Sửa (4), Khóa Đánh giá (7)
                        // Chỉ cho phép Trả về (6) để quay lại bước 1 nếu cần sửa đổi
                    if (raw.Contains(6)) effective.Add(6);
                    break;

                default:
                    break;
            }

            return effective;
        }

        private List<int> FilterPurchaseRequisition(List<int> raw, int status)
        {
            /* Mã hành động (Action):
               2: View Detail, 3: Add/Add Auto, 4: Edit, 5: Approve, 6: Change Status (New <-> Pending)
               Trạng thái (Status):
               1: New, 2: Waiting For Approve, 3: Pending, 4: Done
            */

            // Bước 1: Quyền tĩnh luôn có (Xem và Thêm)
            var effective = raw.Where(p => p == 2 || p == 3).ToList();

            switch (status)
            {
                case 1: // TRẠNG THÁI: NEW
                        // ĐƯỢC: Edit (4), Approve (5 - Purchaser duyệt gửi CFO), Change Status (6 - sang Pending)
                    if (raw.Contains(4)) effective.Add(4);
                    if (raw.Contains(5)) effective.Add(5);
                    if (raw.Contains(6)) effective.Add(6);
                    break;

                case 2: // TRẠNG THÁI: WAITING FOR APPROVE (Chờ CFO/BOD)
                        // ĐƯỢC: Approve (5) 
                        // KHÔNG ĐƯỢC: Edit (4), Change Status (6)
                    if (raw.Contains(5)) effective.Add(5);
                    break;

                case 3: // TRẠNG THÁI: PENDING
                        // CHỈ ĐƯỢC: Change Status (6 - để quay về New)
                        // KHÔNG ĐƯỢC: Edit (4), Approve (5)
                    if (raw.Contains(6)) effective.Add(6);
                    break;

                case 4: // TRẠNG THÁI: DONE
                        // Chỉ View/Add (Đã xử lý ở Bước 1)
                    break;

                default:
                    break;
            }

            return effective;
        }
        private List<int> FilterMaterialRequest(List<int> raw, int status)
        {
            /* Mã quyền (Action) cho Supplier theo yêu cầu của anh:
            (mặc nhiên có view , view list)
            1: Create
            2: Create Auto
            3: Edit  (sửa Description, thêm item, thêm new item, check/uncheck Not Issue)
            4: Reject Item
            5: Submit/ Approve
            Purchaer, CFO,BOD,ADMIN được xem MR của tất cả các bộ phận ==> Không lock select Bộ phận với các user này

            có các trạng thái 
            -1  Just Createed
            0	Submited To Head
            1	Head Approved
            2	Purchaser checked
            3	CFO Approved
            4	Collected to PR
            5	Rejected
            6	ISSUED
             
             Chỉ có trạng thái -1 là được edit, Reject Item 
             */

            var validActions = new List<int> { 1, 2 };
            return raw.Where(p => validActions.Contains(p)).ToList();
        }

        private List<int> FilterApproveSupplier(List<int> raw, int status)
        {
            /* Mã quyền (Action) cho Supplier theo yêu cầu của anh:
            1: View
            2: Approve
            Mỗi user có trường LevelCheckSupplier, nếu khác null thì họ sẽ nhìn thấy các supplier có trạng thái = LevelCheckSupplier-1

             */
            var validActions = new List<int> { 1, 2 };
            return raw.Where(p => validActions.Contains(p)).ToList();
        }

    }
}