using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace POETWeb.Migrations
{
    /// <inheritdoc />
    public partial class AddMaterialIndexContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IndexContent",
                table: "Materials",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IndexContent",
                table: "Materials");
        }
    }
}
