using ActualChat.Db;
using ActualChat.Media.Db;
using ActualChat.Media.Resources;

namespace ActualChat.Media.Module;

public class MediaDbInitializer(IServiceProvider services) : DbInitializer<MediaDbContext>(services)
{
    public override async Task InitializeData(CancellationToken cancellationToken)
    {
        // Add default chat images
        var mediaUploader = new MediaUploader(GetType(), VersionGenerator);
        await mediaUploader
            .Upload(async x => {
                    await x.AddMedia("system-icons:family", Resource.FamilySvg, MediaKind.ChatPicture).ConfigureAwait(false);
                    await x.AddMedia("system-icons:coworkers", Resource.CoworkersSvg, MediaKind.ChatPicture).ConfigureAwait(false);
                    await x.AddMedia("system-icons:friends", Resource.FriendsSvg, MediaKind.ChatPicture).ConfigureAwait(false);
                    await x.AddMedia("system-icons:alumni", Resource.AlumniSvg, MediaKind.ChatPicture).ConfigureAwait(false);
                    await x.AddMedia("system-icons:notes", Resource.NotesSvg, MediaKind.ChatPicture).ConfigureAwait(false);
                },
                cancellationToken)
            .ConfigureAwait(false);

        // Add Sherlock image
        await mediaUploader
            .Upload(c => c.AddMedia(Constants.User.Sherlock.MediaId.Value, Resource.Sherlock, MediaKind.UserPicture), cancellationToken)
            .ConfigureAwait(false);
    }
}
