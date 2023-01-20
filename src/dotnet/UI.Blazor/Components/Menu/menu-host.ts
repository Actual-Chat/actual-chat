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
import { DocumentEvents, endEvent } from 'event-handling';
import { Vector2D } from 'math';
import { delayAsync } from 'promises';
import { nextTick } from 'timeout';
import { Log, LogLevel } from 'logging';

import Escapist from '../../Services/Escapist/escapist';
import { HistoryUI } from '../../Services/HistoryUI/history-ui';
import { ScreenSize } from '../../Services/ScreenSize/screen-size';
import { VibrationUI } from '../../Services/VibrationUI/vibration-ui';
import { getOrInheritData } from '../../../../nodejs/src/dom-helpers';

const LogScope = 'MenuHost';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

enum MenuTrigger {
    None = 0,
    Primary = 1,
    Secondary = 2,
}

interface Menu {
    id: string;
    menuRef: string;
    triggerElement: HTMLElement;
    isHoverMenu: boolean;
    placement: Placement;
    position: Vector2D | null;
    historyStepId: string | null;
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
            DocumentEvents.active.click$,
            DocumentEvents.active.contextmenu$,
            )
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: MouseEvent) => this.onClick(event));

        DocumentEvents.passive.pointerOver$
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: PointerEvent) => this.onPointerOver(event));

        Escapist.event$
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: KeyboardEvent) => {
                if (this.menu != null) {
                    endEvent(event);
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
        triggerElement: HTMLElement | string,
        placement?: Placement | null,
        position?: Vector2D | null,
    ): void {
        let menu = this.create(menuRef, isHoverMenu, triggerElement, placement, position);
        if (this.isShown(menu))
            void this.position(this.menu, menu);
        else
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

    public async positionById(id: string): Promise<void> {
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
        void this.position(menu);
    }

    // Private methods

    private create(
        menuRef: string,
        isHoverMenu: boolean,
        triggerElement: HTMLElement | string,
        placement: Placement | null,
        position: Vector2D | null,
    ): Menu {
        if (!(triggerElement instanceof HTMLElement)) {
            const triggerElementId = triggerElement as string;
            triggerElement = document.getElementById(triggerElementId);
        }
        placement = placement ?? getPlacementFromAttributes(triggerElement);
        return {
            id: nextId(),
            menuRef: menuRef,
            triggerElement: triggerElement,
            isHoverMenu: isHoverMenu,
            placement: placement,
            position: position,
            historyStepId: null,
            menuElement: null,
        };
    }

    private isShown(menu: Menu) {
        let m = this.menu;
        return m
            && m.menuRef === menu.menuRef
            && m.triggerElement === menu.triggerElement
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
            VibrationUI.vibrate();
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

    private async position(menu: Menu, updatedMenu?: Menu): Promise<void> {
        if (!menu)
            throw `${LogScope}.position: menu == null.`;

        if (updatedMenu) {
            menu.menuElement = updatedMenu.menuElement ?? menu.menuElement;
            menu.placement = updatedMenu.placement ?? menu.placement;
            menu.position = updatedMenu.position ?? menu.position;
        }

        let menuElement = menu.menuElement;
        if (!menuElement)
            return;

        debugLog?.log(`position: menu:`, menu);
        if (menuElement.style.display != 'block')
            menuElement.style.display = 'block'

        let referenceElement: ReferenceElement;
        const middleware: Middleware[] = [];
        const position = menu.position;
        if (menu.isHoverMenu) {
            // Hover menu positioning
            referenceElement = menu.triggerElement;
            middleware.push(offset({ mainAxis: -15, crossAxis: -10 }));
            middleware.push(flip());
        } else if (position && !isButtonTrigger(menu.triggerElement)) {
            // Pointer relative positioning
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
        } else {
            // Trigger element relative positioning
            referenceElement = menu.triggerElement;
            middleware.push(offset(6));
            middleware.push(flip());
            middleware.push(shift({ padding: 5 }));
        }
        const { x, y } = await computePosition(
            referenceElement,
            menuElement,
            {
                placement: menu.placement ?? 'top',
                middleware: middleware,
            });
        Object.assign(menuElement.style, {
            left: `${x}px`,
            top: `${y}px`,
        });
    }

    // Event handlers

    private onClick(event: Event): void {
        let trigger = MenuTrigger.None
        if (event.type == 'click')
            trigger = MenuTrigger.Primary;
        if (event.type == 'contextmenu')
            trigger = MenuTrigger.Secondary;
        debugLog?.log('onClick, event:', event, ', trigger:', trigger);

        let isDesktopMode = this.isDesktopMode;

        // Ignore clicks which definitely aren't "ours"
        if (trigger == MenuTrigger.None)
            return;
        if (!(event.target instanceof Element))
            return;

        let [triggerElement, menuRef] = getOrInheritData(event.target, 'menu');
        if (triggerElement && menuRef) {
            const menuTrigger = MenuTrigger[triggerElement.dataset['menuTrigger'] ?? 'Secondary'];
            if (trigger !== menuTrigger) {
                const altMenuTrigger = menuTrigger == MenuTrigger.Primary ? MenuTrigger.Secondary : MenuTrigger.None;
                if (!isDesktopMode || trigger != altMenuTrigger)
                    menuRef = null;
            }
        }

        if (!menuRef) {
            // We couldn't find any menu to activate on click
            const isClickInsideMenu = event.target.closest('.ac-menu, .ac-menu-hover') != null;
            if (isClickInsideMenu) {
                // The menu will process the action, but we can schedule menu hiding here
                nextTick(() => this.hide({ id: this.menu.id }));
                return;
            }

            // It's a click outside of any menu which doesn't trigger another menu
            if (!this.menu || this.menu.isHoverMenu)
                return; // There are no visible menu (unless it's a hover menu)

            // Non-hover menu is visible, so we need to hide it on this click
            this.hide();
            return endEvent(event);
        }

        const position = isDesktopMode && event instanceof PointerEvent
            ? new Vector2D(event.clientX, event.clientY)
            : null;
        const menu = this.create(menuRef, false, triggerElement, null, position);
        if (this.isShown(menu)) {
            // Is it the second click on the same button that triggered the menu?
            if (triggerElement.nodeName == 'BUTTON')
                this.hide();
            else
                void this.position(this.menu, menu)
        }
        else
            this.render(menu);

        endEvent(event);
    }

    private async onPointerOver(event: Event): Promise<void> {
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

        const [triggerElement, menuRef] = getOrInheritData(event.target, 'hoverMenu');
        if (!menuRef) {
            const isInsideHoverMenu = event.target.closest('.ac-menu-hover') != null;
            if (!isInsideHoverMenu)
                this.hide({ isHoverMenu: true });
            return;
        }

        const menu = this.create(menuRef, true, triggerElement, "top-end", null);
        if (this.isShown(menu))
            return;

        this.render(menu);
    }
}

// Helpers

let _nextId = 1;
// Menu Ids are used as HTML element Ids, so they need to have unique prefix
let nextId = () => 'menu:' + (_nextId++).toString();

function getPlacementFromAttributes(triggerElement: HTMLElement): Placement | null {
    const placement = triggerElement.dataset['menuPlacement'];
    return placement?.length > 0 ? placement as Placement : null;
}

function isButtonTrigger(triggerElement: HTMLElement | null): boolean {
    if (!triggerElement)
        return false;

    if (!(triggerElement.closest('button') instanceof HTMLElement))
        return false;

    // Buttons inside menus aren't counted as triggers
    return triggerElement.closest('.ac-menu, .ac-menu-hover') == null;
}
