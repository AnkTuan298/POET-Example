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

        }

    }
}
