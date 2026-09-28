namespace FacultyEvalSystem.Data;

/// <summary>
/// The nine colleges of ISU Cabagan and their degree programs, as they appear on
/// the 1st Semester A.Y. 2026-2027 faculty workload summary and Registrar's
/// masterlist. Used to pre-seed the system and to recognise program names and
/// Program Chair designations on import.
/// </summary>
public static class InstitutionCatalog
{
    public record ProgramInfo(string Code, string Name, params string[] Aliases);
    public record CollegeInfo(string Code, string Name, string WorkloadHeading, ProgramInfo[] Programs);

    public static readonly CollegeInfo[] Colleges =
    [
        new("CAST", "College of Agricultural Sciences and Technology", "COLLEGE OF AGRICULTURAL SCIENCES AND TECHNOLOGY",
        [
            new("DAT", "3-Year Diploma in Agricultural Technology"),
            new("BAT", "Bachelor of Agricultural Technology"),
            new("BSA", "Bachelor of Science in Agriculture"),
            new("MSAS", "Master of Science in Agricultural Science"),
        ]),
        new("CBM", "College of Business Management", "COLLEGE OF BUSINESS MANAGEMENT",
        [
            new("BSAB", "Bachelor of Science in Agribusiness"),
            new("BSENTREP", "Bachelor of Science in Entrepreneurship"),
            new("BSHM", "Bachelor of Science in Hospitality Management"),
            new("BSTM", "Bachelor of Science in Tourism Management"),
        ]),
        new("CCJE", "College of Criminal Justice Education", "COLLEGE OF CRIMINAL JUSTICE EDUCATION",
        [
            new("BSCRIM", "Bachelor of Science in Criminology"),
            new("BSISM", "Bachelor of Science in Industrial Security Management"),
            new("BSLEA", "Bachelor of Science in Law Enforcement Administration"),
        ]),
        new("CCSICT", "College of Computing Studies, Information and Communication Technology",
            "COLLEGE OF COMPUTING STUDIES, INFORMATION AND COMMUNICATION TECHNOLOGY",
        [
            new("BSCS", "Bachelor of Science in Computer Science"),
            new("BSDSA", "Bachelor of Science in Data Science and Analytics"),
            new("BSIT", "Bachelor of Science in Information Technology"),
            new("MSIT", "Master of Science in Information Technology",
                "Masters of Science in Infomation Technology", "Masters of Science in Information Technology"),
        ]),
        new("CCSS", "College of Communication and Social Sciences", "COLLEGE OF COMMUNICATION AND SOCIAL SCIENCES",
        [
            new("BACOMM", "Bachelor of Arts in Communication"),
            new("BASOC", "Bachelor of Arts in Sociology"),
            new("BSDC", "Bachelor of Science in Development Communication"),
            new("MSDC", "Master of Science in Development Communication"),
        ]),
        new("CED", "College of Education", "COLLEGE OF EDUCATION",
        [
            new("BECED", "Bachelor of Early Childhood Education"),
            new("BEED", "Bachelor of Elementary Education"),
            new("BPED", "Bachelor of Physical Education"),
            new("BSED", "Bachelor of Secondary Education"),
            new("BTLED", "Bachelor of Technology and Livelihood Education"),
            new("EDD", "Doctor of Education"),
            new("PHDED", "Doctor of Philosophy in Education"),
            new("MAED", "Master of Arts in Education"),
        ]),
        new("COE", "College of Engineering", "COLLEGE OF ENGINEERING",
        [
            new("BSCPE", "Bachelor of Science in Computer Engineering"),
            new("BSECE", "Bachelor of Science in Electronics Engineering"),
        ]),
        new("CFEM", "College of Forestry and Environmental Management", "COLLEGE OF FORESTRY AND ENVIRONMENTAL MANAGEMENT",
        [
            new("BSES", "Bachelor of Science in Environmental Science"),
            new("BSF", "Bachelor of Science in Forestry"),
            new("PHDRM", "Doctor of Philosophy in Resource Management"),
            new("MSDRMCCA", "Master of Science in Disaster Risk Management and Climate Change Adaptation"),
            new("MSES", "Master of Science in Environmental Science"),
            new("MSF", "Master of Science in Forestry"),
        ]),
        new("CS", "College of Science", "COLLEGE OF SCIENCE",
        [
            new("BSBIO", "Bachelor of Science in Biology"),
            new("BSMATH", "Bachelor of Science in Mathematics"),
            new("BSPHYS", "Bachelor of Science in Physics"),
            new("BSPSYCH", "Bachelor of Science in Psychology"),
            new("MSBIO", "Master of Science in Biology"),
        ]),
    ];

