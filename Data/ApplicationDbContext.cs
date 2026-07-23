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

        builder.Entity<StudentEnrollment>()
            .HasOne(se => se.Student)
            .WithMany()
            .HasForeignKey(se => se.StudentId)
            .OnDelete(DeleteBehavior.Restrict);

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
