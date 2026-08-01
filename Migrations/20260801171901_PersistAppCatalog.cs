using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Household.Api.Migrations
{
    /// <inheritdoc />
    public partial class PersistAppCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "Enabled",
                table: "AppLauncherItems",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Enabled",
                table: "AppLauncherItems");
        }
    }
}
