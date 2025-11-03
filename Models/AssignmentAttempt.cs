using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using AttemptStatus = POETWeb.Models.Enums.AttemptStatus;

namespace POETWeb.Models
{
    public class AssignmentAttempt
    {
        public int Id { get; set; }

        public int AssignmentId { get; set; }
        public Assignment Assignment { get; set; } = null!;

        // Học sinh
        public string UserId { get; set; } = "";
        public ApplicationUser User { get; set; } = null!;

        // Lần thứ mấy (1..MaxAttempts)
        public int AttemptNumber { get; set; } = 1;

        // Snapshot thời lượng để tính deadline của attempt
        public int DurationMinutes { get; set; } = 30;

        public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? SubmittedAt { get; set; }

        public AttemptStatus Status { get; set; } = AttemptStatus.InProgress;

        // Có essay hay không (để biết cần chấm tay)
        public bool RequiresManualGrading { get; set; }

        // Tổng điểm tối đa của đề tại thời điểm bắt đầu attempt (snapshot) - decimal
        public decimal MaxScore { get; set; }

        // Điểm tự chấm (MCQ) và điểm cuối (sau khi chấm essay) - decimal
        public decimal? AutoScore { get; set; }
        public decimal? FinalScore { get; set; }

        [MaxLength(8000)]
        public string? TeacherComment { get; set; }

        public List<AssignmentAnswer> Answers { get; set; } = new();
    }

    public class AssignmentAnswer
    {
        public int Id { get; set; }

        public int AttemptId { get; set; }
        public AssignmentAttempt Attempt { get; set; } = null!;

        public int QuestionId { get; set; }
        public AssignmentQuestion Question { get; set; } = null!;

        // MCQ: choice đã chọn
        public int? SelectedChoiceId { get; set; }
        public AssignmentChoice? SelectedChoice { get; set; }

        // Essay: nội dung trả lời
        [MaxLength(8000)]
        public string? TextAnswer { get; set; }

        // Kết quả chấm
        public bool? IsCorrect { get; set; }           // chỉ dùng cho MCQ
        public decimal? PointsAwarded { get; set; }    // MCQ: auto; Essay: chấm tay

        [MaxLength(8000)]
        public string? TeacherComment { get; set; }
    }
}
