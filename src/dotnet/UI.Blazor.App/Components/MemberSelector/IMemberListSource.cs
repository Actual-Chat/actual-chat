namespace ActualChat.UI.Blazor.App.Components;

public interface IMemberListSource
{
    CandidateListKind CandidateListKind { get; }
    Task<ApiArray<UserId>> ListCandidateUserIds(CancellationToken cancellationToken);
    Task<ApiArray<UserId>> ListMemberUserIds(CancellationToken cancellationToken);
}
