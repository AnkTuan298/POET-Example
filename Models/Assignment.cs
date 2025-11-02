using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using POETWeb.Models.Domain;
using POETWeb.Models.Enums;

namespace POETWeb.Models
{
    public class Assignment
    {
        public int Id { get; set; }

        [Required, MaxLength(160)]
        public string Title { get; set; } = "";

        [MaxLength(400)]
        public string? Description { get; set; }

        public AssignmentType Type { get; set; }

        // ràng buộc theo đề bài
        [Range(1, 600)] public int DurationMinutes { get; set; } = 30; // thời gian làm bài
        [Range(1, 20)] public int MaxAttempts { get; set; } = 1;   // số lần được thi

        // thuộc lớp nào
        public int ClassId { get; set; }
        public Classroom Class { get; set; } = null!;

        // thời điểm mở/đóng
        public DateTimeOffset? OpenAt { get; set; }
        public DateTimeOffset? CloseAt { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
        public string CreatedById { get; set; } = "";
        public ApplicationUser CreatedBy { get; set; } = null!;

        public List<AssignmentQuestion> Questions { get; set; } = new();
    }

    public class AssignmentQuestion
    {
        public int Id { get; set; }
        public int AssignmentId { get; set; }
        public Assignment Assignment { get; set; } = null!;

        public QuestionType Type { get; set; }  // Mcq hoặc Essay
        [Required, MaxLength(1000)]
        public string Prompt { get; set; } = "";

        // điểm tối đa của câu
        [Range(0, 100)] public double Points { get; set; } = 1.0;

        // thứ tự hiển thị
        public int Order { get; set; } = 0;

        // cho trắc nghiệm: danh sách lựa chọn
        public List<AssignmentChoice> Choices { get; set; } = new();
    }

    public class AssignmentChoice
    {
        public int Id { get; set; }
        public int QuestionId { get; set; }
        public AssignmentQuestion Question { get; set; } = null!;

        [Required, MaxLength(400)]
        public string Text { get; set; } = "";

        public bool IsCorrect { get; set; }
        public int Order { get; set; } = 0;
    }
}
