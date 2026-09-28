using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace FacultyEvalSystem.Models;

public class ApplicationUser : IdentityUser
{
    [Required, MaxLength(100)]
    public string FirstName { get; set; } = "";

    [Required, MaxLength(100)]
    public string LastName { get; set; } = "";

    // From the Registrar's masterlist. Part of the Name + Program key that
    // identifies a student on re-import.
    [MaxLength(100)]
    public string? MiddleName { get; set; }

    [MaxLength(20)]
    public string? Suffix { get; set; }

    [MaxLength(20)]
    public string? EmployeeNumber { get; set; }

    [MaxLength(20)]
    public string? StudentNumber { get; set; }

    public int? CollegeId { get; set; }
    public College? College { get; set; }

    public int? ProgramId { get; set; }
    public AcademicProgram? Program { get; set; }

    public bool IsActive { get; set; } = true;

    // Accounts created by an import start with the system's default password,
    // which the user must change on first login.
    public bool MustChangePassword { get; set; }

    // AA-01: the supervisor designated to evaluate this user. Meaningful for
    // Program Chairs (normally their Dean). A Program Chair with no designated
    // evaluator is handled by the AA account instead.
    public string? DesignatedEvaluatorId { get; set; }
    public ApplicationUser? DesignatedEvaluator { get; set; }

    public string FullName => string.IsNullOrWhiteSpace(Suffix)
        ? $"{FirstName} {LastName}"
        : $"{FirstName} {LastName} {Suffix}";

    /// <summary>"Last, First M." — used wherever people are listed alphabetically.</summary>
    public string SortName => string.IsNullOrWhiteSpace(MiddleName)
        ? $"{LastName}, {FirstName}"
        : $"{LastName}, {FirstName} {MiddleName[0]}.";
}
