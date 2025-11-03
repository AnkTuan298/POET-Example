using System;
using System.Collections.Generic;

namespace POETWeb.Models.ViewModels
{
    public class TestAttemptListItemVM
    {
        public int AttemptId { get; set; }
        public int AttemptNumber { get; set; }

        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public int DurationMinutes { get; set; }

        // ===== Legacy fields =====
        public int CorrectCount { get; set; }
        public int TotalQuestions { get; set; }
        public decimal Score { get; set; }        // = McqScore
        public decimal MaxScore { get; set; }     // = McqMax

        public string Status { get; set; } = "InProgress";
        public bool RequiresManual { get; set; }
        // MCQ
        public int McqCorrect { get; set; }
        public int McqTotal { get; set; }
        public decimal McqScore { get; set; }
        public decimal McqMax { get; set; }

        // Essay
        public decimal? EssayScore { get; set; }  // null = Pending
        public decimal EssayMax { get; set; }

        // Final = MCQ + Essay 
        public decimal? FinalScore { get; set; }
        public decimal FinalMax { get; set; }
    }

    public class TestHistoryVM
    {
        public int AssignmentId { get; set; }
        public string AssignmentTitle { get; set; } = "";
        public List<TestAttemptListItemVM> Attempts { get; set; } = new();
        public int MaxAttempts { get; set; }
    }
}
