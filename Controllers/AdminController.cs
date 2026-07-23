using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using FacultyEvalSystem.Data;
using FacultyEvalSystem.Models;
using FacultyEvalSystem.Services;
using FacultyEvalSystem.ViewModels;

namespace FacultyEvalSystem.Controllers;

[Authorize(Roles = "Admin")]
public class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly EvaluationService _evalService;

    public AdminController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, EvaluationService evalService)
    {
        _db = db;
        _userManager = userManager;
        _evalService = evalService;
    }

    // --- User Management ---
    public async Task<IActionResult> Users(string? search)
    {
        var query = _db.Users.Include(u => u.College).Include(u => u.Program).AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(u =>
                u.FirstName.ToLower().Contains(term) ||
                u.LastName.ToLower().Contains(term) ||
                u.Email!.ToLower().Contains(term) ||
                (u.EmployeeNumber != null && u.EmployeeNumber.ToLower().Contains(term)) ||
                (u.StudentNumber != null && u.StudentNumber.ToLower().Contains(term)) ||
                (u.College != null && u.College.Code.ToLower().Contains(term)));
        }

        var users = await query.OrderBy(u => u.LastName).ToListAsync();
        var userRoles = new Dictionary<string, string>();
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            userRoles[user.Id] = roles.FirstOrDefault() ?? "None";
        }
        ViewBag.UserRoles = userRoles;
        ViewBag.Search = search;
        return View(users);
    }

    [HttpGet]
    public async Task<IActionResult> CreateUser()
    {
        await PopulateDropdowns();
        return View(new RegisterViewModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await PopulateDropdowns();
            return View(model);
        }

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            FirstName = model.FirstName,
            LastName = model.LastName,
            EmployeeNumber = model.Role != "Student" ? model.IdNumber : null,
            StudentNumber = model.Role == "Student" ? model.IdNumber : null,
            CollegeId = model.CollegeId,
            ProgramId = model.ProgramId,
            EmailConfirmed = true
        };

        var result = string.IsNullOrWhiteSpace(model.Password)
            ? await _userManager.CreateAsync(user)
            : await _userManager.CreateAsync(user, model.Password);
        if (result.Succeeded)
        {
            await _userManager.AddToRoleAsync(user, model.Role);
            var note = string.IsNullOrWhiteSpace(model.Password)
                ? " (no password — user will set it on first login)"
                : "";
            TempData["Success"] = $"User {user.FullName} created successfully.{note}";
            return RedirectToAction("Users");
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError("", error.Description);

        await PopulateDropdowns();
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleUser(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is not null)
        {
            user.IsActive = !user.IsActive;
            await _userManager.UpdateAsync(user);
        }
        return RedirectToAction("Users");
    }

    // --- Semester Management ---
    public async Task<IActionResult> Semesters()
    {
        return View(await _db.Semesters.OrderByDescending(s => s.AcademicYear).ToListAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSemester(string academicYear, string term)
    {
        // Deactivate all
        await _db.Semesters.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));

        _db.Semesters.Add(new Semester { AcademicYear = academicYear, Term = term, IsActive = true });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Semester created.";
        return RedirectToAction("Semesters");
    }

    // --- Evaluation Period Management ---
    public async Task<IActionResult> Periods()
    {
        var periods = await _db.EvaluationPeriods.Include(p => p.Semester).OrderByDescending(p => p.Id).ToListAsync();
        ViewBag.Semesters = new SelectList(await _db.Semesters.ToListAsync(), "Id", "DisplayName");
        return View(periods);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePeriod(string name, int semesterId, DateTime startDate, DateTime endDate)
    {
        _db.EvaluationPeriods.Add(new EvaluationPeriod
        {
            Name = name,
            SemesterId = semesterId,
            StartDate = startDate,
            EndDate = endDate,
            Status = EvaluationStatus.Pending
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Evaluation period created.";
        return RedirectToAction("Periods");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdatePeriodStatus(int id, EvaluationStatus status)
    {
        var period = await _db.EvaluationPeriods.FindAsync(id);
        if (period is not null)
        {
            period.Status = status;
            await _db.SaveChangesAsync();
        }
        return RedirectToAction("Periods");
    }

    // --- Faculty-Subject Assignment ---
    public async Task<IActionResult> Subjects()
    {
        var activeSemester = await _db.Semesters.FirstOrDefaultAsync(s => s.IsActive);
        var subjects = activeSemester is not null
            ? await _db.FacultySubjects
                .Include(fs => fs.Faculty)
                .Include(fs => fs.Semester)
                .Where(fs => fs.SemesterId == activeSemester.Id)
                .ToListAsync()
            : [];

        ViewBag.ActiveSemester = activeSemester;
        var faculty = await _userManager.GetUsersInRoleAsync("Faculty");
        ViewBag.Faculty = new SelectList(faculty.Where(f => f.IsActive), "Id", "FullName");
        return View(subjects);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSubject(string facultyId, string subjectCode, string subjectName, string? section)
    {
        var activeSemester = await _db.Semesters.FirstOrDefaultAsync(s => s.IsActive);
        if (activeSemester is null)
        {
            TempData["Error"] = "No active semester.";
            return RedirectToAction("Subjects");
        }

        _db.FacultySubjects.Add(new FacultySubject
        {
            FacultyId = facultyId,
            SemesterId = activeSemester.Id,
            SubjectCode = subjectCode,
            SubjectName = subjectName,
            Section = section
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Subject assigned.";
        return RedirectToAction("Subjects");
    }

    // --- Student Enrollment ---
    public async Task<IActionResult> Enrollments(int subjectId, string? search)
    {
        var subject = await _db.FacultySubjects.Include(fs => fs.Faculty).FirstOrDefaultAsync(fs => fs.Id == subjectId);
        if (subject is null) return NotFound();

        var query = _db.StudentEnrollments
            .Include(se => se.Student)
            .Where(se => se.FacultySubjectId == subjectId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(se =>
                se.Student.FirstName.ToLower().Contains(term) ||
                se.Student.LastName.ToLower().Contains(term) ||
                (se.Student.StudentNumber != null && se.Student.StudentNumber.ToLower().Contains(term)));
        }

        var enrollments = await query.ToListAsync();

        var students = await _userManager.GetUsersInRoleAsync("Student");
        var allEnrolledIds = await _db.StudentEnrollments
            .Where(se => se.FacultySubjectId == subjectId)
            .Select(se => se.StudentId)
            .ToListAsync();
        var enrolledIds = allEnrolledIds.ToHashSet();
        ViewBag.AvailableStudents = new SelectList(students.Where(s => s.IsActive && !enrolledIds.Contains(s.Id)), "Id", "FullName");
        ViewBag.Subject = subject;
        ViewBag.Search = search;

        return View(enrollments);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EnrollStudent(int subjectId, string studentId)
    {
        _db.StudentEnrollments.Add(new StudentEnrollment { FacultySubjectId = subjectId, StudentId = studentId });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Student enrolled.";
        return RedirectToAction("Enrollments", new { subjectId });
    }

    // --- Compute Results ---
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ComputeResults(int periodId)
    {
        await _evalService.ComputeResultsAsync(periodId);
        TempData["Success"] = "Results computed successfully.";
        return RedirectToAction("Results", new { periodId });
    }

    public async Task<IActionResult> Results(int? periodId)
    {
        var periods = await _db.EvaluationPeriods.Include(p => p.Semester).OrderByDescending(p => p.Id).ToListAsync();
        ViewBag.Periods = new SelectList(periods, "Id", "DisplayName", periodId);

        var results = periodId.HasValue
            ? await _db.EvaluationResults
                .Include(r => r.Faculty).ThenInclude(f => f.College)
                .Include(r => r.EvaluationPeriod).ThenInclude(p => p.Semester)
                .Where(r => r.EvaluationPeriodId == periodId.Value)
                .OrderByDescending(r => r.OverallRating)
                .ToListAsync()
            : [];

        return View(results);
    }

    // --- Colleges ---
    public async Task<IActionResult> Colleges()
    {
        return View(await _db.Colleges.Include(c => c.Programs).ToListAsync());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCollege(string name, string code)
    {
        _db.Colleges.Add(new College { Name = name, Code = code });
        await _db.SaveChangesAsync();
        TempData["Success"] = "College created.";
        return RedirectToAction("Colleges");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateProgram(int collegeId, string name, string code)
    {
        _db.AcademicPrograms.Add(new AcademicProgram { CollegeId = collegeId, Name = name, Code = code });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Program created.";
        return RedirectToAction("Colleges");
    }

    private async Task PopulateDropdowns()
    {
        ViewBag.Colleges = new SelectList(await _db.Colleges.ToListAsync(), "Id", "Name");
        ViewBag.Programs = new SelectList(await _db.AcademicPrograms.ToListAsync(), "Id", "Name");
        ViewBag.Roles = new SelectList(new[] { "Student", "Faculty", "Dean", "ProgramChair", "Admin" });
    }
}
