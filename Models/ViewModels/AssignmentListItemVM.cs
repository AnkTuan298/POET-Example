using System;
using POETWeb.Models.Enums;

namespace POETWeb.Models.ViewModels
{
    public class AssignmentListItemVM
    {
        public int Id { get; set; }
        public int ClassId { get; set; }
        public string ClassName { get; set; } = "";
        public string Title { get; set; } = "";
        public DateTimeOffset? DueAt { get; set; }
        public string Status { get; set; } = "Not Started";
        public int MaxAttempts { get; set; }
        public AssignmentType Type { get; set; }
        public string? Description { get; set; }
        public int DurationMinutes { get; set; }

    }

    public class AssignmentStudentVM
    {
        public int? ClassId { get; set; }
        public string? ClassName { get; set; }
        public System.Collections.Generic.List<AssignmentListItemVM> Items { get; set; }
            = new System.Collections.Generic.List<AssignmentListItemVM>();
    }
}
