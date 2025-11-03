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

            var usedByAss = await _db.AssignmentAttempts
                .Where(x => x.UserId == me.Id)
                .GroupBy(x => x.AssignmentId)
                .Select(g => new { AssignmentId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.AssignmentId, x => x.Count);

            var items = await q
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
                    DurationMinutes = a.DurationMinutes,
                    AttemptsUsed = 0
                })
                .ToListAsync();

            foreach (var it in items)
                it.AttemptsUsed = usedByAss.TryGetValue(it.Id, out var c) ? c : 0;

            var vm = new AssignmentStudentVM
            {
                ClassId = classId,
                ClassName = classId == null
                    ? null
                    : await _db.Classrooms.AsNoTracking()
                        .Where(c => c.Id == classId.Value)
                        .Select(c => c.Name)
                        .FirstOrDefaultAsync(),
                Items = items
            };

            if (TempData["Error"] is string msg && !string.IsNullOrWhiteSpace(msg))
                ViewBag.Error = msg;

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

            // Cập nhật metadata
            a.Title = vm.Title;
            a.Description = vm.Description;
            a.Type = overall;
            a.DurationMinutes = vm.DurationMinutes;
            a.MaxAttempts = vm.MaxAttempts;
            a.OpenAt = vm.OpenAt;
            a.CloseAt = vm.CloseAt;

            // Lấy id các câu hỏi hiện thời của assignment
            var existingQIds = a.Questions.Select(q => q.Id).ToList();

            if (existingQIds.Count > 0)
            {
                var relatedAnswers = await _db.AssignmentAnswers
                    .Where(ans => existingQIds.Contains(ans.QuestionId))
                    .ToListAsync();

                if (relatedAnswers.Count > 0)
                {
                    _db.AssignmentAnswers.RemoveRange(relatedAnswers);
                    await _db.SaveChangesAsync();
                }
            }


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
                .Include(x => x.Questions).ThenInclude(q => q.Choices)
                .Include(x => x.Attempts).ThenInclude(t => t.Answers)
                .FirstOrDefaultAsync(x => x.Id == id && x.Class.TeacherId == me!.Id);

            if (a == null) return NotFound();

            if (a.Attempts?.Count > 0)
            {
                var allAns = a.Attempts.SelectMany(t => t.Answers ?? Enumerable.Empty<AssignmentAnswer>()).ToList();
                if (allAns.Count > 0) _db.AssignmentAnswers.RemoveRange(allAns);
                await _db.SaveChangesAsync();
            }

            if (a.Attempts?.Count > 0)
            {
                _db.AssignmentAttempts.RemoveRange(a.Attempts);
                await _db.SaveChangesAsync();
            }

            if (a.Questions?.Count > 0)
            {
                var allChoices = a.Questions.SelectMany(q => q.Choices ?? Enumerable.Empty<AssignmentChoice>()).ToList();
                if (allChoices.Count > 0) _db.AssignmentChoices.RemoveRange(allChoices);
                await _db.SaveChangesAsync();
            }

            if (a.Questions?.Count > 0)
            {
                _db.AssignmentQuestions.RemoveRange(a.Questions);
                await _db.SaveChangesAsync();
            }

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

            // Nếu đang có attempt dở thì cho resume (kể cả cửa sổ đã đóng)
            var attempt = await _db.AssignmentAttempts
                .Include(t => t.Answers)
                .Where(t => t.AssignmentId == id && t.UserId == me!.Id && t.Status == AttemptStatus.InProgress)
                .OrderByDescending(t => t.StartedAt)
                .FirstOrDefaultAsync();

            // Khi tạo mới: phải Open và còn lượt
            if (attempt == null)
            {
                var now = DateTimeOffset.UtcNow;
                bool isOpenWindow =
                    (a.OpenAt == null || now >= a.OpenAt) &&
                    (a.CloseAt == null || now <= a.CloseAt);

                var taken = await _db.AssignmentAttempts
                    .CountAsync(t => t.AssignmentId == id && t.UserId == me!.Id);

                bool hasAttemptsLeft = a.MaxAttempts <= 0 || taken < a.MaxAttempts;

                if (!isOpenWindow || !hasAttemptsLeft)
                {
                    TempData["Error"] = !hasAttemptsLeft
                        ? $"You have reached the attempt limit ({taken} / {a.MaxAttempts})."
                        : (a.CloseAt != null && now > a.CloseAt
                            ? "This assignment is closed. You cannot start a new attempt."
                            : "This assignment is not open yet.");

                    return RedirectToAction(nameof(Student), new { classId = a.ClassId });
                }

                // hợp lệ: tạo attempt mới
                var requiresManual = a.Questions.Any(q => q.Type == QuestionType.Essay);

                attempt = new AssignmentAttempt
                {
                    AssignmentId = a.Id,
                    UserId = me!.Id,
                    AttemptNumber = taken + 1,
                    DurationMinutes = a.DurationMinutes,
                    StartedAt = DateTimeOffset.UtcNow,
                    RequiresManualGrading = requiresManual,
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

        [Authorize(Roles = "Student")]
        [HttpPost]
        public async Task<IActionResult> SaveAnswer([FromBody] SaveAnswerDto dto)
        {
            var me = await _userManager.GetUserAsync(User);
            var att = await _db.AssignmentAttempts
                .Include(t => t.Assignment).ThenInclude(a => a.Questions)
                .FirstOrDefaultAsync(t => t.Id == dto.AttemptId && t.UserId == me!.Id);
            if (att == null || att.Status != AttemptStatus.InProgress) return BadRequest();

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

            decimal auto = 0m;
            foreach (var q in att.Assignment.Questions)
            {
                var ans = att.Answers.FirstOrDefault(x => x.QuestionId == q.Id);
                if (q.Type == QuestionType.Mcq)
                {
                    var correctChoiceId = q.Choices.FirstOrDefault(c => c.IsCorrect)?.Id;

                    if (ans != null)
                    {
                        var isRight = ans.SelectedChoiceId != null && correctChoiceId == ans.SelectedChoiceId;
                        ans.IsCorrect = isRight; // đánh dấu để History đếm chuẩn
                        if (isRight) auto += q.Points;
                    }
                }
            }

            att.AutoScore = auto;
            att.FinalScore = att.RequiresManualGrading ? null : auto;
            att.SubmittedAt = DateTimeOffset.UtcNow;
            att.Status = att.RequiresManualGrading ? AttemptStatus.Submitted : AttemptStatus.Graded;

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Student), new { classId = att.Assignment.ClassId });
        }

        // ==== NEW: TEST HISTORY ====
        [Authorize(Roles = "Student")]
        [HttpGet]
        public async Task<IActionResult> History(int id)
        {
            var me = await _userManager.GetUserAsync(User);

            var assignment = await _db.Assignments
                .AsNoTracking()
                .Where(a => a.Id == id)
                .Select(a => new { a.Id, a.Title, a.MaxAttempts })
                .FirstOrDefaultAsync();

            if (assignment == null) return NotFound();

            var rawAttempts = await _db.AssignmentAttempts
                .AsNoTracking()
                .Include(t => t.Assignment)
                    .ThenInclude(a => a.Questions)
                        .ThenInclude(q => q.Choices)
                .Include(t => t.Answers)
                .Where(t => t.AssignmentId == id && t.UserId == me!.Id)
                .OrderByDescending(t => t.SubmittedAt ?? t.StartedAt)
                .ToListAsync();

            var list = new System.Collections.Generic.List<TestAttemptListItemVM>();

            foreach (var t in rawAttempts)
            {
                var answers = t.Answers ?? new System.Collections.Generic.List<AssignmentAnswer>();

                var mcqQs = t.Assignment.Questions.Where(q => q.Type == QuestionType.Mcq).ToList();
                var essayQs = t.Assignment.Questions.Where(q => q.Type == QuestionType.Essay).ToList();

                var mcqIds = mcqQs.Select(q => q.Id).ToHashSet();
                var mcqTotal = mcqIds.Count;

                var mcqMax = mcqQs.Sum(q => q.Points);
                var essayMax = essayQs.Sum(q => q.Points);
                var finalMax = mcqMax + essayMax;

                var correctChoiceByQ = mcqQs.ToDictionary(
                    q => q.Id,
                    q => q.Choices.FirstOrDefault(c => c.IsCorrect)?.Id
                );

                int mcqCorrect = answers
                    .Where(a => mcqIds.Contains(a.QuestionId))
                    .Count(a =>
                    {
                        if (a.IsCorrect == true) return true;
                        if (a.IsCorrect == null && a.SelectedChoiceId.HasValue &&
                            correctChoiceByQ.TryGetValue(a.QuestionId, out var ccid) &&
                            ccid.HasValue && ccid.Value == a.SelectedChoiceId.Value)
                        {
                            return true;
                        }
                        return false;
                    });

                decimal mcqScore;
                if (t.AutoScore.HasValue)
                {
                    mcqScore = t.AutoScore.Value;
                }
                else
                {
                    var qPoints = mcqQs.ToDictionary(q => q.Id, q => q.Points);
                    mcqScore = answers
                        .Where(a => mcqIds.Contains(a.QuestionId) && (
                            a.IsCorrect == true ||
                            (a.IsCorrect == null && a.SelectedChoiceId.HasValue &&
                             correctChoiceByQ.TryGetValue(a.QuestionId, out var ccid2) &&
                             ccid2.HasValue && ccid2.Value == a.SelectedChoiceId.Value)))
                        .Sum(a => qPoints[a.QuestionId]);
                }

                decimal? essayScore = t.RequiresManualGrading
                    ? (t.FinalScore.HasValue
                        ? Math.Clamp(t.FinalScore.Value - mcqScore, 0m, essayMax)
                        : (decimal?)null)
                    : 0m;

                decimal? finalScore = t.FinalScore;
                if (!finalScore.HasValue)
                    finalScore = t.RequiresManualGrading ? (decimal?)null : mcqScore;

                list.Add(new TestAttemptListItemVM
                {
                    AttemptId = t.Id,
                    AttemptNumber = t.AttemptNumber,
                    StartedAt = t.StartedAt,
                    SubmittedAt = t.SubmittedAt,
                    DurationMinutes = t.DurationMinutes,

                    CorrectCount = mcqCorrect,
                    TotalQuestions = mcqTotal,
                    Score = mcqScore,
                    MaxScore = mcqMax,

                    Status = t.Status.ToString(),
                    RequiresManual = t.RequiresManualGrading,

                    McqCorrect = mcqCorrect,
                    McqTotal = mcqTotal,
                    McqScore = mcqScore,
                    McqMax = mcqMax,

                    EssayScore = essayScore,
                    EssayMax = essayMax,

                    FinalScore = finalScore,
                    FinalMax = finalMax
                });
            }

            var vm = new TestHistoryVM
            {
                AssignmentId = assignment.Id,
                AssignmentTitle = assignment.Title,
                Attempts = list,
                MaxAttempts = assignment.MaxAttempts
            };

            return PartialView("_TestHistoryModal", vm);
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

                if (q.Points % 0.5m != 0)
                    ModelState.AddModelError($"Questions[{i}].Points", "Points must be a multiple of 0.01.");

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
