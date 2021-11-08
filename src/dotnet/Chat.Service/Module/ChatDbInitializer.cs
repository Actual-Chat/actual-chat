using System.Buffers;
using System.Text.Json;
using ActualChat.Audio;
using ActualChat.Blobs;
using ActualChat.Chat.Db;
using ActualChat.Db;
using ActualChat.Mathematics;
using ActualChat.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IO;
using Stl.Versioning;

namespace ActualChat.Chat.Module;

public class ChatDbInitializer : DbInitializer<ChatDbContext>
{
    private readonly IBlobStorageProvider _blobStorageProvider;
    private static readonly RecyclableMemoryStreamManager MemoryStreamManager = new();
    public ChatDbInitializer(IServiceProvider services, IBlobStorageProvider blobStorageProvider) : base(services)
        => _blobStorageProvider = blobStorageProvider;

    public override async Task Initialize(CancellationToken cancellationToken)
    {
        await base.Initialize(cancellationToken).ConfigureAwait(false);
        var dependencies = InitializeTasks
            .Where(kv => kv.Key.GetType().Name.StartsWith("Users", StringComparison.Ordinal))
            .Select(kv => kv.Value)
            .ToArray();
        await Task.WhenAll(dependencies).ConfigureAwait(false);

        var dbContextFactory = Services.GetRequiredService<IDbContextFactory<ChatDbContext>>();
        var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);
        await using var _ = dbContext.ConfigureAwait(false);

