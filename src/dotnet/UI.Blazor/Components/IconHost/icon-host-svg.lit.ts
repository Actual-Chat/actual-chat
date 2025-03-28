import { customElement, property } from 'lit/decorators.js';
import { css, html, LitElement, } from 'lit';

const DIST = '/dist/images/';
const EXTENSION = 'svg';
let ORIGINAL: SVGElement = null;
let HOST: HTMLElement = null;
let CONTAINER: HTMLElement = null;
let TEMPORARY: HTMLElement = null;
let ID = "";
let SAVED: SVGElement | null = null;

@customElement('icon-host-svg')
class IconHostSvg extends LitElement {

    @property() svgTitle: string;
    @property() class: string;

    render() {
        if (!this.svgTitle || this.svgTitle == "")
            return;

        ID = this.setSvgId(this.svgTitle);
        const srcUrl = DIST + this.svgTitle + '.' + EXTENSION;
        this.svgHandler(srcUrl).then(svg => {
            SAVED = svg;
        });

        if (!SAVED) {
            return html`
                <img
                    part='icon'
                    alt='${this.svgTitle}'
                    .src='${srcUrl}'
                />
            `;
        } else {
            return html`${SAVED}`;
        }
    }

    private setSvgId(svgTitle: string) {
        const parts = svgTitle.split('-');
        if (parts.length < 1)
            return '';

        let id = '';
        id += parts[0].toLowerCase();
        parts.slice(1).forEach(p => {
            let part = p.charAt(0).toUpperCase() + p.slice(1).toLowerCase();
            id += part;
        });
        id += 'Svg';
        return id;
    }

    private async svgHandler(srcUrl: string) {
        HOST = document.querySelector('.icon-host');
        if (!HOST)
            return;

        CONTAINER = HOST.querySelector('.icon-container');
        if (!CONTAINER)
            return;

        TEMPORARY = HOST.querySelector('.temporary-container');
        if (!TEMPORARY)
            return;

        let svgSymbols = CONTAINER.querySelectorAll(`#${ID}`);
        if (svgSymbols.length < 1) {
            await this.createSvgSymbolElement(srcUrl);
        }
        return this.getSavedSvg();
    }

    private getSavedSvg() {
        const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        const use = document.createElementNS("http://www.w3.org/2000/svg", "use");
        const originalSvg = document.getElementsByName(ID)[0];
        if (originalSvg && originalSvg.getAttribute('viewBox')) {
            svg.setAttribute('viewBox', originalSvg.getAttribute("viewBox"));
        }
        use.setAttribute("href", `#${ID}`);
        svg.appendChild(use);
        return svg;
    }

    private async createSvgSymbolElement(srcUrl: string) {
        TEMPORARY.innerHTML = '';
        ORIGINAL = await this.getOriginalSvg(srcUrl);
        if (!ORIGINAL)
            return;

        const symbol = document.createElementNS("http://www.w3.org/2000/svg", "symbol");
        symbol.setAttribute("id", ID);
        symbol.setAttribute("viewBox", ORIGINAL.getAttribute("viewBox"));

        while (ORIGINAL.firstChild) {
            symbol.appendChild(ORIGINAL.firstChild);
        }

        let newSvg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
        newSvg.setAttribute('name', ID);
        if (!this.isSvgExistInContainer(ID)) {
            newSvg.setAttribute('viewBox', ORIGINAL.getAttribute("viewBox"));
            newSvg.appendChild(symbol);
            CONTAINER.appendChild(newSvg);
            TEMPORARY.innerHTML = '';
        }
    }

    private isSvgExistInContainer(id: string) : boolean {
        let elements = CONTAINER.querySelectorAll(`#${id}`);
        return elements.length > 0;
    }

    private async getOriginalSvg(srcUrl: string) {
        TEMPORARY.innerHTML = '';
        try {
            let response = await fetch(srcUrl);
            if (!response.ok) {
                return null;
            }

            let data = await response.text();
            TEMPORARY.insertAdjacentHTML('beforeend', data);

            let originalSvg = TEMPORARY.querySelector('svg');
            if (!originalSvg) {
                return null;
            }

            return originalSvg;
        } catch (error) {
            return null;
        }
    }
}
