import {
    concat,
    fromEvent,
    of,
    Observable,
    Subject,
} from 'rxjs';
import { Log, LogLevel, LogScope } from 'logging';

const LogScope: LogScope = 'ScreenSize';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

export type Size = 'Unknown' | 'Small' | 'Medium' | 'Large' | 'ExtraLarge' | 'ExtraLarge2';

export class ScreenSize {
    private static screenSizeMeasureDiv: HTMLDivElement;

    public static size: Size;
    public static change$ = new Subject<Size>();
    public static size$: Observable<Size>;
    public static event$: Observable<Event>;

    public static init() {
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
        this.size = 'Unknown';
        this.measureAndUpdate();

        this.event$ = fromEvent(window.visualViewport, 'resize');
        this.event$.subscribe(() => this.measureAndUpdate());
        this.size$ = concat(
            of(this.size),
            this.change$
        );
    }

    public static isNarrow(size?: Size): boolean {
        return (size ?? this.size) == 'Small';
    }

    public static isWide(size?: Size): boolean {
        return !this.isNarrow(size);
    }

    private static measureAndUpdate(): Size {
        const size = this.measure();
        if (size != this.size) {
            debugLog?.log(`measureAndUpdate: new size:`, size);
            this.size = size;
            this.updateBodyClasses();
            try {
                this.change$.next(size);
            }
            catch (e) {
                errorLog?.log("measureAndUpdate: one of change$ handlers failed:", e)
            }
        }
        return size;
    }

    private static measure(): Size {
        let itemDiv: HTMLDivElement = null;
        for (const item of this.screenSizeMeasureDiv.children) {
            itemDiv = item as HTMLDivElement;
            if (!item)
                continue;

            const isVisible = window.getComputedStyle(itemDiv).getPropertyValue('width') !== 'auto';
            // debugLog?.log(`measure: size:`, itemDiv.dataset['size'], ', isVisible:', isVisible);
            if (isVisible)
                return itemDiv.dataset['size'] as Size;
        }
        // Returning the last "available" size
        return (itemDiv.dataset['size'] ?? "Unknown") as Size;
    };

    private static updateBodyClasses() {
        const classList = document.body.classList;
        const isNarrow = this.size == 'Small';
        if (isNarrow) {
            classList.remove('wide');
            classList.add('narrow');
        }
        else {
            classList.remove('narrow');
            classList.add('wide');
        }
    }
}

ScreenSize.init();
