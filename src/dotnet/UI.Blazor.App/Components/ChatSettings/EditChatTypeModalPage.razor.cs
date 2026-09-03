using ActualChat.Invite;
using ActualChat.Localization;
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
    private PlaceId? _placeId;
    private Place? _place;
    private bool _isPlaceWelcomeChat;
    private ElementReference _aliasTextBoxRef;
    private string _aliasLocalPrefix = "";
    private string _copyAliasFormatString = "";
    private Action<ElementReference> _setAliasCopySource = null!;
    private bool _isPlaceAliasRequired;

    private IChats Chats => Hub.Chats;
    private IPlaces Places => Hub.Places;
    private IInvites Invites => Hub.Invites;

    [CascadingParameter] public DiveInModalPageContext Context { get; set; } = null!;
    [CascadingParameter] public Modal Modal { get; set; } = null!;

    private ChatId ChatId { get; set; } = null!;

    protected override void OnInitialized()
    {
        ChatId = Context.GetModel<ChatId>();
        Context.Title = L.Chat_SettingsTitle;
        Context.Class = "edit-chat-type";
        if (ChatId.Kind == ChatKind.Peer)
            throw StandardError.NotSupported("Peer chat is not supported.");

        _submitButtonInfo = DialogButtonInfo.CreateSubmitButton(L.Common_Save, OnSubmit);
        Context.Buttons = [DialogButtonInfo.CreateCancelButton(L), _submitButtonInfo];
        _setAliasCopySource = c => {
            _aliasTextBoxRef = c;
            StateHasChanged();
        };
    }

    protected override async Task OnInitializedAsync()
    {
        _isAdmin = Hub.AccountUI.OwnAccount.Value.IsAdmin;
        var chat = await Chats.Get(Session, ChatId, CancellationToken.None).Require();
        _placeId = (chat.Id as PlaceChatId)?.PlaceId;
        if (_placeId is not null) {
            _isPlaceWelcomeChat = chat.IsWelcome;
            _place = await Places.Get(Session, _placeId, CancellationToken.None).Require().ConfigureAwait(false);
            if (_place.IsPublic && _place.AliasId is not null)
                _aliasLocalPrefix = $"{Links.ChatAliasPrefix}{_place.AliasId.Value}{Links.Separator}{Links.AliasPrefix}";
            else
                _isPlaceAliasRequired = true;
        }
        else
            _aliasLocalPrefix = Links.ChatAliasPrefix;

        if (!_aliasLocalPrefix.IsNullOrEmpty())
            _copyAliasFormatString = UrlMapper.ToAbsolute(_aliasLocalPrefix) + "{0}";

        _form = new FormModel(ComponentIdGenerator) {
            IsPublic = chat.IsPublic,
            AliasId = chat.AliasId?.Value ?? "",
            CurrentAliasId = chat.AliasId?.Value ?? "",
            IsTemplate = chat.IsTemplate,
            AllowGuestAuthors = chat.AllowGuestAuthors,
            AllowAnonymousAuthors = chat.AllowAnonymousAuthors,
        };
        if (_place is not null && !_place.Rules.CanApplyPublicChatType())
            _form.IsPublic = false;
        if (_aliasLocalPrefix.IsNullOrEmpty()) // Aliases are not allowed
            _form.AliasId = "";

        _editContext = new EditContext(_form);
        _editContext.OnFieldChanged += (_, e) => {
            if (e.FieldIdentifier.FieldName == nameof(_form.AliasId)
                || e.FieldIdentifier.FieldName == nameof(_form.IsPublic))
            {
                _editContext.NotifyFieldChanged(_editContext.Field(nameof(_form.ActualAliasId)));
            }
        };
    }

    protected override async Task<ComputedModel> ComputeState(CancellationToken cancellationToken)
    {
        var chatId = ChatId;

        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat == null || !chat.Rules.IsOwner())
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
        ActualChat.Invite.Invite invite = ChatInvite.New(Constants.Invites.Defaults.ChatRemaining, ChatId);
        var uiActionResult = await UICommander.Run(new Invites_Generate {
            Session = Session,
            Invite = invite,
        }).ConfigureAwait(false);
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
        var chat = await Chats.Get(Session, ChatId, CancellationToken.None).Require().ConfigureAwait(false);
        var isPlaceChat = chat.Id.Kind is ChatKind.Place;
        var newChat = chat with {
            IsPublic = _form!.IsPublic,
            AliasId = _form!.IsPublic ? AliasId.ParseNullable(_form.AliasId) : null,
            AllowGuestAuthors = !isPlaceChat && _form.AllowGuestAuthors,
            AllowAnonymousAuthors = !isPlaceChat && _form.AllowAnonymousAuthors,
        };
        var command = new Chats_Change {
            Session = Session,
            ChatId = chat.Id,
            ExpectedVersion = chat.Version,
            Change = new () {
                Update = DiffEngine.Diff<Chat.Chat, ChatDiff>(chat, newChat),
            },
        };
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
        public string AliasId { get; set; } = "";
        public bool IsPublic { get; set; }
        public bool IsTemplate { get; set; }
        public bool AllowGuestAuthors { get; set; }
        public bool AllowAnonymousAuthors { get; set; }

        public string CurrentAliasId { get; set; } = "";
        [AliasId]
        public string ActualAliasId => IsPublic ? AliasId : "";

        public string FormId { get; }
        public string AliasIdFormId { get; }
        public string IsPublicFormId { get; }
        public string IsPublicTrueFormId { get; }
        public string IsPublicFalseFormId { get; }
        public string IsTemplateFormId { get; }
        public string AllowGuestAuthorsFormId { get; }
        public string AllowAnonymousAuthorsFormId { get; }

        public FormModel(ComponentIdGenerator componentIdGenerator)
        {
            FormId = componentIdGenerator.Next("new-chat-form");
            AliasIdFormId = $"{FormId}-alias-id";
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
        public Chat.Chat? Chat { get; init; }
        public List<Invite.Invite> Invites { get; init; } = [];
        public bool AllowEditIsTemplate { get; init; }
        public bool IsPublic { get; init; }
    }
}
