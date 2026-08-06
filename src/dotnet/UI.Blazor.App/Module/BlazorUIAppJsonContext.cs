
namespace ActualChat.UI.Blazor.App.Module;

// NOTE(AY): This type is unused for now, but may be useful later; see

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
    GenerationMode = JsonSourceGenerationMode.Default)]
// JS interop return types
[JsonSerializable(typeof(AudioRecorder.AudioDiagnosticsState))]
[JsonSerializable(typeof(Size2D))]
public partial class BlazorUIAppJsonContext : JsonSerializerContext;
