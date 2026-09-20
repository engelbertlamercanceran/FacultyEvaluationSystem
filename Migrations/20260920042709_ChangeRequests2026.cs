using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FacultyEvalSystem.Migrations
{
    /// <inheritdoc />
    public partial class ChangeRequests2026 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Status",
                table: "EvaluationPeriods",
                newName: "Stage");

            migrationBuilder.AddColumn<int>(
                name: "SortKey",
                table: "Semesters",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ProgramId",
                table: "FacultySubjects",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TeachingMemoNumber",
                table: "FacultySubjects",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ClosedAt",
                table: "EvaluationPeriods",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeanStageOpenedAt",
                table: "EvaluationPeriods",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "StudentStageOpenedAt",
                table: "EvaluationPeriods",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "SupervisorStageOpenedAt",
                table: "EvaluationPeriods",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "AcknowledgedAt",
                table: "DevelopmentPlans",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AcknowledgedById",
                table: "DevelopmentPlans",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SupervisorId",
                table: "DevelopmentPlans",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DesignatedEvaluatorId",
                table: "AspNetUsers",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Semesters_SortKey",
                table: "Semesters",
                column: "SortKey");

            migrationBuilder.CreateIndex(
                name: "IX_FacultySubjects_ProgramId",
                table: "FacultySubjects",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_DesignatedEvaluatorId",
                table: "AspNetUsers",
                column: "DesignatedEvaluatorId");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_AspNetUsers_DesignatedEvaluatorId",
                table: "AspNetUsers",
                column: "DesignatedEvaluatorId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_FacultySubjects_AcademicPrograms_ProgramId",
                table: "FacultySubjects",
                column: "ProgramId",
                principalTable: "AcademicPrograms",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            // ---- Data migration ----------------------------------------------------

            // AAS-03: the column was renamed Status -> Stage, but the enum values moved.
            // Old EvaluationStatus: Pending = 0, Open = 1, Closed = 2
            // New EvaluationStage:  NotStarted = 0, StudentEvaluation = 1,
            //                       SupervisorEvaluation = 2, DeanEvaluation = 3, Closed = 4
            // Only Closed shifts (2 -> 4). Pending and Open keep their numbers, and a
            // period that was simply "Open" is treated as being at the student stage.
            // No row can already hold 3 or 4, so this cannot collide.
            migrationBuilder.Sql(@"
                UPDATE [EvaluationPeriods] SET [Stage] = 4 WHERE [Stage] = 2;");

            // Record a plausible opening/closing time for periods that pre-date staging,
            // so the schedule screen is not blank for historical records.
            migrationBuilder.Sql(@"
                UPDATE [EvaluationPeriods]
                   SET [ClosedAt] = [EndDate]
                 WHERE [Stage] = 4 AND [ClosedAt] IS NULL;

                UPDATE [EvaluationPeriods]
                   SET [StudentStageOpenedAt] = [StartDate]
                 WHERE [Stage] >= 1 AND [StudentStageOpenedAt] IS NULL;");

            // SUP-01: give every existing semester its chronological sort key.
            // Mirrors Semester.ComputeSortKey: start year * 10 + term order.
            migrationBuilder.Sql(@"
                UPDATE [Semesters]
                   SET [SortKey] =
                       (ISNULL(TRY_CAST(LEFT([AcademicYear], 4) AS int), 0) * 10) +
                       CASE LTRIM(RTRIM([Term]))
                            WHEN '1st Semester' THEN 1
                            WHEN '2nd Semester' THEN 2
                            WHEN 'Summer'       THEN 3
                            ELSE 9
                       END
                 WHERE [SortKey] = 0;");

            // PC-02/FC-01: plans written before this change have the faculty member's
            // own Areas for Improvement. They are kept as historical record; marking
            // SupervisorSubmittedAt is NOT done here, so their status stays correct.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse the stage value shift before the column becomes Status again:
            // anything at or past the supervisor stage collapses back to Closed (2),
            // since the old model had no way to express a mid-sequence stage.
            migrationBuilder.Sql(@"
                UPDATE [EvaluationPeriods] SET [Stage] = 2 WHERE [Stage] >= 2;");

            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_AspNetUsers_DesignatedEvaluatorId",
                table: "AspNetUsers");

            migrationBuilder.DropForeignKey(
                name: "FK_FacultySubjects_AcademicPrograms_ProgramId",
                table: "FacultySubjects");

            migrationBuilder.DropIndex(
                name: "IX_Semesters_SortKey",
                table: "Semesters");

            migrationBuilder.DropIndex(
                name: "IX_FacultySubjects_ProgramId",
                table: "FacultySubjects");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_DesignatedEvaluatorId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "SortKey",
                table: "Semesters");

            migrationBuilder.DropColumn(
                name: "ProgramId",
                table: "FacultySubjects");

            migrationBuilder.DropColumn(
                name: "TeachingMemoNumber",
                table: "FacultySubjects");

            migrationBuilder.DropColumn(
                name: "ClosedAt",
                table: "EvaluationPeriods");

            migrationBuilder.DropColumn(
                name: "DeanStageOpenedAt",
                table: "EvaluationPeriods");

            migrationBuilder.DropColumn(
                name: "StudentStageOpenedAt",
                table: "EvaluationPeriods");

            migrationBuilder.DropColumn(
                name: "SupervisorStageOpenedAt",
                table: "EvaluationPeriods");

            migrationBuilder.DropColumn(
                name: "AcknowledgedAt",
                table: "DevelopmentPlans");

            migrationBuilder.DropColumn(
                name: "AcknowledgedById",
                table: "DevelopmentPlans");

            migrationBuilder.DropColumn(
                name: "SupervisorId",
                table: "DevelopmentPlans");

            migrationBuilder.DropColumn(
                name: "DesignatedEvaluatorId",
                table: "AspNetUsers");

            migrationBuilder.RenameColumn(
                name: "Stage",
                table: "EvaluationPeriods",
                newName: "Status");
        }
    }
}
