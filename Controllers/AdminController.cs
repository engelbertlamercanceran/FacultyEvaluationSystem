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
    private readonly NotificationService _notifications;
    private readonly EvaluatorAssignmentService _assignments;

    private const int UsersPageSize = 100;

    public AdminController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, EvaluationService evalService,
        NotificationService notifications, EvaluatorAssignmentService assignments)
    {
        _db = db;
        _userManager = userManager;
        _evalService = evalService;
        _notifications = notifications;
        _assignments = assignments;
    }

    // --- User Management ---
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Users(string? search, string? role, int? collegeId, int page = 1)
    {
        var query = _db.Users.Include(u => u.College).Include(u => u.Program).AsQueryable();

        if (!string.IsNullOrWhiteSpace(role))
        {
            var roleUserIds = from ur in _db.UserRoles
                              join r in _db.Roles on ur.RoleId equals r.Id
                              where r.Name == role
                              select ur.UserId;
            query = query.Where(u => roleUserIds.Contains(u.Id));
        }

        if (collegeId.HasValue)
            query = query.Where(u => u.CollegeId == collegeId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(u =>
                u.FirstName.ToLower().Contains(term) ||
                u.LastName.ToLower().Contains(term) ||
                u.Email!.ToLower().Contains(term) ||
                (u.EmployeeNumber != null && u.EmployeeNumber.ToLower().Contains(term)) ||
                (u.StudentNumber != null && u.StudentNumber.ToLower().Contains(term)) ||
                (u.College != null && u.College.Code.ToLower().Contains(term)) ||
                (u.Program != null && u.Program.Code.ToLower().Contains(term)));
        }

        var total = await query.CountAsync();
        page = Math.Clamp(page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)UsersPageSize)));

        // Alphabetical: last name, then first name
        var users = await query
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName).ThenBy(u => u.MiddleName)
            .Skip((page - 1) * UsersPageSize)
            .Take(UsersPageSize)
            .ToListAsync();

        // One query for the roles of the whole page
        var ids = users.Select(u => u.Id).ToList();
        var userRoles = await (from ur in _db.UserRoles
                               join r in _db.Roles on ur.RoleId equals r.Id
                               where ids.Contains(ur.UserId)
                               select new { ur.UserId, r.Name })
                              .ToListAsync();

        ViewBag.UserRoles = userRoles
            .GroupBy(x => x.UserId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Name!).OrderBy(n => n).First());
        ViewBag.Search = search;
        ViewBag.Role = role;
        ViewBag.CollegeId = collegeId;
        ViewBag.Page = page;
        ViewBag.Total = total;
        ViewBag.PageSize = UsersPageSize;
        ViewBag.Roles = await _db.Roles.OrderBy(r => r.Name).Select(r => r.Name!).ToListAsync();
        ViewBag.CollegeList = new SelectList(await _db.Colleges.OrderBy(c => c.Name).ToListAsync(), "Id", "Name", collegeId);
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
        return View(await _db.Semesters.OrderByDescending(s => s.SortKey).ToListAsync());
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSemester(string academicYear, string term)
    {
        // Deactivate all
        await _db.Semesters.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, false));

        var semester = new Semester { AcademicYear = academicYear, Term = term, IsActive = true };
        semester.RefreshSortKey(); // SUP-01
        _db.Semesters.Add(semester);
        await _db.SaveChangesAsync();
        TempData["Success"] = "Semester created.";
        return RedirectToAction("Semesters");
    }

    // --- Evaluation Period Management ---
    [Authorize(Roles = "AAStaff")]
    public async Task<IActionResult> Periods()
    {
        var periods = await _db.EvaluationPeriods
            .Include(p => p.Semester)
            .OrderByDescending(p => p.Semester.SortKey).ThenByDescending(p => p.Id)
            .ToListAsync();

        ViewBag.Semesters = new SelectList(
            await _db.Semesters.OrderByDescending(s => s.SortKey).ToListAsync(), "Id", "DisplayName");

        // AAS-03: progress figures so AA Staff can see whether the current
        // stage is finished before advancing to the next one.
        ViewBag.Progress = await BuildStageProgressAsync(periods);
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
            Stage = EvaluationStage.NotStarted
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Evaluation period created.";
        return RedirectToAction("Periods");
    }

    /// <summary>
    /// AAS-03: AA Staff advances the period one stage at a time — students first,
    /// then Program Chairs, then Deans, then closed.
    /// </summary>
    [Authorize(Roles = "AAStaff")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AdvancePeriodStage(int id)
    {
        var period = await _db.EvaluationPeriods.FindAsync(id);
        if (period is null) return NotFound();

        var next = period.NextStage;
        if (next is null)
        {
            TempData["Error"] = "This evaluation period is already closed.";
            return RedirectToAction("Periods");
        }

        period.Stage = next.Value;
        var now = DateTime.Now;
        switch (next.Value)
        {
            case EvaluationStage.StudentEvaluation: period.StudentStageOpenedAt = now; break;
            case EvaluationStage.SupervisorEvaluation: period.SupervisorStageOpenedAt = now; break;
            case EvaluationStage.DeanEvaluation: period.DeanStageOpenedAt = now; break;
            case EvaluationStage.Closed: period.ClosedAt = now; break;
        }

        await _db.Entry(period).Reference(p => p.Semester).LoadAsync();

        // In-app notifications when the supervisors' evaluation opens: both the
        // Deans and the Program Chairs are told it is open and available.
        if (next.Value == EvaluationStage.SupervisorEvaluation)
        {
            _notifications.Add(await _assignments.GetSupervisorIdsAsync(),
                "Staff Evaluation for Supervisors is now open",
                $"The supervisor evaluation for {period.Semester.DisplayName} is open and available. "
                + "Program Chairs may now evaluate the faculty teaching in their programs.",
                Url.Action("SupervisorEvaluate", "Evaluation"));
        }
        else if (next.Value == EvaluationStage.DeanEvaluation)
        {
            var deans = (await _userManager.GetUsersInRoleAsync("Dean")).Where(d => d.IsActive).Select(d => d.Id);
            var aa = (await _userManager.GetUsersInRoleAsync("AA")).Where(d => d.IsActive).Select(d => d.Id);
            _notifications.Add(deans.Concat(aa),
                "Dean Evaluation of Program Chairs is now open",
                $"The evaluation of Program Chairs for {period.Semester.DisplayName} is open and available.",
                Url.Action("SupervisorEvaluate", "Evaluation"));
        }

        await _db.SaveChangesAsync();

        // Closing the period computes the results for everyone evaluated in it.
        if (next.Value == EvaluationStage.Closed)
            await _evalService.ComputeResultsAsync(period.Id);

        TempData["Success"] = $"Evaluation period is now at stage: {EvaluationPeriod.StageLabel(next.Value)}.";
        return RedirectToAction("Periods");
    }

    /// <summary>Counts of submitted vs expected evaluations for the stage each period is in.</summary>
    private async Task<Dictionary<int, StageProgress>> BuildStageProgressAsync(List<EvaluationPeriod> periods)
    {
        var progress = new Dictionary<int, StageProgress>();

        foreach (var period in periods)
        {
            var submitted = await _db.Evaluations
                .CountAsync(e => e.EvaluationPeriodId == period.Id
                              && e.EvaluatorType == (period.Stage == EvaluationStage.StudentEvaluation
                                    ? "Student" : "Supervisor"));

            int expected = period.Stage switch
            {
                // One evaluation per enrolled student per class they are taking.
                EvaluationStage.StudentEvaluation => await _db.StudentEnrollments
                    .CountAsync(se => se.FacultySubject.SemesterId == period.SemesterId),
                _ => 0
            };

            progress[period.Id] = new StageProgress(submitted, expected);
        }

        return progress;
    }

    public record StageProgress(int Submitted, int Expected);

    // --- Faculty-Subject Assignment ---
    public async Task<IActionResult> Subjects()
    {
        var activeSemester = await _db.Semesters.FirstOrDefaultAsync(s => s.IsActive);
        var subjects = activeSemester is not null
            ? await _db.FacultySubjects
                .Include(fs => fs.Faculty)
                .Include(fs => fs.Semester)
                .Include(fs => fs.Program).ThenInclude(p => p!.College)
                .Where(fs => fs.SemesterId == activeSemester.Id)
                .OrderBy(fs => fs.Faculty.LastName).ThenBy(fs => fs.Faculty.FirstName).ThenBy(fs => fs.CourseCode)
                .ToListAsync()
            : [];

        ViewBag.ActiveSemester = activeSemester;
        ViewBag.Faculty = new SelectList(await TeachingStaffAsync(), "Id", "SortName");

        // SUP-02: the owning program must be recorded against each course
        ViewBag.Programs = new SelectList(
            (await _db.AcademicPrograms.Include(p => p.College).OrderBy(p => p.Name).ToListAsync())
                .Select(p => new { p.Id, Label = $"{p.Name} ({p.Code})" }),
            "Id", "Label");
        return View(subjects);
    }

    /// <summary>Active faculty, Program Chairs and Deans, alphabetically.</summary>
    private async Task<List<ApplicationUser>> TeachingStaffAsync()
    {
        var ids = await (from ur in _db.UserRoles
                         join r in _db.Roles on ur.RoleId equals r.Id
                         where r.Name == "Faculty" || r.Name == "ProgramChair" || r.Name == "Dean"
                         select ur.UserId).Distinct().ToListAsync();

        return await _db.Users
            .Where(u => u.IsActive && ids.Contains(u.Id))
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
            .ToListAsync();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateSubject(string facultyId, string courseCode, string? courseTitle, string? section, int? programId)
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
            CourseCode = CourseCodes.Clean(courseCode),
            CourseKey = CourseCodes.Key(courseCode),
            CourseTitle = string.IsNullOrWhiteSpace(courseTitle) ? null : courseTitle.Trim(),
            Section = section,
            ProgramId = programId
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Course assigned.";
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

        var enrollments = await query
            .OrderBy(se => se.Student.LastName).ThenBy(se => se.Student.FirstName)
            .ToListAsync();

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
            .OrderBy(u => u.LastName).ThenBy(u => u.FirstName)
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
        var periods = await _db.EvaluationPeriods.Include(p => p.Semester)
            .OrderByDescending(p => p.Semester.SortKey).ThenByDescending(p => p.Id).ToListAsync();
        periodId ??= periods.FirstOrDefault()?.Id;
        ViewBag.Periods = new SelectList(periods, "Id", "DisplayName", periodId);

        var results = periodId.HasValue
            ? await _db.EvaluationResults
                .Include(r => r.Faculty).ThenInclude(f => f.College)
                .Include(r => r.EvaluationPeriod).ThenInclude(p => p.Semester)
                .Where(r => r.EvaluationPeriodId == periodId.Value)
                .OrderBy(r => r.Faculty.LastName).ThenBy(r => r.Faculty.FirstName)
                .ToListAsync()
            : [];

        return View(results);
    }

    // --- Colleges ---
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Colleges()
    {
        var colleges = await _db.Colleges.Include(c => c.Programs).OrderBy(c => c.Name).ToListAsync();
        foreach (var c in colleges) c.Programs = c.Programs.OrderBy(p => p.Name).ToList();
        return View(colleges);
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
    public async Task<IActionResult> CreateCategory(string name, string evaluatorType, int sortOrder)
    {
        _db.EvaluationCategories.Add(new EvaluationCategory
        {
            Name = name,
            EvaluatorType = evaluatorType,
            SortOrder = sortOrder
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Category \"{name}\" created.";
        return RedirectToAction("Criteria");
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCategory(int id, string name, string evaluatorType, int sortOrder)
    {
        var category = await _db.EvaluationCategories.FindAsync(id);
        if (category is not null)
        {
            category.Name = name;
            category.EvaluatorType = evaluatorType;
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
        ViewBag.Colleges = new SelectList(await _db.Colleges.OrderBy(c => c.Name).ToListAsync(), "Id", "Name");
        ViewBag.Programs = new SelectList(await _db.AcademicPrograms.OrderBy(p => p.Name).ToListAsync(), "Id", "Name");
        ViewBag.Roles = new SelectList(new[] { "AA", "AAStaff", "Admin", "CEO", "Dean", "Faculty", "ProgramChair", "QA", "Student" });
    }
}
