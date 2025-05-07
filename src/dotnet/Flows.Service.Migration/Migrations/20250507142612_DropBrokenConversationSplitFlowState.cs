using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Flows.Migrations
{
    /// <inheritdoc />
    public partial class DropBrokenConversationSplitFlowState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Remove broken ConversationSplitFlow state
            migrationBuilder.Sql("DELETE FROM _flows WHERE Id LIKE 'ConversationSplitFlow:%'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        { }
    }
}
