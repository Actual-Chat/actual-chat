import { customElement, property } from 'lit/decorators.js';
import { html, LitElement, svg } from 'lit';

import { getBoolDigit, getContrast, getRandomColor, getUnit, hashCode } from './avatar-utils';

const SIZE = 36;

let id = 0;
let multiplier = 1;
let w = 0;
let h = 0;

@customElement('beam-avatar')
class BeamAvatar extends LitElement {
    @property() key: string;
    @property() square: boolean = false;
    @property() css: string = "";
    @property() width: number = 36;
    @property() height: number = 36;
    @property() colors: string[] = ['FFDBA0', 'BBBEFF', '9294E1', 'FF9BC0', '0F2FE8'];

    private maskId = `beam-avatar-${++id}`;

    render() {
        const data = this.generateData(this.key, this.colors);
        w = this.width == 0 ? 36 : this.width;
        h = this.height == 0 ? 36 : this.height;
        const mouth = data.isMouthOpen
            ? svg`
                <path
                    d='${'M15 ' + (19 + data.mouthSpread) + 'c2 1 4 1 6 0'}'
                    stroke='${data.faceColor}'
                    fill='none'
                    stroke-linecap='round'
                />
            `
            : svg`
                <path
                    d='${'M13,' + (19 + data.mouthSpread) + ' a1,0.75 0 0,0 10,0'}'
                    fill='${data.faceColor}'
                />
            `;

        return html`
            <svg
                xmlns='http://www.w3.org/2000/svg'
                viewBox='${'0 0 ' + w + ' ' + h}'
                fill='none'
                role='img'
                width='100%'
                height='100%'
                style='${this.css}'
            >
                <mask id='${this.maskId}' maskUnits='userSpaceOnUse' x='0' y='0' width='${w}' height='${h}'>
                    <rect width='${w}' height='${h}' fill='#FFFFFF' />
                </mask>
                <g mask='url(#${this.maskId})'>
                    <rect width='${w}' height='${h}' fill='#${data.backgroundColor}' />
                    <rect
                        x='0'
                        y='0'
                        width='${w}'
                        height='${h * 2}'
                        transform='${
                            'translate(' +
                            data.wrapperTranslateX +
                            ' ' +
                            data.wrapperTranslateY +
                            ') rotate(' +
                            data.wrapperRotate +
                            ' ' +
                            w / 2 +
                            ' ' +
                            h / 2 +
                            ') scale(' +
                            data.wrapperScale +
                            ')'
                        }'
                        fill='#${data.wrapperColor}'
                        rx='${data.isCircle ? h : h / 6}'
                    />
                    <g
                        transform='${
                            'translate(' +
                            data.faceTranslateX +
                            ' ' +
                            data.faceTranslateY +
                            ') rotate(' +
                            data.faceRotate +
                            ' ' +
                            w / 2 +
                            ' ' +
                            h / 2 +
                            ')'
                        }'
                    >
                        ${mouth}
                        <rect
                            x='${14 - data.eyeSpread}'
                            y='${h / 2.5}'
                            width='${1.5 * multiplier}'
                            height='${2 * multiplier}'
                            rx='1'
                            stroke='none'
                            fill='${data.faceColor}'
                        />
                        <rect
                            x='${20 + data.eyeSpread}'
                            y='${h / 2.5}'
                            width='${1.5 * multiplier}'
                            height='${2 * multiplier}'
                            rx='1'
                            stroke='none'
                            fill='${data.faceColor}'
                        />
                    </g>
                </g>
            </svg>
        `;
    }

    private generateData(key: string, colors: string[]) {
        let html = document.documentElement;
        let fs = window.getComputedStyle(html, null).getPropertyValue('--font-size');
        let fontSize = parseFloat(fs);
        if (fontSize != 0 && fontSize != 16)
            multiplier = fontSize / 16;

        console.log('multiplier: ', multiplier);
        w = this.width * multiplier;
        h = this.height * multiplier;

        const numFromName = hashCode(key);
        const range = colors && colors.length;
        const wrapperColor = getRandomColor(numFromName, colors, range);
        const preTranslateX = getUnit(numFromName, 10, 1);
        const wrapperTranslateX = preTranslateX < 5 ? preTranslateX + h / 9 : preTranslateX;
        const preTranslateY = getUnit(numFromName, 10, 2);
        const wrapperTranslateY = preTranslateY < 5 ? preTranslateY + h / 9 : preTranslateY;
        return {
            wrapperColor: wrapperColor,
            faceColor: getContrast(wrapperColor),
            backgroundColor: getRandomColor(numFromName + 13, colors, range),
            wrapperTranslateX: wrapperTranslateX,
            wrapperTranslateY: wrapperTranslateY,
            wrapperRotate: getUnit(numFromName, 360, undefined),
            wrapperScale: 1 + getUnit(numFromName, h / 12, undefined) / 10,
            isMouthOpen: getBoolDigit(numFromName, 2),
            isCircle: getBoolDigit(numFromName, 1),
            eyeSpread: getUnit(numFromName, 5, undefined),
            mouthSpread: getUnit(numFromName, 3, undefined),
            faceRotate: getUnit(numFromName, 10, 3),
            faceTranslateX: wrapperTranslateX > h / 6 ? wrapperTranslateX / 2 : getUnit(numFromName, 8, 1),
            faceTranslateY: wrapperTranslateY > h / 6 ? wrapperTranslateY / 2 : getUnit(numFromName, 7, 2),
        };
    }
}
