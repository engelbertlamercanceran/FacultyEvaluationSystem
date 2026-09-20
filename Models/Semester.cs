using System.ComponentModel.DataAnnotations;

namespace FacultyEvalSystem.Models;

public class Semester
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string AcademicYear { get; set; } = ""; // e.g. "2025-2026"

    [Required, MaxLength(20)]
    public string Term { get; set; } = ""; // "1st Semester", "2nd Semester", "Summer"

    public bool IsActive { get; set; }

    // SUP-01: single orderable value so semesters can be placed in time.
    // AcademicYear/Term are free text, so ordering by them alone cannot tell
    // that 2nd Semester follows 1st within the same academic year.
    // Format: <start year> * 10 + <term order>, e.g. "2025-2026" 1st Sem => 20251
    public int SortKey { get; set; }

    public string DisplayName => $"{Term}, A.Y. {AcademicYear}";

    public static int ComputeSortKey(string academicYear, string term)
    {
        int startYear = 0;
        var parts = (academicYear ?? "").Split('-');
        if (parts.Length > 0) int.TryParse(parts[0].Trim(), out startYear);

        return startYear * 10 + GetTermOrder(term);
    }

    public static int GetTermOrder(string? term) => (term ?? "").Trim() switch
    {
        "1st Semester" => 1,
        "2nd Semester" => 2,
        "Summer" => 3,
        _ => 9
    };

    public void RefreshSortKey() => SortKey = ComputeSortKey(AcademicYear, Term);
}
