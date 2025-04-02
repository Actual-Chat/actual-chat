using ActualChat.Invite;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App.Components;

public partial class EditChatTypeModalPage
{
    private const int MaxInviteCount = 5;
    private Form? _formRef;
    private FormModel? _form;
    private EditContext? _editContext;
    private Symbol _newInviteId = Symbol.Empty;
    private DialogButtonInfo _submitButtonInfo = null!;
    private bool _isAdmin;
    private PlaceId _placeId;
    private Place? _place;
    private bool _isPlaceWelcomeChat;
    private ElementReference _userLinkTextBoxRef;
    private string _userLinkLocalPrefix = "";
    private string _copyUserLinkFormatString = "";
    private Action<ElementReference> _setUserLinkCopySource = null!;
    private bool _placeUserLinkRequiredFirst;

    [Inject] private ChatUIHub Hub { get; init; } = null!;
    private Session Session => Hub.Session();
    private IChats Chats => Hub.Chats;
    private IPlaces Places => Hub.Places;
    private IInvites Invites => Hub.Invites;
    private UrlMapper UrlMapper => Hub.UrlMapper();
    private UICommander UICommander => Hub.UICommander();
    private Features Features => Hub.Features();
    private MomentClockSet Clocks => Hub.Clocks();
    private ComponentIdGenerator ComponentIdGenerator => Hub.ComponentIdGenerator;
    private DiffEngine DiffEngine => Hub.DiffEngine;

    [CascadingParameter] public DiveInModalPageContext Context { get; set; } = null!;
    [CascadingParameter] public Modal Modal { get; set; } = null!;
    private ChatId ChatId { get; set; }

    protected override void OnInitialized()
    {
        ChatId = Context.GetModel<ChatId>();
        Context.Title = "Chat settings";
        Context.Class = "edit-chat-type";
        if (ChatId.IsPeerChat(out _))
            throw StandardError.NotSupported("Peer chat is not supported.");

        _submitButtonInfo = DialogButtonInfo.CreateSubmitButton("Save", OnSubmit);
        Context.Buttons = [DialogButtonInfo.CancelButton, _submitButtonInfo];
        _setUserLinkCopySource = c => {
            _userLinkTextBoxRef = c;
            StateHasChanged();
        };
    }

    protected override async Task OnInitializedAsync()
    {
        _isAdmin = Hub.AccountUI.OwnAccount.Value.IsAdmin;
        var chat = await Chats.Get(Session, ChatId, default).Require();
        _placeId = chat.Id.PlaceChatId.PlaceId;
        if (!_placeId.IsNone) {
            _isPlaceWelcomeChat = OrdinalEquals(Constants.Chat.SystemTags.Welcome, chat.SystemTag);
            _place = await Places.Get(Session, _placeId, default).Require().ConfigureAwait(false);
            if (_place.IsPublic && !_place.UserLinkId.IsNone)
                _userLinkLocalPrefix = Links.ChatUserLinkPrefix + _place.UserLinkId + Links.Separator + Links.UserLinkPrefix;
            else
                _placeUserLinkRequiredFirst = true;
        }
        else
            _userLinkLocalPrefix = Links.ChatUserLinkPrefix;

        if (!_userLinkLocalPrefix.IsNullOrEmpty())
            _copyUserLinkFormatString = UrlMapper.ToAbsolute(_userLinkLocalPrefix) + "{0}";

        _form = new FormModel(ComponentIdGenerator) {
            IsPublic = chat.IsPublic,
            UserLinkId = chat.UserLinkId.Value,
            CurrentUserLinkId = chat.UserLinkId.Value,
            IsTemplate = chat.IsTemplate,
            AllowGuestAuthors = chat.AllowGuestAuthors,
            AllowAnonymousAuthors = chat.AllowAnonymousAuthors,
        };
        if (_place is not null && !_place.Rules.CanApplyPublicChatType())
            _form.IsPublic = false;
        if (_userLinkLocalPrefix.IsNullOrEmpty()) // User links are not allowed.
            _form.UserLinkId = UserLinkId.None.Value;

        _editContext = new EditContext(_form);
        _editContext.OnFieldChanged += (_, e) => {
            if (OrdinalEquals(e.FieldIdentifier.FieldName, nameof(_form.UserLinkId))
                || OrdinalEquals(e.FieldIdentifier.FieldName, nameof(_form.IsPublic)))
            {
                _editContext.NotifyFieldChanged(_editContext.Field(nameof(_form.ActualUserLinkId)));
            }
        };
    }

    protected override ComputedState<ComputedModel>.Options GetStateOptions()
        => ComputedStateComponent.GetStateOptions(GetType(),
            static t => new ComputedState<ComputedModel>.Options() {
                InitialValue = ComputedModel.Loading,
                Category = GetStateCategory(t),
            });

    protected override async Task<ComputedModel> ComputeState(CancellationToken cancellationToken)
    {
        var chatId = ChatId;

        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null || !chat.Rules.CanEditProperties())
            return new ComputedModel { Chat = chat };

