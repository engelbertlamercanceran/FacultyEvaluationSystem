using System.ComponentModel.DataAnnotations;

namespace FacultyEvalSystem.Models;

// Pre-computed results for faster dashboard/report access
public class EvaluationResult
{
    public int Id { get; set; }

    public int EvaluationPeriodId { get; set; }
    public EvaluationPeriod EvaluationPeriod { get; set; } = null!;

    [Required]
    public string FacultyId { get; set; } = "";
    public ApplicationUser Faculty { get; set; } = null!;

    // SET Rating: (Total Score / 75) × 100, weighted by class size per CMO Section 8.3
    public double StudentRating { get; set; }
    public int StudentRespondents { get; set; }

    // SEF Rating: (Total Score / 75) × 100
    public double SupervisorRating { get; set; }
    public int SupervisorRespondents { get; set; }

    [MaxLength(50)]
    public string StudentDescriptiveRating { get; set; } = "";

    [MaxLength(50)]
    public string SupervisorDescriptiveRating { get; set; } = "";

    public DateTime ComputedAt { get; set; } = DateTime.Now;

    // Per CMO No. 19 ANNEX A/B Rating Scale operational definitions
    public static string GetDescriptiveRating(double percentageRating) => percentageRating switch
    {
        >= 91 => "Always Manifested",
        >= 61 => "Often Manifested",
        >= 31 => "Sometimes Manifested",
        >= 11 => "Seldom Manifested",
        _ => "Never/Rarely Manifested"
    };
}
