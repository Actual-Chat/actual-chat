namespace ActualChat.Kvas;

public sealed class AccountSettings(IServerKvas serverKvas, Session session)
    : ServerKvasClient(serverKvas, session);
