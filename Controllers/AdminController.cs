using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using POETWeb.Data;
using POETWeb.Models;

namespace POETWeb.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;

        public AdminController(
            ApplicationDbContext db,
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole> roleManager)
        {
            _db = db;
            _userManager = userManager;
            _roleManager = roleManager;
        }

        // ===== Dashboard hub =====
        public IActionResult Index() => View();

        // ===== USERS =====
        public sealed class UserRow
        {
            public string Id { get; set; } = "";
            public string? UserName { get; set; }
            public string? FullName { get; set; }
            public string? Email { get; set; }
            public string? PhoneNumber { get; set; }
            public string? AvatarUrl { get; set; }
            public string RolesCsv { get; set; } = "";
            public string AccountCode { get; set; } = "";

            public bool IsTeacher { get; set; }
            public bool IsStudent { get; set; }
            public bool IsAdmin { get; set; }
        }

        [HttpGet]
        public async Task<IActionResult> Users(string? q = null, int page = 1, int pageSize = 20)
        {
            q = (q ?? "").Trim().ToLowerInvariant();

            var baseQuery = _db.Users.AsNoTracking();
            if (!string.IsNullOrEmpty(q))
            {
                baseQuery = baseQuery.Where(u =>
                    (u.UserName ?? "").ToLower().Contains(q) ||
                    (u.FullName ?? "").ToLower().Contains(q) ||
                    (u.Email ?? "").ToLower().Contains(q));
            }

            var total = await baseQuery.CountAsync();
            var users = await baseQuery
                .OrderBy(u => u.UserName)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var rows = new List<UserRow>(users.Count);
            foreach (var u in users)
            {
                var roles = await _userManager.GetRolesAsync(u);
                rows.Add(new UserRow
                {
                    Id = u.Id,
                    UserName = u.UserName,
                    FullName = u.FullName,
                    Email = u.Email,
                    PhoneNumber = u.PhoneNumber,
                    AvatarUrl = u.AvatarUrl,
                    RolesCsv = string.Join(", ", roles),
                    AccountCode = u.AccountCode ?? "",
                    IsTeacher = roles.Contains("Teacher"),
                    IsStudent = roles.Contains("Student"),
                    IsAdmin = roles.Contains("Admin")
                });
            }

            ViewBag.Total = total; ViewBag.Page = page; ViewBag.PageSize = pageSize; ViewBag.Query = q;
            return View(rows);
        }
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> PromoteToTeacher([FromForm] string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            var current = await _userManager.GetRolesAsync(user);
            if (current.Contains("Admin")) return BadRequest("Cannot change Admin role.");

            // Gỡ tất cả role hiện có
                await _userManager.RemoveFromRolesAsync(user, current);

            // Đảm bảo role Teacher tồn tại
            if (!await _roleManager.RoleExistsAsync("Teacher"))
                await _roleManager.CreateAsync(new IdentityRole("Teacher"));

            // Chỉ thêm Teacher
            await _userManager.AddToRoleAsync(user, "Teacher");

            return RedirectToAction(nameof(Users));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DemoteToStudent([FromForm] string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            // Không đụng vào Admin
            var current = await _userManager.GetRolesAsync(user);
            if (current.Contains("Admin")) return BadRequest("Cannot change Admin role.");

            // Gỡ tất cả role hiện có
            if (current.Count > 0)
                await _userManager.RemoveFromRolesAsync(user, current);

            // Đảm bảo role Student tồn tại
            if (!await _roleManager.RoleExistsAsync("Student"))
                await _roleManager.CreateAsync(new IdentityRole("Student"));

            // Chỉ thêm Student
            await _userManager.AddToRoleAsync(user, "Student");

            return RedirectToAction(nameof(Users));
        }


        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUser([FromForm] string userId)
        {
            var meId = _userManager.GetUserId(User);
            if (userId == meId) return BadRequest("Cannot delete yourself.");

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            // Xoá Enrollment + Attempt của user trước để tránh ràng buộc
            var enrolls = _db.Enrollments.Where(e => e.UserId == userId);
            _db.Enrollments.RemoveRange(enrolls);

            var attempts = _db.AssignmentAttempts.Where(a => a.UserId == userId);
            _db.AssignmentAttempts.RemoveRange(attempts);

            await _db.SaveChangesAsync();
            await _userManager.DeleteAsync(user);
            return RedirectToAction(nameof(Users));
        }

        // ===== CLASSES =====
        public sealed class ClassRow
        {
            public int Id { get; set; }
            public string Name { get; set; } = "";
            public string ClassCode { get; set; } = "";
            public string? Subject { get; set; }
            public string TeacherName { get; set; } = "";
            public int Students { get; set; }
            public DateTime CreatedAt { get; set; }
        }

        [HttpGet]
        public async Task<IActionResult> Classes(string? q = null, int page = 1, int pageSize = 20)
        {
            q = (q ?? "").Trim().ToLowerInvariant();

            var query = _db.Classrooms
                .Include(c => c.Teacher)
                .Include(c => c.Enrollments)
                .AsNoTracking();

            if (!string.IsNullOrEmpty(q))
            {
                query = query.Where(c =>
                    c.Name.ToLower().Contains(q) ||
                    (c.Subject ?? "").ToLower().Contains(q) ||
                    c.ClassCode.ToLower().Contains(q) ||
                    (c.Teacher!.FullName ?? c.Teacher!.UserName ?? "").ToLower().Contains(q));
            }

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(c => c.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new ClassRow
                {
                    Id = c.Id,
                    Name = c.Name,
                    ClassCode = c.ClassCode,
                    Subject = c.Subject,
                    TeacherName = string.IsNullOrWhiteSpace(c.Teacher!.FullName) ? (c.Teacher!.UserName ?? "Teacher") : c.Teacher!.FullName!,
                    Students = c.Enrollments!.Count(e => e.RoleInClass == "Student"),
                    CreatedAt = c.CreatedAt
                })
                .ToListAsync();

            ViewBag.Total = total; ViewBag.Page = page; ViewBag.PageSize = pageSize; ViewBag.Query = q;
            return View(items);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteClass([FromForm] int id)
        {
            var cls = await _db.Classrooms.FirstOrDefaultAsync(c => c.Id == id);
            if (cls == null) return NotFound();

            // Quan hệ đã cấu hình cascade trong DbContext: xoá lớp sẽ cuốn theo Assignment, Questions, Attempts…
            _db.Classrooms.Remove(cls);
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Classes));
        }

        // ===== CHILD: MATERIALS =====
        [HttpGet]
        public async Task<IActionResult> Materials(int classId)
        {
            var cls = await _db.Classrooms.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classId);
            if (cls == null) return NotFound();

            var items = await _db.Materials
                .AsNoTracking()
                .Where(m => m.ClassId == classId)
                .OrderByDescending(m => m.CreatedAt)
                .ToListAsync();

            ViewBag.ClassId = classId; ViewBag.ClassName = cls.Name;
            return View(items);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteMaterial([FromForm] int id, [FromForm] int classId)
        {
            var m = await _db.Materials.FirstOrDefaultAsync(x => x.Id == id);
            if (m == null) return NotFound();
            _db.Materials.Remove(m);
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Materials), new { classId });
        }

        // ===== CHILD: ASSIGNMENTS =====
        [HttpGet]
        public async Task<IActionResult> Assignments(int classId)
        {
            var cls = await _db.Classrooms.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classId);
            if (cls == null) return NotFound();

            var items = await _db.Assignments
                .AsNoTracking()
                .Where(a => a.ClassId == classId)
                .OrderByDescending(a => a.CreatedAt)
                .Select(a => new
                {
                    a.Id,
                    a.Title,
                    TotalMax = a.Questions.Sum(q => q.Points),
                    a.OpenAt,
                    a.CloseAt,
                    a.MaxAttempts
                })
                .ToListAsync();

            ViewBag.ClassId = classId; ViewBag.ClassName = cls.Name;
            return View(items);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteAssignment([FromForm] int id, [FromForm] int classId)
        {
            var a = await _db.Assignments.FirstOrDefaultAsync(x => x.Id == id);
            if (a == null) return NotFound();
            _db.Assignments.Remove(a); // cascade questions, choices, attempts
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Assignments), new { classId });
        }

        // ===== CHILD: STUDENTS =====
        [HttpGet]
        public async Task<IActionResult> Students(int classId)
        {
            var cls = await _db.Classrooms.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classId);
            if (cls == null) return NotFound();

            var items = await _db.Enrollments
                .AsNoTracking()
                .Where(e => e.ClassId == classId && e.RoleInClass == "Student")
                .Join(_db.Users, e => e.UserId, u => u.Id, (e, u) => new
                {
                    u.Id,
                    u.UserName,
                    u.FullName,
                    u.Email,
                    u.AvatarUrl,
                    e.JoinedAt
                })
                .OrderBy(u => u.FullName)
                .ToListAsync();

            ViewBag.ClassId = classId; ViewBag.ClassName = cls.Name;
            return View(items);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> KickStudent([FromForm] int classId, [FromForm] string userId)
        {
            var enr = await _db.Enrollments
                .FirstOrDefaultAsync(e => e.ClassId == classId && e.UserId == userId && e.RoleInClass == "Student");
            if (enr == null) return NotFound();

            _db.Enrollments.Remove(enr); // giống kick ở phần teacher
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Students), new { classId });
        }
    }
}
