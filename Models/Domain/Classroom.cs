using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using POETWeb.Models;

namespace POETWeb.Models.Domain
{
    public class Classroom
    {
        public int Id { get; set; }

        [Required, StringLength(80)]
        public string Name { get; set; } = string.Empty;

        [Required, StringLength(6)]
        [RegularExpression("^[A-Z0-9]{6}$", ErrorMessage = "Class code must be 6 uppercase letters/digits.")]
        public string ClassCode { get; set; } = string.Empty;

        [StringLength(60)]
        public string? Subject { get; set; }

        [Required]
        public string TeacherId { get; set; } = string.Empty;


        [ForeignKey(nameof(TeacherId))]
        public ApplicationUser? Teacher { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;


        public ICollection<Enrollment>? Enrollments { get; set; }
    }
}
