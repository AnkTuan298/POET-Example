using System;
using System.Collections.Generic;
using POETWeb.Models.Enums;

namespace POETWeb.Models.ViewModels
{
    public class AttemptReviewVM
    {
        public int AttemptId { get; set; }
        public int AssignmentId { get; set; }
        public string AssignmentTitle { get; set; } = "";
        public DateTimeOffset? OpenAt { get; set; }
        public DateTimeOffset? CloseAt { get; set; }
        public bool IsClosed { get; set; }

        public decimal Score { get; set; }
        public decimal TotalMax { get; set; }

        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? SubmittedAt { get; set; }
        public TimeSpan? Duration { get; set; }

        public List<QuestionReviewItem> Questions { get; set; } = new();
    }

    public class QuestionReviewItem
    {
        public int Index { get; set; }
        public QuestionType Type { get; set; }
        public string Prompt { get; set; } = "";
        public decimal Points { get; set; }

        public int? ChosenChoiceId { get; set; }
        public int? CorrectChoiceId { get; set; }
        public List<McqChoiceVM>? Choices { get; set; }

        public string? EssayText { get; set; }
        public decimal? EssayScore { get; set; }
        public string? TeacherComment { get; set; }
    }

    public class McqChoiceVM
    {
        public int ChoiceId { get; set; }
        public string Text { get; set; } = "";
        public bool IsChosen { get; set; }
        public bool IsCorrect { get; set; }
    }
}
