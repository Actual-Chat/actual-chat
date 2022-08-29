using System.Text;

namespace ActualChat.Kubernetes;

public record KubeEndpoint(ImmutableArray<string> Addresses, bool IsReady)
{
    protected virtual bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Addresses = [");
        foreach (var address in Addresses.Take(1))
            builder.Append(address);
        foreach (var address in Addresses.Skip(1))
            builder.Append($", {address}");
        builder.Append("], ");
        builder.Append($"IsReady = {IsReady}");
        return true;
    }
}
