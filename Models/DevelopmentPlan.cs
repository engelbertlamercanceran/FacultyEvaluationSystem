using System.ComponentModel.DataAnnotations;

namespace FacultyEvalSystem.Models;

public class DevelopmentPlan
{
    public int Id { get; set; }

    public int EvaluationPeriodId { get; set; }
    public EvaluationPeriod EvaluationPeriod { get; set; } = null!;

    [Required]
    public string FacultyId { get; set; } = "";
    public ApplicationUser Faculty { get; set; } = null!;

    // Faculty fills this
    [MaxLength(2000)]
    public string? AreasForImprovement { get; set; }

    // Supervisor (ProgramChair/Dean) fills these
    [MaxLength(2000)]
    public string? ProposedActivities { get; set; }

    [MaxLength(2000)]
    public string? ActionPlan { get; set; }

    public DateTime? FacultySubmittedAt { get; set; }
    public DateTime? SupervisorSubmittedAt { get; set; }
}
