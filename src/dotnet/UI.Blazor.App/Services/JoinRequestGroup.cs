namespace ActualChat.UI.Blazor.App.Services;

// A Place's requests come under its root chat

public sealed record JoinRequestGroup(Chat.Chat Chat, IReadOnlyList<JoinRequest> Requests);
