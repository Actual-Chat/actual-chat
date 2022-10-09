import { Disposable } from 'disposable';
import { fromEvent, Subject, takeUntil, map, switchMap, of, empty, merge } from 'rxjs';
import {
    Placement,
    Middleware,
    computePosition,
    flip,
    shift,
    offset,
    inline,
} from '@floating-ui/dom';
import escapist from '../Escapist/escapist';

interface Coords {
    x: number;
    y: number;
}

interface EventData {
    element: HTMLElement;
    coords?: Coords;
}

const LogScope = 'ContextMenu';

export class ContextMenu implements Disposable {
    private readonly disposed$: Subject<void> = new Subject<void>();
    private readonly menuRef: HTMLElement;

    public static create(): ContextMenu {
        return new ContextMenu();
    }

    constructor() {
        try {
            this.menuRef = document.getElementsByClassName('ac-menu')[0] as HTMLElement;
            this.listenForEvents();
        } catch (error) {
            console.error(`${LogScope}.ctor: error:`, error);
            this.dispose();
        }
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();

        if (this.menuRef)
            this.hideMenu();
    }

    private listenForEvents(): void {
        let currentData: EventData | undefined = undefined;
        fromEvent(document, 'contextmenu')
            .pipe(
                takeUntil(this.disposed$),
                map((event: PointerEvent) => {
                    event.preventDefault();
                    event.stopPropagation();
                    console.log(event);
                    if (!(event.target instanceof HTMLElement))
                        return undefined;
                    const closestElement = event.target.closest('[data-menu]');
                    if (closestElement == currentData?.element)
                        return undefined;
                    if (!closestElement && currentData?.element) {
                        currentData = undefined;
                        this.hideMenu();
                        return undefined;
                    }
                    if (!(closestElement instanceof HTMLElement))
                        return undefined;
                    const eventData: EventData = {
                        element: closestElement,
                        coords: {
                            x: event.clientX,
                            y: event.clientY,
                        },
                    };
                    return eventData;
                }),
                switchMap((eventData: EventData | undefined) => {
                    return eventData ? of(eventData) : empty();
                }),
            )
            .subscribe((eventData: EventData) => {
                currentData = eventData;
                this.showMenu(currentData);
            });

        escapist.escapeEvents()
            .pipe(takeUntil(this.disposed$))
            .subscribe(async _ => {
                this.hideMenu();
            });
    }

    private showMenu(eventData: EventData) {
        this.menuRef.style.display = 'block';
        this.updatePosition(eventData);
    }

    private hideMenu() {
        this.menuRef.style.display = '';
    }

    private updatePosition(eventData: EventData): void {
        const placement = this.getPlacement(eventData.element);
        const middleware: Middleware[] = [];
        if (eventData.coords) {
            middleware.push(inline({ x: eventData.coords.x, y: eventData.coords.y }));
        }
        // middleware.push(offset(6));
        // middleware.push(flip());
        // middleware.push(shift({ padding: 5 }));
        computePosition(eventData.element, this.menuRef, {
            // placement: placement,
            middleware: middleware,
        }).then(({ x, y }) => {

        });
    }

    private getPlacement(triggerRef: HTMLElement): Placement {
        const placement = triggerRef.dataset['menuPosition'];
        if (placement)
            return placement as Placement;
        return 'top';
    }
}
