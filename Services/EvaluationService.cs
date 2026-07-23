using Microsoft.EntityFrameworkCore;
using FacultyEvalSystem.Data;
using FacultyEvalSystem.Models;

namespace FacultyEvalSystem.Services;

public class EvaluationService
{
    private readonly ApplicationDbContext _db;

    public EvaluationService(ApplicationDbContext db) => _db = db;

    // Student weight = 60%, Supervisor weight = 40% per CMO No. 19
    private const double StudentWeight = 0.60;
    private const double SupervisorWeight = 0.40;

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
                .ThenInclude(r => r.Criterion)
                    .ThenInclude(c => c.Category)
            .Where(e => e.EvaluationPeriodId == evaluationPeriodId && e.FacultyId == facultyId)
            .ToListAsync();

        var studentEvals = evaluations.Where(e => e.EvaluatorType == "Student").ToList();
        var supervisorEvals = evaluations.Where(e => e.EvaluatorType == "Supervisor").ToList();

        double studentRating = ComputeWeightedRating(studentEvals);
        double supervisorRating = ComputeWeightedRating(supervisorEvals);

        double overall = 0;
        if (studentEvals.Count > 0 && supervisorEvals.Count > 0)
            overall = (studentRating * StudentWeight) + (supervisorRating * SupervisorWeight);
        else if (studentEvals.Count > 0)
            overall = studentRating;
        else if (supervisorEvals.Count > 0)
            overall = supervisorRating;

        var existing = await _db.EvaluationResults
            .FirstOrDefaultAsync(r => r.EvaluationPeriodId == evaluationPeriodId && r.FacultyId == facultyId);

        if (existing is not null)
        {
            existing.StudentRating = Math.Round(studentRating, 2);
            existing.StudentRespondents = studentEvals.Count;
            existing.SupervisorRating = Math.Round(supervisorRating, 2);
            existing.SupervisorRespondents = supervisorEvals.Count;
            existing.OverallRating = Math.Round(overall, 2);
            existing.DescriptiveRating = EvaluationResult.GetDescriptiveRating(overall);
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
                OverallRating = Math.Round(overall, 2),
                DescriptiveRating = EvaluationResult.GetDescriptiveRating(overall),
            });
        }

        await _db.SaveChangesAsync();
    }

    private static double ComputeWeightedRating(List<Evaluation> evaluations)
    {
        if (evaluations.Count == 0) return 0;

        var allResponses = evaluations.SelectMany(e => e.Responses).ToList();
        if (allResponses.Count == 0) return 0;

        // Group by category to apply weights
        var categoryGroups = allResponses
            .GroupBy(r => r.Criterion.Category)
            .ToList();

        double totalWeight = categoryGroups.Sum(g => g.Key.Weight);
        if (totalWeight == 0) return 0;

        double weightedSum = 0;
        foreach (var group in categoryGroups)
        {
            double categoryAvg = group.Average(r => r.Rating);
            weightedSum += categoryAvg * (group.Key.Weight / totalWeight);
        }

        return weightedSum;
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
