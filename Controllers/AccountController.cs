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
            var roles = await _userManager.GetRolesAsync(loggedInUser!);

            return roles.FirstOrDefault() switch
            {
                "Admin" or "Dean" or "ProgramChair" => RedirectToAction("Index", "Dashboard"),
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
                "Admin" or "Dean" or "ProgramChair" => RedirectToAction("Index", "Dashboard"),
                "Faculty" => RedirectToAction("Faculty", "Dashboard"),
                "Student" => RedirectToAction("Index", "Evaluation"),
                _ => RedirectToAction("Index", "Home")
            };
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError("", error.Description);

        return View(model);
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
