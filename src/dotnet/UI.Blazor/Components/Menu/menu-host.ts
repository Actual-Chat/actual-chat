import {
    delay,
    filter,
    fromEvent,
    map,
    merge,
    Subject,
    takeUntil,
} from 'rxjs';
import {
    computePosition,
    flip,
    Middleware,
    offset,
    Placement,
    ReferenceElement,
    shift,
    VirtualElement,
} from '@floating-ui/dom';
import { Disposable } from 'disposable';
import { nanoid } from 'nanoid';
import { nextTick } from 'promises';
import { Log, LogLevel } from 'logging';

import { HistoryUI, HistoryStepId } from '../../Services/HistoryUI/history-ui';
import Escapist from '../../Services/Escapist/escapist';
import { ScreenSize } from '../../Services/ScreenSize/screen-size';
import { Vibration } from '../../Services/Vibration/vibration';

const LogScope = 'MenuHost';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

interface Coords {
    x: number;
    y: number;
}

interface EventData {
    event: Event;
    menuRef: string;
    isHoverMenu: boolean;
    placement: Placement;
    element: HTMLElement;
    coords?: Coords;
    time: number;
}

interface Menu {
    id: string;
    eventData: EventData;
    elementRef?: HTMLElement;
}

enum MenuTriggers {
    None = 0,
    LeftClick = 1,
    RightClick = 2,
    LongClick = 4,
}

export class MenuHost implements Disposable {
    private readonly skipClickEventPeriodMs = 350;
    private readonly disposed$: Subject<void> = new Subject<void>();
    private menus: Menu[] = [];
    private preventHistoryModification: boolean;
    private historyStepId: HistoryStepId;

    public static create(blazorRef: DotNet.DotNetObject): MenuHost {
        return new MenuHost(blazorRef);
    }

