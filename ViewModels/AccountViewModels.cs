using System.ComponentModel.DataAnnotations;

namespace FacultyEvalSystem.ViewModels;

public class LoginViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [DataType(DataType.Password)]
    public string? Password { get; set; }

    public bool RememberMe { get; set; }

    // Two-step login: false = email step, true = password step
    public bool ShowPassword { get; set; }
}

public class RegisterViewModel
{
    [Required, MaxLength(100)]
    [Display(Name = "First Name")]
    public string FirstName { get; set; } = "";

    [Required, MaxLength(100)]
    [Display(Name = "Last Name")]
    public string LastName { get; set; } = "";

    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [DataType(DataType.Password)]
    [MinLength(6, ErrorMessage = "Password must be at least 6 characters")]
    public string? Password { get; set; }

    [DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    [Compare("Password")]
    public string? ConfirmPassword { get; set; }

    [Required]
    public string Role { get; set; } = "Student";

    [MaxLength(20)]
    [Display(Name = "Employee/Student Number")]
    public string? IdNumber { get; set; }

    [Display(Name = "College")]
    public int? CollegeId { get; set; }

    [Display(Name = "Program")]
    public int? ProgramId { get; set; }
}

public class SetPasswordViewModel
{
    [Required, EmailAddress]
    public string Email { get; set; } = "";

    [Required, DataType(DataType.Password)]
    [MinLength(6, ErrorMessage = "Password must be at least 6 characters")]
    [Display(Name = "New Password")]
    public string Password { get; set; } = "";

    [Required, DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    [Compare("Password")]
    public string ConfirmPassword { get; set; } = "";
}
