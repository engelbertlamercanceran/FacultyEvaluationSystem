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
public class DashboardController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly EvaluationService _evalService;

    public DashboardController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, EvaluationService evalService)
    {
        _db = db;
        _userManager = userManager;
        _evalService = evalService;
    }

    // Admin/Dean/ProgramChair dashboard
    [Authorize(Roles = "Admin,CEO,Dean,ProgramChair,AA,AAStaff,QA")]
    public async Task<IActionResult> Index()
    {
        var activeSemester = await _db.Semesters.FirstOrDefaultAsync(s => s.IsActive);
        var activePeriod = await _db.EvaluationPeriods
            .Include(p => p.Semester)
            .FirstOrDefaultAsync(p => p.Status == EvaluationStatus.Open);

        var latestPeriod = activePeriod ?? await _db.EvaluationPeriods
            .OrderByDescending(p => p.Id)
            .FirstOrDefaultAsync();

        var results = latestPeriod is not null
            ? await _db.EvaluationResults
                .Include(r => r.Faculty).ThenInclude(f => f.College)
                .Where(r => r.EvaluationPeriodId == latestPeriod.Id)
                .ToListAsync()
            : [];

        // College performance (based on SET rating)
        var collegePerf = results
            .Where(r => r.Faculty.College is not null)
            .GroupBy(r => r.Faculty.College!)
            .Select(g => new CollegePerformance
            {
                CollegeName = g.Key.Name,
                CollegeCode = g.Key.Code,
                AverageRating = Math.Round(g.Average(r => r.StudentRating), 2),
                FacultyCount = g.Count()
            })
            .OrderByDescending(c => c.AverageRating)
            .ToList();

        // Semester trends
        var trends = await _db.EvaluationResults
            .Include(r => r.EvaluationPeriod).ThenInclude(p => p.Semester)
            .GroupBy(r => new { r.EvaluationPeriod.Semester.AcademicYear, r.EvaluationPeriod.Semester.Term })
            .Select(g => new SemesterTrend
            {
                Semester = g.Key.Term + ", " + g.Key.AcademicYear,
                AverageRating = Math.Round(g.Average(r => r.StudentRating), 2),
                TotalEvaluations = g.Sum(r => r.StudentRespondents + r.SupervisorRespondents)
            })
            .ToListAsync();

        var students = await _userManager.GetUsersInRoleAsync("Student");
        var faculty = await _userManager.GetUsersInRoleAsync("Faculty");

        var model = new AdminDashboardViewModel
        {
            TotalStudents = students.Count,
            TotalFaculty = faculty.Count,
            TotalColleges = await _db.Colleges.CountAsync(),
            TotalEvaluations = await _db.Evaluations.CountAsync(),
            ActiveSemester = activeSemester,
            ActivePeriod = activePeriod,
            TopPerformers = results.OrderByDescending(r => r.StudentRating).Take(5).ToList(),
            LowPerformers = results.Where(r => r.StudentRating > 0 && r.StudentRating < 60).OrderBy(r => r.StudentRating).Take(5).ToList(),
            CollegePerformances = collegePerf,
            SemesterTrends = trends
        };

        return View(model);
    }

    // Faculty personal dashboard
    [Authorize(Roles = "Faculty")]
    public async Task<IActionResult> Faculty()
    {
        var user = await _userManager.GetUserAsync(User);

        var history = await _db.EvaluationResults
            .Include(r => r.EvaluationPeriod).ThenInclude(p => p.Semester)
            .Where(r => r.FacultyId == user!.Id)
            .OrderByDescending(r => r.EvaluationPeriod.Semester.AcademicYear)
            .ToListAsync();

        var latest = history.FirstOrDefault();
        var breakdown = new Dictionary<string, double>();

        if (latest is not null)
        {
            breakdown = await _evalService.GetCategoryBreakdownAsync(
                latest.EvaluationPeriodId, user!.Id, "Student");
        }

        var model = new FacultyDashboardViewModel
        {
            FacultyName = user!.FullName,
            LatestResult = latest,
            History = history,
            CategoryBreakdown = breakdown
        };

        return View(model);
    }

    // API endpoint for chart data
    [Authorize(Roles = "Admin,CEO,Dean,ProgramChair,AA,AAStaff,QA")]
    [HttpGet]
    public async Task<IActionResult> ChartData(string type)
    {
        switch (type)
        {
            case "college":
                var latestPeriod = await _db.EvaluationPeriods.OrderByDescending(p => p.Id).FirstOrDefaultAsync();
                if (latestPeriod is null) return Json(new { labels = Array.Empty<string>(), data = Array.Empty<double>() });

                var collegeData = await _db.EvaluationResults
                    .Include(r => r.Faculty).ThenInclude(f => f.College)
                    .Where(r => r.EvaluationPeriodId == latestPeriod.Id && r.Faculty.College != null)
                    .GroupBy(r => r.Faculty.College!.Code)
                    .Select(g => new { Label = g.Key, Avg = Math.Round(g.Average(r => r.StudentRating), 2) })
                    .ToListAsync();

                return Json(new { labels = collegeData.Select(c => c.Label), data = collegeData.Select(c => c.Avg) });

            case "trend":
                var allResults = await _db.EvaluationResults
                    .Include(r => r.EvaluationPeriod).ThenInclude(p => p.Semester)
                    .ToListAsync();

                var trendData = allResults
                    .GroupBy(r => new { r.EvaluationPeriod.SemesterId, r.EvaluationPeriod.Semester.Term, r.EvaluationPeriod.Semester.AcademicYear })
                    .OrderBy(g => g.Key.AcademicYear).ThenBy(g => g.Key.Term)
                    .Select(g => new { Label = g.Key.Term + " " + g.Key.AcademicYear, Avg = Math.Round(g.Average(r => r.StudentRating), 2) })
                    .ToList();

                return Json(new { labels = trendData.Select(t => t.Label), data = trendData.Select(t => t.Avg) });

            default:
                return Json(new { });
        }
    }
}
