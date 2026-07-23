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
        string[] roles = ["Admin", "CEO", "Dean", "ProgramChair", "Faculty", "Student"];
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

        // Seed evaluation criteria (CHED CMO No. 19 - system config, not user data)
        if (!context.EvaluationCategories.Any())
        {
            var studentCategories = new List<EvaluationCategory>
            {
                new()
                {
                    Name = "Commitment", SortOrder = 1, EvaluatorType = "Student", Weight = 20,
                    Criteria =
                    [
                        new() { Description = "Demonstrates sensitivity to students' ability to attend and participate in class activities", SortOrder = 1 },
                        new() { Description = "Integrates and shares personal and professional experiences in teaching", SortOrder = 2 },
                        new() { Description = "Maintains a professional relationship with students and encourages them to do their best", SortOrder = 3 },
                        new() { Description = "Shows fairness and impartiality in dealing with students", SortOrder = 4 },
                    ]
                },
                new()
                {
                    Name = "Knowledge of Subject", SortOrder = 2, EvaluatorType = "Student", Weight = 20,
                    Criteria =
                    [
                        new() { Description = "Demonstrates mastery of the subject matter", SortOrder = 1 },
                        new() { Description = "Draws and discusses illustrations and examples from actual/current situations", SortOrder = 2 },
                        new() { Description = "Integrates subject matter with other disciplines and real-life situations", SortOrder = 3 },
                        new() { Description = "Keeps abreast of new trends and developments in the field", SortOrder = 4 },
                    ]
                },
                new()
                {
                    Name = "Teaching for Independent Learning", SortOrder = 3, EvaluatorType = "Student", Weight = 20,
                    Criteria =
                    [
                        new() { Description = "Creates situations that encourage students to think critically and creatively", SortOrder = 1 },
                        new() { Description = "Encourages students to express their own ideas and opinions", SortOrder = 2 },
                        new() { Description = "Provides varied learning activities and assessment tasks", SortOrder = 3 },
                        new() { Description = "Gives timely and constructive feedback on student outputs and performances", SortOrder = 4 },
                    ]
                },
                new()
                {
                    Name = "Management of Learning", SortOrder = 4, EvaluatorType = "Student", Weight = 20,
                    Criteria =
                    [
                        new() { Description = "Creates a conducive learning environment", SortOrder = 1 },
                        new() { Description = "Starts and ends classes on time", SortOrder = 2 },
                        new() { Description = "Uses varied teaching strategies and instructional materials", SortOrder = 3 },
                        new() { Description = "Clearly presents the course requirements, grading system, and classroom policies", SortOrder = 4 },
                    ]
                },
                new()
                {
                    Name = "Communication Skills", SortOrder = 5, EvaluatorType = "Student", Weight = 20,
                    Criteria =
                    [
                        new() { Description = "Communicates ideas clearly and effectively", SortOrder = 1 },
                        new() { Description = "Uses appropriate language and communication tools", SortOrder = 2 },
                        new() { Description = "Encourages student participation and interaction", SortOrder = 3 },
                        new() { Description = "Listens to students' concerns and responds appropriately", SortOrder = 4 },
                    ]
                },
            };

            var supervisorCategories = new List<EvaluationCategory>
            {
                new()
                {
                    Name = "Commitment", SortOrder = 1, EvaluatorType = "Supervisor", Weight = 25,
                    Criteria =
                    [
                        new() { Description = "Demonstrates dedication and commitment to the teaching profession", SortOrder = 1 },
                        new() { Description = "Complies with institutional policies and requirements", SortOrder = 2 },
                        new() { Description = "Participates actively in department and institutional activities", SortOrder = 3 },
                        new() { Description = "Maintains professional growth through continuing education and research", SortOrder = 4 },
                    ]
                },
                new()
                {
                    Name = "Knowledge of Subject", SortOrder = 2, EvaluatorType = "Supervisor", Weight = 25,
                    Criteria =
                    [
                        new() { Description = "Demonstrates thorough knowledge and understanding of the subject", SortOrder = 1 },
                        new() { Description = "Aligns course content with program objectives and outcomes", SortOrder = 2 },
                        new() { Description = "Updates instructional materials based on current developments", SortOrder = 3 },
                    ]
                },
                new()
                {
                    Name = "Teaching Effectiveness", SortOrder = 3, EvaluatorType = "Supervisor", Weight = 25,
                    Criteria =
                    [
                        new() { Description = "Employs effective teaching strategies and methodologies", SortOrder = 1 },
                        new() { Description = "Uses appropriate assessment tools to measure student learning", SortOrder = 2 },
                        new() { Description = "Submits required reports and grades on time", SortOrder = 3 },
                    ]
                },
                new()
                {
                    Name = "Community and Professional Service", SortOrder = 4, EvaluatorType = "Supervisor", Weight = 25,
                    Criteria =
                    [
                        new() { Description = "Engages in community extension and outreach activities", SortOrder = 1 },
                        new() { Description = "Contributes to research and professional development", SortOrder = 2 },
                        new() { Description = "Supports and mentors colleagues and students beyond the classroom", SortOrder = 3 },
                    ]
                },
            };

            context.EvaluationCategories.AddRange(studentCategories);
            context.EvaluationCategories.AddRange(supervisorCategories);
            await context.SaveChangesAsync();
        }
    }
}
