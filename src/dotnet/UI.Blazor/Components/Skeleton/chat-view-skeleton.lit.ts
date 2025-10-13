import { customElement, property } from 'lit/decorators.js';
import { html, LitElement } from 'lit';
import { guard } from 'lit/directives/guard.js';
import { map } from 'lit/directives/map.js';
import { range } from 'lit/directives/range.js';
import { MessageWidth, randomIntFromInterval } from './helpers';
import { fastRaf } from 'fast-raf';

@customElement('chat-view-skeleton')
class ChatViewSkeleton extends LitElement {
    protected createRenderRoot() {
        return this;
    }

    private observer: IntersectionObserver;

    @property()
    public count = 2;
    @property()
    class = '';

    connectedCallback() {
        super.connectedCallback();

        this.observer = new IntersectionObserver(entries => {
            const isVisible = entries.some(e => e.isIntersecting);
            // console.warn('isVisible', isVisible, entries);
            fastRaf({
                write: () => this.classList.toggle('animated-skeleton', isVisible && this.count > 0)
            });
        });
        this.observer.observe(this);
    }

    disconnectedCallback() {
        super.disconnectedCallback();

        this.observer.disconnect();
    }

    protected render(): unknown {
        const { count } = this;
        return html`
            ${guard([count], () => map(range(count), () => html`
                <div class="message-skeleton">
                    <div class="message-avatar-wrapper">
                        <div class="message-avatar"></div>
                    </div>
                    <div class="c-container">
                        <div class="title message ${this.getMessageWidth(2, 5)}"></div>
                        <div class="message ${this.getMessageWidth(4, 9)}"></div>
                    </div>
                </div>
                <div class="message-list">
                    ${map(range(randomIntFromInterval(0, 4)), () => html`
                        <div class="message-wrapper">
                            <div class="message ${this.getMessageWidth(4, 9)}"></div>
                        </div>
                    `)}
                </div>
            `))}
        `;
    }

    private getMessageWidth(first: number, second: number): string {
        let num = randomIntFromInterval(first, second);
        return MessageWidth[num];
    }
}
