using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using POETWeb.Data;
using POETWeb.Models;
using POETWeb.Models.ViewModels;
using System.Globalization;

namespace POETWeb.Controllers
{
    [Authorize(Roles = "Teacher")]
    public class TeacherController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly ApplicationDbContext _db;

        public TeacherController(UserManager<ApplicationUser> userManager, ApplicationDbContext db)
        {
            _userManager = userManager;
            _db = db;
        }

        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return Challenge();

            var teacherId = user.Id;

            // Lớp của giáo viên
            var classes = await _db.Classrooms
                .Where(c => c.TeacherId == teacherId)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new ClassCardVM
                {
                    Id = c.Id,
                    Title = c.Name,
                    Students = c.Enrollments!.Count(e => e.RoleInClass == "Student"),
                    Subject = c.Subject
                })
                .ToListAsync();

            // Tổng học sinh (distinct theo UserId trong tất cả lớp của teacher)
            var totalStudents = await _db.Enrollments
                .Where(e => e.Classroom!.TeacherId == teacherId && e.RoleInClass == "Student")
                .Select(e => e.UserId)
                .Distinct()
                .CountAsync();

            // Recent activity: 10 lượt join mới nhất
            var recent = await _db.Enrollments
                .Where(e => e.Classroom!.TeacherId == teacherId && e.RoleInClass == "Student")
                .OrderByDescending(e => e.JoinedAt)
                .Take(10)
                .Select(e => new { e.UserId, ClassTitle = e.Classroom!.Name, e.JoinedAt })
                .ToListAsync();

            // Lấy tên hiển thị của học sinh
            var userIds = recent.Select(r => r.UserId).Distinct().ToList();
            var users = await _db.Users
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName, u.UserName })
                .ToListAsync();

            var recentVm = recent.Select(r =>
            {
                var u = users.FirstOrDefault(x => x.Id == r.UserId);
                var name = string.IsNullOrWhiteSpace(u?.FullName) ? (u?.UserName ?? "Student") : u!.FullName;
                return new RecentActivityVM
                {
                    StudentName = name,
                    ClassTitle = r.ClassTitle,
                    TimeAgo = ToTimeAgo(r.JoinedAt)
                };
            }).ToList();

            var vm = new TeacherDashboardVM
            {
                FirstName = ExtractFirstName(user.FullName),
                ActiveClasses = classes.Count,
                TotalStudents = totalStudents,
                Assignments = 0,
                PendingGrades = 0,
                Classes = classes,
                Recent = recentVm
            };

            return View(vm);
        }

        // Helper: “2 hours ago”
        private static string ToTimeAgo(DateTime utcTime)
        {
            var span = DateTime.UtcNow - utcTime;
            if (span.TotalSeconds < 60) return "just now";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} minutes ago";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours} hours ago";
            return $"{(int)span.TotalDays} day{(span.TotalDays >= 2 ? "s" : "")} ago";
        }

        private static string ExtractFirstName(string? fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName)) return "Teacher";
            var parts = fullName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length > 0 ? parts[^1] : "Teacher";
        }
    }
}
