import {customElement} from "lit/decorators.js";
import {html, LitElement} from "lit";
import {HTMLTemplateResult} from "lit-html/development/lit-html";
import {MessageWidth, randomIntFromInterval} from "./helpers";

@customElement('place-menu-button-skeleton')
class PlaceMenuButtonSkeleton extends LitElement {
    protected createRenderRoot() {
        return this;
    }

    render(): HTMLTemplateResult {
        return html`
            <div class="message-skeleton animated-skeleton place">
                <div class="c-container">
                    <div class="title message-content-skeleton ${this.getMessageWidth(4, 7)}"></div>
                    <div class="place-info-container">
                        <div class="message-content-skeleton ${this.getMessageWidth(3, 5)}"></div>
                        <div class="message-content-skeleton ${this.getMessageWidth(2, 4)}"></div>
                    </div>
                </div>
            </div>
        `;
    }

    private getMessageWidth(first: number, second: number): string {
        let num = randomIntFromInterval(first, second);
        return MessageWidth[num];
    }
}
