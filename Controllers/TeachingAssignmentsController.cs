using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FacultyEvalSystem.Data;
using FacultyEvalSystem.Models;
using FacultyEvalSystem.Services;

namespace FacultyEvalSystem.Controllers;

/// <summary>
/// Teaching Assignments: who teaches which course in the active semester. One
/// page combines the imports (Teaching Memo CSV, and for the Administrator the
/// Faculty Workload Summary), adding a single assignment by hand, and editing or
/// deleting existing ones.
///
/// AAS-01 / AAS-02: CDI and CDI Staff import the Teaching Memo, which is the source
/// of faculty teaching assignments for a semester. This responsibility transferred
/// from the MIS Staff account. The Faculty Workload Summary, which also creates
/// faculty accounts, is imported by the Administrator only.
///
/// The memo format is the one assumed in OPEN-01 of the requirements spec; the
/// owning-program column is what allows a Program Chair to be shown the faculty
/// teaching courses within their program (PC-01 / SUP-02).
/// </summary>
[Authorize(Roles = "AA,AAStaff,Admin")]
public class TeachingAssignmentsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly DataImportService _import;

    public TeachingAssignmentsController(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        DataImportService import)
    {
        _db = db;
        _userManager = userManager;
        _import = import;
    }

    public static readonly string[] ExpectedColumns =
        ["MemoNumber", "FacultyEmail", "CourseCode", "CourseTitle", "Section", "ProgramCode"];

    public async Task<IActionResult> Index(string? search)
    {
        var activeSemester = await _db.Semesters.FirstOrDefaultAsync(s => s.IsActive);
        ViewBag.ActiveSemester = activeSemester;
        ViewBag.Search = search;

        var assignments = new List<FacultySubject>();
        if (activeSemester is not null)
        {
            var query = _db.FacultySubjects
                .Include(fs => fs.Faculty)
                .Include(fs => fs.Program)
                .Where(fs => fs.SemesterId == activeSemester.Id);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(fs =>
                    fs.Faculty.FirstName.ToLower().Contains(term) ||
                    fs.Faculty.LastName.ToLower().Contains(term) ||
                    fs.CourseCode.ToLower().Contains(term) ||
                    (fs.CourseTitle != null && fs.CourseTitle.ToLower().Contains(term)) ||
                    (fs.Section != null && fs.Section.ToLower().Contains(term)) ||
                    (fs.Program != null && fs.Program.Code.ToLower().Contains(term)));
            }

            assignments = await query
                .OrderBy(fs => fs.Faculty.LastName).ThenBy(fs => fs.Faculty.FirstName).ThenBy(fs => fs.CourseCode)
                .ToListAsync();
        }

        ViewBag.Assignments = assignments;
        ViewBag.Evaluated = await EvaluatedIdsAsync(assignments.Select(a => a.Id));
        ViewBag.Faculty = await TeachingStaffAsync();
        ViewBag.Programs = await ProgramsAsync();

        return View();
    }

    /// <summary>Downloadable template showing the expected memo columns.</summary>
    public IActionResult Template()
    {
        var csv = string.Join(",", ExpectedColumns) + "\n"
                + "TM-2026-001,dominic.cabauatan@isuc.edu.ph,IT 101,Introduction to Computing,BSIT 1A,BSIT\n"
                + "TM-2026-001,phillip.dumlao@isuc.edu.ph,GEC 1,Understanding the Self,BSIT 1A,BSIT\n";

        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv", "teaching_memo_template.csv");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(IFormFile file, bool replaceExisting = false)
    {
        if (file is null || file.Length == 0)
        {
            TempData["Error"] = "Please select a Teaching Memo file.";
            return RedirectToAction(nameof(Index));
        }

        var activeSemester = await _db.Semesters.FirstOrDefaultAsync(s => s.IsActive);
        if (activeSemester is null)
        {
            TempData["Error"] = "No active semester. Please ask the Administrator to create one first.";
            return RedirectToAction(nameof(Index));
        }

        var rows = await ReadCsvAsync(file);
        if (rows.Count < 2)
        {
            TempData["Error"] = "The file is empty or contains only a header row.";
            return RedirectToAction(nameof(Index));
        }

        var header = rows[0].Select(h => h.Trim()).ToList();
        int Col(params string[] names) => header.FindIndex(h => names.Any(n => h.Equals(n, StringComparison.OrdinalIgnoreCase)));

        // Older memos label the column "SubjectCode"; the system calls it Course Code
        int iMemo = Col("MemoNumber"), iEmail = Col("FacultyEmail"), iCode = Col("CourseCode", "SubjectCode"),
            iName = Col("CourseTitle", "SubjectName"), iSection = Col("Section"), iProgram = Col("ProgramCode");

        if (iEmail < 0 || iCode < 0)
        {
            TempData["Error"] = "The file is missing required columns. It must contain at least "
                              + "FacultyEmail and CourseCode. "
                              + "Download the template to see the expected format.";
            return RedirectToAction(nameof(Index));
        }

        var programs = await _db.AcademicPrograms.ToListAsync();

        if (replaceExisting)
        {
            // Remove assignments that have no evaluations recorded against them,
            // so re-importing a corrected memo does not leave stale rows behind.
            var existing = await _db.FacultySubjects
                .Where(fs => fs.SemesterId == activeSemester.Id)
                .ToListAsync();

            var usedIds = await _db.Evaluations
                .Where(e => e.FacultySubjectId != null)
                .Select(e => e.FacultySubjectId!.Value)
                .Distinct()
                .ToListAsync();

            var removable = existing.Where(fs => !usedIds.Contains(fs.Id)).ToList();

            // Enrollments reference the assignment, so clear those first
            var removableIds = removable.Select(fs => fs.Id).ToList();
            var enrollments = await _db.StudentEnrollments
                .Where(se => removableIds.Contains(se.FacultySubjectId))
                .ToListAsync();

            _db.StudentEnrollments.RemoveRange(enrollments);
            _db.FacultySubjects.RemoveRange(removable);
            await _db.SaveChangesAsync();
        }

        int created = 0, updated = 0;
        var problems = new List<string>();

        for (int r = 1; r < rows.Count; r++)
        {
            var f = rows[r];
            if (f.Length == 0 || f.All(string.IsNullOrWhiteSpace)) continue;

            string Get(int i) => i >= 0 && i < f.Length ? f[i].Trim() : "";

            var email = Get(iEmail);
            var code = CourseCodes.Clean(Get(iCode));
            var key = CourseCodes.Key(code);
            var name = Get(iName);
            var section = Get(iSection);
            var programCode = Get(iProgram);
            var memoNo = Get(iMemo);

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
            {
                problems.Add($"Row {r + 1}: missing faculty e-mail or course code.");
                continue;
            }

            var faculty = await _userManager.FindByEmailAsync(email);
            if (faculty is null)
            {
                problems.Add($"Row {r + 1}: no account found for {email}.");
                continue;
            }

            AcademicProgram? program = null;
            if (!string.IsNullOrWhiteSpace(programCode))
            {
                program = programs.FirstOrDefault(p =>
                    p.Code.Equals(programCode, StringComparison.OrdinalIgnoreCase));

                if (program is null)
                    problems.Add($"Row {r + 1}: program code \"{programCode}\" is not registered — "
                               + "the assignment was saved without an owning program.");
            }
            else
            {
                problems.Add($"Row {r + 1}: no program code given for {code}. No Program Chair "
                           + "will be able to evaluate this course until one is set.");
            }

            var existing = await _db.FacultySubjects.FirstOrDefaultAsync(fs =>
                fs.FacultyId == faculty.Id &&
                fs.CourseKey == key &&
                fs.Section == section &&
                fs.SemesterId == activeSemester.Id);

            if (existing is not null)
            {
                existing.CourseTitle = string.IsNullOrWhiteSpace(name) ? existing.CourseTitle : name;
                existing.ProgramId = program?.Id ?? existing.ProgramId;
                existing.TeachingMemoNumber = string.IsNullOrWhiteSpace(memoNo) ? existing.TeachingMemoNumber : memoNo;
                updated++;
            }
            else
            {
                _db.FacultySubjects.Add(new FacultySubject
                {
                    FacultyId = faculty.Id,
                    SemesterId = activeSemester.Id,
                    CourseCode = code,
                    CourseKey = key,
                    CourseTitle = string.IsNullOrWhiteSpace(name) ? null : name,
                    Section = string.IsNullOrWhiteSpace(section) ? null : section,
                    ProgramId = program?.Id,
                    TeachingMemoNumber = string.IsNullOrWhiteSpace(memoNo) ? null : memoNo
                });
                created++;
            }
        }

        await _db.SaveChangesAsync();

        // Link students from the Registrar's masterlist to these assignments
        var link = await _import.LinkEnrollmentsAsync(activeSemester.Id);

        TempData["Success"] = $"Teaching Memo imported for {activeSemester.DisplayName}: "
                            + $"{created} assignment(s) created, {updated} updated; "
                            + $"{link.EnrollmentsCreated} student enrollment(s) linked.";

        if (problems.Count > 0)
            TempData["Error"] = $"{problems.Count} row(s) need attention: "
                              + string.Join(" ", problems.Take(8))
                              + (problems.Count > 8 ? $" (+{problems.Count - 8} more)" : "");

        return RedirectToAction(nameof(Index));
    }

    /// <summary>Adds one teaching assignment to the active semester by hand.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(string facultyId, string courseCode, string? courseTitle,
        string? section, int? programId, string? memoNumber)
    {
        var activeSemester = await _db.Semesters.FirstOrDefaultAsync(s => s.IsActive);
        if (activeSemester is null)
        {
            TempData["Error"] = "No active semester. Please ask the Administrator to create one first.";
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(facultyId) || string.IsNullOrWhiteSpace(courseCode))
        {
            TempData["Error"] = "Faculty and Course Code are required.";
            return RedirectToAction(nameof(Index));
        }

        var key = CourseCodes.Key(courseCode);
        var cleanSection = Blank(section);
        if (await _db.FacultySubjects.AnyAsync(fs => fs.FacultyId == facultyId && fs.CourseKey == key
                                                  && fs.Section == cleanSection && fs.SemesterId == activeSemester.Id))
        {
            TempData["Error"] = "That faculty member is already assigned to this course and section.";
            return RedirectToAction(nameof(Index));
        }

        _db.FacultySubjects.Add(new FacultySubject
        {
            FacultyId = facultyId,
            SemesterId = activeSemester.Id,
            CourseCode = CourseCodes.Clean(courseCode),
            CourseKey = key,
            CourseTitle = Blank(courseTitle),
            Section = cleanSection,
            ProgramId = programId,
            TeachingMemoNumber = Blank(memoNumber)
        });
        await _db.SaveChangesAsync();

        var link = await _import.LinkEnrollmentsAsync(activeSemester.Id);
        TempData["Success"] = $"Course assigned; {link.EnrollmentsCreated} student enrollment(s) linked.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet]
    public async Task<IActionResult> Edit(int id)
    {
        var assignment = await _db.FacultySubjects
            .Include(fs => fs.Faculty)
            .Include(fs => fs.Semester)
            .FirstOrDefaultAsync(fs => fs.Id == id);
        if (assignment is null) return NotFound();

        ViewBag.HasEvaluations = await _db.Evaluations.AnyAsync(e => e.FacultySubjectId == id);
        ViewBag.Faculty = await TeachingStaffAsync();
        ViewBag.Programs = await ProgramsAsync();
        return View(assignment);
    }

    /// <summary>
    /// Once evaluations are recorded against an assignment, its faculty member and
    /// Course Code are fixed: changing them would re-attribute submitted evaluations.
    /// The title, section, owning program and memo number can always be corrected.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, string facultyId, string courseCode, string? courseTitle,
        string? section, int? programId, string? memoNumber)
    {
        var assignment = await _db.FacultySubjects.FindAsync(id);
        if (assignment is null) return NotFound();

        if (string.IsNullOrWhiteSpace(facultyId) || string.IsNullOrWhiteSpace(courseCode))
        {
            TempData["Error"] = "Faculty and Course Code are required.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        var key = CourseCodes.Key(courseCode);
        bool teachingChanged = assignment.FacultyId != facultyId || assignment.CourseKey != key;

        if (teachingChanged && await _db.Evaluations.AnyAsync(e => e.FacultySubjectId == id))
        {
            TempData["Error"] = "Evaluations have already been submitted for this assignment, so its "
                              + "faculty member and Course Code can no longer be changed.";
            return RedirectToAction(nameof(Edit), new { id });
        }

        assignment.FacultyId = facultyId;
        assignment.CourseCode = CourseCodes.Clean(courseCode);
        assignment.CourseKey = key;
        assignment.CourseTitle = Blank(courseTitle);
        assignment.Section = Blank(section);
        assignment.ProgramId = programId;
        assignment.TeachingMemoNumber = Blank(memoNumber);

        // The students linked to the old course or instructor no longer apply
        if (teachingChanged)
            _db.StudentEnrollments.RemoveRange(
                await _db.StudentEnrollments.Where(se => se.FacultySubjectId == id).ToListAsync());

        await _db.SaveChangesAsync();

        if (teachingChanged)
            await _import.LinkEnrollmentsAsync(assignment.SemesterId);

        TempData["Success"] = "Teaching assignment updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var assignment = await _db.FacultySubjects.FindAsync(id);
        if (assignment is null) return NotFound();

        if (await _db.Evaluations.AnyAsync(e => e.FacultySubjectId == id))
        {
            TempData["Error"] = "Evaluations have already been submitted for this assignment, so it cannot be deleted.";
            return RedirectToAction(nameof(Index));
        }

        // Enrollments reference the assignment, so clear those first
        _db.StudentEnrollments.RemoveRange(
            await _db.StudentEnrollments.Where(se => se.FacultySubjectId == id).ToListAsync());
        _db.FacultySubjects.Remove(assignment);
        await _db.SaveChangesAsync();

        TempData["Success"] = $"Teaching assignment {assignment.CourseCode} deleted.";
        return RedirectToAction(nameof(Index));
    }

    /// <summary>AAS-02: set the owning program of an assignment without re-importing.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetProgram(int id, int? programId)
    {
        var assignment = await _db.FacultySubjects.FindAsync(id);
        if (assignment is null) return NotFound();

        assignment.ProgramId = programId;
        await _db.SaveChangesAsync();

        TempData["Success"] = "Owning program updated.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<HashSet<int>> EvaluatedIdsAsync(IEnumerable<int> assignmentIds)
    {
        var ids = assignmentIds.ToList();
        return (await _db.Evaluations
            .Where(e => e.FacultySubjectId != null && ids.Contains(e.FacultySubjectId.Value))
            .Select(e => e.FacultySubjectId!.Value)
            .Distinct()
            .ToListAsync()).ToHashSet();
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

    private async Task<List<AcademicProgram>> ProgramsAsync()
        => await _db.AcademicPrograms.Include(p => p.College).OrderBy(p => p.Code).ToListAsync();

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task<List<string[]>> ReadCsvAsync(IFormFile file)
    {
        var rows = new List<string[]>();
        using var reader = new StreamReader(file.OpenReadStream());

        while (await reader.ReadLineAsync() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            rows.Add(ParseCsvLine(line));
        }

        return rows;
    }

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;

        foreach (var c in line)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (c == ',' && !inQuotes)
            {
                fields.Add(current.ToString());
                current.Clear();
            }
            else current.Append(c);
        }

        fields.Add(current.ToString());
        return [.. fields];
    }
}
