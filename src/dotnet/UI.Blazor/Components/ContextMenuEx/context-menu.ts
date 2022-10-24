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
    placement: Placement;
    trigger: string;
    closeOnSecondClick: boolean;
    isHover: boolean;
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
        const menu = this.menus.find(x => x.id === id);
        if (!menu)
            return;
        const menuRef = document.getElementById(id);
        if (!menuRef)
            return;
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

        fromEvent(document, 'mouseover')
            .pipe(
                takeUntil(this.disposed$),
                map((event) => this.mapHoverEvent(event)),
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
        if (!(closestElement instanceof HTMLElement)) {
            this.hideAllMenus();
            return undefined;
        }
        const menuTrigger = closestElement.dataset['menuTrigger'];
        if (!menuTrigger || !(ContextMenu.hasTrigger(menuTrigger, triggers))) {
            this.hideAllMenus();
            return undefined;
        }
        event.preventDefault();
        event.stopPropagation();
        const trigger = closestElement.dataset['menu'];
        const placement = ContextMenu.getPlacement(closestElement);
        const coords = byCoords && event instanceof PointerEvent
                       ? { x: event.clientX, y: event.clientY }
                       : undefined;
        const eventData: EventData = {
            placement,
            isHover: false,
            trigger,
            closeOnSecondClick,
            element: closestElement,
            coords: coords,
        };
        return eventData;
    }

    private mapHoverEvent(event: Event): EventData | undefined {
        if (!(event.target instanceof HTMLElement)) {
            this.hideMenus(x => x.isHover);
            return undefined;
        }
        const closestElement = event.target.closest('[data-hover-menu]');
        if (!(closestElement instanceof HTMLElement)) {
            const menu = event.target.closest('.ac-menu');
            if (!menu) {
                this.hideMenus(x => x.isHover);
            }
            return undefined;
        }
        const trigger = closestElement.dataset['hoverMenu'];
        const eventData: EventData = {
            placement: "top-end",
            trigger,
            isHover: true,
            closeOnSecondClick: false,
            element: closestElement,
            coords: undefined,
        };
        return eventData;
    }

    private renderMenu(eventData: EventData): void {
        const exisingMenuIndex = this.menus.findIndex(
            x => x.eventData.trigger == eventData.trigger
            && x.eventData.element == eventData.element);
        if (exisingMenuIndex > -1) {
            const exisingMenu = this.menus[exisingMenuIndex];
            if (exisingMenu.eventData.closeOnSecondClick) {
                this.menus.splice(exisingMenuIndex, 1);
                this.hideMenu(exisingMenu);
            } else {
                exisingMenu.eventData = eventData;
                ContextMenu.updatePosition(exisingMenu);
            }

            return;
        }

        this.hideMenus(x => x.isHover === eventData.isHover);

        const menu: Menu = {
            id: nanoid(),
            eventData: eventData,
        };
        this.menus.push(menu);
        this.blazorRef.invokeMethodAsync('RenderMenu', menu.eventData.trigger, menu.id);
    }

    private hideMenu(menu: Menu): void {
        if (menu.menuRef)
            menu.menuRef.style.display = 'none';

        this.blazorRef.invokeMethodAsync('HideMenu', menu.id);
    }

    private hideAllMenus(): void {
        while (this.menus.length) {
            this.hideMenu(this.menus.pop());
        }
    }

    private hideMenus(predicate: (e: EventData) => boolean): void {
        for (let i = 0; i < this.menus.length; i++) {
            if (predicate(this.menus[i].eventData)) {
                const removed = this.menus.splice(i, 1);
                this.hideMenu(removed[0]);
            }
        }
    }

    private static updatePosition(menu: Menu): void {
        if (!menu.menuRef)
            return;
        menu.menuRef.style.display = 'block';

        if (menu.eventData.coords) {
            Object.assign(menu.menuRef.style, {
                left: `${menu.eventData.coords.x}px`,
                top: `${menu.eventData.coords.y}px`,
            });

            return;
        }

        const middleware: Middleware[] = [];
        if (menu.eventData.isHover) {
            middleware.push(offset({
               mainAxis: -15,
               crossAxis: -10,
           }));
            middleware.push(flip());
        } else {
            middleware.push(offset(6));
            middleware.push(flip());
            middleware.push(shift({ padding: 5 }));
        }
        computePosition(menu.eventData.element, menu.menuRef, {
            placement: menu.eventData.placement,
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
}
