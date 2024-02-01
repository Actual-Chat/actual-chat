import { Subject } from 'rxjs';
import { clearTimeout, setTimeout } from 'timerQueue';

export class PlaceButtons {
    private readonly disposed$: Subject<void> = new Subject<void>();
    private blazorRef: DotNet.DotNetObject;
    private readonly places: HTMLElement;
    private buttons: HTMLElement[] = [];
    private shiftY: number;
    private currentBtn: HTMLElement;
    private ghostBtn: HTMLElement;
    private topLimit: number;
    private bottomLimit: number;
    private readonly longTap: number = 500;
    private delay: number;
    private placeListObserver: MutationObserver;
    private isScrolling: boolean;

    static create(places: HTMLElement, blazorRef: DotNet.DotNetObject): PlaceButtons {
        return new PlaceButtons(places, blazorRef);
    }

    constructor(places: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.places = places;
        this.blazorRef = blazorRef;
        this.updatePlaceState(false);
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
        for(const btn of this.buttons) {
            btn.removeEventListener('dragstart', this.onDragStart);
            btn.removeEventListener('dragend', this.onDragEnd);

            btn.removeEventListener('touchstart', this.onTouchStart);
            btn.removeEventListener('touchend', this.onTouchEnd);
            btn.removeEventListener('touchcancel', this.onTouchEnd);
        }
        this.places.removeEventListener('dragover', this.onDragOver);
        this.placeListObserver.disconnect();
    }

    private updatePlaceList = (mutationList, observer) => {
        mutationList.forEach(m => {
            m.addedNodes.forEach(element => {
                if (element
                    && element.classList
                    && element.classList.contains('navbar-button')
                    && !element.classList.contains('ghost-btn')
                    && this.buttons.length != this.places.children.length) {
                    this.updatePlaceState(true);
                }
            });
            m.removedNodes.forEach(element => {
                if (element
                    && element.classList
                    && element.classList.contains('navbar-button')
                    && !element.classList.contains('ghost-btn')
                    && this.buttons.length != this.places.children.length) {
                    this.updatePlaceState(true);
                }
            });
        });
    }

    private updatePlaceState(dispose: boolean) {
        if (dispose) {
            this.dispose();
        }
        this.updatePlaceOrder();
        for (const btn of this.buttons) {
            btn.addEventListener('dragstart', this.onDragStart);
            btn.addEventListener('dragend', this.onDragEnd);

            btn.addEventListener('touchstart', this.onTouchStart);
            btn.addEventListener('touchend', this.onTouchEnd);
            btn.addEventListener('touchcancel', this.onTouchEnd);
        }
        this.places.addEventListener('dragover', this.onDragOver);

        this.placeListObserver = new MutationObserver(this.updatePlaceList);
        this.placeListObserver.observe(this.places, {
            attributes: true,
            childList: true,
            subtree: true,
        });
    }

    private scrollUp() {
        let delta = Math.round(this.places.scrollTop);
        if (this.isScrolling && delta > 2) {
            setTimeout(() => {
                this.places.scrollBy(0, -10);
                this.scrollUp();
            }, 25);
        } else {
            return;
        }
    }

    private scrollDown() {
        let delta = Math.round(this.places.scrollHeight - this.places.scrollTop - this.places.clientHeight);
        if (this.isScrolling && delta > 2) {
            setTimeout(() => {
                this.places.scrollBy(0, 10);
                this.scrollDown();
            }, 25);
        } else {
            return;
        }
    }

    private updatePlaceOrder() {
        this.buttons = [];
        let navbarButtons = this.places.querySelectorAll('.navbar-button');
        navbarButtons.forEach(b => {
            let btn = b as HTMLElement;
            this.buttons.push(btn);
            btn.draggable = true;
        });
    }

    private onTouchStart = (event: TouchEvent) => {
        this.currentBtn = event.currentTarget as HTMLElement;
        if (this.currentBtn == null)
            return;
        this.delay = setTimeout(() => {
            this.onLongTouch(event);
        }, this.longTap);
    }

