using System.ComponentModel.DataAnnotations;

namespace FacultyEvalSystem.Models;

public class AcademicProgram
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = "";

    [Required, MaxLength(20)]
    public string Code { get; set; } = "";

    public int CollegeId { get; set; }
    public College College { get; set; } = null!;
}
