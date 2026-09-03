using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using FacultyEvalSystem.Data;

namespace FacultyEvalSystem.Controllers;

[Authorize(Roles = "Admin,CEO,AA,QA")]
public class ResultsController : Controller
{
    private readonly ApplicationDbContext _db;

    public ResultsController(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(int? periodId)
    {
        var periods = await _db.EvaluationPeriods.Include(p => p.Semester).OrderByDescending(p => p.Id).ToListAsync();
        ViewBag.Periods = new SelectList(periods, "Id", "DisplayName", periodId);

        var results = periodId.HasValue
            ? await _db.EvaluationResults
                .Include(r => r.Faculty).ThenInclude(f => f.College)
                .Include(r => r.EvaluationPeriod).ThenInclude(p => p.Semester)
                .Where(r => r.EvaluationPeriodId == periodId.Value)
                .OrderByDescending(r => r.StudentRating)
                .ToListAsync()
            : [];

        return View("~/Views/Admin/Results.cshtml", results);
    }
}
