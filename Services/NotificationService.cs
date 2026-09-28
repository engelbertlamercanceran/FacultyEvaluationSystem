using Microsoft.EntityFrameworkCore;
using FacultyEvalSystem.Data;
using FacultyEvalSystem.Models;

namespace FacultyEvalSystem.Services;

/// <summary>
/// In-app notifications. Notifications are shown inside the system only;
/// no e-mail is sent.
/// </summary>
public class NotificationService
{
    private readonly ApplicationDbContext _db;

    public NotificationService(ApplicationDbContext db) => _db = db;

    /// <summary>Queues a notification for each recipient. Call SaveChanges (or use NotifyAsync).</summary>
    public void Add(IEnumerable<string?> userIds, string title, string? message, string? link)
    {
        foreach (var id in userIds.Where(id => !string.IsNullOrEmpty(id)).Distinct())
        {
            _db.Notifications.Add(new Notification
            {
                UserId = id!,
                Title = title.Length > 200 ? title[..200] : title,
                Message = message is { Length: > 1000 } ? message[..1000] : message,
                Link = link,
                CreatedAt = DateTime.Now
            });
        }
    }

    public async Task NotifyAsync(IEnumerable<string?> userIds, string title, string? message, string? link)
    {
        Add(userIds, title, message, link);
        await _db.SaveChangesAsync();
    }

    public Task<int> UnreadCountAsync(string userId)
        => _db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null);

    public Task<List<Notification>> RecentAsync(string userId, int take)
        => _db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Take(take)
            .ToListAsync();
}
