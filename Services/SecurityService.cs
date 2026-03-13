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

        public List<int> GetEffectivePermissions(int functionId, int roleId, int objectStatus)
        {
            // Vì hàm cũ trả về List<int>, anh gọi trực tiếp như sau:
            List<int> raw = _rawPermService.GetPermissionsForPage(roleId, functionId);

            if (raw == null || !raw.Any()) return new List<int>();

            // --- BIỆN LUẬN BẢO MẬT GIAI ĐOẠN 3 ---
            switch (functionId)
            {
                case 5: // shorttem contract
                    return FilterSTContractLogic(raw, objectStatus);

                // Thêm các case khác...

                default:
                    return raw;
            }
        }

        private List<int> FilterSTContractLogic(List<int> raw, int status)
        {
            /* Mã quyền (Action):
               2: View, 3: Add, 4: Edit, 5: Edit Member, 6: Cancel, 
               7: ChangeStatus, 8: Adjust Out Date, 9: Copy

               Trạng thái ST Contract: 
               1: Reser, 2: Living, 3: Done, 4: Cancelled, 9: Exception
            */

            // Các quyền 3 (Add) và 9 (Copy) thường được giữ lại để tạo mới từ bản ghi cũ
            // (Tùy anh quyết định có cho phép Copy từ hợp đồng đã Cancel hay không)

            switch (status)
            {
                case 1: // TRẠNG THÁI: RESER
                        // Đầy đủ quyền: View, Edit, Edit Member, Cancel, ChangeStatus(sang Living), Adjust Out Date
                    return raw;

                case 2: // TRẠNG THÁI: LIVING
                        // ĐƯỢC: View(2), Edit Member(5), Cancel(6), ChangeStatus(7 - về Reser), Adjust Out Date(8)
                        // KHÔNG ĐƯỢC: Edit(4)
                    return raw.Where(p => p != 4).ToList();

                case 3: // TRẠNG THÁI: DONE
                        // CHỈ ĐƯỢC: View(2) (và Add/Copy nếu cần)
                    return raw.Where(p => p == 2 || p == 3 || p == 9).ToList();

                case 4: // TRẠNG THÁI: CANCELLED
                        // Giống Done nhưng CÓ THÊM ChangeStatus(7 - để phục hồi sang Reser)
                    return raw.Where(p => p == 2 || p == 3 || p == 7 || p == 9).ToList();

                case 9: // TRẠNG THÁI: EXCEPTION
                        // CÓ TẤT CẢ quyền như Reser (bao gồm cả ChangeStatus để quay về Reser)
                    return raw;

                default:
                    // Trạng thái mới hoặc không xác định: Giữ quyền View/Add cho an toàn
                    return raw.Where(p => p == 2 || p == 3).ToList();
            }
        }
    }
}