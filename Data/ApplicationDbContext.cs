using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using FacultyEvalSystem.Models;

namespace FacultyEvalSystem.Data;

public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

    public DbSet<College> Colleges => Set<College>();
    public DbSet<AcademicProgram> AcademicPrograms => Set<AcademicProgram>();
    public DbSet<Semester> Semesters => Set<Semester>();
    public DbSet<EvaluationPeriod> EvaluationPeriods => Set<EvaluationPeriod>();
    public DbSet<EvaluationCategory> EvaluationCategories => Set<EvaluationCategory>();
    public DbSet<EvaluationCriterion> EvaluationCriteria => Set<EvaluationCriterion>();
    public DbSet<FacultySubject> FacultySubjects => Set<FacultySubject>();
    public DbSet<StudentEnrollment> StudentEnrollments => Set<StudentEnrollment>();
    public DbSet<Evaluation> Evaluations => Set<Evaluation>();
    public DbSet<EvaluationResponse> EvaluationResponses => Set<EvaluationResponse>();
    public DbSet<EvaluationResult> EvaluationResults => Set<EvaluationResult>();
    public DbSet<DevelopmentPlan> DevelopmentPlans => Set<DevelopmentPlan>();
    public DbSet<StudentCourse> StudentCourses => Set<StudentCourse>();
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Prevent cascade delete cycles
        builder.Entity<Evaluation>()
            .HasOne(e => e.Faculty)
            .WithMany()
            .HasForeignKey(e => e.FacultyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<Evaluation>()
            .HasOne(e => e.Evaluator)
            .WithMany()
            .HasForeignKey(e => e.EvaluatorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<EvaluationResult>()
            .HasOne(r => r.Faculty)
            .WithMany()
            .HasForeignKey(r => r.FacultyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<FacultySubject>()
            .HasOne(fs => fs.Faculty)
            .WithMany()
            .HasForeignKey(fs => fs.FacultyId)
            .OnDelete(DeleteBehavior.Restrict);

        // SUP-02: owning program of the course
        builder.Entity<FacultySubject>()
            .HasOne(fs => fs.Program)
            .WithMany()
            .HasForeignKey(fs => fs.ProgramId)
            .OnDelete(DeleteBehavior.SetNull);

        // AA-01: self-referencing designated evaluator
        builder.Entity<ApplicationUser>()
            .HasOne(u => u.DesignatedEvaluator)
            .WithMany()
            .HasForeignKey(u => u.DesignatedEvaluatorId)
            .OnDelete(DeleteBehavior.Restrict);

        // SUP-01: semesters are queried in chronological order constantly
        builder.Entity<Semester>()
            .HasIndex(s => s.SortKey);

        builder.Entity<StudentEnrollment>()
            .HasOne(se => se.Student)
            .WithMany()
            .HasForeignKey(se => se.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<DevelopmentPlan>()
            .HasOne(d => d.Faculty)
            .WithMany()
            .HasForeignKey(d => d.FacultyId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Entity<DevelopmentPlan>()
            .HasIndex(d => new { d.FacultyId, d.EvaluationPeriodId })
            .IsUnique();

        // Course codes are matched on their normalised key
        builder.Entity<FacultySubject>()
            .HasIndex(fs => new { fs.SemesterId, fs.CourseKey });

        // Registrar masterlist: one row per student per course per semester
        builder.Entity<StudentCourse>()
            .HasOne(sc => sc.Student)
            .WithMany()
            .HasForeignKey(sc => sc.StudentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StudentCourse>()
            .HasIndex(sc => new { sc.StudentId, sc.SemesterId, sc.CourseKey })
            .IsUnique();

        builder.Entity<StudentCourse>()
            .HasIndex(sc => new { sc.SemesterId, sc.CourseKey });

        builder.Entity<StudentCourse>()
            .Property(sc => sc.Units)
            .HasPrecision(5, 2);

        builder.Entity<Notification>()
            .HasOne(n => n.User)
            .WithMany()
            .HasForeignKey(n => n.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Notification>()
            .HasIndex(n => new { n.UserId, n.ReadAt });

        // Unique: one evaluation per student per faculty per subject per period
        builder.Entity<Evaluation>()
            .HasIndex(e => new { e.EvaluatorId, e.FacultyId, e.FacultySubjectId, e.EvaluationPeriodId })
            .IsUnique()
            .HasFilter(null);

        // Unique: one result per faculty per period
        builder.Entity<EvaluationResult>()
            .HasIndex(r => new { r.FacultyId, r.EvaluationPeriodId })
            .IsUnique();
    }
}
