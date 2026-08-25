import { fromEvent, merge, Subject, takeUntil } from 'rxjs';
import {
    computePosition,
    flip,
    limitShift,
    Middleware,
    offset,
    Placement,
    ReferenceElement,
    shift,
    SideObject,
    VirtualElement,
} from '@floating-ui/dom';
import { Disposable } from 'disposable';
import { DocumentEvents, stopEvent } from 'event-handling';
import { getOrInheritData } from 'dom-helpers';
import { delayAsync } from 'actuallab-core';
import { nextTick } from 'timeout';
import { Vector2D } from 'math';
import { ScreenSize } from '../../Services/ScreenSize/screen-size';
import { ScreenOrientation } from 'orientation';
import { getLogs } from 'logging';
import { unselect } from 'keyboard';
import { Tune, TuneUI } from '../../Services/TuneUI/tune-ui';

const {  logScope, debugLog } = getLogs('MenuHost');
// TODO: remove eslint ignores and fix errors
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
    focused: boolean;
}

export class MenuHost implements Disposable {
    private readonly hoverMenuDelayMs = 50;
    private readonly disposed$: Subject<void> = new Subject<void>();
    private menu: Menu | null;
    private currentMenuRef: string;

    public static create(blazorRef: DotNet.DotNetObject): MenuHost {
        return new MenuHost(blazorRef);
    }

