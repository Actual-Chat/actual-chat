using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Chat.Migrations
{
    /// <inheritdoc />
    public partial class Legacy_Renames_2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "entry_id",
                table: "mentions",
                newName: "entry_local_id");

            migrationBuilder.RenameIndex(
                name: "ix_mentions_chat_id_mention_id_entry_id",
                table: "mentions",
                newName: "ix_mentions_chat_id_mention_id_entry_local_id");

            migrationBuilder.RenameIndex(
                name: "ix_mentions_chat_id_entry_id_mention_id",
                table: "mentions",
                newName: "ix_mentions_chat_id_entry_local_id_mention_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "entry_local_id",
                table: "mentions",
                newName: "entry_id");

            migrationBuilder.RenameIndex(
                name: "ix_mentions_chat_id_mention_id_entry_local_id",
                table: "mentions",
                newName: "ix_mentions_chat_id_mention_id_entry_id");

            migrationBuilder.RenameIndex(
                name: "ix_mentions_chat_id_entry_local_id_mention_id",
                table: "mentions",
                newName: "ix_mentions_chat_id_entry_id_mention_id");
        }
    }
}
