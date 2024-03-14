import {customElement, property} from "lit/decorators.js";
import {css, html, LitElement} from "lit";
import {HTMLTemplateResult} from "lit-html/development/lit-html";
import {MessageWidth, randomIntFromInterval} from "./helpers";
import {messageStyles} from "./styles.lit";

@customElement('string-skeleton')
class StringSkeletonLit extends LitElement {
    @property()
    firstWidth = 1;
    @property()
    secondWidth = 10;
    @property()
    rootCls = "layout-body";

    static styles = [messageStyles, css`
        :host {
            display: flex;
            width: 100%;
        }
    `];

    render(): unknown {
        // noinspection JSMismatchedCollectionQueryUpdate

        return html`
            <div class="message animated-skeleton string-skeleton ${this.getWidth(this.getFirstWidth(), this.getSecondWidth())}"></div>
        `;
    }

    private getWidth(first: number, second: number): string {
        let num = randomIntFromInterval(first, second);
        return MessageWidth[num];
    }

    private getFirstWidth() : number {
        let width = this.firstWidth;
        if (Math.round(this.firstWidth) < 1 || Math.round(this.firstWidth) > 10)
            width = 1;
        else
            width = Math.round(this.firstWidth);
        return width;
    }

    private getSecondWidth() : number {
        let width = this.secondWidth;
        if (Math.round(this.secondWidth) < 1 || Math.round(this.secondWidth) > 10)
            width = 10;
        else
            width = Math.round(this.secondWidth);
        return width;
    }
}
