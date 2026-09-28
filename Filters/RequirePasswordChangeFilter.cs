using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using FacultyEvalSystem.Models;

namespace FacultyEvalSystem.Filters;

/// <summary>
/// Keeps a signed-in user on the Change Password page until they have replaced
/// the default password their imported account was created with.
/// </summary>
public class RequirePasswordChangeFilter : IAsyncActionFilter
{
    private readonly UserManager<ApplicationUser> _userManager;

    public RequirePasswordChangeFilter(UserManager<ApplicationUser> userManager) => _userManager = userManager;

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var principal = context.HttpContext.User;
        var controller = context.RouteData.Values["controller"] as string;

        if (principal.Identity?.IsAuthenticated == true && controller != "Account")
        {
            var user = await _userManager.GetUserAsync(principal);
            if (user?.MustChangePassword == true)
            {
                context.Result = new RedirectToActionResult("ChangePassword", "Account", null);
                return;
            }
        }

        await next();
    }
}
