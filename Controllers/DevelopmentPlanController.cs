using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FacultyEvalSystem.Data;
using FacultyEvalSystem.Models;

namespace FacultyEvalSystem.Controllers;

[Authorize]
public class DevelopmentPlanController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public DevelopmentPlanController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    // Faculty: view own evaluation results with IFER/FEDAF links + development plan forms
    [Authorize(Roles = "Faculty")]
    public async Task<IActionResult> Index()
    {
        var userId = _userManager.GetUserId(User);

        var results = await _db.EvaluationResults
            .Include(r => r.EvaluationPeriod).ThenInclude(p => p.Semester)
            .Where(r => r.FacultyId == userId)
            .OrderByDescending(r => r.EvaluationPeriod.Semester.AcademicYear)
            .ToListAsync();

        var plans = await _db.DevelopmentPlans
            .Where(d => d.FacultyId == userId)
            .ToDictionaryAsync(d => d.EvaluationPeriodId);

        ViewBag.Plans = plans;
        return View(results);
    }

    // Faculty: view/edit their development plan for a specific period
    [Authorize(Roles = "Faculty")]
    [HttpGet]
    public async Task<IActionResult> Edit(int periodId)
    {
        var userId = _userManager.GetUserId(User);

        var result = await _db.EvaluationResults
            .Include(r => r.Faculty).ThenInclude(f => f.College)
            .Include(r => r.EvaluationPeriod).ThenInclude(p => p.Semester)
            .FirstOrDefaultAsync(r => r.EvaluationPeriodId == periodId && r.FacultyId == userId);

        if (result is null) return NotFound("No evaluation results found for this period.");

        var plan = await _db.DevelopmentPlans
            .FirstOrDefaultAsync(d => d.EvaluationPeriodId == periodId && d.FacultyId == userId);

        ViewBag.Result = result;
        return View(plan ?? new DevelopmentPlan { EvaluationPeriodId = periodId, FacultyId = userId! });
    }

    // Faculty: save areas for improvement
    [Authorize(Roles = "Faculty")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int evaluationPeriodId, string areasForImprovement)
    {
        var period = await _db.EvaluationPeriods.FindAsync(evaluationPeriodId);
        if (period?.Status == EvaluationStatus.Closed)
        {
            TempData["Error"] = "This evaluation period is closed. Development plans can no longer be modified.";
            return RedirectToAction("Index");
        }

        var userId = _userManager.GetUserId(User);

        var plan = await _db.DevelopmentPlans
            .FirstOrDefaultAsync(d => d.EvaluationPeriodId == evaluationPeriodId && d.FacultyId == userId);

        if (plan is null)
        {
            plan = new DevelopmentPlan
            {
                EvaluationPeriodId = evaluationPeriodId,
                FacultyId = userId!
            };
            _db.DevelopmentPlans.Add(plan);
        }

        plan.AreasForImprovement = areasForImprovement;
        plan.FacultySubmittedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        TempData["Success"] = "Areas for improvement saved.";
        return RedirectToAction("Index");
    }

    // Dean/ProgramChair: list faculty development plans in their college
    [Authorize(Roles = "Dean,ProgramChair")]
    public async Task<IActionResult> Manage(int? periodId)
    {
        var user = await _userManager.GetUserAsync(User);
        var periods = await _db.EvaluationPeriods
            .Include(p => p.Semester)
            .OrderByDescending(p => p.Id)
            .ToListAsync();

        ViewBag.Periods = periods;
        ViewBag.SelectedPeriodId = periodId;

        if (!periodId.HasValue && periods.Any())
            periodId = periods.First().Id;

        var results = periodId.HasValue
            ? await _db.EvaluationResults
                .Include(r => r.Faculty).ThenInclude(f => f.College)
                .Include(r => r.EvaluationPeriod).ThenInclude(p => p.Semester)
                .Where(r => r.EvaluationPeriodId == periodId.Value && r.Faculty.CollegeId == user!.CollegeId)
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

    // Dean/ProgramChair: edit proposed activities and action plan
    [Authorize(Roles = "Dean,ProgramChair")]
    [HttpGet]
    public async Task<IActionResult> Review(int periodId, string facultyId)
    {
        var user = await _userManager.GetUserAsync(User);

        var result = await _db.EvaluationResults
            .Include(r => r.Faculty).ThenInclude(f => f.College)
            .Include(r => r.EvaluationPeriod).ThenInclude(p => p.Semester)
            .FirstOrDefaultAsync(r => r.EvaluationPeriodId == periodId && r.FacultyId == facultyId);

        if (result is null) return NotFound();

        // Ensure supervisor can only review faculty in their college
        if (result.Faculty.CollegeId != user!.CollegeId)
            return Forbid();

        var plan = await _db.DevelopmentPlans
            .FirstOrDefaultAsync(d => d.EvaluationPeriodId == periodId && d.FacultyId == facultyId);

        ViewBag.Result = result;
        return View(plan ?? new DevelopmentPlan { EvaluationPeriodId = periodId, FacultyId = facultyId });
    }

    // Dean/ProgramChair: save proposed activities and action plan
    [Authorize(Roles = "Dean,ProgramChair")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(int evaluationPeriodId, string facultyId, string? proposedActivities, string? actionPlan)
    {
        var period = await _db.EvaluationPeriods.FindAsync(evaluationPeriodId);
        if (period?.Status == EvaluationStatus.Closed)
        {
            TempData["Error"] = "This evaluation period is closed. Development plans can no longer be modified.";
            return RedirectToAction("Manage", new { periodId = evaluationPeriodId });
        }

        var plan = await _db.DevelopmentPlans
            .FirstOrDefaultAsync(d => d.EvaluationPeriodId == evaluationPeriodId && d.FacultyId == facultyId);

        if (plan is null)
        {
            plan = new DevelopmentPlan
            {
                EvaluationPeriodId = evaluationPeriodId,
                FacultyId = facultyId
            };
            _db.DevelopmentPlans.Add(plan);
        }

        plan.ProposedActivities = proposedActivities;
        plan.ActionPlan = actionPlan;
        plan.SupervisorSubmittedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Development plan updated.";
        return RedirectToAction("Manage", new { periodId = evaluationPeriodId });
    }
}
