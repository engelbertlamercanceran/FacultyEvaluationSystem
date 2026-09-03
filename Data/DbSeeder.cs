using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using FacultyEvalSystem.Models;

namespace FacultyEvalSystem.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();

        await context.Database.MigrateAsync();

        // Seed roles
        string[] roles = ["Admin", "CEO", "Dean", "ProgramChair", "Faculty", "Student", "AA", "AAStaff", "QA"];
        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
                await roleManager.CreateAsync(new IdentityRole(role));
        }

        // Seed admin user
        if (await userManager.FindByEmailAsync("admin@isuc.edu.ph") is null)
        {
            var admin = new ApplicationUser
            {
                UserName = "admin@isuc.edu.ph",
                Email = "admin@isuc.edu.ph",
                FirstName = "System",
                LastName = "Administrator",
                EmailConfirmed = true
            };
            await userManager.CreateAsync(admin, "Admin@123");
            await userManager.AddToRoleAsync(admin, "Admin");
        }

        // Seed AA test user
        if (await userManager.FindByEmailAsync("aa@isuc.edu.ph") is null)
        {
            var aa = new ApplicationUser
            {
                UserName = "aa@isuc.edu.ph",
                Email = "aa@isuc.edu.ph",
                FirstName = "Academic",
                LastName = "Affairs",
                EmailConfirmed = true
            };
            await userManager.CreateAsync(aa, "Aa@12345");
            await userManager.AddToRoleAsync(aa, "AA");
        }

        // Seed AA Staff test user
        if (await userManager.FindByEmailAsync("aastaff@isuc.edu.ph") is null)
        {
            var aaStaff = new ApplicationUser
            {
                UserName = "aastaff@isuc.edu.ph",
                Email = "aastaff@isuc.edu.ph",
                FirstName = "AA",
                LastName = "Staff",
                EmailConfirmed = true
            };
            await userManager.CreateAsync(aaStaff, "Aastaff@123");
            await userManager.AddToRoleAsync(aaStaff, "AAStaff");
        }

        // Seed QA test user
        if (await userManager.FindByEmailAsync("qa@isuc.edu.ph") is null)
        {
            var qa = new ApplicationUser
            {
                UserName = "qa@isuc.edu.ph",
                Email = "qa@isuc.edu.ph",
                FirstName = "Quality",
                LastName = "Assurance",
                EmailConfirmed = true
            };
            await userManager.CreateAsync(qa, "Qa@12345");
            await userManager.AddToRoleAsync(qa, "QA");
        }

        // Seed evaluation criteria per CHED CMO No. 19, Series of 2025
        // Re-seed if old NBC 461 categories detected or table is empty
        bool needsReseed = !context.EvaluationCategories.Any()
            || await context.EvaluationCategories.AnyAsync(c => c.Name == "Communication Skills")
            || await context.EvaluationCategories.AnyAsync(c => c.Name == "Community and Professional Service");

        if (needsReseed)
        {
            // Clear old categories (cascades to criteria via FK)
            context.EvaluationCategories.RemoveRange(context.EvaluationCategories);
            await context.SaveChangesAsync();

            // ANNEX A: Student Evaluation of Teachers (SET) — 3 categories, 15 items
            var studentCategories = new List<EvaluationCategory>
            {
                new()
                {
                    Name = "Management of Teaching and Learning", SortOrder = 1, EvaluatorType = "Student",
                    Criteria =
                    [
                        new() { Description = "Comes to class on time.", SortOrder = 1 },
                        new() { Description = "Explains learning outcomes, expectations, grading system, and various requirements of the subject/course.", SortOrder = 2 },
                        new() { Description = "Maximizes the allocated time/learning hours effectively.", SortOrder = 3 },
                        new() { Description = "Facilitates students to think critically and creatively by providing appropriate learning activities.", SortOrder = 4 },
                        new() { Description = "Guides students to learn on their own, reflect on new ideas and experiences, and make decisions in accomplishing given tasks.", SortOrder = 5 },
                        new() { Description = "Communicates constructive feedback to students for their academic growth.", SortOrder = 6 },
                    ]
                },
                new()
                {
                    Name = "Content Knowledge, Pedagogy and Technology", SortOrder = 2, EvaluatorType = "Student",
                    Criteria =
                    [
                        new() { Description = "Demonstrates extensive and broad knowledge of the subject/course.", SortOrder = 7 },
                        new() { Description = "Simplifies complex ideas in the lesson for ease of understanding.", SortOrder = 8 },
                        new() { Description = "Relates the subject matter to contemporary issues and developments in the discipline and/or daily life activities.", SortOrder = 9 },
                        new() { Description = "Promotes active learning and student engagement by using appropriate teaching and learning resources including ICT tools and platforms.", SortOrder = 10 },
                        new() { Description = "Uses appropriate assessments (projects, exams, quizzes, assignments, etc.) aligned with the learning outcomes.", SortOrder = 11 },
                    ]
                },
                new()
                {
                    Name = "Commitment and Transparency", SortOrder = 3, EvaluatorType = "Student",
                    Criteria =
                    [
                        new() { Description = "Recognizes and values the unique diversity and individual differences among students.", SortOrder = 12 },
                        new() { Description = "Assists students with their learning challenges during consultation hours.", SortOrder = 13 },
                        new() { Description = "Provides immediate feedback on student outputs and performance.", SortOrder = 14 },
                        new() { Description = "Provides transparent and clear criteria in rating student's performance.", SortOrder = 15 },
                    ]
                },
            };

            // ANNEX B: Supervisor's Evaluation of Faculty (SEF) — 3 categories, 15 items (same benchmark statements)
            var supervisorCategories = new List<EvaluationCategory>
            {
                new()
                {
                    Name = "Management of Teaching and Learning", SortOrder = 1, EvaluatorType = "Supervisor",
                    Criteria =
                    [
                        new() { Description = "Comes to class on time.", SortOrder = 1 },
                        new() { Description = "Submits updated syllabus, grade sheets, and other required reports on time.", SortOrder = 2 },
                        new() { Description = "Maximizes the allocated time/learning hours effectively.", SortOrder = 3 },
                        new() { Description = "Provides appropriate learning activities that facilitate critical thinking and creativity of students.", SortOrder = 4 },
                        new() { Description = "Guides students to learn on their own, reflect on new ideas and experiences, and make decisions in accomplishing given tasks.", SortOrder = 5 },
                        new() { Description = "Communicates constructive feedback to students for their academic growth.", SortOrder = 6 },
                    ]
                },
                new()
                {
                    Name = "Content Knowledge, Pedagogy and Technology", SortOrder = 2, EvaluatorType = "Supervisor",
                    Criteria =
                    [
                        new() { Description = "Demonstrates extensive and broad knowledge of the subject/course.", SortOrder = 7 },
                        new() { Description = "Simplifies complex ideas in the lesson for ease of understanding.", SortOrder = 8 },
                        new() { Description = "Integrates contemporary issues and developments in the discipline and/or daily life activities in the syllabus.", SortOrder = 9 },
                        new() { Description = "Promotes active learning and student engagement by using appropriate teaching and learning resources including ICT tools and platforms.", SortOrder = 10 },
                        new() { Description = "Uses appropriate assessments (projects, exams, quizzes, assignments, etc.) aligned with the learning outcomes.", SortOrder = 11 },
                    ]
                },
                new()
                {
                    Name = "Commitment and Transparency", SortOrder = 3, EvaluatorType = "Supervisor",
                    Criteria =
                    [
                        new() { Description = "Recognizes and values the unique diversity and individual differences among students.", SortOrder = 12 },
                        new() { Description = "Assists students with their learning challenges during consultation hours.", SortOrder = 13 },
                        new() { Description = "Provides immediate feedback on student outputs and performance.", SortOrder = 14 },
                        new() { Description = "Provides transparent and clear criteria in rating student's performance.", SortOrder = 15 },
                    ]
                },
            };

            context.EvaluationCategories.AddRange(studentCategories);
            context.EvaluationCategories.AddRange(supervisorCategories);
            await context.SaveChangesAsync();
        }
    }
}
