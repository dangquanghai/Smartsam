using Microsoft.AspNetCore.Mvc;
using SmartSam.Services;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace SmartSam.ViewComponents
{
    // Tên class kết thúc bằng "ViewComponent" là bắt buộc
    public class MenuViewComponent : ViewComponent
    {
        private readonly MenuService _menuService;

        public MenuViewComponent(MenuService menuService)
        {
            _menuService = menuService;
        }

        // Phương thức này sẽ được gọi khi bạn dùng @await Component.InvokeAsync("Menu")
        public async Task<IViewComponentResult> InvokeAsync()
        {
            // Lấy EmployeeCode từ User đang đăng nhập (hoặc tạm thời fix cứng để test)
            string employeeCode = User.Identity.Name ?? "AD035";

            // Gọi Service lấy dữ liệu từ SQL
            var model = _menuService.GetMenuForUser(employeeCode);

            // Trả về file Default.cshtml trong thư mục Pages/Shared/Components/Menu/
            return View(model);
        }
    }
}