using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc.Filters;
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
            18,  // Admin/Employee
            71,  // Supplier list
            74,  // Supplier-PO Report	
            75,  // Analyzing Suppliers
            60,  // Inventory Parameters
            64,  // Item list
            //66,  // Item Issue
            //67,  // Item Receice 
            //68,  // Item Transfer
            70,  // Inventory Report
            90 , // Inventory Make  New year=> tạo số liệu tồn đầu năm cho các kho
            114, // Laundty & Linen  list
            115, // Linen  Note Daily
            116, // Laundry Linen Delivery 
            117, // Laundry Linen Receiving
            118, // Laundry Linen Report
            148, // Special Laudry Report
            149, // Post Invoice to Vinhy
           
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

                case 151:  // Item Checking
                    return FilterItemChecking(raw, objectStatus);

                case 66: // Item Issue
                    return FilterItemIssue(raw, objectStatus);
                case 67:  // Item Receice 
                    return FilterItemReceice(raw, objectStatus);
                case 68:  // Item Transfer 
                    return FilterItemTransfer(raw, objectStatus);

                default:
                    return raw;
            }
        }
        private List<int> FilterItemChecking(List<int> raw, int status)
        {
            /* 
               Mã quyền (Action):
               1: View List, 2: View Detail, 3: Add, 4: Edit

               Trạng thái ItemChecking: 
               1: Just Created, 2: Checked, 3: Approved, 4: Received, 5: Cancel
            */

            // Bước 1: Khởi tạo danh sách quyền mặc định (Lúc nào cũng được xem List và thêm mới)
            // Sử dụng Distinct để tránh trùng lặp nếu trong raw đã có sẵn nhiều quyền giống nhau
            var effective = raw.Where(p => p == 1 || p == 3).Distinct().ToList();

            // Bước 2: Xét quyền xem chi tiết (View Detail - 2) 
            // Thường thì trạng thái nào cũng được xem chi tiết nếu có quyền
            if (raw.Contains(2))
            {
                effective.Add(2);
            }

            // Bước 3: Xét quyền đặc thù theo trạng thái
            if (status == 1) // Chỉ trạng thái Just Created mới được Edit
            {
                if (raw.Contains(4))
                {
                    effective.Add(4);
                }
            }
            // Các trạng thái 2, 3, 4, 5 sẽ không rơi vào if này -> Không có quyền 4 (Edit)

            return effective.Distinct().ToList();
        }
        private List<int> FilterItemTransfer(List<int> raw, int status)
        {
            /* Mã quyền (Action):
                1: View, 2: View Detail, 3 Add, 4: Edit, 5: Delete 

                Trạng thái ItemIssue: 
                1: Just Created, 2: Storeman Confirmed, 3: Head Confirmed 
            */
            // Các quyền luôn có: Xem và Thêm mới
            var effective = raw.Where(p => p == 2 || p == 3).ToList();

            if (status == 1)
            {
                if (raw.Contains(2)) effective.Add(2);
                if (raw.Contains(3)) effective.Add(3);
                if (raw.Contains(4)) effective.Add(4);
                if (raw.Contains(5)) effective.Add(5);
            }
            else// 2,3
            {
                if (raw.Contains(2)) effective.Add(2);
                if (raw.Contains(3)) effective.Add(3);

            }

            return effective;
        }

        private List<int> FilterItemReceice(List<int> raw, int status)
        {
            /* Mã quyền (Action):
                1: View, 2: View Detail, 3 Add, 4: Edit, 5: Delete 

                Trạng thái ItemIssue: 
                1: Create Voucher, 2: Storeman Confirmed, 3: Head Checked & Confirmed, 4: PU Confirmed
            */
            // Các quyền luôn có: Xem và Thêm mới
            var effective = raw.Where(p => p == 2 || p == 3).ToList();

            if (status == 1)
            {
                if (raw.Contains(2)) effective.Add(2);
                if (raw.Contains(3)) effective.Add(3);
                if (raw.Contains(4)) effective.Add(4);
                if (raw.Contains(5)) effective.Add(5);
            }
            else// 2,3,4
            {
                if (raw.Contains(2)) effective.Add(2);
                if (raw.Contains(3)) effective.Add(3);

            }

            return effective;
        }
        private List<int> FilterItemIssue(List<int> raw, int status)
        {
            /* Mã quyền (Action):
                1: View, 2: View Detail, 3 Add, 4: Edit, 5: Delete 

                Trạng thái ItemIssue: 
                1: Was Created, 2: Storeman Confirmed, 3: Receiver Confirmed
            */

            // Các quyền luôn có: Xem và Thêm mới
            var effective = raw.Where(p => p == 2 || p == 3).ToList();

            if (status == 1)
            {
                if (raw.Contains(2)) effective.Add(2);
                if (raw.Contains(3)) effective.Add(3);
                if (raw.Contains(4)) effective.Add(4);
                if (raw.Contains(5)) effective.Add(5);
            } 
            else// 2,3
            {
                if (raw.Contains(2)) effective.Add(2);
                if (raw.Contains(3)) effective.Add(3);

            }    
           
            return effective;
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
            // Các quyền luôn có: Xem chi tiết và Thêm mới
            var effective = raw.Where(p => p == 2 || p == 3 || p == 8).ToList();

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
               3: BOD approved

               Mã hành động:
               2: View, 3: Add, 4: Edit, 6: Back to Processing, 7: Purchaser Evaluate, 8 : Attache File
            */

            // Các quyền luôn có: Xem và Thêm mới, Attache File

            var effective = raw.Where(p => p == 2 || p == 3 || p == 8).ToList();

            if (status >= 1 && status <= 3 && raw.Contains(6))
            {
                effective.Add(6);
            }
            

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
                        // Trả về (6) đã được mở cho StatusID <= 3 ở trên
                    break;

                default:
                    break;
            }

            return effective;
        }

        private List<int> FilterPurchaseRequisition(List<int> raw, int status)
        {
            /* Mã hành động (Action):
               2: View Detail, 3: Add/Add Auto, 4: Edit, 5: Approve, 6: Change Status (New <-> Pending), 7 :Attache File
               Trạng thái (Status):
               1: New, 2: Waiting For Approve, 3: Pending, 4: Done
            */

            // Bước 1: Quyền tĩnh luôn có (Xem và Thêm)
            var effective = raw.Where(p => p == 2 || p == 3 || p == 7).ToList();

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
