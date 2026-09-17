using ActualChat.Media;

namespace ActualChat.Chat;

// Deliberately not generic over the suggestion store's key. Generation spends money against an
// external provider, so an endpoint taking a caller-supplied key would need an authorization switch
// that stays exhaustive forever; per-target methods make the permission check unavoidable at the
// call site. Adding places later costs a few more methods, which is cheap insurance.

public interface IImageSuggestions : IComputeService
{
    [ComputeMethod]
    Task<ImageSuggestion?> GetForChat(
        Session session, ChatId chatId, ImageSlot slot, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<Moment?> GetDismissedUntilForChat(
        Session session, ChatId chatId, ImageSlot slot, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<Moment?> GetGenerationStartedAtForChat(
        Session session, ChatId chatId, ImageSlot slot, CancellationToken cancellationToken);

    [CommandHandler]
    Task<ImageSuggestion?> OnGenerateForChat(
        ImageSuggestions_GenerateForChat command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnAcceptForChat(ImageSuggestions_AcceptForChat command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnDismissForChat(ImageSuggestions_DismissForChat command, CancellationToken cancellationToken);
}

// Which image of a piece of content is meant. A chat has a picture; a place has a picture and a
// background. The suggestion store never sees this - it is folded into the opaque key here.
public enum ImageSlot
{
    Picture = 0,
    Background = 1,
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ImageSuggestions_GenerateForChat(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ChatId ChatId,
    [property: DataMember, Key(2)] ImageSlot Slot,
    // null = write a fresh description from the chat; non-null = the owner's edit
    [property: DataMember, Key(3)] string? ImageDescription
) : ISessionCommand<ImageSuggestion?>
{
    // False for the banner's own trigger, true when someone pressed Regenerate
    [DataMember, Key(4)] public bool IsExplicit { get; init; }

    // null = whatever the user picked last, from UserImageStyleSettings
    [DataMember, Key(5)] public ImageStyle? Style { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ImageSuggestions_AcceptForChat(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ChatId ChatId,
    [property: DataMember, Key(2)] ImageSlot Slot
) : ISessionCommand<Unit>;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ImageSuggestions_DismissForChat(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ChatId ChatId,
    [property: DataMember, Key(2)] ImageSlot Slot
) : ISessionCommand<Unit>;
