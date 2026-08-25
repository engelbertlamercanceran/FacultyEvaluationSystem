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

[Authorize(Roles = "Admin,AAStaff")]
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
    [Authorize(Roles = "Admin")]
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

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> CreateUser()
    {
        await PopulateDropdowns();
        return View(new RegisterViewModel());
    }

    [Authorize(Roles = "Admin")]
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

    [Authorize(Roles = "Admin")]
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
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Semesters()
    {
        return View(await _db.Semesters.OrderByDescending(s => s.AcademicYear).ToListAsync());
    }

    [Authorize(Roles = "Admin")]
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
    [Authorize(Roles = "AAStaff")]
    public async Task<IActionResult> Periods()
    {
        var periods = await _db.EvaluationPeriods.Include(p => p.Semester).OrderByDescending(p => p.Id).ToListAsync();
        ViewBag.Semesters = new SelectList(await _db.Semesters.ToListAsync(), "Id", "DisplayName");
        return View(periods);
    }

    [Authorize(Roles = "AAStaff")]
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

    [Authorize(Roles = "AAStaff")]
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
    [Authorize(Roles = "Admin")]
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

        ViewBag.Subject = subject;
        ViewBag.Search = search;

        return View(enrollments);
    }

    [Authorize(Roles = "Admin")]
    [HttpGet]
    public async Task<IActionResult> SearchStudents(int subjectId, string term)
    {
        if (string.IsNullOrWhiteSpace(term) || term.Length < 2)
            return Json(Array.Empty<object>());

        var enrolledIds = await _db.StudentEnrollments
            .Where(se => se.FacultySubjectId == subjectId)
            .Select(se => se.StudentId)
            .ToListAsync();

        var words = term.Trim().ToLower().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var query = _db.Users
            .Where(u => u.IsActive && !enrolledIds.Contains(u.Id));

        foreach (var word in words)
        {
            var w = word; // capture for closure
            query = query.Where(u =>
                u.FirstName.ToLower().Contains(w) ||
                u.LastName.ToLower().Contains(w) ||
                (u.StudentNumber != null && u.StudentNumber.ToLower().Contains(w)));
        }

        var students = await query
            .OrderBy(u => u.LastName)
            .Take(10)
            .Select(u => new { u.Id, Name = u.FirstName + " " + u.LastName, u.StudentNumber })
            .ToListAsync();

        return Json(students);
    }

    [Authorize(Roles = "Admin")]
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
    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ComputeResults(int periodId)
    {
        await _evalService.ComputeResultsAsync(periodId);
        TempData["Success"] = "Results computed successfully.";
        return RedirectToAction("Results", new { periodId });
    }

    [Authorize(Roles = "Admin")]
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
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Colleges()
    {
        return View(await _db.Colleges.Include(c => c.Programs).ToListAsync());
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCollege(string name, string code)
    {
        _db.Colleges.Add(new College { Name = name, Code = code });
        await _db.SaveChangesAsync();
        TempData["Success"] = "College created.";
        return RedirectToAction("Colleges");
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateProgram(int collegeId, string name, string code)
    {
        _db.AcademicPrograms.Add(new AcademicProgram { CollegeId = collegeId, Name = name, Code = code });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Program created.";
        return RedirectToAction("Colleges");
    }

    // --- Evaluation Criteria Management ---
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Criteria()
    {
        var categories = await _db.EvaluationCategories
            .Include(c => c.Criteria.OrderBy(cr => cr.SortOrder))
            .OrderBy(c => c.EvaluatorType)
            .ThenBy(c => c.SortOrder)
            .ToListAsync();
        return View(categories);
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCategory(string name, string evaluatorType, double weight, int sortOrder)
    {
        _db.EvaluationCategories.Add(new EvaluationCategory
        {
            Name = name,
            EvaluatorType = evaluatorType,
            Weight = weight,
            SortOrder = sortOrder
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Category \"{name}\" created.";
        return RedirectToAction("Criteria");
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCategory(int id, string name, string evaluatorType, double weight, int sortOrder)
    {
        var category = await _db.EvaluationCategories.FindAsync(id);
        if (category is not null)
        {
            category.Name = name;
            category.EvaluatorType = evaluatorType;
            category.Weight = weight;
            category.SortOrder = sortOrder;
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Category \"{name}\" updated.";
        }
        return RedirectToAction("Criteria");
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var category = await _db.EvaluationCategories.Include(c => c.Criteria).FirstOrDefaultAsync(c => c.Id == id);
        if (category is not null)
        {
            _db.EvaluationCriteria.RemoveRange(category.Criteria);
            _db.EvaluationCategories.Remove(category);
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Category \"{category.Name}\" and its criteria deleted.";
        }
        return RedirectToAction("Criteria");
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCriterion(int categoryId, string description, int sortOrder)
    {
        _db.EvaluationCriteria.Add(new EvaluationCriterion
        {
            CategoryId = categoryId,
            Description = description,
            SortOrder = sortOrder
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Criterion added.";
        return RedirectToAction("Criteria");
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCriterion(int id, string description, int sortOrder)
    {
        var criterion = await _db.EvaluationCriteria.FindAsync(id);
        if (criterion is not null)
        {
            criterion.Description = description;
            criterion.SortOrder = sortOrder;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Criterion updated.";
        }
        return RedirectToAction("Criteria");
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCriterion(int id)
    {
        var criterion = await _db.EvaluationCriteria.FindAsync(id);
        if (criterion is not null)
        {
            _db.EvaluationCriteria.Remove(criterion);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Criterion deleted.";
        }
        return RedirectToAction("Criteria");
    }

    private async Task PopulateDropdowns()
    {
        ViewBag.Colleges = new SelectList(await _db.Colleges.ToListAsync(), "Id", "Name");
        ViewBag.Programs = new SelectList(await _db.AcademicPrograms.ToListAsync(), "Id", "Name");
        ViewBag.Roles = new SelectList(new[] { "Student", "Faculty", "Dean", "ProgramChair", "CEO", "AA", "AAStaff", "QA", "Admin" });
    }
}
