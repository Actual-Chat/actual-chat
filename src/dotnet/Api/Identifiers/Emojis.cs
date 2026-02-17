namespace ActualChat;

/// <summary>
/// Provides static instances and lookup dictionary for reaction emojis.
/// </summary>
public static class Emojis
{
    public static readonly Emoji ThumbsUp = new("👍", "Thumbs up");
    public static readonly Emoji RedHeart = new("❤️", "Red heart");
    public static readonly Emoji Lol = new("😂", "Face with tears of joy");
    public static readonly Emoji Surprise = new("😲", "Astonished face");
    public static readonly Emoji Sad = new("😥", "Sad but relieved face");
    public static readonly Emoji Angry = new("😠", "Angry face");
    public static readonly Emoji Poo = new("💩", "Pile of poo");
    // public static readonly Emoji OkHand = new("👌", "Ok hand");
    // public static readonly Emoji Fire = new("🔥", "Fire");
    // public static readonly Emoji BeamingFace = new("😁", "Beaming face with smiling eyes");
    // public static readonly Emoji ThumbsDown = new("👎", "Thumbs down");
    // public static readonly Emoji ScreamingFaceInFear = new("😱", "Face screaming in fear");
    // public static readonly Emoji JackOLantern = new("🎃", "Jack-o-Lantern");
    // public static readonly Emoji FramedPicture = new("🖼️️", "Framed picture");
    // public static readonly Emoji PartyPopper = new("🎉", "Party popper");
    // public static readonly Emoji Clap = new("👏", "Clapping hands");
    // public static readonly Emoji ThinkingFace = new("🤔", "Thinking face");
    // public static readonly Emoji RollingEyes = new("🙄", "Face with rolling eyes");
    // public static readonly Emoji Rocket = new("🚀", "Rocket");
    // public static readonly Emoji Eyes = new("👀", "Eyes");
    // public static readonly Emoji HundredPoints = new("💯", "Hundred points");
    // public static readonly Emoji RaisedHands = new("🙌", "Raising hands");
    // public static readonly Emoji FaceWithHeartEyes = new("😍", "Smiling face with heart-eyes");
    // public static readonly Emoji CryingFace = new("😢", "Crying face");
    // public static readonly Emoji Pray = new("🙏", "Folded hands");
    // public static readonly Emoji StarStruck = new("🤩", "Star-struck");

    public static readonly Emoji[] All = [
        ThumbsUp,
        RedHeart,
        Lol,
        Surprise,
        Sad,
        Angry,
        Poo,
        // OkHand,
        // Fire,
        // BeamingFace,
        // ThumbsDown,
        // ScreamingFaceInFear,
        // JackOLantern
        // FramedPicture
        // PartyPopper,
        // Clap,
        // ThinkingFace,
        // RollingEyes,
        // Rocket,
        // Eyes,
        // HundredPoints,
        // RaisedHands,
        // FaceWithHeartEyes,
        // CryingFace,
        // Pray,
        // StarStruck,
    ];

    public static readonly Dictionary<string, Emoji> ById
        = All.ToDictionary(x => x.Id.Value, StringComparer.Ordinal);
}
