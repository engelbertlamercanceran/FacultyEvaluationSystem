using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FacultyEvalSystem.Data;
using FacultyEvalSystem.Models;

namespace FacultyEvalSystem.Controllers;

/// <summary>
/// QA-01 / QA-02: the Quality Assurance account sees all written feedback and
/// summary results organised per college.
///
/// Evaluations are anonymous. Nothing on these screens identifies the person who
/// submitted a comment or a rating — only the faculty member it concerns.
/// </summary>
[Authorize(Roles = "QA,CEO,AA")]
public class QualityAssuranceController : Controller
{
    private readonly ApplicationDbContext _db;

    public QualityAssuranceController(ApplicationDbContext db) => _db = db;

    /// <summary>QA-01: all feedback, comments and suggestions submitted through evaluations.</summary>
    public async Task<IActionResult> Feedback(int? periodId, int? collegeId, string? evaluatorType, string? search)
    {
        var periods = await _db.EvaluationPeriods
            .Include(p => p.Semester)
            .OrderByDescending(p => p.Semester.SortKey).ThenByDescending(p => p.Id)
            .ToListAsync();

        ViewBag.Periods = periods;
        ViewBag.Colleges = await _db.Colleges.OrderBy(c => c.Name).ToListAsync();
        ViewBag.SelectedPeriodId = periodId;
        ViewBag.SelectedCollegeId = collegeId;
        ViewBag.SelectedEvaluatorType = evaluatorType;
        ViewBag.Search = search;

        var query = _db.Evaluations
            .Include(e => e.Faculty).ThenInclude(f => f.College)
            .Include(e => e.Faculty).ThenInclude(f => f.Program)
            .Include(e => e.EvaluationPeriod).ThenInclude(p => p.Semester)
            .Include(e => e.FacultySubject)
            // Only evaluations that actually carry written feedback
            .Where(e => e.Comments != null && e.Comments != "");

        if (periodId.HasValue)
            query = query.Where(e => e.EvaluationPeriodId == periodId.Value);

        if (collegeId.HasValue)
            query = query.Where(e => e.Faculty.CollegeId == collegeId.Value);

        if (!string.IsNullOrWhiteSpace(evaluatorType))
            query = query.Where(e => e.EvaluatorType == evaluatorType);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(e => e.Comments!.Contains(search)
                                  || e.Faculty.FirstName.Contains(search)
                                  || e.Faculty.LastName.Contains(search));

        // Deliberately projected: EvaluatorId is never carried into the view model,
        // so a comment can never be traced back to the student who wrote it.
        var comments = await query
            .OrderByDescending(e => e.SubmittedAt)
            .Select(e => new FeedbackEntry
            {
                FacultyName = e.Faculty.FirstName + " " + e.Faculty.LastName,
                CollegeCode = e.Faculty.College != null ? e.Faculty.College.Code : null,
                ProgramCode = e.Faculty.Program != null ? e.Faculty.Program.Code : null,
                CourseCode = e.FacultySubject != null ? e.FacultySubject.CourseCode : null,
                Section = e.FacultySubject != null ? e.FacultySubject.Section : null,
                EvaluatorType = e.EvaluatorType,
                Semester = e.EvaluationPeriod.Semester.Term + ", A.Y. " + e.EvaluationPeriod.Semester.AcademicYear,
                PeriodName = e.EvaluationPeriod.Name,
                SubmittedAt = e.SubmittedAt,
                Comment = e.Comments!
            })
            .Take(500)
            .ToListAsync();

        ViewBag.TotalWithComments = await query.CountAsync();
        return View(comments);
    }

    /// <summary>QA-02: summary results organised per college.</summary>
    public async Task<IActionResult> CollegeSummary(int? periodId)
    {
        var periods = await _db.EvaluationPeriods
            .Include(p => p.Semester)
            .OrderByDescending(p => p.Semester.SortKey).ThenByDescending(p => p.Id)
            .ToListAsync();

        ViewBag.Periods = periods;

        periodId ??= periods.FirstOrDefault()?.Id;
        ViewBag.SelectedPeriodId = periodId;

        var results = periodId.HasValue
            ? await _db.EvaluationResults
                .Include(r => r.Faculty).ThenInclude(f => f.College)
                .Include(r => r.Faculty).ThenInclude(f => f.Program)
                .Where(r => r.EvaluationPeriodId == periodId.Value)
                .ToListAsync()
            : [];

        var colleges = await _db.Colleges.OrderBy(c => c.Name).ToListAsync();

        // All colleges are listed alphabetically, even one with no results yet
        var buckets = colleges
            .Select(c => (Name: c.Name, Code: c.Code, Items: results.Where(r => r.Faculty.CollegeId == c.Id).ToList()))
            .ToList();

        var unassigned = results.Where(r => r.Faculty.CollegeId is null).ToList();
        if (unassigned.Count > 0) buckets.Add(("Unassigned college", "—", unassigned));

        var groups = buckets
            .Select(b => new CollegeSummaryGroup
            {
                CollegeName = b.Name,
                CollegeCode = b.Code,
                FacultyCount = b.Items.Count,
                AverageSet = b.Items.Any(r => r.StudentRespondents > 0)
                    ? Math.Round(b.Items.Where(r => r.StudentRespondents > 0).Average(r => r.StudentRating), 2) : 0,
                AverageSef = b.Items.Any(r => r.SupervisorRespondents > 0)
                    ? Math.Round(b.Items.Where(r => r.SupervisorRespondents > 0).Average(r => r.SupervisorRating), 2) : 0,
                TotalStudentRespondents = b.Items.Sum(r => r.StudentRespondents),
                TotalSupervisorRespondents = b.Items.Sum(r => r.SupervisorRespondents),
                Faculty = b.Items.OrderBy(r => r.Faculty.LastName).ThenBy(r => r.Faculty.FirstName).ToList()
            })
            .ToList();

        return View(groups);
    }

    public class FeedbackEntry
    {
        public string FacultyName { get; set; } = "";
        public string? CollegeCode { get; set; }
        public string? ProgramCode { get; set; }
        public string? CourseCode { get; set; }
        public string? Section { get; set; }
        public string EvaluatorType { get; set; } = "";
        public string Semester { get; set; } = "";
        public string PeriodName { get; set; } = "";
        public DateTime SubmittedAt { get; set; }
        public string Comment { get; set; } = "";
    }

    public class CollegeSummaryGroup
    {
        public string CollegeName { get; set; } = "";
        public string CollegeCode { get; set; } = "";
        public int FacultyCount { get; set; }
        public double AverageSet { get; set; }
        public double AverageSef { get; set; }
        public int TotalStudentRespondents { get; set; }
        public int TotalSupervisorRespondents { get; set; }
        public List<EvaluationResult> Faculty { get; set; } = [];
    }
}
