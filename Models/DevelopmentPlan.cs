using System.ComponentModel.DataAnnotations;

namespace FacultyEvalSystem.Models;

/// <summary>
/// The Development Plan section of the FEDAF.
///
/// Workflow (either starting point):
///   Program Chair creates -> Faculty views/edits and acknowledges -> Program Chair
///   views/edits -> Program Chair finalizes -> locked.
///   Dean creates -> (Program Chair and Faculty notified) -> same as above.
/// After finalization the plan is read-only for everybody.
/// </summary>
public class DevelopmentPlan
{
    public int Id { get; set; }

    public int EvaluationPeriodId { get; set; }
    public EvaluationPeriod EvaluationPeriod { get; set; } = null!;

    [Required]
    public string FacultyId { get; set; } = "";
    public ApplicationUser Faculty { get; set; } = null!;

    [MaxLength(2000)]
    public string? AreasForImprovement { get; set; }

    [MaxLength(2000)]
    public string? ProposedActivities { get; set; }

    [MaxLength(2000)]
    public string? ActionPlan { get; set; }

    // Retained for historical record only: set on plans where the faculty
    // member entered their own Areas for Improvement before FC-01.
    public DateTime? FacultySubmittedAt { get; set; }

    // When the plan was created / last saved by a supervisor (Dean or Program Chair)
    public DateTime? SupervisorSubmittedAt { get; set; }

    [MaxLength(450)]
    public string? SupervisorId { get; set; }

    // Who started the plan, and in which capacity ("Dean" or "ProgramChair")
    [MaxLength(450)]
    public string? CreatedById { get; set; }

    [MaxLength(20)]
    public string? CreatedByRole { get; set; }

    // The Program Chair concerned — notified on acknowledgement and the one who
    // finalizes. When the faculty member has no Program Chair above them (e.g. the
    // faculty member is a Program Chair) the Dean who created the plan finalizes.
    [MaxLength(450)]
    public string? ProgramChairId { get; set; }

    // Last edit made by the faculty member before acknowledging
    public DateTime? FacultyEditedAt { get; set; }

    public DateTime? AcknowledgedAt { get; set; }

    [MaxLength(450)]
    public string? AcknowledgedById { get; set; }

    public DateTime? FinalizedAt { get; set; }

    [MaxLength(450)]
    public string? FinalizedById { get; set; }

    public bool IsAcknowledged => AcknowledgedAt.HasValue;
    public bool IsFinalized => FinalizedAt.HasValue;

    /// <summary>Finalized plans are read-only for everybody.</summary>
    public bool IsLocked => IsFinalized;

    /// <summary>The faculty member may edit until they acknowledge.</summary>
    public bool FacultyCanEdit => SupervisorSubmittedAt.HasValue && !IsAcknowledged && !IsLocked;

    public FedafStatus Status =>
        FinalizedAt.HasValue ? FedafStatus.Finalized
        : AcknowledgedAt.HasValue ? FedafStatus.Acknowledged
        : SupervisorSubmittedAt.HasValue ? FedafStatus.DevelopmentPlanEntered
        : FedafStatus.Draft;

    public string StatusName => StatusLabel(Status);

    public static string StatusLabel(FedafStatus status) => status switch
    {
        FedafStatus.Draft => "Not Started",
        FedafStatus.DevelopmentPlanEntered => "Awaiting Faculty Acknowledgement",
        FedafStatus.Acknowledged => "Acknowledged — Awaiting Finalization",
        FedafStatus.Finalized => "Finalized (Locked)",
        _ => status.ToString()
    };

    public static string StatusBadgeClass(FedafStatus status) => status switch
    {
        FedafStatus.Draft => "bg-secondary",
        FedafStatus.DevelopmentPlanEntered => "bg-info text-dark",
        FedafStatus.Acknowledged => "bg-warning text-dark",
        FedafStatus.Finalized => "bg-success",
        _ => "bg-secondary"
    };
}

public enum FedafStatus
{
    Draft = 0,
    DevelopmentPlanEntered = 1,
    Acknowledged = 2,
    Finalized = 3
}
