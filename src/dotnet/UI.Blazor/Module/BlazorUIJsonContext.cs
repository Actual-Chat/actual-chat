using ActualChat.UI.Blazor.Components.SideNav;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.Module;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    PropertyNameCaseInsensitive = true,
    GenerationMode = JsonSourceGenerationMode.Default)]
// JS interop return types
[JsonSerializable(typeof(NullableJSObjectReference))]
// JS interop callback parameter types (from [JSInvokable] methods)
[JsonSerializable(typeof(IBrowserInfoBackend.InitResult))]
// [JsonSerializable(typeof(IWebShareInfoBackend.InitResult))] -- name matches with another InitResult, won't work
[JsonSerializable(typeof(VirtualListDataQuery))]
// Enums used in JS interop
[JsonSerializable(typeof(SideNavSide))]
// Supporting types
[JsonSerializable(typeof(Range<string>))]
[JsonSerializable(typeof(Range<double>))]
[JsonSerializable(typeof(Range<long>))]
[JsonSerializable(typeof(Range<int>))]
public partial class BlazorUIJsonContext : JsonSerializerContext;
