using ActualChat.Chat;
using ActualChat.Media.Resources;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Media.Migrations
{
    /// <inheritdoc />
    public partial class AddChatRouletteImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
 #pragma warning disable VSTHRD002
            UpAsync(migrationBuilder).Wait();
 #pragma warning restore VSTHRD002
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }

        private async Task UpAsync(MigrationBuilder migrationBuilder)
        {
            await new ImagesUploader(this.GetType())
                .Execute(async c => {
                    await c.AddMedia(ChatRoulette.MediaId.Value, Resource.ChatRoulette).ConfigureAwait(false);
                }).ConfigureAwait(false);
        }
    }
}
