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
    trigger: string;
    element: HTMLElement;
    coords?: Coords;
}

const LogScope = 'ContextMenu';

export class ContextMenu implements Disposable {
    private readonly disposed$: Subject<void> = new Subject<void>();
    private readonly menuRef: HTMLElement;

    private currentData: EventData | undefined = undefined;

    public static create(blazorRef: DotNet.DotNetObject): ContextMenu {
        return new ContextMenu(blazorRef);
    }

    constructor(
        private readonly blazorRef: DotNet.DotNetObject,
    ) {
        try {
            this.menuRef = document.getElementsByClassName('ac-menu')[0] as HTMLElement;
            this.listenForEvents();
        } catch (error) {
            console.error(`${LogScope}.ctor: error:`, error);
            this.dispose();
        }
    }

    public showMenu() {

        this.updatePosition(this.currentData);
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
        fromEvent(document, 'click')
            .pipe(
                takeUntil(this.disposed$),
                map((event: Event) => {
                    event.preventDefault();
                    event.stopPropagation();
                    console.log(event);
                    if (!(event.target instanceof HTMLElement))
                        return undefined;
                    const closestElement = event.target.closest('[data-menu]');
                    if (closestElement == this.currentData?.element)
                        return undefined;
                    if (!closestElement && this.currentData?.element) {
                        this.currentData = undefined;
                        this.hideMenu();
                        return undefined;
                    }
                    if (!(closestElement instanceof HTMLElement))
                        return undefined;
                    const trigger = closestElement.dataset['menu'];
                    const eventData: EventData = {
                        trigger,
                        element: closestElement,
                    };
                    return eventData;
                }),
                switchMap((eventData: EventData | undefined) => {
                    return eventData ? of(eventData) : empty();
                }),
            )
            .subscribe((eventData: EventData) => {
                this.currentData = eventData;
                this.renderMenu(this.currentData);
            });

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
                    if (closestElement == this.currentData?.element)
                        return undefined;
                    if (!closestElement && this.currentData?.element) {
                        this.currentData = undefined;
                        this.hideMenu();
                        return undefined;
                    }
                    if (!(closestElement instanceof HTMLElement))
                        return undefined;
                    const trigger = closestElement.dataset['menu'];
                    const eventData: EventData = {
                        trigger,
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
                this.currentData = eventData;
                this.renderMenu(this.currentData);
            });

        escapist.escapeEvents()
            .pipe(takeUntil(this.disposed$))
            .subscribe(async _ => {
                this.hideMenu();
            });
    }

    private renderMenu(eventData: EventData) {
        this.blazorRef.invokeMethodAsync('RenderMenu', eventData.trigger);
    }

    private hideMenu() {
        this.menuRef.style.display = '';
    }

    private updatePosition(eventData: EventData): void {
        if (eventData.coords) {
            Object.assign(this.menuRef.style, {
                left: `${eventData.coords.x}px`,
                top: `${eventData.coords.y}px`,
            });
            this.menuRef.style.display = 'block';

            return;
        }

        const placement = this.getPlacement(eventData.element);
        const middleware: Middleware[] = [];
        middleware.push(offset(6));
        middleware.push(flip());
        middleware.push(shift({ padding: 5 }));
        computePosition(eventData.element, this.menuRef, {
            placement: placement,
            middleware: middleware,
        }).then(({ x, y }) => {
            Object.assign(this.menuRef.style, {
                left: `${x}px`,
                top: `${y}px`,
            });
            this.menuRef.style.display = 'block';
        });
    }

    private getPlacement(triggerRef: HTMLElement): Placement {
        const placement = triggerRef.dataset['menuPosition'];
        if (placement)
            return placement as Placement;
        return 'top';
    }
}
