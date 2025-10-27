using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace POETWeb.Models.Domain
{
    public class Material
    {
        public int Id { get; set; }

        [Required]
        public int ClassId { get; set; }

        [ForeignKey(nameof(ClassId))]
        public Classroom? Classroom { get; set; }

        [Required, StringLength(200)]
        public string Title { get; set; } = default!;

        [StringLength(2000)]
        public string? Description { get; set; }

        // Nếu upload file
        [StringLength(500)]
        public string? FileUrl { get; set; }
        [StringLength(255)]
        public string? OriginalFileName { get; set; }
        public long? FileSizeBytes { get; set; }

        // Nếu dùng link ngoài (YouTube, link thường…)
        [StringLength(1000)]
        public string? ExternalUrl { get; set; }
        [StringLength(50)]
        public string? Provider { get; set; }   // "YouTube", "Link"
        [StringLength(20)]
        public string? MediaKind { get; set; }  // "file", "video", "link"
        [StringLength(1000)]
        public string? ThumbnailUrl { get; set; }

        // phân loại “Slide”, “Video”, “Doc”, “Link”…
        [StringLength(50)]
        public string? Category { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        [StringLength(450)]
        public string? CreatedById { get; set; }
    }
}
