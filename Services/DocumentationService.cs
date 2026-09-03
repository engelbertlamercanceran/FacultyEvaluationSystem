using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace FacultyEvalSystem.Services;

public static class DocumentationService
{
    private static readonly string Primary = "#1a5632";
    private static readonly string HeaderBg = "#1a5632";
    private static readonly string LightBg = "#f4f6f9";

    public static byte[] GenerateSystemDocumentation()
    {
        var doc = Document.Create(container =>
        {
            // ===== COVER PAGE =====
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);

                page.Content().Column(col =>
                {
                    col.Item().Height(280).Background(HeaderBg).AlignCenter().AlignMiddle().Column(inner =>
                    {
                        inner.Item().AlignCenter().Text("ISABELA STATE UNIVERSITY").FontColor(Colors.White).Bold().FontSize(22);
                        inner.Item().AlignCenter().Text("Cabagan Campus").FontColor(Colors.White).FontSize(14);
                        inner.Item().PaddingTop(30).AlignCenter().Text("FACULTY EVALUATION SYSTEM").FontColor("#f0b429").Bold().FontSize(28);
                        inner.Item().AlignCenter().Text("Analytics-Driven Academic Quality Assurance").FontColor(Colors.White).FontSize(13);
                    });

                    col.Item().PaddingHorizontal(60).PaddingTop(40).Column(inner =>
                    {
                        inner.Item().AlignCenter().Text("System Documentation").Bold().FontSize(18).FontColor(HeaderBg);
                        inner.Item().PaddingTop(5).AlignCenter().Text("User Manual, Database Schema & Technical Reference").FontSize(11).FontColor(Colors.Grey.Medium);

                        inner.Item().PaddingTop(40).Table(table =>
                        {
                            table.ColumnsDefinition(c => { c.RelativeColumn(); c.RelativeColumn(); });
                            InfoRow(table, "Version", "1.0");
                            InfoRow(table, "Platform", ".NET 10 / ASP.NET Core MVC");
                            InfoRow(table, "Database", "Microsoft SQL Server");
                            InfoRow(table, "Authors", "Lorvin Palattao & Kyla Tarun");
                            InfoRow(table, "Institution", "Isabela State University - Cabagan");
                            InfoRow(table, "Date", DateTime.Now.ToString("MMMM yyyy"));
                        });

                        inner.Item().PaddingTop(50).AlignCenter().Text("COLLEGE OF COMPUTING STUDIES, INFORMATION").FontSize(10).FontColor(Colors.Grey.Medium);
                        inner.Item().AlignCenter().Text("AND COMMUNICATION TECHNOLOGY").FontSize(10).FontColor(Colors.Grey.Medium);
                    });
                });
            });

            // ===== TABLE OF CONTENTS =====
            container.Page(page =>
            {
                SetupPage(page);
                page.Content().Column(col =>
                {
                    SectionTitle(col, "Table of Contents");

                    var toc = new (string Num, string Title)[]
                    {
                        ("1", "System Overview"),
                        ("2", "System Architecture"),
                        ("3", "Database Schema"),
                        ("4", "User Roles & Permissions"),
                        ("5", "Getting Started"),
                        ("6", "Admin Guide"),
                        ("7", "Batch Data Import"),
                        ("8", "Student Evaluation Guide"),
                        ("9", "Supervisor Evaluation Guide"),
                        ("10", "Faculty Dashboard Guide"),
                        ("11", "Reports & PDF Generation"),
                        ("12", "Analytics Dashboard"),
                        ("13", "Test Accounts"),
                    };

                    foreach (var (num, title) in toc)
                    {
                        col.Item().PaddingVertical(4).Row(row =>
                        {
                            row.ConstantItem(30).Text(num).Bold().FontColor(HeaderBg);
                            row.RelativeItem().Text(title).FontSize(11);
                        });
                    }
                });
                AddFooter(page);
            });

