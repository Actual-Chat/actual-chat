import { html, LitElement, TemplateResult } from 'lit';
import { state } from 'lit/decorators.js';

import { SvgCache } from './svg-cache';

export abstract class GeneratedAvatar extends LitElement {
    @state() private pixelSize = 0;
    @state() private cachedUrl: string | undefined;
    private cachedUrlKey: string | undefined;
    private unsubscribe: (() => void) | undefined;
    private resizeObserver: ResizeObserver | undefined;

    protected createRenderRoot() {
        return this;
    }

    connectedCallback() {
        super.connectedCallback();
        this.resizeObserver?.disconnect();
        this.resizeObserver = new ResizeObserver(() => this.measure());
        // Observe the sized parent (.c-content); the host itself may be inline.
        this.resizeObserver.observe(this.parentElement ?? this);
        this.measure();
    }

    disconnectedCallback() {
        super.disconnectedCallback();
        this.resizeObserver?.disconnect();
        this.resizeObserver = undefined;
        this.unsubscribe?.();
        this.unsubscribe = undefined;
    }

    render(): TemplateResult {
        if (this.pixelSize <= 0)
            return GeneratedAvatar.renderSkeleton();

        const cacheKey = this.getCacheKey(this.pixelSize);
        const cached = this.cachedUrlKey === cacheKey ? this.cachedUrl : SvgCache.get(cacheKey);
        if (cached)
            return html`<img src='${cached}' width='100%' height='100%' draggable='false' alt='' />`;

        const svgString = this.getSvgString();
        this.unsubscribe?.();
        this.unsubscribe = SvgCache.onCached(cacheKey, url => {
            this.unsubscribe = undefined;
            this.cachedUrlKey = cacheKey;
            this.cachedUrl = url;
        });
        SvgCache.add(cacheKey, svgString, this.pixelSize);

        return GeneratedAvatar.renderSkeleton();
    }

    protected abstract getCacheKey(pixelSize: number): string;
    protected abstract getSvgString(): string;

    // Private methods

    private measure(): void {
        // getBoundingClientRect works for inline hosts too, unlike clientWidth.
        const width = this.getBoundingClientRect().width;
        const pixelSize = Math.ceil(width * (window.devicePixelRatio || 1));
        if (pixelSize > 0 && pixelSize !== this.pixelSize)
            this.pixelSize = pixelSize;
    }

    private static renderSkeleton(): TemplateResult {
        return html`<div style='width:100%;height:100%;border-radius:50%;background:var(--background-04)'></div>`;
    }
}
