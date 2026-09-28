using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FacultyEvalSystem.Data;
using FacultyEvalSystem.Models;

namespace FacultyEvalSystem.Controllers;

/// <summary>In-app notifications for the signed-in user.</summary>
[Authorize]
public class NotificationsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public NotificationsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User);
        var list = await _db.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Take(200)
            .ToListAsync();
        return View(list);
    }

    /// <summary>Marks the notification read and follows its link.</summary>
    public async Task<IActionResult> Open(int id)
    {
        var userId = _userManager.GetUserId(User);
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
        if (n is null) return RedirectToAction(nameof(Index));

        if (n.ReadAt is null)
        {
            n.ReadAt = DateTime.Now;
            await _db.SaveChangesAsync();
        }

        return !string.IsNullOrEmpty(n.Link) && Url.IsLocalUrl(n.Link)
            ? LocalRedirect(n.Link)
            : RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead()
    {
        var userId = _userManager.GetUserId(User);
        var now = DateTime.Now;
        await _db.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, now));
        return RedirectToAction(nameof(Index));
    }
}
