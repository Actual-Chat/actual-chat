using ActualChat.UI.App;

namespace ActualChat.UI.Blazor.App.Module;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
    GenerationMode = JsonSourceGenerationMode.Default)]
// JS interop return types
[JsonSerializable(typeof(AudioRecorder.AudioDiagnosticsState))]
[JsonSerializable(typeof(Size))]
public partial class BlazorUIAppJsonContext : JsonSerializerContext;
