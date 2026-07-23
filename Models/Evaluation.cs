using System.ComponentModel.DataAnnotations;

namespace FacultyEvalSystem.Models;

public class Evaluation
{
    public int Id { get; set; }

    public int EvaluationPeriodId { get; set; }
    public EvaluationPeriod EvaluationPeriod { get; set; } = null!;

    [Required]
    public string FacultyId { get; set; } = "";
    public ApplicationUser Faculty { get; set; } = null!;

    [Required]
    public string EvaluatorId { get; set; } = "";
    public ApplicationUser Evaluator { get; set; } = null!;

    [Required, MaxLength(20)]
    public string EvaluatorType { get; set; } = "Student"; // "Student" or "Supervisor"

    public int? FacultySubjectId { get; set; }
    public FacultySubject? FacultySubject { get; set; }

    public DateTime SubmittedAt { get; set; } = DateTime.Now;

    [MaxLength(1000)]
    public string? Comments { get; set; }

    public List<EvaluationResponse> Responses { get; set; } = [];
}

public class EvaluationResponse
{
    public int Id { get; set; }

    public int EvaluationId { get; set; }
    public Evaluation Evaluation { get; set; } = null!;

    public int CriterionId { get; set; }
    public EvaluationCriterion Criterion { get; set; } = null!;

    public int Rating { get; set; } // 1-5
}
