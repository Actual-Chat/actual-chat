// TODO: Fix ESLint errors
/* eslint-disable @typescript-eslint/no-unnecessary-condition, @typescript-eslint/ban-ts-comment */
import { Disposable } from 'disposable';
import { fromEvent, Subject, takeUntil, map, switchMap, delay, of, EMPTY } from 'rxjs';
import { getOrInheritData } from 'dom-helpers';
import {
    Placement,
    autoUpdate,
    computePosition,
    flip,
    shift,
    offset,
    arrow,
} from '@floating-ui/dom';
import { MutationProcessor } from 'mutation-processor';
import { getLogs } from 'logging';

const { errorLog } = getLogs('TooltipHost');

// `data-render-script-tooltip-auto-show` shows an element's tooltip with no pointer involved, which
// is the only way to reach a touch device. Its value is the text, so a changed message re-triggers
// the script while a re-render carrying the same one does not.
const AutoShowScript = 'tooltip-auto-show';
const HoverDelayMs = 300;
const TooltipAttributes = ['data-tooltip', 'data-tooltip-severity'];
const AutoShowDurationMs = 3000;
const ErrorAutoShowDurationMs = 10000;

interface HoverTarget {
    element: HTMLElement | SVGElement;
    text: string;
}

export class TooltipHost implements Disposable {
    private static current: TooltipHost | null = null;
    private readonly disposed$: Subject<void> = new Subject<void>();
    private readonly arrowRef: HTMLElement;
    private readonly tooltipRef: HTMLElement;
    private readonly tooltipTextRef: HTMLElement;
    // A shown tooltip has to track its trigger's attributes: the trigger stays put under the pointer
    // while a re-render changes what it should say, and no pointer event follows that.
    private readonly attributeObserver: MutationObserver;
    private hoverElement: HTMLElement | SVGElement | null = null;
    private hoverText = '';
    private autoElement: HTMLElement | null = null;
    private autoText = '';
    private shownElement: HTMLElement | SVGElement | null = null;
    private shownText = '';
    private shownSeverity = '';
    private isShownAuto = false;
    private autoHideHandle: ReturnType<typeof setTimeout> | null = null;
    private clearPositionUpdate: (() => void) | null = null;

    public static create(): TooltipHost {
        return new TooltipHost();
    }

    // Routed through a static so the render script, which lives for the page's lifetime, never
    // captures an instance.
    public static autoShow(element: HTMLElement, text: string): void {
        TooltipHost.current?.setAutoRequest(element, text);
    }

    constructor() {
        try {
            this.tooltipRef = document.getElementsByClassName('ac-tooltip')[0] as HTMLElement;
            this.arrowRef = document.getElementsByClassName('ac-tooltip-arrow')[0] as HTMLElement;
            this.tooltipTextRef = document.getElementsByClassName('ac-tooltip-text')[0] as HTMLElement;
            this.attributeObserver = new MutationObserver(() => this.update());
            TooltipHost.current = this;
            this.listenForMouseOverEvent();
        } catch (error) {
            errorLog?.log(`constructor: unhandled error:`, error);
            this.dispose();
        }
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
        if (TooltipHost.current === this)
            TooltipHost.current = null;
        this.hoverElement = null;
        this.hoverText = '';
        this.clearAutoRequest();
        if (this.tooltipRef)
            this.hide();
    }

    // Private methods

    private listenForMouseOverEvent(): void {
        fromEvent(document, 'mouseover')
            .pipe(
                takeUntil(this.disposed$),
                map((event: Event) => {
                    const [element, text] = getOrInheritData(event.target, 'tooltip');
                    if (element === this.hoverElement && text === this.hoverText)
                        return null;

                    if (!element) {
                        if (this.hoverElement) {
                            this.hoverElement = null;
                            this.hoverText = '';
                            this.update();
                        }

                        return null;
                    }

                    return { element, text: text ?? '' } as HoverTarget;
                }),
                switchMap(target => target ? of(target).pipe(delay(HoverDelayMs)) : EMPTY),
            )
            .subscribe(target => {
                this.hoverElement = target.element;
                this.hoverText = target.text;
                this.update();
            });
    }

    // An empty text clears this element's request only: every element carrying the attribute reports
    // its empty initial value on mount, which must not cancel what another element is showing.
    private setAutoRequest(element: HTMLElement, text: string): void {
        if (!text && this.autoElement !== element)
            return;

        this.clearAutoHide();
        this.autoElement = text ? element : null;
        this.autoText = text;
        const duration = this.autoElement ? this.getAutoShowDuration(element) : 0;
        if (duration > 0)
            this.autoHideHandle = setTimeout(() => {
                this.autoHideHandle = null;
                this.clearAutoRequest();
                this.update();
            }, duration);
        this.update();
    }

