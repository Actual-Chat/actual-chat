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
            // Moved to InitializeData at DbInitializer
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
