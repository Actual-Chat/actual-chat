import { Disposable } from 'disposable';
import {
    fromEvent,
    Subject,
    takeUntil,
    map,
    switchMap,
    of,
    empty,
} from 'rxjs';
import {
    Placement,
    Middleware,
    computePosition,
    flip,
    shift,
    offset,
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

enum MenuTriggers
{
    None = 0,
    LeftClick = 1,
    RightClick = 2,
    LongClick = 4,
    Hover = 5,
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

    private get isMenuVisible() : boolean {
        return this.menuRef.style.display === 'block';
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
                map((event) => this.mapEvent(event, MenuTriggers.LeftClick, false, true)),
                switchMap((eventData: EventData | undefined) => {
                    return eventData ? of(eventData) : empty();
                }),
            )
            .subscribe((eventData: EventData) => {
                this.renderMenu(eventData);
            });

        fromEvent(document, 'contextmenu')
            .pipe(
                takeUntil(this.disposed$),
                map((event) => this.mapEvent(event, MenuTriggers.RightClick, true, false)),
                switchMap((eventData: EventData | undefined) => {
                    return eventData ? of(eventData) : empty();
                }),
            )
            .subscribe((eventData: EventData) => {
                this.renderMenu(eventData);
            });

        escapist.escapeEvents()
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => {
               this.hideMenu();
            });
    }

    private mapEvent(
        event: Event | PointerEvent,
        triggers: MenuTriggers,
        byCoords: boolean,
        closeOnSecondClick: boolean): EventData | undefined {
        if (!(event.target instanceof HTMLElement))
            return undefined;
        const closestElement = event.target.closest('[data-menu]');
        if (closestElement instanceof HTMLElement) {
            const menuTrigger = closestElement.dataset['menuTrigger'];
            if (!menuTrigger || !(this.hasTrigger(menuTrigger, triggers))) {
                if (this.isMenuVisible)
                    this.hideMenu();
                return undefined;
            }
            event.preventDefault();
            event.stopPropagation();
        }
        if (closestElement == null) {
            if (this.isMenuVisible)
                this.hideMenu();
            return undefined;
        }
        if (closestElement == this.currentData?.element) {
            if (this.isMenuVisible && closeOnSecondClick) {
                this.hideMenu();
                return undefined;
            } else if (!this.isMenuVisible) {
                this.showMenu();
                return undefined;
            }
        }
        if (!closestElement && this.currentData?.element) {
            this.hideMenu();
            return undefined;
        }
        if (!(closestElement instanceof HTMLElement))
            return undefined;
        const trigger = closestElement.dataset['menu'];
        const coords = byCoords && event instanceof PointerEvent
                       ? { x: event.clientX, y: event.clientY }
                       : undefined;
        const eventData: EventData = {
            trigger,
            element: closestElement,
            coords: coords,
        };
        return eventData;
    };

    private renderMenu(eventData: EventData): void {
        if (this.currentData) {
            this.hideMenu();
        }
        this.currentData = eventData;
        this.blazorRef.invokeMethodAsync('RenderMenu', eventData.trigger);
    }

    private hideMenu(): void {
        this.currentData = undefined;
        this.menuRef.style.display = 'none';
        this.blazorRef.invokeMethodAsync('HideMenu');
    }

    private updatePosition(eventData: EventData): void {
        this.menuRef.style.display = 'block';

        if (eventData.coords) {
            Object.assign(this.menuRef.style, {
                left: `${eventData.coords.x}px`,
                top: `${eventData.coords.y}px`,
            });

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
        });
    }

    private getPlacement(triggerRef: HTMLElement): Placement {
        const placement = triggerRef.dataset['menuPosition'];
        if (placement)
            return placement as Placement;
        return 'top';
    }

    private hasTrigger(trigger: string, triggers: MenuTriggers): boolean {
        return (Number(trigger) & triggers) === triggers;
    }
}
