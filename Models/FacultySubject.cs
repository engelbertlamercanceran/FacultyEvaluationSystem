using System.ComponentModel.DataAnnotations;

namespace FacultyEvalSystem.Models;

public class FacultySubject
{
    public int Id { get; set; }

    [Required]
    public string FacultyId { get; set; } = "";
    public ApplicationUser Faculty { get; set; } = null!;

    public int SemesterId { get; set; }
    public Semester Semester { get; set; } = null!;

    [Required, MaxLength(20)]
    public string SubjectCode { get; set; } = "";

    [Required, MaxLength(200)]
    public string SubjectName { get; set; } = "";

    [MaxLength(20)]
    public string? Section { get; set; }

    public List<StudentEnrollment> Enrollments { get; set; } = [];
}

public class StudentEnrollment
{
    public int Id { get; set; }

    public int FacultySubjectId { get; set; }
    public FacultySubject FacultySubject { get; set; } = null!;

    [Required]
    public string StudentId { get; set; } = "";
    public ApplicationUser Student { get; set; } = null!;
}
