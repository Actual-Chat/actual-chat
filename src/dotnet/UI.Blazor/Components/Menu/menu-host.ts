import {
    fromEvent,
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
import { delayAsync, nextTick } from 'promises';
import { Log, LogLevel } from 'logging';

import { HistoryUI, HistoryStepId } from '../../Services/HistoryUI/history-ui';
import Escapist from '../../Services/Escapist/escapist';
import { ScreenSize } from '../../Services/ScreenSize/screen-size';
import { Vibration } from '../../Services/Vibration/vibration';

const LogScope = 'MenuHost';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

interface Vector2D {
    x: number;
    y: number;
}

enum MenuTriggers {
    None = 0,
    LeftClick = 1,
    RightClick = 2,
    LongClick = 4,
}

interface Menu {
    id: string;
    menuRef: string;
    referenceElement: HTMLElement;
    isHoverMenu: boolean;
    placement: Placement;
    position: Vector2D | null;
    event: Event | null;
    time: number;
    historyStepId: HistoryStepId | null;
    menuElement: HTMLElement | null;
}

export class MenuHost implements Disposable {
    private readonly skipClickEventPeriodMs = 350;
    private readonly hoverMenuDelayMs = 50;
    private readonly disposed$: Subject<void> = new Subject<void>();
    private menu: Menu | null;
    private isHistoryEnabled: boolean = true;

    public static create(blazorRef: DotNet.DotNetObject): MenuHost {
        return new MenuHost(blazorRef);
    }

    constructor(private readonly blazorRef: DotNet.DotNetObject) {
        debugLog?.log('constructor')
        merge(
            fromEvent(document, 'click'),
            fromEvent(document, 'long-press'),
            fromEvent(document, 'contextmenu')
            )
            .pipe(takeUntil(this.disposed$))
            .subscribe((event) => this.onClick(event));

        fromEvent(document, 'mouseover')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event) => this.onMouseOver(event));

        Escapist.escape$
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: KeyboardEvent) => {
                if (this.menu != null) {
                    event.stopImmediatePropagation();
                    event.preventDefault();
                    this.hide();
                }
            });
    }

    public dispose(): void {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    public get isDesktopMode(): boolean {
        return ScreenSize.isWide();
    }

    public showOrPosition(
        menuRef: string,
        isHoverMenu: boolean,
        referenceElement: HTMLElement | string,
        placement?: Placement | null,
        position?: Vector2D | null,
        event?: Event | null,
    ): void {
        let menu = this.create(menuRef, isHoverMenu, referenceElement, placement, position, event);
        if (this.isShown(menu)) {
            this.menu.placement = menu.placement;
            this.menu.position = menu.position;
            this.menu.event = menu.event;
            this.menu.time = menu.time;
            void updatePosition(this.menu);
            return;
        }
        this.render(menu);
    }

    public hideById(id: string): void {
        const menu = this.menu;
        if (!menu || menu.id !== id) {
            warnLog?.log('hideById: no menu with id:', id)
            return;
        }

        this.hide();
    }

    public async position(id: string): Promise<void> {
        const menu = this.menu;
        if (!menu || menu.id !== id) {
            warnLog?.log('position: no menu with id:', id)
            return;
        }

        if (menu.isHoverMenu && !menu.menuElement) {
            // This is the very first render of hover menu
            await delayAsync(this.hoverMenuDelayMs);
        }

        menu.menuElement = document.getElementById(menu.id);
        void updatePosition(menu);
    }

    // Private methods

    private create(
        menuRef: string,
        isHoverMenu: boolean,
        referenceElement: HTMLElement | string,
        placement: Placement | null,
        position: Vector2D | null,
        event: Event | null,
    ): Menu {
        if (!(referenceElement instanceof HTMLElement)) {
            const referenceElementId = referenceElement as string;
            referenceElement = document.getElementById(referenceElementId);
        }
        placement = placement ?? getPlacement(referenceElement);
        return {
            id: nextId(),
            menuRef: menuRef,
            referenceElement: referenceElement,
            isHoverMenu: isHoverMenu,
            placement: placement,
            position: position,
            event: event,
            time: Date.now(),
            historyStepId: null,
            menuElement: null,
        };
    }

    private isShown(menu: Menu) {
        let m = this.menu;
        return m
            && m.menuRef === menu.menuRef
            && m.referenceElement === menu.referenceElement
            && m.isHoverMenu === menu.isHoverMenu;
    }

    private render(menu: Menu): void {
        debugLog?.log('render:', menu)
        if (!menu)
            throw `${LogScope}.render: menu == null.`;

        let oldMenu = this.menu;
        this.menu = menu;

        // Maybe add history step
        if (this.isHistoryEnabled && !menu.historyStepId && !menu.isHoverMenu) {
            if (oldMenu?.historyStepId)
                menu.historyStepId = oldMenu.historyStepId;
            else
                menu.historyStepId = HistoryUI.pushBackStep(true, () => this.hide());
            debugLog?.log('render: added history step', history.state);
        }

        this.blazorRef.invokeMethodAsync('OnRenderRequest', menu.id, menu.menuRef, menu.isHoverMenu);
        if (ScreenSize.isNarrow())
            Vibration.vibrate();
    }

    private hide(options?: {
        id?: string,
        isHoverMenu?: boolean,
    }): void {
        debugLog?.log('hide, options:', options);
        const menu = this.menu;
        if (!menu)
            return;
        if (options) {
            if (options.id !== undefined && menu.id !== options.id)
                return;
            if (options.isHoverMenu !== undefined && menu.isHoverMenu !== options.isHoverMenu)
                return;
        }

        this.menu = null;

        // Remove history step
        if (this.isHistoryEnabled && menu?.historyStepId) {
            const historyStepId = menu.historyStepId;
            menu.historyStepId = null;
            if (HistoryUI.isCurrentStep(historyStepId)) {
                history.back();
                debugLog?.log('hide: removed history step');
            } else {
                debugLog?.log('hide: history step has already been replaced');
            }
        }

        // Hide (un-render) it
        this.blazorRef.invokeMethodAsync('OnHideRequest', menu.id);
    }

    // Event handlers

    private onClick(event: Event): void {
        let trigger = MenuTriggers.None
        if (event.type == 'click')
            trigger = MenuTriggers.LeftClick;
        if (event.type == 'long-press')
            trigger = MenuTriggers.LongClick;
        if (event.type == 'contextmenu')
            trigger = MenuTriggers.RightClick;
        debugLog?.log('onClick, event:', event, ', trigger:', trigger);

        let isDesktopMode = this.isDesktopMode;

        // Ignore clicks which definitely aren't "ours"
        if (trigger == MenuTriggers.None)
            return;
        if (!(event.target instanceof Element))
            return;

        // Ignore long clicks on desktop: they don't provide pointer position -> menu can't be properly positioned
        if (trigger == MenuTriggers.LongClick && isDesktopMode)
            return;

        // Suppress browser context menu anywhere but on images
        if (trigger == MenuTriggers.RightClick && event.target.nodeName !== 'IMG')
            event.preventDefault();

        const isClickInsideMenu = event.target.closest('.ac-menu, .ac-menu-hover') != null;
        if (isClickInsideMenu) {
            // The menu will process the action, but we can schedule menu hiding here
            nextTick(() => this.hide({ id: this.menu.id }));
            return;
        }

        // We know here the click is outside of any menu

        let triggerElement = event.target.closest('[data-menu]');
        let menuRef = null;
        if ((triggerElement instanceof HTMLElement)) {
            const menuTrigger = triggerElement.dataset['menuTrigger'];
            if (menuTrigger && hasTrigger(menuTrigger, trigger))
                menuRef = triggerElement.dataset['menu'];
        }

        if (!menuRef) {
            // It's a click outside of any menu which doesn't trigger another menu
            this.hide({ isHoverMenu: false });
            event.stopImmediatePropagation();
            event.preventDefault();
            return;
        }

        const position = isDesktopMode && event instanceof PointerEvent
            ? { x: event.clientX, y: event.clientY }
            : null;
        this.showOrPosition(menuRef, false, triggerElement as HTMLElement, null, position, event);
        event.stopImmediatePropagation();
        event.preventDefault();
    }

    private async onMouseOver(event: Event): Promise<void> {
        // Hover menus work only in desktop mode
        if (!this.isDesktopMode)
            return;

        // Hover menus shouldn't be shown when non-hover menu is shown
        if (this.menu?.isHoverMenu === false)
            return;

        // Ignore hovers which definitely aren't "ours"
        if (!(event.target instanceof Element)) {
            this.hide({ isHoverMenu: true });
            return;
        }

        const triggerElement = event.target.closest('[data-hover-menu]');
        if (!(triggerElement instanceof HTMLElement)) {
            const isInsideHoverMenu = event.target.closest('.ac-menu-hover') != null;
            if (!isInsideHoverMenu)
                this.hide({ isHoverMenu: true });
            return;
        }

        const menuRef = triggerElement.dataset['hoverMenu'];
        const menu = this.create(menuRef, true, triggerElement, "top-end", null, event);
        if (this.isShown(menu))
            return;

        this.render(menu);
    }
}

