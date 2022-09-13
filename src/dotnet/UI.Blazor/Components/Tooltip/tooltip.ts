import { Disposable } from 'disposable';
import { fromEvent, Subject, takeUntil, merge } from 'rxjs';
import {
    Placement,
    computePosition,
    flip,
    shift,
    offset,
    arrow,
} from '@floating-ui/dom';

const LogScope = 'Tooltip';

interface TooltipOptions {
    text: string;
    position: TooltipPosition;
}

enum TooltipPosition {
    None,
    Top,
    TopStart,
    TopEnd,
    Right,
    RightStart,
    RightEnd,
    Bottom,
    BottomStart,
    BottomEnd,
    Left,
    LeftStart,
    LeftEnd,
}

export class Tooltip implements Disposable {
    private readonly disposed$: Subject<void> = new Subject<void>();

    public static create(
        triggerRef: HTMLElement,
        blazorRef: DotNet.DotNetObject,
        options?: TooltipOptions): Tooltip {
        return new Tooltip(triggerRef, blazorRef, options);
    }

    constructor(
        private readonly triggerRef: HTMLElement,
        private readonly blazorRef: DotNet.DotNetObject,
        private readonly options?: TooltipOptions,
    ) {
        try {
            const mouseEnterEvents$ = fromEvent(this.triggerRef, 'mouseenter');
            const focusEvents$ = fromEvent(this.triggerRef, 'focus');
            merge(mouseEnterEvents$, focusEvents$)
                .pipe(takeUntil(this.disposed$))
                .subscribe(() => this.showTooltip());

            const mouseLeaveEvents$ = fromEvent(this.triggerRef, 'mouseleave');
            const blurEvents$ = fromEvent(this.triggerRef, 'blur');
            merge(mouseLeaveEvents$, blurEvents$)
                .pipe(takeUntil(this.disposed$))
                .subscribe(() => this.hideTooltip());
        } catch (error) {
            console.error(`${LogScope}.ctor: error:`, error);
            this.dispose();
        }
    }

    public dispose() {
        if (this.disposed$.isStopped)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private showTooltip() {
        const tooltipRef = this.getTooltipElement();
        tooltipRef.style.display = 'block';
        const tooltipTextRef = this.getTooltipTextElement();
        tooltipTextRef.textContent = this.options.text;
        this.update();
    }

    private hideTooltip() {
        const tooltipRef = this.getTooltipElement();
        tooltipRef.style.display = '';
    }

    private getPlacement(): Placement {
        if (!this.options)
            return 'bottom';

        return this.mapPositionToPlacement(this.options.position);
    }

    private update() {
        const tooltipRef = this.getTooltipElement();
        const arrowRef = this.getArrowElement();
        const placement = this.getPlacement();
        computePosition(this.triggerRef, tooltipRef, {
            placement: placement,
            middleware: [
                offset(6),
                flip(),
                shift({ padding: 5 }),
                arrow({ element: arrowRef }),
            ],
        }).then(({ x, y, placement, middlewareData }) => {
            Object.assign(tooltipRef.style, {
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

            Object.assign(arrowRef.style, {
                left: arrowX != null ? `${arrowX}px` : '',
                top: arrowY != null ? `${arrowY}px` : '',
                right: '',
                bottom: '',
                [staticSide]: '-4px',
            });
        });
    }

    private mapPositionToPlacement(position: TooltipPosition): Placement {
        switch (position) {
            case TooltipPosition.Top:
                return 'top';
            case TooltipPosition.TopStart:
                return 'top-start';
            case TooltipPosition.TopEnd:
                return 'top-end';
            case TooltipPosition.Right:
                return 'right';
            case TooltipPosition.RightStart:
                return 'right-start';
            case TooltipPosition.RightEnd:
                return 'right-end';
            case TooltipPosition.Bottom:
                return 'bottom';
            case TooltipPosition.BottomStart:
                return 'bottom-start';
            case TooltipPosition.BottomEnd:
                return 'bottom-end';
            case TooltipPosition.Left:
                return 'left';
            case TooltipPosition.LeftStart:
                return 'left-start';
            case TooltipPosition.LeftEnd:
                return 'left-end';
            default:
                throw Error('Argument out of range.');
        }
    }

    private getArrowElement(): HTMLElement {
        return document.getElementsByClassName('ac-tooltip-arrow')[0] as HTMLElement;
    }

    private getTooltipElement(): HTMLElement {
        return document.getElementsByClassName('ac-tooltip')[0] as HTMLElement;
    }

    private getTooltipTextElement() {
        return document.getElementsByClassName('ac-tooltip-text')[0] as HTMLElement;
    }
}
