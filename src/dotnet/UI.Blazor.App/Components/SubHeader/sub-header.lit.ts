import {customElement, property} from "lit/decorators.js";
import {css, html, LitElement} from "lit";

@customElement('sub-header')
class SubHeaderLit extends LitElement {
    static styles = css`
        .subheader-item {
            flex: 1;
            display: flex;
            flex-direction: row;
            align-items: center;
            column-gap: 0.5rem;
            min-height: 2.5rem;
        }
        @media (min-width: 820px) {
            .subheader-item {
                padding-left: 0.5rem;
                padding-right: 0.5rem;
            }
        }
        .c-wrapper {
            flex: 1;
            display: flex;
            flex-direction: row;
            align-items: center;
            column-gap: 0.75rem;
            transform-origin: top;
        }
        .enter .c-wrapper {
            animation: showSubHeaderMove 150ms ease-in forwards;
        }
        .exit .c-wrapper {
            animation: hideSubHeaderMove 150ms ease-in forwards;
        }
        @keyframes showSubHeaderMove {
            from {
                transform: translateY(-100%);
            }
            to {
                transform: translateY(0);
            }
        }
        @keyframes hideSubHeaderMove {
            from {
                transform: translateY(0);
            }
            to {
                transform: translateY(-100%);
            }
        }
  `;

    @property({ type: String })
    class = "";

    render() {
        return html`
            <div class="${this.class}"'>
                <div class="c-wrapper">
                    <slot></slot>
                </div>
            </div>
        `;
    }
}
