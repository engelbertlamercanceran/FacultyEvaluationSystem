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
    private readonly EvaluatorAssignmentService _assignments;

    // DA-01: the dashboard covers the five most recent semesters
    private const int FiveSemesters = 5;

    /// <summary>
    /// A faculty member with no student respondents has no SET rating, and their
    /// stored 0 is "not measured", not "scored zero". Program Chairs are the usual
    /// case: they are evaluated by their Dean (SEF) but teach no classes, so
    /// averaging their 0 in would understate every college.
    /// </summary>
    private static double AverageSet(IEnumerable<EvaluationResult> results)
    {
        var rated = results.Where(r => r.StudentRespondents > 0).ToList();
        return rated.Count > 0 ? Math.Round(rated.Average(r => r.StudentRating), 2) : 0;
    }

    private static double? AverageSetOrNull(IEnumerable<EvaluationResult> results)
    {
        var rated = results.Where(r => r.StudentRespondents > 0).ToList();
        return rated.Count > 0 ? Math.Round(rated.Average(r => r.StudentRating), 2) : null;
    }

    /// <summary>
    /// The most recent evaluation period that actually holds computed results.
    ///
    /// Results are only computed when AA Staff closes a period (AAS-03), so the
    /// period currently being evaluated has none. Anything that reports on results
    /// must use this rather than the active or highest-numbered period, or it
    /// renders empty for the entire duration of an evaluation.
    /// </summary>
    private async Task<EvaluationPeriod?> GetLatestPeriodWithResultsAsync()
        => await _db.EvaluationPeriods
            .Include(p => p.Semester)
            .Where(p => _db.EvaluationResults.Any(r => r.EvaluationPeriodId == p.Id))
            .OrderByDescending(p => p.Semester.SortKey)
            .ThenByDescending(p => p.Id)
            .FirstOrDefaultAsync();

    public DashboardController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, EvaluationService evalService,
        EvaluatorAssignmentService assignments)
    {
        _db = db;
        _userManager = userManager;
        _evalService = evalService;
        _assignments = assignments;
    }

    /// <summary>
    /// Program Chairs and Deans see only the faculty assigned to them: a Chair, the
    /// faculty of their program and those teaching its courses (PC-01); a Dean, the
    /// faculty and Program Chairs of their college. Null for everyone else, who see
    /// the whole institution.
    /// </summary>
    private async Task<HashSet<string>?> AssignedFacultyAsync()
    {
        if (!User.IsInRole("ProgramChair") && !User.IsInRole("Dean")) return null;

        var user = await _userManager.GetUserAsync(User);
        var roles = await _userManager.GetRolesAsync(user!);

        // A Chair's cross-department faculty come from the current semester's teaching load
        var periodId = await _db.EvaluationPeriods
            .Where(p => p.Semester.IsActive)
            .OrderByDescending(p => p.Id)
            .Select(p => (int?)p.Id)
            .FirstOrDefaultAsync()
            ?? (await GetLatestPeriodWithResultsAsync())?.Id
            ?? 0;

        return (await _assignments.GetDevelopmentPlanScopeAsync(user!, roles, periodId)).ToHashSet();
    }

    // Admin/Dean/ProgramChair dashboard
    [Authorize(Roles = "Admin,CEO,Dean,ProgramChair,AA,AAStaff,QA")]
    public async Task<IActionResult> Index()
    {
        var activeSemester = await _db.Semesters.FirstOrDefaultAsync(s => s.IsActive);
        var activePeriod = await _db.EvaluationPeriods
            .Include(p => p.Semester)
            .FirstOrDefaultAsync(p => p.Stage != EvaluationStage.NotStarted && p.Stage != EvaluationStage.Closed);

        // Results-based panels must key off the most recent period that HAS results,
        // not the active one. A period holds no results until AA Staff closes it
        // (AAS-03), so keying off the active period left these panels empty for the
        // whole time an evaluation was actually running.
        var resultsPeriod = await GetLatestPeriodWithResultsAsync();
        var assigned = await AssignedFacultyAsync();

        var results = resultsPeriod is not null
            ? await _db.EvaluationResults
                .Include(r => r.Faculty).ThenInclude(f => f.College)
                .Where(r => r.EvaluationPeriodId == resultsPeriod.Id)
                .ToListAsync()
            : [];
        if (assigned is not null)
            results = results.Where(r => assigned.Contains(r.FacultyId)).ToList();

        // College performance (based on SET rating) — all nine colleges, alphabetically
        var collegePerf = (await _db.Colleges.OrderBy(c => c.Name).ToListAsync())
            .Select(c =>
            {
                var g = results.Where(r => r.Faculty.CollegeId == c.Id).ToList();
                return new CollegePerformance
                {
                    CollegeName = c.Name,
                    CollegeCode = c.Code,
                    AverageRating = AverageSet(g),
                    FacultyCount = g.Count
                };
            })
            .ToList();

        // DA-01: history is limited to the five most recent semesters
        var recentSemesters = await _db.Semesters
            .OrderByDescending(s => s.SortKey)
            .Take(FiveSemesters)
            .ToListAsync();

        var recentSemesterIds = recentSemesters.Select(s => s.Id).ToList();

        var recentResults = await _db.EvaluationResults
            .Include(r => r.EvaluationPeriod).ThenInclude(p => p.Semester)
            .Include(r => r.Faculty).ThenInclude(f => f.College)
            .Include(r => r.Faculty).ThenInclude(f => f.Program)
            .Where(r => recentSemesterIds.Contains(r.EvaluationPeriod.SemesterId))
            .ToListAsync();
        if (assigned is not null)
            recentResults = recentResults.Where(r => assigned.Contains(r.FacultyId)).ToList();

        var trends = recentResults
            .GroupBy(r => r.EvaluationPeriod.Semester)
            .OrderBy(g => g.Key.SortKey)
            .Select(g => new SemesterTrend
            {
                Semester = g.Key.DisplayName,
                SortKey = g.Key.SortKey,
                AverageRating = AverageSet(g),
                TotalEvaluations = g.Sum(r => r.StudentRespondents + r.SupervisorRespondents)
            })
            .ToList();

        // Semester-over-semester change
        for (int i = 1; i < trends.Count; i++)
            trends[i].ChangeFromPrevious = Math.Round(trends[i].AverageRating - trends[i - 1].AverageRating, 2);

        var students = await _userManager.GetUsersInRoleAsync("Student");
        var faculty = await _userManager.GetUsersInRoleAsync("Faculty");

        var assignedIds = assigned?.ToList() ?? [];

        var model = new AdminDashboardViewModel
        {
            TotalStudents = students.Count,
            TotalFaculty = assigned?.Count ?? faculty.Count,
            TotalColleges = await _db.Colleges.CountAsync(),
            TotalEvaluations = assigned is null
                ? await _db.Evaluations.CountAsync()
                : await _db.Evaluations.CountAsync(e => assignedIds.Contains(e.FacultyId)),
            IsScopedToAssignedFaculty = assigned is not null,
            ActiveSemester = activeSemester,
            ActivePeriod = activePeriod,
            ResultsPeriod = resultsPeriod,
            TopPerformers = results.Where(r => r.StudentRespondents > 0)
                                   .OrderByDescending(r => r.StudentRating).Take(5).ToList(),
            LowPerformers = results.Where(r => r.StudentRating > 0 && r.StudentRating < 60).OrderBy(r => r.StudentRating).Take(5).ToList(),
            CollegePerformances = collegePerf,
            SemesterTrends = trends,
            Comparative = BuildComparativeAnalysis(recentResults, recentSemesters)
        };

        return View(model);
    }

    /// <summary>
    /// DA-01: comparative analysis over the five most recent semesters —
    /// college against college, each program against its own college average,
    /// and each semester against the one before it.
    /// </summary>
    private static ComparativeAnalysis BuildComparativeAnalysis(
        List<EvaluationResult> results, List<Semester> recentSemesters)
    {
        var analysis = new ComparativeAnalysis();
        if (results.Count == 0) return analysis;

        // Oldest first, so the chart reads left to right
        var semesters = recentSemesters.OrderBy(s => s.SortKey).ToList();
        analysis.SemesterLabels = semesters.Select(s => s.DisplayName).ToList();

        // Institution-wide average per semester
        analysis.InstitutionAverages = semesters
            .Select(s =>
            {
                var forSemester = results.Where(r => r.EvaluationPeriod.SemesterId == s.Id).ToList();
                return AverageSetOrNull(forSemester);
            })
            .ToList();

        // One line per college, aligned to the same semester axis
        analysis.CollegeSeries = results
            .Where(r => r.Faculty.College is not null)
            .GroupBy(r => r.Faculty.College!)
            .OrderBy(g => g.Key.Name)
            .Select(g =>
            {
                var values = semesters
                    .Select(s =>
                    {
                        var forSemester = g.Where(r => r.EvaluationPeriod.SemesterId == s.Id).ToList();
                        return AverageSetOrNull(forSemester);
                    })
                    .ToList();

                var present = values.Where(v => v.HasValue).Select(v => v!.Value).ToList();

                return new ComparisonSeries
                {
                    Name = g.Key.Code,
                    Values = values,
                    Change = present.Count >= 2
                        ? Math.Round(present[^1] - present[0], 2)
                        : null
                };
            })
            .ToList();

        // Each program measured against its own college's average, latest semester only
        var latest = semesters.LastOrDefault();
        if (latest is not null)
        {
            var latestResults = results.Where(r => r.EvaluationPeriod.SemesterId == latest.Id).ToList();

            var collegeAverages = latestResults
                .Where(r => r.Faculty.CollegeId is not null)
                .GroupBy(r => r.Faculty.CollegeId!.Value)
                .ToDictionary(g => g.Key, g => AverageSet(g));

            analysis.ProgramComparisons = latestResults
                .Where(r => r.Faculty.Program is not null && r.Faculty.CollegeId is not null
                         && r.StudentRespondents > 0)
                .GroupBy(r => r.Faculty.Program!)
                .Select(g => new ProgramVsCollege
                {
                    ProgramCode = g.Key.Code,
                    ProgramName = g.Key.Name,
                    CollegeCode = g.First().Faculty.College?.Code ?? "—",
                    ProgramAverage = AverageSet(g),
                    CollegeAverage = collegeAverages.GetValueOrDefault(g.First().Faculty.CollegeId!.Value),
                    FacultyCount = g.Count()
                })
                .OrderByDescending(p => p.Difference)
                .ToList();
        }

        return analysis;
    }

    // Faculty personal dashboard
    [Authorize(Roles = "Faculty,ProgramChair")]
    public async Task<IActionResult> Faculty()
    {
        var user = await _userManager.GetUserAsync(User);

        // FC-03: newest first, now ordered reliably via SortKey (SUP-01).
        // DA-01: five most recent semesters.
        var history = await _db.EvaluationResults
            .Include(r => r.EvaluationPeriod).ThenInclude(p => p.Semester)
            .Where(r => r.FacultyId == user!.Id)
            .OrderByDescending(r => r.EvaluationPeriod.Semester.SortKey)
            .ThenByDescending(r => r.EvaluationPeriodId)
            .Take(FiveSemesters)
            .ToListAsync();

        var latest = history.FirstOrDefault();
        var breakdown = new Dictionary<string, double>();

        if (latest is not null)
        {
            breakdown = await _evalService.GetCategoryBreakdownAsync(
                latest.EvaluationPeriodId, user!.Id, "Student");
        }

        // The five most recent semesters up to the current one, whether or not
        // this faculty member has results in them yet
        var current = await _db.Semesters.FirstOrDefaultAsync(s => s.IsActive);
        var trendSemesters = (await _db.Semesters
                .Where(s => current == null || s.SortKey <= current.SortKey)
                .OrderByDescending(s => s.SortKey)
                .Take(FiveSemesters)
                .ToListAsync())
            .OrderBy(s => s.SortKey)
            .ToList();

        var model = new FacultyDashboardViewModel
        {
            FacultyName = user!.FullName,
            LatestResult = latest,
            History = history,
            CategoryBreakdown = breakdown,
            TrendLabels = trendSemesters.Select(s => s.DisplayName).ToList(),
            TrendRatings = trendSemesters
                .Select(s => history.FirstOrDefault(h => h.EvaluationPeriod.SemesterId == s.Id) is { StudentRespondents: > 0 } r
                    ? (double?)r.StudentRating
                    : null)
                .ToList()
        };

        return View(model);
    }

    // API endpoint for chart data
    [Authorize(Roles = "Admin,CEO,Dean,ProgramChair,AA,AAStaff,QA")]
    [HttpGet]
    public async Task<IActionResult> ChartData(string type)
    {
        var assigned = await AssignedFacultyAsync();

        switch (type)
        {
            case "college":
                // Must be a period that has results, not the one being evaluated
                var latestPeriod = await GetLatestPeriodWithResultsAsync();
                if (latestPeriod is null) return Json(new { labels = Array.Empty<string>(), data = Array.Empty<double>() });

                var collegeQuery = _db.EvaluationResults
                    .Include(r => r.Faculty).ThenInclude(f => f.College)
                    .Where(r => r.EvaluationPeriodId == latestPeriod.Id && r.Faculty.College != null
                             && r.StudentRespondents > 0);
                if (assigned is not null)
                    {
                    var assignedIds = assigned.ToList();
                    collegeQuery = collegeQuery.Where(r => assignedIds.Contains(r.FacultyId));
                }

                var collegeData = await collegeQuery
                    .GroupBy(r => r.Faculty.College!.Code)
                    .Select(g => new { Label = g.Key, Avg = Math.Round(g.Average(r => r.StudentRating), 2) })
                    .ToListAsync();

                return Json(new { labels = collegeData.Select(c => c.Label), data = collegeData.Select(c => c.Avg) });

            case "trend":
                // DA-01: five most recent semesters only, ordered chronologically
                var recentIds = await _db.Semesters
                    .OrderByDescending(s => s.SortKey)
                    .Take(FiveSemesters)
                    .Select(s => s.Id)
                    .ToListAsync();

                var allResults = await _db.EvaluationResults
                    .Include(r => r.EvaluationPeriod).ThenInclude(p => p.Semester)
                    .Where(r => recentIds.Contains(r.EvaluationPeriod.SemesterId))
                    .ToListAsync();
                if (assigned is not null)
                    allResults = allResults.Where(r => assigned.Contains(r.FacultyId)).ToList();

                var trendData = allResults
                    .GroupBy(r => r.EvaluationPeriod.Semester)
                    .OrderBy(g => g.Key.SortKey)
                    .Select(g => new { Label = g.Key.DisplayName, Avg = AverageSet(g) })
                    .ToList();

                return Json(new { labels = trendData.Select(t => t.Label), data = trendData.Select(t => t.Avg) });

            case "comparative":
                // DA-01: one line per college across the five most recent semesters
                var semesters = await _db.Semesters
                    .OrderByDescending(s => s.SortKey)
                    .Take(FiveSemesters)
                    .ToListAsync();

                var ids = semesters.Select(s => s.Id).ToList();
                var compResults = await _db.EvaluationResults
                    .Include(r => r.EvaluationPeriod).ThenInclude(p => p.Semester)
                    .Include(r => r.Faculty).ThenInclude(f => f.College)
                    .Include(r => r.Faculty).ThenInclude(f => f.Program)
                    .Where(r => ids.Contains(r.EvaluationPeriod.SemesterId))
                    .ToListAsync();
                if (assigned is not null)
                    compResults = compResults.Where(r => assigned.Contains(r.FacultyId)).ToList();

                var comparative = BuildComparativeAnalysis(compResults, semesters);

                return Json(new
                {
                    labels = comparative.SemesterLabels,
                    institution = comparative.InstitutionAverages,
                    series = comparative.CollegeSeries.Select(s => new { name = s.Name, values = s.Values })
                });

            default:
                return Json(new { });
        }
    }
}
