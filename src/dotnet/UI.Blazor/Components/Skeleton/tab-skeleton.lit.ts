import {customElement, property} from "lit/decorators.js";
import {css, html, LitElement} from "lit";
import {messageStyles} from "./styles.lit";
import {range} from 'lit/directives/range.js';
import {map} from 'lit/directives/map.js';

@customElement('tab-skeleton')
class TabSkeleton extends LitElement {
    static styles = [messageStyles, css`
        :host {
            display: flex;
            flex-direction: row;
            align-items: center;
            column-gap: 1rem;
            min-height: 2rem;
            margin: 0 0.5rem;
        }
        .c-line {
            display: flex;
            flex: 1 1 0;
            align-items: center;
            justify-content: center;
            height: 0.75rem;
            background: var(--skeleton);
            border-radius: 9999px;
            animation: pulse 2s infinite;
        }
    `];

    render(): unknown {
        return html`
            ${map(range(4), () => html`
                <div class="c-line" />
            `)}
        `;
    }
}
