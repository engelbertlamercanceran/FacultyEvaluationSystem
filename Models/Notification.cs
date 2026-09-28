using System.ComponentModel.DataAnnotations;

namespace FacultyEvalSystem.Models;

/// <summary>An in-app notification. The system sends no e-mail notifications.</summary>
public class Notification
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = "";
    public ApplicationUser User { get; set; } = null!;

    [Required, MaxLength(200)]
    public string Title { get; set; } = "";

    [MaxLength(1000)]
    public string? Message { get; set; }

    // Where clicking the notification takes the user
    [MaxLength(500)]
    public string? Link { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public DateTime? ReadAt { get; set; }

    public bool IsRead => ReadAt.HasValue;
}
