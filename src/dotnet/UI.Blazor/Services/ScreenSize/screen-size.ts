import {
    concat,
    debounceTime,
    distinctUntilChanged,
    fromEvent,
    map,
    of,
    Observable,
    shareReplay,
} from 'rxjs';
import { Log, LogLevel } from 'logging';

import './screen-size.css'

const LogScope = 'ScreenSize';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export type Size = 'Unknown' | 'Small' | 'Medium' | 'Large' | 'ExtraLarge' | 'ExtraLarge2';

export class ScreenSize {
    private readonly screenSizeMeasureDiv: HTMLDivElement;
    private readonly screenSize$: Observable<Size>;

    constructor() {
        this.screenSizeMeasureDiv = document.createElement("div");
        this.screenSizeMeasureDiv.className = "screen-size-measure";
        document.body.appendChild(this.screenSizeMeasureDiv);
        this.screenSizeMeasureDiv.innerHTML = `
            <div data-size='ExtraLarge2'></div>
            <div data-size='ExtraLarge'></div>
            <div data-size='Large'></div>
            <div data-size='Medium'></div>
            <div data-size='Small'></div>
        `;

        this.screenSize$ = concat(
            of(1),
            fromEvent(window, 'resize').pipe(debounceTime(300))
        ).pipe(
            map(_ => this.measureScreenSize()),
            distinctUntilChanged(),
            shareReplay(1)
        );
    }

    public get size$(): Observable<Size> {
        return this.screenSize$;
    }

    private measureScreenSize(): Size {
        let itemDiv : HTMLDivElement = null;
        for (const item of this.screenSizeMeasureDiv.children) {
            itemDiv = item as HTMLDivElement;
            if (!item)
                continue;

            const isVisible = window.getComputedStyle(itemDiv).getPropertyValue('width') !== 'auto';
            debugLog?.log(`measureScreenSize: size:`, itemDiv.dataset['size'], ', isVisible:', isVisible);
            if (isVisible)
                return itemDiv.dataset['size'] as Size;
        }
        // Returning the last "available" size
        return (itemDiv.dataset['size'] ?? "Unknown") as Size;
    };
}

const screenSize = new ScreenSize();

export default screenSize;
