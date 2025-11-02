using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using POETWeb.Models.Enums;

namespace POETWeb.Models
{
    public class AssignmentAttempt
    {
        public int Id { get; set; }
        public int AssignmentId { get; set; }
        public Assignment Assignment { get; set; } = null!;

        public string UserId { get; set; } = "";
        public ApplicationUser User { get; set; } = null!;

        public int AttemptNumber { get; set; } = 1;

        public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? SubmittedAt { get; set; }

        public AttemptStatus Status { get; set; } = AttemptStatus.InProgress;

        public double? Score { get; set; }

        public List<AssignmentAnswer> Answers { get; set; } = new();
    }

    public class AssignmentAnswer
    {
        public int Id { get; set; }
        public int AttemptId { get; set; }
        public AssignmentAttempt Attempt { get; set; } = null!;

        public int QuestionId { get; set; }
        public AssignmentQuestion Question { get; set; } = null!;

        // cho MCQ: lưu choice đã chọn
        public int? SelectedChoiceId { get; set; }
        public AssignmentChoice? SelectedChoice { get; set; }

        // cho Essay: nội dung trả lời
        [MaxLength(8000)]
        public string? TextAnswer { get; set; }

        // điểm auto (MCQ) và điểm chấm tay (Essay)
        public double? AutoScore { get; set; }
        public double? ManualScore { get; set; }
    }
}
