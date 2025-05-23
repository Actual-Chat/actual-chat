import {customElement, property} from "lit/decorators.js";
import {css, html, LitElement} from "lit";

@customElement('phone-verification-cat-svg')
class PhoneVerificationCatSvg extends LitElement {
    static styles = [css`
        :host {
        }
        @keyframes move-stripes {
            0% {
                transform: translate(-75px, 0);
            }
            10% {
                transform: translate(15px, 0);
            }
            35% {
                transform: translate(-5px, 0);
            }
            50% {
                transform: translate(15px, 0);
            }
            60% {
                transform: translate(-75px, 0);
            }
            100% {
                transform: translate(-75px, 0);
            }
        }
        @keyframes move-paw {
            0%, 8% {
                transform: translate(0px, 0);
            }
            10%, 30% {
                transform: translate(10px, 10px);
            }
            35%, 45% {
                transform: translate(0, 0);
            }
            50%, 55% {
                transform: translate(10px, 10px);
            }
            60% {
                transform: translate(0, 0);
            }
            100% {
                transform: translate(0, 0);
            }
        }
        .moving-stripe {
            animation: move-stripes 2s linear infinite -1s;
        }
        .moving-paw {
            animation: move-paw 2s linear infinite -1.05s;
        }
        .hide-part {
            visibility: hidden;
        }
    `];
    protected render(): unknown {
        return html`
            <svg width="165" height="165" viewBox="0 0 165 165" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M82.5 165C128.06 165 165 128.06 165 82.5C165 36.94 128.06 0 82.5 0C36.94 0 0 36.94 0 82.5C0 128.06 36.94 165 82.5 165Z" fill="url(#paint0_linear_15147_541916)"/>
                <g clip-path="url(#clip0_15147_541916)">
                    <path d="M89.5 149L152.5 123.5L136.5 107.5L44 144L89.5 149Z" fill="var(--phone-verify-cat-secondary)"/>
                    <path d="M73.51 163.49H5.31998C1.64998 163.49 -0.0100207 160.51 1.62998 156.84L6.16998 146.65C7.80998 142.98 12.11 140 15.79 140H83.98C87.65 140 89.31 142.98 87.67 146.65L83.13 156.84C81.49 160.51 77.19 163.49 73.51 163.49Z" fill="var(--phone-verify-cat-lines)"/>
                    <path d="M73.51 159.49H5.31998C1.64998 159.49 -0.0100207 156.51 1.62998 152.84L6.16998 142.65C7.80998 138.98 12.11 136 15.79 136H83.98C87.65 136 89.31 138.98 87.67 142.65L83.13 152.84C81.49 156.51 77.19 159.49 73.51 159.49Z" fill="var(--phone-verify-cat-phone)"/>
                    <path class="moving-stripe" d="M67.8 135.65L39.47 159.43H43.47L71.8 135.65H67.8Z" fill="var(--phone-verify-cat-main)"/>
                    <path class="moving-stripe" d="M25.57 159.49H14.2L42.5299 135.71L51.9 135.49L25.57 159.49Z" fill="var(--phone-verify-cat-main)"/>
                    <path class="moving-stripe" d="M61.9999 135.609L33.6699 159.379H37.6699L65.9999 135.609H61.9999Z" fill="var(--phone-verify-cat-main)"/>
                    <path d="M6.51009 154.49C5.68009 154.49 5.31009 153.82 5.68009 152.99L10.1301 142.99C10.5001 142.16 11.4701 141.49 12.2901 141.49C13.1201 141.49 13.4901 142.16 13.1201 142.99L8.67009 152.99C8.30009 153.82 7.33009 154.49 6.51009 154.49Z" fill="var(--phone-verify-cat-lines)"/>
                    <path d="M41.8999 53.4902C51.3099 57.1402 83.6499 57.7602 105.9 45.4902C112.9 41.4902 119.54 27.0802 122.9 23.4902C77.8999 59.4902 39.4599 45.2502 34.1499 41.9102C32.8299 41.0802 29.1999 39.5802 27.4999 41.8402C24.4999 45.8402 30.1099 48.9102 41.9099 53.4902H41.8999Z" fill="var(--phone-verify-cat-main)"/>
                    <path class="hide-part" d="M147.5 115.031C147.5 115.031 146.44 114.931 144.68 115.811L142.82 109.801L138.5 95.8509L142.3 87.1909L145.63 71.4509L146.07 60.0509L145.75 54.8409C146.84 50.5909 147.5 45.9209 147.5 40.8509C147.5 21.4809 138.34 17.1309 131.84 16.4609C131.07 29.1909 122.14 48.0109 101.41 57.9909C74.41 70.9909 69.91 97.4909 69.91 97.4909C67.41 95.9909 64.91 95.4909 60.91 94.4909C60.31 96.4709 60.91 96.4909 60.91 96.4909L58.23 96.4509C50.28 96.4409 44.51 102.841 44.51 102.841C30.51 107.841 25.51 115.841 25.51 115.841C31.51 122.841 43.51 118.841 43.51 118.841L44.02 118.331C43.98 118.631 43.94 118.921 43.92 119.221C43.35 125.561 46.52 130.841 46.52 130.841C46.78 130.841 47.04 130.881 47.29 130.931L45.24 134.001C42.28 130.411 38.52 131.331 38.52 131.331C28.52 133.331 31.02 142.831 31.02 142.831H46.02L51.68 134.471C54.01 137.671 55.52 141.831 55.52 141.831H67.52L81.52 127.831C81.52 127.831 83.86 122.111 81.76 115.411L89.29 106.841L89.83 106.261L94.01 108.331L95.51 114.331L94.09 119.301L83.45 135.771C82.31 135.641 81.51 135.831 81.51 135.831C71.51 137.831 74.01 147.331 74.01 147.331H89.01L120.51 100.831C120.51 100.831 121.08 96.0709 121.03 89.8409L124.55 86.5709V86.6009C124.55 86.6009 126.51 85.2609 129.27 82.6209L129.88 85.4209C129.99 89.2609 128.5 94.8309 128.5 94.8309L137.5 124.831C146.5 128.831 153.5 123.831 153.5 123.831C153.5 123.831 156.5 116.201 147.5 115.021V115.031Z" fill="url(#paint1_linear_15147_541916)"/>
                    <path d="M89.9001 146.49H74.9001L74.6201 140.99L77.4901 137.19L82.4001 134.99L85.9701 135.99L85.4001 132.99L90.501 137L92.501 142L89.9001 146.49Z" fill="url(#paint2_linear_15147_541916)"/>
                    <path d="M125.5 16.8397C125.5 16.8397 128.87 15.9297 132.96 16.6097C132.58 16.5497 132.2 16.4997 131.84 16.4597C131.07 29.1897 122.14 48.0097 101.41 57.9897C74.4102 70.9897 70 97.4997 70 97.4997L60.9102 95.4897C60.9102 95.4897 62.5002 69.8397 97.5002 50.8397L98.9002 50.4797V49.4897C101.17 48.5497 103.79 46.6597 105.9 45.4897C112.9 41.4897 119.54 27.0797 122.9 23.4897" fill="url(#paint3_linear_15147_541916)"/>
                    <path class="moving-paw" d="M46.4499 142.06H31.4499L31.1699 136.56L34.0299 132.76L38.9499 130.56L42.5199 131.56L44.8999 131.49L47.5 130L53 134L46.4499 142.06Z" fill="var(var(--phone-verify-cat-secondary))"/>
                    <path d="M45.9 102.49C45.9 102.49 29.9 108.49 26.02 116.07C26.02 116.07 36.94 121.51 44.9 116.49C44.9 116.49 48.9 110.49 47.9 105.49" fill="url(#paint4_linear_15147_541916)"/>
                    <path d="M65 134.405C57.9286 135.852 56.24 138.78 56 140.942C68.8571 141.638 74 135.852 74 135.852C74 135.852 72.0714 132.958 65 134.405Z" fill="var(--phone-verify-cat-secondary)"/>
                    <path d="M139.09 124.49H154.09L154.37 118.99L151.51 115.19L146.59 112.99L144.001 114.5L143.001 115.5L141.001 116.5L138.001 120L139.09 124.49Z" fill="url(#paint5_linear_15147_541916)"/>
                    <path d="M111.4 60.9902C125.4 66.9902 121.4 99.9902 121.4 99.9902L89.8998 146.49H74.8998C74.8998 146.49 72.3998 136.99 82.3998 134.99C82.3998 134.99 86.3998 133.99 89.3998 137.99" stroke="var(--phone-verify-cat-lines)" stroke-width="3" stroke-miterlimit="10"/>
                    <path class="moving-paw" d="M52.9998 132.99L46.8998 141.99H31.8998C31.8998 141.99 29.3998 132.49 39.3998 130.49C39.3998 130.49 43.3998 129.49 46.3998 133.49" fill="url(#paint6_linear_15147_541916)"/>
                    <path class="moving-paw" d="M52.9998 132.99L46.8998 141.99H31.8998C31.8998 141.99 29.3998 132.49 39.3998 130.49C39.3998 130.49 43.3998 129.49 46.3998 133.49" stroke="var(--phone-verify-cat-lines)" stroke-width="3" stroke-miterlimit="10"/>
                    <path d="M84.4 134.99L96.9 115.49L94.9 107.49C94.9 107.49 74.9 101.49 80.9 73.4902" stroke="var(--phone-verify-cat-lines)" stroke-width="3" stroke-miterlimit="10"/>
                    <path d="M47.3998 110.99C45.7598 113.45 45.0198 115.99 44.7998 118.37C44.2298 124.71 47.3998 129.99 47.3998 129.99C52.3998 129.99 56.3998 140.99 56.3998 140.99H68.3998L82.3998 126.99C82.3998 126.99 88.2098 112.86 73.3998 102.99" stroke="var(--phone-verify-cat-lines)" stroke-width="3" stroke-miterlimit="10"/>
                    <path d="M62.3999 92.9902C62.3999 92.9902 66.3999 66.9902 101.4 47.9902" stroke="var(--phone-verify-cat-lines)" stroke-width="3" stroke-miterlimit="10"/>
                    <path d="M65.3999 96.9898C54.3999 91.9898 45.3999 101.99 45.3999 101.99C31.3999 106.99 26.3999 114.99 26.3999 114.99C32.3999 121.99 44.3999 117.99 44.3999 117.99" stroke="var(--phone-verify-cat-lines)" stroke-width="3" stroke-miterlimit="10"/>
                    <path d="M59.4 117.99C59.4 117.99 49.4 124.99 53.4 132.99C53.4 132.99 69.03 132.99 74.71 125.49C74.71 125.49 74.4 117.99 66.4 116.99" fill="url(#paint7_linear_15147_541916)"/>
                    <path d="M60.4 117.99C60.4 117.99 50.4 124.99 54.4 132.99C54.4 132.99 70.03 132.99 75.71 125.49C75.71 125.49 75.4 117.99 67.4 116.99" stroke="var(--phone-verify-cat-lines)" stroke-width="3" stroke-miterlimit="10"/>
                    <path d="M82.5798 114.18L90.7198 105.42" stroke="var(--phone-verify-cat-lines)" stroke-width="3" stroke-miterlimit="10"/>
                    <path d="M101.4 47.9902C116.4 42.9902 126.4 15.9902 126.4 15.9902M125.46 85.7602C125.46 85.7602 127.41 84.4202 130.17 81.7802C136.88 75.3602 148.4 61.2502 148.4 39.9902C148.4 25.5359 143.293 19.4387 138 17.0009" stroke="var(--phone-verify-cat-lines)" stroke-width="3" stroke-miterlimit="10"/>
                    <path d="M126.4 15.9902C126.4 15.9902 118.39 32.1002 88.4 42.9902C61.37 52.8102 40.4 44.4302 35.05 41.0602C33.73 40.2302 30.1 38.7302 28.4 40.9902C25.4 44.9902 30.4 48.9902 42.4 52.9902C46.64 54.4002 85.79 63.6802 112.4 39.9902" fill="url(#paint8_linear_15147_541916)"/>
                    <path d="M126.4 15.9902C126.4 15.9902 118.39 32.1002 88.4 42.9902C61.37 52.8102 40.4 44.4302 35.05 41.0602C33.73 40.2302 30.1 38.7302 28.4 40.9902C25.4 44.9902 30.4 48.9902 42.4 52.9902C46.64 54.4002 85.79 63.6802 112.4 39.9902" stroke="var(--phone-verify-cat-lines)" stroke-width="3" stroke-miterlimit="10"/>
                    <path d="M146.65 53.9805C146.65 53.9805 149.4 76.9905 139.4 94.9905L145.6 114.99" stroke="var(--phone-verify-cat-lines)" stroke-width="3" stroke-miterlimit="10"/>
                    <path d="M130.17 81.7793C131.94 84.5693 129.4 93.9893 129.4 93.9893L138.4 123.989C147.4 127.989 154.4 122.989 154.4 122.989C154.4 122.989 157.4 115.359 148.4 114.179C148.4 114.179 145.88 113.919 142.14 117.459" stroke="var(--phone-verify-cat-lines)" stroke-width="3" stroke-miterlimit="10"/>
                </g>
                <defs>
                    <linearGradient id="paint0_linear_15147_541916" x1="116" y1="4.5" x2="66.8559" y2="182.242" gradientUnits="userSpaceOnUse">
                        <stop offset="0" stop-color="var(--phone-verify-cat-gradient-0-1)"/>
                        <stop offset="1" stop-color="var(--phone-verify-cat-gradient-0-2)"/>
                    </linearGradient>
                    <linearGradient id="paint1_linear_15147_541916" x1="89.8851" y1="24.5396" x2="93.1169" y2="148.665" gradientUnits="userSpaceOnUse">
                        <stop offset="0" stop-color="var(--phone-verify-cat-gradient-1-1)"/>
                        <stop offset="1" stop-color="var(--phone-verify-cat-gradient-1-2)"/>
                    </linearGradient>
                    <linearGradient id="paint2_linear_15147_541916" x1="87.2993" y1="133.44" x2="87.2285" y2="146.9" gradientUnits="userSpaceOnUse">
                        <stop offset="0" stop-color="var(--phone-verify-cat-gradient-2-1)"/>
                        <stop offset="1" stop-color="var(--phone-verify-cat-gradient-2-2)"/>
                    </linearGradient>
                    <linearGradient id="paint3_linear_15147_541916" x1="111.5" y1="13.6568" x2="88.3169" y2="92.9477" gradientUnits="userSpaceOnUse">
                        <stop offset="0" stop-color="var(--phone-verify-cat-gradient-3-1)"/>
                        <stop offset="1" stop-color="var(--phone-verify-cat-gradient-3-2)"/>
                    </linearGradient>
                    <linearGradient id="paint4_linear_15147_541916" x1="41.6487" y1="103.028" x2="41.5667" y2="119.106" gradientUnits="userSpaceOnUse">
                        <stop offset="0" stop-color="var(--phone-verify-cat-gradient-4-1)"/>
                        <stop offset="1" stop-color="var(--phone-verify-cat-gradient-4-2)"/>
                    </linearGradient>
                    <linearGradient id="paint5_linear_15147_541916" x1="149.608" y1="113.374" x2="149.552" y2="124.839" gradientUnits="userSpaceOnUse">
                        <stop offset="0" stop-color="var(--phone-verify-cat-gradient-5-1)"/>
                        <stop offset="1" stop-color="var(--phone-verify-cat-gradient-5-2)"/>
                    </linearGradient>
                    <linearGradient id="paint6_linear_15147_541916" x1="46.7809" y1="130.766" x2="46.737" y2="142.342" gradientUnits="userSpaceOnUse">
                        <stop offset="0" stop-color="var(--phone-verify-cat-gradient-6-1)"/>
                        <stop offset="1" stop-color="var(--phone-verify-cat-gradient-6-2)"/>
                    </linearGradient>
                    <linearGradient id="paint7_linear_15147_541916" x1="68.236" y1="117.524" x2="68.1561" y2="133.475" gradientUnits="userSpaceOnUse">
                        <stop offset="0" stop-color="var(--phone-verify-cat-gradient-7-1)"/>
                        <stop offset="1" stop-color="var(--phone-verify-cat-gradient-7-2)"/>
                    </linearGradient>
                    <linearGradient id="paint8_linear_15147_541916" x1="96.9567" y1="14.66" x2="92.5554" y2="56.5912" gradientUnits="userSpaceOnUse">
                        <stop offset="0" stop-color="var(--phone-verify-cat-gradient-8-1)"/>
                        <stop offset="1" stop-color="var(--phone-verify-cat-gradient-8-2)"/>
                    </linearGradient>
                    <clipPath id="clip0_15147_541916">
                        <rect width="155.38" height="149.49" fill="var(--phone-verify-cat-main)" transform="translate(1 14)"/>
                    </clipPath>
                </defs>
            </svg>
        `;
    }
}
