using System.Collections.Generic;

namespace POETWeb.Models.ViewModels
{
    public class TeacherDashboardVM
    {
        // Stats
        public int ActiveClasses { get; set; }
        public int TotalStudents { get; set; }
        public int Assignments { get; set; }
        public int PendingGrades { get; set; }

        // Data
        public List<ClassCardVM> Classes { get; set; } = new();
        public List<RecentActivityVM> Recent { get; set; } = new();
        public string FirstName { get; set; } = "Teacher";
    }

    public class ClassCardVM
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public int Students { get; set; }
        public string? Subject { get; set; }
    }

    public enum ActivityKind
    {
        JoinedClass = 1,
        SubmittedAssignment = 2
    }

    public class RecentActivityVM
    {
        public ActivityKind Kind { get; set; }
        public string StudentName { get; set; } = "";
        public string ClassTitle { get; set; } = "";
        public string? AssignmentTitle { get; set; }
        public DateTimeOffset When { get; set; }
        public string TimeAgo { get; set; } = "";
    }
}
