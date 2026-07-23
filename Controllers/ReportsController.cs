using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using FacultyEvalSystem.Data;
using FacultyEvalSystem.Models;
using FacultyEvalSystem.Services;

namespace FacultyEvalSystem.Controllers;

[Authorize(Roles = "Admin,Dean,ProgramChair")]
public class ReportsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly EvaluationService _evalService;

    public ReportsController(ApplicationDbContext db, EvaluationService evalService)
    {
        _db = db;
        _evalService = evalService;
    }

    public async Task<IActionResult> Index()
    {
        var periods = await _db.EvaluationPeriods.Include(p => p.Semester).OrderByDescending(p => p.Id).ToListAsync();
        ViewBag.Periods = new SelectList(periods, "Id", "DisplayName");
        return View();
    }

    // Generate Individual Faculty Evaluation Report (IFER) PDF
    [HttpGet]
    public async Task<IActionResult> IFER(int periodId, string facultyId)
    {
        var result = await _db.EvaluationResults
            .Include(r => r.Faculty).ThenInclude(f => f.College)
            .Include(r => r.EvaluationPeriod).ThenInclude(p => p.Semester)
            .FirstOrDefaultAsync(r => r.EvaluationPeriodId == periodId && r.FacultyId == facultyId);

        if (result is null) return NotFound("No evaluation results found.");

        var studentBreakdown = await _evalService.GetCategoryBreakdownAsync(periodId, facultyId, "Student");
        var supervisorBreakdown = await _evalService.GetCategoryBreakdownAsync(periodId, facultyId, "Supervisor");

        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().AlignCenter().Text("ISABELA STATE UNIVERSITY").Bold().FontSize(14);
                    col.Item().AlignCenter().Text("Cabagan Campus");
                    col.Item().AlignCenter().Text("INDIVIDUAL FACULTY EVALUATION REPORT (IFER)").Bold().FontSize(12);
                    col.Item().PaddingBottom(10);
                });

                page.Content().Column(col =>
                {
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text($"Faculty Name: {result.Faculty.FullName}").Bold();
                        row.RelativeItem().Text($"College: {result.Faculty.College?.Code ?? "N/A"}");
                    });
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Text($"Semester: {result.EvaluationPeriod.Semester.DisplayName}");
                        row.RelativeItem().Text($"Date Computed: {result.ComputedAt:MMMM dd, yyyy}");
                    });
                    col.Item().PaddingVertical(10).LineHorizontal(1);

                    // Student Evaluation
                    col.Item().Text("A. STUDENT EVALUATION (60%)").Bold();
                    col.Item().Text($"   Number of Respondents: {result.StudentRespondents}");
                    col.Item().PaddingVertical(5);

                    if (studentBreakdown.Count > 0)
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(1);
                            });
                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Category").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignCenter().Text("Rating").Bold();
                            });
                            foreach (var cat in studentBreakdown)
                            {
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(cat.Key);
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignCenter().Text(cat.Value.ToString("F2"));
                            }
                        });
                    }
                    col.Item().Text($"   Student Rating: {result.StudentRating:F2}").Bold();
                    col.Item().PaddingVertical(10);

                    // Supervisor Evaluation
                    col.Item().Text("B. SUPERVISOR EVALUATION (40%)").Bold();
                    col.Item().Text($"   Number of Respondents: {result.SupervisorRespondents}");
                    col.Item().PaddingVertical(5);

                    if (supervisorBreakdown.Count > 0)
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn(1);
                            });
                            table.Header(header =>
                            {
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(5).Text("Category").Bold();
                                header.Cell().Background(Colors.Grey.Lighten3).Padding(5).AlignCenter().Text("Rating").Bold();
                            });
                            foreach (var cat in supervisorBreakdown)
                            {
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).Text(cat.Key);
                                table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignCenter().Text(cat.Value.ToString("F2"));
                            }
                        });
                    }
                    col.Item().Text($"   Supervisor Rating: {result.SupervisorRating:F2}").Bold();
                    col.Item().PaddingVertical(10);

                    // Overall
                    col.Item().LineHorizontal(1);
                    col.Item().PaddingVertical(5);
                    col.Item().Text($"OVERALL RATING: {result.OverallRating:F2} ({result.DescriptiveRating})").Bold().FontSize(12);
                    col.Item().PaddingVertical(20);

                    // Signatures
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Prepared by:").FontSize(9);
                            c.Item().PaddingTop(25).LineHorizontal(1);
                            c.Item().Text("Quality Assurance Officer").FontSize(9);
                        });
                        row.ConstantItem(40);
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Noted by:").FontSize(9);
                            c.Item().PaddingTop(25).LineHorizontal(1);
                            c.Item().Text("Dean / Program Chair").FontSize(9);
                        });
                    });
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        });

        var bytes = pdf.GeneratePdf();
        return File(bytes, "application/pdf", $"IFER_{result.Faculty.LastName}_{result.EvaluationPeriod.Semester.AcademicYear}.pdf");
    }

    // Generate Faculty Evaluation and Development Acknowledgement Form (FEDAF) PDF
    // Per CHED CMO No. 19, Series of 2025 - Annex D
    [HttpGet]
    public async Task<IActionResult> FEDAF(int periodId, string facultyId)
    {
        var result = await _db.EvaluationResults
            .Include(r => r.Faculty).ThenInclude(f => f.College)
            .Include(r => r.EvaluationPeriod).ThenInclude(p => p.Semester)
            .FirstOrDefaultAsync(r => r.EvaluationPeriodId == periodId && r.FacultyId == facultyId);

        if (result is null) return NotFound("No evaluation results found.");

        // Get qualitative comments from evaluations
        var comments = await _db.Evaluations
            .Where(e => e.EvaluationPeriodId == periodId && e.FacultyId == facultyId && !string.IsNullOrEmpty(e.Comments))
            .Select(e => new { e.EvaluatorType, e.Comments })
            .ToListAsync();

        var studentComments = comments.Where(c => c.EvaluatorType == "Student").Select(c => c.Comments!).ToList();
        var supervisorComments = comments.Where(c => c.EvaluatorType == "Supervisor").Select(c => c.Comments!).ToList();

        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10));

                page.Header().Column(col =>
                {
                    col.Item().AlignCenter().Text("ANNEX D").FontSize(9).Italic();
                    col.Item().AlignCenter().Text("ISABELA STATE UNIVERSITY").Bold().FontSize(13);
                    col.Item().AlignCenter().Text("Cabagan Campus").FontSize(11);
                    col.Item().PaddingTop(5).AlignCenter().Text("FACULTY EVALUATION AND DEVELOPMENT ACKNOWLEDGEMENT FORM").Bold().FontSize(11);
                    col.Item().PaddingBottom(10);
                });

                page.Content().Column(col =>
                {
                    // Section A: Faculty Member Information
                    col.Item().Text("A. FACULTY MEMBER INFORMATION").Bold().FontSize(10);
                    col.Item().PaddingVertical(5).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(1.5f);
                            columns.ConstantColumn(10);
                            columns.RelativeColumn(3);
                        });

                        void InfoRow(string label, string value)
                        {
                            table.Cell().Padding(3).Text(label);
                            table.Cell().Padding(3).Text(":");
                            table.Cell().Padding(3).Text(value).Bold();
                        }

                        InfoRow("Name of Faculty", result.Faculty.FullName);
                        InfoRow("Department/College", result.Faculty.College?.Name ?? "N/A");
                        InfoRow("Current Faculty Rank", "Faculty Member");
                        InfoRow("Semester/Term & Academic Year", result.EvaluationPeriod.Semester.DisplayName);
                    });

                    col.Item().PaddingVertical(8).LineHorizontal(1);

                    // Section B: Faculty Evaluation Summary
                    col.Item().Text("B. FACULTY EVALUATION SUMMARY").Bold().FontSize(10);
                    col.Item().PaddingVertical(5).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(1);
                            columns.RelativeColumn(1);
                        });

                        table.Header(header =>
                        {
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(8).AlignCenter()
                                .Text("Overall Rating").Bold();
                            header.Cell().Background(Colors.Grey.Lighten3).Padding(8).AlignCenter()
                                .Text("Overall Rating").Bold();
                        });

                        // Sub-headers
                        table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignCenter()
                            .Text("Student Evaluation of Teachers (SET)").Bold().FontSize(9);
                        table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(5).AlignCenter()
                            .Text("Supervisor's Evaluation of Faculty (SEF)").Bold().FontSize(9);

                        // Values
                        table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(12).AlignCenter()
                            .Text(result.StudentRating.ToString("F2")).Bold().FontSize(14);
                        table.Cell().Border(1).BorderColor(Colors.Grey.Lighten2).Padding(12).AlignCenter()
                            .Text(result.SupervisorRating.ToString("F2")).Bold().FontSize(14);
                    });

                    col.Item().PaddingVertical(8).LineHorizontal(1);

                    // Section C: Development Plan
                    col.Item().Text("C. Development Plan (to be jointly accomplished by the Supervisor and Faculty)").Bold().FontSize(10);
                    col.Item().PaddingVertical(5);

                    // Areas for Improvement
                    col.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Column(box =>
                    {
                        box.Item().Background(Colors.Grey.Lighten3).Padding(5).Text("Areas for Improvement").Bold().FontSize(9);
                        box.Item().Padding(8).MinHeight(60).Text("").FontSize(9);
                    });

                    col.Item().PaddingTop(5).Border(1).BorderColor(Colors.Grey.Lighten2).Column(box =>
                    {
                        box.Item().Background(Colors.Grey.Lighten3).Padding(5).Text("Proposed Learning and Development Activities").Bold().FontSize(9);
                        box.Item().Padding(8).MinHeight(60).Text("").FontSize(9);
                    });

                    col.Item().PaddingTop(5).Border(1).BorderColor(Colors.Grey.Lighten2).Column(box =>
                    {
                        box.Item().Background(Colors.Grey.Lighten3).Padding(5).Text("Action Plan").Bold().FontSize(9);
                        box.Item().Padding(8).MinHeight(60).Text("").FontSize(9);
                    });

                    col.Item().PaddingVertical(10);

                    // Acknowledgement statement
                    col.Item().Text("I acknowledge that I have received and reviewed the faculty evaluation conducted for the period mentioned above. I understand that my signature below does not necessarily indicate agreement with the evaluation but confirms that I have been given the opportunity to discuss it with my supervisor.").FontSize(9).Italic();

                    col.Item().PaddingVertical(15);

                    // Signature lines
                    col.Item().Border(1).BorderColor(Colors.Grey.Lighten2).Column(sigBox =>
                    {
                        // Supervisor section
                        sigBox.Item().Background(Colors.Grey.Lighten3).Padding(5).AlignCenter().Text("SUPERVISOR").Bold();
                        sigBox.Item().Padding(8).Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Signature").FontSize(9);
                                c.Item().PaddingTop(20).LineHorizontal(1);
                            });
                            row.ConstantItem(20);
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Name").FontSize(9);
                                c.Item().PaddingTop(20).LineHorizontal(1);
                            });
                            row.ConstantItem(20);
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Date Signed").FontSize(9);
                                c.Item().PaddingTop(20).LineHorizontal(1);
                            });
                        });

                        // Faculty section
                        sigBox.Item().Background(Colors.Grey.Lighten3).Padding(5).AlignCenter().Text("FACULTY").Bold();
                        sigBox.Item().Padding(8).Row(row =>
                        {
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Signature").FontSize(9);
                                c.Item().PaddingTop(20).LineHorizontal(1);
                            });
                            row.ConstantItem(20);
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Name").FontSize(9);
                                c.Item().PaddingTop(20).LineHorizontal(1);
                            });
                            row.ConstantItem(20);
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("Date Signed").FontSize(9);
                                c.Item().PaddingTop(20).LineHorizontal(1);
                            });
                        });
                    });
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("FEDAF - Per CHED CMO No. 19, Series of 2025 (Annex D) | Generated: " + DateTime.Now.ToString("MMMM dd, yyyy"));
                });
            });
        });

        var bytes = pdf.GeneratePdf();
        return File(bytes, "application/pdf", $"FEDAF_{result.Faculty.LastName}_{result.EvaluationPeriod.Semester.AcademicYear}.pdf");
    }

    // Generate summary report for all faculty in a period
    [HttpGet]
    public async Task<IActionResult> SummaryReport(int periodId)
    {
        var period = await _db.EvaluationPeriods.Include(p => p.Semester).FirstOrDefaultAsync(p => p.Id == periodId);
        if (period is null) return NotFound();

        var results = await _db.EvaluationResults
            .Include(r => r.Faculty).ThenInclude(f => f.College)
            .Where(r => r.EvaluationPeriodId == periodId)
            .OrderByDescending(r => r.OverallRating)
            .ToListAsync();

        var pdf = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(30);
                page.DefaultTextStyle(x => x.FontSize(9));

                page.Header().Column(col =>
                {
                    col.Item().AlignCenter().Text("ISABELA STATE UNIVERSITY - CABAGAN").Bold().FontSize(13);
                    col.Item().AlignCenter().Text("FACULTY EVALUATION SUMMARY REPORT").Bold().FontSize(11);
                    col.Item().AlignCenter().Text(period.Semester.DisplayName);
                    col.Item().PaddingBottom(10);
                });

                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.ConstantColumn(30);
                        columns.RelativeColumn(3);
                        columns.RelativeColumn(1.5f);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1);
                        columns.RelativeColumn(1.5f);
                    });

                    table.Header(header =>
                    {
                        var style = TextStyle.Default.Bold();
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("#").Style(style);
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("Faculty Name").Style(style);
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(4).Text("College").Style(style);
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignCenter().Text("Student").Style(style);
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignCenter().Text("# Resp.").Style(style);
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignCenter().Text("Supervisor").Style(style);
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignCenter().Text("Overall").Style(style);
                        header.Cell().Background(Colors.Grey.Lighten3).Padding(4).AlignCenter().Text("Description").Style(style);
                    });

                    for (int i = 0; i < results.Count; i++)
                    {
                        var r = results[i];
                        var bg = i % 2 == 0 ? Colors.White : Colors.Grey.Lighten4;
                        table.Cell().Background(bg).Padding(4).Text((i + 1).ToString());
                        table.Cell().Background(bg).Padding(4).Text(r.Faculty.FullName);
                        table.Cell().Background(bg).Padding(4).Text(r.Faculty.College?.Code ?? "N/A");
                        table.Cell().Background(bg).Padding(4).AlignCenter().Text(r.StudentRating.ToString("F2"));
                        table.Cell().Background(bg).Padding(4).AlignCenter().Text(r.StudentRespondents.ToString());
                        table.Cell().Background(bg).Padding(4).AlignCenter().Text(r.SupervisorRating.ToString("F2"));
                        table.Cell().Background(bg).Padding(4).AlignCenter().Text(r.OverallRating.ToString("F2"));
                        table.Cell().Background(bg).Padding(4).AlignCenter().Text(r.DescriptiveRating);
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Generated on: " + DateTime.Now.ToString("MMMM dd, yyyy hh:mm tt") + " | Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            });
        });

        var bytes = pdf.GeneratePdf();
        return File(bytes, "application/pdf", $"Summary_Report_{period.Semester.AcademicYear}.pdf");
    }
}
