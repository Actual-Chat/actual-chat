import {customElement, property} from "lit/decorators.js";
import {html, LitElement} from "lit";
import { MessageWidth, randomIntFromInterval, StringHeight } from './helpers';

@customElement('string-skeleton')
class StringSkeletonLit extends LitElement {
    protected createRenderRoot() {
        return this;
    }

    @property()
    firstWidth = 1;
    @property()
    secondWidth = 10;
    @property()
    height : number = 3;
    @property()
    maxWidth: number = 0;
    @property({type: Boolean})
    system = false;
    @property({type: Boolean})
    rounded = false;
    @property()
    rootCls = "layout-body";

    render(): unknown {
        let wrapperWidthStyle = '';
        let messageWidthCls = '';
        let messageWidthStyle = 'width: 100%;';
        if (this.maxWidth === 0) {
            messageWidthCls = `${this.getWidth(this.getNumber(), this.getNumber(false))}`;
            messageWidthStyle = '';
        } else {
            wrapperWidthStyle = `max-width: ${this.maxWidth / 4}rem`;
            messageWidthCls = 'w-full';
        }
        const roundedCls = this.rounded ? 'rounded' : '';

        return html`
            <div class="string-skeleton-wrapper ${this.system ? "system-string" : ""}" style=${wrapperWidthStyle}>
                <div class="message string-skeleton ${this.getHeight(this.height)} ${messageWidthCls} ${roundedCls}" style="${messageWidthStyle}"></div>
            </div>
        `;
    }

    private getWidth(first: number, second: number) : string {
        let num = randomIntFromInterval(first, second);
        return MessageWidth[num];
    }

    private getHeight(height: number) : string {
        let num = (height < 1 || height > 10) ? 3 : height;
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
