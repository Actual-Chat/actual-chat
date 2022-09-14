using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using BlazorContextMenu.Services;
using Microsoft.JSInterop;
using Microsoft.AspNetCore.Components.RenderTree;
using System.Runtime.CompilerServices;
using ActualChat.UI.Blazor.Module;
using Microsoft.AspNetCore.Components.Rendering;

namespace BlazorContextMenu;

[SuppressMessage("Usage", "MA0011:IFormatProvider is missing")]
[SuppressMessage("Usage", "MA0074:Avoid implicit culture-sensitive methods")]
public class ContextMenuTrigger : ComponentBase, IDisposable
{
    protected ElementReference? ContextMenuTriggerElementRef;
    private DotNetObjectReference<ContextMenuTrigger>? _dotNetObjectRef;

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        /*
            <div @attributes="Attributes"
                 onclick="@(MouseButtonTrigger == MouseButtonTrigger.Left || MouseButtonTrigger == MouseButtonTrigger.Both ? $"blazorContextMenu.OnContextMenu(event, '{MenuId.Replace("'","\\'")}'); " : "")"
                 ondblclick="@(MouseButtonTrigger == MouseButtonTrigger.DoubleClick ? $"blazorContextMenu.OnContextMenu(event, '{MenuId.Replace("'","\\'")}'); " : "")"
                 oncontextmenu="@(MouseButtonTrigger == MouseButtonTrigger.Right || MouseButtonTrigger == MouseButtonTrigger.Both ? $"blazorContextMenu.OnContextMenu(event, '{MenuId.Replace("'","\\'")}');": "")"
                 class="@CssClass">
                @ChildContent
            </div>
         */

        builder.OpenElement(0, WrapperTag);

        builder.AddMultipleAttributes(1, Microsoft.AspNetCore.Components.CompilerServices.RuntimeHelpers.TypeCheck<global::System.Collections.Generic.IEnumerable<global::System.Collections.Generic.KeyValuePair<string, object>>>(Attributes!));

        var triggerHandler = $"{BlazorUICoreModule.ImportName}.blazorContextMenu.OnContextMenu(event, '{MenuId.Replace("'", "\\'")}', {StopPropagation.ToString().ToLower()});";
        if (MouseButtonTrigger == MouseButtonTrigger.Left || MouseButtonTrigger == MouseButtonTrigger.Both)
            builder.AddAttribute(2, "onclick", triggerHandler);

        if (MouseButtonTrigger == MouseButtonTrigger.Right || MouseButtonTrigger == MouseButtonTrigger.Both)
            builder.AddAttribute(3, "oncontextmenu", triggerHandler);

        if (MouseButtonTrigger == MouseButtonTrigger.DoubleClick)
            builder.AddAttribute(4, "ondblclick", triggerHandler);

        if (!string.IsNullOrWhiteSpace(CssClass))
            builder.AddAttribute(5, "class", CssClass);
        builder.AddAttribute(6, "id", Id);
        builder.AddContent(7, ChildContent);
        builder.AddElementReferenceCapture(8, (__value) =>
        {
            ContextMenuTriggerElementRef = __value;
        });
        builder.CloseElement();
    }

    [Inject] private IJSRuntime JS { get; init; } = null!;
    [Inject] private IInternalContextMenuHandler InternalContextMenuHandler { get; init; } = null!;

    [Parameter(CaptureUnmatchedValues = true)]
    public Dictionary<string, object>? Attributes { get; set; }

    /// <summary>
    /// The id of the <see cref="ContextMenuTrigger" /> wrapper element.
    /// </summary>
    [Parameter]
    public string? Id { get; set; }

    /// <summary>
    /// The Id of the <see cref="ContextMenu" /> to open. This parameter is required.
    /// </summary>
    [Parameter, EditorRequired]
    public string MenuId { get; set; } = "";

    /// <summary>
    /// Additional css class for the trigger's wrapper element.
    /// </summary>
    [Parameter]
    public string? CssClass { get; set; }

    /// <summary>
    /// The mouse button that triggers the menu.
    ///
    /// </summary>
    [Parameter]
    public MouseButtonTrigger MouseButtonTrigger { get; set; }

    /// <summary>
    /// The trigger's wrapper element tag (default: "div").
    /// </summary>
    [Parameter]
    public string WrapperTag { get; set; } = "div";

    /// <summary>
    /// Extra data that will be passed to menu events.
    /// </summary>
    [Parameter]
    public object? Data { get; set; }

    /// <summary>
    /// Set to false if you do not want the click event to stop propagating. Default: true
    /// </summary>
    [Parameter]
    public bool StopPropagation { get; set; } = true;

    [Parameter]
    public RenderFragment ChildContent { get; set; } = null!;

    protected override void OnInitialized()
    {
        if (string.IsNullOrEmpty(MenuId))
            throw new ArgumentNullException(nameof(MenuId));
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!InternalContextMenuHandler.ReferencePassedToJs) {
            await JS.InvokeAsync<object>($"{BlazorUICoreModule.ImportName}.blazorContextMenu.SetMenuHandlerReference", DotNetObjectReference.Create(InternalContextMenuHandler));
            InternalContextMenuHandler.ReferencePassedToJs = true;
        }

        if (firstRender) {
            _dotNetObjectRef = DotNetObjectReference.Create(this);
            await JS.InvokeAsync<object>($"{BlazorUICoreModule.ImportName}.blazorContextMenu.RegisterTriggerReference",
                ContextMenuTriggerElementRef, _dotNetObjectRef);
        }
    }

    public void Dispose()
    {
        if (_dotNetObjectRef != null) {
            _dotNetObjectRef.Dispose();
            _dotNetObjectRef = null;
        }
    }
}
