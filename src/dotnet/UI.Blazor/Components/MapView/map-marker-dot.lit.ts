import { customElement } from 'lit/decorators.js';
import { css, html, LitElement } from 'lit';

@customElement('map-marker-dot')
export class MapMarkerDot extends LitElement {
    static styles = css`
        :host {
            display: block;
            width: 16px;
            height: 16px;
        }
        svg {
            display: block;
            width: 100%;
            height: 100%;
        }
    `;

    protected render(): unknown {
        return html`
            <svg viewBox="0 0 16 16" fill="none" xmlns="http://www.w3.org/2000/svg">
                <circle cx="8" cy="8" r="7" fill="#2970FF" stroke="white" stroke-width="2"/>
            </svg>
        `;
    }
}
