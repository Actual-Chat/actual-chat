import { customElement, property } from 'lit/decorators.js';
import { createRef, Ref, ref } from 'lit/directives/ref.js';
import { html, LitElement, nothing } from 'lit';
import { delayAsync } from 'actuallab-core';
import { AC } from 'app-constants';

type ImageState = 'none' | 'skeleton' | 'thumbnail' | 'original';

@customElement('image-skeleton')
export class ImageSkeleton extends LitElement {
    protected createRenderRoot() {
        return this;
    }

    @property({ reflect: true }) class: string;
    @property() src: string;
    @property() thumbnailSrc: string;
    @property() title = '';
    @property({ type: Number }) width?: number;
    @property({ type: Number }) height?: number;

    private _imageRef: Ref<HTMLImageElement> = createRef();
    private _imageState: ImageState = 'none';

    connectedCallback() {
        super.connectedCallback();
        if (this.classList.contains('show-image-original'))
            this._imageState = 'original';
        else if (this.classList.contains('show-image-thumbnail'))
            this._imageState = 'thumbnail';
        else if (this.classList.contains('show-image-skeleton'))
            this._imageState = 'skeleton';
    }

    updated(changedProperties: Map<string, unknown>) {
        if (changedProperties.has('class') && this._imageState !== 'none') {
            const stateClass = `show-image-${this._imageState}`;
            if (!this.classList.contains(stateClass)) {
                this.classList.remove('show-image-skeleton', 'show-image-thumbnail', 'show-image-original');
                this.classList.add(stateClass);
            }
        }
    }

    // for tests
    // willUpdate(changedProperties: any) {
    //     if (changedProperties.has("src")) {
    //         if (Math.floor(Math.random() * 100) % 2 === 0) {
    //             const original = this.src;
    //             this.src = "https://some.host/invalid.svg";
    //             setTimeout(() => {
    //                 this.src = original;
    //             }, 3000)
    //         }
    //     }
    // }

    render() {
        const isSubDomain = this.isSubDomain(this.src);
        // Width/height attributes give the browser an intrinsic aspect-ratio
        // hint BEFORE the bitmap loads, preventing CLS. CSS w-full/h-full /
        // object-fit still drive the actual rendered size.
        const w = this.width && this.width > 0 ? this.width : nothing;
        const h = this.height && this.height > 0 ? this.height : nothing;
        if (this.thumbnailSrc && this.thumbnailSrc != '') {
            return html`
                <img
                    part='image'
                    ${ref(this._imageRef)}
                    class='image'
                    crossorigin='${isSubDomain ? nothing : 'anonymous'}'
                    draggable='false'
                    alt=''
                    width='${w}'
                    height='${h}'
                    .src='${this.src}'
                    .title='${this.title}'
                    @load='${this.imageLoaded.bind(this)}'
                    @error='${this.reloadImage.bind(this)}'
                />
                <img
                    part='image-thumbnail'
                    class='image-thumbnail'
                    crossorigin='${isSubDomain ? nothing : 'anonymous'}'
                    draggable='false'
                    alt=''
                    width='${w}'
                    height='${h}'
                    .src='${this.thumbnailSrc}'
                    .title='${this.title}'
                    @load='${this.thumbnailLoaded.bind(this)}'
                />
            `;
        } else {
            return html`
                <img
                    part='image'
                    ${ref(this._imageRef)}
                    class='image'
                    crossorigin='${isSubDomain ? nothing : 'anonymous'}'
                    draggable='false'
                    alt=''
                    width='${w}'
                    height='${h}'
                    .src='${this.src}'
                    .title='${this.title}'
                    @load='${this.imageLoaded.bind(this)}'
                    @error='${this.reloadImage.bind(this)}'
                />
            `;
        }
    }

    async reloadImage(): Promise<void> {
        this._imageState = 'skeleton';
        this.classList.remove('show-image-original');
        this.classList.add('show-image-skeleton');

        const isSubDomain = this.isSubDomain(this.src);
        for (let attempt = 0; attempt < 10; attempt++) {
            if (attempt >= 1) {
                const delay = Math.min(30, Math.pow(2, attempt - 1));
                await delayAsync(delay * 1000);
            }
            const response = await fetch(this.src, { mode: isSubDomain ? undefined : 'cors' });
            if (response.ok) {
                const blob = await response.blob();
                if (this._imageRef.value)
                    this._imageRef.value.src = URL.createObjectURL(blob);
                return;
            }
        }
        if (this._imageRef.value)
            this._imageRef.value.src = this.src;
    }

    imageLoaded(): void {
        this._imageState = 'original';
        this.classList.remove('show-image-skeleton', 'show-image-thumbnail');
        if (!this.classList.contains('show-image-original'))
            this.classList.add('show-image-original');
    }

    thumbnailLoaded(): void {
        this._imageState = 'thumbnail';
        this.classList.remove('show-image-skeleton');
        if (!this.classList.contains('show-image-thumbnail') && (!this.classList.contains('show-image-original')))
            this.classList.add('show-image-thumbnail');
    }

    isSubDomain(url: string): boolean {
        return url.includes(AC.prodHost);
    }
}
