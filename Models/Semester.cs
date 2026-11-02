using System.ComponentModel.DataAnnotations;

namespace FacultyEvalSystem.Models;

public class Semester
{
    public int Id { get; set; }

    [Required, MaxLength(50)]
    public string AcademicYear { get; set; } = ""; // e.g. "2025-2026"

    [Required, MaxLength(20)]
    public string Term { get; set; } = ""; // "1st Semester", "2nd Semester", "Midyear"

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

    /// <summary>The terms an academic year is divided into, in order.</summary>
    public static readonly string[] Terms = ["1st Semester", "2nd Semester", "Midyear"];

    public static int GetTermOrder(string? term) => (term ?? "").Trim() switch
    {
        "1st Semester" => 1,
        "2nd Semester" => 2,
        "Midyear" or "Summer" => 3, // Midyear was called Summer before
        _ => 9
    };

    /// <summary>
    /// Checks an academic year such as "2026-2027": two four-digit years, the
    /// second exactly one after the first. Returns an error message, or null if valid.
    /// </summary>
    public static string? ValidateAcademicYear(string? academicYear)
    {
        var m = System.Text.RegularExpressions.Regex.Match(academicYear ?? "", @"^(\d{4})-(\d{4})$");
        if (!m.Success)
            return "The academic year must be written as YYYY-YYYY, for example 2026-2027.";

        int start = int.Parse(m.Groups[1].Value), end = int.Parse(m.Groups[2].Value);
        if (end != start + 1)
            return $"An academic year runs for one year only, so {start} must be followed by {start + 1} "
                 + $"(e.g. {start}-{start + 1}), not {end}.";

        if (start < 2000 || start > 2100)
            return "Please enter a realistic academic year.";

        return null;
    }

    public void RefreshSortKey() => SortKey = ComputeSortKey(AcademicYear, Term);
}
