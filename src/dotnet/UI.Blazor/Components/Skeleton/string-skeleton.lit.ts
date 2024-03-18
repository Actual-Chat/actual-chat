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
            flex: 1;
        }
    `];

    render(): unknown {
        return html`
            <div class="message string-skeleton ${this.getWidth(this.getNumber(), this.getNumber(false))}"></div>
        `;
    }

    private getWidth(first: number, second: number): string {
        let num = randomIntFromInterval(first, second);
        return MessageWidth[num];
    }

    private getNumber(isFirst: boolean = true) : number {
        let width = isFirst ? Math.round(this.firstWidth) : Math.round(this.secondWidth);
        if (width < 1 || width > 10)
            return isFirst ? 1 : 10;
        else
            return width;
    }
}
