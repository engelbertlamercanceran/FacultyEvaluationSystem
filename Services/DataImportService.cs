using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FacultyEvalSystem.Data;
using FacultyEvalSystem.Models;

namespace FacultyEvalSystem.Services;

/// <summary>
/// Imports the two source documents the system is built on:
///
///   * the Faculty Workload Summary — faculty, their college, designation
///     (Dean / Program Chair) and the Course Codes they teach; and
///   * the Registrar's masterlist — students, their program and the Course
///     Codes they are enrolled in.
///
/// Students are identified by Name + Program. New accounts are created with the
/// system's default password. After either import, students are linked to the
/// faculty teaching their courses wherever the Course Code identifies a single
/// instructor.
/// </summary>
public class DataImportService
{
    public const string EmailDomain = "isuc.edu.ph";

    private readonly ApplicationDbContext _db;
    private readonly IPasswordHasher<ApplicationUser> _hasher;
    private readonly IConfiguration _config;
    private readonly ILogger<DataImportService> _logger;

    public DataImportService(ApplicationDbContext db, IPasswordHasher<ApplicationUser> hasher,
        IConfiguration config, ILogger<DataImportService> logger)
    {
        _db = db;
        _hasher = hasher;
        _config = config;
        _logger = logger;
    }

    /// <summary>The password given to every account created by an import.</summary>
    public string DefaultPassword => _config["Accounts:DefaultPassword"] is { Length: > 0 } p ? p : "Pass@123";

    public class ImportReport
    {
        public int Created { get; set; }
        public int Updated { get; set; }
        public int CoursesRecorded { get; set; }
        public List<string> Problems { get; } = [];

        public string ProblemSummary(int max = 8) => Problems.Count == 0 ? ""
            : $"{Problems.Count} row(s) need attention: " + string.Join(" ", Problems.Take(max))
              + (Problems.Count > max ? $" (+{Problems.Count - max} more)" : "");
    }

    public record LinkReport(int EnrollmentsCreated, int CoursesWithOneInstructor,
        int CoursesWithSeveralInstructors, int CoursesWithNoInstructor);

    // =====================================================================
    // Colleges, programs and semesters
    // =====================================================================

    /// <summary>Adds any of the nine colleges and their programs that are missing.</summary>
    public async Task EnsureCatalogAsync()
    {
        var colleges = await _db.Colleges.Include(c => c.Programs).ToListAsync();
        var programs = await _db.AcademicPrograms.ToListAsync();

        foreach (var info in InstitutionCatalog.Colleges)
        {
            var college = colleges.FirstOrDefault(c => c.Code == info.Code);
            if (college is null)
            {
                college = new College { Code = info.Code, Name = info.Name };
                _db.Colleges.Add(college);
                colleges.Add(college);
            }

            foreach (var p in info.Programs)
            {
                if (programs.Any(x => x.Code == p.Code)) continue;
                var program = new AcademicProgram { Code = p.Code, Name = p.Name, College = college };
                _db.AcademicPrograms.Add(program);
                programs.Add(program);
            }
        }

        await _db.SaveChangesAsync();
    }

    /// <summary>Creates the five semesters, with 1st Semester, A.Y. 2026-2027 active.</summary>
    public async Task<Semester> EnsureSemestersAsync()
    {
        var existing = await _db.Semesters.ToListAsync();
        var list = InstitutionCatalog.Semesters;

        for (int i = 0; i < list.Length; i++)
        {
            var (ay, term) = list[i];
            if (existing.Any(s => s.AcademicYear == ay && s.Term == term)) continue;

            var semester = new Semester { AcademicYear = ay, Term = term };
            semester.RefreshSortKey();
            _db.Semesters.Add(semester);
            existing.Add(semester);
        }

        var (currentAy, currentTerm) = list[^1];
        foreach (var s in existing)
            s.IsActive = s.AcademicYear == currentAy && s.Term == currentTerm;

        await _db.SaveChangesAsync();
        return existing.First(s => s.IsActive);
    }