    private update(): void {
        if (this.autoElement && !this.autoElement.isConnected)
            this.clearAutoRequest();

        // Read back rather than trust what the request carried: the text and the severity can have
        // been rewritten since, and the tooltip on screen has to follow them.
        // Hover wins while the pointer is on something with a tooltip; an auto-shown one re-asserts
        // itself once that stops holding, rather than being cancelled by a passing hover.
        const hoverText = this.hoverElement?.dataset.tooltip ?? '';
        const isAuto = !hoverText;
        const element = isAuto ? this.autoElement : this.hoverElement;
        const text = isAuto ? this.autoText : hoverText;
        if (!element || !text) {
            this.hide();
            return;
        }

        const severity = element.dataset.tooltipSeverity ?? '';
        if (this.shownElement === element
            && this.shownText === text
            && this.shownSeverity === severity
            && this.isShownAuto === isAuto)
            return;

        this.show(element, text, severity, isAuto);
    }

    private show(element: HTMLElement | SVGElement, text: string, severity: string, isAuto: boolean): void {
        this.shownElement = element;
        this.shownText = text;
        this.shownSeverity = severity;
        this.isShownAuto = isAuto;
        this.tooltipTextRef.textContent = text;
        this.tooltipRef.classList.toggle('error', severity === 'error');
        this.tooltipRef.classList.add('show');
        this.attributeObserver.disconnect();
        this.attributeObserver.observe(element, { attributes: true, attributeFilter: TooltipAttributes });
        this.stopPositionUpdate();
        if (!isAuto) {
            this.updatePosition(element);
            return;
        }

        // An auto-shown tooltip outlives the render that asked for it, so it has to follow its
        // trigger; a hovered one is gone before the layout around it can move.
        this.clearPositionUpdate = autoUpdate(element, this.tooltipRef, () => {
            if (element.isConnected)
                this.updatePosition(element);
            else
                this.update();
        });
    }

    private hide(): void {
        this.shownElement = null;
        this.shownText = '';
        this.shownSeverity = '';
        this.isShownAuto = false;
        this.attributeObserver.disconnect();
        this.stopPositionUpdate();
        this.tooltipRef.classList.remove('show');
    }

    private clearAutoRequest(): void {
        this.clearAutoHide();
        this.autoElement = null;
        this.autoText = '';
    }

    private clearAutoHide(): void {
        if (this.autoHideHandle === null)
            return;

        clearTimeout(this.autoHideHandle);
        this.autoHideHandle = null;
    }

    private stopPositionUpdate(): void {
        this.clearPositionUpdate?.();
        this.clearPositionUpdate = null;
    }

    // No duration means "long enough to read, and longer when it's a failure"; an explicit 0 (or
    // anything unparseable) keeps the tooltip up until the request clears.
    private getAutoShowDuration(element: HTMLElement | SVGElement): number {
        const value = element.dataset.tooltipAutoShowDuration;
        if (value) {
            const duration = Number.parseInt(value);
            return Number.isFinite(duration) && duration > 0 ? duration : 0;
        }

        return element.dataset.tooltipSeverity === 'error' ? ErrorAutoShowDurationMs : AutoShowDurationMs;
    }

    private getPlacement(triggerRef: HTMLElement | SVGElement): Placement {
        const placement = triggerRef.dataset.tooltipPosition;
        return placement ? placement as Placement : 'top';
    }

    private updatePosition(triggerRef: HTMLElement | SVGElement): void {
        const placement = this.getPlacement(triggerRef);
        void computePosition(triggerRef, this.tooltipRef, {
            placement: placement,
            middleware: [
                offset(6),
                flip(),
                shift({ padding: 5 }),
                arrow({ element: this.arrowRef }),
            ],
        }).then(({ x, y, placement, middlewareData }) => {
            Object.assign(this.tooltipRef.style, {
                left: `${x}px`,
                top: `${y}px`,
            });

            if (!middlewareData.arrow)
                return;

            const { x: arrowX, y: arrowY } = middlewareData.arrow;

            const staticSide = {
                top: 'bottom',
                right: 'left',
                bottom: 'top',
                left: 'right',
            }[placement.split('-')[0]];

            Object.assign(this.arrowRef.style, {
                left: arrowX != null ? `${arrowX}px` : '',
                top: arrowY != null ? `${arrowY}px` : '',
                right: '',
                bottom: '',
                // @ts-ignore
                [staticSide]: '-4px',
            });
        });
    }
}

// Registered at import time, like every other render script: the attribute can be in the very first
// render, well before the host component mounts.
MutationProcessor.registerRenderScript(AutoShowScript, (element, value) => TooltipHost.autoShow(element, value));
