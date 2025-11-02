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

        // ==== LISTS ====

        // STUDENT: danh sách bài (để modal xem chi tiết, start...)
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
                        MaxAttempts = a.MaxAttempts,
                        Description = a.Description,
                        DurationMinutes = a.DurationMinutes
                    })
                    .ToListAsync()
            };

            return View(vm);
        }

        // TEACHER: danh sách
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
                               : a.Type == AssignmentType.Mcq ? "MCQ" : "Essay",
                        MaxAttempts = a.MaxAttempts,
                        Type = a.Type,
                        Description = a.Description
                    })
                    .ToListAsync()
            };

            return View(vm);
        }

        //==== CREATE ====

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

        [Authorize(Roles = "Teacher")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(AssignmentCreateVM vm)
        {
            if (!await TeacherOwnsClassAsync(vm.ClassId)) return Forbid();

            if (ApplyDesignerOp(vm))
            {
                ModelState.Clear();
                return View(vm);
            }

            ValidateAssignment(vm);
            if (!ModelState.IsValid) return View(vm);

            var me = await _userManager.GetUserAsync(User);

            var hasMcq = vm.Questions.Any(q => q.Type == QuestionType.Mcq);
            var hasEssay = vm.Questions.Any(q => q.Type == QuestionType.Essay);
            var overall = hasMcq && hasEssay ? AssignmentType.Mixed
                         : hasMcq ? AssignmentType.Mcq
                         : AssignmentType.Essay;

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
                var qEntity = new AssignmentQuestion
                {
                    Assignment = assignment,
                    Type = q.Type,
                    Prompt = q.Prompt,
                    Points = q.Points, // decimal
                    Order = order++
                };

                if (q.Type == QuestionType.Mcq)
                {
                    if (q.Choices == null || q.Choices.Count == 0)
                        q.Choices = new() { new(), new(), new(), new() };

                    for (int i = 0; i < q.Choices.Count; i++)
                    {
                        var ch = q.Choices[i];
                        qEntity.Choices.Add(new AssignmentChoice
                        {
                            Text = ch.Text ?? "",
                            IsCorrect = i == q.CorrectIndex,
                            Order = i + 1
                        });
                    }
                }

                assignment.Questions.Add(qEntity);
            }

            _db.Assignments.Add(assignment);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Teacher), new { classId = assignment.ClassId });
        }

        //==== EDIT ====

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

        [Authorize(Roles = "Teacher")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, AssignmentCreateVM vm, int? classId)
        {
            if (!await TeacherOwnsClassAsync(vm.ClassId)) return Forbid();

            if (ApplyDesignerOp(vm))
            {
                ModelState.Clear();
                ViewBag.EditId = id;
                return View("Create", vm);
            }

            ValidateAssignment(vm);
            if (!ModelState.IsValid) { ViewBag.EditId = id; return View("Create", vm); }

            var me = await _userManager.GetUserAsync(User);
            var a = await _db.Assignments
                .Include(x => x.Class)
                .Include(x => x.Questions).ThenInclude(q => q.Choices)
                .FirstOrDefaultAsync(x => x.Id == id && x.Class.TeacherId == me!.Id);

            if (a == null) return NotFound();

            var hasMcq = vm.Questions.Any(q => q.Type == QuestionType.Mcq);
            var hasEssay = vm.Questions.Any(q => q.Type == QuestionType.Essay);
            var overall = hasMcq && hasEssay ? AssignmentType.Mixed
                         : hasMcq ? AssignmentType.Mcq
                         : AssignmentType.Essay;

            a.Title = vm.Title;
            a.Description = vm.Description;
            a.Type = overall;
            a.DurationMinutes = vm.DurationMinutes;
            a.MaxAttempts = vm.MaxAttempts;
            a.OpenAt = vm.OpenAt;
            a.CloseAt = vm.CloseAt;

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

        // ==== DELETE ====

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

        // ==== STUDENT: TAKE / SAVE / FINISH ====

        // UI làm bài: start hoặc resume attempt đang dở
        [Authorize(Roles = "Student")]
        [HttpGet]
        public async Task<IActionResult> Take(int id, int index = 0)
        {
            var me = await _userManager.GetUserAsync(User);

            var a = await _db.Assignments
                .Include(x => x.Class)
                .Include(x => x.Questions).ThenInclude(q => q.Choices)
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == id);
            if (a == null) return NotFound();

            // attempt đang làm
            var attempt = await _db.AssignmentAttempts
                .Include(t => t.Answers)
                .Where(t => t.AssignmentId == id && t.UserId == me!.Id && t.Status == AttemptStatus.InProgress)
                .OrderByDescending(t => t.StartedAt)
                .FirstOrDefaultAsync();

            if (attempt == null)
            {
                // số lượt đã làm
                var taken = await _db.AssignmentAttempts
                    .CountAsync(t => t.AssignmentId == id && t.UserId == me!.Id);

                if (taken >= a.MaxAttempts) return Forbid();

                attempt = new AssignmentAttempt
                {
                    AssignmentId = a.Id,
                    UserId = me!.Id,
                    AttemptNumber = taken + 1,
                    DurationMinutes = a.DurationMinutes,
                    StartedAt = DateTimeOffset.UtcNow,
                    RequiresManualGrading = a.Questions.Any(q => q.Type == QuestionType.Essay),
                    MaxScore = a.Questions.Sum(q => q.Points)
                };
                _db.AssignmentAttempts.Add(attempt);
                await _db.SaveChangesAsync();

                // seed câu trả lời rỗng
                foreach (var q in a.Questions.OrderBy(q => q.Order))
                {
                    _db.AssignmentAnswers.Add(new AssignmentAnswer
                    {
                        AttemptId = attempt.Id,
                        QuestionId = q.Id
                    });
                }
                await _db.SaveChangesAsync();
            }

            var answers = await _db.AssignmentAnswers
                .Where(x => x.AttemptId == attempt.Id)
                .ToListAsync();

            var vm = new TakeAttemptVM
            {
                AssignmentId = a.Id,
                AttemptId = attempt.Id,
                Title = a.Title,
                ClassName = a.Class.Name,
                DurationMinutes = attempt.DurationMinutes,
                StartedAt = attempt.StartedAt,
                DueAt = attempt.StartedAt.AddMinutes(attempt.DurationMinutes),
                CurrentIndex = Math.Max(0, Math.Min(index, a.Questions.Count - 1)),
                Questions = a.Questions
                    .OrderBy(q => q.Order)
                    .Select((q, i) =>
                    {
                        var ans = answers.First(x => x.QuestionId == q.Id);
                        return new TakeQuestionVM
                        {
                            QuestionId = q.Id,
                            Index = i,
                            Prompt = q.Prompt,
                            Points = (double)q.Points,
                            Type = q.Type,
                            SelectedChoiceId = ans.SelectedChoiceId,
                            TextAnswer = ans.TextAnswer,
                            Choices = q.Type == QuestionType.Mcq
                                ? q.Choices.OrderBy(c => c.Order)
                                    .Select(c => new TakeChoiceVM { ChoiceId = c.Id, Text = c.Text })
                                    .ToList()
                                : new(),
                            IsAnswered = (q.Type == QuestionType.Mcq && ans.SelectedChoiceId != null)
                                         || (q.Type == QuestionType.Essay && !string.IsNullOrWhiteSpace(ans.TextAnswer))
                        };
                    })
                    .ToList()
            };
            vm.AnsweredCount = vm.Questions.Count(q => q.IsAnswered);

            return View("Take", vm);
        }

        // DTO để nhận JSON khi lưu câu trả lời
        public class SaveAnswerDTO
        {
            public int AttemptId { get; set; }
            public int QuestionId { get; set; }
            public int? SelectedChoiceId { get; set; }
            public string? TextAnswer { get; set; }
            public bool? Marked { get; set; }
        }

        // Lưu câu trả lời (AJAX)
        [Authorize(Roles = "Student")]
        [HttpPost]
        public async Task<IActionResult> SaveAnswer([FromBody] SaveAnswerDTO dto)
        {
            var me = await _userManager.GetUserAsync(User);
            var att = await _db.AssignmentAttempts
                .Include(t => t.Assignment).ThenInclude(a => a.Questions)
                .FirstOrDefaultAsync(t => t.Id == dto.AttemptId && t.UserId == me!.Id);
            if (att == null || att.Status != AttemptStatus.InProgress) return BadRequest();

            // quá hạn
            if (DateTimeOffset.UtcNow > att.StartedAt.AddMinutes(att.DurationMinutes))
                return BadRequest("Time is over");

            var ans = await _db.AssignmentAnswers
                .FirstOrDefaultAsync(x => x.AttemptId == att.Id && x.QuestionId == dto.QuestionId);
            if (ans == null) return NotFound();

            if (dto.SelectedChoiceId.HasValue)
            {
                ans.SelectedChoiceId = dto.SelectedChoiceId;
                ans.TextAnswer = null;
            }
            if (dto.TextAnswer != null)
            {
                ans.TextAnswer = dto.TextAnswer;
                ans.SelectedChoiceId = null;
            }
            await _db.SaveChangesAsync();
            return Ok();
        }

        // Nộp bài
        [Authorize(Roles = "Student")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Finish(int attemptId)
        {
            var me = await _userManager.GetUserAsync(User);
            var att = await _db.AssignmentAttempts
                .Include(t => t.Assignment).ThenInclude(a => a.Questions).ThenInclude(q => q.Choices)
                .Include(t => t.Answers)
                .FirstOrDefaultAsync(t => t.Id == attemptId && t.UserId == me!.Id);
            if (att == null) return NotFound();

            if (att.Status != AttemptStatus.InProgress)
                return RedirectToAction(nameof(Student), new { classId = att.Assignment.ClassId });

            // chấm tự động MCQ
            decimal auto = 0m;
            foreach (var q in att.Assignment.Questions)
            {
                var ans = att.Answers.FirstOrDefault(x => x.QuestionId == q.Id);
                if (q.Type == QuestionType.Mcq)
                {
                    var correct = q.Choices.FirstOrDefault(c => c.IsCorrect)?.Id;
                    if (ans?.SelectedChoiceId != null && correct == ans.SelectedChoiceId)
                        auto += q.Points;
                }
            }
            att.AutoScore = auto;
            att.FinalScore = att.RequiresManualGrading ? null : auto;
            att.SubmittedAt = DateTimeOffset.UtcNow;
            att.Status = att.RequiresManualGrading ? AttemptStatus.Submitted : AttemptStatus.Graded;

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Student), new { classId = att.Assignment.ClassId });
        }

        // =========================== HELPERS ===========================

        private bool ApplyDesignerOp(AssignmentCreateVM vm)
        {
            switch (vm.Op)
            {
                case "add-q":
                    vm.Questions.Add(new CreateQuestionVM { Type = QuestionType.Mcq, Points = 1m });
                    return true;

                case "remove-q":
                    if (vm.QIndex is int rq && rq >= 0 && rq < vm.Questions.Count)
                        vm.Questions.RemoveAt(rq);
                    return true;

                case "add-choice":
                    if (vm.QIndex is int aq && aq >= 0 && aq < vm.Questions.Count)
                        vm.Questions[aq].Choices.Add(new CreateChoiceVM());
                    return true;

                case "remove-choice":
                    if (vm.QIndex is int cq && cq >= 0 && cq < vm.Questions.Count
                        && vm.ChoiceIndex is int rc && rc >= 0 && rc < vm.Questions[cq].Choices.Count)
                    {
                        vm.Questions[cq].Choices.RemoveAt(rc);
                        if (vm.Questions[cq].CorrectIndex >= vm.Questions[cq].Choices.Count)
                            vm.Questions[cq].CorrectIndex = Math.Max(0, vm.Questions[cq].Choices.Count - 1);
                    }
                    return true;

                default:
                    return false;
            }
        }

        private void ValidateAssignment(AssignmentCreateVM vm)
        {
            if (vm.OpenAt.HasValue && vm.CloseAt.HasValue && vm.CloseAt <= vm.OpenAt)
                ModelState.AddModelError(nameof(vm.CloseAt), "Due date must be after Open date.");

            if (vm.Questions == null || vm.Questions.Count == 0)
                ModelState.AddModelError(string.Empty, "At least one question is required.");

            for (int i = 0; i < vm.Questions.Count; i++)
            {
                var q = vm.Questions[i];

                // bội số 0.5
                if (q.Points % 0.5m != 0)
                    ModelState.AddModelError($"Questions[{i}].Points", "Points must be a multiple of 0.5.");

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
