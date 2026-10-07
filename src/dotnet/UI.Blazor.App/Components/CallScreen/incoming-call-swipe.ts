import { fromEvent, merge, Subject, take, takeUntil } from 'rxjs';
import { DeviceInfo } from 'device-info';

const RemPx = 16;
const TriggerRem = 5; // drag the button up this far to fire Accept / Decline
const MaxDragRem = 7; // how far the button can travel

// Turns the Accept / Decline buttons into swipe-up gestures on touch devices: a plain tap does
// nothing, only dragging the button up past the threshold accepts / declines. On non-touch devices
// this is a no-op, so the buttons keep their native Blazor click handlers.
export class IncomingCallSwipe {
    private readonly disposed$ = new Subject<void>();

    public static create(root: HTMLElement, blazorRef: DotNet.DotNetObject): IncomingCallSwipe {
        return new IncomingCallSwipe(root, blazorRef);
    }

    constructor(
        private readonly root: HTMLElement,
        private readonly blazorRef: DotNet.DotNetObject,
    ) {
        if (!(root instanceof HTMLElement) || !DeviceInfo.isTouchCapable)
            return;

        // A plain tap must not accept / decline — swallow the click so only a swipe fires.
        fromEvent<MouseEvent>(root, 'click', { capture: true })
            .pipe(takeUntil(this.disposed$))
            .subscribe(e => {
                if ((e.target as HTMLElement | null)?.closest('.c-decline, .c-accept')) {
                    e.stopImmediatePropagation();
                    e.preventDefault();
                }
            });

        fromEvent<PointerEvent>(root, 'pointerdown')
            .pipe(takeUntil(this.disposed$))
            .subscribe(e => this.onDown(e));
    }

    public dispose(): void {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private onDown(e: PointerEvent): void {
        const button = (e.target as HTMLElement | null)?.closest<HTMLElement>('.c-decline, .c-accept');
        if (button == null)
            return;

        const method = button.classList.contains('c-accept') ? 'SwipeAccept' : 'SwipeDecline';
        const startY = e.clientY;
        const trigger = TriggerRem * RemPx;
        const maxDrag = MaxDragRem * RemPx;
        let travel = 0;
        button.style.transition = 'none';
        button.setPointerCapture(e.pointerId);

        const up$ = merge(
            fromEvent<PointerEvent>(button, 'pointerup'),
            fromEvent<PointerEvent>(button, 'pointercancel'),
        ).pipe(take(1), takeUntil(this.disposed$));

        fromEvent<PointerEvent>(button, 'pointermove')
            .pipe(takeUntil(up$), takeUntil(this.disposed$))
            .subscribe(ev => {
                travel = Math.max(0, Math.min(startY - ev.clientY, maxDrag)); // up = positive
                button.style.transform = `translateY(${-travel}px)`;
            });

        up$.subscribe(() => {
            if (button.hasPointerCapture(e.pointerId))
                button.releasePointerCapture(e.pointerId);
            if (travel >= trigger) {
                // Leave the button lifted; the whole view tears down as the call is accepted / declined.
                void this.blazorRef.invokeMethodAsync(method);
            } else {
                button.style.transition = 'transform 0.2s ease-out';
                button.style.transform = '';
            }
        });
    }
}