    constructor(private readonly blazorRef: DotNet.DotNetObject) {
        debugLog?.log('constructor');
        merge(
            DocumentEvents.active.click$,
            DocumentEvents.active.contextmenu$,
        )
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: MouseEvent) => this.onClick(event));

        DocumentEvents.passive.pointerOver$
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: PointerEvent) => this.onPointerOver(event));

        fromEvent<KeyboardEvent>(window, 'keydown')
            .pipe(takeUntil(this.disposed$))
            .subscribe((event: KeyboardEvent) => this.onKeyDown(event));
        ScreenOrientation.change$
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => {
                if (!this.menu)
                    return;

                const { menuRef, triggerElement, isHoverMenu } = this.menu;
                const menu = this.create(menuRef, isHoverMenu, triggerElement, null, null);
                void this.position(this.menu, menu);
            });
    }

    public dispose(): void {
        if (this.disposed$.closed)
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
        placement: Placement | null,
        position: Vector2D | null,
    ): void {
        const menu = this.create(menuRef, isHoverMenu, triggerElement, placement, position);
        if (this.isShown(menu)) {
            debugLog?.log(`showOrPosition: already shown. Setting position.`);
            void this.position(this.menu!, menu);
        }
        else
            this.show(menu);
    }

    public hideById(id: string): void {
        const menu = this.menu;
        if (menu?.id !== id) {
            debugLog?.log('hideById: no menu with id:', id)
            return;
        }

        this.hide();
    }

    public async positionById(id: string): Promise<void> {
        const menu = this.menu;
        if (menu?.id !== id) {
            debugLog?.log('positionById: no menu with id:', id)
            return;
        }

        if (menu.isHoverMenu && !menu.menuElement) {
            // This is the very first render of hover menu
            await delayAsync(this.hoverMenuDelayMs);
        }

        menu.menuElement = document.getElementById(menu.id);
        await this.position(menu);
        this.focusFirstItem(menu);
    }

    // Private methods

    private create(
        menuRef: string,
        isHoverMenu: boolean,
        triggerElement: HTMLElement | SVGElement | string,
        placement: Placement | null,
        position: Vector2D | null,
    ): Menu {
        if (!(triggerElement instanceof HTMLElement)) {
            const triggerElementId = triggerElement as string;
            triggerElement = document.getElementById(triggerElementId)!;
        }
        placement = placement ?? getPlacementFromAttributes(triggerElement);
        return {
            id: nextId(),
            menuRef: menuRef,
            triggerElement: triggerElement,
            isHoverMenu: isHoverMenu,
            placement: placement!,
            position: position,
            historyStepId: null,
            menuElement: null,
            focused: false,
        };
    }

    private isShown(menu: Menu): boolean {
        const m = this.menu;
        return !!m
            && m.menuRef === menu.menuRef
            && m.triggerElement === menu.triggerElement
            && m.isHoverMenu === menu.isHoverMenu;
    }

    private show(menu: Menu): void {
        if (!menu.isHoverMenu)
            unselect();
        debugLog?.log('show:', menu)
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (!menu)
            throw new Error(`${logScope}.show: menu == null.`);

        this.menu = menu;
        void this.blazorRef.invokeMethodAsync('OnShowRequest', menu.id, menu.menuRef, menu.isHoverMenu);
        this.removeMessageMark(this.currentMenuRef);
        this.currentMenuRef = menu.menuRef;
        this.addMessageMark(this.currentMenuRef);
        if (ScreenSize.isNarrow())
            TuneUI.play(Tune.ShowMenu);
    }

    private addMessageMark(menuRef: string) {
        const message = document.querySelector(`[data-menu="${menuRef}"]`);
        if (message && !message.classList.contains('marked-message'))
            message.classList.add('marked-message');
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

        const restoreFocus = menu.menuElement?.contains(document.activeElement) ?? false;
        this.menu = null;
        // Hide (un-render) it
        void this.blazorRef.invokeMethodAsync('OnHideRequest', menu.id);
        this.removeMessageMark(this.currentMenuRef);
        if (restoreFocus && menu.triggerElement.isConnected)
            menu.triggerElement.focus({ preventScroll: true });
    }

    private focusFirstItem(menu: Menu): void {
        if (menu.focused || menu.isHoverMenu || !menu.menuElement)
            return;

        menu.focused = true;
        const firstItem = menu.menuElement.querySelector<HTMLElement>('[role=menuitem]');
        firstItem?.focus({ preventScroll: true });
    }

    private removeMessageMark(menuRef: string) {
        const message = document.querySelector(`[data-menu="${menuRef}"]`);
        message?.classList.remove('marked-message');
    }

    private async position(menu: Menu, updatedMenu?: Menu): Promise<void> {
        // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
        if (!menu)
            throw new Error(`${logScope}.position: menu == null.`);

        if (updatedMenu) {
            menu.menuElement = updatedMenu.menuElement ?? menu.menuElement;
            // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
            menu.placement = updatedMenu.placement ?? menu.placement;
            menu.position = updatedMenu.position ?? menu.position;
        }

        const menuElement = menu.menuElement;
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
        } else {
            // Trigger element relative positioning
            referenceElement = menu.triggerElement;
            middleware.push(offset(6));
        }
        middleware.push(flip());
        // crossAxis: true is what keeps a menu too tall for the space above its anchor from
        // ending up at a negative top - flip() alone leaves it there when neither side fits.
        middleware.push(shift({
            padding: getMenuOverflowPadding(),
            crossAxis: true,
            limiter: limitShift(),
        }));
        const { x, y } = await computePosition(
            referenceElement,
            menuElement,
            {
                // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
                placement: menu.placement ?? 'top',
                middleware: middleware,
            });

        let top = `${y}px`;
        if (!this.isDesktopMode) {
            if (!ScreenOrientation.isPortrait) {
                // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
                if (menuElement) {
                    const menuElementBottom = menuElement.getBoundingClientRect().bottom;
                    const heightDelta = window.screen.availHeight - menuElementBottom;
                    top = `${y < 20 || heightDelta < 20 ? 20 : y}px`;
                }
            } else {
                top = 'auto';
            }
        }

        Object.assign(menuElement.style, {
            left: `${x}px`,
            top: top,
        });
    }

    // Event handlers

    private onKeyDown(event: KeyboardEvent): void {
        const menu = this.menu;
        if (!menu)
            return;

        if (event.key === 'Escape') {
            stopEvent(event);
            this.hide();
            return;
        }

        const target = event.target as HTMLElement | null;
        if (target?.getAttribute('role') !== 'menuitem')
            return;

        if (event.key === 'Enter' || event.key === ' ') {
            stopEvent(event);
            target.click();
        }
    }

    private onClick(event: Event): void {
        let trigger = MenuTrigger.None
        if (event.type == 'click')
            trigger = MenuTrigger.Primary;
        if (event.type == 'contextmenu')
            trigger = MenuTrigger.Secondary;
        debugLog?.log('onClick, event:', event, ', trigger:', trigger);

        const isDesktopMode = this.isDesktopMode;

        // Ignore clicks which definitely aren't "ours"
        if (trigger == MenuTrigger.None)
            return;
        if (!(event.target instanceof Element))
            return;

        const result = getOrInheritData(event.target, 'menu');
        const triggerElement = result[0];
        let menuRef = result[1];
        if (triggerElement && menuRef) {
            const menuTrigger: unknown = MenuTrigger[triggerElement.dataset.menuTrigger ?? 'Secondary'];
            if (trigger !== menuTrigger) {
                const altMenuTrigger = menuTrigger == MenuTrigger.Primary ? MenuTrigger.Secondary : MenuTrigger.None;
                if (!isDesktopMode || trigger != altMenuTrigger)
                    menuRef = null;
            }
        }

        if (!menuRef) {
            // We couldn't find any menu to activate on click
            const isClickInsideMenu = event.target.closest('.ac-menu, .ac-menu-hover') != null;
            if (isClickInsideMenu && this.menu != null) {
                // Check if click is on element that should keep menu open
                const shouldKeepOpen = event.target.closest('[data-menu-keep-open]') != null;
                if (shouldKeepOpen)
                    return;

                // The menu will process the action, but we can schedule menu hiding here
                const menu = this.menu;
                nextTick(() => this.hide({ id: menu.id }));
                return;
            }

            // It's a click outside of any menu which doesn't trigger another menu
            if (!this.menu || this.menu.isHoverMenu)
                return; // There is no visible menu or visible menu is a hover menu

            // Non-hover menu is visible, so we need to hide it on this click
            this.hide();
            stopEvent(event);
            return;
        }

        if (!triggerElement)
            return;

        const position = isDesktopMode && (event instanceof PointerEvent || event instanceof MouseEvent)
            ? new Vector2D(event.clientX, event.clientY)
            : null;
        const menu = this.create(menuRef, false, triggerElement, null, position);
        if (this.isShown(menu)) {
            debugLog?.log(`onClick: already shown. Setting position.`);
            // Is it the second click on the same button that triggered the menu?
            if (triggerElement.nodeName == 'BUTTON')
                this.hide();
            else
                void this.position(this.menu!, menu)
        }
        else
            this.show(menu);

        stopEvent(event);
    }

    private onPointerOver(event: Event): void {
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

        if (!triggerElement)
            return;

        const menu = this.create(menuRef, true, triggerElement, 'top-end', null);
        if (this.isShown(menu))
            return;

        this.show(menu);
    }
}

// Helpers

let _nextId = 1;
// Menu Ids are used as HTML element Ids, so they need to have unique prefix
const nextId = () => 'menu:' + (_nextId++).toString();

const menuViewportGap = 5;

// The insets are read from the CSS variables rather than env(), so debugUI.showSafeAreas applies here too.
function getMenuOverflowPadding(): SideObject {
    const style = getComputedStyle(document.body);
    const inset = (name: string) => menuViewportGap + (Number.parseFloat(style.getPropertyValue(name)) || 0);
    return {
        top: inset('--safe-area-top'),
        right: inset('--safe-area-right'),
        bottom: inset('--safe-area-bottom'),
        left: inset('--safe-area-left'),
    };
}

function getPlacementFromAttributes(triggerElement: HTMLElement): Placement | null {
    const placement = triggerElement.dataset.menuPlacement;
    return placement && placement.length > 0 ? placement as Placement : null;
}

function isButtonTrigger(triggerElement: HTMLElement | null): boolean {
    if (!triggerElement)
        return false;

    if (!(triggerElement.closest('button') instanceof HTMLElement))
        return false;

    // Buttons inside menus aren't counted as triggers
    return triggerElement.closest('.ac-menu, .ac-menu-hover') == null;
}
