import { Subject } from 'rxjs';
import { ScreenSize } from '../../../UI.Blazor/Services/ScreenSize/screen-size';
import { attachInlineDrag } from './inline-drag';

// Wires the visual-activity drag handle to the map panel: swipe up to minimize,
// swipe down to restore. Touches on the map itself pan the map, so the handle
// is the only drag source.
export class MapPanel {
    private readonly disposed$ = new Subject<void>();

    static create(host: HTMLElement): MapPanel {
        return new MapPanel(host);
    }

    constructor(host: HTMLElement) {
        const panel = host.querySelector<HTMLElement>('.map-panel');
        const handle = document.querySelector<HTMLElement>('.c-drag-handle');
        if (panel && handle)
            attachInlineDrag({
                panel,
                handle,
                dragSources: [{ element: handle }],
                getContent: () => panel,
                canStart: () => !ScreenSize.isWide(),
                disposed$: this.disposed$,
            });
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }
}
