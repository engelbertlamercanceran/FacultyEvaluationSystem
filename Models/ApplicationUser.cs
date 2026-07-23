using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;

namespace FacultyEvalSystem.Models;

public class ApplicationUser : IdentityUser
{
    [Required, MaxLength(100)]
    public string FirstName { get; set; } = "";

    [Required, MaxLength(100)]
    public string LastName { get; set; } = "";

    [MaxLength(20)]
    public string? EmployeeNumber { get; set; }

    [MaxLength(20)]
    public string? StudentNumber { get; set; }

    public int? CollegeId { get; set; }
    public College? College { get; set; }

    public int? ProgramId { get; set; }
    public AcademicProgram? Program { get; set; }

    public bool IsActive { get; set; } = true;

    public string FullName => $"{FirstName} {LastName}";
}
