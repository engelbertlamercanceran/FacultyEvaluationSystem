using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using FacultyEvalSystem.Data;
using FacultyEvalSystem.Models;

namespace FacultyEvalSystem.Controllers;

/// <summary>
/// The SET and SEF instruments: categories and their benchmark statements.
/// Managed by CDI and CDI Staff (moved from the Administrator).
/// </summary>
[Authorize(Roles = "AA,AAStaff")]
public class CriteriaController : Controller
{
    private readonly ApplicationDbContext _db;

    public CriteriaController(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var categories = await _db.EvaluationCategories
            .Include(c => c.Criteria.OrderBy(cr => cr.SortOrder))
            .OrderBy(c => c.EvaluatorType)
            .ThenBy(c => c.SortOrder)
            .ToListAsync();
        return View(categories);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCategory(string name, string evaluatorType, int sortOrder)
    {
        _db.EvaluationCategories.Add(new EvaluationCategory
        {
            Name = name,
            EvaluatorType = evaluatorType,
            SortOrder = sortOrder
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = $"Category \"{name}\" created.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCategory(int id, string name, string evaluatorType, int sortOrder)
    {
        var category = await _db.EvaluationCategories.FindAsync(id);
        if (category is not null)
        {
            category.Name = name;
            category.EvaluatorType = evaluatorType;
            category.SortOrder = sortOrder;
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Category \"{name}\" updated.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var category = await _db.EvaluationCategories.Include(c => c.Criteria).FirstOrDefaultAsync(c => c.Id == id);
        if (category is not null)
        {
            _db.EvaluationCriteria.RemoveRange(category.Criteria);
            _db.EvaluationCategories.Remove(category);
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Category \"{category.Name}\" and its criteria deleted.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateCriterion(int categoryId, string description, int sortOrder)
    {
        _db.EvaluationCriteria.Add(new EvaluationCriterion
        {
            CategoryId = categoryId,
            Description = description,
            SortOrder = sortOrder
        });
        await _db.SaveChangesAsync();
        TempData["Success"] = "Criterion added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateCriterion(int id, string description, int sortOrder)
    {
        var criterion = await _db.EvaluationCriteria.FindAsync(id);
        if (criterion is not null)
        {
            criterion.Description = description;
            criterion.SortOrder = sortOrder;
            await _db.SaveChangesAsync();
            TempData["Success"] = "Criterion updated.";
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteCriterion(int id)
    {
        var criterion = await _db.EvaluationCriteria.FindAsync(id);
        if (criterion is not null)
        {
            _db.EvaluationCriteria.Remove(criterion);
            await _db.SaveChangesAsync();
            TempData["Success"] = "Criterion deleted.";
        }
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Both instruments as a PDF, in the order students and supervisors see them.</summary>
    public async Task<IActionResult> Download()
    {
        var categories = await _db.EvaluationCategories
            .Include(c => c.Criteria.OrderBy(cr => cr.SortOrder))
            .OrderBy(c => c.SortOrder)
            .ToListAsync();

        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().AlignCenter().Text("ISABELA STATE UNIVERSITY - CABAGAN").Bold().FontSize(13);
                    col.Item().AlignCenter().Text("FACULTY EVALUATION CRITERIA").Bold().FontSize(11);
                    col.Item().AlignCenter().Text("Per CHED CMO No. 19, Series of 2025").FontSize(9).Italic();
                    col.Item().PaddingBottom(10);
                });

                page.Content().Column(col =>
                {
                    foreach (var (type, title) in new[]
                    {
                        ("Student", "STUDENT EVALUATION OF TEACHERS (SET)"),
                        ("Supervisor", "SUPERVISOR'S EVALUATION OF FACULTY (SEF)")
                    })
                    {
                        // Each instrument starts on its own page
                        if (type == "Supervisor") col.Item().PageBreak();

                        var ofType = categories.Where(c => c.EvaluatorType == type).ToList();
                        col.Item().Text(title).Bold().FontSize(11);

                        if (ofType.Count == 0)
                        {
                            col.Item().PaddingTop(4).Text("No categories defined.").Italic();
                            continue;
                        }

                        int number = 1;
                        foreach (var cat in ofType)
                        {
                            col.Item().PaddingTop(8).Background(Colors.Grey.Lighten3).Padding(5)
                                .Text(cat.Name).Bold();

                            foreach (var criterion in cat.Criteria)
                            {
                                col.Item().PaddingLeft(5).PaddingTop(3).Row(row =>
                                {
                                    row.ConstantItem(25).Text($"{number++}.");
                                    row.RelativeItem().Text(criterion.Description);
                                });
                            }
                        }
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Generated on: " + DateTime.Now.ToString("MMMM dd, yyyy") + " | Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        });

        return File(pdf.GeneratePdf(), "application/pdf", "Evaluation_Criteria.pdf");
    }
}
