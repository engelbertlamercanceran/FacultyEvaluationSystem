using FacultyEvalSystem.Models;

namespace FacultyEvalSystem.ViewModels;

public class AdminDashboardViewModel
{
    public int TotalStudents { get; set; }
    public int TotalFaculty { get; set; }
    public int TotalColleges { get; set; }
    public int TotalEvaluations { get; set; }
    public Semester? ActiveSemester { get; set; }
    public EvaluationPeriod? ActivePeriod { get; set; }
    public List<EvaluationResult> TopPerformers { get; set; } = [];
    public List<EvaluationResult> LowPerformers { get; set; } = [];
    public List<CollegePerformance> CollegePerformances { get; set; } = [];
    public List<SemesterTrend> SemesterTrends { get; set; } = [];

    // DA-01
    public ComparativeAnalysis Comparative { get; set; } = new();
}

public class CollegePerformance
{
    public string CollegeName { get; set; } = "";
    public string CollegeCode { get; set; } = "";
    public double AverageRating { get; set; }
    public int FacultyCount { get; set; }
}

public class SemesterTrend
{
    public string Semester { get; set; } = "";
    public double AverageRating { get; set; }
    public int TotalEvaluations { get; set; }

    // DA-01: change against the semester immediately before this one
    public double? ChangeFromPrevious { get; set; }
    public int SortKey { get; set; }
}

/// <summary>
/// DA-01: comparative analysis across the five most recent semesters.
/// </summary>
public class ComparativeAnalysis
{
    /// <summary>The five most recent semesters, oldest first, as chart labels.</summary>
    public List<string> SemesterLabels { get; set; } = [];

    /// <summary>One series per college, aligned to SemesterLabels.</summary>
    public List<ComparisonSeries> CollegeSeries { get; set; } = [];

    /// <summary>Each program measured against its own college's average.</summary>
    public List<ProgramVsCollege> ProgramComparisons { get; set; } = [];

    /// <summary>Institution-wide average per semester, aligned to SemesterLabels.</summary>
    public List<double?> InstitutionAverages { get; set; } = [];

    public bool HasData => SemesterLabels.Count > 0;
}

public class ComparisonSeries
{
    public string Name { get; set; } = "";
    public List<double?> Values { get; set; } = [];
    public double? Latest => Values.LastOrDefault(v => v.HasValue);
    public double? Change { get; set; }
}

public class ProgramVsCollege
{
    public string ProgramCode { get; set; } = "";
    public string ProgramName { get; set; } = "";
    public string CollegeCode { get; set; } = "";
    public double ProgramAverage { get; set; }
    public double CollegeAverage { get; set; }
    public int FacultyCount { get; set; }
    public double Difference => Math.Round(ProgramAverage - CollegeAverage, 2);
}

public class FacultyDashboardViewModel
{
    public string FacultyName { get; set; } = "";
    public EvaluationResult? LatestResult { get; set; }
    public List<EvaluationResult> History { get; set; } = [];
    public Dictionary<string, double> CategoryBreakdown { get; set; } = [];
}
