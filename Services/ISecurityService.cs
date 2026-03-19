namespace SmartSam.Services.Interfaces
{
    public interface ISecurityService
    {
        // Hàm tổng quát trả về danh sách mã quyền được phép (2, 3, 4, 6, 7...)
        List<int> GetEffectivePermissions(int functionId, int roleId, int objectStatus);
    }
}