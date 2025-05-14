using ActualChat.Contacts;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public abstract class AuthorBadgeBase : ComputedStateComponent<ChatUIHub, AuthorBadgeBase.Model?>
{
    protected IAuthors Authors => Hub.Authors;
    protected IContacts Contacts => Hub.Contacts;
    protected AuthorUI AuthorUI => Hub.AuthorUI;

    protected AuthorId? AuthorId { get; private set; }
    protected ChatId? ChatId => AuthorId?.ChatId;

    // TODO(AY): Use AuthorId instead
    [Parameter, EditorRequired] public string AuthorSid { get; set; } = "";

    protected override void OnInitialized()
        => AuthorId = AuthorId.ParseNullable(AuthorSid);

    protected override void OnParametersSet()
        => AuthorId = AuthorId.ParseNullable(AuthorSid);

    protected override ComputedState<Model?>.Options GetStateOptions()
    {
        var (authorId, chatId) = (AuthorId, AuthorId?.ChatId);
        if (authorId is null || chatId is null)
            return base.GetStateOptions();

        var authorComputed = Computed.GetExisting(() => Authors.Get(Session, chatId, authorId, default));
        var author = authorComputed?.IsConsistent() == true &&  authorComputed.HasValue ? authorComputed.Value : null;

        Model? model = null;
        if (author is not null) {
            model = new Model(author);
            var ownAuthorComputed = Computed.GetExisting(() => Authors.GetOwn(Session, chatId, default));
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

    protected override async Task<Model?> ComputeState(CancellationToken cancellationToken) {
        var (authorId, chatId) = (AuthorId, AuthorId?.ChatId);
        if (authorId is null || chatId is null)
            return Model.None;

        var author = await Authors.Get(Session, chatId, authorId, cancellationToken).ConfigureAwait(false);
        if (author == null)
            return Model.None;

        var ownAuthor = await Authors.GetOwn(Session, chatId, cancellationToken).ConfigureAwait(false);
        var isOwn = ownAuthor != null && author.Id == ownAuthor.Id;
        return new Model(author, isOwn);
    }

    // Nested types

    public sealed record Model(
        Author? Author,
        bool IsOwn = false)
    {
        public static Model None => new ((Author?)null);
    }
}
