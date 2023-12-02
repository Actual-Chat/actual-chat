import { customElement, property } from 'lit/decorators.js';
import { html, LitElement } from 'lit';

import { getRandomColor, getUnit, hashCode } from './avatar-utils';

const SIZE = 80;
const ELEMENTS = 3;

let id = 0;

@customElement('marble-avatar')
class MarbleAvatar extends LitElement {
    @property() key: string;
    @property() title: string;
    @property() width: number = 80;
    @property() height: number = 80;
    @property() css: string = "";
    @property() colors: string[] = ['F56095', 'F5CD65', '00B27D', '37D3F5', '2F89EB'];

    private maskId = `marble-avatar-${++id}`;

    render() {
        const properties = this.generateColors(this.key, this.colors);
        return html`
            <svg
                viewBox='${'0 0 ' + this.width + ' ' + this.height}'
                fill='none'
                role='img'
                xmlns='http://www.w3.org/2000/svg'
                width='${this.width}'
                height='${this.height}'
                style='${this.css}'
            >
                <mask id='${this.maskId}' maskUnits='userSpaceOnUse' x='0' y='0' width='${this.width}' height='${this.height}'>
                    <rect width='${this.width}' height='${this.height}' fill='#FFFFFF'/>
                </mask>
                <g mask='url(#${this.maskId})'>
                    <rect width='${this.width}' height='${this.height}' fill='#${properties[0].color}' />
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
                            this.height / 2 +
                            ' ' +
                            this.height / 2 +
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
                            this.height / 2 +
                            ' ' +
                            this.height / 2 +
                            ') scale(' +
                            properties[2].scale +
                            ')'
                        }'
                    />
                </g>
                <text x="50%"
                      y="50%"
                      dominant-baseline="central"
                      text-anchor="middle"
                      font-size='${this.height / 2}'
                      font-weight='500'
                      fill='white'>
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

    private generateColors(key: string, colors: string[]) {
        const numFromName = hashCode(key);
        const range = colors && colors.length;
        return Array.from({ length: ELEMENTS }, (_, i) => ({
            color: getRandomColor(numFromName + i, colors, range),
            translateX: getUnit(numFromName * (i + 1), this.width / 10, 1),
            translateY: getUnit(numFromName * (i + 1), this.height / 10, 2),
            scale: 1.2 + getUnit(numFromName * (i + 1), this.height / 20, undefined) / 10,
            rotate: getUnit(numFromName * (i + 1), 360, 1),
        }));
    }
}
