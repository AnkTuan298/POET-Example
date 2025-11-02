using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using POETWeb.Data;
using POETWeb.Models;
using POETWeb.Models.Enums;
using POETWeb.Models.ViewModels;

namespace POETWeb.Controllers
{
    [Authorize]
    public class AssignmentController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public AssignmentController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        // =================== STUDENT LIST ===================
        [Authorize(Roles = "Student")]
        [HttpGet]
        public async Task<IActionResult> Student(int? classId)
        {
            var me = await _userManager.GetUserAsync(User);

            var myClassIds = _db.Enrollments
                                .Where(e => e.UserId == me!.Id)
                                .Select(e => e.ClassId);

            var q = _db.Assignments
                       .AsNoTracking()
                       .Include(a => a.Class)
                       .Where(a => myClassIds.Contains(a.ClassId));

            if (classId.HasValue) q = q.Where(a => a.ClassId == classId.Value);

            var now = DateTimeOffset.UtcNow;

            var vm = new AssignmentStudentVM
            {
                ClassId = classId,
                ClassName = classId == null
                    ? null
                    : await _db.Classrooms.AsNoTracking()
                        .Where(c => c.Id == classId.Value)
                        .Select(c => c.Name)
                        .FirstOrDefaultAsync(),
                Items = await q
                    .OrderByDescending(a => a.CreatedAt)
                    .Select(a => new AssignmentListItemVM
                    {
                        Id = a.Id,
                        ClassId = a.ClassId,
                        ClassName = a.Class.Name,
                        Title = a.Title,
                        DueAt = a.CloseAt,
                        Status = a.OpenAt != null && now < a.OpenAt ? "Not Open"
                                : a.CloseAt != null && now > a.CloseAt ? "Closed" : "Open",
                        Type = a.Type,
                        Description = a.Description,
                        DurationMinutes = a.DurationMinutes,
                        MaxAttempts = a.MaxAttempts
                    })
                    .ToListAsync()
            };

            return View(vm);
        }

        // =================== TEACHER LIST ===================
        [Authorize(Roles = "Teacher")]
        [HttpGet]
        public async Task<IActionResult> Teacher(int? classId)
        {
            var me = await _userManager.GetUserAsync(User);

            var q = _db.Assignments
                       .AsNoTracking()
                       .Include(a => a.Class)
                       .Where(a => a.Class.TeacherId == me!.Id);

            if (classId.HasValue) q = q.Where(a => a.ClassId == classId.Value);

            var vm = new AssignmentTeacherVM
            {
                ClassId = classId,
                ClassName = classId == null
                    ? null
                    : await _db.Classrooms.AsNoTracking()
                        .Where(c => c.Id == classId.Value)
                        .Select(c => c.Name)
                        .FirstOrDefaultAsync(),
                Items = await q
                    .OrderByDescending(a => a.CreatedAt)
                    .Select(a => new AssignmentListItemVM
                    {
                        Id = a.Id,
                        ClassId = a.ClassId,
                        ClassName = a.Class.Name,
                        Title = a.Title,
                        DueAt = a.CloseAt,
                        Status = a.Type == AssignmentType.Mixed ? "Mixed"
                               : a.Type == AssignmentType.Mcq ? "MCQ"
                               : "Essay",
                        MaxAttempts = a.MaxAttempts,
                        Description = a.Description
                    })
                    .ToListAsync()
            };

            return View(vm);
        }

        // =================== CREATE (GET) ===================
        [Authorize(Roles = "Teacher")]
        [HttpGet]
        public async Task<IActionResult> Create(int classId, AssignmentType type = AssignmentType.Mcq)
        {
            await EnsureTeacherOwnsClassAsync(classId);

            var vm = new AssignmentCreateVM
            {
                ClassId = classId,
                Type = type,
                Questions = type == AssignmentType.Essay
                    ? new() { new CreateQuestionVM { Type = QuestionType.Essay } }
                    : new() { new CreateQuestionVM { Type = QuestionType.Mcq } }
            };
            return View(vm);
        }

