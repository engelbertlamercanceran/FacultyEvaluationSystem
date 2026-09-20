using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FacultyEvalSystem.Data;
using FacultyEvalSystem.Models;

namespace FacultyEvalSystem.Controllers;

/// <summary>
/// AAS-01 / AAS-02: AA Staff imports the Teaching Memo, which is the source of
/// faculty teaching assignments for a semester. This responsibility transferred
/// from the MIS Staff account.
///
/// The memo format is the one assumed in OPEN-01 of the requirements spec; the
/// owning-program column is what allows a Program Chair to be shown the faculty
/// teaching courses within their program (PC-01 / SUP-02).
/// </summary>
[Authorize(Roles = "AAStaff,Admin")]
public class TeachingMemoController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public TeachingMemoController(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    public static readonly string[] ExpectedColumns =
        ["MemoNumber", "FacultyEmail", "SubjectCode", "SubjectName", "Section", "ProgramCode"];

    public async Task<IActionResult> Index()
    {
        var activeSemester = await _db.Semesters.FirstOrDefaultAsync(s => s.IsActive);
        ViewBag.ActiveSemester = activeSemester;

        ViewBag.Assignments = activeSemester is not null
            ? await _db.FacultySubjects
                .Include(fs => fs.Faculty)
                .Include(fs => fs.Program)
                .Where(fs => fs.SemesterId == activeSemester.Id)
                .OrderBy(fs => fs.SubjectCode)
                .ToListAsync()
            : [];

        ViewBag.Programs = await _db.AcademicPrograms
            .Include(p => p.College)
            .OrderBy(p => p.Code)
            .ToListAsync();

        return View();
    }

    /// <summary>Downloadable template showing the expected memo columns.</summary>
    public IActionResult Template()
    {
        var csv = string.Join(",", ExpectedColumns) + "\n"
                + "TM-2026-001,jose.santos@isuc.edu.ph,IT 101,Introduction to Computing,BSIT 1A,BSIT\n"
                + "TM-2026-001,anna.cruz@isuc.edu.ph,GE 102,Purposive Communication,BSIT 1A,BSIT\n";

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
        int Col(string name) => header.FindIndex(h => h.Equals(name, StringComparison.OrdinalIgnoreCase));

        int iMemo = Col("MemoNumber"), iEmail = Col("FacultyEmail"), iCode = Col("SubjectCode"),
            iName = Col("SubjectName"), iSection = Col("Section"), iProgram = Col("ProgramCode");

        if (iEmail < 0 || iCode < 0 || iName < 0)
        {
            TempData["Error"] = "The file is missing required columns. It must contain at least "
                              + "FacultyEmail, SubjectCode and SubjectName. "
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
            var code = Get(iCode);
            var name = Get(iName);
            var section = Get(iSection);
            var programCode = Get(iProgram);
            var memoNo = Get(iMemo);

            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(code))
            {
                problems.Add($"Row {r + 1}: missing faculty e-mail or subject code.");
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
                fs.SubjectCode == code &&
                fs.Section == section &&
                fs.SemesterId == activeSemester.Id);

            if (existing is not null)
            {
                existing.SubjectName = name;
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
                    SubjectCode = code,
                    SubjectName = name,
                    Section = string.IsNullOrWhiteSpace(section) ? null : section,
                    ProgramId = program?.Id,
                    TeachingMemoNumber = string.IsNullOrWhiteSpace(memoNo) ? null : memoNo
                });
                created++;
            }
        }

        await _db.SaveChangesAsync();

        TempData["Success"] = $"Teaching Memo imported for {activeSemester.DisplayName}: "
                            + $"{created} assignment(s) created, {updated} updated.";

        if (problems.Count > 0)
            TempData["Error"] = $"{problems.Count} row(s) need attention: "
                              + string.Join(" ", problems.Take(8))
                              + (problems.Count > 8 ? $" (+{problems.Count - 8} more)" : "");

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
