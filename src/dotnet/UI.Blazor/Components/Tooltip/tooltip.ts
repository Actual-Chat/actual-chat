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
        tooltipRef: HTMLElement,
        arrowRef: HTMLElement,
        blazorRef: DotNet.DotNetObject,
        options?: TooltipOptions): Tooltip {
        return new Tooltip(triggerRef, tooltipRef, arrowRef, blazorRef, options);
    }

    constructor(
        private readonly triggerRef: HTMLElement,
        private readonly tooltipRef: HTMLElement,
        private readonly arrowRef: HTMLElement,
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
        }
        catch (error) {
            console.error(`${LogScope}.constructor: error:`, error)
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
        this.tooltipRef.style.display = 'block';
        this.update();
    }

    private hideTooltip() {
        this.tooltipRef.style.display = '';
    }

    private getPlacement(): Placement {
        if (!this.options)
            return 'bottom';

        return this.mapPositionToPlacement(this.options.position);
    }

    private update() {
        const placement = this.getPlacement();
        computePosition(this.triggerRef, this.tooltipRef, {
            placement: placement,
            middleware: [
                offset(6),
                flip(),
                shift({padding: 5}),
                arrow({element: this.arrowRef}),
            ],
        }).then(({x, y, placement, middlewareData}) => {
            Object.assign(this.tooltipRef.style, {
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

            Object.assign(this.arrowRef.style, {
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
