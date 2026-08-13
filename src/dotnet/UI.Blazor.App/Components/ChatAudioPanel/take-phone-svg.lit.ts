// TODO: Fix ESLint errors
/* eslint-disable @typescript-eslint/no-unused-vars */
import { customElement } from 'lit/decorators.js';
import { css, html, LitElement } from 'lit';
import { AnimationSync } from 'animation-sync';

@customElement('take-phone-svg')
class TakePhoneSvg extends LitElement {
    static styles = [css`
        :host {
            display: inline-block;
            width: 1.5rem;
            height: 1.5rem;
        }
        svg {
            width: 1.5rem;
            height: 1.5rem;
        }
        @media (min-width: 820px) {
            :host {
                width: 1.25rem;
                height: 1.25rem;
            }
            svg {
                width: 1.25rem;
                height: 1.25rem;
            }
        }
        .phone {
            transform-origin: center;
            animation: shake 1s steps(10) infinite;
        }
        .arrow {
            transform-origin: center;
            animation: move-arrow 1s steps(10) infinite;
        }
        @keyframes shake {
            0%, 20%, 70%, 100% { transform: rotate(0deg); }
            30% { transform: rotate(7deg); }
            40% { transform: rotate(-7deg); }
            50% { transform: rotate(4deg); }
            60% { transform: rotate(-4deg); }
        }
        @keyframes move-arrow {
            0%, 100% { transform: translate(0, 0); }
            50% { transform: translate(1px, -1px); }
        }
    `];

    // Animated nodes live in the shadow root, where a document sweep cannot
    // reach them, and they are new elements on every re-render.
    protected updated(): void {
        AnimationSync.syncAll(this.renderRoot);
    }

    protected render(): unknown {
        return html`
            <svg
                viewBox="0 0 28 28"
                xmlns="http://www.w3.org/2000/svg"
                class="take-phone-svg">
                <g class="phone" data-anim-sync>
                    <path fill-rule="evenodd" clip-rule="evenodd" d="M4.92773 1.02081C6.08345 0.891193 7.2118 1.08959 8.17578 1.55792C8.92659 1.9229 9.31612 2.62977 9.41406 3.28253L10.0049 7.22491C10.1027 7.87758 9.93776 8.66743 9.32715 9.23663C8.49053 10.0161 7.38561 10.5571 6.14941 10.7425C5.58189 10.8276 5.01913 10.8316 4.47656 10.764C6.337 16.795 10.8949 21.6754 17.6182 23.6517C17.5791 23.2238 17.5847 22.7845 17.6387 22.3411C17.79 21.1004 18.3008 19.981 19.0566 19.1233C19.6087 18.4971 20.3936 18.3102 21.0488 18.3899L25.0059 18.8724C25.661 18.9522 26.3781 19.3224 26.7637 20.0628C27.2916 21.0771 27.5184 22.2858 27.3672 23.5267C27.2159 24.7674 26.7055 25.8864 25.9492 26.7444C25.397 27.3707 24.6121 27.5577 23.957 27.4778L23.0098 27.3616C9.09305 26.6703 0.734592 16.3168 0.734375 4.40264C0.734375 4.21148 0.772394 4.02864 0.841797 3.8626C0.910027 3.39948 1.11706 2.92992 1.51855 2.55596L1.67871 2.41338C2.49415 1.71099 3.53727 1.2239 4.69629 1.0501L4.92773 1.02081ZM20.9785 21.2034C20.6964 21.6002 20.4893 22.1034 20.4189 22.68C20.3487 23.2569 20.4289 23.7953 20.6074 24.2483L23.2666 24.5726C23.5328 24.5846 23.8014 24.5918 24.0723 24.596C24.3306 24.2103 24.5205 23.7327 24.5869 23.1878L24.6104 22.931C24.6329 22.4512 24.5493 22.0054 24.3975 21.6204L20.9785 21.2034ZM6.68359 3.96612C6.22587 3.80012 5.68583 3.73453 5.11133 3.82061C4.53668 3.90685 4.03937 4.12731 3.65039 4.42022L4.16113 7.82647C4.619 7.9926 5.15902 8.05922 5.73438 7.97296L5.98828 7.92608C6.45645 7.81983 6.86397 7.62127 7.19434 7.37237L6.68359 3.96612Z" fill="white"/>
                </g>

                <g class="arrow" data-anim-sync>
                    <path opacity="0.6" d="M25.8602 7.56787C25.8602 8.34107 25.233 8.96826 24.4598 8.96826C23.6867 8.96812 23.0604 8.34098 23.0604 7.56787V5.80518L17.8241 11.0435C17.2774 11.5903 16.3904 11.5911 15.8436 11.0444C15.2969 10.4978 15.297 9.61076 15.8436 9.06396L21.0799 3.82568L19.2928 3.82568C18.5197 3.82562 17.8934 3.19845 17.8934 2.42529C17.8934 1.65213 18.5197 1.02497 19.2928 1.0249L24.4598 1.0249C25.233 1.0249 25.8602 1.65209 25.8602 2.42529V7.56787Z" fill="white"/>
                </g>
            </svg>
        `;
    }
}
