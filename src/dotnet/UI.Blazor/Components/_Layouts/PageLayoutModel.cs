using Microsoft.AspNetCore.Components;

namespace ActualChat.UI.Blazor.Components;

public class PageLayoutModel
{
    private RenderFragment? _header;
    private RenderFragment? _footer;

    public event Action<PageLayoutModel>? Changed;

    public RenderFragment? Header {
        get => _header;
        set {
            if (_header == value)
                return;
            _header = value;
            Changed?.Invoke(this);
        }
    }

    public RenderFragment? Footer {
        get => _footer;
        set {
            if (_footer == value)
                return;
            _footer = value;
            Changed?.Invoke(this);
        }
    }
}
