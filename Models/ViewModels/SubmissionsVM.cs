using System;
using System.Collections.Generic;

namespace POETWeb.Models.ViewModels
{
    public class SubmissionListItemVM
    {
        public int AttemptId { get; set; }
        public int AttemptNumber { get; set; }
        public string StudentId { get; set; } = "";
        public string StudentName { get; set; } = "";
        public string StudentEmail { get; set; } = "";
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public string Status { get; set; } = "InProgress";

        public decimal McqScore { get; set; }
        public decimal McqMax { get; set; }
        public decimal? EssayScore { get; set; }
        public decimal EssayMax { get; set; }
        public decimal? FinalScore { get; set; }
        public decimal FinalMax { get; set; }

        public bool RequiresManual { get; set; }
    }

    public class SubmissionsVM
    {
        public int ClassId { get; set; }
        public int AssignmentId { get; set; }
        public string AssignmentTitle { get; set; } = "";
        public List<SubmissionListItemVM> Items { get; set; } = new();
    }

    public class GradeEssayItemVM
    {
        public int QuestionId { get; set; }
        public string Prompt { get; set; } = "";
        public decimal MaxPoints { get; set; }
        public string? StudentAnswer { get; set; }
        public decimal? Score { get; set; }
        public string? Comment { get; set; }
    }

    public class GradeAttemptVM
    {
        public int AssignmentId { get; set; }
        public string AssignmentTitle { get; set; } = "";
        public int AttemptId { get; set; }
        public int AttemptNumber { get; set; }
        public string StudentId { get; set; } = "";
        public string StudentName { get; set; } = "";
        public string StudentEmail { get; set; } = "";

        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public string Status { get; set; } = "Submitted";

        public decimal McqScore { get; set; }
        public decimal McqMax { get; set; }
        public decimal EssayMax { get; set; }
        public decimal? CurrentEssayScore { get; set; }
        public decimal FinalMax { get; set; }
        public decimal? CurrentFinalScore { get; set; }

        public List<GradeEssayItemVM> Essays { get; set; } = new();

        // overall comment
        public string? TeacherComment { get; set; }
    }
}
