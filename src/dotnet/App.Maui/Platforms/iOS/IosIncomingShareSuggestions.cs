using ActualChat.Maui;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.App.Maui;

public class IosIncomingShareSuggestions(IServiceProvider services) : IncomingShareSuggestions(services)
{
    private IntentDonation IntentDonation => field ??= Services.GetRequiredService<IntentDonation>();

    protected override Task SuggestInternal(Chat.Chat chat, CancellationToken cancellationToken)
        => IntentDonation.Donate(chat, null, cancellationToken);
}
