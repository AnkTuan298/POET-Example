using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using POETWeb.Data;
using POETWeb.Models;
using POETWeb.Models.Enums;
using AttemptStatusEnum = POETWeb.Models.Enums.AttemptStatus;

namespace POETWeb.Services
{
    public class GradingService
    {
        private readonly ApplicationDbContext _db;
        public GradingService(ApplicationDbContext db) { _db = db; }

        public async Task<AssignmentAttempt> StartAttemptAsync(int assignmentId, string userId)
        {
            var a = await _db.Assignments
                .Include(x => x.Questions).ThenInclude(q => q.Choices)
                .FirstOrDefaultAsync(x => x.Id == assignmentId)
                ?? throw new InvalidOperationException("Assignment not found.");

            var currentCount = await _db.AssignmentAttempts
                .Where(t => t.AssignmentId == assignmentId && t.UserId == userId)
                .CountAsync();

            if (currentCount >= a.MaxAttempts)
                throw new InvalidOperationException("You have reached the maximum number of attempts.");

            var hasEssay = a.Questions.Any(q => q.Type == QuestionType.Essay);
            var maxScore = a.Questions.Sum(q => q.Points); // decimal

            var attempt = new AssignmentAttempt
            {
                AssignmentId = a.Id,
                UserId = userId,
                AttemptNumber = currentCount + 1,
                DurationMinutes = a.DurationMinutes,
                RequiresManualGrading = hasEssay,
                MaxScore = maxScore,
                StartedAt = DateTimeOffset.UtcNow,
                Status = AttemptStatusEnum.InProgress
            };

            _db.AssignmentAttempts.Add(attempt);
            await _db.SaveChangesAsync();
            return attempt;
        }

        public async Task<decimal> AutoGradeMcqAsync(int attemptId)
        {
            var attempt = await _db.AssignmentAttempts
                .Include(t => t.Assignment).ThenInclude(a => a.Questions).ThenInclude(q => q.Choices)
                .Include(t => t.Answers).ThenInclude(ans => ans.SelectedChoice)
                .FirstOrDefaultAsync(t => t.Id == attemptId)
                ?? throw new InvalidOperationException("Attempt not found.");

            decimal mcqTotal = 0m;

            var mcqQuestions = attempt.Assignment.Questions.Where(q => q.Type == QuestionType.Mcq).ToList();

            foreach (var q in mcqQuestions)
            {
                var ans = attempt.Answers.FirstOrDefault(x => x.QuestionId == q.Id);
                if (ans == null)
                {
                    ans = new AssignmentAnswer
                    {
                        AttemptId = attempt.Id,
                        QuestionId = q.Id
                    };
                    _db.AssignmentAnswers.Add(ans);
                }

                if (ans.SelectedChoiceId.HasValue)
                {
                    bool correct = q.Choices.Any(c => c.Id == ans.SelectedChoiceId && c.IsCorrect);
                    ans.IsCorrect = correct;
                    ans.PointsAwarded = correct ? q.Points : 0m;
                }
                else
                {
                    ans.IsCorrect = false;
                    ans.PointsAwarded = 0m;
                }

                mcqTotal += ans.PointsAwarded ?? 0m;
            }

            attempt.AutoScore = mcqTotal;
            await _db.SaveChangesAsync();
            return mcqTotal;
        }

        public async Task SubmitAsync(int attemptId)
        {
            var attempt = await _db.AssignmentAttempts
                .Include(t => t.Assignment).ThenInclude(a => a.Questions)
                .FirstOrDefaultAsync(t => t.Id == attemptId)
                ?? throw new InvalidOperationException("Attempt not found.");

            await AutoGradeMcqAsync(attemptId);

            attempt.SubmittedAt = DateTimeOffset.UtcNow;

            if (!attempt.RequiresManualGrading)
            {
                attempt.FinalScore = attempt.AutoScore ?? 0m;
                attempt.Status = AttemptStatusEnum.Graded;
            }
            else
            {
                attempt.Status = AttemptStatusEnum.Submitted;
            }

            await _db.SaveChangesAsync();
        }

        public async Task<decimal> RecomputeFinalAfterManualAsync(int attemptId)
        {
            var attempt = await _db.AssignmentAttempts
                .Include(t => t.Assignment).ThenInclude(a => a.Questions)
                .Include(t => t.Answers)
                .FirstOrDefaultAsync(t => t.Id == attemptId)
                ?? throw new InvalidOperationException("Attempt not found.");

            var essayQIds = attempt.Assignment.Questions
                .Where(q => q.Type == QuestionType.Essay)
                .Select(q => q.Id)
                .ToHashSet();

            decimal essayPoints = attempt.Answers
                .Where(a => essayQIds.Contains(a.QuestionId))
                .Sum(a => a.PointsAwarded ?? 0m);

            decimal auto = attempt.AutoScore ?? 0m;
            attempt.FinalScore = auto + essayPoints;
            attempt.Status = AttemptStatusEnum.Graded;

            await _db.SaveChangesAsync();
            return attempt.FinalScore ?? 0m;
        }
    }
}
