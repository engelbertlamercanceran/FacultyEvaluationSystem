using System.ComponentModel.DataAnnotations;

namespace FacultyEvalSystem.Models;

/// <summary>
/// A Course Code a student is enrolled in for a semester, exactly as reported on
/// the Registrar's masterlist. Kept separately from StudentEnrollment because the
/// masterlist names the course but not the instructor: where only one faculty
/// member teaches the course the two are linked automatically, otherwise the
/// student picks their instructor on the evaluation page.
/// </summary>
public class StudentCourse
{
    public int Id { get; set; }

    [Required]
    public string StudentId { get; set; } = "";
    public ApplicationUser Student { get; set; } = null!;

    public int SemesterId { get; set; }
    public Semester Semester { get; set; } = null!;

    [Required, MaxLength(50)]
    public string CourseCode { get; set; } = "";

    [Required, MaxLength(50)]
    public string CourseKey { get; set; } = "";

    public decimal? Units { get; set; }
}

public static class CourseCodes
{
    /// <summary>
    /// Matching key for a course code: upper-case, annotations in brackets removed,
    /// and spaces, dots, dashes and underscores dropped.
    /// </summary>
    public static string Key(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var s = System.Text.RegularExpressions.Regex.Replace(code, @"\(.*?\)", "");
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (var c in s.ToUpperInvariant())
            if (!char.IsWhiteSpace(c) && c is not ('.' or '-' or '_' or '*')) sb.Append(c);
        var key = sb.ToString();
        return key.Length > 50 ? key[..50] : key;
    }

    /// <summary>Tidies a course code for display: trims and collapses repeated spaces.</summary>
    public static string Clean(string? code)
    {
        var s = System.Text.RegularExpressions.Regex.Replace((code ?? "").Trim(), @"\s+", " ");
        return s.Length > 50 ? s[..50] : s;
    }
}
