import './menu.css';
import { Disposable } from 'disposable';
import { nanoid } from 'nanoid';
import { HistoryUI, HistoryStepToken } from '../../Services/HistoryUI/history-ui';

import {
    combineLatestWith,
    delay,
    empty,
    filter,
    fromEvent,
    map,
    of,
    Subject,
    switchMap,
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
import escapist from '../../Services/Escapist/escapist';
import screenSize from '../../Services/ScreenSize/screen-size';
import { Vibration } from '../../Services/Vibration/vibration';
import { Log, LogLevel } from 'logging';

const LogScope = 'MenuHost';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

interface Coords {
    x: number;
    y: number;
}

interface EventData {
    menuRef: string;
    placement: Placement;
    closeOnSecondClick: boolean;
    isHoverMenu: boolean;
    element: HTMLElement;
    coords?: Coords;
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
    private readonly disposed$: Subject<void> = new Subject<void>();
    private menus: Menu[] = [];
    private preventHistoryModification: boolean;
    private historyStepToken: HistoryStepToken;

    public static create(blazorRef: DotNet.DotNetObject): MenuHost {
        return new MenuHost(blazorRef);
    }

    constructor(
        private readonly blazorRef: DotNet.DotNetObject,
    ) {
        try {
            this.listenForEvents();
        } catch (error) {
            errorLog?.log(`constructor: unhandled error:`, error);
            this.dispose();
        }
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

    private getHistoryUi() : HistoryUI {
       return <HistoryUI>(<any>window).App.historyUI;
    }

    private renderMenu(eventData: EventData): void {
        const menuIndex = this.menus.findIndex(
            x => x.eventData.menuRef == eventData.menuRef
            && x.eventData.element == eventData.element);
        if (menuIndex > -1) {
            const menu = this.menus[menuIndex];
            if (menu.eventData.closeOnSecondClick) {
                this.menus.splice(menuIndex, 1);
                this.hideMenu(menu);
            } else {
                menu.eventData = eventData;
                void updatePosition(menu);
            }

            return;
        }

        try {
            this.preventHistoryModification = true;
            this.hideMenus(x => x.isHoverMenu === eventData.isHoverMenu);
        }
        finally {
            this.preventHistoryModification = false;
        }

        const menu: Menu = {
            id: nanoid(),
            eventData: eventData,
        };
        this.menus.push(menu);
        if (!eventData.isHoverMenu && !this.historyStepToken) {
            debugLog?.log(`about to push backStep on render menu`, history.state);
            this.historyStepToken = this.getHistoryUi()
                .pushBackStep(true, this.hideMenusOnBack);
            debugLog?.log(`pushed state to render menu`, history.state);
        }
        this.blazorRef.invokeMethodAsync('OnRenderMenu', menu.eventData.menuRef, menu.id, eventData.isHoverMenu);
    }

    private hideMenusOnBack = (): void => {
        debugLog?.log('context menu should be closed on back');
        if (this.menus.length)
            this.hideMenus(e => !e.isHoverMenu);
    }

    private hideOverlay(): void {
        if (this.menus.length)
            return;

        const overlay = document.getElementsByClassName('ac-menu-overlay')[0] as HTMLElement;
        if (!overlay)
            return;

        overlay.style.display = 'none';
    }

    private removeHistoryStep(): void {
        if (this.preventHistoryModification || !this.historyStepToken)
            return;
        if (this.menus.filter(c => !c.eventData.isHoverMenu).length)
            return;
        const goBack = this.getHistoryUi()
            .isActiveStep(this.historyStepToken);
        this.historyStepToken = undefined;
        if (goBack) {
            history.back();
            debugLog?.log(`removed history back step on hide menu`);
        } else {
            debugLog?.log(`history back step has been already replaced before on hide menu`);
        }
    }

    private hideMenu(menu: Menu): void {
        debugLog?.log(`hideMenu, menu:`, menu);
        if (menu.elementRef)
            menu.elementRef.style.display = 'none';
        this.hideOverlay();
        this.removeHistoryStep();

        this.blazorRef.invokeMethodAsync('OnHideMenu', menu.id);
    }

    private hideAllMenus(): void {
        debugLog?.log(`hideAllMenus`);
        while (this.menus.length) {
            this.hideMenu(this.menus.pop());
        }
    }

    private hideMenus(predicate: (e: EventData) => boolean): void {
        for (let i = 0; i < this.menus.length; i++) {
            if (predicate(this.menus[i].eventData)) {
                const removed = this.menus.splice(i, 1);
                this.hideMenu(removed[0]);
                i--;
            }
        }
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
            )
            .subscribe((event: Event) => {
                if (!(event.target instanceof Element))
                    return undefined;

                if (event.target.nodeName === 'IMG')
                    return undefined;

                event.preventDefault();
            });

        fromEvent(document, 'contextmenu')
            .pipe(
                takeUntil(this.disposed$),
                combineLatestWith(screenSize.size$),
                filter(([_, screenSize]) => screenSize !== 'Small'),
                map(([mouseEvent, _]) => mouseEvent),
                map((event) => this.mapEvent(event, MenuTriggers.RightClick, true, false)),
                switchMap((eventData: EventData | undefined) => {
                    return eventData ? of(eventData) : empty();
                }),
            )
            .subscribe((eventData: EventData) => {
                this.renderMenu(eventData);
            });

        fromEvent(document, 'long-press')
            .pipe(
                takeUntil(this.disposed$),
                combineLatestWith(screenSize.size$),
                filter(([_, screenSize]) => screenSize === 'Small'),
                map(([event, _]) => event),
                map((event) => this.mapEvent(event, MenuTriggers.LongClick, false, false)),
                switchMap((eventData: EventData | undefined) => {
                    return eventData ? of(eventData) : empty();
                }),
            )
            .subscribe((eventData: EventData) => {
                this.renderMenu(eventData);
                Vibration.vibrate();
            });

        fromEvent(document, 'mouseover')
            .pipe(
                takeUntil(this.disposed$),
                combineLatestWith(screenSize.size$),
                filter(([_, screenSize]) => screenSize !== 'Small'),
                map(([mouseEvent, _]) => mouseEvent),
                map((event) => this.mapHoverEvent(event)),
                switchMap((eventData: EventData | undefined) => {
                    return eventData ? of(eventData).pipe(delay(100)) : empty();
                }),
            )
            .subscribe((eventData: EventData) => {
                this.renderMenu(eventData);
            });

        fromEvent(document, 'touchend')
            .pipe(
                takeUntil(this.disposed$),
                filter((event: TouchEvent) => {
                    if (!(event.target instanceof Element))
                        return false;

                    const isOverlayClicked = event.target.classList.contains('ac-menu-overlay');
                    if (isOverlayClicked)
                        return true;

                    const isMenuClicked = event.target.closest('.ac-menu, .ac-menu-hover') != null;
                    return isMenuClicked;
                }),
            )
            .subscribe(() => {
                this.hideAllMenus();
            });

        escapist.escapeEvents()
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => {
                this.hideAllMenus();
            });
    }

    private mapEvent(
        event: Event | PointerEvent,
        triggers: MenuTriggers,
        byCoords: boolean,
        closeOnSecondClick: boolean
    ): EventData | undefined {
        if (!(event.target instanceof Element))
            return undefined;
        if (event.target.nodeName === 'IMG')
            return undefined;
        debugLog?.log(
            `mapEvent: event:`, event,
            `, triggers:`, triggers,
            `, byCoords:`, byCoords,
            `, closeOnSecondClick`, closeOnSecondClick);

        const closestElement = event.target.closest('[data-menu]');
        if (!(closestElement instanceof HTMLElement)) {
            this.hideAllMenus();
            return undefined;
        }
        const menuTrigger = closestElement.dataset['menuTrigger'];
        if (!menuTrigger || !(hasTrigger(menuTrigger, triggers))) {
            this.hideAllMenus();
            return undefined;
        }
        event.preventDefault();
        const menuRef = closestElement.dataset['menu'];
        const placement = getPlacement(closestElement);
        const coords =
            byCoords && event instanceof PointerEvent
               ? { x: event.clientX, y: event.clientY }
               : undefined;
        return {
            placement,
            isHoverMenu: false,
            menuRef: menuRef,
            closeOnSecondClick,
            element: closestElement,
            coords: coords,
        };
    }

    private mapHoverEvent(event: Event): EventData | undefined {
        if (!(event.target instanceof Element)) {
            this.hideMenus(x => x.isHoverMenu);
            return undefined;
        }
        const closestElement = event.target.closest('[data-hover-menu]');
        if (!(closestElement instanceof HTMLElement)) {
            const menu = event.target.closest('.ac-menu, .ac-menu-hover');
            if (!menu) {
                this.hideMenus(x => x.isHoverMenu);
            }
            return undefined;
        }
        const trigger = closestElement.dataset['hoverMenu'];
        const hoverMenu = this.menus.find(x => x.eventData.isHoverMenu);
        if (hoverMenu) {
            if (hoverMenu.eventData.menuRef === trigger) {
                return undefined;
            } else {
                this.hideMenu(hoverMenu);
            }
        }

        return {
            placement: "top-end",
            menuRef: trigger,
            isHoverMenu: true,
            closeOnSecondClick: false,
            element: closestElement,
            coords: undefined,
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
