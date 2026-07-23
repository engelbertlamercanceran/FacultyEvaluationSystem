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
                string[] validRoles = ["Admin", "Dean", "ProgramChair", "Faculty", "Student"];
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

        // === 1. Import Colleges & Programs ===
        var collegeLines = await ReadCsvFile(Path.Combine(samplesPath, "colleges_programs.csv"));
        int collegesCreated = 0;
        foreach (var fields in collegeLines.Skip(1))
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
                collegesCreated++;
            }

            if (fields.Length >= 4)
            {
                var progName = fields[2].Trim();
                var progCode = fields[3].Trim();
                if (!string.IsNullOrEmpty(progName) && !string.IsNullOrEmpty(progCode))
                {
                    if (!await _db.AcademicPrograms.AnyAsync(p => p.Code == progCode))
                    {
                        _db.AcademicPrograms.Add(new AcademicProgram { Name = progName, Code = progCode, CollegeId = college.Id });
                        await _db.SaveChangesAsync();
                    }
                }
            }
        }
        log.Add($"Colleges & programs created");

        // === 2. Import Users ===
        var userLines = await ReadCsvFile(Path.Combine(samplesPath, "users.csv"));
        int usersCreated = 0;
        foreach (var fields in userLines.Skip(1))
        {
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

            if (string.IsNullOrEmpty(email) || await _userManager.FindByEmailAsync(email) is not null) continue;

            int? collegeId = null, programId = null;
            if (!string.IsNullOrEmpty(collegeCode))
                collegeId = (await _db.Colleges.FirstOrDefaultAsync(c => c.Code == collegeCode))?.Id;
            if (!string.IsNullOrEmpty(programCode))
                programId = (await _db.AcademicPrograms.FirstOrDefaultAsync(p => p.Code == programCode))?.Id;

            var user = new ApplicationUser
            {
                UserName = email, Email = email, FirstName = firstName, LastName = lastName,
                EmployeeNumber = string.IsNullOrEmpty(empNumber) ? null : empNumber,
                StudentNumber = string.IsNullOrEmpty(stuNumber) ? null : stuNumber,
                CollegeId = collegeId, ProgramId = programId, EmailConfirmed = true
            };
            var result = string.IsNullOrEmpty(password)
                ? await _userManager.CreateAsync(user)
                : await _userManager.CreateAsync(user, password);
            if (result.Succeeded)
            {
                string[] validRoles = ["Admin", "Dean", "ProgramChair", "Faculty", "Student"];
                await _userManager.AddToRoleAsync(user, validRoles.Contains(role) ? role : "Student");
                usersCreated++;
            }
        }
        log.Add($"{usersCreated} users created");

        // === 3. Create 3 Semesters ===
        if (!_db.Semesters.Any())
        {
            _db.Semesters.AddRange(
                new Semester { AcademicYear = "2024-2025", Term = "1st Semester", IsActive = false },
                new Semester { AcademicYear = "2024-2025", Term = "2nd Semester", IsActive = false },
                new Semester { AcademicYear = "2025-2026", Term = "1st Semester", IsActive = true }
            );
            await _db.SaveChangesAsync();
        }
        log.Add("3 semesters created");

        // === 4. Import Subjects (for active semester) ===
        var activeSemester = await _db.Semesters.FirstAsync(s => s.IsActive);
        var subjectLines = await ReadCsvFile(Path.Combine(samplesPath, "subjects.csv"));
        int subjectsCreated = 0;
        foreach (var fields in subjectLines.Skip(1))
        {
            if (fields.Length < 4) continue;
            var faculty = await _userManager.FindByEmailAsync(fields[0].Trim());
            if (faculty is null) continue;
            var subjectCode = fields[1].Trim();
            var section = fields[3].Trim();

            if (await _db.FacultySubjects.AnyAsync(s => s.FacultyId == faculty.Id && s.SubjectCode == subjectCode && s.Section == section && s.SemesterId == activeSemester.Id))
                continue;

            _db.FacultySubjects.Add(new FacultySubject
            {
                FacultyId = faculty.Id, SemesterId = activeSemester.Id,
                SubjectCode = subjectCode, SubjectName = fields[2].Trim(), Section = section
            });
            subjectsCreated++;
        }
        await _db.SaveChangesAsync();
        log.Add($"{subjectsCreated} subjects assigned");

        // === 5. Import Enrollments ===
        var enrollLines = await ReadCsvFile(Path.Combine(samplesPath, "enrollments.csv"));
        int enrollsCreated = 0;
        foreach (var fields in enrollLines.Skip(1))
        {
            if (fields.Length < 3) continue;
            var student = await _userManager.FindByEmailAsync(fields[0].Trim());
            if (student is null) continue;
            var subject = await _db.FacultySubjects.FirstOrDefaultAsync(s =>
                s.SubjectCode == fields[1].Trim() && s.Section == fields[2].Trim() && s.SemesterId == activeSemester.Id);
            if (subject is null) continue;
            if (await _db.StudentEnrollments.AnyAsync(e => e.FacultySubjectId == subject.Id && e.StudentId == student.Id)) continue;

            _db.StudentEnrollments.Add(new StudentEnrollment { FacultySubjectId = subject.Id, StudentId = student.Id });
            enrollsCreated++;
        }
        await _db.SaveChangesAsync();
        log.Add($"{enrollsCreated} enrollments created");

        // === 6. Create 3 Evaluation Periods ===
        var semesters = await _db.Semesters.OrderBy(s => s.Id).ToListAsync();
        var periodsToCreate = new List<EvaluationPeriod>();
        if (!_db.EvaluationPeriods.Any())
        {
            periodsToCreate.Add(new EvaluationPeriod { Name = "Midterm Evaluation", SemesterId = semesters[0].Id, StartDate = new DateTime(2024, 10, 1), EndDate = new DateTime(2024, 10, 15), Status = EvaluationStatus.Closed });
            periodsToCreate.Add(new EvaluationPeriod { Name = "Final Evaluation", SemesterId = semesters[1].Id, StartDate = new DateTime(2025, 3, 1), EndDate = new DateTime(2025, 3, 15), Status = EvaluationStatus.Closed });
            periodsToCreate.Add(new EvaluationPeriod { Name = "Midterm Evaluation", SemesterId = semesters[2].Id, StartDate = new DateTime(2025, 10, 1), EndDate = new DateTime(2025, 12, 31), Status = EvaluationStatus.Open });
            _db.EvaluationPeriods.AddRange(periodsToCreate);
            await _db.SaveChangesAsync();
        }
        log.Add("3 evaluation periods created");

        // === 7. Generate Sample Evaluations ===
        // Call the existing method logic inline
        var allPeriods = await _db.EvaluationPeriods.Include(p => p.Semester).OrderBy(p => p.StartDate).ToListAsync();
        var allFaculty = await _userManager.GetUsersInRoleAsync("Faculty");
        var allStudents = await _userManager.GetUsersInRoleAsync("Student");
        var deans = await _userManager.GetUsersInRoleAsync("Dean");
        var chairs = await _userManager.GetUsersInRoleAsync("ProgramChair");
        var supervisors = deans.Concat(chairs).ToList();

        var studentCriteria = await _db.EvaluationCriteria.Include(c => c.Category).Where(c => c.Category.EvaluatorType == "Student").ToListAsync();
        var supervisorCriteria = await _db.EvaluationCriteria.Include(c => c.Category).Where(c => c.Category.EvaluatorType == "Supervisor").ToListAsync();

        var rng = new Random(42);
        int totalEvals = 0;

        var studentComments = new[]
        {
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
        };

        var supComments = new[]
        {
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
        };

        // Per-semester rating profiles for trend variation
        var profilesBySemester = new (int Min, int Max)[][]
        {
            [(3,4),(3,4),(2,4),(3,5),(1,3),(2,4),(2,3),(3,5),(2,4),(1,3)],
            [(3,5),(4,5),(3,4),(4,5),(2,3),(3,4),(2,4),(4,5),(3,4),(2,3)],
            [(4,5),(4,5),(3,5),(4,5),(2,4),(3,5),(3,4),(4,5),(3,5),(2,4)],
        };

        for (int pi = 0; pi < allPeriods.Count; pi++)
        {
            var period = allPeriods[pi];
            var semProfiles = profilesBySemester[Math.Min(pi, profilesBySemester.Length - 1)];

            var semSubjects = await _db.FacultySubjects.Where(s => s.SemesterId == period.SemesterId).ToListAsync();
            if (semSubjects.Count == 0)
            {
                foreach (var f in allFaculty)
                {
                    semSubjects.Add(new FacultySubject { FacultyId = f.Id, SemesterId = period.SemesterId, SubjectCode = $"SUBJ-{rng.Next(100, 999)}", SubjectName = "General Subject", Section = "Section A" });
                }
                _db.FacultySubjects.AddRange(semSubjects);
                await _db.SaveChangesAsync();
            }

            for (int f = 0; f < allFaculty.Count; f++)
            {
                var fac = allFaculty[f];
                var facSubjects = semSubjects.Where(s => s.FacultyId == fac.Id).ToList();
                if (facSubjects.Count == 0) continue;

                var profile = semProfiles[f % semProfiles.Length];

                int evalCount = rng.Next(4, 9);
                for (int e = 0; e < evalCount; e++)
                {
                    var student = allStudents[(f * 3 + e) % allStudents.Count];
                    var subj = facSubjects[e % facSubjects.Count];

                    if (await _db.Evaluations.AnyAsync(ev => ev.EvaluatorId == student.Id && ev.FacultyId == fac.Id && ev.FacultySubjectId == subj.Id && ev.EvaluationPeriodId == period.Id))
                        continue;

                    _db.Evaluations.Add(new Evaluation
                    {
                        EvaluationPeriodId = period.Id, FacultyId = fac.Id, EvaluatorId = student.Id,
                        EvaluatorType = "Student", FacultySubjectId = subj.Id,
                        SubmittedAt = period.StartDate.AddDays(rng.Next(0, 10)),
                        Comments = rng.Next(0, 5) <= 1 ? studentComments[rng.Next(studentComments.Length)] : null,
                        Responses = studentCriteria.Select(c => new EvaluationResponse { CriterionId = c.Id, Rating = Math.Clamp(rng.Next(profile.Min, profile.Max + 1), 1, 5) }).ToList()
                    });
                    totalEvals++;
                }

                var supervisor = supervisors[f % supervisors.Count];
                if (!await _db.Evaluations.AnyAsync(ev => ev.EvaluatorId == supervisor.Id && ev.FacultyId == fac.Id && ev.EvaluationPeriodId == period.Id))
                {
                    _db.Evaluations.Add(new Evaluation
                    {
                        EvaluationPeriodId = period.Id, FacultyId = fac.Id, EvaluatorId = supervisor.Id,
                        EvaluatorType = "Supervisor", SubmittedAt = period.StartDate.AddDays(rng.Next(5, 14)),
                        Comments = supComments[f % supComments.Length],
                        Responses = supervisorCriteria.Select(c => new EvaluationResponse { CriterionId = c.Id, Rating = Math.Clamp(rng.Next(profile.Min, profile.Max + 1), 1, 5) }).ToList()
                    });
                    totalEvals++;
                }
            }

            await _db.SaveChangesAsync();
            await _evalService.ComputeResultsAsync(period.Id);
        }

        log.Add($"{totalEvals} evaluations generated & results computed");

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
        var periods = await _db.EvaluationPeriods.Include(p => p.Semester).ToListAsync();
        if (periods.Count == 0)
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

        var studentCriteria = await _db.EvaluationCriteria
            .Include(c => c.Category)
            .Where(c => c.Category.EvaluatorType == "Student")
            .ToListAsync();

        var supervisorCriteria = await _db.EvaluationCriteria
            .Include(c => c.Category)
            .Where(c => c.Category.EvaluatorType == "Supervisor")
            .ToListAsync();

        // Find supervisors (Dean or ProgramChair)
        var deans = await _userManager.GetUsersInRoleAsync("Dean");
        var chairs = await _userManager.GetUsersInRoleAsync("ProgramChair");
        var supervisors = deans.Concat(chairs).ToList();
        if (supervisors.Count == 0)
        {
            TempData["Error"] = "No supervisors (Dean/ProgramChair) found. Import supervisor users first.";
            return RedirectToAction(nameof(Index));
        }

        var rng = new Random(42);
        int totalEvals = 0;

        var studentComments = new[]
        {
            "Very approachable and explains lessons clearly.",
            "Good instructor overall. Makes the subject interesting.",
            "Sometimes rushes through topics. Needs to slow down a bit.",
            "Very knowledgeable and passionate about teaching.",
            "Gives clear examples that relate to real-life situations.",
            "Could improve on time management during class.",
            "One of the best teachers I've had. Very patient and understanding.",
            "The instructor encourages students to participate in class discussions.",
            "Provides helpful feedback on assignments and projects.",
            "Very organized and always comes prepared for class.",
            "Should give more hands-on activities and exercises.",
            "Fair and consistent in grading. Explains rubrics well.",
            "Makes difficult concepts easy to understand.",
            "Could use more visual aids and multimedia in lectures.",
            "Always willing to help students during consultation hours.",
        };

        var supComments = new[]
        {
            "Faculty demonstrates strong commitment to teaching and student development. Recommended for continued professional development.",
            "Consistently meets instructional objectives. Should explore innovative teaching methodologies.",
            "Shows dedication to subject mastery. Encourage participation in research activities.",
            "Good classroom management skills. Consider mentoring junior faculty members.",
            "Demonstrates effective use of technology in instruction. Continue to update course materials.",
            "Active in community extension activities. Maintain engagement in professional organizations.",
            "Reliable and punctual. Shows improvement from previous evaluation period.",
            "Strong pedagogical skills. Recommended for advanced teaching certification training.",
            "Contributes positively to department activities. Should pursue further graduate studies.",
            "Effective communicator with students. Continue developing assessment strategies.",
        };

        // Sort periods by date for trend variation
        var sortedPeriods = periods.OrderBy(p => p.StartDate).ToList();

        for (int pi = 0; pi < sortedPeriods.Count; pi++)
        {
            var period = sortedPeriods[pi];

            // Get or create subjects for this period's semester
            var semesterSubjects = await _db.FacultySubjects
                .Where(s => s.SemesterId == period.SemesterId)
                .ToListAsync();

            // If no subjects exist for this semester, create generic ones
            if (semesterSubjects.Count == 0)
            {
                foreach (var f in faculty)
                {
                    semesterSubjects.Add(new FacultySubject
                    {
                        FacultyId = f.Id,
                        SemesterId = period.SemesterId,
                        SubjectCode = $"SUBJ-{rng.Next(100, 999)}",
                        SubjectName = "General Subject",
                        Section = "Section A"
                    });
                }
                _db.FacultySubjects.AddRange(semesterSubjects);
                await _db.SaveChangesAsync();
            }

            // Rating profiles improve over time for trend visibility
            int baseMin = Math.Max(1, 2 + pi);  // 2, 3, 4, ...
            int baseMax = Math.Min(5, 4 + pi);  // 4, 5, 5, ...

            for (int f = 0; f < faculty.Count; f++)
            {
                var fac = faculty[f];
                var facSubjects = semesterSubjects.Where(s => s.FacultyId == fac.Id).ToList();
                if (facSubjects.Count == 0) continue;

                // Vary ratings per faculty for realistic spread
                int fMin = Math.Max(1, baseMin + (f % 3 == 0 ? -1 : 0));
                int fMax = Math.Min(5, baseMax + (f % 2 == 0 ? 0 : 1));

                // Student evaluations (4-8 per faculty)
                int evalCount = rng.Next(4, 9);
                for (int e = 0; e < evalCount; e++)
                {
                    var student = students[(f * 3 + e) % students.Count];
                    var subj = facSubjects[e % facSubjects.Count];

                    var alreadyExists = await _db.Evaluations.AnyAsync(ev =>
                        ev.EvaluatorId == student.Id &&
                        ev.FacultyId == fac.Id &&
                        ev.FacultySubjectId == subj.Id &&
                        ev.EvaluationPeriodId == period.Id);
                    if (alreadyExists) continue;

                    _db.Evaluations.Add(new Evaluation
                    {
                        EvaluationPeriodId = period.Id,
                        FacultyId = fac.Id,
                        EvaluatorId = student.Id,
                        EvaluatorType = "Student",
                        FacultySubjectId = subj.Id,
                        SubmittedAt = period.StartDate.AddDays(rng.Next(0, 10)),
                        Comments = rng.Next(0, 5) <= 1 ? studentComments[rng.Next(studentComments.Length)] : null,
                        Responses = studentCriteria.Select(c => new EvaluationResponse
                        {
                            CriterionId = c.Id,
                            Rating = Math.Clamp(rng.Next(fMin, fMax + 1), 1, 5)
                        }).ToList()
                    });
                    totalEvals++;
                }

                // Supervisor evaluation
                var supervisor = supervisors[f % supervisors.Count];
                var supExists = await _db.Evaluations.AnyAsync(ev =>
                    ev.EvaluatorId == supervisor.Id &&
                    ev.FacultyId == fac.Id &&
                    ev.EvaluationPeriodId == period.Id);

                if (!supExists)
                {
                    _db.Evaluations.Add(new Evaluation
                    {
                        EvaluationPeriodId = period.Id,
                        FacultyId = fac.Id,
                        EvaluatorId = supervisor.Id,
                        EvaluatorType = "Supervisor",
                        SubmittedAt = period.StartDate.AddDays(rng.Next(5, 14)),
                        Comments = supComments[f % supComments.Length],
                        Responses = supervisorCriteria.Select(c => new EvaluationResponse
                        {
                            CriterionId = c.Id,
                            Rating = Math.Clamp(rng.Next(fMin, fMax + 1), 1, 5)
                        }).ToList()
                    });
                    totalEvals++;
                }
            }

            await _db.SaveChangesAsync();

            // Compute results
            await _evalService.ComputeResultsAsync(period.Id);
        }

        TempData["Success"] = $"Sample evaluations generated: {totalEvals} evaluations across {sortedPeriods.Count} period(s). Results computed.";
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
