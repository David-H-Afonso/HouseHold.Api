using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Household.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSettingsAdministrationAndMonitors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SessionVersion",
                table: "Users",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                table: "TaskTemplates",
                type: "TEXT",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "TaskTemplates"
                SET "OwnerUserId" = (
                    SELECT "Id" FROM "Users" WHERE "IsAdmin" = 1 AND "IsActive" = 1 ORDER BY "CreatedAt" LIMIT 1
                )
                WHERE "OwnerUserId" IS NULL
                """
            );

            migrationBuilder.AddColumn<int>(
                name: "SchemaVersion",
                table: "DashboardWidgets",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "Size",
                table: "DashboardWidgets",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "medium");

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TargetUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    SummaryJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditEvents_Users_ActorUserId",
                        column: x => x.ActorUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UserAppFavorites",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AppId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAppFavorites", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAppFavorites_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserInvitations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Email = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsAdmin = table.Column<bool>(type: "INTEGER", nullable: false),
                    TokenHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RedeemedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedByUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RedeemedUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserInvitations_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UserInvitations_Users_RedeemedUserId",
                        column: x => x.RedeemedUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "UserPreferences",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    VisualPreference = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PokemonSpriteSource = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    GamesStatusOrderJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    HiddenGitHubReposJson = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    JellyfinUserId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPreferences", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserPreferences_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TaskTemplates_OwnerUserId",
                table: "TaskTemplates",
                column: "OwnerUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardWidgets_UserId_WidgetType",
                table: "DashboardWidgets",
                columns: new[] { "UserId", "WidgetType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_ActorUserId",
                table: "AuditEvents",
                column: "ActorUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_CreatedAt",
                table: "AuditEvents",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_UserAppFavorites_UserId_AppId",
                table: "UserAppFavorites",
                columns: new[] { "UserId", "AppId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserInvitations_CreatedByUserId",
                table: "UserInvitations",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserInvitations_Email_ExpiresAt",
                table: "UserInvitations",
                columns: new[] { "Email", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserInvitations_RedeemedUserId",
                table: "UserInvitations",
                column: "RedeemedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserInvitations_TokenHash",
                table: "UserInvitations",
                column: "TokenHash",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DashboardWidgets_Users_UserId",
                table: "DashboardWidgets",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_TaskTemplates_Users_OwnerUserId",
                table: "TaskTemplates",
                column: "OwnerUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DashboardWidgets_Users_UserId",
                table: "DashboardWidgets");

            migrationBuilder.DropForeignKey(
                name: "FK_TaskTemplates_Users_OwnerUserId",
                table: "TaskTemplates");

            migrationBuilder.DropTable(
                name: "AuditEvents");

            migrationBuilder.DropTable(
                name: "UserAppFavorites");

            migrationBuilder.DropTable(
                name: "UserInvitations");

            migrationBuilder.DropTable(
                name: "UserPreferences");

            migrationBuilder.DropIndex(
                name: "IX_TaskTemplates_OwnerUserId",
                table: "TaskTemplates");

            migrationBuilder.DropIndex(
                name: "IX_DashboardWidgets_UserId_WidgetType",
                table: "DashboardWidgets");

            migrationBuilder.DropColumn(
                name: "SessionVersion",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "TaskTemplates");

            migrationBuilder.DropColumn(
                name: "SchemaVersion",
                table: "DashboardWidgets");

            migrationBuilder.DropColumn(
                name: "Size",
                table: "DashboardWidgets");
        }
    }
}
