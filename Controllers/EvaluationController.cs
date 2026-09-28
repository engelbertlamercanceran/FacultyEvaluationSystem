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
    private readonly EvaluatorAssignmentService _assignments;

    public EvaluationController(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        EvaluationService evalService, EvaluatorAssignmentService assignments)
    {
        _db = db;
        _userManager = userManager;
        _evalService = evalService;
        _assignments = assignments;
    }

    // Student: list faculty to evaluate
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);

        // AAS-03: students may only submit while the period is at the student stage
        var activePeriod = await _db.EvaluationPeriods
            .Include(p => p.Semester)
            .FirstOrDefaultAsync(p => p.Stage == EvaluationStage.StudentEvaluation);

        if (activePeriod is null)
            return View(new EvaluationListViewModel { ActivePeriod = null });

        // Courses the student is linked to an instructor for
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
                SortName = e.FacultySubject.Faculty.SortName,
                FacultySubjectId = e.FacultySubjectId,
                CourseCode = e.FacultySubject.CourseCode,
                CourseTitle = e.FacultySubject.CourseTitle,
                Section = e.FacultySubject.Section,
                AlreadyEvaluated = myEval is not null,
                EvaluationId = myEval?.Id
            };
        })
        .OrderBy(f => f.CourseCode).ThenBy(f => f.SortName)
        .ToList();

        // Registrar's masterlist: Course Codes not yet linked to an instructor
        var linkedKeys = enrollments.Select(e => e.FacultySubject.CourseKey).ToHashSet();
        var unlinked = (await _db.StudentCourses
                .Where(sc => sc.StudentId == user!.Id && sc.SemesterId == activePeriod.SemesterId)
                .ToListAsync())
            .Where(sc => !linkedKeys.Contains(sc.CourseKey))
            .OrderBy(sc => sc.CourseCode)
            .ToList();

        var unlinkedKeys = unlinked.Select(sc => sc.CourseKey).ToList();
        var teaching = await _db.FacultySubjects
            .Include(fs => fs.Faculty).ThenInclude(f => f.College)
            .Where(fs => fs.SemesterId == activePeriod.SemesterId && unlinkedKeys.Contains(fs.CourseKey) && fs.Faculty.IsActive)
            .ToListAsync();

        var choices = new List<InstructorChoiceViewModel>();
        var noInstructor = new List<string>();
        foreach (var sc in unlinked)
        {
            var options = teaching.Where(t => t.CourseKey == sc.CourseKey)
                .DistinctBy(t => t.FacultyId)
                .OrderBy(t => t.Faculty.LastName).ThenBy(t => t.Faculty.FirstName)
                .Select(t => (t.Id, t.Faculty.FullName, t.Faculty.College?.Code))
                .ToList();

            if (options.Count == 0) noInstructor.Add(sc.CourseCode);
            else choices.Add(new InstructorChoiceViewModel { CourseCode = sc.CourseCode, Options = options });
        }

        return View(new EvaluationListViewModel
        {
            ActivePeriod = activePeriod,
            FacultyList = facultyList,
            InstructorChoices = choices,
            CoursesWithoutInstructor = noInstructor
        });
    }

    /// <summary>
    /// Student: where a Course Code on the Registrar's masterlist is taught by more
    /// than one faculty member, the student confirms which one is their instructor.
    /// </summary>
    [Authorize(Roles = "Student")]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChooseInstructor(int facultySubjectId)
    {
        var user = await _userManager.GetUserAsync(User);

        var assignment = await _db.FacultySubjects.FindAsync(facultySubjectId);
        if (assignment is null) return NotFound();

        // The course must be on the student's own masterlist for that semester
        var enrolled = await _db.StudentCourses.AnyAsync(sc => sc.StudentId == user!.Id
            && sc.SemesterId == assignment.SemesterId && sc.CourseKey == assignment.CourseKey);
        if (!enrolled)
        {
            TempData["Error"] = "That course is not on your list of enrolled courses.";
            return RedirectToAction(nameof(Index));
        }

        // One instructor per course
        var already = await _db.StudentEnrollments.AnyAsync(se => se.StudentId == user!.Id
            && se.FacultySubject.SemesterId == assignment.SemesterId
            && se.FacultySubject.CourseKey == assignment.CourseKey);
        if (already)
        {
            TempData["Error"] = $"You have already chosen your instructor for {assignment.CourseCode}.";
            return RedirectToAction(nameof(Index));
        }

        _db.StudentEnrollments.Add(new StudentEnrollment { StudentId = user!.Id, FacultySubjectId = assignment.Id });
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Instructor for {assignment.CourseCode} recorded. You can now evaluate them.";
        return RedirectToAction(nameof(Index));
    }

    // Show evaluation form
    [HttpGet]
    public async Task<IActionResult> Evaluate(string facultyId, int periodId, int? subjectId)
    {
        var user = await _userManager.GetUserAsync(User);
        var roles = await _userManager.GetRolesAsync(user!);
        var evaluatorType = roles.Contains("Student") ? "Student" : "Supervisor";

        // AAS-03: the evaluator's stage must be open
        if (!await IsStageOpenForAsync(periodId, roles))
        {
            TempData["Error"] = "The evaluation is not currently open for your role.";
            return RedirectToAction(evaluatorType == "Student" ? "Index" : "SupervisorEvaluate");
        }

        // Check if already evaluated
        var exists = await _db.Evaluations.AnyAsync(e =>
            e.EvaluatorId == user!.Id &&
            e.FacultyId == facultyId &&
            e.FacultySubjectId == subjectId &&
            e.EvaluationPeriodId == periodId);

        if (exists)
        {
            TempData["Error"] = "You have already submitted an evaluation for this faculty member.";
            return RedirectToAction(evaluatorType == "Student" ? "Index" : "SupervisorEvaluate");
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
            CourseTitle = subject?.CourseDisplay,
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
        var roles = await _userManager.GetRolesAsync(user!);

        // AAS-03: re-check on submit so the stage cannot be bypassed by posting directly
        if (!await IsStageOpenForAsync(model.EvaluationPeriodId, roles))
        {
            TempData["Error"] = "The evaluation is not currently open for your role. "
                              + "Your responses were not saved.";
            return RedirectToAction(model.EvaluatorType == "Student" ? "Index" : "SupervisorEvaluate");
        }

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

    /// <summary>AAS-03: is the given period at the stage this evaluator submits in?</summary>
    private async Task<bool> IsStageOpenForAsync(int periodId, IList<string> roles)
    {
        var period = await _db.EvaluationPeriods.FindAsync(periodId);
        if (period is null) return false;

        foreach (var role in roles)
        {
            if (period.IsOpenFor(role)) return true;
        }
        return false;
    }

    // Supervisor roster. Who appears depends on the signed-in role:
    //   PC-01  Program Chair -> faculty teaching courses owned by their program
    //   DN-01  Dean          -> Program Chairs in their college, grouped per program
    //   AA-01  AA            -> Program Chairs with no designated evaluator
    [Authorize(Roles = "Dean,ProgramChair,AA")]
    public async Task<IActionResult> SupervisorEvaluate()
    {
        var user = await _userManager.GetUserAsync(User);
        var roles = await _userManager.GetRolesAsync(user!);
        var role = roles.FirstOrDefault(r => r is "ProgramChair" or "Dean" or "AA") ?? "";

        // Most recent period that has actually started
        var period = await _db.EvaluationPeriods
            .Include(p => p.Semester)
            .Where(p => p.Stage != EvaluationStage.NotStarted)
            .OrderByDescending(p => p.Semester.SortKey).ThenByDescending(p => p.Id)
            .FirstOrDefaultAsync();

        if (period is null)
            return View("Index", new EvaluationListViewModel { ActivePeriod = null });

        // AAS-03: this evaluator may only submit during their own stage
        if (!period.IsOpenFor(role))
        {
            return View("Index", new EvaluationListViewModel
            {
                ActivePeriod = period,
                StageMessage = period.IsClosed
                    ? $"{period.DisplayName} is closed. No further evaluations can be submitted."
                    : $"The evaluation is currently at the \"{period.StageName}\" stage. "
                      + "You will be able to submit once Academic Affairs opens your stage."
            });
        }

        var existingEvals = await _db.Evaluations
            .Where(e => e.EvaluatorId == user!.Id && e.EvaluationPeriodId == period.Id)
            .ToListAsync();

        var model = new EvaluationListViewModel { ActivePeriod = period };

        if (role == "ProgramChair")
            await BuildProgramChairRosterAsync(model, user!, period, existingEvals);
        else if (role == "Dean")
            await BuildDeanRosterAsync(model, user!, period, existingEvals);
        else
            await BuildAaRosterAsync(model, period, existingEvals);

        return View("Index", model);
    }

    /// <summary>
    /// PC-01: the Chair evaluates every faculty member who teaches a course owned by
    /// their program in this semester — including faculty whose home department is
    /// elsewhere. Derived from actual teaching assignments, not from college membership.
    /// </summary>
    private async Task BuildProgramChairRosterAsync(
        EvaluationListViewModel model, ApplicationUser chair,
        EvaluationPeriod period, List<Evaluation> existingEvals)
    {
        if (chair.ProgramId is null)
        {
            model.StageMessage = "Your account is not linked to a program, so the system cannot "
                               + "determine which faculty you are responsible for. Please ask "
                               + "Academic Affairs to set your program.";
            return;
        }

        var assignments = await _assignments.GetProgramTeachingAssignmentsAsync(chair, period.SemesterId);

        var program = await _db.AcademicPrograms
            .Include(p => p.College)
            .FirstOrDefaultAsync(p => p.Id == chair.ProgramId);

        model.RosterDescription = program is not null
            ? $"Faculty teaching {program.Code} courses this semester, by teaching assignment."
            : "Faculty teaching courses in your program this semester.";

        model.FacultyList = assignments
            .GroupBy(fs => fs.Faculty)
            .Select(g =>
            {
                var faculty = g.Key;
                var myEval = existingEvals.FirstOrDefault(x => x.FacultyId == faculty.Id);
                return new FacultyToEvaluateViewModel
                {
                    FacultyId = faculty.Id,
                    FacultyName = faculty.FullName,
                    SortName = faculty.SortName,
                    AlreadyEvaluated = myEval is not null,
                    EvaluationId = myEval?.Id,
                    CoursesInProgram = g
                        .Select(fs => string.IsNullOrWhiteSpace(fs.Section)
                            ? fs.CourseCode
                            : $"{fs.CourseCode} ({fs.Section})")
                        .Distinct().OrderBy(c => c).ToList(),
                    IsFromAnotherDepartment = faculty.ProgramId != chair.ProgramId,
                    HomeDepartment = faculty.Program?.Code ?? faculty.College?.Code
                };
            })
            .OrderBy(f => f.SortName)
            .ToList();

        if (model.FacultyList.Count == 0)
            model.StageMessage = "No faculty are currently assigned to teach courses in your "
                               + "program for this semester. Teaching assignments are imported "
                               + "by Academic Affairs from the Teaching Memo.";
    }

    /// <summary>
    /// DN-01: the Dean evaluates the Program Chairs in their college, presented per program.
    /// </summary>
    private async Task BuildDeanRosterAsync(
        EvaluationListViewModel model, ApplicationUser dean,
        EvaluationPeriod period, List<Evaluation> existingEvals)
    {
        var chairs = await _assignments.GetChairsForDeanAsync(dean);

        model.RosterDescription = "Program Chairs under your college, organised per program.";
        model.Groups = chairs
            .GroupBy(c => c.Program?.Name ?? "Unassigned program")
            .OrderBy(g => g.Key)
            .Select(g => new EvaluationGroupViewModel
            {
                GroupName = g.Key,
                GroupSubtitle = g.First().Program?.Code,
                Members = g.Select(c => ToRosterEntry(c, existingEvals)).OrderBy(m => m.SortName).ToList()
            })
            .ToList();

        if (model.Groups.Count == 0)
            model.StageMessage = "There are no Program Chairs registered under your college.";
    }

    /// <summary>
    /// AA-01: the AA account covers Program Chairs who have no designated evaluator.
    /// </summary>
    private async Task BuildAaRosterAsync(
        EvaluationListViewModel model, EvaluationPeriod period, List<Evaluation> existingEvals)
    {
        var chairs = await _assignments.GetChairsWithoutEvaluatorAsync();

        model.RosterDescription = "Program Chairs with no designated evaluator, organised per college.";
        model.Groups = chairs
            .GroupBy(c => c.College?.Name ?? "Unassigned college")
            .OrderBy(g => g.Key)
            .Select(g => new EvaluationGroupViewModel
            {
                GroupName = g.Key,
                GroupSubtitle = g.First().College?.Code,
                Members = g.Select(c => ToRosterEntry(c, existingEvals)).OrderBy(m => m.SortName).ToList()
            })
            .ToList();

        if (model.Groups.Count == 0)
            model.StageMessage = "Every Program Chair has a designated evaluator, so there is "
                               + "nothing for the AA account to evaluate this period.";
    }

    private static FacultyToEvaluateViewModel ToRosterEntry(
        ApplicationUser user, List<Evaluation> existingEvals)
    {
        var myEval = existingEvals.FirstOrDefault(x => x.FacultyId == user.Id);
        return new FacultyToEvaluateViewModel
        {
            FacultyId = user.Id,
            FacultyName = user.FullName,
            SortName = user.SortName,
            AlreadyEvaluated = myEval is not null,
            EvaluationId = myEval?.Id,
            HomeDepartment = user.Program?.Code ?? user.College?.Code
        };
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

        double totalScore = eval.Responses.Sum(r => r.Rating);
        double rating = (totalScore / 75.0) * 100;

        return Json(new {
            facultyName = eval.Faculty.FullName,
            categories,
            rating = Math.Round(rating, 2),
            description = EvaluationResult.GetDescriptiveRating(rating)
        });
    }
}
