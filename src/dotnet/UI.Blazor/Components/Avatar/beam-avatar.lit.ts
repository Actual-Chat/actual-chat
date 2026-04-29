import { customElement, property, state } from 'lit/decorators.js';
import { html, LitElement } from 'lit';

import { getBoolDigit, getContrast, getRandomColor, getUnit, hashCode } from './avatar-utils';
import { SvgCache } from './svg-cache';

const SIZE = 36;

@customElement('beam-avatar')
export class BeamAvatar extends LitElement {
    protected createRenderRoot() {
        return this;
    }

    @property() key: string;
    @property() square = false;
    @property() colors: string[] = ['FFDBA0', 'BBBEFF', '9294E1', 'FF9BC0', '0F2FE8'];

    @state() private cachedUrl: string | undefined;
    private cachedUrlKey: string | undefined;
    private unsubscribe: (() => void) | undefined;

    disconnectedCallback() {
        super.disconnectedCallback();
        this.unsubscribe?.();
        this.unsubscribe = undefined;
    }

    render() {
        const pixelSize = Math.ceil(this.clientWidth * (window.devicePixelRatio || 1)) || SIZE;
        const cacheKey = `beam-${this.key}-${pixelSize}`;
        const cached = this.cachedUrlKey === cacheKey ? this.cachedUrl : SvgCache.get(cacheKey);

        if (cached)
            return html`<img src='${cached}' width='100%' height='100%' draggable='false' alt='' />`;

        // Cache miss: show skeleton and kick off async caching
        const svgString = BeamAvatar.generateSvgString(this.key, this.colors, this.square);
        this.unsubscribe?.();
        this.unsubscribe = SvgCache.onCached(cacheKey, url => {
            this.unsubscribe = undefined;
            this.cachedUrlKey = cacheKey;
            this.cachedUrl = url;
        });
        SvgCache.add(cacheKey, svgString, pixelSize);

        return html`<div style='width:100%;height:100%;border-radius:50%;background:var(--background-04)'></div>`;
    }

    static generateSvgString(key: string, colors: string[], square: boolean): string {
        const numFromName = hashCode(key);
        const range = colors.length;
        const wrapperColor = getRandomColor(numFromName, colors, range);
        const faceColor = getContrast(wrapperColor);
        const backgroundColor = getRandomColor(numFromName + 13, colors, range);
        const preTranslateX = getUnit(numFromName, 10, 1);
        const wrapperTranslateX = preTranslateX < 5 ? preTranslateX + SIZE / 9 : preTranslateX;
        const preTranslateY = getUnit(numFromName, 10, 2);
        const wrapperTranslateY = preTranslateY < 5 ? preTranslateY + SIZE / 9 : preTranslateY;
        const wrapperRotate = getUnit(numFromName, 360);
        const wrapperScale = 1 + getUnit(numFromName, SIZE / 12) / 10;
        const isMouthOpen = getBoolDigit(numFromName, 2);
        const isCircle = getBoolDigit(numFromName, 1);
        const eyeSpread = getUnit(numFromName, 5);
        const mouthSpread = getUnit(numFromName, 3);
        const faceRotate = getUnit(numFromName, 10, 3);
        const faceTranslateX = wrapperTranslateX > SIZE / 6 ? wrapperTranslateX / 2 : getUnit(numFromName, 8, 1);
        const faceTranslateY = wrapperTranslateY > SIZE / 6 ? wrapperTranslateY / 2 : getUnit(numFromName, 7, 2);

        const rx = square ? '' : `rx='${SIZE * 2}'`;
        const wrapperRx = isCircle ? SIZE : SIZE / 6;
        const mouth = isMouthOpen
            ? `<path d='M15 ${19 + mouthSpread}c2 1 4 1 6 0' stroke='${faceColor}' fill='none' stroke-linecap='round' />`
            : `<path d='M13,${19 + mouthSpread} a1,0.75 0 0,0 10,0' fill='${faceColor}' />`;

        return `<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 ${SIZE} ${SIZE}' fill='none' width='${SIZE}' height='${SIZE}'>` +
            `<mask id='m' maskUnits='userSpaceOnUse' x='0' y='0' width='${SIZE}' height='${SIZE}'>` +
            `<rect width='${SIZE}' height='${SIZE}' ${rx} fill='#FFFFFF' /></mask>` +
            `<g mask='url(#m)'>` +
            `<rect width='${SIZE}' height='${SIZE}' fill='#${backgroundColor}' />` +
            `<rect x='0' y='0' width='${SIZE}' height='${SIZE}' ` +
            `transform='translate(${wrapperTranslateX} ${wrapperTranslateY}) rotate(${wrapperRotate} ${SIZE / 2} ${SIZE / 2}) scale(${wrapperScale})' ` +
            `fill='#${wrapperColor}' rx='${wrapperRx}' />` +
            `<g transform='translate(${faceTranslateX} ${faceTranslateY}) rotate(${faceRotate} ${SIZE / 2} ${SIZE / 2})'>` +
            mouth +
            `<rect x='${14 - eyeSpread}' y='14' width='1.5' height='2' rx='1' stroke='none' fill='${faceColor}' />` +
            `<rect x='${20 + eyeSpread}' y='14' width='1.5' height='2' rx='1' stroke='none' fill='${faceColor}' />` +
            `</g></g></svg>`;
    }
}
