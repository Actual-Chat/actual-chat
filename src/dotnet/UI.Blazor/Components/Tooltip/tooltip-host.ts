import { Disposable } from 'disposable';
import { fromEvent, Subject, takeUntil, map, switchMap, delay, of, EMPTY } from 'rxjs';
import { getOrInheritData } from 'dom-helpers';
import {
    Placement,
    computePosition,
    flip,
    shift,
    offset,
    arrow,
} from '@floating-ui/dom';
import { Log } from 'logging';

const { errorLog } = Log.get('TooltipHost');

export class TooltipHost implements Disposable {
    private readonly disposed$: Subject<void> = new Subject<void>();
    private readonly arrowRef: HTMLElement;
    private readonly tooltipRef: HTMLElement;
    private readonly tooltipTextRef: HTMLElement;

    public static create(): TooltipHost {
        return new TooltipHost();
    }

    constructor() {
        try {
            this.tooltipRef = document.getElementsByClassName('ac-tooltip')[0] as HTMLElement;
            this.arrowRef = document.getElementsByClassName('ac-tooltip-arrow')[0] as HTMLElement;
            this.tooltipTextRef = document.getElementsByClassName('ac-tooltip-text')[0] as HTMLElement;
            this.listenForMouseOverEvent();
        } catch (error) {
            errorLog?.log(`constructor: unhandled error:`, error);
            this.dispose();
        }
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();

        if (this.tooltipRef)
            this.hideTooltip();
    }

    private listenForMouseOverEvent(): void {
        let activeTooltip: { element: HTMLElement | SVGElement, text: string } | null;
        fromEvent(document, 'mouseover')
            .pipe(
                takeUntil(this.disposed$),
                map((event: Event) => {
                    const [element, text] = getOrInheritData(event.target, 'tooltip');
                    if (element === activeTooltip?.element && text === activeTooltip?.text)
                        return null;

                    if (!element && activeTooltip) {
                        activeTooltip = null;
                        this.hideTooltip();
                        return null;
                    }
                    if (element == null)
                        return null;

                    return { element, text };
                }),
                switchMap(tooltip => {
                    return tooltip ? of(tooltip).pipe(delay(300)) : EMPTY;
                }),
            )
            .subscribe(tooltip => {
                activeTooltip = tooltip;
                this.showTooltip(activeTooltip.element);
            });
    }

    private showTooltip(triggerRef: HTMLElement | SVGElement) {
        const tooltipText = triggerRef.dataset['tooltip'];
        if (!tooltipText)
            return;

        this.tooltipTextRef.textContent = tooltipText;
        this.tooltipRef.style.display = 'block';
        this.updatePosition(triggerRef);
    }

    private hideTooltip() {
        this.tooltipRef.style.display = '';
    }

    private getPlacement(triggerRef: HTMLElement | SVGElement): Placement {
        const placement = triggerRef.dataset['tooltipPosition'];
        return placement ? placement as Placement : 'top';
    }

    private updatePosition(triggerRef: HTMLElement | SVGElement): void {
        const placement = this.getPlacement(triggerRef);
        computePosition(triggerRef, this.tooltipRef, {
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
                [staticSide]: '-4px',
            });
        });
    }
}
