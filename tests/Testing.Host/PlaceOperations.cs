using ActualChat.Chat;
using ActualChat.Users;

namespace ActualChat.Testing.Host;

public static class PlaceOperations
{
    private const string DefaultPlaceTitle = "test place";

    public static Task<Place> CreatePlace(this IWebTester tester, bool isPublicPlace, string title = DefaultPlaceTitle, params AccountFull[] usersToInvite)
        => CreatePlace(tester, c => c with { IsPublic = isPublicPlace, Title = title }, usersToInvite.OfType<Account>().ToArray());

    public static async Task<Place> CreatePlace(this IWebTester tester, Func<PlaceDiff, PlaceDiff> configure, params Account[] usersToInvite)
    {
        var session = tester.Session;
        var placeDiff = configure(new PlaceDiff() {
            Title = DefaultPlaceTitle,
        });
        var commander = tester.Commander;
        var place = await commander.Call(new Places_Change(session,
            default,
            null,
            new () {
                Create = placeDiff,
            }));
        place.Require();
        if (usersToInvite.Length > 0)
            await tester.InviteToPlace(place.Id, usersToInvite);
        return place;
    }

    public static Task<Place> UpdatePlace(this IWebTester tester, PlaceId placeId, string newTitle)
        => tester.Commander.Call(new Places_Change(tester.Session,
            placeId,
            null,
            Change.Update(new PlaceDiff {
                Title = newTitle,
            })));

    public static Task<Place> DeletePlace(this IWebTester tester, PlaceId placeId)
        => tester.Commander.Call(new Places_Change(tester.Session, placeId, null, Change.Remove<PlaceDiff>()));

    public static async Task InviteToPlace(this IWebTester tester, PlaceId placeId, params UserId[] userIds)
    {
        var session = tester.Session;
        var commander = tester.Commander;
        await commander.Call(new Places_Invite(session, placeId, userIds));
    }

    public static Task InviteToPlace(this IWebTester tester, PlaceId placeId, params Account[] accounts)
        => tester.InviteToPlace(placeId, accounts.Select(x => x.Id).ToArray());
}
