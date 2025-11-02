using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using POETWeb.Data;
using POETWeb.Models;
using POETWeb.Models.Enums;

namespace POETWeb.Services
{
    public class GradingService
    {
        private readonly ApplicationDbContext _db;
        public GradingService(ApplicationDbContext db) { _db = db; }

        // Chấm tự động toàn bài MCQ, trả về điểm tổng
        public async Task<double> AutoGradeMcqAsync(int attemptId)
        {
            var attempt = await _db.AssignmentAttempts
                .Include(a => a.Assignment).ThenInclude(x => x.Questions).ThenInclude(q => q.Choices)
                .Include(a => a.Answers).ThenInclude(ans => ans.SelectedChoice)
                .FirstAsync(a => a.Id == attemptId);

            if (attempt.Assignment.Type != AssignmentType.Mcq) return attempt.Score ?? 0;

            double total = 0;
            foreach (var q in attempt.Assignment.Questions.Where(q => q.Type == QuestionType.Mcq))
            {
                var ans = attempt.Answers.FirstOrDefault(x => x.QuestionId == q.Id);
                if (ans?.SelectedChoiceId != null)
                {
                    bool correct = q.Choices.Any(c => c.Id == ans.SelectedChoiceId && c.IsCorrect);
                    ans.AutoScore = correct ? q.Points : 0;
                    total += ans.AutoScore.Value;
                }
                else
                {
                    ans ??= new AssignmentAnswer { AttemptId = attempt.Id, QuestionId = q.Id, AutoScore = 0 };
                    _db.AssignmentAnswers.Add(ans);
                }
            }

            attempt.Score = total;
            attempt.Status = AttemptStatus.Submitted; // hoặc Graded nếu không có essay
            await _db.SaveChangesAsync();
            return total;
        }
    }
}
