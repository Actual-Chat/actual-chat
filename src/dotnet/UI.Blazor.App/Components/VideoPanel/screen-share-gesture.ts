import { MediaCapture } from '../../Services/Video/services/media-capture';

// Pre-acquires the screen track inside the real DOM click on any "Share screen"
// control. getDisplayMedia needs transient user activation, which is gone by the
// time the Blazor server round-trip reaches VideoRecorder.startScreenCast — so we
// grab it here, in-gesture, and stash it for that flow to consume. Every start-state
// trigger carries `screen-share-start`; stop controls omit it so we never capture.
export class ScreenShareGesture {
    private static isInitialized = false;

    public static init(): void {
        if (this.isInitialized || typeof document === 'undefined')
            return;

        this.isInitialized = true;
        document.addEventListener('click', ScreenShareGesture.onClick, { capture: true });
    }

    private static onClick = (event: MouseEvent): void => {
        const target = event.target as Element | null;
        if (!target?.closest('.screen-share-start'))
            return;

        MediaCapture.preacquireScreenCast();
    };
}

ScreenShareGesture.init();
