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

interface TooltipOptions {
    position: TooltipPosition;
}

enum TooltipPosition
{
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
        tooltip: HTMLDivElement,
        reference: HTMLDivElement,
        arrow: HTMLDivElement,
        blazorRef: DotNet.DotNetObject,
        options?: TooltipOptions): Tooltip {
        return new Tooltip(tooltip, reference, arrow, blazorRef, options);
    }

    constructor(
        private readonly tooltip: HTMLDivElement,
        private readonly reference: HTMLDivElement,
        private readonly arrow: HTMLDivElement,
        private readonly blazorRef: DotNet.DotNetObject,
        private readonly options?: TooltipOptions,
    ) {
        const mouseenterEvents$ = fromEvent(this.reference, 'mouseenter');
        const focusEvent$ = fromEvent(this.reference, 'focus');
        merge(mouseenterEvents$, focusEvent$)
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => {
                this.showTooltip();
            });

        const mouseleaveEvent$ = fromEvent(this.reference, 'mouseleave');
        const blurEvent$ = fromEvent(this.reference, 'blur');
        merge(mouseleaveEvent$, blurEvent$)
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => {
                this.hideTooltip();
            });
    }

    public dispose() {
        this.disposed$.next();
        this.disposed$.complete();
    }

    private showTooltip() {
        this.tooltip.style.display = 'block';
        this.update();
    }

    private hideTooltip() {
        this.tooltip.style.display = '';
    }

    private getPlacement(): Placement {
        if (!this.options)
            return 'bottom';

        return this.mapPositionToPlacement(this.options.position);
    }

    private update() {
        const placement = this.getPlacement();
        computePosition(this.reference, this.tooltip, {
            placement: placement,
            middleware: [
                offset(6),
                flip(),
                shift({padding: 5}),
                arrow({element: this.arrow}),
            ],
        }).then(({x, y, placement, middlewareData}) => {
            Object.assign(this.tooltip.style, {
                left: `${x}px`,
                top: `${y}px`,
            });

            const {x: arrowX, y: arrowY} = middlewareData.arrow;

            const staticSide = {
                top: 'bottom',
                right: 'left',
                bottom: 'top',
                left: 'right',
            }[placement.split('-')[0]];

            Object.assign(this.arrow.style, {
                left: arrowX != null ? `${arrowX}px` : '',
                top: arrowY != null ? `${arrowY}px` : '',
                right: '',
                bottom: '',
                [staticSide]: '-4px',
            });
        });
    }

    private mapPositionToPlacement(position: TooltipPosition): Placement {
        switch (position){
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
                throw Error("Argument out of range.")
        }
    }
}
