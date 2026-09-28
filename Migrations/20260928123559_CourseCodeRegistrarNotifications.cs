using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FacultyEvalSystem.Migrations
{
    /// <inheritdoc />
    public partial class CourseCodeRegistrarNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FacultySubjects_SemesterId",
                table: "FacultySubjects");

            // "Subject Code" becomes "Course Code": rename, keeping existing data
            migrationBuilder.RenameColumn(
                name: "SubjectCode",
                table: "FacultySubjects",
                newName: "CourseCode");

            migrationBuilder.RenameColumn(
                name: "SubjectName",
                table: "FacultySubjects",
                newName: "CourseTitle");

            migrationBuilder.AlterColumn<string>(
                name: "CourseCode",
                table: "FacultySubjects",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(20)",
                oldMaxLength: 20);

            migrationBuilder.AlterColumn<string>(
                name: "CourseTitle",
                table: "FacultySubjects",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);

            migrationBuilder.AddColumn<string>(
                name: "CourseKey",
                table: "FacultySubjects",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            // Matching key for existing rows (same normalisation as CourseCodes.Key)
            migrationBuilder.Sql(
                "UPDATE FacultySubjects SET CourseKey = UPPER(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(CourseCode, ' ', ''), '.', ''), '-', ''), '_', ''), '*', ''))");

            migrationBuilder.AddColumn<string>(
                name: "CreatedById",
                table: "DevelopmentPlans",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CreatedByRole",
                table: "DevelopmentPlans",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FacultyEditedAt",
                table: "DevelopmentPlans",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FinalizedAt",
                table: "DevelopmentPlans",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FinalizedById",
                table: "DevelopmentPlans",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProgramChairId",
                table: "DevelopmentPlans",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MiddleName",
                table: "AspNetUsers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MustChangePassword",
                table: "AspNetUsers",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Suffix",
                table: "AspNetUsers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    Link = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Notifications_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StudentCourses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    StudentId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SemesterId = table.Column<int>(type: "int", nullable: false),
                    CourseCode = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CourseKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Units = table.Column<decimal>(type: "decimal(5,2)", precision: 5, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StudentCourses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StudentCourses_AspNetUsers_StudentId",
                        column: x => x.StudentId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StudentCourses_Semesters_SemesterId",
                        column: x => x.SemesterId,
                        principalTable: "Semesters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FacultySubjects_SemesterId_CourseKey",
                table: "FacultySubjects",
                columns: new[] { "SemesterId", "CourseKey" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_ReadAt",
                table: "Notifications",
                columns: new[] { "UserId", "ReadAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentCourses_SemesterId_CourseKey",
                table: "StudentCourses",
                columns: new[] { "SemesterId", "CourseKey" });

            migrationBuilder.CreateIndex(
                name: "IX_StudentCourses_StudentId_SemesterId_CourseKey",
                table: "StudentCourses",
                columns: new[] { "StudentId", "SemesterId", "CourseKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "StudentCourses");

            migrationBuilder.DropIndex(
                name: "IX_FacultySubjects_SemesterId_CourseKey",
                table: "FacultySubjects");

                        migrationBuilder.DropColumn(
                name: "CourseKey",
                table: "FacultySubjects");

            migrationBuilder.AlterColumn<string>(
                name: "CourseTitle",
                table: "FacultySubjects",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "CourseCode",
                table: "FacultySubjects",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);

            migrationBuilder.RenameColumn(
                name: "CourseCode",
                table: "FacultySubjects",
                newName: "SubjectCode");

            migrationBuilder.RenameColumn(
                name: "CourseTitle",
                table: "FacultySubjects",
                newName: "SubjectName");

                        migrationBuilder.DropColumn(
                name: "CreatedById",
                table: "DevelopmentPlans");

            migrationBuilder.DropColumn(
                name: "CreatedByRole",
                table: "DevelopmentPlans");

            migrationBuilder.DropColumn(
                name: "FacultyEditedAt",
                table: "DevelopmentPlans");

            migrationBuilder.DropColumn(
                name: "FinalizedAt",
                table: "DevelopmentPlans");

            migrationBuilder.DropColumn(
                name: "FinalizedById",
                table: "DevelopmentPlans");

            migrationBuilder.DropColumn(
                name: "ProgramChairId",
                table: "DevelopmentPlans");

            migrationBuilder.DropColumn(
                name: "MiddleName",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "MustChangePassword",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "Suffix",
                table: "AspNetUsers");

                                    migrationBuilder.CreateIndex(
                name: "IX_FacultySubjects_SemesterId",
                table: "FacultySubjects",
                column: "SemesterId");
        }
    }
}
