using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POETWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddTeacherComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TeacherComment",
                table: "AssignmentAttempts",
                type: "nvarchar(max)",
                maxLength: 8000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TeacherComment",
                table: "AssignmentAnswers",
                type: "nvarchar(max)",
                maxLength: 8000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TeacherComment",
                table: "AssignmentAttempts");

            migrationBuilder.DropColumn(
                name: "TeacherComment",
                table: "AssignmentAnswers");
        }
    }
}