    // =====================================================================
    // Faculty workload summary
    // =====================================================================

    private record WorkloadFaculty(string RawName, string CollegeCode, string? Designation,
        string? AcademicRank, List<string> Courses);

    /// <summary>
    /// Imports the faculty workload summary: one account per faculty member,
    /// associated with their college (and, for Deans and Program Chairs, the
    /// program in their designation), plus one course assignment per Course Code
    /// they teach in the given semester.
    /// </summary>
    public async Task<ImportReport> ImportFacultyWorkloadAsync(Stream xlsx, Semester semester)
    {
        var report = new ImportReport();
        var rows = XlsxReader.ReadFirstSheet(xlsx);
        var parsed = ParseWorkload(rows, report);

        var colleges = await _db.Colleges.ToListAsync();
        var programs = await _db.AcademicPrograms.ToListAsync();
        var roleIds = await _db.Roles.ToDictionaryAsync(r => r.Name!, r => r.Id);

        var staffRoleIds = new[] { roleIds["Faculty"], roleIds["ProgramChair"], roleIds["Dean"] };
        var staffIds = await _db.UserRoles.Where(ur => staffRoleIds.Contains(ur.RoleId))
            .Select(ur => ur.UserId).Distinct().ToListAsync();
        var staff = await _db.Users.Where(u => staffIds.Contains(u.Id)).ToListAsync();
        var existingRoles = await _db.UserRoles.Where(ur => staffIds.Contains(ur.UserId)).ToListAsync();

        var emails = new HashSet<string>(
            await _db.Users.Select(u => u.NormalizedEmail!).Where(e => e != null).ToListAsync(),
            StringComparer.OrdinalIgnoreCase);

        var passwordHash = _hasher.HashPassword(new ApplicationUser(), DefaultPassword);

        var assignments = await _db.FacultySubjects.Where(fs => fs.SemesterId == semester.Id).ToListAsync();

        foreach (var f in parsed)
        {
            var college = colleges.FirstOrDefault(c => c.Code == f.CollegeCode);
            var (first, middle, last, suffix) = SplitFacultyName(f.RawName);
            if (string.IsNullOrWhiteSpace(last) || string.IsNullOrWhiteSpace(first))
            {
                report.Problems.Add($"Could not read the name \"{f.RawName.Trim()}\".");
                continue;
            }

            string role = InstitutionCatalog.IsDeanDesignation(f.Designation) ? "Dean"
                        : InstitutionCatalog.IsProgramChairDesignation(f.Designation) ? "ProgramChair"
                        : "Faculty";

            var programCode = InstitutionCatalog.ProgramCodeFromDesignation(f.Designation);
            var program = programCode is null ? null : programs.FirstOrDefault(p => p.Code == programCode);

            if (role == "ProgramChair" && program is null)
                report.Problems.Add($"{last}, {first}: the program in \"{f.Designation?.Trim()}\" was not recognised.");

            var user = staff.FirstOrDefault(u =>
                string.Equals(u.LastName, last, StringComparison.OrdinalIgnoreCase)
                && string.Equals(u.FirstName, first, StringComparison.OrdinalIgnoreCase));

            if (user is null)
            {
                user = NewAccount(first, middle, last, suffix, passwordHash, emails);
                user.CollegeId = college?.Id;
                user.ProgramId = program?.Id;
                _db.Users.Add(user);
                _db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = roleIds[role] });
                staff.Add(user);
                report.Created++;
            }
            else
            {
                user.CollegeId = college?.Id ?? user.CollegeId;
                if (program is not null) user.ProgramId = program.Id;
                user.MiddleName ??= middle;

                // Keep the account's role in step with the designation on the memo
                var current = existingRoles.Where(ur => ur.UserId == user.Id).ToList();
                if (!current.Any(ur => ur.RoleId == roleIds[role]))
                {
                    _db.UserRoles.RemoveRange(current.Where(ur => staffRoleIds.Contains(ur.RoleId)));
                    _db.UserRoles.Add(new IdentityUserRole<string> { UserId = user.Id, RoleId = roleIds[role] });
                }
                report.Updated++;
            }

