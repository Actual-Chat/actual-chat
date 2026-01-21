import {
    Subject,
    takeUntil,
    fromEvent
} from 'rxjs';

export class FontSizeSlider {
    private readonly disposed$: Subject<void> = new Subject<void>();
    private blazorRef: DotNet.DotNetObject;
    private readonly slider: HTMLElement;
    private readonly input: HTMLInputElement | null;
    private readonly tile: HTMLElement | null;
    private readonly fontSizes: string[];

    static create(slider: HTMLElement, blazorRef: DotNet.DotNetObject, fontSizes: string[], fontSize: string): FontSizeSlider {
        return new FontSizeSlider(slider, blazorRef, fontSizes, fontSize);
    }

    constructor(slider: HTMLElement, blazorRef: DotNet.DotNetObject, fontSizes: string[], fontSize: string) {
        this.slider = slider;
        this.blazorRef = blazorRef;
        this.fontSizes = fontSizes;
        this.input = this.slider.querySelector('input');
        if (!this.input)
            return;

        this.tile = this.slider.closest('.font-size-tile');
        if (!this.tile)
            return;

        this.input.min = '0';
        this.input.max = (fontSizes.length - 1).toString();
        const index = Math.max(0, fontSizes.indexOf(fontSize));
        this.input.value = index.toString();

        this.updateProgress(index);

        fromEvent(this.input, 'input')
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.onInput());
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private updateProgress(index: number) {
        const percent =
            index / (this.fontSizes.length - 1) * 100;

        this.input!.style.setProperty('--progress', `${percent}%`);
    }

    private onInput() {
        const index = Math.round(Number(this.input!.value));
        this.input!.value = index.toString();

        this.updateProgress(index);
        this.blazorRef.invokeMethodAsync('OnFontSizeChangedFromJs', index);
    }

    public setValue(fontSize: string) {
        const index = this.fontSizes.indexOf(fontSize);
        if (index === -1)
            return;

        this.input!.value = index.toString();
    }
}

