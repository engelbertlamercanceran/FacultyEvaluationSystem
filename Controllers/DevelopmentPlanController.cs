using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FacultyEvalSystem.Data;
using FacultyEvalSystem.Models;
using FacultyEvalSystem.Services;

namespace FacultyEvalSystem.Controllers;

[Authorize]
public class DevelopmentPlanController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly EvaluatorAssignmentService _assignments;

    public DevelopmentPlanController(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        EvaluatorAssignmentService assignments)
    {
        _db = db;
        _userManager = userManager;
        _assignments = assignments;
    }

    // Faculty: own results, IFER/FEDAF and the Development Plan (read-only, FC-01).
    // FC-03: opens on the most recent semester. FC-04: search to look further back.
    [Authorize(Roles = "Faculty,ProgramChair")]
    public async Task<IActionResult> Index(string? search, int? semesterId)
    {
        var userId = _userManager.GetUserId(User);

        var query = _db.EvaluationResults
            .Include(r => r.EvaluationPeriod).ThenInclude(p => p.Semester)
            .Where(r => r.FacultyId == userId);

        var all = await query
            .OrderByDescending(r => r.EvaluationPeriod.Semester.SortKey)
            .ThenByDescending(r => r.EvaluationPeriodId)
            .ToListAsync();

        // Every semester this person has a record for, newest first (FC-04)
        var semesters = all
            .Select(r => r.EvaluationPeriod.Semester)
            .DistinctBy(s => s.Id)
            .OrderByDescending(s => s.SortKey)
            .ToList();

        var results = all;

        if (!string.IsNullOrWhiteSpace(search))
        {
            // FC-04: free-text search across semester, term and period name
            var term = search.Trim();
            results = all.Where(r =>
                r.EvaluationPeriod.Semester.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)
                || r.EvaluationPeriod.Semester.AcademicYear.Contains(term, StringComparison.OrdinalIgnoreCase)
                || r.EvaluationPeriod.Semester.Term.Contains(term, StringComparison.OrdinalIgnoreCase)
                || r.EvaluationPeriod.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        else if (semesterId.HasValue)
        {
            results = all.Where(r => r.EvaluationPeriod.SemesterId == semesterId.Value).ToList();
        }
        else if (semesters.Count > 0)
        {
            // FC-03: most recent semester by default
            var latest = semesters[0];
            results = all.Where(r => r.EvaluationPeriod.SemesterId == latest.Id).ToList();
            ViewBag.DefaultedToLatest = true;
            ViewBag.LatestSemester = latest;
        }

        var plans = await _db.DevelopmentPlans
            .Where(d => d.FacultyId == userId)
            .ToDictionaryAsync(d => d.EvaluationPeriodId);

        ViewBag.Plans = plans;
        ViewBag.Semesters = semesters;
        ViewBag.Search = search;
        ViewBag.SelectedSemesterId = semesterId;
        ViewBag.TotalRecords = all.Count;
        return View(results);
    }

    /// <summary>
    /// FC-02: the faculty member acknowledges the FEDAF. Replaces the previous
    /// "Save" action, which let faculty enter their own Development Plan (FC-01).
    /// </summary>
    [Authorize(Roles = "Faculty,ProgramChair")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Acknowledge(int evaluationPeriodId)
    {
        var userId = _userManager.GetUserId(User);

        var plan = await _db.DevelopmentPlans
            .FirstOrDefaultAsync(d => d.EvaluationPeriodId == evaluationPeriodId && d.FacultyId == userId);

        if (plan is null || plan.Status == FedafStatus.Draft)
        {
            TempData["Error"] = "This FEDAF cannot be acknowledged yet — your supervisor has not "
                              + "completed the Development Plan.";
            return RedirectToAction("Index");
        }

        if (plan.IsAcknowledged)
        {
            TempData["Error"] = "You have already acknowledged this FEDAF.";
            return RedirectToAction("Index");
        }

        plan.AcknowledgedAt = DateTime.Now;
        plan.AcknowledgedById = userId;
        await _db.SaveChangesAsync();

        TempData["Success"] = "FEDAF acknowledged. Your acknowledgement has been recorded with "
                            + $"today's date ({plan.AcknowledgedAt:MMMM dd, yyyy}).";
        return RedirectToAction("Index");
    }

    // PC-02: supervisors manage the Development Plans of the people they evaluate.
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
            // Open on the most recent period that actually has results to work with.
            // The current period has none until AA Staff closes it (AAS-03), so
            // defaulting to it would show an empty screen.
            var periodsWithResults = await _db.EvaluationResults
                .Select(r => r.EvaluationPeriodId)
                .Distinct()
                .ToListAsync();

            periodId = periods.FirstOrDefault(p => periodsWithResults.Contains(p.Id))?.Id
                       ?? periods.First().Id;
        }

        ViewBag.SelectedPeriodId = periodId;

        var facultyIds = periodId.HasValue
            ? await GetSupervisedFacultyIdsAsync(user!, roles, periodId.Value)
            : [];

        var results = periodId.HasValue && facultyIds.Count > 0
            ? await _db.EvaluationResults
                .Include(r => r.Faculty).ThenInclude(f => f.College)
                .Include(r => r.Faculty).ThenInclude(f => f.Program)
                .Include(r => r.EvaluationPeriod).ThenInclude(p => p.Semester)
                .Where(r => r.EvaluationPeriodId == periodId.Value && facultyIds.Contains(r.FacultyId))
                .OrderByDescending(r => r.StudentRating)
                .ToListAsync()
            : [];

        var plans = periodId.HasValue
            ? await _db.DevelopmentPlans
                .Where(d => d.EvaluationPeriodId == periodId.Value)
                .ToDictionaryAsync(d => d.FacultyId)
            : new Dictionary<string, DevelopmentPlan>();

        ViewBag.Plans = plans;
        return View(results);
    }

    [Authorize(Roles = "Dean,ProgramChair,AA")]
    [HttpGet]
    public async Task<IActionResult> Review(int periodId, string facultyId)
    {
        var user = await _userManager.GetUserAsync(User);
        var roles = await _userManager.GetRolesAsync(user!);

        if (!await CanSuperviseAsync(user!, roles, facultyId, periodId))
            return Forbid();

        var result = await _db.EvaluationResults
            .Include(r => r.Faculty).ThenInclude(f => f.College)
            .Include(r => r.Faculty).ThenInclude(f => f.Program)
            .Include(r => r.EvaluationPeriod).ThenInclude(p => p.Semester)
            .FirstOrDefaultAsync(r => r.EvaluationPeriodId == periodId && r.FacultyId == facultyId);

        if (result is null) return NotFound();

        var plan = await _db.DevelopmentPlans
            .FirstOrDefaultAsync(d => d.EvaluationPeriodId == periodId && d.FacultyId == facultyId);

        ViewBag.Result = result;
        return View(plan ?? new DevelopmentPlan { EvaluationPeriodId = periodId, FacultyId = facultyId });
    }

    /// <summary>
    /// PC-02: the supervisor enters all three sections of the Development Plan.
    /// Faculty are restricted from entering any of them (FC-01).
    /// </summary>
    [Authorize(Roles = "Dean,ProgramChair,AA")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(int evaluationPeriodId, string facultyId,
        string? areasForImprovement, string? proposedActivities, string? actionPlan)
    {
        var user = await _userManager.GetUserAsync(User);
        var roles = await _userManager.GetRolesAsync(user!);

        if (!await CanSuperviseAsync(user!, roles, facultyId, evaluationPeriodId))
            return Forbid();

        var plan = await _db.DevelopmentPlans
            .FirstOrDefaultAsync(d => d.EvaluationPeriodId == evaluationPeriodId && d.FacultyId == facultyId);

        if (plan is not null && plan.IsAcknowledged)
        {
            TempData["Error"] = "This FEDAF has already been acknowledged by the faculty member "
                              + "and can no longer be changed.";
            return RedirectToAction("Manage", new { periodId = evaluationPeriodId });
        }

        if (plan is null)
        {
            plan = new DevelopmentPlan
            {
                EvaluationPeriodId = evaluationPeriodId,
                FacultyId = facultyId
            };
            _db.DevelopmentPlans.Add(plan);
        }

        plan.AreasForImprovement = areasForImprovement;
        plan.ProposedActivities = proposedActivities;
        plan.ActionPlan = actionPlan;
        plan.SupervisorSubmittedAt = DateTime.Now;
        plan.SupervisorId = user!.Id;
        await _db.SaveChangesAsync();

        TempData["Success"] = "Development Plan saved. The FEDAF is now available to the faculty "
                            + "member for acknowledgement.";
        return RedirectToAction("Manage", new { periodId = evaluationPeriodId });
    }

    /// <summary>
    /// Who this supervisor is responsible for. Delegates to the same service the
    /// evaluation rosters use, so the two screens can never disagree.
    /// </summary>
    private Task<List<string>> GetSupervisedFacultyIdsAsync(
        ApplicationUser user, IList<string> roles, int periodId)
        => _assignments.GetSupervisedIdsAsync(user, roles, periodId);

    private async Task<bool> CanSuperviseAsync(
        ApplicationUser user, IList<string> roles, string facultyId, int periodId)
    {
        var ids = await GetSupervisedFacultyIdsAsync(user, roles, periodId);
        return ids.Contains(facultyId);
    }
}