        // =================== CREATE (POST) ===================
        [Authorize(Roles = "Teacher")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AssignmentCreateVM vm)
        {
            if (!await TeacherOwnsClassAsync(vm.ClassId)) return Forbid();

            // dynamic ops
            if (!string.IsNullOrEmpty(vm.Op))
            {
                HandleDynamicOp(vm);
                ModelState.Clear();
                return View(vm);
            }

            ApplyCustomValidation(vm);
            if (!ModelState.IsValid) return View(vm);

            var me = await _userManager.GetUserAsync(User);

            var overall = InferOverallType(vm);
            var assignment = new Assignment
            {
                Title = vm.Title,
                Description = vm.Description,
                Type = overall,
                DurationMinutes = vm.DurationMinutes,
                MaxAttempts = vm.MaxAttempts,
                ClassId = vm.ClassId,
                CreatedById = me!.Id,
                CreatedAt = DateTimeOffset.UtcNow,
                OpenAt = vm.OpenAt,
                CloseAt = vm.CloseAt
            };

            int order = 1;
            foreach (var q in vm.Questions)
            {
                var qq = new AssignmentQuestion
                {
                    Assignment = assignment,
                    Type = q.Type,
                    Prompt = q.Prompt,
                    Points = q.Points,
                    Order = order++
                };

                if (q.Type == QuestionType.Mcq)
                {
                    for (int i = 0; i < (q.Choices?.Count ?? 0); i++)
                    {
                        var ch = q.Choices![i];
                        qq.Choices.Add(new AssignmentChoice
                        {
                            Text = ch.Text ?? "",
                            IsCorrect = i == q.CorrectIndex,
                            Order = i + 1
                        });
                    }
                }

                assignment.Questions.Add(qq);
            }

            _db.Assignments.Add(assignment);
            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Teacher), new { classId = assignment.ClassId });
        }

        // =================== EDIT (GET) ===================
        [Authorize(Roles = "Teacher")]
        [HttpGet]
        public async Task<IActionResult> Edit(int id, int? classId)
        {
            var me = await _userManager.GetUserAsync(User);

            var a = await _db.Assignments
                .Include(x => x.Class)
                .Include(x => x.Questions).ThenInclude(q => q.Choices)
                .FirstOrDefaultAsync(x => x.Id == id && x.Class.TeacherId == me!.Id);

            if (a == null) return NotFound();

            var vm = new AssignmentCreateVM
            {
                ClassId = a.ClassId,
                Title = a.Title,
                Description = a.Description,
                Type = a.Type,
                DurationMinutes = a.DurationMinutes,
                MaxAttempts = a.MaxAttempts,
                OpenAt = a.OpenAt,
                CloseAt = a.CloseAt,
                Questions = a.Questions
                    .OrderBy(q => q.Order)
                    .Select(q => new CreateQuestionVM
                    {
                        Type = q.Type,
                        Prompt = q.Prompt,
                        Points = q.Points,
                        Choices = q.Type == QuestionType.Mcq
                            ? q.Choices.OrderBy(c => c.Order)
                                       .Select(c => new CreateChoiceVM { Text = c.Text })
                                       .ToList()
                            : new System.Collections.Generic.List<CreateChoiceVM>(),
                        CorrectIndex = q.Type == QuestionType.Mcq
                            ? q.Choices.OrderBy(c => c.Order).ToList().FindIndex(c => c.IsCorrect)
                            : 0
                    })
                    .ToList()
            };

            ViewBag.EditId = id;
            return View("Create", vm);
        }

        // =================== EDIT (POST) ===================
        [Authorize(Roles = "Teacher")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, AssignmentCreateVM vm, int? classId)
        {
            if (!await TeacherOwnsClassAsync(vm.ClassId)) return Forbid();

            // dynamic ops in Edit too
            if (!string.IsNullOrEmpty(vm.Op))
            {
                HandleDynamicOp(vm);
                ModelState.Clear();
                ViewBag.EditId = id;
                return View("Create", vm);
            }

            ApplyCustomValidation(vm);
            if (!ModelState.IsValid) { ViewBag.EditId = id; return View("Create", vm); }

            var me = await _userManager.GetUserAsync(User);

            var a = await _db.Assignments
                .Include(x => x.Class)
                .Include(x => x.Questions).ThenInclude(q => q.Choices)
                .FirstOrDefaultAsync(x => x.Id == id && x.Class.TeacherId == me!.Id);

            if (a == null) return NotFound();

            var overall = InferOverallType(vm);

            a.Title = vm.Title;
            a.Description = vm.Description;
            a.Type = overall;
            a.DurationMinutes = vm.DurationMinutes;
            a.MaxAttempts = vm.MaxAttempts;
            a.OpenAt = vm.OpenAt;
            a.CloseAt = vm.CloseAt;

            // replace all questions for simplicity
            _db.AssignmentChoices.RemoveRange(a.Questions.SelectMany(q => q.Choices));
            _db.AssignmentQuestions.RemoveRange(a.Questions);
            a.Questions.Clear();

            int order = 1;
            foreach (var q in vm.Questions)
            {
                var qq = new AssignmentQuestion
                {
                    AssignmentId = a.Id,
                    Type = q.Type,
                    Prompt = q.Prompt,
                    Points = q.Points,
                    Order = order++
                };

                if (q.Type == QuestionType.Mcq)
                {
                    for (int i = 0; i < (q.Choices?.Count ?? 0); i++)
                    {
                        var ch = q.Choices![i];
                        qq.Choices.Add(new AssignmentChoice
                        {
                            Text = ch.Text ?? "",
                            IsCorrect = i == q.CorrectIndex,
                            Order = i + 1
                        });
                    }
                }

                a.Questions.Add(qq);
            }

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Teacher), new { classId = a.ClassId });
        }

        // =================== DELETE ===================
        [Authorize(Roles = "Teacher")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id, int? classId)
        {
            var me = await _userManager.GetUserAsync(User);

            var a = await _db.Assignments
                .Include(x => x.Class)
                .FirstOrDefaultAsync(x => x.Id == id && x.Class.TeacherId == me!.Id);

            if (a == null) return NotFound();

            _db.Assignments.Remove(a);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Teacher), new { classId = classId ?? a.ClassId });
        }

        // =================== Helpers ===================
        private static AssignmentType InferOverallType(AssignmentCreateVM vm)
        {
            var hasMcq = vm.Questions.Any(q => q.Type == QuestionType.Mcq);
            var hasEssay = vm.Questions.Any(q => q.Type == QuestionType.Essay);
            return hasMcq && hasEssay ? AssignmentType.Mixed
                   : hasMcq ? AssignmentType.Mcq
                   : AssignmentType.Essay;
        }

        private void HandleDynamicOp(AssignmentCreateVM vm)
        {
            switch (vm.Op)
            {
                case "add-q":
                    vm.Questions.Add(new CreateQuestionVM { Type = QuestionType.Mcq, Points = 1 });
                    break;

                case "remove-q":
                    if (vm.QIndex is int rq && rq >= 0 && rq < vm.Questions.Count)
                        vm.Questions.RemoveAt(rq);
                    break;

                case "add-choice":
                    if (vm.QIndex is int aq && aq >= 0 && aq < vm.Questions.Count)
                    {
                        var tgt = vm.Questions[aq];
                        if (tgt.Type != QuestionType.Mcq)
                        {
                            // nếu đang là essay mà add-choice thì cũng chuyển sang MCQ
                            tgt.Type = QuestionType.Mcq;
                        }
                        tgt.Choices.Add(new CreateChoiceVM());
                    }
                    break;

                case "remove-choice":
                    if (vm.QIndex is int cq && cq >= 0 && cq < vm.Questions.Count
                        && vm.ChoiceIndex is int rc && rc >= 0 && rc < vm.Questions[cq].Choices.Count)
                    {
                        var tgt = vm.Questions[cq];
                        tgt.Choices.RemoveAt(rc);
                        if (tgt.CorrectIndex >= tgt.Choices.Count)
                            tgt.CorrectIndex = Math.Max(0, tgt.Choices.Count - 1);
                    }
                    break;

                // chuyển đổi kiểu bằng postback để view re-render
                case "to-mcq":
                    if (vm.QIndex is int tm && tm >= 0 && tm < vm.Questions.Count)
                    {
                        var q = vm.Questions[tm];
                        q.Type = QuestionType.Mcq;
                        if (q.Choices == null || q.Choices.Count < 2)
                            q.Choices = new() { new(), new(), new(), new() };
                        if (q.CorrectIndex < 0 || q.CorrectIndex >= q.Choices.Count)
                            q.CorrectIndex = 0;
                    }
                    break;

                case "to-essay":
                    if (vm.QIndex is int te && te >= 0 && te < vm.Questions.Count)
                    {
                        var q = vm.Questions[te];
                        q.Type = QuestionType.Essay;
                        q.Choices.Clear();
                        q.CorrectIndex = 0;
                    }
                    break;
            }
        }

        private void ApplyCustomValidation(AssignmentCreateVM vm)
        {
            if (vm.OpenAt.HasValue && vm.CloseAt.HasValue && vm.CloseAt <= vm.OpenAt)
                ModelState.AddModelError(nameof(vm.CloseAt), "Due date must be after Open date.");

            if (vm.Questions == null || vm.Questions.Count == 0)
                ModelState.AddModelError(string.Empty, "At least one question is required.");

            for (int i = 0; i < vm.Questions.Count; i++)
            {
                var q = vm.Questions[i];

                if (q.Type == QuestionType.Mcq)
                {
                    if (q.Choices == null || q.Choices.Count < 2)
                        ModelState.AddModelError($"Questions[{i}].Choices", "MCQ must have at least 2 choices.");

                    if (q.CorrectIndex < 0 || q.CorrectIndex >= (q.Choices?.Count ?? 0))
                        ModelState.AddModelError($"Questions[{i}].CorrectIndex", "Select a valid correct answer.");
                }

                if (string.IsNullOrWhiteSpace(q.Prompt))
                    ModelState.AddModelError($"Questions[{i}].Prompt", "Prompt is required.");
            }
        }

        private async Task EnsureTeacherOwnsClassAsync(int classId)
        {
            var me = await _userManager.GetUserAsync(User);
            var ok = await _db.Classrooms.AsNoTracking()
                          .AnyAsync(c => c.Id == classId && c.TeacherId == me!.Id);
            if (!ok) throw new UnauthorizedAccessException("You do not own this class.");
        }

        private async Task<bool> TeacherOwnsClassAsync(int classId)
        {
            var me = await _userManager.GetUserAsync(User);
            return await _db.Classrooms.AsNoTracking()
                         .AnyAsync(c => c.Id == classId && c.TeacherId == me!.Id);
        }
    }
}
