using System.ComponentModel.DataAnnotations;

namespace FacultyEvalSystem.Models;

public class EvaluationCategory
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = ""; // e.g. "Commitment", "Knowledge of Subject"

    public int SortOrder { get; set; }

    [Required, MaxLength(20)]
    public string EvaluatorType { get; set; } = "Student"; // "Student" or "Supervisor"

    public double Weight { get; set; } // percentage weight

    public List<EvaluationCriterion> Criteria { get; set; } = [];
}

public class EvaluationCriterion
{
    public int Id { get; set; }

    public int CategoryId { get; set; }
    public EvaluationCategory Category { get; set; } = null!;

    [Required, MaxLength(500)]
    public string Description { get; set; } = "";

    public int SortOrder { get; set; }
}
