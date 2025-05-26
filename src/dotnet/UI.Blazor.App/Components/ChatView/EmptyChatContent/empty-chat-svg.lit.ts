import {customElement, property} from "lit/decorators.js";
import {css, html, LitElement} from "lit";

@customElement('empty-chat-svg')
class EmptyChatSvg extends LitElement {
    static styles = [css`
        :host {
        }
        @keyframes move-right-ear {
            0%, 44%, 46%, 54%, 56%, 100% {
                transform: rotate(0) translate(0, 0);
            }
            45%, 55% {
                transform: rotate(1deg) translate(2px, 1px)
            }
        }
        @keyframes move-left-paw {
            0%, 43%, 47%, 53%, 57%, 100% {
                transform: rotate(0) translate(0, 0);
            }
            45%, 55% {
                transform: rotate(-0.5deg) translate(1px, 1px);
            }
        }
        @keyframes move-fly {
            0%, 100% {
                transform: translate(0, 0);
                opacity: 0;
            }
            20% {
                transform: translate(-10px, 30px);
                opacity: 1;
            }
            30% {
                transform: translate(-20px, 35px);
                opacity: 1;
            }
            40%, 44% {
                transform: translate(-25px, 40px);
                opacity: 1;
            }
            45% {
                transform: translate(-20px, 35px);
                opacity: 1;
            }
            48% {
                transform: translate(-22px, 47px);
                opacity: 1;
            }
            50%, 54% {
                transform: translate(-25px, 40px);
                opacity: 1;
            }
            55% {
                transform: translate(-20px, 35px);
                opacity: 1;
            }
            57% {
                transform: translate(-25px, 40px);
                opacity: 1;
            }
            60% {
                transform: translate(-20px, 50px);
                opacity: 1;
            }
            75% {
                transform: translate(-15px, 40px);
                opacity: 1;
            }
            85% {
                transform: translate(-30px, 20px);
                opacity: 1;
            }

        }
        .left-paw {
            animation: move-left-paw 5s ease-in-out 2.5s infinite;
        }
        .right-ear {
            animation: move-right-ear 5s ease-in-out infinite;
        }
        .fly {
            animation: move-fly 5s ease-in-out infinite;
        }
    `];
    protected render(): unknown {
        return html`
            <svg width="187" height="185" viewBox="0 0 187 185" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M82.5582 178.953C128.154 178.953 165.116 141.991 165.116 96.3951C165.116 50.7995 128.154 13.8369 82.5582 13.8369C36.9625 13.8369 0 50.7995 0 96.3951C0 141.991 36.9625 178.953 82.5582 178.953Z" fill="url(#paint0_linear_15147_541749)"/>
                <path d="M81 149.5L97.5 140.5L106.5 148H150.5L156.5 153V159.5L150.5 165L135.5 166.5L128.5 171.5L130 179.5L134 183L142.5 184L158.5 182L171.5 171.5L175 159.5L173 146L163.5 134L151 130L138.5 129L144 124V116L138.5 104L127.5 94.5L115.5 90.5L113.5 89L112.5 80L108 66L103 60L98.5 57L97.5 48L94 40L97.5 37.5L100 32.5L95.5 30L88.5 28.5L83 30L69.5 27L56 31L50.5 36.5L45 39L39.5 46L43 49L44 62L38 76.5L39.5 89L41.5 96L32.5 104L27 116L28.5 132.5L38 146L54.5 154H69.5L81 149.5Z" fill="url(#paint1_linear_15147_541749)"/>
                <path d="M78.4512 65.4133L85.638 61.9709L82.5579 55.2672L82.0748 45.1814C82.0748 45.1814 80.7461 38.1154 74.7672 39.9876C68.7882 41.7994 71.2643 49.5298 71.2643 49.5298L74.586 55.6899C74.586 55.7503 78.7531 62.0313 78.4512 65.4133Z" fill="var(--empty-chat-cat-body)"/>
                <path d="M81.4713 101.227L87.3899 108.112L90.5304 107.388L92.5838 93.4971L78.4517 98.8721L81.4713 101.227Z" fill="var(--empty-chat-cat-body)"/>
                <path d="M56.7093 75.438L63.4734 77.4913L72.2909 78.2765L80.5648 76.2231L88.5368 71.15L93.4287 65.171L95.6633 60.2791C95.6633 60.2791 94.3346 57.7426 90.4694 57.2595C84.5508 56.5951 75.7333 59.3732 71.5662 63.2988L62.7487 64.9898L59.1855 65.171L55.0183 69.9421L56.7093 75.438Z" fill="var(--empty-chat-cat-body)"/>
                <path d="M73.0161 126.169C73.0161 126.169 74.5863 135.711 83.1622 134.624C91.7382 133.537 92.2817 123.814 92.2817 123.814L91.8589 119.828C91.8589 119.828 90.4699 108.836 80.6257 111.494C71.9894 113.728 73.3181 121.156 73.0161 126.169Z" fill="var(--empty-chat-cat-body)"/>
                <path d="M94.1542 121.157C94.1542 121.157 95.4828 129.249 103.334 129.612C112.272 129.974 111.608 118.801 111.608 118.801L110.34 115.963C110.34 115.963 109.857 105.454 99.7104 107.085C95.362 107.749 89.7454 111.192 94.1542 121.157Z" fill="var(--empty-chat-cat-body)"/>
                <path d="M111.849 90.5366C111.849 90.5366 105.508 87.6981 99.6497 89.1476" stroke="var(--empty-chat-cat-lines-2)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M112.212 84.8603C112.212 84.8603 102.549 80.3912 95.0598 84.8603" stroke="var(--empty-chat-cat-lines-2)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M111.607 78.458C111.607 78.458 104.179 74.2305 96.1462 78.458" stroke="var(--empty-chat-cat-lines-2)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M42.1548 175.027L46.1407 175.873L59.8501 172.612L89.0203 162.345L103.575 155.037L106.655 151.112L100.314 142.415L96.7506 139.637L78.3306 149.541L67.8825 153.225L59.4274 153.89L52.7237 155.52L46.1407 158.902L37.9876 165.304L37.1421 170.317L42.1548 175.027Z" fill="var(--empty-chat-cat-body)"/>
                <path d="M138.724 129.008L143.253 123.693H164.089L174.477 123.995L182.69 126.834L184.804 132.088L182.268 136.255L176.41 138.188H167.592L163.485 134.141L157.325 130.941L151.225 129.672L138.724 129.008Z" fill="var(--empty-chat-cat-body)"/>
                <path d="M80.8669 99.4149C88.6385 99.4149 94.9386 93.1148 94.9386 85.3432C94.9386 77.5716 88.6385 71.2715 80.8669 71.2715C73.0953 71.2715 66.7952 77.5716 66.7952 85.3432C66.7952 93.1148 73.0953 99.4149 80.8669 99.4149Z" fill="var(--empty-chat-cat-body)"/>
                <path d="M100.797 57.9238C109.132 66.4997 113.54 78.2765 111.91 90.5364" stroke="var(--empty-chat-cat-lines-1)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M37.2623 145.073L11.595 163.735" stroke="var(--empty-chat-cat-lines-2)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M51.8784 103.038C51.8784 103.038 56.2268 93.0734 64.5007 94.8852" stroke="var(--empty-chat-cat-lines-2)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M71.8684 36.4248C76.7603 40.3504 78.3305 49.5906 78.4513 54.7845" stroke="var(--empty-chat-cat-lines-2)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M74.5867 56.4154C74.5867 56.4154 72.9561 50.0137 69.0305 46.9336" stroke="var(--empty-chat-cat-lines-2)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M82.1959 55.026C82.1959 55.026 83.585 48.5639 81.4712 44.0947" stroke="var(--empty-chat-cat-lines-2)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M45.4768 101.711C45.4768 101.711 49.5232 89.1489 60.6356 90.5379" stroke="var(--empty-chat-cat-lines-2)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M40.5842 94.8854C40.5842 94.8854 43.7247 84.3165 55.0183 84.9204" stroke="var(--empty-chat-cat-lines-2)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M26.2712 119.768C26.2712 119.768 36.1154 132.329 55.079 122.244" stroke="var(--empty-chat-cat-lines-2)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M116.56 107.628C116.56 107.628 126.344 109.198 133.893 100.019" stroke="var(--empty-chat-cat-lines-2)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M121.029 113.608C121.029 113.608 130.813 115.178 138.362 105.998" stroke="var(--empty-chat-cat-lines-2)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M123.867 119.647C123.867 119.647 133.651 121.217 141.2 112.037" stroke="var(--empty-chat-cat-lines-2)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M27.1169 127.98C27.1169 127.98 36.9611 140.542 55.9247 130.457" stroke="var(--empty-chat-cat-lines-2)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M29.9551 136.014C29.9551 136.014 39.7993 148.576 58.7629 138.49" stroke="var(--empty-chat-cat-lines-2)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M48.9186 69.6409C48.9186 69.6409 48.4958 62.454 55.0787 60.8838" stroke="var(--empty-chat-cat-lines-2)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M40.5845 94.8857C31.8879 100.804 26.2712 110.407 26.2712 121.278C26.2712 139.275 41.7924 153.89 60.9372 153.89C69.6943 153.89 77.7267 150.81 83.8264 145.798" stroke="var(--empty-chat-cat-lines-1)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M112.031 89.1475L111.064 90.8989C108.467 96.0927 107.32 102.011 107.984 108.111C108.467 112.701 109.736 114.211 111.245 118.318C112.333 121.277 112.453 129.732 103.153 129.732C100.737 129.732 96.0866 127.377 94.7579 123.995L93.5501 121.639C87.9939 111.554 88.5978 99.5956 94.2144 89.8722" stroke="var(--empty-chat-cat-lines-1)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M111.608 89.8125C129.364 91.2619 143.254 105.273 143.254 122.304V123.754H169.525C169.525 123.754 184.865 123.331 184.865 131.243C184.865 138.49 174.9 138.248 174.9 138.248" stroke="var(--empty-chat-cat-lines-1)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M104.119 147.186H125.076H138.604H146.938C151.89 147.186 155.876 151.233 155.937 156.185C155.997 160.956 152.071 164.821 146.998 165.123H140.355L136.309 165.183C131.236 165.485 127.31 169.23 127.37 174.001C127.431 178.953 131.417 183 136.369 183L143.194 182.939C163.667 182.939 173.27 170.86 173.27 155.943C173.27 141.086 164.513 128.947 143.194 128.947H107.078" stroke="var(--empty-chat-cat-lines-1)" stroke-width="3" stroke-miterlimit="10" stroke-linejoin="round"/>
                <path d="M71.1436 94.8868C83.2223 99.7183 91.8586 110.77 92.2814 123.815V124.963C92.2814 130.338 87.933 134.626 82.6184 134.626C77.2434 134.626 72.9554 130.278 72.9554 124.963C72.9554 124.963 71.8079 117.172 70.6604 114.756C65.6478 104.489 45.4763 101.772 45.4763 101.772C35.8737 92.1691 33.9411 73.5075 43.6041 61.0664C43.6041 61.0664 53.6898 47.8402 69.513 52.7924" stroke="var(--empty-chat-cat-lines-1)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path class="left-paw" d="M89.3224 133.96C89.3224 133.96 107.018 145.978 105.991 152.018C105.025 157.755 76.7001 166.875 63.4739 171.344C55.6831 174.001 42.5173 178.229 38.4105 172.672C32.9147 165.304 45.8993 158.117 57.0118 153.709" stroke="var(--empty-chat-cat-lines-1)" stroke-width="3" stroke-miterlimit="10" stroke-linejoin="round"/>
                <path d="M98.2603 128.042C98.2603 128.042 102.488 126.109 100.797 119.768" stroke="var(--empty-chat-cat-lines-1)" stroke-width="3" stroke-miterlimit="10" stroke-linejoin="round"/>
                <path d="M104.119 129.672C104.119 129.672 108.226 125.505 105.931 118.62" stroke="var(--empty-chat-cat-lines-1)" stroke-width="3" stroke-miterlimit="10" stroke-linejoin="round"/>
                <path d="M78.7537 133.779C78.7537 133.779 83.3436 131.363 81.0486 124.479" stroke="var(--empty-chat-cat-lines-1)" stroke-width="3" stroke-miterlimit="10" stroke-linejoin="round"/>
                <path d="M84.8533 134.081C84.8533 134.081 88.96 129.914 86.6651 123.029" stroke="var(--empty-chat-cat-lines-1)" stroke-width="3" stroke-miterlimit="10" stroke-linejoin="round"/>
                <path d="M75.7339 27.6069V2" stroke="var(--empty-chat-cat-lines-2)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path class="right-ear" d="M81.7131 29.4787C87.0278 26.6402 93.7919 27.4857 98.261 31.9549C98.261 31.9549 97.5363 37.1487 92.584 38.417" fill="var(--empty-chat-cat-lines-2)"/>
                <path class="right-ear" d="M81.7131 29.4787C87.0278 26.6402 93.7919 27.4857 98.261 31.9549C98.261 31.9549 97.5363 37.1487 92.584 38.417" stroke="var(--empty-chat-cat-lines-1)" stroke-width="3" stroke-miterlimit="10" stroke-linejoin="round"/>
                <path d="M69.574 78.4584C84.7169 78.4584 96.9927 66.9397 96.9927 52.7306C96.9927 38.5216 84.7169 27.0029 69.574 27.0029C54.4311 27.0029 42.1553 38.5216 42.1553 52.7306C42.1553 66.9397 54.4311 78.4584 69.574 78.4584Z" stroke="var(--empty-chat-cat-lines-1)" stroke-width="3" stroke-miterlimit="10" stroke-linejoin="round"/>
                <path d="M76.7 74.2309C78.1494 70.7281 81.5918 68.252 85.6382 68.252C87.3896 68.252 89.0203 68.7351 90.4697 69.5202" stroke="var(--empty-chat-cat-lines-1)" stroke-width="3" stroke-miterlimit="10" stroke-linejoin="round"/>
                <path d="M72.8351 60.8232C70.1777 64.3865 65.467 65.5943 61.481 64.0241C61.481 64.0241 51.6369 67.5873 57.4951 75.8009" stroke="var(--empty-chat-cat-lines-1)" stroke-width="3" stroke-miterlimit="10" stroke-linejoin="round"/>
                <path d="M96.9314 47.9596C96.1463 47.2952 95.482 46.9329 95.482 46.9329C90.4693 45.1211 85.8794 48.2011 83.1013 52.7307C81.0479 56.0523 85.8794 61.7897 85.8794 61.7897C83.7052 64.2659 80.3231 65.5341 76.8203 64.8698" stroke="var(--empty-chat-cat-lines-1)" stroke-width="3" stroke-miterlimit="10" stroke-linejoin="round"/>
                <path d="M83.4042 69.641L81.7131 64.3867" stroke="var(--empty-chat-cat-lines-1)" stroke-width="3" stroke-miterlimit="10" stroke-linejoin="round"/>
                <path class="left-paw" d="M48.4361 175.27C48.4361 175.27 47.5905 167.902 56.1061 165.305" stroke="var(--empty-chat-cat-lines-1)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path class="left-paw" d="M40.5842 174.485C40.5842 174.485 40.6446 167.6 46.0801 165.305" stroke="var(--empty-chat-cat-lines-1)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M178.826 137.765C178.826 137.765 182.51 129.491 166.868 130.699" stroke="var(--empty-chat-cat-lines-1)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M107.5356 140.241C97.5356 140.241 130.813 139.154 143.495 139.819C147.481 140 157.567 141.026 158.412 150.267" stroke="var(--empty-chat-cat-tail-line)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M133.975 177.203C127.975 177.203 160.466 180.766 165.721 167.781" stroke="var(--empty-chat-cat-tail-line)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M50.0063 44.8794L55.7437 38.055C55.7437 38.055 52.0597 35.6996 45.7788 38.8401C39.4978 42.0409 40.4641 42.5845 40.4641 42.5845C40.4641 42.5845 42.5175 45.0606 45.5372 45.9061C48.5569 46.7516 50.0063 44.8794 50.0063 44.8794Z" fill="var(--empty-chat-cat-lines-2)"/>
                <path d="M47.6508 50.7978C47.6508 50.7978 49.2211 50.3147 51.1537 48.2613C53.0259 46.3287 53.1467 43.6714 53.1467 43.6714C53.1467 43.6714 50.5497 40.5913 45.0539 42.5239C38.4106 44.8188 39.3165 45.604 39.3165 45.604C39.3165 45.604 40.5244 49.7711 43.5441 50.4959C46.2618 51.0998 47.6508 50.7978 47.6508 50.7978Z" fill="var(--empty-chat-cat-body)"/>
                <path d="M56.7095 37.0285C49.3415 34.7939 41.6111 38.9007 39.3162 46.2688C39.3162 46.2688 42.3962 50.5567 47.3485 49.3488" stroke="var(--empty-chat-cat-lines-1)" stroke-width="3" stroke-miterlimit="10" stroke-linejoin="round"/>
                <path d="M39.3162 46.2687C39.3162 46.2687 46.3218 39.2631 53.7503 42.3431" stroke="var(--empty-chat-cat-lines-1)" stroke-width="3" stroke-miterlimit="10" stroke-linejoin="round"/>
                <circle class="fly" cx="121.82" cy="-5" r="1.5" fill="var(--empty-chat-cat-lines-1)"></circle>
                <defs>
                    <linearGradient id="paint0_linear_15147_541749" x1="82.7382" y1="24.0296" x2="86.741" y2="180.64" gradientUnits="userSpaceOnUse">
                        <stop offset="0" stop-color="var(--empty-chat-cat-round-gradient-1)"/>
                        <stop offset="1" stop-color="var(--empty-chat-cat-round-gradient-2)"/>
                    </linearGradient>
                    <linearGradient id="paint1_linear_15147_541749" x1="130.917" y1="21.7745" x2="88.2873" y2="176.567" gradientUnits="userSpaceOnUse">
                        <stop offset="0" stop-color="var(--empty-chat-cat-gradient-1)"/>
                        <stop offset="1" stop-color="var(--empty-chat-cat-gradient-2)"/>
                    </linearGradient>
                </defs>
            </svg>
        `;
    }
}