        List<Invite.Invite> activeInvites;
        if (chat.CanInvite()) {
            var invites = await Invites.ListChatInvites(Session, chatId, cancellationToken).ConfigureAwait(false);
            var threshold = Clocks.SystemClock.Now - TimeSpan.FromDays(3);
            activeInvites = invites
                .Where(c => c.ExpiresOn > threshold)
                .OrderByDescending(c => c.ExpiresOn)
                .ToList();
        }
        else
            activeInvites = [];
        var allowEditIsTemplate = await Features.Get<Features_EnableTemplateChatUI>(cancellationToken);
        var isPublic = chat.IsPublic;
        return new () {
            Chat = chat,
            Invites = activeInvites,
            AllowEditIsTemplate = allowEditIsTemplate,
            IsPublic = isPublic,
        };
    }

    private async Task OnNewInviteClick()
    {
        var invite = Invite.Invite.New(Constants.Invites.Defaults.ChatRemaining, new ChatInviteOption(ChatId));
        var uiActionResult = await UICommander.Run(new Invites_Generate(Session, invite)).ConfigureAwait(false);
        invite = uiActionResult.Value;
        _newInviteId = invite.Id;
    }

    private async Task OnSubmit()
    {
        if (_formRef is not { IsValid: true })
            return;

        if (!await Save())
            Context.Close();
        else
            _ = Modal.StepBack();
    }

    private async Task<bool> Save()
    {
        var chat = await Chats.Get(Session, ChatId, default).Require().ConfigureAwait(false);
        var isPlaceChat = chat.Id.IsPlaceChat;
        var newChat = chat with {
            IsPublic = _form!.IsPublic,
            UserLinkId = _form!.IsPublic ? UserLinkId.ParseOrNone(_form.UserLinkId) : UserLinkId.None,
            AllowGuestAuthors = !isPlaceChat && _form.AllowGuestAuthors,
            AllowAnonymousAuthors = !isPlaceChat && _form.AllowAnonymousAuthors,
        };
        var command = new Chats_Change(Session,
            chat.Id,
            chat.Version,
            new () {
                Update = DiffEngine.Diff<Chat.Chat, ChatDiff>(chat, newChat),
            });
        var uiActionResult = await UICommander.Run(command).ConfigureAwait(false);
        return !uiActionResult.HasError;
    }

    private void OnAllowGuestAuthorsClick()
    {
        _form!.AllowGuestAuthors = !_form.AllowGuestAuthors;
        StateHasChanged();
    }

    private void OnAllowAnonymousAuthorsClick()
    {
        _form!.AllowAnonymousAuthors = !_form.AllowAnonymousAuthors;
        StateHasChanged();
    }

    private void OnIsTemplateClick()
    {
        _form!.IsTemplate = !_form.IsTemplate;
        StateHasChanged();
    }


    private void OnPublicChatClick(bool isPublic)
    {
        if (_form!.IsPublic == isPublic)
            return;

        _form!.IsPublic = isPublic;
        StateHasChanged();
    }

    // Nested types

    public sealed class FormModel
    {
        public string UserLinkId { get; set; } = "";
        public bool IsPublic { get; set; }
        public bool IsTemplate { get; set; }
        public bool AllowGuestAuthors { get; set; }
        public bool AllowAnonymousAuthors { get; set; }

        public string CurrentUserLinkId { get; set; } = "";
        [UserLinkId]
        public string ActualUserLinkId => IsPublic ? UserLinkId : ActualChat.UserLinkId.None.Value;

        public string FormId { get; }
        public string UserLinkIdFormId { get; }
        public string IsPublicFormId { get; }
        public string IsPublicTrueFormId { get; }
        public string IsPublicFalseFormId { get; }
        public string IsTemplateFormId { get; }
        public string AllowGuestAuthorsFormId { get; }
        public string AllowAnonymousAuthorsFormId { get; }

        public FormModel(ComponentIdGenerator componentIdGenerator)
        {
            FormId = componentIdGenerator.Next("new-chat-form");
            UserLinkIdFormId = $"{FormId}-user-link-id";
            IsPublicFormId = $"{FormId}-is-public";
            IsPublicTrueFormId = IsPublicFormId + "-true";
            IsPublicFalseFormId = IsPublicFormId + "-false";
            IsTemplateFormId = $"{FormId}-is-template";
            AllowGuestAuthorsFormId = $"{FormId}-allows-guests";
            AllowAnonymousAuthorsFormId = $"{FormId}-allows-anonymous";
        }
    }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)]
    public sealed record ComputedModel
    {
        public static readonly ComputedModel Loading = new ();

        public Chat.Chat? Chat { get; init; }
        public List<Invite.Invite> Invites { get; init; } = [];
        public bool AllowEditIsTemplate { get; init; }
        public bool IsPublic { get; init; }
    }
}
