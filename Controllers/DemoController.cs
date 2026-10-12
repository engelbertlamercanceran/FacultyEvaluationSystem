using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FacultyEvalSystem.Data;
using FacultyEvalSystem.Models;
using FacultyEvalSystem.Services;

namespace FacultyEvalSystem.Controllers;

/// <summary>
/// Demo evaluation generator, triggered from the browser address bar. It is not
/// linked anywhere in the interface (the client removed the button) and is limited
/// to the Administrator.
///
///   GET /Demo/GenerateEvaluations              -> shows what would be generated
///   GET /Demo/GenerateEvaluations?confirm=yes  -> generates it
///
/// The confirm step keeps a stray visit or link from writing data by accident.
/// </summary>
[Authorize(Roles = "Admin")]
public class DemoController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly DemoDataSeeder _seeder;

    public DemoController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, DemoDataSeeder seeder)
    {
        _db = db;
        _userManager = userManager;
        _seeder = seeder;
    }

    [HttpGet]
    public async Task<IActionResult> GenerateEvaluations(string? confirm)
    {
        var periods = await _db.EvaluationPeriods
            .Include(p => p.Semester)
            .OrderBy(p => p.Semester.SortKey).ThenBy(p => p.Id)
            .Select(p => new
            {
                p.Id,
                Semester = p.Semester.AcademicYear + " " + p.Semester.Term,
                Stage = p.Stage.ToString(),
                Assignments = _db.FacultySubjects.Count(fs => fs.SemesterId == p.SemesterId),
                Enrollments = _db.StudentEnrollments.Count(se => se.FacultySubject.SemesterId == p.SemesterId)
            })
            .ToListAsync();

        var faculty = (await _userManager.GetUsersInRoleAsync("Faculty")).Count;
        var students = (await _userManager.GetUsersInRoleAsync("Student")).Count;

        var problem = periods.Count == 0
            ? "No evaluation periods exist. CDI or CDI Staff must create one under Evaluation Schedule first."
            : faculty == 0 || students == 0
                ? "No faculty or students exist. Run Import Faculty and Import Student first."
                : null;

        if (problem is not null)
            return BadRequest(new { ok = false, message = problem, periods });

        if (!string.Equals(confirm, "yes", StringComparison.OrdinalIgnoreCase))
        {
            return Ok(new
            {
                ok = true,
                generated = false,
                message = "Nothing written yet. Add ?confirm=yes to generate. Student evaluations are "
                        + "created for every period with enrollments (about two thirds of students while a "
                        + "period is at the student stage); supervisor evaluations and computed results "
                        + "only for Closed periods. Existing evaluations are kept, so it is safe to re-run.",
                faculty,
                students,
                periods
            });
        }

        var started = DateTime.Now;
        var total = await _seeder.GenerateEvaluationsAsync();

        return Ok(new
        {
            ok = true,
            generated = true,
            evaluationsCreated = total,
            seconds = Math.Round((DateTime.Now - started).TotalSeconds, 1),
            message = $"{total} sample evaluations generated; results computed for Closed periods.",
            periods
        });
    }
}
