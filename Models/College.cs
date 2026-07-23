using System.ComponentModel.DataAnnotations;

namespace FacultyEvalSystem.Models;

public class College
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = "";

    [Required, MaxLength(20)]
    public string Code { get; set; } = "";

    public List<AcademicProgram> Programs { get; set; } = [];
}
