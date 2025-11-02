using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POETWeb.Migrations
{
    /// <inheritdoc />
    public partial class UpdateAssignment1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Score",
                table: "AssignmentAttempts");

            migrationBuilder.DropColumn(
                name: "AutoScore",
                table: "AssignmentAnswers");

            migrationBuilder.DropColumn(
                name: "ManualScore",
                table: "AssignmentAnswers");

            migrationBuilder.AlterColumn<decimal>(
                name: "Points",
                table: "AssignmentQuestions",
                type: "decimal(6,2)",
                precision: 6,
                scale: 2,
                nullable: false,
                oldClrType: typeof(double),
                oldType: "float(6)",
                oldPrecision: 6,
                oldScale: 2);

            migrationBuilder.AddColumn<decimal>(
                name: "AutoScore",
                table: "AssignmentAttempts",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationMinutes",
                table: "AssignmentAttempts",
                type: "int",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<decimal>(
                name: "FinalScore",
                table: "AssignmentAttempts",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MaxScore",
                table: "AssignmentAttempts",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresManualGrading",
                table: "AssignmentAttempts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsCorrect",
                table: "AssignmentAnswers",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PointsAwarded",
                table: "AssignmentAnswers",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoScore",
                table: "AssignmentAttempts");

            migrationBuilder.DropColumn(
                name: "DurationMinutes",
                table: "AssignmentAttempts");

            migrationBuilder.DropColumn(
                name: "FinalScore",
                table: "AssignmentAttempts");

            migrationBuilder.DropColumn(
                name: "MaxScore",
                table: "AssignmentAttempts");

            migrationBuilder.DropColumn(
                name: "RequiresManualGrading",
                table: "AssignmentAttempts");

            migrationBuilder.DropColumn(
                name: "IsCorrect",
                table: "AssignmentAnswers");

            migrationBuilder.DropColumn(
                name: "PointsAwarded",
                table: "AssignmentAnswers");

            migrationBuilder.AlterColumn<double>(
                name: "Points",
                table: "AssignmentQuestions",
                type: "float(6)",
                precision: 6,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(6,2)",
                oldPrecision: 6,
                oldScale: 2);

            migrationBuilder.AddColumn<double>(
                name: "Score",
                table: "AssignmentAttempts",
                type: "float(8)",
                precision: 8,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "AutoScore",
                table: "AssignmentAnswers",
                type: "float(6)",
                precision: 6,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "ManualScore",
                table: "AssignmentAnswers",
                type: "float(6)",
                precision: 6,
                scale: 2,
                nullable: true);
        }
    }
}
