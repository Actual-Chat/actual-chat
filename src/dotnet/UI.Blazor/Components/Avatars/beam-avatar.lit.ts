import { customElement, property } from 'lit/decorators.js';
import { html, svg, LitElement } from 'lit';

import { hashCode, getUnit, getBoolean, getRandomColor, getContrast } from './utilities';

const SIZE = 36;

let id = 0;

@customElement('beam-avatar')
class BeamAvatar extends LitElement {
    @property() name: string;
    @property() square: boolean = false;
    @property() colors: string[] = ['FFDBA0', 'BBBEFF', '9294E1', 'FF9BC0', '0F2FE8'];

    private maskId = `beam-avatar-${++id}`;

    render() {
        const data = this.generateData(this.name, this.colors);
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
                viewBox='${'0 0 ' + SIZE + ' ' + SIZE}'
                fill='none'
                role='img'
                width='100%'
                height='100%'
            >
                <mask id='${this.maskId}' maskUnits='userSpaceOnUse' x='0' y='0' width='${SIZE}' height='${SIZE}'>
                    <rect width='${SIZE}' height='${SIZE}' rx='${this.square ? undefined : SIZE * 2}' fill='#FFFFFF' />
                </mask>
                <g mask='url(#${this.maskId})'>
                    <rect width='${SIZE}' height='${SIZE}' fill='#${data.backgroundColor}' />
                    <rect
                        x='0'
                        y='0'
                        width='${SIZE}'
                        height='${SIZE}'
                        transform='${
                            'translate(' +
                            data.wrapperTranslateX +
                            ' ' +
                            data.wrapperTranslateY +
                            ') rotate(' +
                            data.wrapperRotate +
                            ' ' +
                            SIZE / 2 +
                            ' ' +
                            SIZE / 2 +
                            ') scale(' +
                            data.wrapperScale +
                            ')'
                        }'
                        fill='#${data.wrapperColor}'
                        rx='${data.isCircle ? SIZE : SIZE / 6}'
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
                            SIZE / 2 +
                            ' ' +
                            SIZE / 2 +
                            ')'
                        }'
                    >
                        ${mouth}
                        <rect
                            x='${14 - data.eyeSpread}'
                            y='14'
                            width='1.5'
                            height='2'
                            rx='1'
                            stroke='none'
                            fill='${data.faceColor}'
                        />
                        <rect
                            x='${20 + data.eyeSpread}'
                            y='14'
                            width='1.5'
                            height='2'
                            rx='1'
                            stroke='none'
                            fill='${data.faceColor}'
                        />
                    </g>
                </g>
            </svg>
        `;
    }

    private generateData(name, colors) {
        const numFromName = hashCode(name);
        const range = colors && colors.length;
        const wrapperColor = getRandomColor(numFromName, colors, range);
        const preTranslateX = getUnit(numFromName, 10, 1);
        const wrapperTranslateX = preTranslateX < 5 ? preTranslateX + SIZE / 9 : preTranslateX;
        const preTranslateY = getUnit(numFromName, 10, 2);
        const wrapperTranslateY = preTranslateY < 5 ? preTranslateY + SIZE / 9 : preTranslateY;

        const data = {
            wrapperColor: wrapperColor,
            faceColor: getContrast(wrapperColor),
            backgroundColor: getRandomColor(numFromName + 13, colors, range),
            wrapperTranslateX: wrapperTranslateX,
            wrapperTranslateY: wrapperTranslateY,
            wrapperRotate: getUnit(numFromName, 360, undefined),
            wrapperScale: 1 + getUnit(numFromName, SIZE / 12, undefined) / 10,
            isMouthOpen: getBoolean(numFromName, 2),
            isCircle: getBoolean(numFromName, 1),
            eyeSpread: getUnit(numFromName, 5, undefined),
            mouthSpread: getUnit(numFromName, 3, undefined),
            faceRotate: getUnit(numFromName, 10, 3),
            faceTranslateX: wrapperTranslateX > SIZE / 6 ? wrapperTranslateX / 2 : getUnit(numFromName, 8, 1),
            faceTranslateY: wrapperTranslateY > SIZE / 6 ? wrapperTranslateY / 2 : getUnit(numFromName, 7, 2),
        };

        return data;
    }
}
