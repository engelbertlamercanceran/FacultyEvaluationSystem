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
}

public class FacultyDashboardViewModel
{
    public string FacultyName { get; set; } = "";
    public EvaluationResult? LatestResult { get; set; }
    public List<EvaluationResult> History { get; set; } = [];
    public Dictionary<string, double> CategoryBreakdown { get; set; } = [];
}
