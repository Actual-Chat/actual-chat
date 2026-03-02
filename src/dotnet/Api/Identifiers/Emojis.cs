namespace ActualChat;

/// <summary>
/// Provides static instances and lookup dictionary for reaction emojis.
/// </summary>
public static class Emojis
{
    // Positive faces - laughter, smirks, happy
    public static readonly Emoji Lol = new("😂", "Face with tears of joy", EmojiGroup.Positive);
    public static readonly Emoji Awesome = new("🤩", "Star-struck", EmojiGroup.Positive);
    public static readonly Emoji Cool = new("😎", "Smiling face with sunglasses", EmojiGroup.Positive);
    public static readonly Emoji Smile = new("😊", "Smiling face", EmojiGroup.Positive);
    public static readonly Emoji Nerd = new("🤓", "Nerd face", EmojiGroup.Positive);
    public static readonly Emoji SmileRotated = new("🙃", "Upside-down face", EmojiGroup.Positive);
    public static readonly Emoji Kiss = new("😘", "Face blowing a kiss", EmojiGroup.Positive);

    // Negative faces - anger, sadness, devil, clown
    public static readonly Emoji Angry = new("😠", "Angry face", EmojiGroup.Negative);
    public static readonly Emoji Sad = new("😥", "Sad but relieved face", EmojiGroup.Negative);
    public static readonly Emoji Cry = new("😢", "Crying face", EmojiGroup.Negative);
    public static readonly Emoji Melt = new("🫠", "Melting face", EmojiGroup.Negative);
    public static readonly Emoji Devil = new("😈", "Smiling face with horns", EmojiGroup.Negative);
    public static readonly Emoji Clown = new("🤡", "Clown face", EmojiGroup.Negative);

    // Hearts and love
    public static readonly Emoji RedHeart = new("❤️", "Red heart", EmojiGroup.Love);
    public static readonly Emoji InLove = new("😍", "Smiling face with heart-eyes", EmojiGroup.Love);
    public static readonly Emoji BrokenHeart = new("💔", "Broken heart", EmojiGroup.Love);
    public static readonly Emoji Please = new("🥺", "Pleading face", EmojiGroup.Love);

    // Gestures and symbols
    public static readonly Emoji ThumbsUp = new("👍", "Thumbs up", EmojiGroup.Gestures);
    public static readonly Emoji Done = new("✅", "Check mark", EmojiGroup.Gestures);
    public static readonly Emoji Eyes = new("👀", "Eyes", EmojiGroup.Gestures);
    public static readonly Emoji Poo = new("💩", "Pile of poo", EmojiGroup.Gestures);
    public static readonly Emoji Boom = new("💥", "Boom", EmojiGroup.Gestures);
    public static readonly Emoji Surprise = new("😲", "Astonished face", EmojiGroup.Gestures);
    public static readonly Emoji Mysterious = new("🤫", "Shushing face", EmojiGroup.Gestures);
    public static readonly Emoji Stone = new("🗿", "Moai", EmojiGroup.Gestures);

    public static readonly Emoji[] All = [
        // Positive
        Lol,
        Awesome,
        Cool,
        Smile,
        Nerd,
        SmileRotated,
        Kiss,
        // Negative
        Angry,
        Sad,
        Cry,
        Melt,
        Devil,
        Clown,
        // Love
        RedHeart,
        InLove,
        BrokenHeart,
        Please,
        // Gestures
        ThumbsUp,
        Done,
        Eyes,
        Poo,
        Boom,
        Surprise,
        Mysterious,
        Stone,
    ];

    public static readonly Dictionary<string, Emoji> ById
        = All.ToDictionary(x => x.Id.Value, StringComparer.Ordinal);

    public static readonly IReadOnlyDictionary<EmojiGroup, Emoji[]> ByGroup
        = All.GroupBy(x => x.Group).ToDictionary(g => g.Key, g => g.ToArray());

    public static Emoji[] GetByGroup(EmojiGroup group)
        => ByGroup.TryGetValue(group, out var emojis) ? emojis : [];
}
