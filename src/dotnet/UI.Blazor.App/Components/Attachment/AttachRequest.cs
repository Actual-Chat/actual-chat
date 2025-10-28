using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.UI.Blazor.App.Components;

public interface IAttachRequest;

public record UploadSessionAttachRequest(string UploadSessionId) : IAttachRequest;

public record AttachFileRequest(IFileProvider FileProvider) : IAttachRequest;
