import { customElement, property } from 'lit/decorators.js';
import { createRef, Ref, ref } from 'lit/directives/ref.js';
import { html, LitElement, nothing } from 'lit';
import { delayAsync } from 'actuallab-core';
import { AC } from 'app-constants';

type ImageState = 'none' | 'skeleton' | 'thumbnail' | 'original';

const ImageStates: readonly string[] = ['skeleton', 'thumbnail', 'original'];

const RetryCount = 10;
const MaxRetryDelay = 30;

@customElement('image-skeleton')
export class ImageSkeleton extends LitElement {
    private _imageRef: Ref<HTMLImageElement> = createRef();
    private _imageState: ImageState = 'none';
    private _isRetrying = false;
    // The src whose retries were exhausted; tracked by value so a new src retries.
    private _failedSrc: string | null = null;

    @property() src: string;
    @property() thumbnailSrc: string;
    @property() title = '';
    @property({ type: Number }) width?: number;
    @property({ type: Number }) height?: number;
    // Razor's one-time seed. It renders a constant, so re-renders can't fight the
    // `data-image-state` this component writes — see docs/ui/components.md.
    @property({ attribute: 'initial-state' }) initialState = '';

    connectedCallback() {
        super.connectedCallback();
        // Blazor can detach and re-attach the element; re-seeding here would roll a
        // state that already advanced back to Razor's one-time value.
        if (this._imageState === 'none' && ImageStates.includes(this.initialState))
            this._imageState = this.initialState as ImageState;
        this.applyState();
    }

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

    // Protected/internal methods

    protected createRenderRoot() {
        return this;
    }

    // Private methods

    /** The `error` handler, so it must not hand a promise back to the DOM — nothing
     *  there would catch it, and retrying IS how this component handles failure. */
    private reloadImage(): void {
        void this.retryImage();
    }

    private async retryImage(): Promise<void> {
        // Giving up re-assigns a src that already failed, which fires `error` again;
        // without this the component would retry that same src forever.
        if (this._isRetrying || this._failedSrc === this.src)
            return;

        this._isRetrying = true;
        this._imageState = 'skeleton';
        this.applyState();
        const isSubDomain = this.isSubDomain(this.src);
        try {
            for (let attempt = 0; attempt < RetryCount; attempt++) {
                if (attempt >= 1) {
                    const delay = Math.min(MaxRetryDelay, Math.pow(2, attempt - 1));
                    await delayAsync(delay * 1000);
                }
                // Blocked/offline/DNS failures reject instead of returning a non-ok
                // response, which would otherwise skip the backoff and the fallback.
                const response = await fetch(this.src, { mode: isSubDomain ? undefined : 'cors' })
                    .catch(() => null);
                const blob = response?.ok ? await response.blob().catch(() => null) : null;
                if (!blob)
                    continue;

                const image = this._imageRef.value;
                if (image) {
                    const url = URL.createObjectURL(blob);
                    image.addEventListener('load', () => URL.revokeObjectURL(url), { once: true });
                    image.src = url;
                }

                return;
            }

            this._failedSrc = this.src;
            if (this._imageRef.value)
                this._imageRef.value.src = this.src;
        } finally {
            this._isRetrying = false;
        }
    }

    private imageLoaded(): void {
        this._imageState = 'original';
        this.applyState();
    }

    // A cached original often loads before a proxied thumbnail; downgrading here would
    // leave the blurry one showing for good, since imageLoaded() never fires again.
    private thumbnailLoaded(): void {
        if (this._imageState === 'original')
            return;

        this._imageState = 'thumbnail';
        this.applyState();
    }

    private applyState(): void {
        if (this._imageState === 'none')
            return;

        if (this.getAttribute('data-image-state') !== this._imageState)
            this.setAttribute('data-image-state', this._imageState);
    }

    private isSubDomain(url: string): boolean {
        return url.includes(AC.prodHost);
    }
}
