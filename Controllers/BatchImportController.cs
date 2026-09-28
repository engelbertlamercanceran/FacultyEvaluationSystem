using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FacultyEvalSystem.Data;
using FacultyEvalSystem.Models;
using FacultyEvalSystem.Services;

namespace FacultyEvalSystem.Controllers;

[Authorize(Roles = "Admin,AAStaff")]
public class BatchImportController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly EvaluationService _evalService;
    private readonly DataImportService _import;

    public BatchImportController(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        EvaluationService evalService, DataImportService import)
    {
        _db = db;
        _userManager = userManager;
        _evalService = evalService;
        _import = import;
    }

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

    /// <summary>The Faculty Workload Summary: faculty, their college and designation, and the Course Codes they teach.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> ImportWorkload(IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Please choose the Faculty Workload Summary (.xlsx).";
            return RedirectToAction(nameof(Index));
        }

        var semester = await _db.Semesters.FirstOrDefaultAsync(s => s.IsActive);
        if (semester is null)
        {
            TempData["Error"] = "No active semester. Please create one first.";
            return RedirectToAction(nameof(Index));
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

        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> ImportColleges(IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Please select a CSV file.";
            return RedirectToAction(nameof(Index));
        }

        var lines = await ReadCsvLines(file);
        if (lines.Count < 2) { TempData["Error"] = "CSV file is empty or has no data rows."; return RedirectToAction(nameof(Index)); }

        int created = 0, skipped = 0;

        foreach (var fields in lines.Skip(1))
        {
            if (fields.Length < 2) continue;
            var collegeName = fields[0].Trim();
            var collegeCode = fields[1].Trim();
            if (string.IsNullOrEmpty(collegeName) || string.IsNullOrEmpty(collegeCode)) continue;

            var college = await _db.Colleges.FirstOrDefaultAsync(c => c.Code == collegeCode);
            if (college is null)
            {
                college = new College { Name = collegeName, Code = collegeCode };
                _db.Colleges.Add(college);
                await _db.SaveChangesAsync();
                created++;
            }
            else skipped++;

            // Program columns are optional
            if (fields.Length >= 4)
            {
                var progName = fields[2].Trim();
                var progCode = fields[3].Trim();
                if (!string.IsNullOrEmpty(progName) && !string.IsNullOrEmpty(progCode))
                {
                    var exists = await _db.AcademicPrograms.AnyAsync(p => p.Code == progCode);
                    if (!exists)
                    {
                        _db.AcademicPrograms.Add(new AcademicProgram { Name = progName, Code = progCode, CollegeId = college.Id });
                        await _db.SaveChangesAsync();
                        created++;
                    }
                    else skipped++;
                }
            }
        }

        TempData["Success"] = $"Colleges & Programs imported: {created} created, {skipped} already existed.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> ImportUsers(IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Please select a CSV file.";
            return RedirectToAction(nameof(Index));
        }

        var lines = await ReadCsvLines(file);
        if (lines.Count < 2) { TempData["Error"] = "CSV file is empty or has no data rows."; return RedirectToAction(nameof(Index)); }

        int created = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var fields in lines.Skip(1))
        {
            // Email,FirstName,LastName,Role,Password,EmployeeNumber,StudentNumber,CollegeCode,ProgramCode
            if (fields.Length < 5) continue;

            var email = fields[0].Trim();
            var firstName = fields[1].Trim();
            var lastName = fields[2].Trim();
            var role = fields[3].Trim();
            var password = fields[4].Trim();
            var empNumber = fields.Length > 5 ? fields[5].Trim() : null;
            var stuNumber = fields.Length > 6 ? fields[6].Trim() : null;
            var collegeCode = fields.Length > 7 ? fields[7].Trim() : null;
            var programCode = fields.Length > 8 ? fields[8].Trim() : null;

            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(firstName) || string.IsNullOrEmpty(role)) continue;

            if (await _userManager.FindByEmailAsync(email) is not null)
            {
                skipped++;
                continue;
            }

            int? collegeId = null;
            int? programId = null;

            if (!string.IsNullOrEmpty(collegeCode))
            {
                var college = await _db.Colleges.FirstOrDefaultAsync(c => c.Code == collegeCode);
                collegeId = college?.Id;
            }
            if (!string.IsNullOrEmpty(programCode))
            {
                var program = await _db.AcademicPrograms.FirstOrDefaultAsync(p => p.Code == programCode);
                programId = program?.Id;
            }

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FirstName = firstName,
                LastName = lastName,
                EmployeeNumber = string.IsNullOrEmpty(empNumber) ? null : empNumber,
                StudentNumber = string.IsNullOrEmpty(stuNumber) ? null : stuNumber,
                CollegeId = collegeId,
                ProgramId = programId,
                EmailConfirmed = true
            };

            var result = string.IsNullOrEmpty(password)
                ? await _userManager.CreateAsync(user)
                : await _userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                // Validate role
                string[] validRoles = ["Admin", "CEO", "Dean", "ProgramChair", "Faculty", "Student", "AA", "AAStaff", "QA"];
                if (validRoles.Contains(role))
                    await _userManager.AddToRoleAsync(user, role);
                else
                    await _userManager.AddToRoleAsync(user, "Student");

                created++;
            }
            else
            {
                errors.Add($"{email}: {string.Join(", ", result.Errors.Select(e => e.Description))}");
            }
        }

        var msg = $"Users imported: {created} created, {skipped} already existed.";
        if (errors.Count > 0) msg += $" {errors.Count} errors.";
        TempData["Success"] = msg;
        if (errors.Count > 0) TempData["Error"] = string.Join(" | ", errors.Take(5));
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> ImportSubjects(IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Please select a CSV file.";
            return RedirectToAction(nameof(Index));
        }

        var activeSemester = await _db.Semesters.FirstOrDefaultAsync(s => s.IsActive);
        if (activeSemester is null)
        {
            TempData["Error"] = "No active semester found. Please create and activate a semester first.";
            return RedirectToAction(nameof(Index));
        }

        var lines = await ReadCsvLines(file);
        if (lines.Count < 2) { TempData["Error"] = "CSV file is empty or has no data rows."; return RedirectToAction(nameof(Index)); }

        int created = 0, skipped = 0;

        foreach (var fields in lines.Skip(1))
        {
            // FacultyEmail,CourseCode,CourseTitle,Section
            if (fields.Length < 4) continue;

            var facultyEmail = fields[0].Trim();
            var subjectCode = CourseCodes.Clean(fields[1]);
            var courseKey = CourseCodes.Key(subjectCode);
            var subjectName = fields[2].Trim();
            var section = fields[3].Trim();

            var faculty = await _userManager.FindByEmailAsync(facultyEmail);
            if (faculty is null) { skipped++; continue; }

            var exists = await _db.FacultySubjects.AnyAsync(s =>
                s.FacultyId == faculty.Id && s.CourseKey == courseKey &&
                s.Section == section && s.SemesterId == activeSemester.Id);

            if (exists) { skipped++; continue; }

            _db.FacultySubjects.Add(new FacultySubject
            {
                FacultyId = faculty.Id,
                SemesterId = activeSemester.Id,
                CourseCode = subjectCode,
                CourseKey = courseKey,
                CourseTitle = string.IsNullOrWhiteSpace(subjectName) ? null : subjectName,
                Section = section
            });
            created++;
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = $"Course assignments imported for {activeSemester.DisplayName}: {created} created, {skipped} skipped.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> ImportEnrollments(IFormFile file)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Please select a CSV file.";
            return RedirectToAction(nameof(Index));
        }

        var activeSemester = await _db.Semesters.FirstOrDefaultAsync(s => s.IsActive);
        if (activeSemester is null)
        {
            TempData["Error"] = "No active semester found. Please create and activate a semester first.";
            return RedirectToAction(nameof(Index));
        }

        var lines = await ReadCsvLines(file);
        if (lines.Count < 2) { TempData["Error"] = "CSV file is empty or has no data rows."; return RedirectToAction(nameof(Index)); }

        int created = 0, skipped = 0;

        foreach (var fields in lines.Skip(1))
        {
            // StudentEmail,CourseCode,Section
            if (fields.Length < 3) continue;

            var studentEmail = fields[0].Trim();
            var courseKey = CourseCodes.Key(fields[1]);
            var section = fields[2].Trim();

            var student = await _userManager.FindByEmailAsync(studentEmail);
            if (student is null) { skipped++; continue; }

            var subject = await _db.FacultySubjects.FirstOrDefaultAsync(s =>
                s.CourseKey == courseKey && s.Section == section && s.SemesterId == activeSemester.Id);

            if (subject is null) { skipped++; continue; }

            var exists = await _db.StudentEnrollments.AnyAsync(e =>
                e.FacultySubjectId == subject.Id && e.StudentId == student.Id);

            if (exists) { skipped++; continue; }

            _db.StudentEnrollments.Add(new StudentEnrollment
            {
                FacultySubjectId = subject.Id,
                StudentId = student.Id
            });
            created++;
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = $"Enrollments imported: {created} created, {skipped} skipped.";
        return RedirectToAction(nameof(Index));
    }
    [Authorize(Roles = "Admin")]
    [HttpPost]
    public async Task<IActionResult> GenerateSampleEvaluations([FromServices] DemoDataSeeder seeder)
    {
        var periodCount = await _db.EvaluationPeriods.CountAsync();
        if (periodCount == 0)
        {
            TempData["Error"] = "No evaluation periods found. Create at least one evaluation period first.";
            return RedirectToAction(nameof(Index));
        }

        var faculty = await _userManager.GetUsersInRoleAsync("Faculty");
        var students = await _userManager.GetUsersInRoleAsync("Student");

        if (faculty.Count == 0 || students.Count == 0)
        {
            TempData["Error"] = "No faculty or students found. Import users first.";
            return RedirectToAction(nameof(Index));
        }

        var total = await seeder.GenerateEvaluationsAsync();
        TempData["Success"] = $"{total} sample evaluations generated and results computed.";
        return RedirectToAction(nameof(Index));
    }

    private static async Task<List<string[]>> ReadCsvLines(IFormFile file)
    {
        var lines = new List<string[]>();
        using var reader = new StreamReader(file.OpenReadStream());
        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            lines.Add(ParseCsvLine(line));
        }
        return lines;
    }

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        bool inQuotes = false;
        var current = new System.Text.StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        fields.Add(current.ToString());
        return fields.ToArray();
    }
}