        if (ShouldRecreateDb) {
            // Creating "The Actual One" chat
            var defaultChatId = ChatConstants.DefaultChatId;
            var adminUserId = UserConstants.Admin.UserId;
            var dbChat = new DbChat() {
                Id = defaultChatId,
                Version = VersionGenerator.NextVersion(),
                Title = "The Actual One",
                CreatedAt = Clocks.SystemClock.Now,
                IsPublic = true,
                Owners = {
                        new DbChatOwner() {
                            ChatId = defaultChatId,
                            UserId = adminUserId,
                        },
                    },
            };
            await dbContext.Chats.AddAsync(dbChat, cancellationToken).ConfigureAwait(false);

            var dbAuthor = new DbChatAuthor() {
                Id = DbChatAuthor.ComposeId(defaultChatId, 1),
                ChatId = defaultChatId,
                LocalId = 1,
                Version = VersionGenerator.NextVersion(),
                Name = UserConstants.Admin.Name,
                Picture = UserConstants.Admin.Picture,
                IsAnonymous = false,
                UserId = adminUserId,
            };
            await dbContext.ChatAuthors.AddAsync(dbAuthor, cancellationToken).ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Uncomment this if you need just random text messages
            // await GetChatWithRandomMessages(dbContext, dbChat, dbAuthor, VersionGenerator, Clocks, cancellationToken).ConfigureAwait(false);

            // Uncomment this if you need initial audio and text data
            await GetChatWithAudioRecords(dbContext, dbChat, dbAuthor, VersionGenerator, Clocks, _blobStorageProvider, cancellationToken).ConfigureAwait(false);

            static async Task GetChatWithAudioRecords(
                DbContext dbContext,
                DbChat dbChat,
                DbChatAuthor dbAuthor,
                VersionGenerator<long> versionGenerator,
                MomentClockSet clocks,
                IBlobStorageProvider blobStorageProvider,
                CancellationToken cancellationToken)
            {
                var audioEntry = new DbChatEntry {
                    CompositeId = "the-actual-one:131",
                    AuthorId = dbAuthor.Id,
                    AudioEntryId = null,
                    BeginsAt = new Moment(DateTime.Parse("2021-11-05 16:41:18.504314 +00:00",
                        NumberFormatInfo.InvariantInfo)),
                    ChatId = dbChat.Id,
                    Content = "audio-record/01FKJ8FKQ9K5X84XQY3F7YN7NS/0000.webm",
                    EndsAt = new Moment(DateTime.Parse("2021-11-05 16:41:29.543314 +00:00",
                        NumberFormatInfo.InvariantInfo)),
                    Id = 131,
                    Type = ChatEntryType.Audio,
                    Version = 16359216898269180,
                };
                await dbContext.AddAsync(audioEntry, cancellationToken).ConfigureAwait(false);

                var textToTimeMap =
                    "{\"SourcePoints\":[0,4,18,20,25,27,37,46,53,57,64,74,81,93,98],\"TargetPoints\":[0,1.8,2.4,3.2,3.4,4.2,4.3,5.4,5.5,6.9,7.4,7.6,8.9,9.9,10.5]}";
                var textEntry = new DbChatEntry {
                    CompositeId = "the-actual-one:132",
                    AuthorId = dbAuthor.Id,
                    AudioEntryId = 131,
                    BeginsAt = new Moment(DateTime.Parse("2021-11-05 16:41:18.504314 +00:00",
                        NumberFormatInfo.InvariantInfo)),
                    ChatId = dbChat.Id,
                    Content =
                        "Мой друг художник и поэт в Дождливый вечер на стекле мою любовь нарисовал открыв мне чудо на Земле",
                    EndsAt = new Moment(DateTime.Parse("2021-11-05 16:41:29.004314 +00:00",
                        NumberFormatInfo.InvariantInfo)),
                    Id = 132,
                    TextToTimeMap = textToTimeMap,
                    Type = ChatEntryType.Text,
                    Version = 16359216898897618,
                };
                await dbContext.AddAsync(textEntry, cancellationToken).ConfigureAwait(false);

                audioEntry = new DbChatEntry {
                    CompositeId = "the-actual-one:141",
                    AuthorId = dbAuthor.Id,
                    AudioEntryId = null,
                    BeginsAt = new Moment(DateTime.Parse("2021-11-05 17:26:05.671804 +00:00",
                        NumberFormatInfo.InvariantInfo)),
                    ChatId = dbChat.Id,
                    Content = "audio-record/01FKRJ5P2C87TYP1V3JTNB228D/0000.webm",
                    EndsAt = new Moment(DateTime.Parse("2021-11-05 17:26:16.710804 +00:00",
                        NumberFormatInfo.InvariantInfo)),
                    Id = 141,
                    Type = ChatEntryType.Audio,
                    Version = 16361331765465404,
                };
                await dbContext.AddAsync(audioEntry, cancellationToken).ConfigureAwait(false);

                textToTimeMap =
                    "{\"SourcePoints\":[0,5,31,35,53,63,69,76,82,119,121,126],\"TargetPoints\":[0,1.4,3,3.6,4.8,5.3,6,6.3,7,9.5,9.5,10.53]}";
                textEntry = new DbChatEntry {
                    CompositeId = "the-actual-one:142",
                    AuthorId = dbAuthor.Id,
                    AudioEntryId = 141,
                    BeginsAt = new Moment(DateTime.Parse("2021-11-05 17:26:05.745700 +00:00",
                        NumberFormatInfo.InvariantInfo)),
                    ChatId = dbChat.Id,
                    Content =
                        "утро в декабре туманом окутана под ногами белый снег предатель виден каждый шаг и холоду лютому слишком просто сладить с тобой",
                    EndsAt = new Moment(DateTime.Parse("2021-11-05 17:26:16.275700 +00:00",
                        NumberFormatInfo.InvariantInfo)),
                    Id = 142,
                    TextToTimeMap = textToTimeMap,
                    Type = ChatEntryType.Text,
                    Version = 16361331767501582,
                };
                await dbContext.AddAsync(textEntry, cancellationToken).ConfigureAwait(false);
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                await CreateInitialData(blobStorageProvider, "0000.webm", "audio-record/01FKJ8FKQ9K5X84XQY3F7YN7NS/0000.webm", cancellationToken).ConfigureAwait(false);
                await CreateInitialData(blobStorageProvider, "0001.webm", "audio-record/01FKRJ5P2C87TYP1V3JTNB228D/0000.webm", cancellationToken).ConfigureAwait(false);

                static async Task CreateInitialData(
                    IBlobStorageProvider blobStorageProvider,
                    string fileName,
                    string blobId,
                    CancellationToken cancellationToken)
                {
                    var audioSourceProvider = new AudioSourceProvider();
                    var blobChannel = Channel.CreateUnbounded<BlobPart>();
                    var audioSourceTask =
                        audioSourceProvider.CreateMediaSource(blobChannel.Reader, TimeSpan.Zero, CancellationToken.None).ConfigureAwait(false);

                    var readFromFile = ReadFromFile(blobChannel.Writer, fileName);
                    var audioSource = await audioSourceTask;
                    await SaveBlob(blobStorageProvider, audioSource, blobId, CancellationToken.None).ConfigureAwait(false);
                }

                static async Task<int> ReadFromFile(ChannelWriter<BlobPart> writer, string fileName)
                {
                    var size = 0;
                    var pathToAudioFiles = Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, @"..\..\..\"));
                    await using var inputStream = new FileStream(
                        Path.Combine(pathToAudioFiles, @"tests\Audio.IntegrationTests\data", fileName),
                        FileMode.Open,
                        FileAccess.Read);
                    using var readBufferLease = MemoryPool<byte>.Shared.Rent(1 * 1024);
                    var readBuffer = readBufferLease.Memory;
                    var index = 0;
                    var bytesRead = await inputStream.ReadAsync(readBuffer);
                    while (bytesRead < 1 * 1024)
                        bytesRead += await inputStream.ReadAsync(readBuffer[bytesRead..]);
                    size += bytesRead;
                    while (bytesRead > 0) {
                        var command = new BlobPart(
                            index++,
                            readBuffer[..bytesRead].ToArray());
                        await writer.WriteAsync(command, CancellationToken.None);

                        bytesRead = await inputStream.ReadAsync(readBuffer);
                        size += bytesRead;
                    }

                    writer.Complete();
                    return size;
                }

                static async Task SaveBlob(
                    IBlobStorageProvider blobStorageProvider,
                    AudioSource source,
                    string blobId,
                    CancellationToken cancellationToken)
                {
                    var blobStorage = blobStorageProvider.GetBlobStorage(BlobScope.AudioRecord);
                    await using var stream = MemoryStreamManager.GetStream(nameof(ChatDbInitializer));
                    var header = Convert.FromBase64String(source.Format.CodecSettings);
                    await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
                    await foreach (var audioFrame in source.Frames.WithCancellation(cancellationToken))
                        await stream.WriteAsync(audioFrame.Data, cancellationToken).ConfigureAwait(false);

                    stream.Position = 0;
                    await blobStorage.WriteAsync(blobId, stream, append: false, cancellationToken).ConfigureAwait(false);
                }
            }

            static async Task GetChatWithRandomMessages(
                DbContext dbContext,
                DbChat dbChat,
                DbChatAuthor dbAuthor,
                VersionGenerator<long> versionGenerator,
                MomentClockSet clocks,
                CancellationToken cancellationToken)
            {
                var words = new[] { "most", "chat", "actual", "ever", "amazing", "absolutely" };
                for (var id = 0; id < 96; id++) {
                    var dbChatEntry = new DbChatEntry() {
                        CompositeId = DbChatEntry.GetCompositeId(dbChat.Id, id),
                        ChatId = dbChat.Id,
                        Id = id,
                        // Version = VersionGenerator.NextVersion(),
                        Version = versionGenerator.NextVersion(),
                        BeginsAt = clocks.SystemClock.Now,
                        EndsAt = clocks.SystemClock.Now,
                        Type = ChatEntryType.Text,
                        Content = GetRandomSentence(30),
                        AuthorId = dbAuthor.Id,
                    };
                    if (id == 0)
                        dbChatEntry.Content = "First";
                    dbContext.Add(dbChatEntry);
                }
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

                string GetRandomSentence(int maxLength)
                    => Enumerable
                        .Range(0, Random.Shared.Next(maxLength))
                        .Select(_ => words![Random.Shared.Next(words.Length)])
                        .ToDelimitedString(" ");
            }
        }
    }
}
