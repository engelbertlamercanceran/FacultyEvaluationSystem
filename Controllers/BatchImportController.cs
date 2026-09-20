using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using FacultyEvalSystem.Data;
using FacultyEvalSystem.Models;
using FacultyEvalSystem.Services;

namespace FacultyEvalSystem.Controllers;

[Authorize(Roles = "Admin")]
public class BatchImportController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly EvaluationService _evalService;

    public BatchImportController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, EvaluationService evalService)
    {
        _db = db;
        _userManager = userManager;
        _evalService = evalService;
    }

    public IActionResult Index()
    {
        return View();
    }

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
            // FacultyEmail,SubjectCode,SubjectName,Section
            if (fields.Length < 4) continue;

            var facultyEmail = fields[0].Trim();
            var subjectCode = fields[1].Trim();
            var subjectName = fields[2].Trim();
            var section = fields[3].Trim();

            var faculty = await _userManager.FindByEmailAsync(facultyEmail);
            if (faculty is null) { skipped++; continue; }

            var exists = await _db.FacultySubjects.AnyAsync(s =>
                s.FacultyId == faculty.Id && s.SubjectCode == subjectCode &&
                s.Section == section && s.SemesterId == activeSemester.Id);

            if (exists) { skipped++; continue; }

            _db.FacultySubjects.Add(new FacultySubject
            {
                FacultyId = faculty.Id,
                SemesterId = activeSemester.Id,
                SubjectCode = subjectCode,
                SubjectName = subjectName,
                Section = section
            });
            created++;
        }

        await _db.SaveChangesAsync();
        TempData["Success"] = $"Subjects imported for {activeSemester.DisplayName}: {created} created, {skipped} skipped.";
        return RedirectToAction(nameof(Index));
    }

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
            // StudentEmail,SubjectCode,Section
            if (fields.Length < 3) continue;

            var studentEmail = fields[0].Trim();
            var subjectCode = fields[1].Trim();
            var section = fields[2].Trim();

            var student = await _userManager.FindByEmailAsync(studentEmail);
            if (student is null) { skipped++; continue; }

            var subject = await _db.FacultySubjects.FirstOrDefaultAsync(s =>
                s.SubjectCode == subjectCode && s.Section == section && s.SemesterId == activeSemester.Id);

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
    [HttpPost]
    public async Task<IActionResult> QuickDemoSetup([FromServices] IWebHostEnvironment env)
    {
        // Check if demo data already exists
        var existingFaculty = await _userManager.GetUsersInRoleAsync("Faculty");
        if (existingFaculty.Count > 0)
        {
            TempData["Error"] = "Demo data already exists. Drop the database and restart to run setup again.";
            return RedirectToAction(nameof(Index));
        }

        var samplesPath = Path.Combine(env.WebRootPath, "samples");
        var log = new List<string>();

        // === 1. Colleges & Programs ===
        var collegeLines = await ReadCsvFile(Path.Combine(samplesPath, "colleges_programs.csv"));
        foreach (var fields in collegeLines.Skip(1))
        {
            if (fields.Length < 2) continue;
            var collegeName = fields[0].Trim();
            var collegeCode = fields[1].Trim();
            if (string.IsNullOrWhiteSpace(collegeCode)) continue;

            var college = await _db.Colleges.FirstOrDefaultAsync(c => c.Code == collegeCode);
            if (college is null)
            {
                college = new College { Name = collegeName, Code = collegeCode };
                _db.Colleges.Add(college);
                await _db.SaveChangesAsync();
            }

            if (fields.Length >= 4 && !string.IsNullOrWhiteSpace(fields[3]))
            {
                var programCode = fields[3].Trim();
                if (!await _db.AcademicPrograms.AnyAsync(p => p.Code == programCode))
                {
                    _db.AcademicPrograms.Add(new AcademicProgram
                    {
                        Name = fields[2].Trim(),
                        Code = programCode,
                        CollegeId = college.Id
                    });
                }
            }
        }
        await _db.SaveChangesAsync();
        log.Add($"{await _db.Colleges.CountAsync()} colleges, {await _db.AcademicPrograms.CountAsync()} programs");

        // === 2. Users ===
        var userLines = await ReadCsvFile(Path.Combine(samplesPath, "users.csv"));
        int usersCreated = 0;
        foreach (var fields in userLines.Skip(1))
        {
            if (fields.Length < 5) continue;
            var email = fields[0].Trim();
            if (string.IsNullOrWhiteSpace(email)) continue;
            if (await _userManager.FindByEmailAsync(email) is not null) continue;

            var role = fields[3].Trim();
            var collegeCode = fields.Length > 7 ? fields[7].Trim() : "";
            var programCode = fields.Length > 8 ? fields[8].Trim() : "";

            var college = string.IsNullOrWhiteSpace(collegeCode) ? null
                : await _db.Colleges.FirstOrDefaultAsync(c => c.Code == collegeCode);
            var program = string.IsNullOrWhiteSpace(programCode) ? null
                : await _db.AcademicPrograms.FirstOrDefaultAsync(p => p.Code == programCode);

            var user = new ApplicationUser
            {
                UserName = email,
                Email = email,
                FirstName = fields[1].Trim(),
                LastName = fields[2].Trim(),
                EmployeeNumber = string.IsNullOrWhiteSpace(fields[5]) ? null : fields[5].Trim(),
                StudentNumber = fields.Length > 6 && !string.IsNullOrWhiteSpace(fields[6]) ? fields[6].Trim() : null,
                CollegeId = college?.Id,
                ProgramId = program?.Id,
                EmailConfirmed = true,
                IsActive = true
            };

            var result = await _userManager.CreateAsync(user, fields[4].Trim());
            if (!result.Succeeded) continue;

            await _userManager.AddToRoleAsync(user, role);
            usersCreated++;
        }
        log.Add($"{usersCreated} users created");

        // === 3. Five semesters, so the dashboard's five-semester history (DA-01) is exercised ===
        if (!_db.Semesters.Any())
        {
            var terms = new[]
            {
                ("2023-2024", "1st Semester"),
                ("2023-2024", "2nd Semester"),
                ("2024-2025", "1st Semester"),
                ("2024-2025", "2nd Semester"),
                ("2025-2026", "1st Semester"),
            };

            for (int i = 0; i < terms.Length; i++)
            {
                var (ay, term) = terms[i];
                _db.Semesters.Add(new Semester
                {
                    AcademicYear = ay,
                    Term = term,
                    IsActive = i == terms.Length - 1,   // newest is the active one
                    SortKey = Semester.ComputeSortKey(ay, term)
                });
            }
            await _db.SaveChangesAsync();
        }

        var semesters = await _db.Semesters.OrderBy(s => s.SortKey).ToListAsync();
        log.Add($"{semesters.Count} semesters created");

        // === 4. Teaching assignments for EVERY semester ===
        // Each course records the program that owns it (SUP-02), which is what lets a
        // Program Chair see faculty from other departments teaching in their program (PC-01).
        var subjectLines = await ReadCsvFile(Path.Combine(samplesPath, "subjects.csv"));
        int subjectsCreated = 0;

        foreach (var semester in semesters)
        {
            foreach (var fields in subjectLines.Skip(1))
            {
                if (fields.Length < 4) continue;

                var faculty = await _userManager.FindByEmailAsync(fields[0].Trim());
                if (faculty is null) continue;

                var subjectCode = fields[1].Trim();
                var section = fields[3].Trim();
                var programCode = fields.Length > 4 ? fields[4].Trim() : "";

                if (await _db.FacultySubjects.AnyAsync(s => s.FacultyId == faculty.Id
                        && s.SubjectCode == subjectCode && s.Section == section
                        && s.SemesterId == semester.Id))
                    continue;

                var program = string.IsNullOrWhiteSpace(programCode) ? null
                    : await _db.AcademicPrograms.FirstOrDefaultAsync(p => p.Code == programCode);

                _db.FacultySubjects.Add(new FacultySubject
                {
                    FacultyId = faculty.Id,
                    SemesterId = semester.Id,
                    SubjectCode = subjectCode,
                    SubjectName = fields[2].Trim(),
                    Section = section,
                    ProgramId = program?.Id,
                    TeachingMemoNumber = $"TM-{semester.AcademicYear.Split('-')[0]}-{semester.Id:D3}"
                });
                subjectsCreated++;
            }
        }
        await _db.SaveChangesAsync();
        log.Add($"{subjectsCreated} teaching assignments across {semesters.Count} semesters");

        // === 5. Enrollments for every semester ===
        var enrollLines = await ReadCsvFile(Path.Combine(samplesPath, "enrollments.csv"));
        int enrollsCreated = 0;

        foreach (var semester in semesters)
        {
            foreach (var fields in enrollLines.Skip(1))
            {
                if (fields.Length < 3) continue;

                var student = await _userManager.FindByEmailAsync(fields[0].Trim());
                if (student is null) continue;

                var subject = await _db.FacultySubjects.FirstOrDefaultAsync(s =>
                    s.SubjectCode == fields[1].Trim() && s.Section == fields[2].Trim()
                    && s.SemesterId == semester.Id);
                if (subject is null) continue;

                if (await _db.StudentEnrollments.AnyAsync(e => e.FacultySubjectId == subject.Id
                        && e.StudentId == student.Id))
                    continue;

                _db.StudentEnrollments.Add(new StudentEnrollment
                {
                    FacultySubjectId = subject.Id,
                    StudentId = student.Id
                });
                enrollsCreated++;
            }
        }
        await _db.SaveChangesAsync();
        log.Add($"{enrollsCreated} enrollments created");

        // === 6. One end-of-semester evaluation per semester ===
        // Per CMO No. 19 and the change request, evaluation runs once, at the end of the
        // semester — students first, then supervisors. There is no midterm evaluation.
        if (!_db.EvaluationPeriods.Any())
        {
            for (int i = 0; i < semesters.Count; i++)
            {
                var semester = semesters[i];
                var isCurrent = i == semesters.Count - 1;

                // 1st Semester ends around October, 2nd Semester around March
                int startYear = int.Parse(semester.AcademicYear.Split('-')[0]);
                var (endMonth, endYear) = semester.Term == "1st Semester"
                    ? (10, startYear)
                    : (3, startYear + 1);

                var start = new DateTime(endYear, endMonth, 1);

                _db.EvaluationPeriods.Add(new EvaluationPeriod
                {
                    Name = "End-of-Semester Evaluation",
                    SemesterId = semester.Id,
                    StartDate = start,
                    EndDate = start.AddDays(30),
                    // Past semesters are finished; the current one is open to students.
                    Stage = isCurrent ? EvaluationStage.StudentEvaluation : EvaluationStage.Closed,
                    StudentStageOpenedAt = start,
                    SupervisorStageOpenedAt = isCurrent ? null : start.AddDays(10),
                    DeanStageOpenedAt = isCurrent ? null : start.AddDays(18),
                    ClosedAt = isCurrent ? null : start.AddDays(30)
                });
            }
            await _db.SaveChangesAsync();
        }
        log.Add($"{await _db.EvaluationPeriods.CountAsync()} end-of-semester evaluation periods");

        // === 7. Sample evaluations ===
        var generated = await GenerateEvaluationsAsync();
        log.Add($"{generated} evaluations generated & results computed");

        // === 8. Development plans for the closed periods ===
        var plansCreated = await GenerateDevelopmentPlansAsync();
        log.Add($"{plansCreated} development plans recorded");

        TempData["Success"] = "Demo setup complete: " + string.Join(" → ", log);
        return RedirectToAction(nameof(Index));
    }


    private static async Task<List<string[]>> ReadCsvFile(string path)
    {
        var lines = new List<string[]>();
        foreach (var line in await System.IO.File.ReadAllLinesAsync(path))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            lines.Add(ParseCsvLine(line));
        }
        return lines;
    }
    [HttpPost]
    public async Task<IActionResult> GenerateSampleEvaluations()
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

        var total = await GenerateEvaluationsAsync();
        TempData["Success"] = $"{total} sample evaluations generated and results computed.";
        return RedirectToAction(nameof(Index));
    }

    private static readonly string[] StudentComments =
    [
        "Very approachable and explains lessons clearly.",
        "Good instructor overall. Makes the subject interesting.",
        "Sometimes rushes through topics. Needs to slow down a bit.",
        "Very knowledgeable and passionate about teaching.",
        "Gives clear examples that relate to real-life situations.",
        "Could improve on time management during class.",
        "One of the best teachers I've had. Very patient and understanding.",
        "Provides helpful feedback on assignments and projects.",
        "Very organized and always comes prepared for class.",
        "Fair and consistent in grading. Explains rubrics well.",
        "Makes difficult concepts easy to understand.",
        "Always willing to help students during consultation hours.",
    ];

    private static readonly string[] SupervisorComments =
    [
        "Faculty demonstrates strong commitment to teaching. Recommended for continued professional development.",
        "Consistently meets instructional objectives. Should explore innovative teaching methodologies.",
        "Shows dedication to subject mastery. Encourage participation in research activities.",
        "Good classroom management skills. Consider mentoring junior faculty members.",
        "Demonstrates effective use of technology in instruction.",
        "Active in community extension activities. Maintain engagement in professional organizations.",
        "Reliable and punctual. Shows improvement from previous evaluation period.",
        "Strong pedagogical skills. Recommended for advanced teaching certification training.",
        "Contributes positively to department activities. Should pursue further graduate studies.",
        "Effective communicator with students. Continue developing assessment strategies.",
    ];

    /// <summary>
    /// Generates student and supervisor evaluations for every period.
    ///
    /// Supervisor evaluations follow the same rules the live rosters use, so the demo
    /// data matches what the system would actually produce:
    ///   PC-01  each faculty member is evaluated by the Chair of every program that
    ///          owns a course they teach — including Chairs of other departments;
    ///   DN-01  each Program Chair is evaluated by the Dean of their college;
    ///   AA-01  a Chair whose college has no Dean is left for the AA account.
    /// </summary>
    private async Task<int> GenerateEvaluationsAsync()
    {
        var periods = await _db.EvaluationPeriods
            .Include(p => p.Semester)
            .OrderBy(p => p.Semester.SortKey)
            .ToListAsync();

        var allFaculty = await _userManager.GetUsersInRoleAsync("Faculty");
        var chairs = (await _userManager.GetUsersInRoleAsync("ProgramChair")).Where(c => c.IsActive).ToList();
        var deans = (await _userManager.GetUsersInRoleAsync("Dean")).Where(d => d.IsActive).ToList();

        var studentCriteria = await _db.EvaluationCriteria
            .Include(c => c.Category).Where(c => c.Category.EvaluatorType == "Student").ToListAsync();
        var supervisorCriteria = await _db.EvaluationCriteria
            .Include(c => c.Category).Where(c => c.Category.EvaluatorType == "Supervisor").ToListAsync();

        var rng = new Random(42);
        int totalEvals = 0;

        // Ratings drift upward across semesters so the five-semester trend (DA-01) is
        // visible. A rating is (total score / 75) x 100, so a band of [min,max] averages
        // to ((min+max)/2 / 5) x 100 — e.g. [3,5] lands near 80, [4,5] near 90.
        // The spread is chosen to look like a real college: most faculty "Often
        // Manifested" (61-90), a couple "Always Manifested" (91+), and one or two
        // consistently weak so the low-performer panel and development plans have
        // something real to show.
        var profilesBySemester = new (int Min, int Max)[][]
        {
            [(3,4),(3,5),(3,4),(4,5),(2,4),(3,4),(2,4),(4,5),(3,4),(2,4)], // ~72 avg
            [(3,4),(3,5),(3,5),(4,5),(2,4),(3,4),(3,4),(4,5),(3,5),(2,4)], // ~75
            [(3,5),(4,5),(3,5),(4,5),(3,4),(3,5),(3,4),(4,5),(3,5),(2,4)], // ~79
            [(4,5),(4,5),(3,5),(4,5),(3,4),(4,5),(3,5),(5,5),(4,5),(3,4)], // ~85
            [(4,5),(4,5),(4,5),(5,5),(3,5),(4,5),(3,5),(5,5),(4,5),(3,4)], // ~88
        };

        for (int pi = 0; pi < periods.Count; pi++)
        {
            var period = periods[pi];
            var profiles = profilesBySemester[Math.Min(pi, profilesBySemester.Length - 1)];

            // A period that is still at the student stage is genuinely mid-flight:
            // only some students have submitted, supervisors have not started, and
            // results are not computed until AA Staff closes it. Generating a full
            // set here would show data that the staged workflow could not have
            // produced yet (AAS-03).
            bool studentStageInProgress = period.Stage == EvaluationStage.StudentEvaluation;
            bool supervisorsHaveSubmitted = period.Stage == EvaluationStage.Closed;

            // Assignments in this semester, so supervisors can be derived from teaching load
            var assignments = await _db.FacultySubjects
                .Where(s => s.SemesterId == period.SemesterId)
                .ToListAsync();

            if (assignments.Count == 0) continue;

            var enrollments = await _db.StudentEnrollments
                .Where(e => e.FacultySubject.SemesterId == period.SemesterId)
                .ToListAsync();

            // ---- Student evaluations: one per enrolled student per class ----
            for (int f = 0; f < allFaculty.Count; f++)
            {
                var fac = allFaculty[f];
                var profile = profiles[f % profiles.Length];
                var facSubjects = assignments.Where(s => s.FacultyId == fac.Id).ToList();

                foreach (var subject in facSubjects)
                {
                    var enrolled = enrollments.Where(e => e.FacultySubjectId == subject.Id).ToList();

                    // Roughly two thirds submitted so far while the stage is still open
                    if (studentStageInProgress)
                        enrolled = enrolled.Where((_, idx) => idx % 3 != 2).ToList();

                    foreach (var enrollment in enrolled)
                    {
                        if (await _db.Evaluations.AnyAsync(ev => ev.EvaluatorId == enrollment.StudentId
                                && ev.FacultyId == fac.Id && ev.FacultySubjectId == subject.Id
                                && ev.EvaluationPeriodId == period.Id))
                            continue;

                        _db.Evaluations.Add(new Evaluation
                        {
                            EvaluationPeriodId = period.Id,
                            FacultyId = fac.Id,
                            EvaluatorId = enrollment.StudentId,
                            EvaluatorType = "Student",
                            FacultySubjectId = subject.Id,
                            SubmittedAt = period.StartDate.AddDays(rng.Next(0, 8)),
                            Comments = rng.Next(0, 5) <= 1 ? StudentComments[rng.Next(StudentComments.Length)] : null,
                            Responses = studentCriteria.Select(c => new EvaluationResponse
                            {
                                CriterionId = c.Id,
                                Rating = Math.Clamp(rng.Next(profile.Min, profile.Max + 1), 1, 5)
                            }).ToList()
                        });
                        totalEvals++;
                    }
                }

                // ---- PC-01: the Chair of every program owning a course this faculty teaches ----
                var owningProgramIds = facSubjects
                    .Where(s => s.ProgramId is not null)
                    .Select(s => s.ProgramId!.Value)
                    .Distinct()
                    .ToList();

                foreach (var programId in owningProgramIds)
                {
                    if (!supervisorsHaveSubmitted) break;

                    var chair = chairs.FirstOrDefault(c => c.ProgramId == programId && c.Id != fac.Id);
                    if (chair is null) continue;

                    if (await _db.Evaluations.AnyAsync(ev => ev.EvaluatorId == chair.Id
                            && ev.FacultyId == fac.Id && ev.EvaluationPeriodId == period.Id))
                        continue;

                    _db.Evaluations.Add(new Evaluation
                    {
                        EvaluationPeriodId = period.Id,
                        FacultyId = fac.Id,
                        EvaluatorId = chair.Id,
                        EvaluatorType = "Supervisor",
                        SubmittedAt = period.StartDate.AddDays(rng.Next(10, 18)),
                        Comments = SupervisorComments[f % SupervisorComments.Length],
                        Responses = supervisorCriteria.Select(c => new EvaluationResponse
                        {
                            CriterionId = c.Id,
                            Rating = Math.Clamp(rng.Next(profile.Min, profile.Max + 1), 1, 5)
                        }).ToList()
                    });
                    totalEvals++;
                }
            }

            // ---- DN-01: each Program Chair is evaluated by the Dean of their college ----
            for (int ci = 0; ci < chairs.Count && supervisorsHaveSubmitted; ci++)
            {
                var chair = chairs[ci];
                var dean = deans.FirstOrDefault(d => d.CollegeId == chair.CollegeId);

                // AA-01: a Chair with no Dean in their college is left for the AA account,
                // so no evaluation is generated for them here.
                if (dean is null) continue;

                if (await _db.Evaluations.AnyAsync(ev => ev.EvaluatorId == dean.Id
                        && ev.FacultyId == chair.Id && ev.EvaluationPeriodId == period.Id))
                    continue;

                var profile = profiles[ci % profiles.Length];
                _db.Evaluations.Add(new Evaluation
                {
                    EvaluationPeriodId = period.Id,
                    FacultyId = chair.Id,
                    EvaluatorId = dean.Id,
                    EvaluatorType = "Supervisor",
                    SubmittedAt = period.StartDate.AddDays(rng.Next(18, 26)),
                    Comments = SupervisorComments[ci % SupervisorComments.Length],
                    Responses = supervisorCriteria.Select(c => new EvaluationResponse
                    {
                        CriterionId = c.Id,
                        Rating = Math.Clamp(rng.Next(profile.Min, profile.Max + 1), 1, 5)
                    }).ToList()
                });
                totalEvals++;
            }

            await _db.SaveChangesAsync();

            // Results are computed when AA Staff closes the period (AAS-03)
            if (period.Stage == EvaluationStage.Closed)
                await _evalService.ComputeResultsAsync(period.Id);
        }

        return totalEvals;
    }

    /// <summary>
    /// PC-02/FC-02: for closed periods, records the Development Plan the supervisor
    /// entered and — for all but the most recent closed period — the faculty member's
    /// acknowledgement, so every FEDAF status is represented in the demo data.
    /// </summary>
    private async Task<int> GenerateDevelopmentPlansAsync()
    {
        var closedPeriods = await _db.EvaluationPeriods
            .Include(p => p.Semester)
            .Where(p => p.Stage == EvaluationStage.Closed)
            .OrderBy(p => p.Semester.SortKey)
            .ToListAsync();

        if (closedPeriods.Count == 0) return 0;

        var areas = new[]
        {
            "Pacing of lectures during the second half of the term.",
            "Turnaround time on returning graded outputs.",
            "Use of ICT tools and platforms in classroom delivery.",
            "Consistency of consultation hours.",
            "Alignment of assessments with the stated learning outcomes.",
        };
        var activities = new[]
        {
            "Attend the campus workshop on outcomes-based assessment.",
            "Enrol in the LMS facilitation course offered by the ICT office.",
            "Join the college mentoring circle for one semester.",
            "Complete the CHED webinar series on learner-centred teaching.",
            "Sit in on two peer classes and submit a reflection.",
        };
        var actions = new[]
        {
            "Submit a revised course pack before the next term begins.",
            "Return all graded outputs within one week, reviewed monthly by the Chair.",
            "Publish and observe fixed consultation hours from the first week of the term.",
            "Map every assessment to a learning outcome in the syllabus.",
            "Report progress to the Chair at midterm and end of term.",
        };

        int created = 0;
        int n = 0;

        // The most recent closed period is left awaiting acknowledgement so the
        // "Development Plan Entered" status is visible alongside "Acknowledged".
        var latestClosed = closedPeriods[^1];

        foreach (var period in closedPeriods)
        {
            var results = await _db.EvaluationResults
                .Where(r => r.EvaluationPeriodId == period.Id)
                .ToListAsync();

            foreach (var result in results)
            {
                if (await _db.DevelopmentPlans.AnyAsync(d => d.EvaluationPeriodId == period.Id
                        && d.FacultyId == result.FacultyId))
                    continue;

                var supervisor = await _db.Evaluations
                    .Where(e => e.EvaluationPeriodId == period.Id
                             && e.FacultyId == result.FacultyId
                             && e.EvaluatorType == "Supervisor")
                    .Select(e => e.EvaluatorId)
                    .FirstOrDefaultAsync();

                if (supervisor is null) continue;

                var acknowledged = period.Id != latestClosed.Id;

                _db.DevelopmentPlans.Add(new DevelopmentPlan
                {
                    EvaluationPeriodId = period.Id,
                    FacultyId = result.FacultyId,
                    AreasForImprovement = areas[n % areas.Length],
                    ProposedActivities = activities[n % activities.Length],
                    ActionPlan = actions[n % actions.Length],
                    SupervisorSubmittedAt = period.EndDate.AddDays(-5),
                    SupervisorId = supervisor,
                    AcknowledgedAt = acknowledged ? period.EndDate.AddDays(-2) : null,
                    AcknowledgedById = acknowledged ? result.FacultyId : null
                });

                created++;
                n++;
            }
        }

        await _db.SaveChangesAsync();
        return created;
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
