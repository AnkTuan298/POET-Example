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

            // 1) Classes
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

            // 2) Distinct students
            var totalStudents = await _db.Enrollments
                .Where(e => e.Classroom!.TeacherId == teacherId && e.RoleInClass == "Student")
                .Select(e => e.UserId)
                .Distinct()
                .CountAsync();

            // 3) Assignments count
            var assignmentsCount = await _db.Assignments
                .AsNoTracking()
                .CountAsync(a => a.Class.TeacherId == teacherId);

            // 4) Pending manual grading
            var pendingGrades = await _db.AssignmentAttempts
                .AsNoTracking()
                .Where(t => t.Assignment.Class.TeacherId == teacherId
                         && t.RequiresManualGrading
                         && t.SubmittedAt != null
                         && t.FinalScore == null)
                .CountAsync();

            // ===== RECENT ACTIVITY: Submissions + Joins, take newest 5 =====

            // Recent submissions
            var subRaw = await _db.AssignmentAttempts
                .AsNoTracking()
                .Where(t => t.Assignment.Class.TeacherId == teacherId && t.SubmittedAt != null)
                .OrderByDescending(t => t.SubmittedAt)
                .Select(t => new
                {
                    t.UserId,
                    t.SubmittedAt,
                    ClassTitle = t.Assignment.Class.Name,
                    AssignmentTitle = t.Assignment.Title
                })
                .Take(10)
                .ToListAsync();

            // Recent joins
            var joinRaw = await _db.Enrollments
                .AsNoTracking()
                .Where(e => e.Classroom!.TeacherId == teacherId
                         && e.RoleInClass == "Student"
                         && e.JoinedAt != null)
                .OrderByDescending(e => e.JoinedAt)
                .Select(e => new
                {
                    e.UserId,
                    e.JoinedAt,
                    ClassTitle = e.Classroom!.Name
                })
                .Take(10)
                .ToListAsync();

            // Fetch user display names
            var userIds = subRaw.Select(x => x.UserId).Concat(joinRaw.Select(x => x.UserId)).Distinct().ToList();
            var users = await _db.Users
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName, u.UserName })
                .ToListAsync();

            string DisplayName(string? full, string? username)
                => string.IsNullOrWhiteSpace(full) ? (username ?? "Student") : full!;

            // Helper: convert various DateTime / DateTimeOffset into DateTimeOffset safely
            DateTimeOffset SafeToDto(object? raw)
            {
                if (raw is DateTimeOffset dto) return dto;
                if (raw is DateTime dt)
                {
                    return dt.Kind switch
                    {
                        DateTimeKind.Utc => new DateTimeOffset(dt, TimeSpan.Zero),
                        DateTimeKind.Local => new DateTimeOffset(dt.ToUniversalTime(), TimeSpan.Zero),
                        _ => new DateTimeOffset(DateTime.SpecifyKind(dt, DateTimeKind.Utc), TimeSpan.Zero)
                    };
                }
                return DateTimeOffset.UtcNow;
            }

            var recent = new List<RecentActivityVM>();

            // Map submissions
            recent.AddRange(subRaw.Select(s =>
            {
                var u = users.FirstOrDefault(x => x.Id == s.UserId);
                var whenDto = SafeToDto(s.SubmittedAt);
                return new RecentActivityVM
                {
                    Kind = ActivityKind.SubmittedAssignment,
                    StudentName = DisplayName(u?.FullName, u?.UserName),
                    ClassTitle = s.ClassTitle,
                    AssignmentTitle = s.AssignmentTitle,
                    When = whenDto,
                    TimeAgo = ToTimeAgo(whenDto.UtcDateTime)
                };
            }));

            // Map joins
            recent.AddRange(joinRaw.Select(j =>
            {
                var u = users.FirstOrDefault(x => x.Id == j.UserId);
                var whenDto = SafeToDto(j.JoinedAt);
                return new RecentActivityVM
                {
                    Kind = ActivityKind.JoinedClass,
                    StudentName = DisplayName(u?.FullName, u?.UserName),
                    ClassTitle = j.ClassTitle,
                    AssignmentTitle = null,
                    When = whenDto,
                    TimeAgo = ToTimeAgo(whenDto.UtcDateTime)
                };
            }));

            // Take newest 5
            var recentTop5 = recent
                .OrderByDescending(x => x.When)
                .Take(5)
                .ToList();

            // Build VM
            var vm = new TeacherDashboardVM
            {
                FirstName = ExtractFirstName(user.FullName),
                ActiveClasses = classes.Count,
                TotalStudents = totalStudents,
                Assignments = assignmentsCount,
                PendingGrades = pendingGrades,
                Classes = classes,
                Recent = recentTop5
            };

            return View(vm);
        }

        // Helper
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
