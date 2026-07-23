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

    public bool IsOpen => DateTime.Now >= StartDate && DateTime.Now <= EndDate;

    public EvaluationStatus Status { get; set; } = EvaluationStatus.Pending;

    public string DisplayName => Semester is not null
        ? $"{Name} — {Semester.Term}, {Semester.AcademicYear}"
        : Name;
}

public enum EvaluationStatus
{
    Pending,
    Open,
    Closed
}
