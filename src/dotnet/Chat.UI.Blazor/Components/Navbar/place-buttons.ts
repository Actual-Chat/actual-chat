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
        this.places.removeEventListener('pointerdown', this.onPointerDown);
        window.removeEventListener('pointermove', this.onPointerMove);
        window.removeEventListener('pointerup', this.onPointerUp);
        window.removeEventListener('pointercancel', this.onPointerUp);
        if (this.currentBtn && this.currentBtn.classList.contains('drag-btn'))
            this.currentBtn.classList.remove('drag-btn');
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
            btn.style.touchAction = 'none';
            btn.ondragstart = () => false;
        }
        this.places.addEventListener('pointerdown', this.onPointerDown);
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
        });
    }

    private onPointerDown = (event: PointerEvent) => {
        event.preventDefault();
        for (const btn of this.buttons) {
            if (btn.contains(event.target as HTMLElement)) {
                this.currentBtn = btn;
                window.addEventListener('pointerup', this.onPointerUp);
                window.addEventListener('pointercancel', this.onPointerUp);
                break;
            }
        }
        if (this.currentBtn == null)
            return;
        this.delay = setTimeout(() => {
            this.onLongTap(event);
        }, this.longTap);
    }

    private onLongTap = (event: PointerEvent) => {
        event.preventDefault();
        this.addGhostBtn(event);
        window.addEventListener('pointermove', this.onPointerMove);
    }

    private onPointerUp = (event: PointerEvent) => {
        event.preventDefault();
        if (this.delay)
            clearTimeout(this.delay);
        if (this.ghostBtn) {
            this.ghostBtn.remove();
        }
        this.currentBtn.classList.remove('drag-btn');
        window.removeEventListener('pointermove', this.onPointerMove);
        window.removeEventListener('pointerup', this.onPointerUp);
        window.removeEventListener('pointercancel', this.onPointerUp);
    }

    private onPointerMove = (event: PointerEvent) => {
        event.preventDefault();
        let ghostRect = this.ghostBtn.getBoundingClientRect();
        let top = event.y - this.shiftY;
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
        this.replaceButton(ghostRect);
    }

    private replaceButton = (ghostRect: DOMRect) => {
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

    private addGhostBtn = (event: PointerEvent) => {
        this.ghostBtn = document.createElement('div');
        this.ghostBtn.classList.add('ghost-btn');

        let offsetX = 8;
        let dots = document.createElement('div');
        dots.innerHTML = `<i class="icon-more-vertical-2 text-xl"></i>`;
        dots.style.marginRight = offsetX + 'px';

        let btn = this.currentBtn.cloneNode(true) as HTMLElement;

        let rect = this.currentBtn.getBoundingClientRect();
        this.shiftY = (event.y - rect.top) / 2;
        this.ghostBtn.appendChild(dots);
        this.ghostBtn.appendChild(btn);
        document.body.appendChild(this.ghostBtn);

        this.currentBtn.classList.add('drag-btn');

        let dotsOffset = dots.getBoundingClientRect().width;
        this.ghostBtn.style.left = rect.left - dotsOffset - offsetX + 'px';
        this.ghostBtn.style.top = rect.top + 'px';

        let placesRect = this.places.getBoundingClientRect();
        this.topLimit = placesRect.top - 5;
        this.bottomLimit = placesRect.bottom + 10;
    }
}

