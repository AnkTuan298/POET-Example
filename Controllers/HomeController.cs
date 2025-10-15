using Microsoft.AspNetCore.Mvc;

namespace POETWeb.Controllers
{
    public class HomeController : Controller
    {
        public IActionResult Index()
        {
            // Nếu đã đăng nhập thì điều hướng theo role
            if (User?.Identity?.IsAuthenticated == true)
            {
                if (User.IsInRole("Admin")) return RedirectToAction("Index", "Admin");
                if (User.IsInRole("Teacher")) return RedirectToAction("Index", "Teacher");
                return RedirectToAction("Index", "Student"); // mặc định student
            }

            // Khách (chưa login) thì trả về trang landing bình thường
            return View();
        }

        public IActionResult Privacy() => View();
    }
}