    constructor(
        private readonly blazorRef: DotNet.DotNetObject,
    ) {
        merge(
            fromEvent(document, 'click', { capture: true }),
            fromEvent(document, 'long-press'),
            fromEvent(document, 'contextmenu', { capture: true }),
            )
            .pipe(
                takeUntil(this.disposed$),
                map((event) => this.mapClickEvent(event)),
                filter(eventData => eventData !== undefined),
            ).subscribe((eventData: EventData) => this.renderMenu(eventData));

        fromEvent(document, 'mouseover')
            .pipe(
                takeUntil(this.disposed$),
                map(event => this.mapHoverEvent(event)),
                filter(eventData => eventData !== undefined),
                delay(100),
            )
            .subscribe((eventData: EventData) => this.renderMenu(eventData));

        Escapist.escape$
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: KeyboardEvent) => {
                if (this.hideAllMenus()) {
                    event.preventDefault();
                }
            });
    }

    public dispose(): void {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
        this.menus = [];
    }

    public showMenuById(id: string): void {
        const menu = this.menus.find(x => x.id === id);
        if (!menu)
            return;

        const elementRef = document.getElementById(id);
        if (!elementRef)
            return;

        menu.elementRef = elementRef;
        void updatePosition(menu);
    }

    public hideMenuById(id: string): void {
        const menu = this.menus.find(x => x.id === id);
        if (!menu)
            return;

        this.hideMenu(menu);
    }

    public get isDesktopMode(): boolean {
        return ScreenSize.isWide();
    }

    // Private methods

    private renderMenu(eventData: EventData): void {
        debugLog?.log('renderMenu: eventData:', eventData)
        const menuIndex = this.menus.findIndex(
            x => x.eventData.menuRef == eventData.menuRef
            && x.eventData.element == eventData.element);
        if (menuIndex >= 0) {
            const menu = this.menus[menuIndex];
            if (Date.now() - menu.eventData.time < this.skipClickEventPeriodMs) {
                // We ignore all subsequent events within certain interval:
                // they're either other "flavours" of the same event,
                // or simply double / or mis-clicks.
                return;
            }

            menu.eventData = eventData;
            void updatePosition(menu);
            return;
        }

        /* NOTE(AY): Not sure what this block is supposed to do due to above "if"
        try {
            this.preventHistoryModification = true;
            this.hideMenus(x => x.isHoverMenu === eventData.isHoverMenu);
        }
        finally {
            this.preventHistoryModification = false;
        }
        */

        const menu: Menu = {
            id: nanoid(),
            eventData: eventData,
        };
        this.menus.push(menu);
        if (!eventData.isHoverMenu && !this.historyStepId) {
            this.historyStepId = HistoryUI.pushBackStep(true, this.hideMenusOnBack);
            debugLog?.log(`renderMenu: pushed state to render menu`, history.state);
        }
        this.blazorRef.invokeMethodAsync('OnRenderMenu', menu.eventData.menuRef, menu.id, eventData.isHoverMenu);
        if (ScreenSize.isNarrow())
            Vibration.vibrate();
    }

    private hideMenusOnBack = (): void => {
        debugLog?.log('hideMenusOnBack()');
        if (this.menus.length)
            this.hideNonHoverMenus();
    }

    private tryHideOverlay(): void {
        if (this.menus.length)
            return;

        const overlay = document.getElementsByClassName('ac-menu-overlay')[0] as HTMLElement;
        if (!overlay)
            return;

        overlay.style.display = 'none';
    }

    private removeHistoryStep(): void {
        if (this.preventHistoryModification || !this.historyStepId)
            return;
        if (this.menus.filter(m => !m.eventData.isHoverMenu).length)
            return;

        const goBack = HistoryUI.isActiveStep(this.historyStepId);
        this.historyStepId = undefined;
        if (goBack) {
            history.back();
            debugLog?.log(`removeHistoryStep(): removed history back step on hide menu`);
        } else {
            debugLog?.log(`removeHistoryStep(): history back step has already been replaced`);
        }
    }

    private hideMenu(menu: Menu): boolean {
        debugLog?.log(`hideMenu: menu:`, menu);
        if (menu.elementRef)
            menu.elementRef.style.display = 'none';

        const menuIndex = this.menus.indexOf(menu);
        if (menuIndex >= 0)
            this.menus.splice(menuIndex, 1);

        this.tryHideOverlay();
        this.removeHistoryStep();

        this.blazorRef.invokeMethodAsync('OnHideMenu', menu.id);
        return menuIndex >= 0;
    }

    private hideAllMenus(): boolean {
        debugLog?.log(`hideAllMenus()`);
        const count = this.menus.length;
        for (let i = count - 1; i >= 0; i--) {
            const menu = this.menus[i];
            this.hideMenu(menu)
        }
        return count != 0;
    }

    private hideHoverMenus(): boolean {
        debugLog?.log(`hideHoverMenus()`);
        if (this.menus.length == 0) // Just to speed up typical case
            return false;

        return this.hideMenus(m => m.isHoverMenu);
    }

    private hideNonHoverMenus(): boolean {
        debugLog?.log(`hideNonHoverMenus()`);
        if (this.menus.length == 0) // Just to speed up typical case
            return false;

        return this.hideMenus(m => !m.isHoverMenu);
    }

    private hideMenus(predicate: (e: EventData) => boolean): boolean {
        let result = false;
        for (let i = this.menus.length - 1; i >= 0; i--) {
            const menu = this.menus[i];
            if (predicate(menu.eventData)) {
                this.hideMenu(menu);
                result = true;
            }
        }
        return result;
    }

    private mapClickEvent(event: Event | PointerEvent): EventData | undefined {
        let trigger = MenuTriggers.None
        if (event.type == 'click')
            trigger = MenuTriggers.LeftClick;
        if (event.type == 'long-press')
            trigger = MenuTriggers.LongClick;
        if (event.type == 'contextmenu')
            trigger = MenuTriggers.RightClick;
        debugLog?.log('mapClickEvent, event:', event, ', trigger:', trigger);

        const tryHideNonHoverMenus = (mustHandleAnyway: boolean = false): undefined => {
            if (this.hideNonHoverMenus() || mustHandleAnyway) {
                event.stopPropagation();
                event.preventDefault();
            }
            return undefined;
        }

        let isDesktopMode = this.isDesktopMode;

        // Ignore clicks which definitely aren't "ours"
        if (trigger == MenuTriggers.None)
            return undefined;
        if (!(event.target instanceof Element))
            return undefined;

        // Ignore long clicks on desktop: they don't provide pointer coords -> menus can't be properly positioned
        if (trigger == MenuTriggers.LongClick && isDesktopMode)
            return undefined;

        // Suppress browser context menu anywhere but on images
        if (trigger == MenuTriggers.RightClick && event.target.nodeName !== 'IMG')
            event.preventDefault();

        // Process overlay click - useless, coz we anyway process clicks outside menus below
        /*
        const isOverlayClick = event.target.classList.contains('ac-menu-overlay');
        if (isOverlayClick)
            return tryHideNonHoverMenus(true);
        */

        const isClickInsideMenu = event.target.closest('.ac-menu, .ac-menu-hover') != null;
        if (isClickInsideMenu) {
            if (trigger == MenuTriggers.RightClick) {
                // Right click may follow long click events, so we definitely need to suppress them inside menus
                event.stopPropagation();
                return undefined;
            }

            // The menu will process the action, but we can schedule menu hiding here
            nextTick(() => this.hideAllMenus());
            return undefined;
        }

        // We know here the click is outside of any menu.
        // Are there any other non-hover menus visible?
        if (this.menus.find(m => !m.eventData.isHoverMenu))
            return tryHideNonHoverMenus(true);

        const closestElement = event.target.closest('[data-menu]');
        if (!(closestElement instanceof HTMLElement))
            return undefined;

        const menuTrigger = closestElement.dataset['menuTrigger'];
        if (!menuTrigger || !(hasTrigger(menuTrigger, trigger)))
            return undefined;

        event.preventDefault();
        if (isDesktopMode)
            this.hideHoverMenus(); // Hide all hover menus on appearance of non-hover one

        const menuRef = closestElement.dataset['menu'];
        const placement = getPlacement(closestElement);
        const coords =
            isDesktopMode && event instanceof PointerEvent
               ? { x: event.clientX, y: event.clientY }
               : undefined;
        return {
            event: event,
            menuRef: menuRef,
            isHoverMenu: false,
            placement,
            element: closestElement,
            coords: coords,
            time: Date.now(),
        };
    }

    private mapHoverEvent(event: Event): EventData | undefined {
        if (!this.isDesktopMode)
            return undefined;

        const tryHideHoverMenus = (): undefined => {
            this.hideHoverMenus();
            return undefined;
        }

        if (!(event.target instanceof Element))
            return tryHideHoverMenus();

        const closestElement = event.target.closest('[data-hover-menu]');
        if (!(closestElement instanceof HTMLElement)) {
            const isInsideHoverMenu = event.target.closest('.ac-menu-hover') != null;
            return isInsideHoverMenu ? undefined : tryHideHoverMenus();
        }

        const menuRef = closestElement.dataset['hoverMenu'];
        const shownHoverMenu = this.menus.find(x => x.eventData.isHoverMenu);
        if (shownHoverMenu && shownHoverMenu.eventData.menuRef === menuRef)
            return undefined; // On top of shown hover menu trigger

        tryHideHoverMenus();
        return {
            event: event,
            menuRef: menuRef,
            isHoverMenu: true,
            placement: "top-end",
            element: closestElement,
            coords: undefined,
            time: Date.now(),
        };
    }
}

