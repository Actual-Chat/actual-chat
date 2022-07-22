using System.ComponentModel.DataAnnotations;

namespace ActualChat;

public static class BoolExt
{
    public static bool RequireTrue(this bool source, string message)
        => !source ? throw new ValidationException(message) : source;
    public static bool RequireFalse(this bool source, string message)
        => source ? throw new ValidationException(message) : source;
}
