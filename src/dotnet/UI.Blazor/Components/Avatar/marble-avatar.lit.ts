import { customElement, property, state } from 'lit/decorators.js';
import { html, LitElement } from 'lit';

import { getRandomColor, getUnit, hashCode } from './avatar-utils';
import { cacheAvatar, getCachedUrl, onCached } from './avatar-cache';

const SIZE = 80;
const ELEMENTS = 3;

@customElement('marble-avatar')
export class MarbleAvatar extends LitElement {
    protected createRenderRoot() {
        return this;
    }

    @property() key: string;
    @property() title: string;
    @property({ type: Boolean }) doNotBlur = false;
    @property() colors: string[] = ['F56095', 'F5CD65', '00B27D', '37D3F5', '2F89EB'];

    @state() private cachedUrl: string | undefined;
    private unsubscribe: (() => void) | undefined;

    disconnectedCallback() {
        super.disconnectedCallback();
        this.unsubscribe?.();
        this.unsubscribe = undefined;
    }

    render() {
        const pixelSize = Math.ceil(this.clientWidth * (window.devicePixelRatio || 1)) || SIZE;
        const title = this.title || '';
        const cacheKey = `marble-${this.key}-${title}-${this.doNotBlur ? 1 : 0}-${pixelSize}`;
        const cached = this.cachedUrl ?? getCachedUrl(cacheKey);

        if (cached)
            return html`<img src='${cached}' width='100%' height='100%' draggable='false' alt='' />`;

        // Cache miss: render inline SVG and kick off async caching
        const svgString = MarbleAvatar.generateSvgString(this.key, this.colors, this.title, this.doNotBlur);
        this.unsubscribe?.();
        this.unsubscribe = onCached(cacheKey, url => {
            this.unsubscribe = undefined;
            this.cachedUrl = url;
        });
        cacheAvatar(cacheKey, svgString, pixelSize);

        const span = document.createElement('span');
        span.innerHTML = svgString;
        return html`${span}`;
    }

    static generateSvgString(key: string, colors: string[], title: string, doNotBlur: boolean): string {
        const numFromName = hashCode(key);
        const range = colors.length;
        const properties = Array.from({ length: ELEMENTS }, (_, i) => ({
            color: getRandomColor(numFromName + i, colors, range),
            translateX: getUnit(numFromName * (i + 1), SIZE / 10, 1),
            translateY: getUnit(numFromName * (i + 1), SIZE / 10, 2),
            scale: 1.2 + getUnit(numFromName * (i + 1), SIZE / 20, undefined) / 10,
            rotate: getUnit(numFromName * (i + 1), 360, 1),
        }));

        const blurEffect = doNotBlur
            ? ''
            : `<feGaussianBlur stdDeviation='7' result='effect1_foregroundBlur' />`;

        const displayTitle = !title ? '' : title[0].toUpperCase();

        return `<svg viewBox='0 0 ${SIZE} ${SIZE}' fill='none' xmlns='http://www.w3.org/2000/svg' width='${SIZE}' height='${SIZE}'>` +
            `<mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='${SIZE}' height='${SIZE}'>` +
            `<rect width='${SIZE}' height='${SIZE}' fill='#FFFFFF' /></mask>` +
            `<g mask='url(#m)'>` +
            `<rect width='${SIZE}' height='${SIZE}' fill='#${properties[0].color}' />` +
            `<path filter='url(#f)' ` +
            `d='M32.414 59.35L50.376 70.5H72.5v-71H33.728L26.5 13.381l19.057 27.08L32.414 59.35z' ` +
            `fill='#${properties[1].color}' ` +
            `transform='translate(${properties[1].translateX} ${properties[1].translateY}) rotate(${properties[1].rotate} ${SIZE / 2} ${SIZE / 2}) scale(${properties[2].scale})' />` +
            `<path filter='url(#f)' style='mix-blend-mode: overlay;' ` +
            `d='M22.216 24L0 46.75l14.108 38.129L78 86l-3.081-59.276-22.378 4.005 12.972 20.186-23.35 27.395L22.215 24z' ` +
            `fill='#${properties[2].color}' ` +
            `transform='translate(${properties[2].translateX} ${properties[2].translateY}) rotate(${properties[2].rotate} ${SIZE / 2} ${SIZE / 2}) scale(${properties[2].scale})' />` +
            `</g>` +
            `<text x='50%' y='50%' dominant-baseline='central' text-anchor='middle' font-family='TT Commons Pro, sans-serif' font-size='2.5em' font-weight='500' fill='white'>${displayTitle}</text>` +
            `<defs><filter id='f' filterUnits='userSpaceOnUse' color-interpolation-filters='sRGB'>` +
            `<feFlood flood-opacity='0' result='BackgroundImageFix' />` +
            `<feBlend in='SourceGraphic' in2='BackgroundImageFix' result='shape' />` +
            blurEffect +
            `</filter></defs></svg>`;
    }
}
