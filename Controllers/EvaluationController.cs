using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FacultyEvalSystem.Data;
using FacultyEvalSystem.Models;
using FacultyEvalSystem.Services;
using FacultyEvalSystem.ViewModels;

namespace FacultyEvalSystem.Controllers;

[Authorize]
public class EvaluationController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly EvaluationService _evalService;

    public EvaluationController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, EvaluationService evalService)
    {
        _db = db;
        _userManager = userManager;
        _evalService = evalService;
    }

    // Student: list faculty to evaluate
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        var activePeriod = await _db.EvaluationPeriods
            .Include(p => p.Semester)
            .FirstOrDefaultAsync(p => p.Status == EvaluationStatus.Open);

        if (activePeriod is null)
            return View(new EvaluationListViewModel { ActivePeriod = null });

        // Get faculty-subjects the student is enrolled in
        var enrollments = await _db.StudentEnrollments
            .Include(se => se.FacultySubject)
                .ThenInclude(fs => fs.Faculty)
            .Where(se => se.StudentId == user!.Id && se.FacultySubject.SemesterId == activePeriod.SemesterId)
            .ToListAsync();

        // Check which ones are already evaluated
        var existingEvals = await _db.Evaluations
            .Where(e => e.EvaluatorId == user!.Id && e.EvaluationPeriodId == activePeriod.Id)
            .ToListAsync();

        var facultyList = enrollments.Select(e =>
        {
            var myEval = existingEvals.FirstOrDefault(x => x.FacultyId == e.FacultySubject.FacultyId && x.FacultySubjectId == e.FacultySubjectId);
            return new FacultyToEvaluateViewModel
            {
                FacultyId = e.FacultySubject.FacultyId,
                FacultyName = e.FacultySubject.Faculty.FullName,
                FacultySubjectId = e.FacultySubjectId,
                SubjectCode = e.FacultySubject.SubjectCode,
                SubjectName = e.FacultySubject.SubjectName,
                Section = e.FacultySubject.Section,
                AlreadyEvaluated = myEval is not null,
                EvaluationId = myEval?.Id
            };
        }).ToList();

        return View(new EvaluationListViewModel
        {
            ActivePeriod = activePeriod,
            FacultyList = facultyList
        });
    }

    // Show evaluation form
    [HttpGet]
    public async Task<IActionResult> Evaluate(string facultyId, int periodId, int? subjectId)
    {
        var user = await _userManager.GetUserAsync(User);
        var roles = await _userManager.GetRolesAsync(user!);
        var evaluatorType = roles.Contains("Student") ? "Student" : "Supervisor";

        // Check if already evaluated
        var exists = await _db.Evaluations.AnyAsync(e =>
            e.EvaluatorId == user!.Id &&
            e.FacultyId == facultyId &&
            e.FacultySubjectId == subjectId &&
            e.EvaluationPeriodId == periodId);

        if (exists)
        {
            TempData["Error"] = "You have already submitted an evaluation for this faculty member.";
            return RedirectToAction("Index");
        }

        var faculty = await _userManager.FindByIdAsync(facultyId);
        var categories = await _db.EvaluationCategories
            .Include(c => c.Criteria.OrderBy(cr => cr.SortOrder))
            .Where(c => c.EvaluatorType == evaluatorType)
            .OrderBy(c => c.SortOrder)
            .ToListAsync();

        var subject = subjectId.HasValue
            ? await _db.FacultySubjects.FindAsync(subjectId.Value)
            : null;

        var model = new EvaluationFormViewModel
        {
            EvaluationPeriodId = periodId,
            FacultyId = facultyId,
            FacultyName = faculty?.FullName ?? "",
            FacultySubjectId = subjectId,
            SubjectName = subject is not null ? $"{subject.SubjectCode} - {subject.SubjectName}" : null,
            EvaluatorType = evaluatorType,
            Categories = categories.Select(c => new CategoryViewModel
            {
                CategoryId = c.Id,
                CategoryName = c.Name,
                Criteria = c.Criteria.Select(cr => new CriterionViewModel
                {
                    CriterionId = cr.Id,
                    Description = cr.Description
                }).ToList()
            }).ToList()
        };

        return View(model);
    }

    // Submit evaluation
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Evaluate(EvaluationFormViewModel model)
    {
        var user = await _userManager.GetUserAsync(User);

        // Validate all ratings are provided
        var allCriteria = model.Categories.SelectMany(c => c.Criteria).ToList();
        if (allCriteria.Any(c => c.Rating < 1 || c.Rating > 5))
        {
            ModelState.AddModelError("", "Please provide a rating for all criteria.");
            return View(model);
        }

        var evaluation = new Evaluation
        {
            EvaluationPeriodId = model.EvaluationPeriodId,
            FacultyId = model.FacultyId,
            EvaluatorId = user!.Id,
            EvaluatorType = model.EvaluatorType,
            FacultySubjectId = model.FacultySubjectId,
            Comments = model.Comments,
            Responses = allCriteria.Select(c => new EvaluationResponse
            {
                CriterionId = c.CriterionId,
                Rating = c.Rating
            }).ToList()
        };

        _db.Evaluations.Add(evaluation);
        await _db.SaveChangesAsync();

        // Re-compute results for this faculty
        await _evalService.ComputeFacultyResultAsync(model.EvaluationPeriodId, model.FacultyId);

        TempData["Success"] = "Evaluation submitted successfully. Thank you!";
        return model.EvaluatorType == "Supervisor"
            ? RedirectToAction("SupervisorEvaluate")
            : RedirectToAction("Index");
    }

    // Supervisor: list faculty to evaluate
    [Authorize(Roles = "Dean,ProgramChair")]
    public async Task<IActionResult> SupervisorEvaluate()
    {
        var user = await _userManager.GetUserAsync(User);
        var activePeriod = await _db.EvaluationPeriods
            .Include(p => p.Semester)
            .FirstOrDefaultAsync(p => p.Status == EvaluationStatus.Open);

        if (activePeriod is null)
            return View(new EvaluationListViewModel { ActivePeriod = null });

        // Get faculty in same college
        var facultyRole = await _db.Roles.FirstAsync(r => r.Name == "Faculty");
        var facultyUsers = await _db.Users
            .Where(u => u.CollegeId == user!.CollegeId && u.IsActive)
            .ToListAsync();

        var facultyInRole = new List<ApplicationUser>();
        foreach (var f in facultyUsers)
        {
            if (await _userManager.IsInRoleAsync(f, "Faculty"))
                facultyInRole.Add(f);
        }

        var existingEvals = await _db.Evaluations
            .Where(e => e.EvaluatorId == user!.Id && e.EvaluationPeriodId == activePeriod.Id)
            .ToListAsync();

        var facultyList = facultyInRole.Select(f =>
        {
            var myEval = existingEvals.FirstOrDefault(x => x.FacultyId == f.Id);
            return new FacultyToEvaluateViewModel
            {
                FacultyId = f.Id,
                FacultyName = f.FullName,
                AlreadyEvaluated = myEval is not null,
                EvaluationId = myEval?.Id
            };
        }).ToList();

        return View("Index", new EvaluationListViewModel
        {
            ActivePeriod = activePeriod,
            FacultyList = facultyList
        });
    }

    [HttpGet]
    public async Task<IActionResult> MyRating(int evaluationId)
    {
        var user = await _userManager.GetUserAsync(User);
        var eval = await _db.Evaluations
            .Include(e => e.Responses)
                .ThenInclude(r => r.Criterion)
                    .ThenInclude(cr => cr.Category)
            .Include(e => e.Faculty)
            .FirstOrDefaultAsync(e => e.Id == evaluationId && e.EvaluatorId == user!.Id);

        if (eval is null) return NotFound();

        var categories = eval.Responses
            .GroupBy(r => r.Criterion.Category)
            .OrderBy(g => g.Key.SortOrder)
            .Select(g => new {
                name = g.Key.Name,
                average = g.Average(r => (double)r.Rating)
            }).ToList();

        var overall = eval.Responses.Average(r => (double)r.Rating);

        return Json(new {
            facultyName = eval.Faculty.FullName,
            categories,
            overall,
            description = GetDescriptiveRating(overall)
        });
    }

    private static string GetDescriptiveRating(double rating) =>
        rating >= 4.5 ? "Outstanding" :
        rating >= 3.5 ? "Very Satisfactory" :
        rating >= 2.5 ? "Satisfactory" :
        rating >= 1.5 ? "Fair" : "Poor";
}
