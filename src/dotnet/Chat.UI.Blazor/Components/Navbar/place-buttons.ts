import { Subject } from 'rxjs';

export class PlaceButtons {
    private readonly disposed$: Subject<void> = new Subject<void>();
    private blazorRef: DotNet.DotNetObject;
    private readonly places: HTMLElement;
    private buttons: HTMLElement[] = [];
    private shiftX: number;
    private shiftY: number;
    private currentBtn: HTMLElement;
    private limitRect: DOMRect;
    private readonly longPress: number = 1000;

    static create(places: HTMLElement, blazorRef: DotNet.DotNetObject): PlaceButtons {
        return new PlaceButtons(places, blazorRef);
    }

    constructor(places: HTMLElement, blazorRef: DotNet.DotNetObject) {
        this.places = places;
        this.blazorRef = blazorRef;

        let navbarButtons = this.places.querySelectorAll('.navbar-button');
        navbarButtons.forEach(b => {
            this.buttons.push(b as HTMLElement);
        });
        for (const btn of this.buttons) {
            btn.addEventListener('pointerdown', this.onPointerDown);
            btn.addEventListener('pointerup', this.onPointerUp);
            btn.addEventListener('pointercancel', this.onPointerUp);
        }
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
        this.places.removeEventListener('dragover', this.onDragOver);
        this.places.removeEventListener('dragstart', this.onDragStart);
        this.places.removeEventListener('dragend', this.onDragEnd);
    }

    public onPointerDown = (event: Event) => {
        this.currentBtn = event.currentTarget as HTMLElement;
        this.places.addEventListener('pointermove', this.onPointerMove);
        console.log('currentBtn: ', this.currentBtn);
    }

    public onPointerMove = (event: PointerEvent) => {
        console.log('onPointerMove invoked.');
        this.limitRect = this.places.getBoundingClientRect();
        let btnRect = this.currentBtn.getBoundingClientRect();
        this.currentBtn.style.position = 'fixed';
        this.currentBtn.style.width = btnRect.width + 'px';
        this.currentBtn.style.height = btnRect.height + 'px';
        // this.currentBtn.style.left = event.clientX - this.currentBtn.clientWidth/2 + 'px';
        let top = event.clientY - this.currentBtn.clientHeight/2;
        if (top > this.limitRect.top) {
            top = this.limitRect.top;
        }
        if (top < this.limitRect.bottom - this.currentBtn.clientHeight) {
            top = this.limitRect.top - this.currentBtn.clientHeight;
        }
        this.currentBtn.style.top = top + 'px';

    }

    public onPointerUp = (event: Event) => {
        this.currentBtn.removeEventListener('pointerdown', this.onPointerDown);
        this.places.removeEventListener('pointermove', this.onPointerMove);
        this.currentBtn.style.position = 'static';
        console.log('Listeners removed.');
    }

    private onDragStart = (event: DragEvent) => {
        let target = event.target as HTMLElement;
        let btn = target.closest('.navbar-button');
        btn.classList.add('drag-btn');
    }

    private onDragEnd = (event: DragEvent) => {
        let target = event.target as HTMLElement;
        let btn = target.closest('.navbar-button');
        btn.classList.remove('drag-btn');
    }

    private onDragOver = (event: DragEvent) => {
        console.log('onDragOver invoked.');
        event.preventDefault();
        const activeElement = this.places.querySelector('.drag-btn');
        let target = event.target as HTMLElement;
        let currentElement = target.closest('.navbar-button');

        const isMovable = activeElement !== currentElement &&
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