            // ===== 1. SYSTEM OVERVIEW =====
            container.Page(page =>
            {
                SetupPage(page);
                page.Content().Column(col =>
                {
                    SectionTitle(col, "1. System Overview");

                    Paragraph(col, "The Faculty Evaluation System is an analytics-driven web-based platform designed for Isabela State University Cabagan (ISUC). It automates the faculty performance evaluation process, replacing the previous semi-automated workflow that relied on manual spreadsheet computations and report preparation.");

                    SubSection(col, "Purpose");
                    Paragraph(col, "The system facilitates data-informed academic quality assurance by enabling online evaluation submission, automated computation of performance ratings, generation of CHED-compliant reports, and analytics dashboards for trend monitoring.");

                    SubSection(col, "Key Features");
                    var features = new[]
                    {
                        "Online Student and Supervisor Evaluation - Students and supervisors (Deans/Program Chairs) can submit evaluations through web-based forms with 5-point Likert scale ratings across multiple categories.",
                        "Automated Computation of Performance Ratings - The system computes SET and SEF ratings separately using the formula (Total Score / 75) x 100, with class-size weighting for SET, per CHED CMO No. 19, Series of 2025.",
                        "CHED-Compliant Report Generation - Generates Individual Faculty Evaluation Reports (IFER, Annex C), Faculty Evaluation and Development Acknowledgement Forms (FEDAF, Annex D), and Faculty Evaluation Summary Reports as downloadable PDFs per CHED CMO No. 19.",
                        "Analytics Dashboards - Interactive charts showing college performance comparisons, semester trends, category breakdowns, and top/low performing faculty.",
                        "Centralized Data Management - All evaluation records are stored in a centralized SQL Server database with historical tracking across semesters.",
                        "Role-Based Access Control - Five distinct user roles (Admin, Dean, Program Chair, Faculty, Student) with appropriate access levels.",
                    };
                    foreach (var f in features)
                        BulletPoint(col, f);

                    SubSection(col, "Evaluation Categories (Student)");
                    Paragraph(col, "Based on CHED CMO No. 19, student evaluations cover five categories, each weighted equally at 20%:");
                    var cats = new[] { "Commitment", "Knowledge of Subject", "Teaching for Independent Learning", "Management of Learning", "Communication Skills" };
                    foreach (var c in cats) BulletPoint(col, c + " (20%)");

                    SubSection(col, "Evaluation Categories (Supervisor)");
                    Paragraph(col, "Supervisor evaluations cover four categories, each weighted at 25%:");
                    var supCats = new[] { "Commitment", "Knowledge of Subject", "Teaching Effectiveness", "Community and Professional Service" };
                    foreach (var c in supCats) BulletPoint(col, c + " (25%)");

                    SubSection(col, "Rating Scale");
                    col.Item().PaddingVertical(5).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.ConstantColumn(80); c.ConstantColumn(80); c.RelativeColumn(); });
                        TableHeader(table, "Range", "Rating", "Description");
                        TableRow(table, "4.50 - 5.00", "5", "Outstanding");
                        TableRow(table, "3.50 - 4.49", "4", "Very Satisfactory");
                        TableRow(table, "2.50 - 3.49", "3", "Satisfactory");
                        TableRow(table, "1.50 - 2.49", "2", "Fair");
                        TableRow(table, "1.00 - 1.49", "1", "Poor");
                    });
                });
                AddFooter(page);
            });

            // ===== 2. SYSTEM ARCHITECTURE =====
            container.Page(page =>
            {
                SetupPage(page);
                page.Content().Column(col =>
                {
                    SectionTitle(col, "2. System Architecture");

                    SubSection(col, "Technology Stack");
                    col.Item().PaddingVertical(5).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(2); });
                        TableHeader(table, "Component", "Technology");
                        TableRow(table, "Backend Framework", "ASP.NET Core MVC (.NET 10)");
                        TableRow(table, "Frontend", "Razor Views + Bootstrap 5");
                        TableRow(table, "Database", "Microsoft SQL Server");
                        TableRow(table, "ORM", "Entity Framework Core 10");
                        TableRow(table, "Authentication", "ASP.NET Core Identity (Cookie-based)");
                        TableRow(table, "PDF Generation", "QuestPDF");
                        TableRow(table, "Charts", "Chart.js (JavaScript)");
                        TableRow(table, "Language", "C# 13");
                    });

                    SubSection(col, "Architecture Pattern");
                    Paragraph(col, "The system follows the Model-View-Controller (MVC) pattern:");
                    BulletPoint(col, "Models - Entity classes mapped to SQL Server tables via Entity Framework Core. Includes ApplicationUser (extends ASP.NET Identity), College, AcademicProgram, Semester, EvaluationPeriod, EvaluationCategory, EvaluationCriterion, FacultySubject, StudentEnrollment, Evaluation, EvaluationResponse, and EvaluationResult.");
                    BulletPoint(col, "Views - Razor (.cshtml) views with Bootstrap 5 for responsive UI. Includes a sidebar layout for authenticated users and a clean login page for guests.");
                    BulletPoint(col, "Controllers - AccountController (auth), AdminController (management), EvaluationController (submission), DashboardController (analytics), ReportsController (PDF generation).");
                    BulletPoint(col, "Services - EvaluationService handles weighted computation logic and category breakdowns.");

                    SubSection(col, "Project Structure");
                    var structure = new[]
                    {
                        "FacultyEvalSystem/",
                        "  Controllers/       - MVC Controllers (Account, Admin, Dashboard, Evaluation, Reports)",
                        "  Data/              - DbContext and Database Seeder",
                        "  Models/            - Entity/Domain Models",
                        "  Services/          - Business Logic (EvaluationService)",
                        "  ViewModels/        - Data transfer objects for Views",
                        "  Views/             - Razor Views organized by Controller",
                        "    Account/         - Login, AccessDenied",
                        "    Admin/           - Users, Colleges, Semesters, Periods, Subjects, Enrollments, Results",
                        "    Dashboard/       - Admin Dashboard, Faculty Dashboard",
                        "    Evaluation/      - Faculty list, Evaluation form",
                        "    Reports/         - Report generation page",
                        "    Shared/          - _Layout.cshtml (main layout)",
                        "  Program.cs         - Application entry point & service configuration",
                        "  appsettings.json   - Connection string & configuration",
                    };
                    foreach (var s in structure)
                    {
                        col.Item().Text(s).FontSize(9).FontFamily("Courier New");
                    }
                });
                AddFooter(page);
            });

            // ===== 3. DATABASE SCHEMA =====
            container.Page(page =>
            {
                SetupPage(page);
                page.Content().Column(col =>
                {
                    SectionTitle(col, "3. Database Schema");
                    Paragraph(col, "The system uses the following database tables. ASP.NET Identity tables (AspNetUsers, AspNetRoles, etc.) are extended with custom fields.");

                    SubSection(col, "AspNetUsers (ApplicationUser)");
                    Paragraph(col, "Extends the default Identity user with faculty/student-specific fields.");
                    col.Item().PaddingVertical(3).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(2); });
                        TableHeader(table, "Column", "Type", "Description");
                        TableRow(table, "Id", "nvarchar(450)", "Primary key (GUID)");
                        TableRow(table, "FirstName", "nvarchar(100)", "User's first name");
                        TableRow(table, "LastName", "nvarchar(100)", "User's last name");
                        TableRow(table, "EmployeeNumber", "nvarchar(20)", "Faculty/staff employee number");
                        TableRow(table, "StudentNumber", "nvarchar(20)", "Student ID number");
                        TableRow(table, "CollegeId", "int (FK)", "References Colleges table");
                        TableRow(table, "ProgramId", "int (FK)", "References AcademicPrograms table");
                        TableRow(table, "IsActive", "bit", "Account active/inactive flag");
                        TableRow(table, "Email, PasswordHash...", "various", "Standard Identity fields");
                    });

                    SubSection(col, "Colleges");
                    col.Item().PaddingVertical(3).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(2); });
                        TableHeader(table, "Column", "Type", "Description");
                        TableRow(table, "Id", "int (PK)", "Auto-increment primary key");
                        TableRow(table, "Name", "nvarchar(200)", "Full college name");
                        TableRow(table, "Code", "nvarchar(20)", "Short code (e.g., CCSICT)");
                    });

                    SubSection(col, "AcademicPrograms");
                    col.Item().PaddingVertical(3).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(2); });
                        TableHeader(table, "Column", "Type", "Description");
                        TableRow(table, "Id", "int (PK)", "Auto-increment primary key");
                        TableRow(table, "Name", "nvarchar(200)", "Full program name");
                        TableRow(table, "Code", "nvarchar(20)", "Short code (e.g., BSIT)");
                        TableRow(table, "CollegeId", "int (FK)", "References Colleges");
                    });

                    SubSection(col, "Semesters");
                    col.Item().PaddingVertical(3).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(2); });
                        TableHeader(table, "Column", "Type", "Description");
                        TableRow(table, "Id", "int (PK)", "Auto-increment primary key");
                        TableRow(table, "AcademicYear", "nvarchar(50)", "e.g., 2025-2026");
                        TableRow(table, "Term", "nvarchar(20)", "1st Semester, 2nd Semester, Summer");
                        TableRow(table, "IsActive", "bit", "Only one semester is active");
                    });
                });
                AddFooter(page);
            });

            // ===== 3b. DATABASE SCHEMA (continued) =====
            container.Page(page =>
            {
                SetupPage(page);
                page.Content().Column(col =>
                {
                    SectionTitle(col, "3. Database Schema (continued)");

                    SubSection(col, "EvaluationPeriods");
                    col.Item().PaddingVertical(3).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(2); });
                        TableHeader(table, "Column", "Type", "Description");
                        TableRow(table, "Id", "int (PK)", "Auto-increment primary key");
                        TableRow(table, "Name", "nvarchar(200)", "e.g., Midterm Evaluation");
                        TableRow(table, "SemesterId", "int (FK)", "References Semesters");
                        TableRow(table, "StartDate", "datetime2", "Evaluation start date");
                        TableRow(table, "EndDate", "datetime2", "Evaluation end date");
                        TableRow(table, "Status", "int", "0=Pending, 1=Open, 2=Closed");
                    });

                    SubSection(col, "EvaluationCategories");
                    col.Item().PaddingVertical(3).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(2); });
                        TableHeader(table, "Column", "Type", "Description");
                        TableRow(table, "Id", "int (PK)", "Auto-increment primary key");
                        TableRow(table, "Name", "nvarchar(200)", "Category name (e.g., Management of Teaching and Learning)");
                        TableRow(table, "SortOrder", "int", "Display order");
                        TableRow(table, "EvaluatorType", "nvarchar(20)", "Student or Supervisor");
                    });

                    SubSection(col, "EvaluationCriteria");
                    col.Item().PaddingVertical(3).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(2); });
                        TableHeader(table, "Column", "Type", "Description");
                        TableRow(table, "Id", "int (PK)", "Auto-increment primary key");
                        TableRow(table, "CategoryId", "int (FK)", "References EvaluationCategories");
                        TableRow(table, "Description", "nvarchar(500)", "The evaluation statement");
                        TableRow(table, "SortOrder", "int", "Display order within category");
                    });

                    SubSection(col, "FacultySubjects");
                    col.Item().PaddingVertical(3).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(2); });
                        TableHeader(table, "Column", "Type", "Description");
                        TableRow(table, "Id", "int (PK)", "Auto-increment primary key");
                        TableRow(table, "FacultyId", "nvarchar(450) FK", "References AspNetUsers");
                        TableRow(table, "SemesterId", "int (FK)", "References Semesters");
                        TableRow(table, "SubjectCode", "nvarchar(20)", "e.g., IT 101");
                        TableRow(table, "SubjectName", "nvarchar(200)", "e.g., Intro to Computing");
                        TableRow(table, "Section", "nvarchar(20)", "e.g., BSIT 1A");
                    });

                    SubSection(col, "StudentEnrollments");
                    col.Item().PaddingVertical(3).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(2); });
                        TableHeader(table, "Column", "Type", "Description");
                        TableRow(table, "Id", "int (PK)", "Auto-increment primary key");
                        TableRow(table, "FacultySubjectId", "int (FK)", "References FacultySubjects");
                        TableRow(table, "StudentId", "nvarchar(450) FK", "References AspNetUsers");
                    });
                });
                AddFooter(page);
            });

            // ===== 3c. DATABASE SCHEMA (continued) =====
            container.Page(page =>
            {
                SetupPage(page);
                page.Content().Column(col =>
                {
                    SectionTitle(col, "3. Database Schema (continued)");

                    SubSection(col, "Evaluations");
                    Paragraph(col, "Each row represents one evaluation submission (one student evaluating one faculty for one subject).");
                    col.Item().PaddingVertical(3).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(2); });
                        TableHeader(table, "Column", "Type", "Description");
                        TableRow(table, "Id", "int (PK)", "Auto-increment primary key");
                        TableRow(table, "EvaluationPeriodId", "int (FK)", "References EvaluationPeriods");
                        TableRow(table, "FacultyId", "nvarchar(450) FK", "The faculty being evaluated");
                        TableRow(table, "EvaluatorId", "nvarchar(450) FK", "The student/supervisor evaluating");
                        TableRow(table, "EvaluatorType", "nvarchar(20)", "Student or Supervisor");
                        TableRow(table, "FacultySubjectId", "int (FK)", "Subject context (nullable for supervisors)");
                        TableRow(table, "SubmittedAt", "datetime2", "Submission timestamp");
                        TableRow(table, "Comments", "nvarchar(1000)", "Optional comments");
                    });
                    Paragraph(col, "Unique constraint: One evaluation per evaluator + faculty + subject + period.");

                    SubSection(col, "EvaluationResponses");
                    Paragraph(col, "Individual ratings per criterion within an evaluation.");
                    col.Item().PaddingVertical(3).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(2); });
                        TableHeader(table, "Column", "Type", "Description");
                        TableRow(table, "Id", "int (PK)", "Auto-increment primary key");
                        TableRow(table, "EvaluationId", "int (FK)", "References Evaluations");
                        TableRow(table, "CriterionId", "int (FK)", "References EvaluationCriteria");
                        TableRow(table, "Rating", "int", "1-5 Likert scale rating");
                    });

                    SubSection(col, "EvaluationResults");
                    Paragraph(col, "Pre-computed summary results per faculty per evaluation period.");
                    col.Item().PaddingVertical(3).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(2); });
                        TableHeader(table, "Column", "Type", "Description");
                        TableRow(table, "Id", "int (PK)", "Auto-increment primary key");
                        TableRow(table, "EvaluationPeriodId", "int (FK)", "References EvaluationPeriods");
                        TableRow(table, "FacultyId", "nvarchar(450) FK", "References AspNetUsers");
                        TableRow(table, "StudentRating", "float", "Average student rating");
                        TableRow(table, "StudentRespondents", "int", "Number of student evaluators");
                        TableRow(table, "SupervisorRating", "float", "Average supervisor rating");
                        TableRow(table, "SupervisorRespondents", "int", "Number of supervisor evaluators");
                        TableRow(table, "StudentDescriptiveRating", "nvarchar(50)", "SET descriptive: Always/Often/Sometimes/Seldom/Never Manifested");
                        TableRow(table, "SupervisorDescriptiveRating", "nvarchar(50)", "SEF descriptive: Always/Often/Sometimes/Seldom/Never Manifested");
                        TableRow(table, "ComputedAt", "datetime2", "When results were last computed");
                    });
                    Paragraph(col, "Unique constraint: One result per faculty per period.");

                    SubSection(col, "Entity Relationship Summary");
                    BulletPoint(col, "College 1:N AcademicProgram");
                    BulletPoint(col, "College 1:N ApplicationUser (faculty/students belong to a college)");
                    BulletPoint(col, "Semester 1:N EvaluationPeriod");
                    BulletPoint(col, "Semester 1:N FacultySubject");
                    BulletPoint(col, "FacultySubject N:1 ApplicationUser (faculty)");
                    BulletPoint(col, "FacultySubject 1:N StudentEnrollment");
                    BulletPoint(col, "StudentEnrollment N:1 ApplicationUser (student)");
                    BulletPoint(col, "EvaluationPeriod 1:N Evaluation");
                    BulletPoint(col, "Evaluation 1:N EvaluationResponse");
                    BulletPoint(col, "EvaluationCategory 1:N EvaluationCriterion");
                    BulletPoint(col, "EvaluationPeriod + Faculty 1:1 EvaluationResult");
                });
                AddFooter(page);
            });

            // ===== 4. USER ROLES =====
            container.Page(page =>
            {
                SetupPage(page);
                page.Content().Column(col =>
                {
                    SectionTitle(col, "4. User Roles & Permissions");

                    col.Item().PaddingVertical(5).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(3); });
                        TableHeader(table, "Role", "Permissions");
                        TableRow(table, "Admin", "Full system access: manage users, colleges, programs, semesters, evaluation periods, subject assignments, student enrollments, compute results, view reports, access dashboard");
                        TableRow(table, "Dean", "View analytics dashboard, evaluate faculty (supervisor evaluation), view evaluation results, generate reports");
                        TableRow(table, "Program Chair", "Same as Dean - evaluate faculty in their college, view results and reports");
                        TableRow(table, "Faculty", "View personal evaluation dashboard with ratings, category breakdown radar chart, and performance history across semesters");
                        TableRow(table, "Student", "View list of assigned faculty to evaluate, submit evaluations during open evaluation periods");
                    });

                    SectionTitle(col, "5. Getting Started");

                    SubSection(col, "Step 1: Login");
                    Paragraph(col, "Navigate to the system URL in your web browser. You will see the login page. Enter your email and password provided by the administrator.");

                    SubSection(col, "Step 2: Navigation");
                    Paragraph(col, "After login, the sidebar on the left shows navigation options based on your role. The top bar displays the current page title and today's date. Success and error messages appear as colored alerts below the top bar.");

                    SubSection(col, "Step 3: Sign Out");
                    Paragraph(col, "Click the 'Sign Out' button at the bottom of the sidebar to log out of the system.");
                });
                AddFooter(page);
            });

            // ===== 6. ADMIN GUIDE =====
            container.Page(page =>
            {
                SetupPage(page);
                page.Content().Column(col =>
                {
                    SectionTitle(col, "6. Admin Guide");

                    SubSection(col, "6.1 Managing Users");
                    Paragraph(col, "Navigate to Users in the sidebar. Here you can view all system users with their roles, colleges, and status. Click '+ Add User' to create a new user account. You can assign roles (Student, Faculty, Dean, ProgramChair, Admin), link them to a college and program, and set their employee/student number. Use the Deactivate/Activate button to toggle user access.");

                    SubSection(col, "6.2 Managing Colleges & Programs");
                    Paragraph(col, "Navigate to Colleges & Programs. Add new colleges with a name and code (e.g., CCSICT). Then add academic programs under each college (e.g., BSIT under CCSICT).");

                    SubSection(col, "6.3 Managing Semesters");
                    Paragraph(col, "Navigate to Semesters. Create new semesters by specifying the academic year (e.g., 2025-2026) and term (1st Semester, 2nd Semester, or Summer). Creating a new semester automatically sets it as the active semester.");

                    SubSection(col, "6.4 Managing Evaluation Periods");
                    Paragraph(col, "Navigate to Evaluation Periods. Create a new period by providing a name (e.g., Midterm Evaluation), selecting a semester, and setting start/end dates. Use the Open/Close buttons to control when evaluations can be submitted. Only open periods accept new evaluations. Click 'Compute' to calculate results after evaluations are submitted.");

                    SubSection(col, "6.5 Subject Assignments");
                    Paragraph(col, "Navigate to Subject Assignments. Select a faculty member, enter the subject code, name, and section, then click 'Assign Subject'. This creates a teaching assignment for the active semester. Click 'Enrollments' next to any subject to manage which students are enrolled.");

                    SubSection(col, "6.6 Student Enrollment");
                    Paragraph(col, "From the Subject Assignments page, click 'Enrollments' for a subject. Select students from the dropdown and click 'Enroll'. Enrolled students will see this faculty-subject pair in their evaluation list when an evaluation period is open.");

                    SubSection(col, "6.7 Computing Results");
                    Paragraph(col, "After evaluations are submitted, go to Evaluation Periods and click 'Compute' for the desired period. The system calculates SET and SEF ratings separately using (Total Score / 75) x 100, with class-size weighting for SET per CMO Section 8.3. Results are viewable in the Evaluation Results page.");
                });
                AddFooter(page);
            });

            // ===== 6b. NEW SEMESTER WORKFLOW =====
            container.Page(page =>
            {
                SetupPage(page);
                page.Content().Column(col =>
                {
                    SectionTitle(col, "6. Admin Guide (continued)");

                    SubSection(col, "6.8 Starting a New Semester / Academic Year");
                    Paragraph(col, "At the start of each new semester or academic year, the admin must set up the system for the new evaluation cycle. Follow these steps in order:");

                    BulletPoint(col, "Step 1: Create a New Semester - Go to Semesters and enter the new academic year (e.g., '2026-2027') and select the term (1st Semester, 2nd Semester, or Summer). Creating a new semester automatically sets it as the active semester. Previous semesters become inactive but their data is preserved.");

                    BulletPoint(col, "Step 2: Assign Subjects to Faculty - Go to Subject Assignments. For each faculty member, assign their subjects for the new semester by entering the subject code (e.g., IT 101), subject name (e.g., Introduction to Computing), and section (e.g., BSIT 1A). Note: Subject assignments are per-semester, so even if a faculty teaches the same subject, it must be reassigned each semester.");

                    BulletPoint(col, "Step 3: Enroll Students - For each subject assignment, click 'Enrollments' and add the students who are enrolled in that class. Only enrolled students will be able to evaluate the corresponding faculty member for that subject.");

                    BulletPoint(col, "Step 4: Create an Evaluation Period - Go to Evaluation Periods and create a new period (e.g., 'Midterm Evaluation' or 'Final Evaluation'). Select the new semester, set the start and end dates, then click 'Create Period'. The period starts in 'Pending' status.");

                    BulletPoint(col, "Step 5: Open the Evaluation Period - When ready to accept evaluations, click the 'Open' button next to the period. Students and supervisors can now login and submit their evaluations. The system will only accept evaluations while the period is in 'Open' status.");

                    BulletPoint(col, "Step 6: Close and Compute - After the evaluation deadline, click 'Close' to stop accepting new submissions. Then click 'Compute' to calculate the weighted results for all faculty. The results will appear in Evaluation Results and the analytics dashboard.");

                    SubSection(col, "6.9 Viewing Historical Data");
                    Paragraph(col, "All past semester data remains accessible in the system:");
                    BulletPoint(col, "Evaluation Results - Use the dropdown to select any past evaluation period and view the full results table for that period.");
                    BulletPoint(col, "IFER PDF Reports - From any past period's results, click 'IFER' to generate the Individual Faculty Evaluation Report (Annex C) for any faculty member.");
                    BulletPoint(col, "FEDAF PDF Reports - Click 'FEDAF' next to any faculty to generate the Faculty Evaluation and Development Acknowledgement Form (Annex D) with evaluation summary and development plan section.");
                    BulletPoint(col, "Summary Reports - Click 'Download Summary PDF' for any past period to get a complete faculty ranking report.");
                    BulletPoint(col, "Analytics Dashboard - The Semester Trend chart automatically includes all historical periods, allowing administrators to track institutional teaching quality over time.");
                    BulletPoint(col, "Faculty Dashboard - Faculty members can see their own performance history across all semesters, including a line chart showing their rating trend over time.");

                    SubSection(col, "6.10 Complete Evaluation Cycle Summary");
                    col.Item().PaddingVertical(5).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.ConstantColumn(30); c.RelativeColumn(1.2f); c.RelativeColumn(2); });
                        TableHeader(table, "#", "Step", "Description");
                        TableRow(table, "1", "Create Semester", "Set up the new academic year and term");
                        TableRow(table, "2", "Assign Subjects", "Link faculty to their subjects and sections");
                        TableRow(table, "3", "Enroll Students", "Add students to each faculty-subject class");
                        TableRow(table, "4", "Create Period", "Define the evaluation period with dates");
                        TableRow(table, "5", "Open Period", "Allow students and supervisors to submit evaluations");
                        TableRow(table, "6", "Monitor", "Track submission progress on the dashboard");
                        TableRow(table, "7", "Close Period", "Stop accepting new evaluation submissions");
                        TableRow(table, "8", "Compute Results", "Calculate weighted ratings for all faculty");
                        TableRow(table, "9", "Generate Reports", "Download IFER, FEDAF, and Summary PDFs as needed");
                    });
                });
                AddFooter(page);
            });

            // ===== 7. BATCH DATA IMPORT =====
            container.Page(page =>
            {
                SetupPage(page);
                page.Content().Column(col =>
                {
                    SectionTitle(col, "7. Batch Data Import");

                    Paragraph(col, "The system supports batch importing of data via CSV (Comma-Separated Values) files. This feature allows administrators to efficiently load large volumes of data instead of creating records one by one. CSV files can be created from Excel by using 'Save As → CSV (Comma delimited)'.");

                    SubSection(col, "Import Order");
                    Paragraph(col, "Data must be imported in the correct order due to dependencies between records:");
                    BulletPoint(col, "Step 1: Colleges & Programs — Creates the institutional structure (colleges and their academic programs).");
                    BulletPoint(col, "Step 2: Users — Creates user accounts for Deans, Program Chairs, Faculty, and Students. Each user is assigned to a college and program via college/program codes.");
                    BulletPoint(col, "Step 3: Subject Assignments — Assigns subjects to faculty for the active semester. A semester must be created and set as active before importing.");
                    BulletPoint(col, "Step 4: Student Enrollments — Links students to specific subject-section combinations. Subjects must be imported first.");

                    SubSection(col, "CSV File Formats");

                    Paragraph(col, "Colleges & Programs CSV:");
                    BulletPoint(col, "Columns: CollegeName, CollegeCode, ProgramName, ProgramCode");
                    BulletPoint(col, "Program columns are optional — leave blank for colleges with no programs to add.");

                    Paragraph(col, "Users CSV:");
                    BulletPoint(col, "Columns: Email, FirstName, LastName, Role, Password, EmployeeNumber, StudentNumber, CollegeCode, ProgramCode");
                    BulletPoint(col, "Valid roles: Dean, ProgramChair, Faculty, Student. Leave EmployeeNumber or StudentNumber blank if not applicable.");

                    Paragraph(col, "Subjects CSV:");
                    BulletPoint(col, "Columns: FacultyEmail, SubjectCode, SubjectName, Section");
                    BulletPoint(col, "Subjects are assigned to the currently active semester.");

                    Paragraph(col, "Enrollments CSV:");
                    BulletPoint(col, "Columns: StudentEmail, SubjectCode, Section");
                    BulletPoint(col, "Matches students to subjects by code and section in the active semester.");

                    SubSection(col, "Quick Demo Setup");
                    Paragraph(col, "For demonstration purposes, a 'Quick Demo Setup' button is available on the Batch Import page. Clicking this single button automatically:");
                    BulletPoint(col, "Creates 5 colleges and 4 academic programs");
                    BulletPoint(col, "Creates 32 user accounts (1 dean, 1 program chair, 10 faculty, 20 students)");
                    BulletPoint(col, "Creates 3 semesters (2024-2025 1st & 2nd, 2025-2026 1st)");
                    BulletPoint(col, "Assigns 14 subjects to faculty and enrolls 55 students");
                    BulletPoint(col, "Creates 3 evaluation periods and generates sample evaluations with varied ratings");
                    BulletPoint(col, "Computes all results — dashboard, charts, and reports are immediately available");

                    SubSection(col, "Important Notes");
                    BulletPoint(col, "Duplicate records are automatically skipped — re-importing the same file will not create duplicates.");
                    BulletPoint(col, "Sample CSV templates are available for download on the Batch Import page.");
                    BulletPoint(col, "The first row of each CSV file must be the header row with column names.");
                    BulletPoint(col, "Data imported via CSV can also be managed individually through the Admin management pages.");
                });
                AddFooter(page);
            });

            // ===== 8-10. USER GUIDES =====
            container.Page(page =>
            {
                SetupPage(page);
                page.Content().Column(col =>
                {
                    SectionTitle(col, "8. Student Evaluation Guide");

                    Paragraph(col, "When a student logs in, they see the 'Evaluate Faculty' page listing all faculty members they are enrolled with for the active semester.");

                    SubSection(col, "Submitting an Evaluation");
                    BulletPoint(col, "Step 1: Check that an evaluation period is currently open (shown in the blue info bar).");
                    BulletPoint(col, "Step 2: Find the faculty member you want to evaluate. Faculty with 'Pending' status have not been evaluated yet.");
                    BulletPoint(col, "Step 3: Click the 'Evaluate' button to open the evaluation form.");
                    BulletPoint(col, "Step 4: Rate each criterion on a scale of 1 (Never/Rarely Manifested) to 5 (Always Manifested). All ratings are required.");
                    BulletPoint(col, "Step 5: Optionally add comments in the text area at the bottom.");
                    BulletPoint(col, "Step 6: Click 'Submit Evaluation'. A confirmation dialog will appear. Once submitted, the evaluation cannot be changed.");
                    BulletPoint(col, "Step 7: The faculty member's status changes to 'Completed' in your list.");
                    Paragraph(col, "Note: Results are automatically re-computed after each submission. The dashboard and reports reflect the latest data in real time.");
                    Paragraph(col, "Note: Each student can only evaluate a faculty member once per subject per evaluation period. The evaluation is anonymous - faculty members cannot see individual student responses.");

                    SectionTitle(col, "9. Supervisor Evaluation Guide");
                    Paragraph(col, "Deans and Program Chairs can evaluate faculty members in their college as supervisors.");
                    BulletPoint(col, "Navigate to 'Evaluate Faculty' in the sidebar.");
                    BulletPoint(col, "The system shows all active faculty in your college.");
                    BulletPoint(col, "Click 'Evaluate' and fill out the supervisor evaluation form (4 categories, 25% each).");
                    BulletPoint(col, "Submit the evaluation. It contributes to the 40% supervisor weight in the overall rating.");

                    SectionTitle(col, "10. Faculty Dashboard Guide");
                    Paragraph(col, "Faculty members see their personal evaluation dashboard upon login.");
                    SubSection(col, "Dashboard Components");
                    BulletPoint(col, "Overall Rating Card - Shows the latest overall rating and descriptive rating (e.g., 4.52 Outstanding).");
                    BulletPoint(col, "Student Rating Card - Average student evaluation score with respondent count.");
                    BulletPoint(col, "Supervisor Rating Card - Average supervisor evaluation score with respondent count.");
                    BulletPoint(col, "Category Breakdown (Radar Chart) - Visual breakdown of student ratings across all 5 evaluation categories. Helps identify strengths and areas for improvement.");
                    BulletPoint(col, "Performance History (Line Chart) - Shows overall rating trends across semesters.");
                    BulletPoint(col, "Evaluation History Table - Detailed semester-by-semester breakdown with student, supervisor, and overall ratings.");
                });
                AddFooter(page);
            });

            // ===== 10-11. REPORTS & ANALYTICS =====
            container.Page(page =>
            {
                SetupPage(page);
                page.Content().Column(col =>
                {
                    SectionTitle(col, "11. Reports & PDF Generation");

                    SubSection(col, "Individual Faculty Evaluation Report (IFER)");
                    Paragraph(col, "The IFER is a CHED-compliant PDF report for individual faculty members. It includes:");
                    BulletPoint(col, "Faculty name, college, semester, and computation date");
                    BulletPoint(col, "Student evaluation breakdown by category with ratings");
                    BulletPoint(col, "Supervisor evaluation breakdown by category with ratings");
                    BulletPoint(col, "Overall weighted rating and descriptive rating");
                    BulletPoint(col, "Signature lines for QA Officer and Dean/Program Chair");
                    Paragraph(col, "To generate: Go to Evaluation Results, select a period, and click 'IFER PDF' next to any faculty member.");

                    SubSection(col, "Faculty Evaluation and Development Acknowledgement Form (FEDAF)");
                    Paragraph(col, "The FEDAF is a CHED-compliant form per CMO No. 19 Annex D. It is a per-faculty acknowledgement form used during the feedback and follow-up process. It includes:");
                    BulletPoint(col, "Section A: Faculty Member Information — name, department/college, faculty rank, semester");
                    BulletPoint(col, "Section B: Faculty Evaluation Summary — SET (Student) Rating and SEF (Supervisor) Rating displayed side by side");
                    BulletPoint(col, "Section C: Development Plan — blank sections for Areas for Improvement, Proposed Learning and Development Activities, and Action Plan (to be jointly filled by supervisor and faculty)");
                    BulletPoint(col, "Acknowledgement statement and signature lines for both Supervisor and Faculty");
                    Paragraph(col, "To generate: Go to Evaluation Results, select a period, and click 'FEDAF' next to any faculty member. The form is intended to be printed, filled out during the one-on-one feedback meeting between supervisor and faculty, then signed by both parties.");

                    SubSection(col, "Faculty Evaluation Summary Report");
                    Paragraph(col, "A landscape PDF listing all faculty in a given evaluation period with their ratings ranked from highest to lowest. Includes student rating, respondent count, supervisor rating, overall rating, and descriptive rating.");
                    Paragraph(col, "To generate: Go to Evaluation Results and click 'Download Summary PDF', or go to Reports and select a period.");

                    SectionTitle(col, "12. Analytics Dashboard");
                    Paragraph(col, "The Admin/Dean/Program Chair dashboard provides real-time analytics:");

                    SubSection(col, "Summary Cards");
                    Paragraph(col, "Four cards at the top show: Total Faculty, Total Students, Total Colleges, and Total Evaluations submitted.");

                    SubSection(col, "Active Semester Card");
                    Paragraph(col, "Shows the current active semester and whether an evaluation period is open.");

                    SubSection(col, "College Performance Comparison (Bar Chart)");
                    Paragraph(col, "Compares average faculty ratings across all colleges. Helps identify which colleges have higher or lower teaching quality metrics.");

                    SubSection(col, "Top 5 Performers Table");
                    Paragraph(col, "Lists the 5 highest-rated faculty members for the latest evaluation period with their overall rating and descriptive rating.");

                    SubSection(col, "Faculty Needing Support Table");
                    Paragraph(col, "Lists faculty members with ratings below 3.50 (Satisfactory or lower), enabling targeted faculty development planning. If all faculty are rated Very Satisfactory or above, this table will be empty.");

                    SubSection(col, "Semester Trend (Line Chart)");
                    Paragraph(col, "Shows the average overall rating across all faculty over multiple semesters. Helps track institutional teaching quality trends over time.");
                });
                AddFooter(page);
            });

            // ===== 12. TEST ACCOUNTS =====
            container.Page(page =>
            {
                SetupPage(page);
                page.Content().Column(col =>
                {
                    SectionTitle(col, "13. Test Accounts & Sample Data");

                    Paragraph(col, "After running Quick Demo Setup (or importing the sample CSV files), the following accounts are available. All test account passwords are Pass@123 unless noted otherwise.");

                    SubSection(col, "Admin Account");
                    col.Item().PaddingVertical(3).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(1); c.RelativeColumn(2); });
                        TableRow(table, "Email", "admin@isuc.edu.ph");
                        TableRow(table, "Password", "Admin@123");
                        TableRow(table, "Role", "System Administrator");
                    });

                    SubSection(col, "Dean & Program Chair");
                    col.Item().PaddingVertical(3).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(2); c.RelativeColumn(1.5f); c.RelativeColumn(1); });
                        TableHeader(table, "Email", "Name", "Role");
                        TableRow(table, "dean.garcia@isuc.edu.ph", "Maria Garcia", "Dean (CCSICT)");
                        TableRow(table, "chair.reyes@isuc.edu.ph", "Roberto Reyes", "ProgramChair (BSIT)");
                    });

                    SubSection(col, "Faculty Members (10)");
                    col.Item().PaddingVertical(3).Table(table =>
                    {
                        table.ColumnsDefinition(c => { c.RelativeColumn(2.5f); c.RelativeColumn(1.5f); c.RelativeColumn(1); c.RelativeColumn(1.2f); });
                        TableHeader(table, "Email", "Name", "College", "Profile");
                        TableRow(table, "jose.santos@isuc.edu.ph", "Jose Santos", "CCSICT", "Outstanding");
                        TableRow(table, "anna.cruz@isuc.edu.ph", "Anna Marie Cruz", "CCSICT", "Very Good");
                        TableRow(table, "mark.bautista@isuc.edu.ph", "Mark A. Bautista", "CCSICT", "Good");
                        TableRow(table, "elena.ramos@isuc.edu.ph", "Elena Ramos", "CCSICT", "Outstanding");
                        TableRow(table, "pedro.villanueva@isuc.edu.ph", "Pedro Villanueva", "CCSICT", "Needs Improvement");
                        TableRow(table, "rosa.mendoza@isuc.edu.ph", "Rosa Mendoza", "CAS", "Good");
                        TableRow(table, "carlos.aquino@isuc.edu.ph", "Carlos Aquino", "CAS", "Satisfactory");
                        TableRow(table, "lucia.pascual@isuc.edu.ph", "Lucia Pascual", "CED", "Outstanding");
                        TableRow(table, "ramon.delacruz@isuc.edu.ph", "Ramon Dela Cruz", "CED", "Good");
                        TableRow(table, "grace.tolentino@isuc.edu.ph", "Grace Tolentino", "CED", "Mixed");
                    });

                    SubSection(col, "Sample Students (20)");
                    Paragraph(col, "20 students are enrolled across various subjects. Example: juan.delacruz@isuc.edu.ph (Juan Dela Cruz), mariaclara.reyes@isuc.edu.ph (Maria Clara Reyes), etc. All use password Pass@123.");

                    SubSection(col, "Demo Data Summary (via Quick Demo Setup)");
                    BulletPoint(col, "5 Colleges: CCSICT, CAS, CED, COE, CA");
                    BulletPoint(col, "4 Programs: BSIT, BSCS, BAE, BEEd");
                    BulletPoint(col, "32 Users: 1 Admin, 1 Dean, 1 Program Chair, 10 Faculty, 20 Students");
                    BulletPoint(col, "3 Semesters: 2024-2025 (1st & 2nd), 2025-2026 (1st - Active)");
                    BulletPoint(col, "3 Evaluation Periods: 2 closed (past), 1 open (current)");
                    BulletPoint(col, "14 Subject assignments with 55 student enrollments");
                    BulletPoint(col, "200+ evaluation submissions with varied ratings across semesters");
                    BulletPoint(col, "30 computed faculty results (10 faculty x 3 periods) for trend analysis");
                    Paragraph(col, "All demo data is created via the 'Quick Demo Setup' button on the Batch Import page, or by manually uploading the sample CSV files.");
                });
                AddFooter(page);
            });
        });

        return doc.GeneratePdf();
    }

    private static void SetupPage(PageDescriptor page)
    {
        page.Size(PageSizes.A4);
        page.MarginHorizontal(40);
        page.MarginVertical(35);
        page.DefaultTextStyle(x => x.FontSize(10));
    }

    private static void AddFooter(PageDescriptor page)
    {
        page.Footer().Row(row =>
        {
            row.RelativeItem().Text("Faculty Evaluation System - Documentation").FontSize(8).FontColor(Colors.Grey.Medium);
            row.ConstantItem(100).AlignRight().Text(t =>
            {
                t.Span("Page ").FontSize(8).FontColor(Colors.Grey.Medium);
                t.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
            });
        });
    }

    private static void SectionTitle(ColumnDescriptor col, string title)
    {
        col.Item().PaddingBottom(10).PaddingTop(5).BorderBottom(2).BorderColor(HeaderBg)
            .Text(title).Bold().FontSize(16).FontColor(HeaderBg);
    }

    private static void SubSection(ColumnDescriptor col, string title)
    {
        col.Item().PaddingTop(10).PaddingBottom(3)
            .Text(title).Bold().FontSize(12).FontColor("#333");
    }

    private static void Paragraph(ColumnDescriptor col, string text)
    {
        col.Item().PaddingVertical(3).Text(text).FontSize(10).LineHeight(1.5f);
    }

    private static void BulletPoint(ColumnDescriptor col, string text)
    {
        col.Item().PaddingLeft(15).PaddingVertical(2).Row(row =>
        {
            row.ConstantItem(12).Text("\u2022").FontSize(10);
            row.RelativeItem().Text(text).FontSize(10).LineHeight(1.4f);
        });
    }

    private static void InfoRow(TableDescriptor table, string label, string value)
    {
        table.Cell().Padding(5).Text(label).Bold().FontSize(10).FontColor(Colors.Grey.Darken1);
        table.Cell().Padding(5).Text(value).FontSize(10);
    }

    private static void TableHeader(TableDescriptor table, params string[] headers)
    {
        foreach (var h in headers)
            table.Cell().Background(HeaderBg).Padding(5).Text(h).Bold().FontSize(9).FontColor(Colors.White);
    }

    private static void TableRow(TableDescriptor table, params string[] cells)
    {
        foreach (var c in cells)
            table.Cell().BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(c).FontSize(9);
    }
}
