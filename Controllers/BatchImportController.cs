using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FacultyEvalSystem.Data;
using FacultyEvalSystem.Models;
using FacultyEvalSystem.Services;

namespace FacultyEvalSystem.Controllers;

/// <summary>
/// Faculty and Student imports. The Student Import is the Administrator's; the
/// Faculty Workload Summary is also imported by CDI and CDI Staff, from their own
/// Workload Summary page.
/// </summary>
[Authorize(Roles = "Admin,AA,AAStaff")]
public class BatchImportController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly DataImportService _import;

    public BatchImportController(ApplicationDbContext db, DataImportService import)
    {
        _db = db;
        _import = import;
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Index()
    {
        var active = await _db.Semesters.FirstOrDefaultAsync(s => s.IsActive);
        ViewBag.ActiveSemester = active;
        ViewBag.DefaultPassword = _import.DefaultPassword;
        if (active is not null)
        {
            ViewBag.StudentCourseCount = await _db.StudentCourses.CountAsync(sc => sc.SemesterId == active.Id);
            ViewBag.StudentCount = await _db.StudentCourses.Where(sc => sc.SemesterId == active.Id)
                .Select(sc => sc.StudentId).Distinct().CountAsync();
            ViewBag.AssignmentCount = await _db.FacultySubjects.CountAsync(fs => fs.SemesterId == active.Id);
        }
        return View();
    }

    /// <summary>
    /// Student Import: the Registrar's masterlist of students and their enrolled
    /// Course Codes. Students are matched on Name + Program; new students get an
    /// account with the system's default password.
    /// </summary>
    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> ImportRegistrar(IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Please choose the Registrar's masterlist (.xlsx or .csv).";
            return RedirectToAction(nameof(Index), null, "registrar");
        }

        var semester = await _db.Semesters.FirstOrDefaultAsync(s => s.IsActive);
        if (semester is null)
        {
            TempData["Error"] = "No active semester. Please create one first.";
            return RedirectToAction(nameof(Index), null, "registrar");
        }

        try
        {
            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            var report = await _import.ImportRegistrarMasterlistAsync(stream, file.FileName, semester);
            var link = await _import.LinkEnrollmentsAsync(semester.Id);
            await _import.InferProgramsAsync(semester.Id);

            TempData["Success"] = $"Registrar masterlist imported for {semester.DisplayName}: "
                + $"{report.Created} student account(s) created (default password), {report.Updated} existing student row(s) matched by name and program, "
                + $"{report.CoursesRecorded} enrolled Course Code(s) recorded. "
                + $"{link.EnrollmentsCreated} linked to their instructor; "
                + $"{link.CoursesWithSeveralInstructors} course(s) are taught by several faculty — those students choose their instructor when evaluating.";
            if (report.Problems.Count > 0) TempData["Error"] = report.ProblemSummary();
        }
        catch (InvalidDataException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return RedirectToAction(nameof(Index), null, "registrar");
    }

    /// <summary>The Workload Summary page for CDI and CDI Staff.</summary>
    public async Task<IActionResult> Workload()
    {
        var active = await _db.Semesters.FirstOrDefaultAsync(s => s.IsActive);
        ViewBag.ActiveSemester = active;
        ViewBag.DefaultPassword = _import.DefaultPassword;
        if (active is not null)
            ViewBag.AssignmentCount = await _db.FacultySubjects.CountAsync(fs => fs.SemesterId == active.Id);
        return View();
    }

    /// <summary>The Faculty Workload Summary: faculty, their college and designation, and the Course Codes they teach.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> ImportWorkload(IFormFile file, bool fromAssignments = false)
    {
        // The same import is offered on the Teaching Assignments page and on
        // CDI's Workload Summary page; return to where it was started
        IActionResult Done() => fromAssignments
            ? RedirectToAction("Index", "TeachingAssignments")
            : User.IsInRole("Admin")
                ? RedirectToAction(nameof(Index), null, "workload")
                : RedirectToAction(nameof(Workload));

        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Please choose the Faculty Workload Summary (.xlsx).";
            return Done();
        }

        var semester = await _db.Semesters.FirstOrDefaultAsync(s => s.IsActive);
        if (semester is null)
        {
            TempData["Error"] = "No active semester. Please create one first.";
            return Done();
        }

        try
        {
            await using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            await _import.EnsureCatalogAsync();
            var report = await _import.ImportFacultyWorkloadAsync(stream, semester);
            await _import.InferProgramsAsync(semester.Id);
            var link = await _import.LinkEnrollmentsAsync(semester.Id);

            TempData["Success"] = $"Faculty Workload Summary imported for {semester.DisplayName}: "
                + $"{report.Created} faculty account(s) created (default password), {report.Updated} updated, "
                + $"{report.CoursesRecorded} course assignment(s) recorded; {link.EnrollmentsCreated} student enrollment(s) linked.";
            if (report.Problems.Count > 0) TempData["Error"] = report.ProblemSummary();
        }
        catch (InvalidDataException ex)
        {
            TempData["Error"] = ex.Message;
        }

        return Done();
    }
}
