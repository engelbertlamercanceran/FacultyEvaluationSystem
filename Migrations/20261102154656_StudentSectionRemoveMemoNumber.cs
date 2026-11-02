using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FacultyEvalSystem.Migrations
{
    /// <inheritdoc />
    public partial class StudentSectionRemoveMemoNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TeachingMemoNumber",
                table: "FacultySubjects");

            migrationBuilder.AddColumn<string>(
                name: "Section",
                table: "AspNetUsers",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Section",
                table: "AspNetUsers");

            migrationBuilder.AddColumn<string>(
                name: "TeachingMemoNumber",
                table: "FacultySubjects",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);
        }
    }
}