function hasTrigger(trigger: string, triggers: MenuTriggers): boolean {
    return (Number(trigger) & triggers) === triggers;
}

function getPlacement(triggerRef: HTMLElement): Placement {
    const placement = triggerRef.dataset['menuPosition'];
    if (placement)
        return placement as Placement;

    return 'top';
}

async function updatePosition(menu: Menu): Promise<void> {
    if (!menu.elementRef)
        return;

    debugLog?.log(`updatePosition, menu:`, menu);
    menu.elementRef.style.display = 'block';

    let reference: ReferenceElement;
    const middleware: Middleware[] = [];
    if (menu.eventData.coords) {
        const virtualElement: VirtualElement = {
            getBoundingClientRect() {
                return {
                    width: 0,
                    height: 0,
                    x: menu.eventData.coords.x,
                    y: menu.eventData.coords.y,
                    top: menu.eventData.coords.y,
                    left: menu.eventData.coords.x,
                    right: menu.eventData.coords.x,
                    bottom: menu.eventData.coords.y,
                };
            },
        };
        reference = virtualElement;
        middleware.push(flip());
        middleware.push(shift({ padding: 5 }));
    } else if (menu.eventData.isHoverMenu) {
        reference = menu.eventData.element;
        middleware.push(offset({ mainAxis: -15, crossAxis: -10 }));
        middleware.push(flip());
    } else {
        reference = menu.eventData.element;
        middleware.push(offset(6));
        middleware.push(flip());
        middleware.push(shift({ padding: 5 }));
    }
    const { x, y } = await computePosition(
        reference,
        menu.elementRef,
        {
            placement: menu.eventData.placement,
            middleware: middleware,
        });
    Object.assign(menu.elementRef.style, {
        left: `${x}px`,
        top: `${y}px`,
    });
}