    /// <summary>
    /// Program Chair designations on the workload summary, matched case-insensitively
    /// against the Designation column. Checked in order, so more specific patterns
    /// come first.
    /// </summary>
    public static readonly (string Pattern, string ProgramCode)[] ChairDesignations =
    [
        ("MSAS Program", "MSAS"), ("BSA Program", "BSA"), ("BAT Program", "BAT"),
        ("BSTM Program", "BSTM"), ("BSAB Program", "BSAB"), ("BSENTREP Program", "BSENTREP"), ("BSHM Program", "BSHM"),
        ("BSLEA Program", "BSLEA"), ("BSISM Program", "BSISM"), ("BS Criminology", "BSCRIM"),
        ("MSIT Program", "MSIT"), ("BSIT Program", "BSIT"), ("BSCS Program", "BSCS"), ("BSDSA Program", "BSDSA"),
        ("MSDC Program", "MSDC"), ("BSDC Program", "BSDC"), ("BA Socio", "BASOC"), ("BA Comm", "BACOMM"),
        ("PhD. Program", "PHDED"), ("BTLEd Program", "BTLED"), ("BPED Program", "BPED"), ("BECED Program", "BECED"),
        ("MAED Program", "MAED"), ("BSED Program", "BSED"),
        ("MSDRM", "MSDRMCCA"), ("PhDRM", "PHDRM"), ("BSF Program", "BSF"),
        ("BSCpE", "BSCPE"), ("BSECE", "BSECE"),
        ("BS Physics", "BSPHYS"), ("BS Mathematics", "BSMATH"), ("MS Biology", "MSBIO"),
        ("BS Psychology", "BSPSYCH"), ("BS Biology", "BSBIO"),
    ];

    /// <summary>Program code named in a designation, if any.</summary>
    public static string? ProgramCodeFromDesignation(string? designation)
    {
        if (string.IsNullOrWhiteSpace(designation)) return null;
        var d = System.Text.RegularExpressions.Regex.Replace(designation, @"\s+", " ");
        foreach (var (pattern, code) in ChairDesignations)
            if (d.Contains(pattern, StringComparison.OrdinalIgnoreCase)) return code;
        return null;
    }

    public static bool IsDeanDesignation(string? designation)
        => designation is not null && designation.Contains("College Dean", StringComparison.OrdinalIgnoreCase);

    public static bool IsProgramChairDesignation(string? designation)
        => designation is not null && designation.Contains("Program Chair", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Normalises a program name for matching: bracketed notes such as "(1st &amp; 2nd yr)"
    /// removed, "&amp;" read as "and", spacing collapsed, case ignored.
    /// </summary>
    public static string NormalizeProgramName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var s = System.Text.RegularExpressions.Regex.Replace(name, @"\(.*?\)", " ");
        s = s.Replace("&", " and ");
        s = System.Text.RegularExpressions.Regex.Replace(s, @"\s+", " ").Trim();
        return s.ToUpperInvariant();
    }

    /// <summary>The five semesters kept in the system, oldest first. The last is the current one.</summary>
    public static readonly (string AcademicYear, string Term)[] Semesters =
    [
        ("2024-2025", "1st Semester"),
        ("2024-2025", "2nd Semester"),
        ("2025-2026", "1st Semester"),
        ("2025-2026", "2nd Semester"),
        ("2026-2027", "1st Semester"),
    ];
}
