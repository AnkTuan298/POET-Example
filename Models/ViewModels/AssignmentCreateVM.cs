using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using POETWeb.Models.Enums;

namespace POETWeb.Models.ViewModels
{
    public class AssignmentCreateVM
    {
        [Required] public int ClassId { get; set; }

        [Required, MaxLength(160)]
        public string Title { get; set; } = "";

        [MaxLength(400)]
        public string? Description { get; set; }

        public AssignmentType Type { get; set; } = AssignmentType.Mcq;

        [Range(1, 600)]
        public int DurationMinutes { get; set; } = 30;

        [Range(1, 20)]
        public int MaxAttempts { get; set; } = 1;

        // Thời điểm mở và hạn nộp
        public DateTimeOffset? OpenAt { get; set; }
        public DateTimeOffset? CloseAt { get; set; }

        // Danh sách câu hỏi
        public List<CreateQuestionVM> Questions { get; set; } = new();

        // --- Commands cho postback ---
        // Op: add-q | remove-q | add-choice | remove-choice | (null/empty => Save)
        public string? Op { get; set; }

        // Chỉ mục câu hỏi tác động (cho add/remove choice, remove question)
        public int? QIndex { get; set; }

        // Chỉ mục đáp án tác động (khi remove-choice)
        public int? ChoiceIndex { get; set; }
    }

    public class CreateQuestionVM
    {
        public QuestionType Type { get; set; } = QuestionType.Mcq;

        [Required, MaxLength(1000)]
        public string Prompt { get; set; } = "";

        [Range(0, 100)]
        public double Points { get; set; } = 1.0;

        // Chỉ dùng khi Type = MCQ
        public List<CreateChoiceVM> Choices { get; set; } = new()
        {
            new(), new(), new(), new()
        };

        // Index đáp án đúng trong mảng Choices
        public int CorrectIndex { get; set; } = 0;
    }

    public class CreateChoiceVM
    {
        public string? Text { get; set; } = "";
    }

}
