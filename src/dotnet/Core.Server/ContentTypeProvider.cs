using System.Net.Mime;
using Microsoft.AspNetCore.StaticFiles;

namespace ActualChat;

public class ContentTypeProvider : FileExtensionContentTypeProvider
{
    public static readonly ContentTypeProvider Instance = new () {
        Mappings = {
            [".br"] = MediaTypeNames.Application.Octet,
            [".onnx"] = MediaTypeNames.Application.Octet,
        },
    };
}
