namespace ActualChat.UI.Blazor.App.Components;

public interface IMemberListSource
{
    CandidateListKind CandidateListKind { get; }
    Task<UserId[]> ListCandidateUserIds(CancellationToken cancellationToken);
    Task<UserId[]> ListMemberUserIds(CancellationToken cancellationToken);
}
