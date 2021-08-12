using System;
using System.ComponentModel.DataAnnotations;

namespace ActualChat.Users
{
    public interface ISpeakerNameService
    {
        ValidationException? ValidateName(in ReadOnlySpan<char> name);
        ReadOnlySpan<char> ParseName(ref ReadOnlySpan<char> text);
    }
}
