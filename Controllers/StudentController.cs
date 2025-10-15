using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using POETWeb.Data;
using POETWeb.Models;

namespace POETWeb.Controllers
{
    [Authorize(Roles = "Student")]
    public class StudentController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public StudentController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        // Dashboard học sinh
        public IActionResult Index()
        {
            // Trang này bạn đã có view rồi (Views/Student/Index.cshtml)
            return View();
        }

        // Danh sách lớp đã tham gia
        public async Task<IActionResult> Classes()
        {
            var me = await _userManager.GetUserAsync(User);

            var q = from e in _db.Enrollments
                    join c in _db.Classrooms on e.ClassId equals c.Id
                    join t in _db.Users on c.TeacherId equals t.Id
                    where e.UserId == me!.Id
                    orderby c.Name
                    select new
                    {
                        c.Id,
                        c.Name,
                        c.Subject,
                        TeacherName = t.FullName,
                        TeacherAvatar = t.AvatarUrl
                    };


            var list = await q.AsNoTracking().ToListAsync();
            return View(list);
        }
    }
}
