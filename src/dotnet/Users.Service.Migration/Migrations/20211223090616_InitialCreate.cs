using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations.Migrations
{
    public partial class InitialCreate : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "_Operations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    AgentId = table.Column<string>(type: "text", nullable: false),
                    StartTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CommitTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CommandJson = table.Column<string>(type: "text", nullable: false),
                    ItemsJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Operations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "_Sessions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IPAddress = table.Column<string>(type: "text", nullable: false),
                    UserAgent = table.Column<string>(type: "text", nullable: false),
                    AuthenticatedIdentity = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    IsSignOutForced = table.Column<bool>(type: "boolean", nullable: false),
                    OptionsJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserAuthors",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Picture = table.Column<string>(type: "text", nullable: false),
                    IsAnonymous = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAuthors", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    ClaimsJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserStates",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "text", nullable: false),
                    OnlineCheckInAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserStates", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "UserIdentities",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    Secret = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserIdentities_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CommitTime",
                table: "_Operations",
                column: "CommitTime");

            migrationBuilder.CreateIndex(
                name: "IX_StartTime",
                table: "_Operations",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX__Sessions_CreatedAt_IsSignOutForced",
                table: "_Sessions",
                columns: new[] { "CreatedAt", "IsSignOutForced" });

            migrationBuilder.CreateIndex(
                name: "IX__Sessions_IPAddress_IsSignOutForced",
                table: "_Sessions",
                columns: new[] { "IPAddress", "IsSignOutForced" });

            migrationBuilder.CreateIndex(
                name: "IX__Sessions_LastSeenAt_IsSignOutForced",
                table: "_Sessions",
                columns: new[] { "LastSeenAt", "IsSignOutForced" });

            migrationBuilder.CreateIndex(
                name: "IX__Sessions_UserId_IsSignOutForced",
                table: "_Sessions",
                columns: new[] { "UserId", "IsSignOutForced" });

            migrationBuilder.CreateIndex(
                name: "IX_UserIdentities_Id",
                table: "UserIdentities",
                column: "Id");

            migrationBuilder.CreateIndex(
                name: "IX_UserIdentities_UserId",
                table: "UserIdentities",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Name",
                table: "Users",
                column: "Name");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "_Operations");

            migrationBuilder.DropTable(
                name: "_Sessions");

            migrationBuilder.DropTable(
                name: "UserAuthors");

            migrationBuilder.DropTable(
                name: "UserIdentities");

            migrationBuilder.DropTable(
                name: "UserStates");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
