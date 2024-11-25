using System.Text;
using ActualChat.Db;
using ActualChat.Hashing;
using ActualChat.Hosting;
using ActualChat.Media.Db;
using ActualChat.Media.Module;
using ActualChat.Media.Resources;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using ActualLab.Fusion.EntityFramework;

#nullable disable
#pragma warning disable VSTHRD002

namespace ActualChat.Media.Migrations
{
    /// <inheritdoc />
    public partial class AddDefaultChatImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            UpAsync(migrationBuilder).Wait();
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }

        private async Task UpAsync(MigrationBuilder migrationBuilder)
        {
            await new ImagesUploader(this.GetType())
                .Execute(async c => {
                    await c.AddMedia("system-icons:family", Resource.FamilySvg).ConfigureAwait(false);
                    await c.AddMedia("system-icons:coworkers", Resource.CoworkersSvg).ConfigureAwait(false);
                    await c.AddMedia("system-icons:friends", Resource.FriendsSvg).ConfigureAwait(false);
                    await c.AddMedia("system-icons:alumni", Resource.AlumniSvg).ConfigureAwait(false);
                    await c.AddMedia("system-icons:notes", Resource.NotesSvg).ConfigureAwait(false);
                }).ConfigureAwait(false);
        }
    }
}
