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
/// AA-01  CDI office    -> Program Chairs with no designated evaluator
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

        if (RoleNames.IsCdiOffice(roles))
            return (await GetChairsWithoutEvaluatorAsync()).Select(c => c.Id).ToList();

        return [];
    }

    /// <summary>
    /// Ids of the CDI and CDI Staff accounts. They act as one office: an evaluation
    /// or Development Plan by either counts for both.
    /// </summary>
    public Task<List<string>> GetCdiOfficeIdsAsync() => UserIdsInRolesAsync(RoleNames.CdiOffice);

    /// <summary>Ids of active users holding any of the given roles.</summary>
    private async Task<List<string>> UserIdsInRolesAsync(params string[] roleNames)
    {
        return await (from ur in _db.UserRoles
                      join r in _db.Roles on ur.RoleId equals r.Id
                      where roleNames.Contains(r.Name!)
                      select ur.UserId).Distinct().ToListAsync();
    }

    /// <summary>
    /// Whose Development Plans a supervisor may create and edit:
    ///   Program Chair -> the faculty they evaluate (PC-01) plus the faculty of their program;
    ///   Dean          -> every faculty member and Program Chair under their college;
    ///   CDI office    -> Program Chairs with no designated evaluator (AA-01).
    /// </summary>
    public async Task<List<string>> GetDevelopmentPlanScopeAsync(
        ApplicationUser user, IList<string> roles, int periodId)
    {
        var teachingStaff = await UserIdsInRolesAsync("Faculty", "ProgramChair");

        if (roles.Contains("ProgramChair"))
        {
            var ids = await GetSupervisedIdsAsync(user, roles, periodId);
            if (user.ProgramId is not null)
            {
                ids.AddRange(await _db.Users
                    .Where(u => u.IsActive && u.ProgramId == user.ProgramId && u.Id != user.Id
                             && teachingStaff.Contains(u.Id))
                    .Select(u => u.Id)
                    .ToListAsync());
            }
            return ids.Distinct().ToList();
        }

        if (roles.Contains("Dean"))
        {
            if (user.CollegeId is null) return [];
            return await _db.Users
                .Where(u => u.IsActive && u.CollegeId == user.CollegeId && u.Id != user.Id
                         && teachingStaff.Contains(u.Id))
                .Select(u => u.Id)
                .ToListAsync();
        }

        if (RoleNames.IsCdiOffice(roles))
            return (await GetChairsWithoutEvaluatorAsync()).Select(c => c.Id).ToList();

        return [];
    }

    /// <summary>
    /// The Program Chair concerned with a faculty member's Development Plan: the Chair
    /// of the faculty member's own program, otherwise the Chair of a program in the
    /// same college whose courses they teach this semester (PC-01). Null when the
    /// faculty member is themselves the only Chair in question.
    /// </summary>
    public async Task<ApplicationUser?> FindConcernedProgramChairAsync(ApplicationUser faculty, int semesterId)
    {
        var chairs = (await GetProgramChairsAsync()).Where(c => c.Id != faculty.Id).ToList();

        if (faculty.ProgramId is not null)
        {
            var own = chairs.FirstOrDefault(c => c.ProgramId == faculty.ProgramId);
            if (own is not null) return own;
        }

        var teachingPrograms = await _db.FacultySubjects
            .Where(fs => fs.FacultyId == faculty.Id && fs.SemesterId == semesterId && fs.ProgramId != null)
            .GroupBy(fs => fs.ProgramId)
            .OrderByDescending(g => g.Count())
            .Select(g => g.Key)
            .ToListAsync();

        foreach (var programId in teachingPrograms)
        {
            var chair = chairs.FirstOrDefault(c => c.ProgramId == programId
                                                && (faculty.CollegeId is null || c.CollegeId == faculty.CollegeId));
            if (chair is not null) return chair;
        }

        return null;
    }

    /// <summary>
    /// The supervisor named as reviewer on a faculty member's IFER and FEDAF: whoever
    /// actually submitted their supervisor evaluation in the period. When several
    /// Chairs evaluated them, the Chair of their own department is preferred. Before
    /// anyone has evaluated, falls back to the expected evaluator — the Dean for a
    /// Program Chair, otherwise the Program Chair concerned.
    /// </summary>
    public async Task<ApplicationUser?> FindReviewerAsync(ApplicationUser faculty, int periodId)
    {
        var evaluators = await _db.Evaluations
            .Where(e => e.FacultyId == faculty.Id && e.EvaluationPeriodId == periodId
                     && e.EvaluatorType == "Supervisor")
            .OrderBy(e => e.SubmittedAt)
            .Select(e => e.Evaluator)
            .ToListAsync();

        if (evaluators.Count > 0)
            return evaluators.FirstOrDefault(e => faculty.ProgramId is not null && e.ProgramId == faculty.ProgramId)
                ?? evaluators[0];

        if (await _userManager.IsInRoleAsync(faculty, "ProgramChair"))
            return await FindDeanAsync(faculty.CollegeId);

        var period = await _db.EvaluationPeriods.FindAsync(periodId);
        return period is null ? null : await FindConcernedProgramChairAsync(faculty, period.SemesterId);
    }

    /// <summary>The active Dean of a college, if there is one.</summary>
    public async Task<ApplicationUser?> FindDeanAsync(int? collegeId)
    {
        if (collegeId is null) return null;
        var deans = await _userManager.GetUsersInRoleAsync("Dean");
        return deans.FirstOrDefault(d => d.IsActive && d.CollegeId == collegeId);
    }

    /// <summary>Active Deans and Program Chairs — notified when the supervisor evaluation opens.</summary>
    public async Task<List<string>> GetSupervisorIdsAsync()
    {
        var ids = await UserIdsInRolesAsync("Dean", "ProgramChair");
        return await _db.Users.Where(u => u.IsActive && ids.Contains(u.Id)).Select(u => u.Id).ToListAsync();
    }
}
