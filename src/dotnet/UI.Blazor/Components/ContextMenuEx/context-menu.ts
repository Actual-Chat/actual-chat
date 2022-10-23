import { Disposable } from 'disposable';
import { nanoid } from 'nanoid';
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

interface Menu {
    id: string;
    eventData: EventData;
    menuRef?: HTMLElement;
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
    private menus: Menu[] = [];

    public static create(blazorRef: DotNet.DotNetObject): ContextMenu {
        return new ContextMenu(blazorRef);
    }

    constructor(
        private readonly blazorRef: DotNet.DotNetObject,
    ) {
        try {
            this.listenForEvents();
        } catch (error) {
            console.error(`${LogScope}.ctor: error:`, error);
            this.dispose();
        }
    }

    public showMenu(id: string) {
        const menuRef = document.getElementById(id);
        const menu = this.menus.find(x => x.id === id);
        menu.menuRef = menuRef;
        ContextMenu.updatePosition(menu);
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();

        this.menus = [];
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
                if (this.menus.length) {
                    this.hideMenu(this.menus.pop());
                }
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
        if (!(closestElement instanceof HTMLElement))
            return undefined;
        const menuTrigger = closestElement.dataset['menuTrigger'];
        if (!menuTrigger || !(ContextMenu.hasTrigger(menuTrigger, triggers))) {
            return undefined;
        }
        event.preventDefault();
        event.stopPropagation();
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
        const menu: Menu = {
            id: nanoid(),
            eventData: eventData,
        };
        this.menus.push(menu);
        this.blazorRef.invokeMethodAsync('RenderMenu', menu.eventData.trigger, menu.id);
    }

    private hideMenu(menu: Menu): void {
        menu.menuRef.style.display = 'none';
        this.blazorRef.invokeMethodAsync('HideMenu', menu.id);
    }

    private static updatePosition(menu: Menu): void {
        menu.menuRef.style.display = 'block';

        if (menu.eventData.coords) {
            Object.assign(menu.menuRef.style, {
                left: `${menu.eventData.coords.x}px`,
                top: `${menu.eventData.coords.y}px`,
            });

            return;
        }

        const placement = this.getPlacement(menu.eventData.element);
        const middleware: Middleware[] = [];
        middleware.push(offset(6));
        middleware.push(flip());
        middleware.push(shift({ padding: 5 }));
        computePosition(menu.eventData.element, menu.menuRef, {
            placement: placement,
            middleware: middleware,
        }).then(({ x, y }) => {
            Object.assign(menu.menuRef.style, {
                left: `${x}px`,
                top: `${y}px`,
            });
        });
    }

    private static getPlacement(triggerRef: HTMLElement): Placement {
        const placement = triggerRef.dataset['menuPosition'];
        if (placement)
            return placement as Placement;
        return 'top';
    }

    private static hasTrigger(trigger: string, triggers: MenuTriggers): boolean {
        return (Number(trigger) & triggers) === triggers;
    }

    private static isMenuVisible(menuRef: HTMLElement) : boolean {
        return menuRef.style.display === 'block';
    }
}
