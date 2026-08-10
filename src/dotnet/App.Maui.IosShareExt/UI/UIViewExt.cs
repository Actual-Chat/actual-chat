using ActualChat.App.Maui.IosShareExt.UI.Fusion.Ios;

namespace ActualChat.App.Maui.IosShareExt.UI;

public static class UIViewExt
{
    extension(UIView view)
    {
        public void RemoveAndDisposeStates()
        {
            // DisposeStates only reaches what's still in the view tree, so a view pulled out of
            // it has to take its states along on the way out.
            view.RemoveFromSuperview();
            view.DisposeStates();
        }

        public void DisposeStates()
        {
            // Digs out the stateful views buried under plain containers - collection view
            // cells in particular; from there each one takes its own subtree down. Disposal
            // is synchronous, and UIKit teardown is main-thread bound anyway.
            if (view is IStatefulView statefulView) {
                statefulView.DisposeStates();
                return;
            }

            foreach (var subview in view.Subviews)
                subview.DisposeStates();
        }
    }
}
