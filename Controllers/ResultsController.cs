using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using FacultyEvalSystem.Data;

namespace FacultyEvalSystem.Controllers;

// Moved from the Administrator to CDI and CDI Staff
[Authorize(Roles = "CEO,AA,AAStaff,QA")]
public class ResultsController : Controller
{
    private readonly ApplicationDbContext _db;

    public ResultsController(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(int? periodId)
    {
        var periods = await _db.EvaluationPeriods.Include(p => p.Semester)
            .OrderByDescending(p => p.Semester.SortKey).ThenByDescending(p => p.Id).ToListAsync();
        periodId ??= periods.FirstOrDefault()?.Id;
        ViewBag.Periods = new SelectList(periods, "Id", "DisplayName", periodId);

        var results = periodId.HasValue
            ? await _db.EvaluationResults
                .Include(r => r.Faculty).ThenInclude(f => f.College)
                .Include(r => r.EvaluationPeriod).ThenInclude(p => p.Semester)
                .Where(r => r.EvaluationPeriodId == periodId.Value)
                .OrderBy(r => r.Faculty.LastName).ThenBy(r => r.Faculty.FirstName)
                .ToListAsync()
            : [];

        return View("~/Views/Admin/Results.cshtml", results);
    }
}
