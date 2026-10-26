using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FacultyEvalSystem.Migrations
{
    /// <inheritdoc />
    public partial class MergeDeanIntoSupervisorStage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The Dean stage (3) is merged into the Supervisor stage (2): a period
            // that is at the Dean stage stays open to Deans, and now to Chairs too.
            migrationBuilder.Sql("UPDATE EvaluationPeriods SET Stage = 2 WHERE Stage = 3");

            migrationBuilder.DropColumn(
                name: "DeanStageOpenedAt",
                table: "EvaluationPeriods");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeanStageOpenedAt",
                table: "EvaluationPeriods",
                type: "datetime2",
                nullable: true);
        }
    }
}
