using Microsoft.EntityFrameworkCore;
using FacultyEvalSystem.Data;
using FacultyEvalSystem.Models;

namespace FacultyEvalSystem.Services;

public class EvaluationService
{
    private readonly ApplicationDbContext _db;

    public EvaluationService(ApplicationDbContext db) => _db = db;

    // Max possible score per CMO No. 19: 15 items × 5 = 75
    private const double MaxScore = 75.0;

    public async Task ComputeResultsAsync(int evaluationPeriodId)
    {
        var facultyIds = await _db.Evaluations
            .Where(e => e.EvaluationPeriodId == evaluationPeriodId)
            .Select(e => e.FacultyId)
            .Distinct()
            .ToListAsync();

        foreach (var facultyId in facultyIds)
        {
            await ComputeFacultyResultAsync(evaluationPeriodId, facultyId);
        }
    }

    public async Task ComputeFacultyResultAsync(int evaluationPeriodId, string facultyId)
    {
        var evaluations = await _db.Evaluations
            .Include(e => e.Responses)
            .Where(e => e.EvaluationPeriodId == evaluationPeriodId && e.FacultyId == facultyId)
            .ToListAsync();

        var studentEvals = evaluations.Where(e => e.EvaluatorType == "Student").ToList();
        var supervisorEvals = evaluations.Where(e => e.EvaluatorType == "Supervisor").ToList();

        double studentRating = ComputeClassWeightedSETRating(studentEvals);
        double supervisorRating = ComputeSEFRating(supervisorEvals);

        var existing = await _db.EvaluationResults
            .FirstOrDefaultAsync(r => r.EvaluationPeriodId == evaluationPeriodId && r.FacultyId == facultyId);

        if (existing is not null)
        {
            existing.StudentRating = Math.Round(studentRating, 2);
            existing.StudentRespondents = studentEvals.Count;
            existing.SupervisorRating = Math.Round(supervisorRating, 2);
            existing.SupervisorRespondents = supervisorEvals.Count;
            existing.StudentDescriptiveRating = studentEvals.Count > 0 ? EvaluationResult.GetDescriptiveRating(studentRating) : "";
            existing.SupervisorDescriptiveRating = supervisorEvals.Count > 0 ? EvaluationResult.GetDescriptiveRating(supervisorRating) : "";
            existing.ComputedAt = DateTime.Now;
        }
        else
        {
            _db.EvaluationResults.Add(new EvaluationResult
            {
                EvaluationPeriodId = evaluationPeriodId,
                FacultyId = facultyId,
                StudentRating = Math.Round(studentRating, 2),
                StudentRespondents = studentEvals.Count,
                SupervisorRating = Math.Round(supervisorRating, 2),
                SupervisorRespondents = supervisorEvals.Count,
                StudentDescriptiveRating = studentEvals.Count > 0 ? EvaluationResult.GetDescriptiveRating(studentRating) : "",
                SupervisorDescriptiveRating = supervisorEvals.Count > 0 ? EvaluationResult.GetDescriptiveRating(supervisorRating) : "",
            });
        }

        await _db.SaveChangesAsync();
    }

    /// <summary>
    /// CMO Section 8.3: Weighted computation of overall SET rating by class size.
    /// Step 1: Get average SET rating per class.
    /// Step 2: Multiply by number of students in that class.
    /// Step 3: Overall SET = total weighted score / total students.
    /// Formula per evaluator: Rating = (Total Score / 75) × 100
    /// </summary>
    private static double ComputeClassWeightedSETRating(List<Evaluation> studentEvals)
    {
        if (studentEvals.Count == 0) return 0;

        // Group by class (FacultySubjectId)
        var byClass = studentEvals.GroupBy(e => e.FacultySubjectId).ToList();

        double totalWeightedScore = 0;
        int totalStudents = 0;

        foreach (var classGroup in byClass)
        {
            var classEvals = classGroup.ToList();
            int numStudents = classEvals.Count;

            // Average SET rating for this class
            double classAvg = classEvals.Average(e =>
                (e.Responses.Sum(r => r.Rating) / MaxScore) * 100);

            totalWeightedScore += numStudents * classAvg;
            totalStudents += numStudents;
        }

        return totalStudents > 0 ? totalWeightedScore / totalStudents : 0;
    }

    /// <summary>
    /// SEF Rating = (Total Score / 75) × 100, averaged across supervisors.
    /// </summary>
    private static double ComputeSEFRating(List<Evaluation> supervisorEvals)
    {
        if (supervisorEvals.Count == 0) return 0;

        return supervisorEvals.Average(e =>
            (e.Responses.Sum(r => r.Rating) / MaxScore) * 100);
    }

    public async Task<Dictionary<string, double>> GetCategoryBreakdownAsync(int evaluationPeriodId, string facultyId, string evaluatorType)
    {
        var responses = await _db.EvaluationResponses
            .Include(r => r.Criterion)
                .ThenInclude(c => c.Category)
            .Include(r => r.Evaluation)
            .Where(r => r.Evaluation.EvaluationPeriodId == evaluationPeriodId
                     && r.Evaluation.FacultyId == facultyId
                     && r.Evaluation.EvaluatorType == evaluatorType)
            .ToListAsync();

        return responses
            .GroupBy(r => r.Criterion.Category.Name)
            .ToDictionary(g => g.Key, g => Math.Round(g.Average(r => r.Rating), 2));
    }
}
