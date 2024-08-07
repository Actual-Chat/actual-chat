using ActualChat.Chat;
using ActualChat.Users;
using ActualLab.Generators;

namespace ActualChat.MLSearch.UnitTests.Indexing.ChatContent;

public enum AuthorNick { Alice, Bob, Clark }

public class EntryProto(AuthorNick author, string content)
{
    public AuthorNick Author { get; } = author;
    public string Content { get; } = content;
    public int? TimestampOffset { get; set; }
}

public static class ChatContentArranger2Utils
{
    private static readonly RandomStringGenerator AvatarIdGenerator = new(10, Alphabet.AlphaNumeric);

    public static readonly EntryProto[] Messages = [
        new (AuthorNick.Alice, "Hello"),
        new (AuthorNick.Bob, "Hi!"),
        new (AuthorNick.Alice, "Does anybody know how to repair washing machine?"),

        new (AuthorNick.Bob, "What a beautiful picture. Just take a look at it."),
        new (AuthorNick.Clark, "Agree with you Bob, it looks amazing."),

        new (AuthorNick.Bob, "Check in Walmart. There should be a large selection of household appliances."),
    ];

    public static IReadOnlyDictionary<AuthorNick, AuthorFull> CreateAuthors(IEnumerable<EntryProto> messages)
    {
        var chatId = new ChatId(Generate.Option);
        var authors = new Dictionary<AuthorNick, AuthorFull>();
        foreach (var msg in messages) {
            var authorNick = msg.Author;
            if (!authors.TryGetValue(authorNick, out var author)) {
                var authorId = new AuthorId(chatId, authors.Count + 1, AssumeValid.Option);
                var avatarId = AvatarIdGenerator.Next();
                author = new AuthorFull(authorId, 1) {
                    AvatarId = avatarId,
                    Avatar = new Avatar(avatarId, 1) {
                        Name = authorNick.ToString()
                    },
                };
                authors.Add(authorNick, author);
            }
        }
        return authors;
    }

    public static IAuthorsBackend CreateAuthorsBackend(IEnumerable<AuthorFull> authors)
    {
        var mock = new Mock<IAuthorsBackend>();
        mock
            .Setup(c => c.Get(
                It.IsAny<ChatId>(),
                It.IsAny<AuthorId>(),
                It.IsAny<AuthorsBackend_GetAuthorOption>(),
                It.IsAny<CancellationToken>()))
            .Returns<ChatId, AuthorId, AuthorsBackend_GetAuthorOption, CancellationToken>((_, aId, _, _) => {
                var author = authors.FirstOrDefault(x => x.Id == aId);
                return Task.FromResult(author);
            });
        return mock.Object;
    }

    public static IEnumerable<ChatEntry> CreateEntries(IEnumerable<EntryProto> messages, IReadOnlyDictionary<AuthorNick, AuthorFull> authors)
    {
        var chatId = new ChatId(Generate.Option);
        var localId = 1L;
        var version = DateTime.Now.Ticks;
        var beginsAt = new DateTime(2024, 5, 1, 13, 0 ,0);
        foreach (var msg in messages) {
            if (msg.TimestampOffset.HasValue)
                beginsAt = beginsAt.AddSeconds(msg.TimestampOffset.Value);
            else if (localId > 1)
                beginsAt = beginsAt.AddSeconds(1);
            var authorNick = msg.Author;
            var content = msg.Content;
            if (!authors.TryGetValue(authorNick, out var author))
                throw StandardError.Constraint("No author");

            var entryId = new ChatEntryId(chatId, ChatEntryKind.Text, localId++, AssumeValid.Option);
            yield return new ChatEntry(entryId, version++) {
                BeginsAt = beginsAt,
                AuthorId = author.Id,
                Content = content,
            };
        }
    }
}
