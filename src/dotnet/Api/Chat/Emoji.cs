using ActualLab.Fusion.Blazor;

namespace ActualChat.Chat;

[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed record Emoji(Symbol Id, string Name) : IHasId<Symbol>, IRequirementTarget
{
    public static readonly Emoji None = new(Symbol.Empty, "");

    public static readonly Emoji ThumbsUp = new("👍", "thumbs up");
    public static readonly Emoji RedHeart = new("❤️", "red heart");
    public static readonly Emoji Lol = new("😂", "face with tears of joy");
    public static readonly Emoji Surprise = new("😲", "astonished face");
    public static readonly Emoji Sad = new("😥", "sad but relieved face");
    public static readonly Emoji Angry = new("😠", "angry face");
    public static readonly Emoji Poo = new("💩", "pile of poo");
    public static readonly Emoji OkHand = new("👌", "ok hand");
    public static readonly Emoji Fire = new("🔥", "fire");
    public static readonly Emoji BeamingFace = new("😁", "beaming face with smiling eyes");
    public static readonly Emoji ThumbsDown = new("👎", "thumbs down");
    public static readonly Emoji ScreamingFaceInFear = new("😱", "face screaming in fear");
    public static readonly Emoji JackOLantern = new("🎃", "jack-o-lantern");
    public static readonly Emoji FramedPicture = new("🖼️️", "frame with picture");

    private static readonly Dictionary<Symbol, Emoji> _all = new[] {
        ThumbsUp,
        RedHeart,
        Lol,
        Surprise,
        Sad,
        Angry,
    }.ToDictionary(x => x.Id);

    public static readonly Requirement<Emoji> MustExist = Requirement.New(
        (Emoji? e) => e != null && _all.ContainsKey(e.Id),
        new(() => StandardError.NotFound<Emoji>()));

    public static readonly IReadOnlyCollection<Emoji> All = _all.Values;

    public static Emoji Get(Symbol code) => _all.GetValueOrDefault(code) ?? None;

    public static implicit operator Symbol(Emoji emoji) => emoji.Id;
    public static implicit operator string(Emoji emoji) => emoji.Id;
    public static implicit operator Emoji(Symbol code) => _all.GetValueOrDefault(code, None);
    public static implicit operator Emoji(string code) => _all.GetValueOrDefault(code, None);

    // Equality is based on Id
    public bool Equals(Emoji? other) => !ReferenceEquals(null, other) && Id.Equals(other.Id);
    public override int GetHashCode() => Id.GetHashCode();
}
