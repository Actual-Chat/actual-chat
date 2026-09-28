// TODO: Fix ESLint errors
/* eslint-disable @typescript-eslint/no-floating-promises */
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
    private readonly sizeLabel: HTMLElement | null;
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

        this.sizeLabel = this.slider.querySelector('.c-size');

        this.input.min = '0';
        this.input.max = (fontSizes.length - 1).toString();
        const index = Math.max(0, fontSizes.indexOf(fontSize));
        this.input.value = index.toString();

        this.update(index);

        fromEvent(this.input, 'input')
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.onInput());
        fromEvent(this.input, 'change')
            .pipe(takeUntil(this.disposed$))
            .subscribe(() => this.onChange());
    }

    public dispose() {
        if (this.disposed$.closed)
            return;

        this.disposed$.next();
        this.disposed$.complete();
    }

    private update(index: number) {
        const size = this.fontSizes[index];
        const percent = index / (this.fontSizes.length - 1) * 100;

        this.input!.style.setProperty('--progress', `${percent}%`);
        this.slider.style.setProperty('--preview-font-size', size);
        if (this.sizeLabel)
            this.sizeLabel.textContent = size;
    }

    private onInput() {
        const index = Math.round(Number(this.input!.value));
        this.input!.value = index.toString();
        this.update(index);
    }

    private onChange() {
        const index = Math.round(Number(this.input!.value));
        this.blazorRef.invokeMethodAsync('OnFontSizeChangedFromJs', index);
    }

    public setValue(fontSize: string) {
        const index = this.fontSizes.indexOf(fontSize);
        if (index === -1)
            return;

        this.input!.value = index.toString();
        this.update(index);
    }
}

