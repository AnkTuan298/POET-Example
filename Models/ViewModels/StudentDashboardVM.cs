namespace POETWeb.Models.ViewModels
{
    public class StudentClassCardVM
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string? Subject { get; set; }
        public string TeacherName { get; set; } = "Teacher";
        public string? TeacherAvatar { get; set; }
        public int Students { get; set; }
    }

    public class StudentDashboardVM
    {
        public string FirstName { get; set; } = "Student";
        public int JoinedClasses { get; set; }
        public int AssignmentsDue { get; set; }
        public int MaterialsPosted { get; set; }
        public int HoursLearned { get; set; }
        public List<StudentClassCardVM> Classes { get; set; } = new();
    }

    public class ClassQuickViewVM
    {
        public int Id { get; set; }
        public string? Title { get; set; }
        public string? Subject { get; set; }
        public string? ClassCode { get; set; }
        public string? TeacherName { get; set; }
        public string? TeacherAvatar { get; set; }
    }

    public class RosterStudentVM
    {
        public int ClassId { get; set; }
        public string ClassName { get; set; } = "";
        public string? ClassCode { get; set; }
        public string? Subject { get; set; }
        public string TeacherName { get; set; } = "";
        public string? TeacherAvatar { get; set; }
        public List<RosterStudentItemVM> Students { get; set; } = new();
        public string? ReturnUrl { get; set; }
    }

    public class RosterStudentItemVM
    {
        public string FullName { get; set; } = "";
        public string? AvatarUrl { get; set; }
        public DateTime? JoinedAt { get; set; }
    }

}
