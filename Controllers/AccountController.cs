using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using FacultyEvalSystem.Data;
using FacultyEvalSystem.Models;
using FacultyEvalSystem.ViewModels;

namespace FacultyEvalSystem.Controllers;

public class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ApplicationDbContext _db;

    public AccountController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, ApplicationDbContext db)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
    }

    [HttpGet]
    public IActionResult Login() => View(new LoginViewModel());

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Email))
        {
            ModelState.AddModelError("", "Email is required.");
            return View(model);
        }

        // Step 1: Email only — check if account exists
        if (!model.ShowPassword)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user is null)
            {
                ModelState.AddModelError("", "No account found with that email.");
                return View(model);
            }

            // No password set — go to set password page
            if (!await _userManager.HasPasswordAsync(user))
            {
                return RedirectToAction("SetPassword", new { email = model.Email });
            }

            // Has password — show password field
            model.ShowPassword = true;
            ModelState.Clear();
            return View(model);
        }

        // Step 2: Sign in with password
        if (string.IsNullOrWhiteSpace(model.Password))
        {
            ModelState.AddModelError("", "Password is required.");
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(model.Email, model.Password, model.RememberMe, false);
        if (result.Succeeded)
        {
            var loggedInUser = await _userManager.FindByEmailAsync(model.Email);

            // Imported accounts start on the default password: change it first
            if (loggedInUser!.MustChangePassword)
                return RedirectToAction(nameof(ChangePassword));

            var roles = await _userManager.GetRolesAsync(loggedInUser!);

            return roles.FirstOrDefault() switch
            {
                "Admin" or "CEO" or "Dean" or "ProgramChair" or "AA" or "AAStaff" or "QA" => RedirectToAction("Index", "Dashboard"),
                "Faculty" => RedirectToAction("Faculty", "Dashboard"),
                "Student" => RedirectToAction("Index", "Evaluation"),
                _ => RedirectToAction("Index", "Home")
            };
        }

        ModelState.AddModelError("", "Invalid password.");
        return View(model);
    }

    [HttpGet]
    public IActionResult SetPassword(string email)
    {
        return View(new SetPasswordViewModel { Email = email });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetPassword(SetPasswordViewModel model)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user is null)
        {
            ModelState.AddModelError("", "Account not found.");
            return View(model);
        }

        if (await _userManager.HasPasswordAsync(user))
        {
            ModelState.AddModelError("", "Password has already been set. Please use the login page.");
            return View(model);
        }

        var result = await _userManager.AddPasswordAsync(user, model.Password);
        if (result.Succeeded)
        {
            await _signInManager.SignInAsync(user, isPersistent: false);
            var roles = await _userManager.GetRolesAsync(user);

            TempData["Success"] = "Password set successfully. Welcome!";
            return roles.FirstOrDefault() switch
            {
                "Admin" or "CEO" or "Dean" or "ProgramChair" or "AA" or "AAStaff" or "QA" => RedirectToAction("Index", "Dashboard"),
                "Faculty" => RedirectToAction("Faculty", "Dashboard"),
                "Student" => RedirectToAction("Index", "Evaluation"),
                _ => RedirectToAction("Index", "Home")
            };
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError("", error.Description);

        return View(model);
    }

    /// <summary>
    /// First login on an imported account: the default password must be replaced
    /// before the user can go anywhere else.
    /// </summary>
    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpGet]
    public IActionResult ChangePassword() => View(new ChangePasswordViewModel());

    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model,
        [FromServices] FacultyEvalSystem.Services.DataImportService import)
    {
        if (!ModelState.IsValid) return View(model);

        var user = await _userManager.GetUserAsync(User);
        if (user is null) return RedirectToAction(nameof(Login));

        if (model.Password == import.DefaultPassword)
        {
            ModelState.AddModelError("", "Please choose a password different from the default password.");
            return View(model);
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, token, model.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
                ModelState.AddModelError("", error.Description);
            return View(model);
        }

        user.MustChangePassword = false;
        await _userManager.UpdateAsync(user);
        await _signInManager.RefreshSignInAsync(user);

        var roles = await _userManager.GetRolesAsync(user);
        TempData["Success"] = "Your password has been changed. Welcome!";
        return roles.FirstOrDefault() switch
        {
            "Admin" or "CEO" or "Dean" or "ProgramChair" or "AA" or "AAStaff" or "QA" => RedirectToAction("Index", "Dashboard"),
            "Faculty" => RedirectToAction("Faculty", "Dashboard"),
            "Student" => RedirectToAction("Index", "Evaluation"),
            _ => RedirectToAction("Index", "Home")
        };
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction("Login");
    }

    public IActionResult AccessDenied() => View();
}