// Helpers

let _nextId = 1;
// Menu Ids are used as HTML element Ids, so they need to have unique prefix
let nextId = () => 'menu:' + (_nextId++).toString();

function hasTrigger(trigger: string, triggers: MenuTriggers): boolean {
    return (Number(trigger) & triggers) === triggers;
}

function getPlacement(referenceElement: HTMLElement): Placement {
    const placement = referenceElement.dataset['menuPlacement'];
    if (placement)
        return placement as Placement;

    return 'top';
}

async function updatePosition(menu: Menu): Promise<void> {
    if (!menu.menuElement)
        return;

    debugLog?.log(`updatePosition, menu:`, menu);
    menu.menuElement.style.display = 'block';

    let referenceElement: ReferenceElement;
    const middleware: Middleware[] = [];
    const position = menu.position;
    if (position) {
        referenceElement = {
            getBoundingClientRect() {
                return {
                    width: 0,
                    height: 0,
                    x: position.x,
                    y: position.y,
                    top: position.y,
                    left: position.x,
                    right: position.x,
                    bottom: position.y,
                };
            },
        } as VirtualElement;
        middleware.push(flip());
        middleware.push(shift({ padding: 5 }));
    } else if (menu.isHoverMenu) {
        referenceElement = menu.referenceElement;
        middleware.push(offset({ mainAxis: -15, crossAxis: -10 }));
        middleware.push(flip());
    } else {
        referenceElement = menu.referenceElement;
        middleware.push(offset(6));
        middleware.push(flip());
        middleware.push(shift({ padding: 5 }));
    }
    const { x, y } = await computePosition(
        referenceElement,
        menu.menuElement,
        {
            placement: menu.placement,
            middleware: middleware,
        });
    Object.assign(menu.menuElement.style, {
        left: `${x}px`,
        top: `${y}px`,
    });
}
