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
}

public class EvaluationListViewModel
{
    public EvaluationPeriod? ActivePeriod { get; set; }
    public List<FacultyToEvaluateViewModel> FacultyList { get; set; } = [];
}
