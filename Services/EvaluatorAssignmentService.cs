using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FacultyEvalSystem.Data;
using FacultyEvalSystem.Models;

namespace FacultyEvalSystem.Services;

/// <summary>
/// Works out who evaluates whom, so the evaluation rosters and the Development
/// Plan screens can never disagree with each other.
///
/// PC-01  Program Chair -> faculty teaching courses owned by their program
/// DN-01  Dean          -> Program Chairs in their college
/// AA-01  AA            -> Program Chairs with no designated evaluator
/// </summary>
public class EvaluatorAssignmentService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;

    public EvaluatorAssignmentService(ApplicationDbContext db, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    /// <summary>Active Program Chairs, with College and Program navigations loaded.</summary>
    public async Task<List<ApplicationUser>> GetProgramChairsAsync()
    {
        var chairs = await _userManager.GetUsersInRoleAsync("ProgramChair");
        var ids = chairs.Where(c => c.IsActive).Select(c => c.Id).ToList();

        return await _db.Users
            .Include(u => u.College)
            .Include(u => u.Program)
            .Where(u => ids.Contains(u.Id))
            .ToListAsync();
    }

    /// <summary>Ids of active Deans, keyed by the college they head.</summary>
    public async Task<HashSet<int>> GetCollegesWithDeanAsync()
    {
        var deans = await _userManager.GetUsersInRoleAsync("Dean");
        return deans
            .Where(d => d.IsActive && d.CollegeId is not null)
            .Select(d => d.CollegeId!.Value)
            .ToHashSet();
    }

    /// <summary>
    /// DN-01: the Program Chairs a given Dean evaluates.
    ///
    /// A Chair explicitly designated to somebody else is excluded, so an explicit
    /// designation always wins over the default "your college's Dean" rule.
    /// </summary>
    public async Task<List<ApplicationUser>> GetChairsForDeanAsync(ApplicationUser dean)
    {
        var chairs = await GetProgramChairsAsync();

        return chairs
            .Where(c => c.CollegeId == dean.CollegeId
                     && c.Id != dean.Id
                     && (c.DesignatedEvaluatorId is null || c.DesignatedEvaluatorId == dean.Id))
            .ToList();
    }

    /// <summary>
    /// AA-01: the Program Chairs the AA account covers — those with no designated
    /// evaluator and no Dean in their college to fall back on.
    ///
    /// Without the second condition a Chair would be evaluated twice: once by their
    /// Dean under DN-01 and once by AA.
    /// </summary>
    public async Task<List<ApplicationUser>> GetChairsWithoutEvaluatorAsync()
    {
        var chairs = await GetProgramChairsAsync();
        var collegesWithDean = await GetCollegesWithDeanAsync();

        return chairs
            .Where(c => c.DesignatedEvaluatorId is null
                     && (c.CollegeId is null || !collegesWithDean.Contains(c.CollegeId.Value)))
            .ToList();
    }

    /// <summary>
    /// PC-01: faculty teaching a course owned by this Chair's program in the
    /// period's semester, including faculty whose home department is elsewhere.
    /// </summary>
    public async Task<List<FacultySubject>> GetProgramTeachingAssignmentsAsync(
        ApplicationUser chair, int semesterId)
    {
        if (chair.ProgramId is null) return [];

        return await _db.FacultySubjects
            .Include(fs => fs.Faculty).ThenInclude(f => f.College)
            .Include(fs => fs.Faculty).ThenInclude(f => f.Program)
            .Where(fs => fs.SemesterId == semesterId
                      && fs.ProgramId == chair.ProgramId
                      && fs.Faculty.IsActive
                      && fs.FacultyId != chair.Id)
            .ToListAsync();
    }

    /// <summary>
    /// The ids of everyone this supervisor is responsible for in the given period.
    /// Used by both the evaluation roster and the Development Plan screens.
    /// </summary>
    public async Task<List<string>> GetSupervisedIdsAsync(
        ApplicationUser user, IList<string> roles, int periodId)
    {
        if (roles.Contains("ProgramChair"))
        {
            var period = await _db.EvaluationPeriods.FindAsync(periodId);
            if (period is null) return [];

            var assignments = await GetProgramTeachingAssignmentsAsync(user, period.SemesterId);
            return assignments.Select(a => a.FacultyId).Distinct().ToList();
        }

        if (roles.Contains("Dean"))
            return (await GetChairsForDeanAsync(user)).Select(c => c.Id).ToList();

        if (roles.Contains("AA"))
            return (await GetChairsWithoutEvaluatorAsync()).Select(c => c.Id).ToList();

        return [];
    }
}
