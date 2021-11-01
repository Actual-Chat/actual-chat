namespace ActualChat.Users;

public interface INicknameGenerator
{
    ValueTask<string> Generate(CancellationToken cancellationToken = default);
}
