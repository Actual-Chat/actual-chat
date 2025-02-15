using ActualChat.Db;
using ActualChat.Media.Db;
using ActualChat.Media.Resources;
using ActualChat.Roulette;

namespace ActualChat.Media.Module;

public class MediaDbInitializer(IServiceProvider services) : DbInitializer<MediaDbContext>(services)
{
    public override async Task InitializeData(CancellationToken cancellationToken)
    {
        // Add default chat images
        await new MediaUploader(GetType())
            .Upload(async x => {
                await x.AddMedia("system-icons:family", Resource.FamilySvg).ConfigureAwait(false);
                await x.AddMedia("system-icons:coworkers", Resource.CoworkersSvg).ConfigureAwait(false);
                await x.AddMedia("system-icons:friends", Resource.FriendsSvg).ConfigureAwait(false);
                await x.AddMedia("system-icons:alumni", Resource.AlumniSvg).ConfigureAwait(false);
                await x.AddMedia("system-icons:notes", Resource.NotesSvg).ConfigureAwait(false);
            }).ConfigureAwait(false);

        // Add Sherlock image
        await new MediaUploader(GetType())
            .Upload(async c => {
                await c.AddMedia(Constants.User.Sherlock.MediaId.Value, Resource.Sherlock).ConfigureAwait(false);
            }).ConfigureAwait(false);

        // Add Chat Roulette image
        await new MediaUploader(GetType())
            .Upload(async c => {
                await c.AddMedia(ChatRoulette.MediaId.Value, Resource.ChatRoulette).ConfigureAwait(false);
            }).ConfigureAwait(false);
    }
}
