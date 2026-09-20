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

    // PC-02: all three sections are entered by the evaluating supervisor.
    // Faculty can no longer enter any of them (FC-01).
    [MaxLength(2000)]
    public string? AreasForImprovement { get; set; }

    [MaxLength(2000)]
    public string? ProposedActivities { get; set; }

    [MaxLength(2000)]
    public string? ActionPlan { get; set; }

    // Retained for historical record only: set on plans where the faculty
    // member entered their own Areas for Improvement before FC-01.
    public DateTime? FacultySubmittedAt { get; set; }

    public DateTime? SupervisorSubmittedAt { get; set; }

    [MaxLength(450)]
    public string? SupervisorId { get; set; }

    // FC-02: the faculty member acknowledges the FEDAF in the system.
    public DateTime? AcknowledgedAt { get; set; }

    [MaxLength(450)]
    public string? AcknowledgedById { get; set; }

    public bool IsAcknowledged => AcknowledgedAt.HasValue;

    /// <summary>D-03: the status shown on the IFER/FEDAF preview (PC-03).</summary>
    public FedafStatus Status =>
        AcknowledgedAt.HasValue ? FedafStatus.Acknowledged
        : SupervisorSubmittedAt.HasValue ? FedafStatus.DevelopmentPlanEntered
        : FedafStatus.Draft;

    public string StatusName => StatusLabel(Status);

    public static string StatusLabel(FedafStatus status) => status switch
    {
        FedafStatus.Draft => "Draft",
        FedafStatus.DevelopmentPlanEntered => "Development Plan Entered",
        FedafStatus.Acknowledged => "Acknowledged",
        _ => status.ToString()
    };

    public static string StatusBadgeClass(FedafStatus status) => status switch
    {
        FedafStatus.Draft => "bg-secondary",
        FedafStatus.DevelopmentPlanEntered => "bg-info text-dark",
        FedafStatus.Acknowledged => "bg-success",
        _ => "bg-secondary"
    };
}

public enum FedafStatus
{
    Draft = 0,
    DevelopmentPlanEntered = 1,
    Acknowledged = 2
}
