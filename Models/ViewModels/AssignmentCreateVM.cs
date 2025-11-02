using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using POETWeb.Models.Enums;

namespace POETWeb.Models.ViewModels
{
    public class AssignmentCreateVM
    {
        [Required] public int ClassId { get; set; }
        [Required, MaxLength(160)] public string Title { get; set; } = "";
        [MaxLength(400)] public string? Description { get; set; }

        [Required] public AssignmentType Type { get; set; } = AssignmentType.Mcq;

        [Range(1, 600)] public int DurationMinutes { get; set; } = 30;
        [Range(1, 20)] public int MaxAttempts { get; set; } = 1;

        public DateTimeOffset? OpenAt { get; set; }
        public DateTimeOffset? CloseAt { get; set; }

        public List<CreateQuestionVM> Questions { get; set; } = new();

        // postback ops (add/remove)
        public string? Op { get; set; }
        public int? QIndex { get; set; }
        public int? ChoiceIndex { get; set; }
    }

    public class CreateQuestionVM
    {
        public QuestionType Type { get; set; } = QuestionType.Mcq;

        [Required, MaxLength(1000)]
        public string Prompt { get; set; } = "";

        [Range(typeof(decimal), "0", "1000000")]
        public decimal Points { get; set; } = 1m;

        public List<CreateChoiceVM> Choices { get; set; } = new()
        {
            new(), new(), new(), new()
        };
        public int CorrectIndex { get; set; } = 0;
    }

    public class CreateChoiceVM
    {
        [MaxLength(400)]
        public string Text { get; set; } = "";
    }
}
