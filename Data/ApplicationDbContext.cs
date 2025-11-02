using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using POETWeb.Models;
using POETWeb.Models.Domain;

namespace POETWeb.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<Classroom> Classrooms => Set<Classroom>();
        public DbSet<Enrollment> Enrollments => Set<Enrollment>();
        public DbSet<Material> Materials { get; set; } = default!;
        public DbSet<Assignment> Assignments => Set<Assignment>();
        public DbSet<AssignmentQuestion> AssignmentQuestions => Set<AssignmentQuestion>();
        public DbSet<AssignmentChoice> AssignmentChoices => Set<AssignmentChoice>();
        public DbSet<AssignmentAttempt> AssignmentAttempts => Set<AssignmentAttempt>();
        public DbSet<AssignmentAnswer> AssignmentAnswers => Set<AssignmentAnswer>();


        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            builder.Entity<ApplicationUser>(b =>
            {
                b.Property(u => u.AccountCode).HasMaxLength(8);
                b.HasIndex(u => u.AccountCode).IsUnique();
            });

            builder.Entity<Classroom>(b =>
            {
                b.Property(c => c.Name).HasMaxLength(80).IsRequired();
                b.Property(c => c.Subject).HasMaxLength(60);
                b.Property(c => c.ClassCode).HasMaxLength(6).IsRequired();
                b.HasIndex(c => c.ClassCode).IsUnique();
            });

            // Enrollment: 1 user chỉ có 1 dòng trong 1 lớp
            builder.Entity<Enrollment>(b =>
            {
                b.HasKey(e => e.Id);
                b.HasOne(e => e.Classroom)
                 .WithMany(c => c.Enrollments)
                 .HasForeignKey(e => e.ClassId)
                 .OnDelete(DeleteBehavior.Cascade);

                b.HasIndex(e => new { e.ClassId, e.UserId }).IsUnique();
                b.Property(e => e.RoleInClass).HasMaxLength(30);
            });

            // ================== ASSIGNMENT BLOCK ==================
            builder.Entity<Assignment>(b =>
            {
                b.Property(x => x.Title).HasMaxLength(160).IsRequired();
                b.Property(x => x.Description).HasMaxLength(400);
                b.Property(x => x.DurationMinutes).HasDefaultValue(30);
                b.Property(x => x.MaxAttempts).HasDefaultValue(1);

                // Assignment thuộc về một Classroom, xóa lớp sẽ xóa tất cả assignment
                b.HasOne(x => x.Class)
                 .WithMany() // không cần nav property trên Classroom
                 .HasForeignKey(x => x.ClassId)
                 .OnDelete(DeleteBehavior.Cascade);

                // Người tạo (giáo viên) — không cho cascade delete user
                b.HasOne(x => x.CreatedBy)
                 .WithMany()
                 .HasForeignKey(x => x.CreatedById)
                 .OnDelete(DeleteBehavior.Restrict);

                b.HasIndex(x => new { x.ClassId, x.Title }); // phụ: để tìm kiếm trong lớp
            });

            builder.Entity<AssignmentQuestion>(b =>
            {
                b.Property(x => x.Prompt).HasMaxLength(1000).IsRequired();
                b.Property(x => x.Points).HasPrecision(6, 2);
                b.HasOne(x => x.Assignment)
                 .WithMany(a => a.Questions)
                 .HasForeignKey(x => x.AssignmentId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<AssignmentChoice>(b =>
            {
                b.Property(x => x.Text).HasMaxLength(400).IsRequired();
                b.HasOne(x => x.Question)
                 .WithMany(q => q.Choices)
                 .HasForeignKey(x => x.QuestionId)
                 .OnDelete(DeleteBehavior.Cascade);
            });

            builder.Entity<AssignmentAttempt>(b =>
            {
                b.Property(x => x.Score).HasPrecision(8, 2);

                b.HasOne(x => x.Assignment)
                 .WithMany()
                 .HasForeignKey(x => x.AssignmentId)
                 .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.User)
                 .WithMany()
                 .HasForeignKey(x => x.UserId)
                 .OnDelete(DeleteBehavior.Restrict);

                // mỗi user chỉ có 1 attemptNumber cho 1 assignment
                b.HasIndex(x => new { x.AssignmentId, x.UserId, x.AttemptNumber })
                 .IsUnique();
            });

            builder.Entity<AssignmentAnswer>(b =>
            {
                b.Property(x => x.TextAnswer).HasMaxLength(8000);
                b.Property(x => x.AutoScore).HasPrecision(6, 2);
                b.Property(x => x.ManualScore).HasPrecision(6, 2);

                b.HasOne(x => x.Attempt)
                 .WithMany(a => a.Answers)
                 .HasForeignKey(x => x.AttemptId)
                 .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.Question)
                 .WithMany()
                 .HasForeignKey(x => x.QuestionId)
                 .OnDelete(DeleteBehavior.Restrict); // giữ câu hỏi khi xóa answer

                // cho MCQ: SelectedChoice (tùy chọn), không cascade
                b.HasOne(x => x.SelectedChoice)
                 .WithMany()
                 .HasForeignKey(x => x.SelectedChoiceId)
                 .OnDelete(DeleteBehavior.Restrict);
            });
            // ================== END ASSIGNMENT BLOCK ==================

        }

    }
}
