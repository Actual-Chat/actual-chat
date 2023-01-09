﻿using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Stl.Versioning;
namespace ActualChat.Chat.Db;

[Table("ChatEntries")]
[Index(nameof(ChatId), nameof(Kind), nameof(LocalId), IsUnique = true)]
[Index(nameof(ChatId), nameof(Kind), nameof(IsRemoved), nameof(LocalId))] // For GetEntryCount queries
[Index(nameof(ChatId), nameof(Kind), nameof(BeginsAt), nameof(EndsAt))]
[Index(nameof(ChatId), nameof(Kind), nameof(EndsAt), nameof(BeginsAt))]
[Index(nameof(ChatId), nameof(Kind), nameof(Version))]
public class DbChatEntry : IHasId<string>, IHasVersion<long>, IRequirementTarget
{
    private static ITextSerializer<SystemEntry> SystemEntrySerializer { get; } =
        SystemJsonSerializer.Default.ToTyped<SystemEntry>();
    private DateTime _beginsAt;
    private DateTime? _clientSideBeginsAt;
    private DateTime? _endsAt;
    private DateTime? _contentEndsAt;

    public DbChatEntry() { }
    public DbChatEntry(ChatEntry model) => UpdateFrom(model);

    // (ChatId, Type, Id)
    [Key] public string Id { get; set; } = "";
    [ConcurrencyCheck] public long Version { get; set; }
    public string ChatId { get; set; } = "";
    public long LocalId { get; set; }

    public bool IsRemoved { get; set; }
    public string AuthorId { get; set; } = null!;
    public long? RepliedChatEntryId { get; set; }
    public bool IsSystemEntry { get; set; }


    public DateTime BeginsAt {
        get => _beginsAt.DefaultKind(DateTimeKind.Utc);
        set => _beginsAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime? ClientSideBeginsAt {
        get => _clientSideBeginsAt?.DefaultKind(DateTimeKind.Utc);
        set => _clientSideBeginsAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime? EndsAt {
        get => _endsAt?.DefaultKind(DateTimeKind.Utc);
        set => _endsAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public DateTime? ContentEndsAt {
        get => _contentEndsAt?.DefaultKind(DateTimeKind.Utc);
        set => _contentEndsAt = value.DefaultKind(DateTimeKind.Utc);
    }

    public double Duration { get; set; }

    public ChatEntryKind Kind { get; set; }
    public string Content { get; set; } = "";
    public bool HasAttachments { get; set; }
    public bool HasReactions { get; set; }
    public string? StreamId { get; set; }

    public long? AudioEntryId { get; set; }
    public long? VideoEntryId { get; set; }
    public string? TextToTimeMap { get; set; }

    public ChatEntry ToModel(IEnumerable<TextEntryAttachment>? attachments = null)
    {
        var chatId = new ChatId(ChatId);
        var id = new ChatEntryId(Id, chatId, Kind, LocalId, AssumeValid.Option);
        return new (id, Version) {
            IsRemoved = IsRemoved,
            AuthorId = new AuthorId(AuthorId),
            BeginsAt = BeginsAt,
            ClientSideBeginsAt = ClientSideBeginsAt,
            EndsAt = EndsAt,
            ContentEndsAt = ContentEndsAt,
            Content = !IsSystemEntry ? Content : "",
            SystemEntry = IsSystemEntry ? SystemEntrySerializer.Read(Content) : null,
            HasReactions = HasReactions,
            StreamId = StreamId ?? "",
            AudioEntryId = AudioEntryId,
            VideoEntryId = VideoEntryId,
            RepliedEntryLocalId = RepliedChatEntryId!,
            Attachments = attachments?.ToImmutableArray() ?? ImmutableArray<TextEntryAttachment>.Empty,
#pragma warning disable IL2026
            TextToTimeMap = Kind == ChatEntryKind.Text
                ? TextToTimeMap != null
                    ? JsonSerializer.Deserialize<LinearMap>(TextToTimeMap)
                    : default
                : default,
#pragma warning restore IL2026
        };
    }

    public void UpdateFrom(ChatEntry model)
    {
        var id = model.Id;
        this.RequireSameOrEmptyId(id);
        model.RequireSomeVersion();

        Id = id;
        ChatId = model.ChatId;
        Kind = model.Kind;
        LocalId = model.LocalId;
        Version = model.Version;
        IsRemoved = model.IsRemoved;

        AuthorId = model.AuthorId;
        BeginsAt = model.BeginsAt;
        ClientSideBeginsAt = model.ClientSideBeginsAt;
        EndsAt = model.EndsAt;
        ContentEndsAt = model.ContentEndsAt;
        Duration = EndsAt.HasValue ? (EndsAt.GetValueOrDefault() - BeginsAt).TotalSeconds : 0;
        HasReactions = model.HasReactions;
        StreamId = model.StreamId;
        AudioEntryId = model.AudioEntryId;
        VideoEntryId = model.VideoEntryId;
        RepliedChatEntryId = model.RepliedEntryLocalId;
        Content = model.SystemEntry != null ? SystemEntrySerializer.Write(model.SystemEntry) : model.Content;
        IsSystemEntry = model.SystemEntry != null;
#pragma warning disable IL2026
        TextToTimeMap = !model.TextToTimeMap.IsEmpty
            ? JsonSerializer.Serialize(model.TextToTimeMap)
            : null;
#pragma warning restore IL2026
    }

    internal class EntityConfiguration : IEntityTypeConfiguration<DbChatEntry>
    {
        public void Configure(EntityTypeBuilder<DbChatEntry> builder)
            => builder.Property(x => x.AuthorId).IsRequired();
    }
}
