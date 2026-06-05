import { Disposable } from 'disposable';
import { getOrInheritData } from 'dom-helpers';
import { HoverIntent } from 'hover-intent';
import { ScreenSize } from '../../../UI.Blazor/Services/ScreenSize/screen-size';

const HoverDelayMs = 180;

export class ChatHoverMenu implements Disposable {
    public static create(blazorRef: DotNet.DotNetObject): ChatHoverMenu {
        return new ChatHoverMenu(blazorRef);
    }

    private readonly hoverIntent: HoverIntent;

    constructor(private readonly blazorRef: DotNet.DotNetObject) {
        this.hoverIntent = new HoverIntent({
            delayMs: HoverDelayMs,
            isEnabled: () => ScreenSize.isWide(),
            resolveKey: target => {
                const [element, entryId] = getOrInheritData(target, 'chatEntryId');
                return element && entryId ? [element, entryId] as const : null;
            },
            onSettled: entryId => void this.blazorRef.invokeMethodAsync('OnHoverShow', entryId),
            onLeave: () => void this.blazorRef.invokeMethodAsync('OnHoverHide'),
        });
    }

    public dispose(): void {
        this.hoverIntent.dispose();
    }
}
