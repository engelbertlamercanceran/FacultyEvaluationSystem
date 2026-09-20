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

    // SUP-02: which program owns this course. Required by PC-01 so a Program
    // Chair can be shown faculty from other departments who teach courses
    // within their program. Nullable: assignments imported before the
    // Teaching Memo carried a program code have none.
    public int? ProgramId { get; set; }
    public AcademicProgram? Program { get; set; }

    // AAS-01: the Teaching Memo this assignment came from, kept so an
    // assignment can be traced back to the document that authorised it.
    [MaxLength(50)]
    public string? TeachingMemoNumber { get; set; }

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
