using System;
using ActualChat.Contacts.Db;
using ActualChat.Db;
using ActualChat.Hosting;
using ActualChat.Users.Module;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Contacts.Migrations
{
    public partial class MoveContacts : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Does nothing, the logic was moved to MoveContacts2
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        { }
    }
}
