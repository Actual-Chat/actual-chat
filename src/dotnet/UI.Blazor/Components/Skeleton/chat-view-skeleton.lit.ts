import { customElement, property } from 'lit/decorators.js';
import { css, html, LitElement } from 'lit';
import { messageStyles } from './styles.lit';
import { guard } from 'lit/directives/guard.js';
import { map } from 'lit/directives/map.js';
import { range } from 'lit/directives/range.js';
import { MessageWidth, randomIntFromInterval } from './helpers';

@customElement('chat-view-skeleton')
class ChatViewSkeleton extends LitElement {
    static styles = [
        messageStyles, css`
            :host {
                width: 100%;
                scrollbar-width: none;
                display: flex;
                flex-direction: column;
            }

            :host::-webkit-scrollbar {
                display: none;
            }

            :host(.animated-skeleton) {
                animation: pulse 2s infinite;
            }
        `];
    private observer: IntersectionObserver;

    @property()
    public count = 2;

    connectedCallback() {
        super.connectedCallback();

        this.observer = new IntersectionObserver(entries => {
            const isVisible = entries.some(e => e.isIntersecting);
            // console.warn('isVisible', isVisible, entries);
            if (isVisible)
                this.classList.add('animated-skeleton');
            else
                this.classList.remove('animated-skeleton');
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
