using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FacultyEvalSystem.Services;

namespace FacultyEvalSystem.Controllers;

[Authorize(Roles = "Admin")]
public class DocumentationController : Controller
{
    [HttpGet]
    public IActionResult Download()
    {
        var pdf = DocumentationService.GenerateSystemDocumentation();
        return File(pdf, "application/pdf", "FacultyEvalSystem_Documentation.pdf");
    }
}
