using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using POETWeb.Data;
using POETWeb.Helpers;
using POETWeb.Models;
using POETWeb.Models.Domain;

namespace POETWeb.Controllers
{

    [Authorize]
    public class ClassroomsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public ClassroomsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db; _userManager = userManager;
        }

        // ===== TEACHER ZONE =====

        // Danh sách lớp của chính giáo viên
        [Authorize(Roles = "Teacher")]
        public async Task<IActionResult> Index(int page = 1, int pageSize = 10)
        {
            var me = await _userManager.GetUserAsync(User);
            var query = _db.Classrooms
                .AsNoTracking()
                .Where(c => c.TeacherId == me!.Id)
                .OrderByDescending(c => c.CreatedAt);

            var total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            ViewBag.Total = total; ViewBag.Page = page; ViewBag.PageSize = pageSize;
            return View(items);
        }

        // Xem chi tiết lớp
        // Cho cả Teacher (chủ lớp) và Student (đã join) vào xem
        [Authorize(Roles = "Teacher,Student")]
        public async Task<IActionResult> Details(int id)
        {

            var cls = await _db.Classrooms
                .AsNoTracking()
                .Include(c => c.Teacher)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (cls == null) return NotFound();

            if (User.IsInRole("Teacher"))
            {
                // giáo viên chỉ xem lớp của mình
                await EnsureOwnerAsync(cls.TeacherId);
                return View(cls);
            }

            // student phải là người đã join mới xem được
            var me = await _userManager.GetUserAsync(User);
            bool joined = await _db.Enrollments.AnyAsync(e => e.ClassId == id && e.UserId == me!.Id);
            if (!joined) return Forbid();

            return View(cls);
        }

        [Authorize(Roles = "Teacher")]
        public IActionResult Create() => View(new Classroom());

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "Teacher")]
        public async Task<IActionResult> Create(Classroom model)
        {
            var me = await _userManager.GetUserAsync(User);


            ModelState.Remove(nameof(Classroom.ClassCode));
            ModelState.Remove(nameof(Classroom.TeacherId));
            if (!ModelState.IsValid) return View(model);

            model.TeacherId = me!.Id;

            for (int i = 0; i < 8; i++)
            {
                model.ClassCode = CodeGenerator.GenerateClassCode6();
                if (!await _db.Classrooms.AnyAsync(c => c.ClassCode == model.ClassCode)) break;
                if (i == 7)
                {
                    ModelState.AddModelError(string.Empty, "Could not generate unique class code. Try again.");
                    return View(model);
                }
            }

            _db.Classrooms.Add(model);
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Details), new { id = model.Id });
        }

        [Authorize(Roles = "Teacher")]
        public async Task<IActionResult> Edit(int id)
        {
            var cls = await _db.Classrooms.FindAsync(id);
            if (cls == null) return NotFound();
            await EnsureOwnerAsync(cls.TeacherId);
            return View(cls);
        }

        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "Teacher")]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Subject")] Classroom input)
        {
            if (!ModelState.IsValid) return View(input);

            var cls = await _db.Classrooms.FirstOrDefaultAsync(c => c.Id == id);
            if (cls == null) return NotFound();
            await EnsureOwnerAsync(cls.TeacherId);

            cls.Name = input.Name;
            cls.Subject = input.Subject;
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Details), new { id = cls.Id });
        }

        [Authorize(Roles = "Teacher")]
        public async Task<IActionResult> Delete(int id)
        {
            var cls = await _db.Classrooms.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (cls == null) return NotFound();
            await EnsureOwnerAsync(cls.TeacherId);
            return View(cls);
        }

        [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
        [Authorize(Roles = "Teacher")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var cls = await _db.Classrooms.FirstOrDefaultAsync(c => c.Id == id);
            if (cls == null) return NotFound();
            await EnsureOwnerAsync(cls.TeacherId);

            _db.Classrooms.Remove(cls);
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }

        // Danh sách học viên cho giáo viên
        [Authorize(Roles = "Teacher")]
        public async Task<IActionResult> Roster(int id)
        {
            var cls = await _db.Classrooms.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (cls == null) return NotFound();
            await EnsureOwnerAsync(cls.TeacherId);

            var q = from e in _db.Enrollments
                    join u in _db.Users on e.UserId equals u.Id
                    where e.ClassId == id
                    orderby u.FullName
                    select new
                    {
                        u.Id,
                        u.UserName,
                        u.FullName,
                        u.Email,
                        u.PhoneNumber,
                        u.AccountCode,
                        u.AvatarUrl,            
                        e.JoinedAt,
                        e.RoleInClass
                    };


            var items = await q.AsNoTracking().ToListAsync();
            ViewBag.ClassName = cls.Name;
            ViewBag.ClassCode = cls.ClassCode;
            return View(items);
        }

        // ===== STUDENT ZONE =====

        // Form nhập mã lớp
        [Authorize(Roles = "Student")]
        public IActionResult Join() => View();

        // Xử lý join
        [HttpPost, ValidateAntiForgeryToken]
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> Join(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                ModelState.AddModelError(string.Empty, "Please enter class code.");
                return View();
            }

            var me = await _userManager.GetUserAsync(User);
            var normalized = code.Trim().ToUpper();

            var cls = await _db.Classrooms.AsNoTracking()
                .FirstOrDefaultAsync(c => c.ClassCode == normalized);

            if (cls == null)
            {
                ModelState.AddModelError(string.Empty, "Class not found.");
                return View();
            }

            bool exists = await _db.Enrollments
                .AnyAsync(e => e.ClassId == cls.Id && e.UserId == me!.Id);

            if (!exists)
            {
                _db.Enrollments.Add(new Enrollment
                {
                    ClassId = cls.Id,
                    UserId = me!.Id,
                    RoleInClass = "Student",
                    JoinedAt = DateTime.UtcNow
                });
                await _db.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Details), new { id = cls.Id });
        }

        [Authorize(Roles = "Student")]
        public IActionResult JoinSuccess(int classId)
        {
            var cls = _db.Classrooms.AsNoTracking().FirstOrDefault(c => c.Id == classId);
            if (cls == null) return NotFound();

            ViewBag.ClassId = classId;
            ViewBag.ClassName = cls.Name;
            return View();
        }

        //Danh sách học viên cho STUDENT xem
        [Authorize(Roles = "Student")]
        public async Task<IActionResult> RosterStudent(int id)
        {
            var me = await _userManager.GetUserAsync(User);

            bool joined = await _db.Enrollments.AnyAsync(e => e.ClassId == id && e.UserId == me!.Id);
            if (!joined) return Forbid();

            var cls = await _db.Classrooms.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (cls == null) return NotFound();

            var q = from e in _db.Enrollments
                    join u in _db.Users on e.UserId equals u.Id
                    where e.ClassId == id
                    orderby u.FullName
                    select new
                    {
                        u.FullName,
                        u.Email,
                        u.PhoneNumber,
                        u.AccountCode,
                        u.AvatarUrl,
                        e.JoinedAt
                    };

            var list = await q.AsNoTracking().ToListAsync();
            ViewBag.ClassId = cls.Id;
            ViewBag.ClassName = cls.Name;
            ViewBag.ClassCode = cls.ClassCode;
            return View(list);
        }



        // helper
        private async Task EnsureOwnerAsync(string teacherId)
        {
            var me = await _userManager.GetUserAsync(User);
            if (me!.Id != teacherId) throw new UnauthorizedAccessException("Not your class.");
        }
    }
}
