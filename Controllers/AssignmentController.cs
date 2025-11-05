using System;
using System.Globalization;
using System.Linq;
using System.Text;
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
        //Test Details After
        [Authorize(Roles = "Student")]
        [HttpGet]
        public async Task<IActionResult> Review(int attemptId, string? returnUrl = null)
        {
            var me = await _userManager.GetUserAsync(User);
            if (me == null) return Challenge();

            var t = await _db.AssignmentAttempts
                .Include(x => x.Assignment).ThenInclude(a => a.Questions).ThenInclude(q => q.Choices)
                .Include(x => x.Answers).ThenInclude(a => a.SelectedChoice)
                .FirstOrDefaultAsync(x => x.Id == attemptId && x.UserId == me.Id);
            if (t == null) return NotFound();

            // Chỉ xem khi đã CLOSED
            var now = DateTimeOffset.UtcNow;
            var isClosed = t.Assignment.CloseAt.HasValue && t.Assignment.CloseAt.Value <= now;
            if (!isClosed)
            {
                TempData["Warn"] = "Bài thi chưa đóng. Không thể xem đáp án lúc này.";
                return RedirectToAction(nameof(History), new { id = t.AssignmentId });
            }

            var vm = new AttemptReviewVM
            {
                AttemptId = t.Id,
                AssignmentId = t.AssignmentId,
                AssignmentTitle = t.Assignment.Title ?? "Assignment",
                OpenAt = t.Assignment.OpenAt,
                CloseAt = t.Assignment.CloseAt,
                IsClosed = true,
                // Nếu đã chấm essay thì FinalScore có giá trị, còn không chỉ có AutoScore
                Score = t.FinalScore ?? t.AutoScore ?? 0m,
                TotalMax = t.MaxScore,
                StartedAt = t.StartedAt,
                SubmittedAt = t.SubmittedAt,
                Duration = t.SubmittedAt.HasValue ? t.SubmittedAt.Value - t.StartedAt : (TimeSpan?)null
            };

            var qs = t.Assignment.Questions.OrderBy(q => q.Order).ToList();
            for (int i = 0; i < qs.Count; i++)
            {
                var q = qs[i];
                var ans = t.Answers.FirstOrDefault(a => a.QuestionId == q.Id);

                var item = new QuestionReviewItem
                {
                    Index = i + 1,
                    Type = q.Type,
                    Prompt = q.Prompt,
                    Points = q.Points
                };

                if (q.Type == POETWeb.Models.Enums.QuestionType.Mcq)
                {
                    var correct = q.Choices.FirstOrDefault(c => c.IsCorrect)?.Id;
                    var chosen = ans?.SelectedChoiceId;

                    item.ChosenChoiceId = chosen;
                    item.CorrectChoiceId = correct;
                    item.Choices = q.Choices
                        .OrderBy(c => c.Order)
                        .Select(c => new McqChoiceVM
                        {
                            ChoiceId = c.Id,
                            Text = c.Text,
                            IsChosen = chosen.HasValue && chosen.Value == c.Id,
                            IsCorrect = correct.HasValue && correct.Value == c.Id
                        })
                        .ToList();
                }
                else
                {
                    item.EssayText = ans?.TextAnswer;
                    item.EssayScore = ans?.PointsAwarded;      // điểm chấm tay
                    item.TeacherComment = ans?.TeacherComment; // nhận xét câu
                }

                vm.Questions.Add(item);
            }

            ViewBag.ReturnUrl = string.IsNullOrWhiteSpace(returnUrl)
            ? Url.Action("Student", "Assignment")
            : returnUrl;
            return View(vm);
        }

        // FUNCTIONs OF TEACHER
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
                        ans.IsCorrect = isRight;
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

        // ==== TEST HISTORY ====
        [Authorize(Roles = "Student")]
        [HttpGet]
        public async Task<IActionResult> History(int id)
        {
            var me = await _userManager.GetUserAsync(User);

            var assignment = await _db.Assignments
                .AsNoTracking()
                .Where(a => a.Id == id)
                .Select(a => new { a.Id, a.Title, a.MaxAttempts, a.CloseAt })
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

            var now = DateTimeOffset.UtcNow;
            ViewBag.IsClosed = assignment.CloseAt.HasValue && assignment.CloseAt.Value <= now;

            return PartialView("_TestHistoryModal", vm);
        }

        // ==== TEACHER: SUBMISSIONS ====
        [Authorize(Roles = "Teacher")]
        [HttpGet]
        public async Task<IActionResult> Submissions(int id)
        {
            var me = await _userManager.GetUserAsync(User);

            var a = await _db.Assignments
                .Include(x => x.Class)
                .FirstOrDefaultAsync(x => x.Id == id && x.Class.TeacherId == me!.Id);
            if (a == null) return NotFound();

            var attempts = await _db.AssignmentAttempts
                .AsNoTracking()
                .Include(t => t.Answers)
                .Include(t => t.Assignment).ThenInclude(A => A.Questions).ThenInclude(q => q.Choices)
                .Where(t => t.AssignmentId == id)
                .OrderByDescending(t => t.SubmittedAt ?? t.StartedAt)
                .ToListAsync();

            var userIds = attempts.Select(t => t.UserId).Distinct().ToList();
            var users = await _db.Users.Where(u => userIds.Contains(u.Id))
                .Select(u => new { u.Id, u.FullName, u.Email })
                .ToDictionaryAsync(u => u.Id, u => u);

            var list = new List<SubmissionListItemVM>();

            foreach (var t in attempts)
            {
                var mcqQs = t.Assignment.Questions.Where(q => q.Type == QuestionType.Mcq).ToList();
                var essayQs = t.Assignment.Questions.Where(q => q.Type == QuestionType.Essay).ToList();
                var mcqIds = mcqQs.Select(q => q.Id).ToHashSet();

                var mcqMax = mcqQs.Sum(q => q.Points);
                var essayMax = essayQs.Sum(q => q.Points);
                var finalMax = mcqMax + essayMax;

                decimal mcqScore = t.AutoScore ?? 0m;
                if (!t.AutoScore.HasValue)
                {
                    var correctByQ = mcqQs.ToDictionary(q => q.Id, q => q.Choices.FirstOrDefault(c => c.IsCorrect)?.Id);
                    var qPoints = mcqQs.ToDictionary(q => q.Id, q => q.Points);
                    mcqScore = t.Answers
                        .Where(a => mcqIds.Contains(a.QuestionId) &&
                            (a.IsCorrect == true ||
                             (a.IsCorrect == null && a.SelectedChoiceId.HasValue &&
                              correctByQ.TryGetValue(a.QuestionId, out var cc) && cc.HasValue && cc.Value == a.SelectedChoiceId.Value)))
                        .Sum(a => qPoints[a.QuestionId]);
                }

                // Essay score
                var essayScore = t.Answers
                    .Where(a => !mcqIds.Contains(a.QuestionId))
                    .Where(a => a.PointsAwarded.HasValue)
                    .Sum(a => a.PointsAwarded!.Value);

                decimal? essayScoreNullable = essayMax == 0 ? 0m : (t.RequiresManualGrading ? (decimal?)essayScore : 0m);
                decimal? finalScore = t.FinalScore ?? (t.RequiresManualGrading ? (decimal?)null : mcqScore);

                users.TryGetValue(t.UserId, out var u);

                list.Add(new SubmissionListItemVM
                {
                    AttemptId = t.Id,
                    AttemptNumber = t.AttemptNumber,
                    StudentId = t.UserId,
                    StudentName = u?.FullName ?? "(unknown)",
                    StudentEmail = u?.Email ?? "",
                    StartedAt = t.StartedAt,
                    SubmittedAt = t.SubmittedAt,
                    Status = t.Status.ToString(),

                    McqScore = mcqScore,
                    McqMax = mcqMax,
                    EssayScore = essayScoreNullable,
                    EssayMax = essayMax,
                    FinalScore = finalScore,
                    FinalMax = finalMax,

                    RequiresManual = t.RequiresManualGrading
                });
            }

            var vm = new SubmissionsVM
            {
                AssignmentId = a.Id,
                ClassId = a.ClassId,
                AssignmentTitle = a.Title,
                Items = list
            };

            return View("Submissions", vm);
        }

        // TEACHER: chấm điểm
        [Authorize(Roles = "Teacher")]
        [HttpGet]
        public async Task<IActionResult> Grade(int attemptId)
        {
            var me = await _userManager.GetUserAsync(User);

            var t = await _db.AssignmentAttempts
                .Include(x => x.Assignment).ThenInclude(a => a.Class)
                .Include(x => x.Assignment).ThenInclude(a => a.Questions).ThenInclude(q => q.Choices)
                .Include(x => x.Answers)
                .FirstOrDefaultAsync(x => x.Id == attemptId && x.Assignment.Class.TeacherId == me!.Id);
            if (t == null) return NotFound();

            var u = await _db.Users.Where(x => x.Id == t.UserId)
                .Select(x => new { x.Id, x.FullName, x.Email }).FirstOrDefaultAsync();

            var mcqQs = t.Assignment.Questions.Where(q => q.Type == QuestionType.Mcq).OrderBy(q => q.Order).ToList();
            var essayQs = t.Assignment.Questions.Where(q => q.Type == QuestionType.Essay).OrderBy(q => q.Order).ToList();
            var mcqIds = mcqQs.Select(q => q.Id).ToHashSet();

            var mcqMax = mcqQs.Sum(q => q.Points);
            var essayMax = essayQs.Sum(q => q.Points);
            var finalMax = mcqMax + essayMax;

            decimal mcqScore = t.AutoScore ?? 0m;
            if (!t.AutoScore.HasValue)
            {
                var correctByQ = mcqQs.ToDictionary(q => q.Id, q => q.Choices.FirstOrDefault(c => c.IsCorrect)?.Id);
                var qPoints = mcqQs.ToDictionary(q => q.Id, q => q.Points);
                mcqScore = t.Answers
                    .Where(a => mcqIds.Contains(a.QuestionId) &&
                        (a.IsCorrect == true ||
                         (a.IsCorrect == null && a.SelectedChoiceId.HasValue &&
                          correctByQ.TryGetValue(a.QuestionId, out var cc) && cc.HasValue && cc.Value == a.SelectedChoiceId.Value)))
                    .Sum(a => qPoints[a.QuestionId]);
            }

            var essays = new List<GradeEssayItemVM>();
            foreach (var q in essayQs)
            {
                var ans = t.Answers.FirstOrDefault(a => a.QuestionId == q.Id);
                essays.Add(new GradeEssayItemVM
                {
                    QuestionId = q.Id,
                    Prompt = q.Prompt,
                    MaxPoints = q.Points,
                    StudentAnswer = ans?.TextAnswer,
                    Score = ans?.PointsAwarded,
                    Comment = ans?.TeacherComment
                });
            }

            var vm = new GradeAttemptVM
            {
                AssignmentId = t.AssignmentId,
                AssignmentTitle = t.Assignment.Title,
                AttemptId = t.Id,
                AttemptNumber = t.AttemptNumber,
                StudentId = u?.Id ?? t.UserId,
                StudentName = u?.FullName ?? "(unknown)",
                StudentEmail = u?.Email ?? "",

                StartedAt = t.StartedAt,
                SubmittedAt = t.SubmittedAt,
                Status = t.Status.ToString(),

                McqScore = mcqScore,
                McqMax = mcqMax,
                EssayMax = essayMax,
                CurrentEssayScore = t.Answers.Where(a => !mcqIds.Contains(a.QuestionId)).Sum(a => a.PointsAwarded ?? 0m),
                FinalMax = finalMax,
                CurrentFinalScore = t.FinalScore,

                Essays = essays,
                TeacherComment = t.TeacherComment       // nếu đã thêm field
            };

            return View("Grade", vm);
        }

        // TEACHER: lưu điểm chấm
        [Authorize(Roles = "Teacher")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Grade(GradeAttemptVM vm)
        {
            var me = await _userManager.GetUserAsync(User);

            var t = await _db.AssignmentAttempts
                .Include(x => x.Assignment).ThenInclude(a => a.Questions)
                .Include(x => x.Answers)
                .FirstOrDefaultAsync(x => x.Id == vm.AttemptId && x.Assignment.Class.TeacherId == me!.Id);
            if (t == null) return NotFound();

            // Validate & apply essay scores/comments
            decimal essayScore = 0m;

            foreach (var e in vm.Essays ?? new List<GradeEssayItemVM>())
            {
                var q = t.Assignment.Questions.FirstOrDefault(x => x.Id == e.QuestionId && x.Type == QuestionType.Essay);
                if (q == null) continue;

                if (e.Score.HasValue)
                {
                    if (e.Score.Value < 0 || e.Score.Value > q.Points)
                        ModelState.AddModelError("", $"Score must be between 0 and {q.Points}.");
                    if (e.Score.Value % 0.5m != 0)
                        ModelState.AddModelError("", "Essay scores must be multiples of 0.5.");
                }
            }

            if (!ModelState.IsValid)
                return View("Grade", vm);

            foreach (var e in vm.Essays ?? new List<GradeEssayItemVM>())
            {
                var ans = t.Answers.FirstOrDefault(a => a.QuestionId == e.QuestionId);
                if (ans == null) continue;

                ans.PointsAwarded = e.Score;                 // lưu điểm câu Essay
                                                             // Nếu đã thêm field:
                ans.TeacherComment = e.Comment;              // lưu comment câu

                if (e.Score.HasValue) essayScore += e.Score.Value;
            }

            // overall comment (nếu đã thêm field)
            t.TeacherComment = vm.TeacherComment;

            // Final = Auto(MCQ) + sum Essay
            var mcq = t.AutoScore ?? 0m;
            t.FinalScore = mcq + essayScore;
            t.Status = AttemptStatus.Graded;

            await _db.SaveChangesAsync();
            return RedirectToAction(nameof(Submissions), new { id = t.AssignmentId });
        }

        // ====== IMPORT FROM TXT ======
        [Authorize(Roles = "Teacher")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ImportTxt(int classId, IFormFile txtFile)
        {
            if (!await TeacherOwnsClassAsync(classId)) return Forbid();

            if (txtFile == null || txtFile.Length == 0)
            {
                ModelState.AddModelError(string.Empty, "Please choose a non-empty .txt file.");
                return await Create(classId);
            }

            AssignmentCreateVM vm;
            using (var stream = txtFile.OpenReadStream())
            using (var reader = new StreamReader(stream, Encoding.UTF8, true))
            {
                var raw = await reader.ReadToEndAsync();
                try
                {
                    vm = ParseAssignmentTxt(raw, classId);
                }
                catch (FormatException ex)
                {
                    ModelState.AddModelError(string.Empty, $"Import error: {ex.Message}");
                    return await Create(classId);
                }
            }

            // Cho user thấy form đã đổ dữ liệu (validation chi tiết làm ở Save)
            ValidateAssignment(vm);
            return View("Create", vm);
        }

        private static readonly string[] TrueLetters =
            new[] { "A","B","C","D","E","F","G","H","I","J","K","L","M",
            "N","O","P","Q","R","S","T","U","V","W","X","Y","Z" };

        private AssignmentCreateVM ParseAssignmentTxt(string raw, int classId)
        {
            var vm = new AssignmentCreateVM
            {
                ClassId = classId,
                Type = AssignmentType.Mcq, // sẽ auto quyết định lại sau
                DurationMinutes = 30,
                MaxAttempts = 1,
                TotalPointsMax = 100,      // MẶC ĐỊNH 100 nếu file không chỉ định
                Questions = new List<CreateQuestionVM>()
            };

            if (string.IsNullOrWhiteSpace(raw))
                throw new FormatException("Empty file.");

            var lines = raw.Replace("\r\n", "\n").Split('\n');
            int i = 0;

            // helpers
            Func<string, bool> isBlank = s => string.IsNullOrWhiteSpace(s?.Trim());
            string peek(int k = 0) => i + k < lines.Length ? lines[i + k] : "";
            string read() => i < lines.Length ? lines[i++] : "";

            // ======= METADATA =======
            while (i < lines.Length)
            {
                var line = peek();
                if (line.TrimStart().StartsWith("Q:", StringComparison.OrdinalIgnoreCase))
                    break;

                read();
                var t = line.Trim();

                if (t.StartsWith("Title:", StringComparison.OrdinalIgnoreCase)) vm.Title = t.Substring(6).Trim();
                else if (t.StartsWith("Description:", StringComparison.OrdinalIgnoreCase)) vm.Description = t.Substring(12).Trim();
                else if (t.StartsWith("TotalPoints:", StringComparison.OrdinalIgnoreCase))
                {
                    var v = ParseInt(t.Substring(12).Trim(), "TotalPoints");
                    if (v < 1 || v > 100) throw new FormatException("TotalPoints must be between 1 and 100.");
                    vm.TotalPointsMax = v;
                }
                else if (t.StartsWith("Duration:", StringComparison.OrdinalIgnoreCase)) vm.DurationMinutes = ParseInt(t.Substring(9).Trim(), "Duration");
                else if (t.StartsWith("MaxAttempts:", StringComparison.OrdinalIgnoreCase)) vm.MaxAttempts = ParseInt(t.Substring(12).Trim(), "MaxAttempts");
                else if (t.StartsWith("OpenAt:", StringComparison.OrdinalIgnoreCase)) vm.OpenAt = ParseDate(t.Substring(7).Trim(), "OpenAt");
                else if (t.StartsWith("CloseAt:", StringComparison.OrdinalIgnoreCase)) vm.CloseAt = ParseDate(t.Substring(8).Trim(), "CloseAt");
                else if (isBlank(t)) { /* skip */ }
            }

            if (string.IsNullOrWhiteSpace(vm.Title))
                throw new FormatException("Missing Title.");

            // ======= QUESTIONS =======
            while (i < lines.Length)
            {
                while (i < lines.Length && isBlank(peek())) read();
                if (i >= lines.Length) break;

                var header = read();
                if (!header.TrimStart().StartsWith("Q:", StringComparison.OrdinalIgnoreCase))
                    throw new FormatException($"Expect 'Q:' at line {i}.");

                var q = new CreateQuestionVM
                {
                    Type = QuestionType.Mcq,
                    Points = 1m,
                    Prompt = header.Substring(header.IndexOf(':') + 1).Trim(),
                    Choices = new List<CreateChoiceVM>()
                };

                while (i < lines.Length && !isBlank(peek()) &&
                       !peek().TrimStart().StartsWith("Q:", StringComparison.OrdinalIgnoreCase))
                {
                    var l = read().Trim();

                    if (l.StartsWith("Type:", StringComparison.OrdinalIgnoreCase))
                    {
                        var v = l.Substring(5).Trim().ToLowerInvariant();
                        q.Type = v.StartsWith("mcq") ? QuestionType.Mcq : QuestionType.Essay;
                    }
                    else if (l.StartsWith("Points:", StringComparison.OrdinalIgnoreCase))
                    {
                        q.Points = ParseDecimal(l.Substring(7).Trim(), "Points");
                        if (q.Points < 0 || (q.Points % 0.5m != 0))
                            throw new FormatException("Each question's Points must be non-negative and multiple of 0.5.");
                    }
                    else if (l.StartsWith("Choices:", StringComparison.OrdinalIgnoreCase))
                    {

                        while (i < lines.Length)
                        {
                            var rawLine = peek();
                            var cLine = rawLine.Trim();
                            if (isBlank(cLine)) break;
                            if (cLine.StartsWith("Q:", StringComparison.OrdinalIgnoreCase)) break;
                            if (cLine.StartsWith("Type:", StringComparison.OrdinalIgnoreCase)) break;
                            if (cLine.StartsWith("Points:", StringComparison.OrdinalIgnoreCase)) break;
                            if (cLine.StartsWith("Answer:", StringComparison.OrdinalIgnoreCase)) break;

                            bool looksChoice = cLine.StartsWith("-", StringComparison.Ordinal)
                                            || (cLine.Length >= 2 && cLine[1] == ')' && (char.IsLetter(cLine[0]) || char.IsDigit(cLine[0])));

                            if (!looksChoice) break;

                            read();
                            string text = cLine;

                            if (text.StartsWith("-", StringComparison.Ordinal))
                                text = text.Substring(1).Trim();
                            else if (text.Length >= 2 && text[1] == ')' && (char.IsLetter(text[0]) || char.IsDigit(text[0])))
                                text = text.Substring(2).Trim();

                            if (string.IsNullOrWhiteSpace(text))
                                throw new FormatException("Choice text cannot be empty.");

                            q.Choices!.Add(new CreateChoiceVM { Text = text });
                        }
                    }
                    else if (l.StartsWith("Answer:", StringComparison.OrdinalIgnoreCase))
                    {
                        var v = l.Substring(7).Trim();

                        int idx = -1;
                        if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var num))
                        {
                            idx = Math.Max(1, num) - 1;
                        }
                        else
                        {
                            var letter = v.ToUpperInvariant();
                            idx = Array.IndexOf(TrueLetters, letter);
                        }
                        q.CorrectIndex = idx;
                    }
                }

                if (string.IsNullOrWhiteSpace(q.Prompt))
                    throw new FormatException("A question is missing its prompt.");

                if (q.Type == QuestionType.Mcq)
                {
                    if (q.Choices == null || q.Choices.Count < 2)
                        throw new FormatException("MCQ must have at least 2 choices.");
                    if (q.CorrectIndex < 0 || q.CorrectIndex >= q.Choices.Count)
                        throw new FormatException("MCQ Answer index is invalid.");
                }
                else
                {
                    q.Choices = new List<CreateChoiceVM>();
                    q.CorrectIndex = 0;
                }

                vm.Questions.Add(q);

                while (i < lines.Length && isBlank(peek())) read();
            }

            // Suy luận overall type
            var hasMcq = vm.Questions.Any(z => z.Type == QuestionType.Mcq);
            var hasEssay = vm.Questions.Any(z => z.Type == QuestionType.Essay);
            vm.Type = hasMcq && hasEssay ? AssignmentType.Mixed
                      : hasMcq ? AssignmentType.Mcq
                      : AssignmentType.Essay;


            var total = vm.Questions.Sum(z => z.Points);
            if (total != vm.TotalPointsMax)
                throw new FormatException($"Total points must be {vm.TotalPointsMax}. Current total: {total:0.##}");

            return vm;

            // Local helpers
            static int ParseInt(string s, string field)
            {
                if (!int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                    throw new FormatException($"Invalid {field}.");
                return v;
            }
            static decimal ParseDecimal(string s, string field)
            {
                if (!decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    throw new FormatException($"Invalid {field}.");
                return v;
            }
            static DateTimeOffset? ParseDate(string s, string field)
            {
                if (string.IsNullOrWhiteSpace(s)) return null;
                if (DateTime.TryParseExact(s.Trim(),
                    new[] { "dd/MM/yyyy HH:mm", "dd/MM/yyyy" },
                    CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
                    return new DateTimeOffset(dt);
                throw new FormatException($"Invalid {field} format. Use dd/MM/yyyy HH:mm.");
            }
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
            // 1) Thời gian
            if (vm.OpenAt.HasValue && vm.CloseAt.HasValue && vm.CloseAt <= vm.OpenAt)
                ModelState.AddModelError(nameof(vm.CloseAt), "Due date must be after Open date.");

            // 2) Thang điểm tối đa (1..100)
            if (vm.TotalPointsMax < 1 || vm.TotalPointsMax > 100)
                ModelState.AddModelError(nameof(vm.TotalPointsMax), "Total points (max) must be between 1 and 100.");

            // 3) Ít nhất 1 câu hỏi
            if (vm.Questions == null || vm.Questions.Count == 0)
                ModelState.AddModelError(string.Empty, "At least one question is required.");

            // 4) Validate từng câu
            decimal total = 0m;
            for (int i = 0; i < (vm.Questions?.Count ?? 0); i++)
            {
                var q = vm.Questions![i];

                // Prompt
                if (string.IsNullOrWhiteSpace(q.Prompt))
                    ModelState.AddModelError($"Questions[{i}].Prompt", "Prompt is required.");

                // Points: không âm & bội số 0.5
                if (q.Points < 0 || (q.Points % 0.5m != 0))
                    ModelState.AddModelError($"Questions[{i}].Points", "Points must be a multiple of 0.5 and not negative.");

                // MCQ: tối thiểu 2 lựa chọn + chỉ mục đáp án hợp lệ
                if (q.Type == QuestionType.Mcq)
                {
                    if (q.Choices == null || q.Choices.Count < 2)
                        ModelState.AddModelError($"Questions[{i}].Choices", "MCQ must have at least 2 choices.");

                    if (q.CorrectIndex < 0 || q.CorrectIndex >= (q.Choices?.Count ?? 0))
                        ModelState.AddModelError($"Questions[{i}].CorrectIndex", "Select a valid correct answer.");
                }

                total += q.Points;
            }

            // 5) Tổng điểm phải KHỚP với TotalPointsMax
            const decimal EPS = 0.0000001m;
            if (vm.Questions != null && vm.Questions.Count > 0)
            {
                if (Math.Abs(total - vm.TotalPointsMax) > EPS)
                    ModelState.AddModelError(string.Empty,
                        $"Total points must equal {vm.TotalPointsMax}. Current total: {total:0.##}.");
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
