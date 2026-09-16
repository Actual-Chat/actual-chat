using System.ComponentModel;
using ActualChat.Invite;
using ActualChat.Mcp.Auth;
using ModelContextProtocol.Server;

namespace ActualChat.Mcp.Tools;

[McpServerToolType]
public sealed class McpPlaceTools(IServiceProvider services)
{
    private const int DefaultLimit = 256;

    private IPlaces Places { get; } = services.GetRequiredService<IPlaces>();
    private IAuthors Authors { get; } = services.GetRequiredService<IAuthors>();
    private IInvites Invites { get; } = services.GetRequiredService<IInvites>();
    private UrlMapper UrlMapper { get; } = services.GetRequiredService<UrlMapper>();
    private ICommander Commander { get; } = services.Commander();
    private McpSessionAccessor SessionAccessor { get; } = services.GetRequiredService<McpSessionAccessor>();

    private Session Session => SessionAccessor.Session;

    [McpServerTool(Name = "get_place", UseStructuredContent = true)]
    [Description("Returns full info about a place: title, description, picture, background, " +
        "member count and the caller's permissions.")]
    public async Task<McpPlaceDetails> GetPlace(
        [Description("The place id.")] string placeId,
        CancellationToken cancellationToken = default)
    {
        var place = await Places.Get(Session, PlaceId.Parse(placeId), cancellationToken)
            .Require()
            .ConfigureAwait(false);
        return await ToDetails(place, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "create_place", UseStructuredContent = true)]
    [Description("Creates a place. The caller becomes its owner. " +
        "Pictures come from finish_upload with purpose 'chat_picture'.")]
    public async Task<McpPlaceDetails> CreatePlace(
        [Description("Place title.")] string title,
        [Description("True for a public place.")] bool isPublic,
        [Description("Place description.")] string? description = null,
        [Description("Media id of the place picture.")] string? pictureMediaId = null,
        [Description("Media id of the place background.")] string? backgroundMediaId = null,
        CancellationToken cancellationToken = default)
    {
        var diff = new PlaceDiff {
            Title = title,
            IsPublic = isPublic,
            Description = description,
            MediaId = MediaId.ParseNullable(pictureMediaId),
            BackgroundMediaId = MediaId.ParseNullable(backgroundMediaId),
        };
        var command = new Places_Change {
            Session = Session,
            PlaceId = default,
            ExpectedVersion = null,
            Change = Change.Create(diff),
        };
        var place = await Commander.Call(command, cancellationToken).ConfigureAwait(false);
        return await ToDetails(place, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "update_place", UseStructuredContent = true)]
    [Description("Updates a place's title, description, visibility, picture and/or background. " +
        "Omitted fields keep their value.")]
    public async Task<McpPlaceDetails> UpdatePlace(
        [Description("The place id.")] string placeId,
        [Description("New title.")] string? title = null,
        [Description("New description.")] string? description = null,
        [Description("New visibility.")] bool? isPublic = null,
        [Description("Media id of the place picture.")] string? pictureMediaId = null,
        [Description("Media id of the place background.")] string? backgroundMediaId = null,
        CancellationToken cancellationToken = default)
    {
        var diff = new PlaceDiff {
            Title = title,
            Description = description,
            IsPublic = isPublic,
            MediaId = MediaId.ParseNullable(pictureMediaId),
            BackgroundMediaId = MediaId.ParseNullable(backgroundMediaId),
        };
        var command = new Places_Change {
            Session = Session,
            PlaceId = PlaceId.Parse(placeId),
            ExpectedVersion = null,
            Change = Change.Update(diff),
        };
        var place = await Commander.Call(command, cancellationToken).ConfigureAwait(false);
        return await ToDetails(place, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "list_place_members", UseStructuredContent = true)]
    [Description("Lists place members. `userId` is null for anonymous members. " +
        "`afterId` is an exclusive author id; `limit` is capped at 1024.")]
    public async Task<McpListMembersResult> ListPlaceMembers(
        [Description("The place id.")] string placeId,
        [Description("Return members with author id > afterId.")] string? afterId = null,
        [Description("Max members to return; capped at 1024.")] int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var parsedPlaceId = PlaceId.Parse(placeId);
        var rootChatId = parsedPlaceId.RootChatId;
        var authorIds = await Places.ListAuthorIds(Session, parsedPlaceId, cancellationToken).ConfigureAwait(false);
        var ownerIds = await Places.ListOwnerIds(Session, parsedPlaceId, cancellationToken).ConfigureAwait(false);
        var page = McpChatTools.Page(authorIds.OrderBy(id => id.Value).ToArray(), afterId, limit, id => id.Value);
        var members = await page
            .ToMcpMembers(
                id => Places.Get(Session, parsedPlaceId, id, cancellationToken),
                id => Authors.GetAccount(Session, rootChatId, id, cancellationToken),
                ownerIds.ToHashSet())
            .ConfigureAwait(false);
        return new McpListMembersResult(members);
    }

