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

    public double StudentRating { get; set; }
    public int StudentRespondents { get; set; }

    public double SupervisorRating { get; set; }
    public int SupervisorRespondents { get; set; }

    // Weighted: Student (60%) + Supervisor (40%) per CMO No. 19
    public double OverallRating { get; set; }

    [MaxLength(50)]
    public string DescriptiveRating { get; set; } = "";

    public DateTime ComputedAt { get; set; } = DateTime.Now;

    public static string GetDescriptiveRating(double rating) => rating switch
    {
        >= 4.5 => "Outstanding",
        >= 3.5 => "Very Satisfactory",
        >= 2.5 => "Satisfactory",
        >= 1.5 => "Fair",
        _ => "Poor"
    };
}
