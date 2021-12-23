using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations.Migrations
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
                name: "ChatAuthors",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    ChatId = table.Column<string>(type: "text", nullable: false),
                    LocalId = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Picture = table.Column<string>(type: "text", nullable: false),
                    IsAnonymous = table.Column<bool>(type: "boolean", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatAuthors", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatEntries",
                columns: table => new
                {
                    CompositeId = table.Column<string>(type: "text", nullable: false),
                    ChatId = table.Column<string>(type: "text", nullable: false),
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    AuthorId = table.Column<string>(type: "text", nullable: false),
                    BeginsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Duration = table.Column<double>(type: "double precision", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    StreamId = table.Column<string>(type: "text", nullable: true),
                    AudioEntryId = table.Column<long>(type: "bigint", nullable: true),
                    VideoEntryId = table.Column<long>(type: "bigint", nullable: true),
                    TextToTimeMap = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatEntries", x => x.CompositeId);
                });

            migrationBuilder.CreateTable(
                name: "Chats",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chats", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatOwners",
                columns: table => new
                {
                    ChatId = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    DbChatId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatOwners", x => new { x.ChatId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ChatOwners_Chats_DbChatId",
                        column: x => x.DbChatId,
                        principalTable: "Chats",
                        principalColumn: "Id");
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
                name: "IX_ChatAuthors_ChatId_UserId",
                table: "ChatAuthors",
                columns: new[] { "ChatId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatEntries_ChatId_BeginsAt_EndsAt_Type",
                table: "ChatEntries",
                columns: new[] { "ChatId", "BeginsAt", "EndsAt", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatEntries_ChatId_EndsAt_BeginsAt_Type",
                table: "ChatEntries",
                columns: new[] { "ChatId", "EndsAt", "BeginsAt", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatEntries_ChatId_Id",
                table: "ChatEntries",
                columns: new[] { "ChatId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatEntries_ChatId_Version",
                table: "ChatEntries",
                columns: new[] { "ChatId", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatOwners_DbChatId",
                table: "ChatOwners",
                column: "DbChatId");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "_Operations");

            migrationBuilder.DropTable(
                name: "ChatAuthors");

            migrationBuilder.DropTable(
                name: "ChatEntries");

            migrationBuilder.DropTable(
                name: "ChatOwners");

            migrationBuilder.DropTable(
                name: "Chats");
        }
    }
}
