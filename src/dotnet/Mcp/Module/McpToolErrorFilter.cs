using ModelContextProtocol;

namespace ActualChat.Mcp.Module;

/// <summary>
/// Lets a caller read why its tool call failed. The MCP SDK replaces every exception but
/// <see cref="McpException"/> with "An error occurred invoking '&lt;tool&gt;'", which tells an agent
/// nothing it can act on - and an agent that cannot read the reason cannot correct itself.
/// </summary>
public static class McpToolErrorFilter
{
    public static IMcpServerBuilder WithToolErrorFilter(this IMcpServerBuilder builder)
        => builder.WithRequestFilters(filters => filters
            .AddCallToolFilter(next => async (request, cancellationToken) => {
                try {
                    return await next(request, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (IsCallerError(e)) {
                    throw new McpException(e.Message);
                }
            }));

    // Private methods

    private static bool IsCallerError(Exception e)
        // The types StandardError produces for input the caller got wrong: Constraint, Format,
        // NotFound, Unauthorized and the non-transient upload errors. Everything else stays
        // masked - it may carry internals.
        => e is InvalidOperationException
            or FormatException
            or KeyNotFoundException
            or NotFoundException
            or ArgumentException
            or UnauthorizedAccessException
            or UploadNotFoundException
            or OffsetConflictException
            or UploadFileEmptyException;
}
