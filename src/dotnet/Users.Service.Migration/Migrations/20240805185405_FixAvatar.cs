using ActualChat.Chat.Module;
using ActualChat.Db;
using ActualChat.Hosting;
using ActualChat.Media.Module;
using ActualChat.Users.Module;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class FixAvatar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                update avatars
                set avatar_key = replace(replace(picture, 'https://source.boringavatars.com/beam/160/', ''), '?colors=FFDBA0,BBBEFF,9294E1,FF9BC0,0F2FE8', '')
                where picture ilike 'https://source.boringavatars.com%';

                update avatars
                set picture = ''
                where picture ilike 'https://source.boringavatars.com%';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
