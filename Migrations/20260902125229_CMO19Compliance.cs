using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FacultyEvalSystem.Migrations
{
    /// <inheritdoc />
    public partial class CMO19Compliance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OverallRating",
                table: "EvaluationResults");

            migrationBuilder.DropColumn(
                name: "Weight",
                table: "EvaluationCategories");

            migrationBuilder.RenameColumn(
                name: "DescriptiveRating",
                table: "EvaluationResults",
                newName: "SupervisorDescriptiveRating");

            migrationBuilder.AddColumn<string>(
                name: "StudentDescriptiveRating",
                table: "EvaluationResults",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            // Clear old computed results (formula changed from 1-5 scale to 0-100 percentage)
            migrationBuilder.Sql("DELETE FROM EvaluationResults");

            // Clear old NBC 461 categories/criteria so seeder inserts CMO No. 19 compliant ones
            migrationBuilder.Sql("DELETE FROM EvaluationCriteria");
            migrationBuilder.Sql("DELETE FROM EvaluationCategories");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StudentDescriptiveRating",
                table: "EvaluationResults");

            migrationBuilder.RenameColumn(
                name: "SupervisorDescriptiveRating",
                table: "EvaluationResults",
                newName: "DescriptiveRating");

            migrationBuilder.AddColumn<double>(
                name: "OverallRating",
                table: "EvaluationResults",
                type: "float",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "Weight",
                table: "EvaluationCategories",
                type: "float",
                nullable: false,
                defaultValue: 0.0);
        }
    }
}
