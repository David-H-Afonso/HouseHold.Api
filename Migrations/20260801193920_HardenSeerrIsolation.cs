using Household.Api.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Household.Api.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("20260801193920_HardenSeerrIsolation")]
    public partial class HardenSeerrIsolation : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ConfigurationVersion",
                table: "Integrations",
                type: "TEXT",
                nullable: false,
                defaultValue: Guid.Empty);

            migrationBuilder.AddColumn<int>(
                name: "SeerrResolvedUserId",
                table: "UserPreferences",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPreferences_JellyfinUserId",
                table: "UserPreferences",
                column: "JellyfinUserId",
                unique: true,
                filter: "\"SeerrJellyfinMappingApproved\" = 1 AND \"JellyfinUserId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserPreferences_SeerrUserIdOverride",
                table: "UserPreferences",
                column: "SeerrUserIdOverride",
                unique: true,
                filter: "\"SeerrUserIdOverride\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_UserPreferences_SeerrResolvedUserId",
                table: "UserPreferences",
                column: "SeerrResolvedUserId",
                unique: true,
                filter: "\"SeerrResolvedUserId\" IS NOT NULL");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserPreferences_JellyfinUserId",
                table: "UserPreferences");

            migrationBuilder.DropIndex(
                name: "IX_UserPreferences_SeerrUserIdOverride",
                table: "UserPreferences");

            migrationBuilder.DropIndex(
                name: "IX_UserPreferences_SeerrResolvedUserId",
                table: "UserPreferences");

            migrationBuilder.DropColumn(
                name: "ConfigurationVersion",
                table: "Integrations");

            migrationBuilder.DropColumn(
                name: "SeerrResolvedUserId",
                table: "UserPreferences");
        }
    }
}
