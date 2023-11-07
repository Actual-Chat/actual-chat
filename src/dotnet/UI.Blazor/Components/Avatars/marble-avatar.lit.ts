import { customElement, property } from 'lit/decorators.js';
import { html, LitElement } from 'lit';

import { hashCode, getUnit, getRandomColor } from './utilities';

const SIZE = 80;
const ELEMENTS = 3;

let id = 0;

@customElement('marble-avatar')
class MarbleAvatar extends LitElement {
    @property() name: string;
    @property() title: string;
    @property() square: boolean = false;
    @property() colors: string[] = ['FF649C', 'FFD569', '00D193', '39DBFF', '3395FF'];

    private maskId = `marble-avatar-${++id}`;

    render() {
        const properties = this.generateColors(this.name, this.colors);
        return html`
            <svg
                viewBox='${'0 0 ' + SIZE + ' ' + SIZE}'
                fill='none'
                role='img'
                xmlns='http://www.w3.org/2000/svg'
                width='100%'
                height='100%'
            >
                <mask id='${this.maskId}' maskUnits='userSpaceOnUse' x='0' y='0' width='${SIZE}' height='${SIZE}'>
                    <rect width='${SIZE}' height='${SIZE}' rx='${this.square ? undefined : SIZE * 2}' fill='#FFFFFF' />
                </mask>
                <g mask='url(#${this.maskId})'>
                    <rect width='${SIZE}' height='${SIZE}' fill='#${properties[0].color}' />
                    <path
                        filter='url(#prefix__filter0_f)'
                        d='M32.414 59.35L50.376 70.5H72.5v-71H33.728L26.5 13.381l19.057 27.08L32.414 59.35z'
                        fill='#${properties[1].color}'
                        transform='${
                            'translate(' +
                            properties[1].translateX +
                            ' ' +
                            properties[1].translateY +
                            ') rotate(' +
                            properties[1].rotate +
                            ' ' +
                            SIZE / 2 +
                            ' ' +
                            SIZE / 2 +
                            ') scale(' +
                            properties[2].scale +
                            ')'
                        }'
                    />
                    <path
                        filter='url(#prefix__filter0_f)'
                        style='mix-blend-mode: overlay;'
                        d='M22.216 24L0 46.75l14.108 38.129L78 86l-3.081-59.276-22.378 4.005 12.972 20.186-23.35 27.395L22.215 24z'
                        fill='#${properties[2].color}'
                        transform='${
                            'translate(' +
                            properties[2].translateX +
                            ' ' +
                            properties[2].translateY +
                            ') rotate(' +
                            properties[2].rotate +
                            ' ' +
                            SIZE / 2 +
                            ' ' +
                            SIZE / 2 +
                            ') scale(' +
                            properties[2].scale +
                            ')'
                        }'
                    />
                </g>
                <text x="50%"
                      y="55%"
                      dominant-baseline="middle"
                      text-anchor="middle"
                      style='fill: white; font-size: 2.5em; font-weight: 500;'>
                    ${this.title}
                </text>
                <defs>
                    <filter
                        id='prefix__filter0_f'
                        filterUnits='userSpaceOnUse'
                        color-interpolation-filters='sRGB'
                    >
                        <feFlood flood-opacity='0' result='BackgroundImageFix' />
                        <feBlend in='SourceGraphic' in2='BackgroundImageFix' result='shape' />
                        <feGaussianBlur stdDeviation='7' result='effect1_foregroundBlur' />
                    </filter>
                </defs>
            </svg>
        `;
    }

    private generateColors(name, colors) {
        const numFromName = hashCode(name);
        const range = colors && colors.length;

        const elementsProperties = Array.from({ length: ELEMENTS }, (_, i) => ({
            color: getRandomColor(numFromName + i, colors, range),
            translateX: getUnit(numFromName * (i + 1), SIZE / 10, 1),
            translateY: getUnit(numFromName * (i + 1), SIZE / 10, 2),
            scale: 1.2 + getUnit(numFromName * (i + 1), SIZE / 20, undefined) / 10,
            rotate: getUnit(numFromName * (i + 1), 360, 1),
        }));

        return elementsProperties;
    }
}
