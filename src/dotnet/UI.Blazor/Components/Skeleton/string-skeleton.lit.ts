import {customElement, property} from "lit/decorators.js";
import {css, html, LitElement} from "lit";
import {HTMLTemplateResult} from "lit-html/development/lit-html";
import { MessageWidth, randomIntFromInterval, StringHeight } from './helpers';
import {messageStyles} from "./styles.lit";

@customElement('string-skeleton')
class StringSkeletonLit extends LitElement {
    @property()
    firstWidth = 1;
    @property()
    secondWidth = 10;
    @property()
    height = 3;
    @property({type: Boolean})
    system = false;
    @property()
    rootCls = "layout-body";

    static styles = [messageStyles, css`
        :host {
            display: flex;
            flex-direction: row;
            flex: 1 1 0;
        }
    `];

    render(): unknown {
        return html`
            <div class="string-skeleton-wrapper ${this.system ? "system-string" : ""}">
                <div class="message string-skeleton ${this.getHeight(this.height)} ${this.getWidth(this.getNumber(), this.getNumber(false))}"></div>
            </div>
        `;
    }

    private getWidth(first: number, second: number) : string {
        let num = randomIntFromInterval(first, second);
        return MessageWidth[num];
    }

    private getHeight(height: number) : string {
        let num = (height < 1 || height > 6) ? 3 : height;
        return StringHeight[num];
    }

    private getNumber(isFirst: boolean = true) : number {
        let width = isFirst ? Math.round(this.firstWidth) : Math.round(this.secondWidth);
        if (width < 1 || width > 10)
            return isFirst ? 1 : 10;
        else
            return width;
    }
}
