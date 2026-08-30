namespace ActualChat.UI.Blazor.Services;

/// <summary>
/// A single item of a <see cref="IFileSaver"/> save request.
/// </summary>
public sealed record FileToSave(string Url, string FileName, string ContentType);