    [McpServerTool(Name = "add_place_members", UseStructuredContent = true)]
    [Description("Adds users to a place directly (requires the Invite permission).")]
    public async Task AddPlaceMembers(
        [Description("The place id.")] string placeId,
        [Description("User ids to add.")] string[] userIds,
        CancellationToken cancellationToken = default)
    {
        var command = new Places_Invite {
            Session = Session,
            PlaceId = PlaceId.Parse(placeId),
            UserIds = userIds.Select(UserId.Parse).ToArray(),
        };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "remove_place_member", UseStructuredContent = true)]
    [Description("Removes a member from a place by author id (requires the EditMembers permission).")]
    public async Task RemovePlaceMember(
        [Description("The place id.")] string placeId,
        [Description("The member's author id (from list_place_members).")] string authorId,
        CancellationToken cancellationToken = default)
    {
        var parsedAuthorId = AuthorId.Parse(authorId);
        if (parsedAuthorId.ChatId != PlaceId.Parse(placeId).RootChatId)
            throw StandardError.Constraint("authorId does not belong to placeId.");

        var command = new Places_Exclude { Session = Session, AuthorId = parsedAuthorId };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "create_place_invite_link", UseStructuredContent = true)]
    [Description("Returns the place's current invite link, generating one if none is active.")]
    public async Task<McpInviteLink> CreatePlaceInviteLink(
        [Description("The place id.")] string placeId,
        CancellationToken cancellationToken = default)
    {
        var invite = await Invites.GetOrGeneratePlaceInvite(Session, PlaceId.Parse(placeId), cancellationToken)
            .Require()
            .ConfigureAwait(false);
        return invite.ToMcpModel(UrlMapper);
    }

    [McpServerTool(Name = "list_place_invite_links", UseStructuredContent = true)]
    [Description("Lists the place's active invite links. Revoke with revoke_invite_link.")]
    public async Task<McpInviteLink[]> ListPlaceInviteLinks(
        [Description("The place id.")] string placeId,
        CancellationToken cancellationToken = default)
    {
        var invites = await Invites.ListPlaceInvites(Session, PlaceId.Parse(placeId), cancellationToken)
            .ConfigureAwait(false);
        return invites.Select(i => i.ToMcpModel(UrlMapper)).ToArray();
    }

    // Private methods

    private async Task<McpPlaceDetails> ToDetails(Place place, CancellationToken cancellationToken)
    {
        // The command result may carry no Rules; re-read through the session to get the caller's permissions
        place = await Places.Get(Session, place.Id, cancellationToken).Require().ConfigureAwait(false);
        var authorIds = await Places.ListAuthorIds(Session, place.Id, cancellationToken).ConfigureAwait(false);
        return place.ToMcpDetails(authorIds.Length, UrlMapper);
    }
}
