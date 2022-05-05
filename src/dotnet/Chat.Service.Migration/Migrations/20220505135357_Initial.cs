using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    public partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "_Operations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AgentId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CommitTime = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    CommandJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ItemsJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK__Operations", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ChatAuthors",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChatId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    LocalId = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsAnonymous = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    UserId = table.Column<string>(type: "varchar(255)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatAuthors", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ChatEntries",
                columns: table => new
                {
                    CompositeId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChatId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Id = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    IsRemoved = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AuthorId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    BeginsAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    ClientSideBeginsAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EndsAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ContentEndsAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Duration = table.Column<double>(type: "double", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    HasAttachments = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    StreamId = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    AudioEntryId = table.Column<long>(type: "bigint", nullable: true),
                    VideoEntryId = table.Column<long>(type: "bigint", nullable: true),
                    TextToTimeMap = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatEntries", x => x.CompositeId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Chats",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    Title = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    IsPublic = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    ChatType = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Chats", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TextEntryAttachments",
                columns: table => new
                {
                    CompositeId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChatId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EntryId = table.Column<long>(type: "bigint", nullable: false),
                    Index = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    ContentId = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MetadataJson = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TextEntryAttachments", x => x.CompositeId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ChatOwners",
                columns: table => new
                {
                    ChatId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    UserId = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DbChatId = table.Column<string>(type: "varchar(255)", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatOwners", x => new { x.ChatId, x.UserId });
                    table.ForeignKey(
                        name: "FK_ChatOwners_Chats_DbChatId",
                        column: x => x.DbChatId,
                        principalTable: "Chats",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CommitTime",
                table: "_Operations",
                column: "CommitTime");

            migrationBuilder.CreateIndex(
                name: "IX_StartTime",
                table: "_Operations",
                column: "StartTime");

            migrationBuilder.CreateIndex(
                name: "IX_ChatAuthors_ChatId_LocalId",
                table: "ChatAuthors",
                columns: new[] { "ChatId", "LocalId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatAuthors_ChatId_UserId",
                table: "ChatAuthors",
                columns: new[] { "ChatId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatEntries_ChatId_Type_BeginsAt_EndsAt",
                table: "ChatEntries",
                columns: new[] { "ChatId", "Type", "BeginsAt", "EndsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatEntries_ChatId_Type_EndsAt_BeginsAt",
                table: "ChatEntries",
                columns: new[] { "ChatId", "Type", "EndsAt", "BeginsAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatEntries_ChatId_Type_Id",
                table: "ChatEntries",
                columns: new[] { "ChatId", "Type", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatEntries_ChatId_Type_IsRemoved_Id",
                table: "ChatEntries",
                columns: new[] { "ChatId", "Type", "IsRemoved", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatEntries_ChatId_Type_Version",
                table: "ChatEntries",
                columns: new[] { "ChatId", "Type", "Version" });

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
                name: "TextEntryAttachments");

            migrationBuilder.DropTable(
                name: "Chats");
        }
    }
}
