using ActualChat.Contacts;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public abstract class AuthorBadgeBase : ComputedStateComponent<AuthorBadgeBase.Model>
{
    [Inject] protected ChatUIHub Hub { get; init; } = null!;
    protected Session Session => Hub.Session();
    protected IAuthors Authors => Hub.Authors;
    protected AuthorUI AuthorUI => Hub.AuthorUI;
    protected IContacts Contacts => Hub.Contacts;

    protected AuthorId AuthorId { get; private set; }
    protected ChatId ChatId => AuthorId.ChatId;

    [Parameter, EditorRequired] public string AuthorSid { get; set; } = "";

    protected override void OnInitialized()
        // Set AuthorId here to have actual AuthorId value in GetStateOptions.
        => AuthorId = new AuthorId(AuthorSid);

    protected override void OnParametersSet()
        => AuthorId = new AuthorId(AuthorSid);

    protected override ComputedState<Model>.Options GetStateOptions()
    {
        if (AuthorId.IsNone)
            return ComputedStateComponent.GetStateOptions(GetType(),
                static t => new ComputedState<Model>.Options() {
                    InitialValue = Model.Loading,
                    Category = GetStateCategory(t),
                });

        var authorComputed = Computed.GetExisting(() => Authors.Get(Session, AuthorId.ChatId, AuthorId, default));
        var author = authorComputed?.IsConsistent() == true &&  authorComputed.HasValue ? authorComputed.Value : null;

        var model = Model.Loading;
        if (author != null) {
            model = new Model(author);

            var ownAuthorComputed = Computed.GetExisting(() => Authors.GetOwn(Session, ChatId, default));
            var ownAuthor = ownAuthorComputed?.IsConsistent() == true &&  ownAuthorComputed.HasValue ? ownAuthorComputed.Value : null;
            var isOwn = ownAuthor != null && ownAuthor.Id == author.Id;
            if (isOwn)
                model = model with { IsOwn = true };
        }

        return new () {
            InitialValue = model,
            Category = GetStateCategory(GetType()),
        };
    }

    protected override async Task<Model> ComputeState(CancellationToken cancellationToken) {
        var authorId = AuthorId;
        var chatId = ChatId;
        if (authorId.IsNone)
            return Model.None;

        var author = await Authors.Get(Session, authorId.ChatId, authorId, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return Model.None;

        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        var isOwn = ownAuthor != null && author.Id == ownAuthor.Id;
        return new Model(author, isOwn);
    }

    // Nested types

    public sealed record Model(
        Author Author,
        bool IsOwn = false)
    {
        public static readonly Model None = new(Author.None);
        public static readonly Model Loading = new(Author.Loading); // Should differ by ref. from None
    }
}
