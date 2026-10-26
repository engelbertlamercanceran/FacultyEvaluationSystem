using System.ComponentModel.DataAnnotations;

namespace FacultyEvalSystem.Models;

public class EvaluationPeriod
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = "";

    public int SemesterId { get; set; }
    public Semester Semester { get; set; } = null!;

    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }

    // AAS-03: the period runs as a sequence of stages rather than one window
    // open to everybody at once. CDI advances it one stage at a time.
    public EvaluationStage Stage { get; set; } = EvaluationStage.NotStarted;

    public DateTime? StudentStageOpenedAt { get; set; }
    // One supervisor stage for Program Chairs and Deans alike: they share the
    // same schedule, each evaluating only the people assigned to them.
    public DateTime? SupervisorStageOpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    public bool IsStudentStageOpen => Stage == EvaluationStage.StudentEvaluation;
    public bool IsSupervisorStageOpen => Stage == EvaluationStage.SupervisorEvaluation;
    public bool IsClosed => Stage == EvaluationStage.Closed;

    public bool IsActive => Stage is EvaluationStage.StudentEvaluation
                                  or EvaluationStage.SupervisorEvaluation;

    /// <summary>Is this period currently accepting submissions from the given role?</summary>
    public bool IsOpenFor(string role) => role switch
    {
        "Student" => IsStudentStageOpen,
        // Program Chairs evaluate their faculty (PC-01) and Deans their Program
        // Chairs (DN-01) during the same stage. The CDI office stands in for a
        // Program Chair with no designated evaluator (AA-01), so it does too.
        "ProgramChair" or "Dean" or "AA" or "AAStaff" => IsSupervisorStageOpen,
        _ => false
    };

    public EvaluationStage? NextStage => Stage switch
    {
        EvaluationStage.NotStarted => EvaluationStage.StudentEvaluation,
        EvaluationStage.StudentEvaluation => EvaluationStage.SupervisorEvaluation,
        EvaluationStage.SupervisorEvaluation => EvaluationStage.Closed,
        _ => null
    };

    public string DisplayName => Semester is not null
        ? $"{Name} — {Semester.DisplayName}"
        : Name;

    public static string StageLabel(EvaluationStage stage) => stage switch
    {
        EvaluationStage.NotStarted => "Not Started",
        EvaluationStage.StudentEvaluation => "Student Evaluation",
        EvaluationStage.SupervisorEvaluation => "Supervisor Evaluation",
        EvaluationStage.Closed => "Closed",
        _ => stage.ToString()
    };

    public string StageName => StageLabel(Stage);
}

public enum EvaluationStage
{
    NotStarted = 0,
    StudentEvaluation = 1,
    SupervisorEvaluation = 2,
    // 3 was a separate Dean stage, merged into SupervisorEvaluation.
    Closed = 4
}
