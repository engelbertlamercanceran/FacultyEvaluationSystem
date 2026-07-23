using System.ComponentModel.DataAnnotations;

namespace FacultyEvalSystem.Models;

public class Semester
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string AcademicYear { get; set; } = ""; // e.g. "2025-2026"

    [Required, MaxLength(20)]
    public string Term { get; set; } = ""; // "1st Semester", "2nd Semester", "Summer"

    public bool IsActive { get; set; }

    public string DisplayName => $"{Term}, A.Y. {AcademicYear}";
}
