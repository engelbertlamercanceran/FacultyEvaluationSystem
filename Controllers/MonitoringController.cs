using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FacultyEvalSystem.Data;
using FacultyEvalSystem.Models;
using FacultyEvalSystem.ViewModels;

namespace FacultyEvalSystem.Controllers;

/// <summary>
/// Evaluation Monitoring: lets a Dean (their college) or a Program Chair (their
/// program) see which students have finished the student evaluation of the
/// current period, and which still have faculty left to evaluate.
/// </summary>
[Authorize(Roles = "Dean,ProgramChair")]
public class MonitoringController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public MonitoringController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public async Task<IActionResult> Index(int? programId)
    {
        var user = await _userManager.GetUserAsync(User);
        var isDean = User.IsInRole("Dean");
        var model = new MonitoringViewModel { IsDean = isDean };

        // Same period the evaluation pages work on: the most recent one that has started
        model.Period = await _db.EvaluationPeriods
            .Include(p => p.Semester)
            .Where(p => p.Stage != EvaluationStage.NotStarted)
            .OrderByDescending(p => p.Semester.SortKey).ThenByDescending(p => p.Id)
            .FirstOrDefaultAsync();

        if (model.Period is null)
        {
            model.Message = "No evaluation period has started yet. Progress appears here once CDI opens the student evaluation.";
            return View(model);
        }

        // Scope: a Dean sees their college (optionally one program), a Program Chair their program
        var studentRoleId = await _db.Roles.Where(r => r.Name == "Student").Select(r => r.Id).FirstOrDefaultAsync();
        var students = _db.Users.Where(u => u.IsActive
            && _db.UserRoles.Any(ur => ur.UserId == u.Id && ur.RoleId == studentRoleId));

        if (isDean)
        {
            if (user!.CollegeId is null)
            {
                model.Message = "Your account is not linked to a college. Please ask the Administrator to set it.";
                return View(model);
            }

            var college = await _db.Colleges.FindAsync(user.CollegeId);
            model.Programs = await _db.AcademicPrograms.Where(p => p.CollegeId == user.CollegeId)
                .OrderBy(p => p.Name).ToListAsync();
            model.ProgramId = model.Programs.Any(p => p.Id == programId) ? programId : null;

            students = students.Where(u => u.CollegeId == user.CollegeId);
            if (model.ProgramId is not null) students = students.Where(u => u.ProgramId == model.ProgramId);

            model.ScopeName = model.ProgramId is not null
                ? model.Programs.First(p => p.Id == model.ProgramId).Name
                : college?.Name ?? "your college";
        }
        else
        {
            if (user!.ProgramId is null)
            {
                model.Message = "Your account is not linked to a program. Please ask the Administrator to set it.";
                return View(model);
            }

            students = students.Where(u => u.ProgramId == user.ProgramId);
            model.ScopeName = (await _db.AcademicPrograms.FindAsync(user.ProgramId))?.Name ?? "your program";
        }

        var semesterId = model.Period.SemesterId;
        var periodId = model.Period.Id;

        // The scoped student query is reused as a sub-query, so no long id lists are sent to the database
        var people = await students.Include(u => u.Program).ToListAsync();

        var linked = await _db.StudentEnrollments
            .Where(se => se.FacultySubject.SemesterId == semesterId && students.Any(u => u.Id == se.StudentId))
            .Select(se => new { se.StudentId, se.FacultySubject.CourseKey })
            .ToListAsync();

        var masterlist = await _db.StudentCourses
            .Where(sc => sc.SemesterId == semesterId && students.Any(u => u.Id == sc.StudentId))
            .Select(sc => new { sc.StudentId, sc.CourseKey })
            .ToListAsync();

        // A course on the masterlist only counts if somebody teaches it this semester;
        // otherwise the student has nobody to evaluate for it
        var taught = (await _db.FacultySubjects
                .Where(fs => fs.SemesterId == semesterId && fs.Faculty.IsActive)
                .Select(fs => fs.CourseKey).Distinct().ToListAsync())
            .ToHashSet();

        var submitted = await _db.Evaluations
            .Where(e => e.EvaluationPeriodId == periodId && e.EvaluatorType == "Student"
                     && e.FacultySubjectId != null && students.Any(u => u.Id == e.EvaluatorId))
            .Select(e => new { e.EvaluatorId, e.FacultySubject!.CourseKey })
            .ToListAsync();

        var linkedBy = linked.ToLookup(x => x.StudentId, x => x.CourseKey);
        var masterBy = masterlist.ToLookup(x => x.StudentId, x => x.CourseKey);
        var doneBy = submitted.ToLookup(x => x.EvaluatorId, x => x.CourseKey);

        foreach (var s in people)
        {
            // One evaluation is due per course the student takes this semester
            var due = linkedBy[s.Id].Concat(masterBy[s.Id].Where(taught.Contains)).ToHashSet();
            if (due.Count == 0)
            {
                model.WithoutCourses++;
                continue;
            }

            var done = doneBy[s.Id].Where(due.Contains).Distinct().Count();
            var row = new StudentProgressRow
            {
                Name = s.SortName + (string.IsNullOrWhiteSpace(s.Suffix) ? "" : $" {s.Suffix}"),
                StudentNumber = s.StudentNumber,
                Email = s.Email,
                Program = s.Program?.Code,
                Section = s.Section,
                Done = done,
                Due = due.Count
            };
            (row.IsComplete ? model.Completed : model.NotCompleted).Add(row);
        }

        model.Completed = model.Completed.OrderBy(r => r.Program).ThenBy(r => r.Section).ThenBy(r => r.Name).ToList();
        model.NotCompleted = model.NotCompleted.OrderBy(r => r.Program).ThenBy(r => r.Section).ThenBy(r => r.Name).ToList();
        return View(model);
    }
}