    private onLongTouch = (event: TouchEvent) => {
        this.ghostBtn = this.currentBtn.cloneNode(true) as HTMLElement;
        this.ghostBtn.style.position = 'fixed';
        this.ghostBtn.style.zIndex = '100';
        this.ghostBtn.style.scale = '1.1';
        this.ghostBtn.classList.add('ghost-btn');
        let rect = this.currentBtn.getBoundingClientRect();
        this.shiftY = (event.touches[0].pageY - rect.top) / 2;
        document.body.appendChild(this.ghostBtn);
        this.currentBtn.classList.add('drag-btn');

        this.ghostBtn.style.left = rect.left + 'px';
        this.ghostBtn.style.top = rect.top + 'px';

        let placesRect = this.places.getBoundingClientRect();
        this.topLimit = placesRect.top - 5;
        this.bottomLimit = placesRect.bottom + 10;

        window.addEventListener('touchmove', this.onTouchMove);
    }

    private onTouchMove = (event: TouchEvent) => {
        event.preventDefault();
        let ghostRect = this.ghostBtn.getBoundingClientRect();
        let top = event.touches[0].clientY - this.shiftY;
        if (top <= this.topLimit) {
            this.ghostBtn.style.top = this.topLimit + 'px';
            if (!this.isScrolling) {
                this.isScrolling = true;
                this.scrollUp();
            }
        } else if (top >= this.bottomLimit - ghostRect.height) {
            this.ghostBtn.style.top = this.bottomLimit - ghostRect.height + 'px';
            if (!this.isScrolling) {
                this.isScrolling = true;
                this.scrollDown();
            }
        } else {
            this.ghostBtn.style.top = top + 'px';
            this.isScrolling = false;
        }
        this.replaceButton(event, ghostRect);
    }

    private replaceButton = (event: TouchEvent, ghostRect: DOMRect) => {
        for (const btn of this.buttons) {
            let rect = btn.getBoundingClientRect();
            let topDelta = Math.abs(rect.top - ghostRect.top);
            let bottomDelta = Math.abs(rect.bottom - ghostRect.bottom);
            if (topDelta < 10 && bottomDelta < 10 && !btn.classList.contains('drag-btn')) {
                let currentBtnIndex = this.buttons.indexOf(this.currentBtn);
                let btnIndex = this.buttons.indexOf(btn);
                if (currentBtnIndex < btnIndex) {
                    this.places.insertBefore(btn, this.currentBtn);
                } else {
                    this.places.insertBefore(this.currentBtn, btn);
                }
                this.updatePlaceOrder();
            }
        }
    }

    private onTouchEnd = (event: TouchEvent) => {
        clearTimeout(this.delay);
        window.removeEventListener('touchmove', this.onTouchMove);
        if (this.ghostBtn) {
            this.ghostBtn.remove();
        }
        if (this.currentBtn != null) {
            this.currentBtn.classList.remove('drag-btn');
            this.currentBtn = null;
        }
    }

    private onDragStart = (event: Event) => {
        let target = event.target as HTMLElement;
        let btn = target.closest('.navbar-button');
        btn.classList.add('drag-btn');
    }

    private onDragEnd = (event: Event) => {
        let target = event.target as HTMLElement;
        let btn = target.closest('.navbar-button');
        btn.classList.remove('drag-btn');
    }

    private onDragOver = (event: DragEvent) => {
        event.preventDefault();
        const activeElement = this.places.querySelector('.drag-btn');
        let target = event.target as HTMLElement;
        let currentElement = target.closest('.navbar-button');

        const isMovable = currentElement != null && activeElement !== currentElement &&
            currentElement.classList.contains('navbar-button');

        if (!isMovable) {
            return;
        }

        const nextElement = this.getNextElement(event.pageY, currentElement);

        if (nextElement && activeElement === nextElement.previousElementSibling || activeElement === nextElement) {
            return;
        }

        this.places.insertBefore(activeElement, nextElement);
    }

    private getNextElement = (cursorPosition: number, currentElement: Element) => {
        const currentElementRect = currentElement.getBoundingClientRect();
        const currentElementCenter = currentElementRect.y + currentElementRect.height / 2;

        return (cursorPosition < currentElementCenter) ?
               currentElement :
               currentElement.nextElementSibling;
    };
}

