using System;
using System.Collections.Generic;
using POETWeb.Models.Enums;
using POETWeb.Models.ViewModels;
using POETWeb.Models;


namespace POETWeb.Models.ViewModels
{
    public class TakeAttemptVM
    {
        public int AssignmentId { get; set; }
        public int AttemptId { get; set; }
        public string Title { get; set; } = "";
        public string ClassName { get; set; } = "";
        public int DurationMinutes { get; set; }
        public DateTimeOffset StartedAt { get; set; }
        public DateTimeOffset? DueAt { get; set; }    // StartedAt + Duration
        public int CurrentIndex { get; set; }         // 0-based
        public List<TakeQuestionVM> Questions { get; set; } = new();
        public int AnsweredCount { get; set; }
    }

    public class TakeQuestionVM
    {
        public int QuestionId { get; set; }
        public int Index { get; set; }
        public string Prompt { get; set; } = "";
        public double Points { get; set; }
        public QuestionType Type { get; set; }
        // MCQ
        public int? SelectedChoiceId { get; set; }
        public List<TakeChoiceVM> Choices { get; set; } = new();
        // Essay
        public string? TextAnswer { get; set; }
        // UI
        public bool IsAnswered { get; set; }
        public bool Marked { get; set; }
    }

    public class TakeChoiceVM
    {
        public int ChoiceId { get; set; }
        public string Text { get; set; } = "";
    }

    public class SaveAnswerDto
    {
        public int AttemptId { get; set; }
        public int QuestionId { get; set; }
        public int? SelectedChoiceId { get; set; }
        public string? TextAnswer { get; set; }
        public bool? Marked { get; set; }
    }
}
