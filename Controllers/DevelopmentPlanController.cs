using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FacultyEvalSystem.Data;
using FacultyEvalSystem.Models;
using FacultyEvalSystem.Services;

namespace FacultyEvalSystem.Controllers;

/// <summary>
/// Development Plan (FEDAF) workflow. Either the Program Chair or the Dean starts it:
///
///   A. Program Chair creates -> Faculty notified -> Faculty views/edits and
///      acknowledges -> Program Chair notified -> Program Chair views/edits ->
///      Program Chair finalizes -> locked.
///   B. Dean creates -> Program Chair AND Faculty notified -> continues as in A.
///
/// After finalization the plan is read-only for everybody. All notifications are
/// in-app only.
/// </summary>
[Authorize]
public class DevelopmentPlanController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly EvaluatorAssignmentService _assignments;
    private readonly NotificationService _notifications;

    public DevelopmentPlanController(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        EvaluatorAssignmentService assignments, NotificationService notifications)
    {
        _db = db;
        _userManager = userManager;
        _assignments = assignments;
        _notifications = notifications;
    }

    public record PlanItem(EvaluationPeriod Period, EvaluationResult? Result, DevelopmentPlan? Plan);

    public record RosterRow(ApplicationUser Faculty, EvaluationResult? Result, DevelopmentPlan? Plan);

    // =====================================================================
    // Faculty side: own results, IFER/FEDAF and Development Plans
    // FC-03: opens on the most recent semester. FC-04: search to look further back.
    // =====================================================================

    [Authorize(Roles = "Faculty,ProgramChair")]
    public async Task<IActionResult> Index(string? search, int? semesterId)
    {
        var userId = _userManager.GetUserId(User);

        var results = await _db.EvaluationResults
            .Include(r => r.EvaluationPeriod).ThenInclude(p => p.Semester)
            .Where(r => r.FacultyId == userId)
            .ToListAsync();

        var plans = await _db.DevelopmentPlans
            .Include(d => d.EvaluationPeriod).ThenInclude(p => p.Semester)
            .Where(d => d.FacultyId == userId)
            .ToListAsync();

        // A plan can exist before results are computed, so list every period with either
        var all = results.Select(r => r.EvaluationPeriod)
            .Concat(plans.Select(p => p.EvaluationPeriod))
            .DistinctBy(p => p.Id)
            .OrderByDescending(p => p.Semester.SortKey).ThenByDescending(p => p.Id)
            .Select(p => new PlanItem(p,
                results.FirstOrDefault(r => r.EvaluationPeriodId == p.Id),
                plans.FirstOrDefault(d => d.EvaluationPeriodId == p.Id)))
            .ToList();

        var semesters = all.Select(i => i.Period.Semester)
            .DistinctBy(s => s.Id)
            .OrderByDescending(s => s.SortKey)
            .ToList();

        var items = all;

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            items = all.Where(i =>
                i.Period.Semester.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || i.Period.Semester.AcademicYear.Contains(term, StringComparison.OrdinalIgnoreCase)
                || i.Period.Semester.Term.Contains(term, StringComparison.OrdinalIgnoreCase)
                || i.Period.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        else if (semesterId.HasValue)
        {
            items = all.Where(i => i.Period.SemesterId == semesterId.Value).ToList();
        }
        else if (semesters.Count > 0)
        {
            var latest = semesters[0];
            items = all.Where(i => i.Period.SemesterId == latest.Id).ToList();
            ViewBag.DefaultedToLatest = true;
            ViewBag.LatestSemester = latest;
        }

        ViewBag.Semesters = semesters;
        ViewBag.Search = search;
        ViewBag.SelectedSemesterId = semesterId;
        ViewBag.TotalRecords = all.Count;
        return View(items);
    }

    /// <summary>The faculty member edits the Development Plan before acknowledging it.</summary>
    [Authorize(Roles = "Faculty,ProgramChair")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> FacultySave(int evaluationPeriodId,
        string? areasForImprovement, string? proposedActivities, string? actionPlan)
    {
        var userId = _userManager.GetUserId(User);
        var plan = await _db.DevelopmentPlans
            .FirstOrDefaultAsync(d => d.EvaluationPeriodId == evaluationPeriodId && d.FacultyId == userId);

        if (plan is null || !plan.FacultyCanEdit)
        {
            TempData["Error"] = plan?.IsLocked == true
                ? "This Development Plan has been finalized and is now read-only."
                : "This Development Plan can no longer be edited.";
            return RedirectToAction(nameof(Index));
        }

        plan.AreasForImprovement = Trim(areasForImprovement);
        plan.ProposedActivities = Trim(proposedActivities);
        plan.ActionPlan = Trim(actionPlan);
        plan.FacultyEditedAt = DateTime.Now;

        // Faculty updates -> the Program Chair concerned is notified
        var faculty = await _userManager.GetUserAsync(User);
        _notifications.Add([FinalizerId(plan)],
            $"{faculty!.FullName} updated their Development Plan",
            "The faculty member edited their Development Plan before acknowledging it.",
            Url.Action(nameof(Review), new { periodId = plan.EvaluationPeriodId, facultyId = plan.FacultyId }));

        await _db.SaveChangesAsync();

        TempData["Success"] = "Your changes to the Development Plan were saved. Acknowledge it when you are ready.";
        return RedirectToAction(nameof(Index), new { semesterId = await SemesterOfAsync(evaluationPeriodId) });
    }

    /// <summary>
    /// FC-02: the faculty member acknowledges the Development Plan. The concerned
    /// Program Chair is notified.
    /// </summary>
    [Authorize(Roles = "Faculty,ProgramChair")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Acknowledge(int evaluationPeriodId)
    {
        var user = await _userManager.GetUserAsync(User);

        var plan = await _db.DevelopmentPlans
            .FirstOrDefaultAsync(d => d.EvaluationPeriodId == evaluationPeriodId && d.FacultyId == user!.Id);

        if (plan is null || plan.Status == FedafStatus.Draft)
        {
            TempData["Error"] = "There is no Development Plan to acknowledge yet.";
            return RedirectToAction(nameof(Index));
        }

        if (plan.IsAcknowledged)
        {
            TempData["Error"] = "You have already acknowledged this Development Plan.";
            return RedirectToAction(nameof(Index));
        }

        plan.AcknowledgedAt = DateTime.Now;
        plan.AcknowledgedById = user!.Id;

        // Notify the Program Chair concerned (or whoever will finalize)
        _notifications.Add([FinalizerId(plan)],
            $"{user.FullName} acknowledged their Development Plan",
            "The Development Plan is ready for your review. You may edit it, then click Finalize to lock it.",
            Url.Action(nameof(Review), new { periodId = plan.EvaluationPeriodId, facultyId = plan.FacultyId }));

        await _db.SaveChangesAsync();

        TempData["Success"] = "Development Plan acknowledged on "
                            + $"{plan.AcknowledgedAt:MMMM dd, yyyy}. Your Program Chair has been notified.";
        return RedirectToAction(nameof(Index), new { semesterId = await SemesterOfAsync(evaluationPeriodId) });
    }

    // =====================================================================
    // Supervisor side
    // =====================================================================

    [Authorize(Roles = "Dean,ProgramChair,AA")]
    public async Task<IActionResult> Manage(int? periodId)
    {
        var user = await _userManager.GetUserAsync(User);
        var roles = await _userManager.GetRolesAsync(user!);

        var periods = await _db.EvaluationPeriods
            .Include(p => p.Semester)
            .OrderByDescending(p => p.Semester.SortKey).ThenByDescending(p => p.Id)
            .ToListAsync();

        ViewBag.Periods = periods;

        if (!periodId.HasValue && periods.Count > 0)
        {
            // Prefer the most recent period that has results; otherwise the newest period
            var withResults = await _db.EvaluationResults.Select(r => r.EvaluationPeriodId).Distinct().ToListAsync();
            periodId = periods.FirstOrDefault(p => withResults.Contains(p.Id))?.Id ?? periods[0].Id;
        }

        ViewBag.SelectedPeriodId = periodId;

        var rows = new List<RosterRow>();
        if (periodId.HasValue)
        {
            var ids = await _assignments.GetDevelopmentPlanScopeAsync(user!, roles, periodId.Value);

            var faculty = await _db.Users
                .Include(u => u.College)
                .Include(u => u.Program)
                .Where(u => ids.Contains(u.Id))
                .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
                .ToListAsync();

            var results = await _db.EvaluationResults
                .Where(r => r.EvaluationPeriodId == periodId.Value && ids.Contains(r.FacultyId))
                .ToDictionaryAsync(r => r.FacultyId);

            var plans = await _db.DevelopmentPlans
                .Where(d => d.EvaluationPeriodId == periodId.Value && ids.Contains(d.FacultyId))
                .ToDictionaryAsync(d => d.FacultyId);

            rows = faculty.Select(f => new RosterRow(f, results.GetValueOrDefault(f.Id), plans.GetValueOrDefault(f.Id)))
                .ToList();
        }

        return View(rows);
    }

    [Authorize(Roles = "Dean,ProgramChair,AA")]
    [HttpGet]
    public async Task<IActionResult> Review(int periodId, string facultyId)
    {
        var user = await _userManager.GetUserAsync(User);
        var roles = await _userManager.GetRolesAsync(user!);

        var plan = await _db.DevelopmentPlans
            .FirstOrDefaultAsync(d => d.EvaluationPeriodId == periodId && d.FacultyId == facultyId);

        if (!await CanAccessAsync(user!, roles, facultyId, periodId, plan))
            return Forbid();

        var period = await _db.EvaluationPeriods.Include(p => p.Semester).FirstOrDefaultAsync(p => p.Id == periodId);
        var faculty = await _db.Users.Include(u => u.College).Include(u => u.Program)
            .FirstOrDefaultAsync(u => u.Id == facultyId);
        if (period is null || faculty is null) return NotFound();

        var result = await _db.EvaluationResults
            .FirstOrDefaultAsync(r => r.EvaluationPeriodId == periodId && r.FacultyId == facultyId);

        plan ??= new DevelopmentPlan { EvaluationPeriodId = periodId, FacultyId = facultyId };

        ViewBag.Period = period;
        ViewBag.Faculty = faculty;
        ViewBag.Result = result;
        ViewBag.CanEdit = !plan.IsLocked;
        ViewBag.CanFinalize = plan.IsAcknowledged && !plan.IsFinalized && FinalizerId(plan) == user!.Id;
        ViewBag.Finalizer = await NameOfAsync(plan.Id == 0 ? null : FinalizerId(plan));
        ViewBag.CreatedBy = await NameOfAsync(plan.CreatedById);
        return View(plan);
    }

    /// <summary>
    /// A Dean or Program Chair creates or edits the Development Plan. Creating it
    /// notifies the faculty member — and, when the Dean creates it, the Program Chair
    /// concerned as well.
    /// </summary>
    [Authorize(Roles = "Dean,ProgramChair,AA")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(int evaluationPeriodId, string facultyId,
        string? areasForImprovement, string? proposedActivities, string? actionPlan)
    {
        var user = await _userManager.GetUserAsync(User);
        var roles = await _userManager.GetRolesAsync(user!);

        var plan = await _db.DevelopmentPlans
            .FirstOrDefaultAsync(d => d.EvaluationPeriodId == evaluationPeriodId && d.FacultyId == facultyId);

        if (!await CanAccessAsync(user!, roles, facultyId, evaluationPeriodId, plan))
            return Forbid();

        if (plan is not null && plan.IsLocked)
        {
            TempData["Error"] = "This Development Plan has been finalized and is now read-only.";
            return RedirectToAction(nameof(Review), new { periodId = evaluationPeriodId, facultyId });
        }

        var faculty = await _db.Users.FirstAsync(u => u.Id == facultyId);
        var period = await _db.EvaluationPeriods.Include(p => p.Semester).FirstAsync(p => p.Id == evaluationPeriodId);
        var facultyLink = Url.Action(nameof(Index), new { semesterId = period.SemesterId });
        var reviewLink = Url.Action(nameof(Review), new { periodId = evaluationPeriodId, facultyId });

        bool isNew = plan is null;
        if (plan is null)
        {
            var creatorRole = roles.Contains("ProgramChair") ? "ProgramChair"
                            : roles.Contains("Dean") ? "Dean" : "AA";

            plan = new DevelopmentPlan
            {
                EvaluationPeriodId = evaluationPeriodId,
                FacultyId = facultyId,
                CreatedById = user!.Id,
                CreatedByRole = creatorRole,
                // A Program Chair's own plan has no Chair above it; its creator finalizes.
                ProgramChairId = creatorRole == "ProgramChair"
                    ? user.Id
                    : await _userManager.IsInRoleAsync(faculty, "ProgramChair")
                        ? null
                        : (await _assignments.FindConcernedProgramChairAsync(faculty, period.SemesterId))?.Id
            };
            _db.DevelopmentPlans.Add(plan);
        }

        plan.AreasForImprovement = Trim(areasForImprovement);
        plan.ProposedActivities = Trim(proposedActivities);
        plan.ActionPlan = Trim(actionPlan);
        plan.SupervisorSubmittedAt = DateTime.Now;
        plan.SupervisorId = user!.Id;

        var semesterName = period.Semester.DisplayName;

        if (!isNew)
        {
            // Dean updates -> the Program Chair concerned is notified.
            // Any supervisor update before acknowledgement -> the faculty member is told too.
            if (plan.ProgramChairId is not null && plan.ProgramChairId != user.Id)
            {
                _notifications.Add([plan.ProgramChairId],
                    $"Development Plan for {faculty.FullName} was updated",
                    $"{user.FullName} updated the Development Plan for {faculty.FullName} ({semesterName}).",
                    reviewLink);
            }

            if (!plan.IsAcknowledged)
            {
                _notifications.Add([facultyId],
                    "Your Development Plan was updated",
                    $"{user.FullName} updated your Development Plan ({semesterName}). Please review it before acknowledging.",
                    facultyLink);
            }
        }

        if (isNew)
        {

            // Program Chair creates -> Faculty notified.
            // Dean creates -> Program Chair AND Faculty notified.
            _notifications.Add([facultyId],
                $"A Development Plan was created for you ({semesterName})",
                $"{user.FullName} created your Development Plan. You may view and edit it, then acknowledge it.",
                facultyLink);

            if (plan.CreatedByRole != "ProgramChair" && plan.ProgramChairId is not null)
            {
                _notifications.Add([plan.ProgramChairId],
                    $"The Dean created a Development Plan for {faculty.FullName}",
                    $"{user.FullName} created a Development Plan for {faculty.FullName} ({semesterName}). "
                    + "You will be notified when the faculty member acknowledges it, and you can then finalize it.",
                    reviewLink);
            }
        }

        await _db.SaveChangesAsync();

        TempData["Success"] = isNew
            ? $"Development Plan created. {faculty.FullName} has been notified"
              + (plan.CreatedByRole != "ProgramChair" && plan.ProgramChairId is not null ? ", as has the Program Chair." : ".")
            : "Development Plan saved.";
        return RedirectToAction(nameof(Review), new { periodId = evaluationPeriodId, facultyId });
    }

    /// <summary>
    /// The Program Chair finalizes the Development Plan after the faculty member has
    /// acknowledged it. The plan is then locked and read-only for everybody.
    /// </summary>
    [Authorize(Roles = "Dean,ProgramChair,AA")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Finalize(int evaluationPeriodId, string facultyId)
    {
        var user = await _userManager.GetUserAsync(User);

        var plan = await _db.DevelopmentPlans
            .FirstOrDefaultAsync(d => d.EvaluationPeriodId == evaluationPeriodId && d.FacultyId == facultyId);

        if (plan is null) return NotFound();

        if (FinalizerId(plan) != user!.Id)
        {
            TempData["Error"] = "Only the Program Chair concerned can finalize this Development Plan.";
            return RedirectToAction(nameof(Review), new { periodId = evaluationPeriodId, facultyId });
        }

        if (!plan.IsAcknowledged)
        {
            TempData["Error"] = "The faculty member has not acknowledged this Development Plan yet.";
            return RedirectToAction(nameof(Review), new { periodId = evaluationPeriodId, facultyId });
        }

        if (plan.IsFinalized)
        {
            TempData["Error"] = "This Development Plan is already finalized.";
            return RedirectToAction(nameof(Review), new { periodId = evaluationPeriodId, facultyId });
        }

        plan.FinalizedAt = DateTime.Now;
        plan.FinalizedById = user.Id;

        var semesterId = await SemesterOfAsync(evaluationPeriodId);
        _notifications.Add([plan.FacultyId],
            "Your Development Plan has been finalized",
            $"{user.FullName} finalized your Development Plan. It is now locked and view-only.",
            Url.Action(nameof(Index), new { semesterId }));

        await _db.SaveChangesAsync();

        TempData["Success"] = "Development Plan finalized. It is now locked and read-only.";
        return RedirectToAction(nameof(Review), new { periodId = evaluationPeriodId, facultyId });
    }

    // =====================================================================

    /// <summary>
    /// The person who finalizes: the Program Chair concerned, or — when the faculty
    /// member has no Program Chair above them — whoever created the plan.
    /// </summary>
    private static string? FinalizerId(DevelopmentPlan plan)
        => plan.ProgramChairId ?? plan.CreatedById ?? plan.SupervisorId;

    /// <summary>
    /// A supervisor may open a plan when the faculty member is within their scope,
    /// or when they are the Program Chair / creator recorded on the plan.
    /// </summary>
    private async Task<bool> CanAccessAsync(ApplicationUser user, IList<string> roles,
        string facultyId, int periodId, DevelopmentPlan? plan)
    {
        if (plan is not null && (plan.ProgramChairId == user.Id || plan.CreatedById == user.Id))
            return true;

        var ids = await _assignments.GetDevelopmentPlanScopeAsync(user, roles, periodId);
        return ids.Contains(facultyId);
    }

    private async Task<string?> NameOfAsync(string? userId)
    {
        if (userId is null) return null;
        var u = await _db.Users.FindAsync(userId);
        return u?.FullName;
    }

    private async Task<int?> SemesterOfAsync(int periodId)
        => await _db.EvaluationPeriods.Where(p => p.Id == periodId).Select(p => (int?)p.SemesterId).FirstOrDefaultAsync();

    private static string? Trim(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
