using ActualChat.Hosting;
using ActualChat.Users.Db;
using ActualChat.Users.Module;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ActualChat.Users.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSherlockPic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
 #pragma warning disable VSTHRD002
            UpAsync(migrationBuilder, default).Wait();
 #pragma warning restore VSTHRD002
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }

        private async Task UpAsync(MigrationBuilder migrationBuilder, CancellationToken cancellationToken)
        {
            var dbInitializer = DbInitializer.GetCurrent<UsersDbInitializer>();
            var services = dbInitializer.Services;
            var log = services.LogFor(GetType());
            var accountsBackend = services.GetRequiredService<IAccountsBackend>();

            var account = await accountsBackend.Get(Constants.User.Sherlock.UserId, cancellationToken).ConfigureAwait(false);
            if (account == null)
                return;

            var avatar = account.Avatar;
            if (!avatar.MediaId.IsNone && OrdinalEquals(avatar.Bio, Constants.User.Sherlock.Name))
                return;

            //using var dbContext = dbInitializer.CreateDbContext(true);
            log.LogInformation("Updating Sherlock Avatar");
            var avatarFull = new AvatarFull(account.Id, avatar.Id).WithMissingPropertiesFrom(avatar);
            avatarFull = avatarFull with {
                Bio = Constants.User.Sherlock.Name,
                MediaId = Constants.User.Sherlock.MediaId,
                PictureUrl = "",
            };
            var changeAvatarCommand = new AvatarsBackend_Change(avatar.Id, avatar.Version, Change.Update(avatarFull));
            var commander = services.Commander();
            avatar = await commander.Call(changeAvatarCommand, cancellationToken).ConfigureAwait(false);
        }
    }
}
