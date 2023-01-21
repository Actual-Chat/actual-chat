import { Disposable } from 'disposable';
import { fromEvent, Subject, takeUntil, map, switchMap, delay, of, empty } from 'rxjs';
import {
    Placement,
    computePosition,
    flip,
    shift,
    offset,
    arrow,
} from '@floating-ui/dom';
import { Log, LogLevel, LogScope } from 'logging';
import { getOrInheritData } from '../../../../nodejs/src/dom-helpers';

const LogScope: LogScope = 'TooltipHost';
const debugLog = Log.get(LogScope, LogLevel.Debug);
const warnLog = Log.get(LogScope, LogLevel.Warn);
const errorLog = Log.get(LogScope, LogLevel.Error);

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
        let activeTooltipElement: HTMLElement | null = null;
        fromEvent(document, 'mouseover')
            .pipe(
                takeUntil(this.disposed$),
                map((event: Event) => {
                    const [tooltipElement, _] = getOrInheritData(event.target, 'tooltip');
                    if (tooltipElement === activeTooltipElement)
                        return null;

                    if (!tooltipElement && activeTooltipElement) {
                        activeTooltipElement = null;
                        this.hideTooltip();
                        return null;
                    }
                    if (tooltipElement == null)
                        return null;

                    return tooltipElement;
                }),
                switchMap((tooltipElement: HTMLElement | null) => {
                    return tooltipElement ? of(tooltipElement).pipe(delay(300)) : empty();
                }),
            )
            .subscribe((tooltipElement: HTMLElement) => {
                activeTooltipElement = tooltipElement;
                this.showTooltip(activeTooltipElement);
            });
    }

    private showTooltip(triggerRef: HTMLElement) {
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

    private getPlacement(triggerRef: HTMLElement): Placement {
        const placement = triggerRef.dataset['tooltipPosition'];
        return placement ? placement as Placement : 'top';
    }

    private updatePosition(triggerRef: HTMLElement): void {
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
