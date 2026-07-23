using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Household.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHouseholdConsumerConnections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HouseholdAuthorizationAttempts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    StateHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ProtectedCodeVerifier = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    RedirectUri = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    RequestedScopes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseholdAuthorizationAttempts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HouseholdAuthorizationAttempts_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HouseholdConsumerConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ProtectedAccessToken = table.Column<string>(type: "TEXT", maxLength: 24000, nullable: false),
                    AccessTokenExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProtectedRefreshToken = table.Column<string>(type: "TEXT", maxLength: 24000, nullable: false),
                    RefreshTokenExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SourceConnectionId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    AccountId = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    AccountDisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    GrantedScopes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LastError = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ConnectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastValidatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseholdConsumerConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HouseholdConsumerConnections_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdAuthorizationAttempts_StateHash",
                table: "HouseholdAuthorizationAttempts",
                column: "StateHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdAuthorizationAttempts_UserId_Provider_ExpiresAt",
                table: "HouseholdAuthorizationAttempts",
                columns: new[] { "UserId", "Provider", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdConsumerConnections_UserId_Provider",
                table: "HouseholdConsumerConnections",
                columns: new[] { "UserId", "Provider" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HouseholdAuthorizationAttempts");

            migrationBuilder.DropTable(
                name: "HouseholdConsumerConnections");
        }
    }
}
