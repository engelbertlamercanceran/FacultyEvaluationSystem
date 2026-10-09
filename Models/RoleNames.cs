namespace FacultyEvalSystem.Models;

/// <summary>
/// How each role is shown to users. The role keys themselves ("AA", "AAStaff")
/// are unchanged so existing accounts and [Authorize] rules keep working; only
/// the labels follow the CDI naming.
/// </summary>
public static class RoleNames
{
    public static string Display(string? role) => role switch
    {
        "AA" => "CDI",
        "AAStaff" => "CDI Staff",
        "ProgramChair" => "Program Chair",
        null or "" => "None",
        _ => role
    };
}
