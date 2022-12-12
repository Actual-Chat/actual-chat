using Newtonsoft.Json;
using Stl.Fusion.EntityFramework.Authentication;

namespace ActualChat.Users.Db;

public sealed class DbSessionInfoConverter : DbSessionInfoConverter<UsersDbContext, DbSessionInfo, string>
{
    public DbSessionInfoConverter(IServiceProvider services) : base(services) { }

    public override void UpdateEntity(SessionInfo source, DbSessionInfo target)
    {
        base.UpdateEntity(source, target);
        GuestIdOption? guestIdOption = null;
        try {
            guestIdOption = target.Options.Get<GuestIdOption>();
        }
        catch {
            // Intended: GuestId type was changed, so it might throw an error
        }

        var guestId = guestIdOption?.GuestId ?? default;
        if (guestId.IsNone || !guestId.IsGuestId) {
            guestId = UserId.NewGuest();
            guestIdOption = new GuestIdOption(guestId);
            target.Options = target.Options.Set(guestIdOption);
        }
    }

    public override SessionInfo UpdateModel(DbSessionInfo source, SessionInfo target)
    {
        try {
            return base.UpdateModel(source, target);
        }
        catch (JsonSerializationException e) {
            Log.LogError(e, "SessionInfo.Options are incompatible with the current codebase, resetting them");
            source.Options = ImmutableOptionSet.Empty;
            return base.UpdateModel(source, target);
        }
    }
}
