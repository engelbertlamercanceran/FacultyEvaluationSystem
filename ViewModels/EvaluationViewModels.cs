using System.ComponentModel.DataAnnotations;
using FacultyEvalSystem.Models;

namespace FacultyEvalSystem.ViewModels;

public class EvaluationFormViewModel
{
    public int EvaluationPeriodId { get; set; }
    public string FacultyId { get; set; } = "";
    public string FacultyName { get; set; } = "";
    public int? FacultySubjectId { get; set; }
    public string? CourseTitle { get; set; }
    public string EvaluatorType { get; set; } = "Student";

    // Shown on the form: the semester and academic year the evaluation is recorded under
    public string? SemesterName { get; set; }
    public string? PeriodName { get; set; }

    public List<CategoryViewModel> Categories { get; set; } = [];

    [MaxLength(1000)]
    public string? Comments { get; set; }
}

public class CategoryViewModel
{
    public int CategoryId { get; set; }
    public string CategoryName { get; set; } = "";
    public List<CriterionViewModel> Criteria { get; set; } = [];
}

public class CriterionViewModel
{
    public int CriterionId { get; set; }
    public string Description { get; set; } = "";

    [Required(ErrorMessage = "Please select a rating")]
    [Range(1, 5)]
    public int Rating { get; set; }
}

public class FacultyToEvaluateViewModel
{
    public string FacultyId { get; set; } = "";
    public string FacultyName { get; set; } = "";
    public string SortName { get; set; } = "";
    public int? FacultySubjectId { get; set; }
    public string? CourseCode { get; set; }
    public string? CourseTitle { get; set; }
    public string? Section { get; set; }
    public bool AlreadyEvaluated { get; set; }
    public int? EvaluationId { get; set; }
    public double? CurrentRating { get; set; }
    public string? DescriptiveRating { get; set; }

    // PC-01: the courses this faculty member teaches within the evaluating
    // Chair's program — shown so the Chair knows why the person is on the list.
    public List<string> CoursesInProgram { get; set; } = [];

    // PC-01: true when the faculty member's home department differs from the
    // program whose Chair is evaluating them.
    public bool IsFromAnotherDepartment { get; set; }
    public string? HomeDepartment { get; set; }
}

/// <summary>DN-01: Deans see Program Chairs organised per program.</summary>
public class EvaluationGroupViewModel
{
    public string GroupName { get; set; } = "";
    public string? GroupSubtitle { get; set; }
    public List<FacultyToEvaluateViewModel> Members { get; set; } = [];
}

public class EvaluationListViewModel
{
    public EvaluationPeriod? ActivePeriod { get; set; }
    public List<FacultyToEvaluateViewModel> FacultyList { get; set; } = [];

    /// <summary>Set when the list should be presented in groups (DN-01).</summary>
    public List<EvaluationGroupViewModel> Groups { get; set; } = [];
    public bool IsGrouped => Groups.Count > 0;

    /// <summary>Heading describing who the signed-in evaluator is responsible for.</summary>
    public string? RosterDescription { get; set; }

    /// <summary>Shown when the period exists but this evaluator's stage is not open yet.</summary>
    public string? StageMessage { get; set; }

    /// <summary>
    /// Student: Course Codes from the Registrar's masterlist taught by more than one
    /// faculty member this semester — the student chooses their instructor.
    /// </summary>
    public List<InstructorChoiceViewModel> InstructorChoices { get; set; } = [];

    /// <summary>Student: Course Codes with no faculty member on record for this semester.</summary>
    public List<string> CoursesWithoutInstructor { get; set; } = [];
}

public class InstructorChoiceViewModel
{
    public string CourseCode { get; set; } = "";
    public List<(int FacultySubjectId, string FacultyName, string? College)> Options { get; set; } = [];
}

/// <summary>Evaluation Monitoring (Dean / Program Chair): student completion for the current period.</summary>
public class MonitoringViewModel
{
    public EvaluationPeriod? Period { get; set; }
    public string? Message { get; set; }
    public string ScopeName { get; set; } = "";

    // Dean only: narrow the college down to one program
    public bool IsDean { get; set; }
    public List<AcademicProgram> Programs { get; set; } = [];
    public int? ProgramId { get; set; }

    public List<StudentProgressRow> Completed { get; set; } = [];
    public List<StudentProgressRow> NotCompleted { get; set; } = [];

    /// <summary>Students in scope with no course to evaluate this semester; left out of the totals.</summary>
    public int WithoutCourses { get; set; }

    public int Total => Completed.Count + NotCompleted.Count;
    public int Percent => Total == 0 ? 0 : (int)Math.Round(100.0 * Completed.Count / Total);
    public int InProgress => NotCompleted.Count(r => r.Done > 0);
}

public class StudentProgressRow
{
    public string Name { get; set; } = "";
    public string? StudentNumber { get; set; }
    public string? Email { get; set; }
    public string? Program { get; set; }
    public string? Section { get; set; }
    public int Done { get; set; }
    public int Due { get; set; }
    public bool IsComplete => Done >= Due;
}
