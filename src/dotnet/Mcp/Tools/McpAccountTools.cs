using System.ComponentModel;
using ActualChat.Mcp.Auth;
using ModelContextProtocol.Server;

namespace ActualChat.Mcp.Tools;

[McpServerToolType]
public sealed class McpAccountTools(IServiceProvider services)
{
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IAvatars Avatars { get; } = services.GetRequiredService<IAvatars>();
    private UrlMapper UrlMapper { get; } = services.GetRequiredService<UrlMapper>();
    private ICommander Commander { get; } = services.Commander();
    private McpSessionAccessor SessionAccessor { get; } = services.GetRequiredService<McpSessionAccessor>();

    private Session Session => SessionAccessor.Session;

    [McpServerTool(Name = "get_me", UseStructuredContent = true)]
    [Description("Returns the caller's own account: user id, display name and default avatar.")]
    public async Task<McpAccount> GetMe(CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
        var avatar = account.Avatar;
        return new McpAccount(
            account.Id.Value,
            avatar.Name,
            avatar.Id.Value,
            avatar.Name,
            avatar.ToMcpPictureUrl(UrlMapper));
    }

    [McpServerTool(Name = "list_avatars", UseStructuredContent = true)]
    [Description("Lists the caller's own avatars. `isDefault` marks the one used in new chats.")]
    public async Task<McpAvatar[]> ListAvatars(CancellationToken cancellationToken)
    {
        var account = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
        var avatarIds = await Avatars.ListOwnAvatarIds(Session, cancellationToken).ConfigureAwait(false);
        var avatars = await Task.WhenAll(avatarIds.Select(id => Avatars.GetOwn(Session, id, cancellationToken)))
            .ConfigureAwait(false);
        return avatars
            .Where(a => a is not null)
            .Select(a => a!.ToMcpModel(account.Avatar.Id, UrlMapper))
            .ToArray();
    }

    [McpServerTool(Name = "create_avatar", UseStructuredContent = true)]
    [Description("Creates a new avatar for the caller. " +
        "`pictureMediaId` comes from finish_upload with purpose 'avatar_picture'.")]
    public async Task<McpAvatar> CreateAvatar(
        [Description("Display name.")] string name,
        [Description("Short bio; empty by default.")] string? bio = null,
        [Description("Media id of an uploaded picture.")] string? pictureMediaId = null,
        CancellationToken cancellationToken = default)
    {
        var account = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
        var template = new AvatarFull(account.Id).WithMissingPropertiesFrom(account.Avatar) with {
            Name = name,
            Bio = bio ?? "",
            MediaId = MediaId.ParseNullable(pictureMediaId),
        };
        var command = new Avatars_Change {
            Session = Session,
            AvatarId = Symbol.Empty,
            ExpectedVersion = null,
            Change = Change.Create(AvatarDiff.FromFull(template)),
        };
        var avatar = await Commander.Call(command, cancellationToken).ConfigureAwait(false);
        return avatar.ToMcpModel(account.Avatar.Id, UrlMapper);
    }

    [McpServerTool(Name = "update_avatar", UseStructuredContent = true)]
    [Description("Updates name, bio and/or picture of one of the caller's avatars. Omitted fields keep their value.")]
    public async Task<McpAvatar> UpdateAvatar(
        [Description("The avatar id.")] string avatarId,
        [Description("New display name.")] string? name = null,
        [Description("New bio.")] string? bio = null,
        [Description("Media id of an uploaded picture.")] string? pictureMediaId = null,
        CancellationToken cancellationToken = default)
    {
        var account = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
        var diff = new AvatarDiff {
            Name = name,
            Bio = bio,
            MediaId = pictureMediaId is null ? default : Option.Some<MediaId?>(MediaId.Parse(pictureMediaId)),
        };
        var command = new Avatars_Change {
            Session = Session,
            AvatarId = avatarId,
            ExpectedVersion = null,
            Change = Change.Update(diff),
        };
        var avatar = await Commander.Call(command, cancellationToken).ConfigureAwait(false);
        return avatar.ToMcpModel(account.Avatar.Id, UrlMapper);
    }

    [McpServerTool(Name = "set_default_avatar", UseStructuredContent = true)]
    [Description("Makes one of the caller's avatars the default one.")]
    public async Task SetDefaultAvatar(
        [Description("The avatar id.")] string avatarId,
        CancellationToken cancellationToken = default)
    {
        var command = new Avatars_SetDefault { Session = Session, AvatarId = avatarId };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }
}
