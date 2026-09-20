using System.ComponentModel.DataAnnotations;
using FacultyEvalSystem.Models;

namespace FacultyEvalSystem.ViewModels;

public class EvaluationFormViewModel
{
    public int EvaluationPeriodId { get; set; }
    public string FacultyId { get; set; } = "";
    public string FacultyName { get; set; } = "";
    public int? FacultySubjectId { get; set; }
    public string? SubjectName { get; set; }
    public string EvaluatorType { get; set; } = "Student";

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
    public int? FacultySubjectId { get; set; }
    public string? SubjectCode { get; set; }
    public string? SubjectName { get; set; }
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
}
