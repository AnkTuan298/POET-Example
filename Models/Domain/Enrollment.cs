using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace POETWeb.Models.Domain
{
    public class Enrollment
    {
        public int Id { get; set; }

        [Required]
        public int ClassId { get; set; }              

        [ForeignKey(nameof(ClassId))]
        public Classroom? Classroom { get; set; }    

        [Required]
        public string UserId { get; set; } = string.Empty;

        [StringLength(30)]
        public string RoleInClass { get; set; } = "Student";

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    }
}
