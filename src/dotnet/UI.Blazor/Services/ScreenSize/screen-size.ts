import {
    concat,
    debounceTime,
    fromEvent,
    of,
    Observable,
    Subject,
} from 'rxjs';
import { Log } from 'logging';

const { debugLog, errorLog } = Log.get('ScreenSize');

export type Size = 'Unknown' | 'Small' | 'Medium' | 'Large' | 'ExtraLarge' | 'ExtraLarge2';

export class ScreenSize {
    private static screenSizeMeasureDiv: HTMLDivElement;
    private static hoverMeasureDiv: HTMLDivElement;
    private static innerHoverMeasureDiv: HTMLDivElement;

    public static width: number;
    public static height: number;
    public static size: Size = 'Unknown';
    public static isHoverable: boolean;
    public static change$ = new Subject<Size>();
    public static size$: Observable<Size>;
    public static event$ = new Subject<Event | null>();

    public static init() {
        this.hoverMeasureDiv = document.createElement("div");
        this.hoverMeasureDiv.className = "hover-measure";
        this.hoverMeasureDiv.innerHTML = `<div></div>`;
        document.body.appendChild(this.hoverMeasureDiv);
        this.innerHoverMeasureDiv = this.hoverMeasureDiv.children[0] as HTMLDivElement;

        this.screenSizeMeasureDiv = document.createElement("div");
        this.screenSizeMeasureDiv.className = "screen-size-measure";
        this.screenSizeMeasureDiv.innerHTML = `
            <div data-size='ExtraLarge2'></div>
            <div data-size='ExtraLarge'></div>
            <div data-size='Large'></div>
            <div data-size='Medium'></div>
            <div data-size='Small'></div>
        `;
        document.body.appendChild(this.screenSizeMeasureDiv);
        this.measureAndUpdate();

        this.size$ = concat(of(this.size), this.change$);
        fromEvent(window.visualViewport, 'resize')
            .pipe(debounceTime(50))
            .subscribe((event: Event) => this.notifyChanged(event));
    }

    public static isNarrow(size?: Size): boolean {
        return (size ?? this.size) == 'Small';
    }

    public static isWide(size?: Size): boolean {
        return !this.isNarrow(size);
    }

    public static notifyChanged(event?: Event): void {
        this.measureAndUpdate()
        this.event$.next(event);
    }

    private static measureAndUpdate(): Size {
        let [size, isHoverable] = this.measure();
        if (size == 'Small') // We're always non-hoverable in narrow mode
            isHoverable = false;

        if (size != this.size || isHoverable != this.isHoverable) {
            debugLog?.log(`measureAndUpdate: new size:`, size, ', isHoverable:', isHoverable);
            this.size = size;
            this.isHoverable = isHoverable;
            this.updateBodyClasses();
            try {
                this.change$.next(size);
            }
            catch (e) {
                errorLog?.log('measureAndUpdate: one of change$ handlers failed:', e)
            }
        }
        return size;
    }

    private static measure(): [Size, boolean] {
        this.width = visualViewport.width;
        this.height = visualViewport.height;
        const isHoverable = window.getComputedStyle(this.innerHoverMeasureDiv).getPropertyValue('width') !== 'auto';
        let itemDiv: HTMLDivElement = null;
        for (const item of this.screenSizeMeasureDiv.children) {
            itemDiv = item as HTMLDivElement;
            if (!item)
                continue;

            const isVisible = window.getComputedStyle(itemDiv).getPropertyValue('width') !== 'auto';
            // debugLog?.log(`measure: size:`, itemDiv.dataset['size'], ', isVisible:', isVisible);
            if (isVisible)
                return [itemDiv.dataset['size'] as Size, isHoverable];
        }
        // Returning the last "available" size
        return [(itemDiv.dataset['size'] ?? "Unknown") as Size, isHoverable];
    };

    private static updateBodyClasses() {
        const classList = document.body.classList;
        if (this.isHoverable) {
            classList.remove('non-hoverable');
            classList.add('hoverable');
        }
        else {
            classList.remove('hoverable');
            classList.add('non-hoverable');
        }
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
