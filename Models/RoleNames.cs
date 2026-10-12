namespace FacultyEvalSystem.Models;

/// <summary>
/// How each role is shown to users. The role keys themselves ("AA", "AAStaff")
/// are unchanged so existing accounts and [Authorize] rules keep working; only
/// the labels follow the CDI naming.
/// </summary>
public static class RoleNames
{
    /// <summary>
    /// The CDI office: CDI and CDI Staff have the same features and act as one
    /// office, so either can carry on the other's work.
    /// </summary>
    public static readonly string[] CdiOffice = ["AA", "AAStaff"];

    public static bool IsCdiOffice(string? role) => role is "AA" or "AAStaff";

    public static bool IsCdiOffice(IEnumerable<string> roles) => roles.Any(IsCdiOffice);

    public static string Display(string? role) => role switch
    {
        "AA" => "CDI",
        "AAStaff" => "CDI Staff",
        "ProgramChair" => "Program Chair",
        null or "" => "None",
        _ => role
    };
}
