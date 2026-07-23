using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Household.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddIntegrationFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AllowedComposeApps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AppId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    ComposePath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ProjectName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: true),
                    ContainerNamesJson = table.Column<string>(type: "TEXT", nullable: true),
                    AllowedActionsJson = table.Column<string>(type: "TEXT", nullable: true),
                    HealthCheckUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    HealthCheckTimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    AdminActionsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AllowedComposeApps", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppLauncherItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AppId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    IconUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    InternalUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ExternalUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    OpenUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Favorite = table.Column<bool>(type: "INTEGER", nullable: false),
                    AdminActionsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppLauncherItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    OpenUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastHealthStatus = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Integrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DashboardWidgets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WidgetType = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    IntegrationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    SettingsJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DashboardWidgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DashboardWidgets_Integrations_IntegrationId",
                        column: x => x.IntegrationId,
                        principalTable: "Integrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationActionLogs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IntegrationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AppId = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Action = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    RequestSummaryJson = table.Column<string>(type: "TEXT", nullable: true),
                    ResultSummaryJson = table.Column<string>(type: "TEXT", nullable: true),
                    ErrorMessage = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationActionLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntegrationActionLogs_Integrations_IntegrationId",
                        column: x => x.IntegrationId,
                        principalTable: "Integrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "IntegrationSecrets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IntegrationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SecretKey = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    ProtectedValue = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntegrationSecrets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntegrationSecrets_Integrations_IntegrationId",
                        column: x => x.IntegrationId,
                        principalTable: "Integrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AllowedComposeApps_AppId",
                table: "AllowedComposeApps",
                column: "AppId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppLauncherItems_AppId",
                table: "AppLauncherItems",
                column: "AppId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AppLauncherItems_Category",
                table: "AppLauncherItems",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardWidgets_IntegrationId",
                table: "DashboardWidgets",
                column: "IntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_DashboardWidgets_UserId_Position",
                table: "DashboardWidgets",
                columns: new[] { "UserId", "Position" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationActionLogs_AppId_StartedAt",
                table: "IntegrationActionLogs",
                columns: new[] { "AppId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationActionLogs_IntegrationId_StartedAt",
                table: "IntegrationActionLogs",
                columns: new[] { "IntegrationId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Integrations_Type_Name",
                table: "Integrations",
                columns: new[] { "Type", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IntegrationSecrets_IntegrationId_SecretKey",
                table: "IntegrationSecrets",
                columns: new[] { "IntegrationId", "SecretKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AllowedComposeApps");

            migrationBuilder.DropTable(
                name: "AppLauncherItems");

            migrationBuilder.DropTable(
                name: "DashboardWidgets");

            migrationBuilder.DropTable(
                name: "IntegrationActionLogs");

            migrationBuilder.DropTable(
                name: "IntegrationSecrets");

            migrationBuilder.DropTable(
                name: "Integrations");
        }
    }
}
