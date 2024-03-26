import {customElement, property} from "lit/decorators.js";
import {css, html, LitElement} from "lit";
import {HTMLTemplateResult} from "lit-html/development/lit-html";
import {MessageWidth, randomIntFromInterval} from "./helpers";
import {messageStyles} from "./styles.lit";

@customElement('round-skeleton')
class RoundSkeletonLit extends LitElement {
    @property()
    radius = 10;
    @property()
    rootCls = "layout-body";

    static styles = [messageStyles, css`
        :host {
            display: flex;
            flex: 1;
        }
    `];

    render(): unknown {
        return html`
            <div class="animated-skeleton round-skeleton radius-${this.radius}"></div>
        `;
    }
}