            foreach (var (code, section) in f.Courses.Select(SplitCourseAndSection).DistinctBy(c => CourseCodes.Key(c.Code)))
            {
                var key = CourseCodes.Key(code);
                if (key.Length == 0) continue;
                if (assignments.Any(a => a.FacultyId == user.Id && a.CourseKey == key)) continue;

                var fs = new FacultySubject
                {
                    FacultyId = user.Id,
                    SemesterId = semester.Id,
                    CourseCode = code,
                    CourseKey = key,
                    Section = section,
                    TeachingMemoNumber = "Faculty Workload Summary"
                };
                _db.FacultySubjects.Add(fs);
                assignments.Add(fs);
                report.CoursesRecorded++;
            }
        }

        await _db.SaveChangesAsync();
        return report;
    }

    private static List<WorkloadFaculty> ParseWorkload(List<string?[]> rows, ImportReport report)
    {
        static string? Cell(string?[] r, int i) => i < r.Length && !string.IsNullOrWhiteSpace(r[i]) ? r[i]!.Trim() : null;

        // Header row: "Name" in column A
        int start = rows.FindIndex(r => Cell(r, 0)?.Equals("Name", StringComparison.OrdinalIgnoreCase) == true);
        if (start < 0) throw new InvalidDataException(
            "This does not look like the Faculty Workload Summary: no \"Name\" header was found in column A.");

        // Column positions, found from the header rows below "Name"
        int iDesignation = 6, iRank = 3, iGradCourse = 8, iUgCourse = 13;
        for (int h = start; h < Math.Min(rows.Count, start + 4); h++)
        {
            for (int c = 0; c < rows[h].Length; c++)
            {
                var t = Cell(rows[h], c);
                if (t is null) continue;
                if (t.Equals("Designation", StringComparison.OrdinalIgnoreCase) && h == start) iDesignation = c;
                if (t.Equals("Academic Rank", StringComparison.OrdinalIgnoreCase)) iRank = c;
            }
            var courseCols = Enumerable.Range(0, rows[h].Length)
                .Where(c => Cell(rows[h], c)?.Equals("Course", StringComparison.OrdinalIgnoreCase) == true).ToList();
            if (courseCols.Count >= 2) { iGradCourse = courseCols[0]; iUgCourse = courseCols[1]; }
        }

        var colleges = InstitutionCatalog.Colleges;
        var list = new List<WorkloadFaculty>();
        string? collegeCode = null;
        WorkloadFaculty? current = null;

        for (int i = start + 1; i < rows.Count; i++)
        {
            var r = rows[i];
            var a = Cell(r, 0);
            bool othersEmpty = r.Skip(1).All(string.IsNullOrWhiteSpace);

            if (a is not null && othersEmpty && a.StartsWith("COLLEGE", StringComparison.OrdinalIgnoreCase))
            {
                var heading = Regex.Replace(a, @"\s+", " ").ToUpperInvariant();
                collegeCode = colleges.FirstOrDefault(c => c.WorkloadHeading == heading)?.Code;
                if (collegeCode is null) report.Problems.Add($"Row {i + 1}: college \"{a}\" is not one of the nine colleges.");
                current = null;
                continue;
            }

            // Employment-status headings (PERMANENT, CONTRACT OF SERVICE ...)
            if (a is not null && othersEmpty) { current = null; continue; }

            if (a is not null)
            {
                if (collegeCode is null) continue;
                current = new WorkloadFaculty(a, collegeCode, Cell(r, iDesignation), Cell(r, iRank), []);
                list.Add(current);
            }

            if (current is null) continue;

            foreach (var col in new[] { iGradCourse, iUgCourse })
            {
                var code = Cell(r, col);
                if (code is null || code.Contains("LEAVE", StringComparison.OrdinalIgnoreCase)) continue;
                current.Courses.Add(code);
            }
        }

        return list;
    }

    /// <summary>"EnSci 111-BSES 1" -> ("EnSci 111", "BSES 1"). Otherwise the code as-is.</summary>
    private static (string Code, string? Section) SplitCourseAndSection(string raw)
    {
        var m = Regex.Match(raw.Trim(), @"^(.*?\d+)\s*-\s*([A-Za-z]{2,}.*)$");
        return m.Success
            ? (CourseCodes.Clean(m.Groups[1].Value), m.Groups[2].Value.Trim())
            : (CourseCodes.Clean(raw), null);
    }

    private static readonly HashSet<string> NameParticles =
        new(StringComparer.OrdinalIgnoreCase) { "DE", "DEL", "DELA", "DELOS", "LA", "LAS", "LOS", "SAN", "STA", "STA.", "STO", "STO." };

    private static readonly HashSet<string> Suffixes =
        new(StringComparer.OrdinalIgnoreCase) { "JR", "JR.", "SR", "SR.", "II", "III", "IV", "V" };

    /// <summary>
    /// Splits a workload-summary name such as "ABELLERA, VELMOR M." or
    /// "SAGUIBO, ANASTACIO SIOGIE JR. G." into first / middle initial / last / suffix.
    /// </summary>
    public static (string First, string? Middle, string Last, string? Suffix) SplitFacultyName(string raw)
    {
        var s = raw.Replace('�', 'Ñ').Replace('\n', ' ').Replace('\r', ' ');
        bool firstLast = Regex.IsMatch(s, @"^\s*Atty\.", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\(.*?\)", " ");
        s = Regex.Replace(s, @"^\s*Atty\.\s*", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s+\.(\s|$)", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim().TrimEnd(',');

        string last;
        List<string> rest;

        if (s.Contains(','))
        {
            var idx = s.IndexOf(',');
            last = s[..idx].Trim();
            rest = s[(idx + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        }
        else
        {
            var tokens = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            if (tokens.Count < 2) return (Title(s), null, "", null);

            if (firstLast)
            {
                last = tokens[^1];
                rest = tokens.Take(tokens.Count - 1).ToList();
            }
            else
            {
                // "DELA ROSA JOHN PAUL A." — surname first, particles stay with it
                int take = 1;
                while (take < tokens.Count - 1 && NameParticles.Contains(tokens[take - 1])) take++;
                last = string.Join(' ', tokens.Take(take));
                rest = tokens.Skip(take).ToList();
            }
        }

        string? suffix = null;
        var sfx = rest.FirstOrDefault(t => Suffixes.Contains(t.TrimEnd(',')));
        if (sfx is not null)
        {
            rest.Remove(sfx);
            suffix = sfx.TrimEnd('.', ',').ToUpperInvariant() switch { "JR" => "Jr.", "SR" => "Sr.", var x => x };
        }
        else
        {
            var parts = last.Split(' ');
            if (parts.Length > 1 && Suffixes.Contains(parts[^1]))
            {
                suffix = parts[^1].TrimEnd('.').ToUpperInvariant() switch { "JR" => "Jr.", "SR" => "Sr.", var x => x };
                last = string.Join(' ', parts[..^1]);
            }
        }

        string? middle = null;
        if (rest.Count > 1)
        {
            var m = rest[^1].TrimEnd('.');
            // A trailing initial ("M.", "SC.") is the middle initial
            if (m.Length is >= 1 and <= 2 && m.All(char.IsLetter) && rest[^1].EndsWith('.') || m.Length == 1)
            {
                middle = m.ToUpperInvariant();
                rest.RemoveAt(rest.Count - 1);
            }
        }

        return (Title(string.Join(' ', rest)), middle, Title(last), suffix);
    }

    private static string Title(string s)
    {
        var ti = CultureInfo.GetCultureInfo("en-US").TextInfo;
        return ti.ToTitleCase(s.Trim().ToLowerInvariant());
    }

    // =====================================================================
    // Registrar's masterlist
    // =====================================================================

    /// <summary>
    /// Imports the Registrar's masterlist for the given semester. Each row is a
    /// student; the student is matched on Name + Program, created with the default
    /// password when new, and their enrolled Course Codes are stored against them.
    /// </summary>
    public async Task<ImportReport> ImportRegistrarMasterlistAsync(Stream file, string fileName, Semester semester)
    {
        var report = new ImportReport();

        var rows = fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)
            ? await ReadCsvAsync(file)
            : XlsxReader.ReadFirstSheet(file);

        int headerIndex = rows.FindIndex(r => r.Any(c => c is not null && c.Contains("Last Name", StringComparison.OrdinalIgnoreCase)));
        if (headerIndex < 0) throw new InvalidDataException(
            "This does not look like the Registrar's masterlist: no \"Last Name\" column was found.");

        var header = rows[headerIndex].Select(h => Regex.Replace(h ?? "", @"\s+", " ").Trim()).ToList();
        int Col(string starts) => header.FindIndex(h => h.StartsWith(starts, StringComparison.OrdinalIgnoreCase));

        int iProgram = Col("Program Name"), iLast = Col("Last Name"), iFirst = Col("First Name"),
            iMiddle = Col("Middle Name"), iSuffix = Col("Suffix");

        // Optional: the student's ID number, if the Registrar's file carries one
        int iStudentId = new[] { "Student ID", "Student No", "Student Number", "ID Number", "ID No" }
            .Select(Col).FirstOrDefault(i => i >= 0, -1);

        if (iProgram < 0 || iFirst < 0)
            throw new InvalidDataException("The masterlist must have Program Name, Last Name and First Name columns.");

        // Course Code columns (the Registrar's template calls them "Subject Code n"),
        // each followed by its Units column.
        var courseCols = Enumerable.Range(0, header.Count)
            .Where(i => Regex.IsMatch(header[i], @"^(Subject|Course) Code\b", RegexOptions.IgnoreCase))
            .Select(i => (Code: i, Units: i + 1 < header.Count && header[i + 1].StartsWith("Units", StringComparison.OrdinalIgnoreCase) ? i + 1 : -1))
            .ToList();

        if (courseCols.Count == 0)
            throw new InvalidDataException("No Course Code columns were found in the masterlist.");

        // Programs by normalised name, including the Registrar's spelling variants
        var programs = await _db.AcademicPrograms.ToListAsync();
        var programByName = new Dictionary<string, AcademicProgram>();
        foreach (var p in programs)
        {
            programByName[InstitutionCatalog.NormalizeProgramName(p.Name)] = p;
            var info = InstitutionCatalog.Colleges.SelectMany(c => c.Programs).FirstOrDefault(x => x.Code == p.Code);
            foreach (var alias in info?.Aliases ?? [])
                programByName[InstitutionCatalog.NormalizeProgramName(alias)] = p;
        }

        var studentRoleId = await _db.Roles.Where(r => r.Name == "Student").Select(r => r.Id).FirstAsync();
        var studentIds = await _db.UserRoles.Where(ur => ur.RoleId == studentRoleId).Select(ur => ur.UserId).ToListAsync();
        var students = await _db.Users.Where(u => studentIds.Contains(u.Id)).ToListAsync();

        var byKey = new Dictionary<string, ApplicationUser>();
        foreach (var s in students.Where(s => s.ProgramId != null))
            byKey.TryAdd(StudentKey(s.LastName, s.FirstName, s.MiddleName, s.ProgramId!.Value), s);

        var emails = new HashSet<string>(
            await _db.Users.Select(u => u.NormalizedEmail!).Where(e => e != null).ToListAsync(),
            StringComparer.OrdinalIgnoreCase);

        var passwordHash = _hasher.HashPassword(new ApplicationUser(), DefaultPassword);

        var existingCourses = await _db.StudentCourses
            .Where(sc => sc.SemesterId == semester.Id)
            .ToListAsync();
        var coursesByStudent = existingCourses.GroupBy(sc => sc.StudentId).ToDictionary(g => g.Key, g => g.ToList());

        var seenThisFile = new HashSet<string>();
        var unknownPrograms = new HashSet<string>();
        var createdIds = new HashSet<string>();
        int rowsSinceSave = 0;

        _db.ChangeTracker.AutoDetectChangesEnabled = false;
        try
        {
        for (int r = headerIndex + 1; r < rows.Count; r++)
        {
            var row = rows[r];
            string? Get(int i) => i >= 0 && i < row.Length && !string.IsNullOrWhiteSpace(row[i]) ? Regex.Replace(row[i]!, @"\s+", " ").Trim() : null;

            var last = Get(iLast);
            var first = Get(iFirst);
            if (last is null && first is null) continue;
            if (last is null || first is null)
            {
                report.Problems.Add($"Row {r + 1}: missing first or last name.");
                continue;
            }

            var programName = Get(iProgram);
            if (programName is null || !programByName.TryGetValue(InstitutionCatalog.NormalizeProgramName(programName), out var program))
            {
                if (unknownPrograms.Add(programName ?? "(blank)"))
                    report.Problems.Add($"Row {r + 1}: program \"{programName}\" is not registered — rows for it were skipped.");
                continue;
            }

            var middle = NullIfNa(Get(iMiddle));
            var suffix = NullIfNa(Get(iSuffix));
            var studentNumber = NullIfNa(Get(iStudentId));
            if (studentNumber is { Length: > 20 }) studentNumber = studentNumber[..20];
            var key = StudentKey(last, first, middle, program.Id);

            if (!seenThisFile.Add(key))
            {
                report.Problems.Add($"Row {r + 1}: {first} {last} ({program.Code}) appears more than once; the rows were combined.");
            }

            if (!byKey.TryGetValue(key, out var student))
            {
                student = NewAccount(first, middle, last, suffix, passwordHash, emails);
                student.ProgramId = program.Id;
                student.CollegeId = program.CollegeId;
                student.StudentNumber = studentNumber;
                _db.Users.Add(student);
                _db.UserRoles.Add(new IdentityUserRole<string> { UserId = student.Id, RoleId = studentRoleId });
                byKey[key] = student;
                createdIds.Add(student.Id);
                report.Created++;
            }
            else if (!createdIds.Contains(student.Id))
            {
                student.Suffix ??= suffix;
                student.CollegeId = program.CollegeId;
                if (studentNumber is not null) student.StudentNumber = studentNumber;
                report.Updated++;
            }

            if (!coursesByStudent.TryGetValue(student.Id, out var theirs))
                coursesByStudent[student.Id] = theirs = [];

            foreach (var (codeCol, unitsCol) in courseCols)
            {
                var code = Get(codeCol);
                if (code is null) continue;

                var clean = CourseCodes.Clean(code);
                var courseKey = CourseCodes.Key(clean);
                if (courseKey.Length == 0 || theirs.Any(c => c.CourseKey == courseKey)) continue;

                decimal? units = decimal.TryParse(Get(unitsCol), NumberStyles.Any, CultureInfo.InvariantCulture, out var u) ? u : null;

                var sc = new StudentCourse
                {
                    StudentId = student.Id,
                    SemesterId = semester.Id,
                    CourseCode = clean,
                    CourseKey = courseKey,
                    Units = units
                };
                _db.StudentCourses.Add(sc);
                theirs.Add(sc);
                report.CoursesRecorded++;
            }

            // Save in batches: thousands of students per file
            if (++rowsSinceSave >= 1000)
            {
                _db.ChangeTracker.DetectChanges();
                await _db.SaveChangesAsync();
                rowsSinceSave = 0;
            }
        }

        _db.ChangeTracker.DetectChanges();
        await _db.SaveChangesAsync();
        }
        finally
        {
            _db.ChangeTracker.AutoDetectChangesEnabled = true;
        }
        return report;
    }

    private static string? NullIfNa(string? s)
        => s is null || s.Equals("NA", StringComparison.OrdinalIgnoreCase) || s.Equals("N/A", StringComparison.OrdinalIgnoreCase) || s == "-" ? null : s;

    /// <summary>Name + Program: how a student is recognised across imports.</summary>
    private static string StudentKey(string last, string first, string? middle, int programId)
        => $"{Fold(last)}|{Fold(first)}|{Fold(middle)}|{programId}";

    private static string Fold(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var d = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in d)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToUpperInvariant(c));
        return sb.ToString();
    }

    // =====================================================================
    // Accounts
    // =====================================================================

    /// <summary>
    /// A new, ready-to-save account with the default password. Built directly
    /// rather than through UserManager so that thousands of students can be created
    /// in one import without hashing the password thousands of times.
    /// </summary>
    private ApplicationUser NewAccount(string first, string? middle, string last, string? suffix,
        string passwordHash, HashSet<string> takenEmails)
    {
        var email = UniqueEmail(first, middle, last, takenEmails);
        return new ApplicationUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            EmailConfirmed = true,
            PasswordHash = passwordHash,
            SecurityStamp = Guid.NewGuid().ToString("N").ToUpperInvariant(),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
            LockoutEnabled = true,
            MustChangePassword = true,
            FirstName = Truncate(first, 100),
            MiddleName = middle is null ? null : Truncate(middle, 100),
            LastName = Truncate(last, 100),
            Suffix = suffix is null ? null : Truncate(suffix, 20),
            IsActive = true
        };
    }

    /// <summary>firstname.lastname@isuc.edu.ph, adding the middle initial and then a number if taken.</summary>
    private static string UniqueEmail(string first, string? middle, string last, HashSet<string> taken)
    {
        var f = Slug(first);
        var l = Slug(last);
        var m = Slug(middle);
        if (f.Length == 0) f = "user";

        var candidates = new List<string> { $"{f}.{l}" };
        if (m.Length > 0) candidates.Add($"{f}.{m[0]}.{l}");

        foreach (var c in candidates)
        {
            var email = $"{c}@{EmailDomain}";
            if (taken.Add(email)) return email;
        }

        for (int n = 2; ; n++)
        {
            var email = $"{f}.{l}{n}@{EmailDomain}";
            if (taken.Add(email)) return email;
        }
    }

    private static string Slug(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        var d = s.Replace('Ñ', 'N').Replace('ñ', 'n').Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var c in d)
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9') sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    private static string Truncate(string s, int max) => s.Length > max ? s[..max] : s;

    // =====================================================================
    // Linking students to faculty, and filling in programs
    // =====================================================================

    /// <summary>
    /// Links each student Course Code to the faculty member teaching it, wherever
    /// only one faculty member teaches that Course Code this semester. Where several
    /// do, the student chooses their instructor on the evaluation page.
    /// </summary>
    public async Task<LinkReport> LinkEnrollmentsAsync(int semesterId)
    {
        var assignments = await _db.FacultySubjects
            .Where(fs => fs.SemesterId == semesterId)
            .Select(fs => new { fs.Id, fs.FacultyId, fs.CourseKey })
            .ToListAsync();

        var byKey = assignments.GroupBy(a => a.CourseKey).ToDictionary(g => g.Key, g => g.ToList());

        var courses = await _db.StudentCourses
            .Where(sc => sc.SemesterId == semesterId)
            .Select(sc => new { sc.StudentId, sc.CourseKey })
            .ToListAsync();

        var existing = await _db.StudentEnrollments
            .Where(se => se.FacultySubject.SemesterId == semesterId)
            .Select(se => new { se.StudentId, se.FacultySubject.CourseKey })
            .ToListAsync();
        var enrolled = existing.Select(e => (e.StudentId, e.CourseKey)).ToHashSet();

        int created = 0, one = 0, several = 0, none = 0;
        var toAdd = new List<StudentEnrollment>();

        foreach (var c in courses)
        {
            if (!byKey.TryGetValue(c.CourseKey, out var teaching)) { none++; continue; }

            if (teaching.Select(t => t.FacultyId).Distinct().Count() > 1) { several++; continue; }

            one++;
            if (enrolled.Contains((c.StudentId, c.CourseKey))) continue;

            toAdd.Add(new StudentEnrollment { StudentId = c.StudentId, FacultySubjectId = teaching[0].Id });
            enrolled.Add((c.StudentId, c.CourseKey));
            created++;
        }

        foreach (var batch in toAdd.Chunk(2000))
        {
            _db.StudentEnrollments.AddRange(batch);
            await _db.SaveChangesAsync();
        }

        return new LinkReport(created, one, several, none);
    }

    /// <summary>
    /// Fills in, where not already set:
    ///  * each faculty member's program — the program within their college whose
    ///    students take the most of their courses; and
    ///  * each course assignment's owning program (whose Program Chair evaluates
    ///    it, PC-01) — the program within the teacher's college whose students take
    ///    it most, otherwise the teacher's own program.
    /// Never overwrites a value that has been set by hand.
    /// </summary>
    public async Task<(int Faculty, int Courses)> InferProgramsAsync(int semesterId)
    {
        // Students taking each course key, by program
        var demand = await _db.StudentCourses
            .Where(sc => sc.SemesterId == semesterId && sc.Student.ProgramId != null)
            .GroupBy(sc => new { sc.CourseKey, ProgramId = sc.Student.ProgramId!.Value })
            .Select(g => new { g.Key.CourseKey, g.Key.ProgramId, Count = g.Count() })
            .ToListAsync();

        var demandByKey = demand.GroupBy(d => d.CourseKey)
            .ToDictionary(g => g.Key, g => g.Select(x => (x.ProgramId, x.Count)).ToList());

        var programCollege = await _db.AcademicPrograms.ToDictionaryAsync(p => p.Id, p => p.CollegeId);

        var assignments = await _db.FacultySubjects
            .Include(fs => fs.Faculty)
            .Where(fs => fs.SemesterId == semesterId)
            .ToListAsync();

        int facultyUpdated = 0, coursesUpdated = 0;

        foreach (var g in assignments.GroupBy(a => a.Faculty))
        {
            var faculty = g.Key;
            if (faculty.ProgramId is not null || faculty.CollegeId is null) continue;

            var best = g.SelectMany(a => demandByKey.GetValueOrDefault(a.CourseKey) ?? [])
                .Where(d => programCollege[d.ProgramId] == faculty.CollegeId)
                .GroupBy(d => d.ProgramId)
                .OrderByDescending(x => x.Sum(d => d.Count))
                .Select(x => (int?)x.Key)
                .FirstOrDefault();

            if (best is not null)
            {
                faculty.ProgramId = best;
                facultyUpdated++;
            }
        }

        foreach (var a in assignments.Where(a => a.ProgramId is null))
        {
            var options = demandByKey.GetValueOrDefault(a.CourseKey) ?? [];
            var inCollege = options.Where(d => programCollege[d.ProgramId] == a.Faculty.CollegeId)
                .OrderByDescending(d => d.Count).Select(d => (int?)d.ProgramId).FirstOrDefault();

            a.ProgramId = inCollege
                ?? a.Faculty.ProgramId
                ?? options.OrderByDescending(d => d.Count).Select(d => (int?)d.ProgramId).FirstOrDefault();

            if (a.ProgramId is not null) coursesUpdated++;
        }

        await _db.SaveChangesAsync();
        return (facultyUpdated, coursesUpdated);
    }

    private static async Task<List<string?[]>> ReadCsvAsync(Stream stream)
    {
        var rows = new List<string?[]>();
        using var reader = new StreamReader(stream, leaveOpen: true);
        while (await reader.ReadLineAsync() is { } line)
        {
            var fields = new List<string?>();
            var current = new StringBuilder();
            bool inQuotes = false;
            foreach (var c in line)
            {
                if (c == '"') inQuotes = !inQuotes;
                else if (c == ',' && !inQuotes) { fields.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
            fields.Add(current.ToString());
            rows.Add([.. fields]);
        }
        return rows;
    }
}
