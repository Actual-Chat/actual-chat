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
    public static readonly Emoji Blessed = new("😇", "Smiling face with halo", EmojiGroup.Positive);
    public static readonly Emoji Hugging = new("🤗", "Hugging face", EmojiGroup.Positive);
    public static readonly Emoji ExplodingHead = new("🤯", "Exploding head", EmojiGroup.Positive);
    public static readonly Emoji Party = new("🥳", "Partying face", EmojiGroup.Positive);
    public static readonly Emoji Saluting = new("🫡", "Saluting face", EmojiGroup.Positive);

    // Negative faces - anger, sadness, devil, clown
    public static readonly Emoji Angry = new("😠", "Angry face", EmojiGroup.Negative);
    public static readonly Emoji Sad = new("😥", "Sad but relieved face", EmojiGroup.Negative);
    public static readonly Emoji Crying = new("😭", "Loudly crying face", EmojiGroup.Negative);
    public static readonly Emoji Melting = new("🫠", "Melting face", EmojiGroup.Negative);
    public static readonly Emoji Devil = new("😈", "Smiling face with horns", EmojiGroup.Negative);
    public static readonly Emoji Clown = new("🤡", "Clown face", EmojiGroup.Negative);
    public static readonly Emoji ClownGinger = new("🤡", "ginger-clown", "Ginger clown face", EmojiGroup.Negative);
    public static readonly Emoji Bored = new("😒", "Unamused face", EmojiGroup.Negative);
    public static readonly Emoji Crazy = new("🤪", "Zany face", EmojiGroup.Negative);
    public static readonly Emoji Dead = new("💀", "Skull", EmojiGroup.Negative);
    public static readonly Emoji EyeRoll = new("🙄", "Face with rolling eyes", EmojiGroup.Negative);
    public static readonly Emoji NoWords = new("😶", "Face without mouth", EmojiGroup.Negative);
    public static readonly Emoji Scared = new("😨", "Fearful face", EmojiGroup.Negative);
    public static readonly Emoji Sick = new("🤢", "Nauseated face", EmojiGroup.Negative);
    public static readonly Emoji Sleeping = new("😴", "Sleeping face", EmojiGroup.Negative);

    // Hearts and love
    public static readonly Emoji Love = new("❤️", "Red heart", EmojiGroup.Love);
    public static readonly Emoji InLove = new("😍", "Smiling face with heart-eyes", EmojiGroup.Love);
    public static readonly Emoji BrokenHeart = new("💔", "Broken heart", EmojiGroup.Love);
    public static readonly Emoji KissLips = new("💋", "Kiss mark", EmojiGroup.Love);
    public static readonly Emoji Praying = new("🙏", "Folded hands", EmojiGroup.Love);

    // Gestures and symbols
    public static readonly Emoji ThumbsUp = new("👍", "Thumbs up", EmojiGroup.Gestures);
    public static readonly Emoji ThumbsDown = new("👎", "Thumbs down", EmojiGroup.Gestures);
    public static readonly Emoji Done = new("✅", "Check mark", EmojiGroup.Gestures);
    public static readonly Emoji Eyes = new("👀", "Eyes", EmojiGroup.Gestures);
    public static readonly Emoji Poop = new("💩", "Pile of poo", EmojiGroup.Gestures);
    public static readonly Emoji Surprise = new("😲", "Astonished face", EmojiGroup.Gestures);
    public static readonly Emoji Mysterious = new("🤫", "Shushing face", EmojiGroup.Gestures);
    public static readonly Emoji StoneFaceMoai = new("🗿", "Moai", EmojiGroup.Gestures);
    public static readonly Emoji Banana = new("🍌", "Banana", EmojiGroup.Gestures);
    public static readonly Emoji Cup = new("☕", "Hot beverage", EmojiGroup.Gestures);
    public static readonly Emoji Deal = new("🤝", "Handshake", EmojiGroup.Gestures);
    public static readonly Emoji FuckYou = new("🖕", "Middle finger", EmojiGroup.Gestures);
    public static readonly Emoji Lightning = new("⚡", "Lightning", EmojiGroup.Gestures);
    public static readonly Emoji Ok = new("👌", "OK hand", EmojiGroup.Gestures);
    public static readonly Emoji PeekingEye = new("🫣", "Face with peeking eye", EmojiGroup.Gestures);
    public static readonly Emoji Pill = new("💊", "Pill", EmojiGroup.Gestures);
    public static readonly Emoji RoboKitty = new("🐱", "Robo kitty", EmojiGroup.Gestures);
    public static readonly Emoji Thinking = new("🤔", "Thinking face", EmojiGroup.Gestures);
    public static readonly Emoji Writing = new("✍️", "Writing hand", EmojiGroup.Gestures);
    public static readonly Emoji Fire = new("🔥", "Fire", EmojiGroup.Gestures);
    public static readonly Emoji HundredPoints = new("💯", "Hundred points", EmojiGroup.Gestures);

    // Legacy emojis: no longer shown in the picker, but still parseable from existing reactions
    public static readonly Emoji Boom = new("💥", "Boom", EmojiGroup.Gestures);
    public static readonly Emoji BeamingFace = new("😁", "Beaming face with smiling eyes", EmojiGroup.Positive);
    public static readonly Emoji Cry = new("😢", "Crying face", EmojiGroup.Negative);
    public static readonly Emoji ScreamingFaceInFear = new("😱", "Face screaming in fear", EmojiGroup.Negative);
    public static readonly Emoji Please = new("🥺", "Pleading face", EmojiGroup.Love);
    public static readonly Emoji Clap = new("👏", "Clapping hands", EmojiGroup.Gestures);
    public static readonly Emoji RaisedHands = new("🙌", "Raising hands", EmojiGroup.Gestures);
    public static readonly Emoji Rocket = new("🚀", "Rocket", EmojiGroup.Gestures);
    public static readonly Emoji PartyPopper = new("🎉", "Party popper", EmojiGroup.Gestures);
    public static readonly Emoji JackOLantern = new("🎃", "Jack-o-Lantern", EmojiGroup.Gestures);
    public static readonly Emoji FramedPicture = new("🖼️️", "Framed picture", EmojiGroup.Gestures);
    public static readonly Emoji ClownYellow = new("🤡", "clown-yellow", "Ginger clown face", EmojiGroup.Negative);

    /// <summary>
    /// Legacy emojis that were removed from the picker but may exist in stored reactions.
    /// They are included in <see cref="ById"/> for parsing but excluded from <see cref="All"/>
    /// so they don't appear in the emoji picker UI.
    /// </summary>
    public static readonly Emoji[] Legacy = [
        Boom,
        BeamingFace,
        Cry,
        ScreamingFaceInFear,
        Please,
        Clap,
        RaisedHands,
        Rocket,
        PartyPopper,
        JackOLantern,
        FramedPicture,
        ClownYellow,
    ];

    public static readonly Emoji[] All = [
        // Positive
        Lol,
        Awesome,
        Cool,
        Smile,
        Nerd,
        SmileRotated,
        Kiss,
        Blessed,
        Hugging,
        ExplodingHead,
        Party,
        Saluting,
        // Negative
        Angry,
        Sad,
        Crying,
        Melting,
        Devil,
        Clown,
        ClownGinger,
        Bored,
        Crazy,
        Dead,
        EyeRoll,
        NoWords,
        Scared,
        Sick,
        Sleeping,
        // Love
        Love,
        InLove,
        BrokenHeart,
        KissLips,
        Praying,
        // Gestures
        ThumbsUp,
        ThumbsDown,
        Done,
        Eyes,
        Poop,
        Surprise,
        Mysterious,
        StoneFaceMoai,
        Banana,
        Cup,
        Deal,
        FuckYou,
        Lightning,
        Ok,
        PeekingEye,
        Pill,
        RoboKitty,
        Thinking,
        Writing,
        Fire,
        HundredPoints,
    ];

    public static readonly Dictionary<string, Emoji> ById
        = All.Concat(Legacy).DistinctBy(x => x.Id.Value).ToDictionary(x => x.Id.Value);

    public static readonly Dictionary<string, Emoji> BySymbol
        = All.Concat(Legacy).DistinctBy(x => x.Symbol).ToDictionary(x => x.Symbol);

    public static readonly IReadOnlyDictionary<EmojiGroup, Emoji[]> ByGroup
        = All.GroupBy(x => x.Group).ToDictionary(g => g.Key, g => g.ToArray());

    public static readonly IReadOnlyDictionary<Emoji, string> SvgNames = new Dictionary<Emoji, string> {
        // Positive
        { Lol, "emoji-lol" },
        { Awesome, "emoji-awesome" },
        { Cool, "emoji-cool" },
        { Smile, "emoji-smile" },
        { Nerd, "emoji-nerd" },
        { SmileRotated, "emoji-smile-rotated" },
        { Kiss, "emoji-kiss" },
        { Blessed, "emoji-blessed" },
        { Hugging, "emoji-hugging" },
        { ExplodingHead, "emoji-exploding-head" },
        { Party, "emoji-party" },
        { Saluting, "emoji-saluting" },
        // Negative
        { Angry, "emoji-angry" },
        { Sad, "emoji-sad" },
        { Crying, "emoji-crying" },
        { Melting, "emoji-melting" },
        { Devil, "emoji-devil" },
        { Clown, "emoji-clown" },
        { ClownGinger, "emoji-clown-yellow" },
        { Bored, "emoji-bored" },
        { Crazy, "emoji-crazy" },
        { Dead, "emoji-dead" },
        { EyeRoll, "emoji-eye-roll" },
        { NoWords, "emoji-no-words" },
        { Scared, "emoji-scared" },
        { Sick, "emoji-sick" },
        { Sleeping, "emoji-sleeping" },
        // Love
        { Love, "emoji-love" },
        { InLove, "emoji-in-love" },
        { BrokenHeart, "emoji-broken-heart" },
        { KissLips, "emoji-kiss-lips" },
        { Praying, "emoji-praying" },
        // Gestures
        { ThumbsUp, "emoji-thumbs-up" },
        { ThumbsDown, "emoji-thumbs-down" },
        { Done, "emoji-done" },
        { Eyes, "emoji-eyes" },
        { Poop, "emoji-poop" },
        { Surprise, "emoji-surprise" },
        { Mysterious, "emoji-mysterious" },
        { StoneFaceMoai, "emoji-stone-face-moai" },
        { Banana, "emoji-banana" },
        { Cup, "emoji-cup" },
        { Deal, "emoji-deal" },
        { FuckYou, "emoji-fuck-you" },
        { Lightning, "emoji-lightning" },
        { Ok, "emoji-ok" },
        { PeekingEye, "emoji-peeking-eye" },
        { Pill, "emoji-pill" },
        { RoboKitty, "emoji-robo-kitty" },
        { Thinking, "emoji-thinking" },
        { Writing, "emoji-writing" },
        { Fire, "emoji-fire" },
        { HundredPoints, "emoji-hundred-points" },
        // Legacy
        { Boom, "emoji-fire" },
    };

    public static Emoji[] GetByGroup(EmojiGroup group)
        => ByGroup.TryGetValue(group, out var emojis) ? emojis : [];

    public static Emoji? TryGetByIdOrSymbol(string text)
        => ById.GetValueOrDefault(text) ?? BySymbol.GetValueOrDefault(text);

    public static string? TryGetSvgName(Emoji? emoji)
        => emoji is null ? null : SvgNames.GetValueOrDefault(emoji);

    public static string? TryGetSvgName(string? idOrSymbol)
        => idOrSymbol.IsNullOrEmpty() ? null : TryGetSvgName(TryGetByIdOrSymbol(idOrSymbol));
}
