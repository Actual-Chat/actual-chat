import { customElement } from 'lit/decorators.js';
import {css, html, LitElement} from "lit";

@customElement('no-search-results-svg')
class NoSearchResultsSvg extends LitElement {
    static styles = [css`
        :host {
        }
        @keyframes move-flower-1 {
            0%, 100% {
                transform: rotate(0) translate(0, 0);
            }
            50% {
                transform: rotate(3deg) translate(10px, -7px);
            }
        }
        @keyframes move-flower-2 {
            0%, 100% {
                transform: translate(0, 0);
            }
            50% {
                transform: translate(3px, -1px);
            }
        }
        @keyframes star-pulse {
            0%, 40%, 60%, 100% {
                transform: scale(1);
            }
            50% {
                transform: scale(1.25);
            }
        }
        @keyframes move-dash {
            to {
                stroke-dashoffset: 0;
            }
        }
        @keyframes move-dash-reverse {
            to {
                stroke-dashoffset: 32;
            }
        }
        .moving-line {
            stroke-dasharray: 8 8;
            stroke-dashoffset: 16;
            animation: move-dash 2s linear infinite;
        }
        .moving-line.reverse {
            animation: move-dash-reverse 2s linear infinite;
        }
        .moving-flower-1 {
            animation: move-flower-1 3s ease-in-out infinite;
        }
        .moving-flower-2 {
            animation: move-flower-2 3s ease-in-out infinite;
        }
        .moving-flower-3 {
            animation: move-flower-2 3s ease-in-out infinite;
        }
        .moving-flower-4 {
            animation: move-flower-2 3s ease-in-out infinite;
        }
        .medal-star {
            transform-box: fill-box;
            transform-origin: center;
            animation: star-pulse 1.5s ease-in-out infinite;
        }
    `];
    protected render(): unknown {
        return html`
            <svg width="198" height="234" viewBox="0 0 198 234" fill="none" xmlns="http://www.w3.org/2000/svg">
                <g clip-path="url(#clip0_20364_776232)">
                    <path d="M114.82 233.815C160.38 233.815 197.32 196.875 197.32 151.315C197.32 105.755 160.38 68.8154 114.82 68.8154C69.2601 68.8154 32.3201 105.755 32.3201 151.315C32.3201 196.875 69.2601 233.815 114.82 233.815Z" fill="url(#no-search-results-gradient-1)"/>
                    <defs>
                        <linearGradient id="no-search-results-gradient-1" x1="77.5" y1="8" x2="82.8201" y2="165.814" gradientUnits="userSpaceOnUse">
                            <stop offset="0" stop-color="var(--no-results-cat-circle-gradient-1)" stop-opacity="0"/>
                            <stop offset="1" stop-color="var(--no-results-cat-circle-gradient-2)"/>
                        </linearGradient>
                    </defs>
                    <g clip-path="url(#clip1_20364_776232)">
                        <path d="M47.934 23.6643L46.749 35.2766L41.0047 40.9338L37.218 54.4119L40.803 73.0773L30.3208 78.9614L21.9749 86.4845L13.0559 88.6529L7.44694 93.8287C5.82253 94.0648 2.93818 96.1975 4.39603 102.839C5.85388 109.481 10.841 110.709 13.1523 110.493L14.6605 125.459L22.7452 152.14L30.0138 165.089L38.5591 171.644L50.0362 173.311L61.4216 169.758L73.6023 159.677L88.5635 141.548L96.3364 128.67C96.3364 128.67 98.0326 125.808 99.1764 118.561C100.32 111.314 99.2635 107.16 99.2635 107.16L94.8234 97.0835L90.8052 89.2029L98.6101 83.6054L105.766 71.0739L106.757 62.0036L105.957 51.911L109.5 45L112.5 37.5L113.665 24.4734L102.67 22.9422L94.789 26.9604L89.9318 31.3087L72.6027 26.4401L71.381 14.1516L64.6419 12.2583L60.25 13.1018L55.7986 16.006L47.934 23.6643Z" fill="url(#paint0_linear_20364_776416)"/>
                        <path d="M101.82 132.313L91.8201 133.313L93.8201 106.314L102.82 108.314L111.32 115.814C112.32 117.648 114.02 122.314 112.82 126.314C111.62 130.314 104.987 131.98 101.82 132.313Z" fill="url(#paint0_linear_20364_776416)"/>
                        <path d="M53.3201 30.3145L62.8201 26.8145C64.2321 19.9401 69.2042 15.8665 71.56 13.6832C71.56 13.6832 54.8201 7.68262 53.3201 30.3145Z" fill="var(--no-results-cat-ears)"/>
                        <path d="M89.7668 87.3538C94.8436 93.1892 98.1934 100.07 99.3336 107.253" stroke="var(--no-results-cat-main-stroke)" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
                        <path d="M96.1652 35.0024L104.059 45.6654C104.498 36.3498 110.041 27.4749 112.432 25.166C112.432 25.166 100.998 22.4505 96.1652 35.0024Z" fill="#A0A0A0"/>
                        <path d="M70.8623 14.3169C70.8623 14.3169 60.6202 9.98025 52.5804 18.2176C48.7244 22.1683 46.4221 27.7665 47.5061 34.8145" stroke="var(--no-results-cat-main-stroke)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round"/>
                        <path d="M95.1319 34.9829L104.379 46.7938C104.731 36.5956 110.955 26.9639 113.665 24.4736C113.665 24.4736 100.481 21.3114 95.1319 34.9829Z" fill="var(--no-results-cat-ears)"/>
                        <path d="M42.7893 75.8793C38.6782 68.4401 35.5837 59.1571 38.0045 50.5407C43.0408 32.6147 62.3957 22.4717 81.1111 27.7298C99.8265 32.9879 111.035 51.8423 106.031 69.6527C104.462 75.2365 101.482 80.1851 97.49 84.0804" stroke="var(--no-results-cat-main-stroke)" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
                        <circle cx="88.1805" cy="7.96563" r="6" transform="rotate(15.6928 88.1805 7.96563)" fill="url(#paint0_linear_20364_776232)" fill-opacity="0.5"/>
                        <path d="M83.9129 85.7082C89.0767 81.8557 92.2951 76.7794 94.1615 71.4487" stroke="var(--no-results-cat-body-center)" stroke-width="8" stroke-linecap="round"/>
                        <path d="M105.5 49.9996C117.5 38 112 23.4993 112 23.4993C104.943 21.5167 98.6521 27.7028 96.4288 35.6164" stroke="var(--no-results-cat-main-stroke)" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
                        <path d="M78.5726 73.8749C92.6413 77.8276 105.831 74.6785 108.033 66.8412C110.235 59.004 100.615 49.4464 86.5463 45.4938C72.4775 41.5412 59.2876 44.6903 57.0857 52.5275C54.8838 60.3647 64.5038 69.9223 78.5726 73.8749Z" fill="var(--no-results-cat-mask)"/>
                        <path d="M48.8201 36.3145L51.8201 37.3135L54.3201 25.3145L48.8201 31.8145V36.3145Z" fill="var(--no-results-cat-mask)"/>
                        <path d="M85.8058 14.5694C89.2619 15.5404 92.8507 13.5259 93.8217 10.0698C94.7926 6.6138 92.7781 3.02499 89.3221 2.05401C85.866 1.08303 82.2772 3.09757 81.3062 6.55361C80.3352 10.0097 82.3498 13.5985 85.8058 14.5694Z" stroke="var(--no-results-cat-main-stroke)" stroke-width="3" stroke-miterlimit="10"/>
                        <path d="M38.1158 82.0136C38.1158 82.0136 60.9058 99.4996 95.094 96.1209C97.0415 96.0656 89.6317 87.8345 89.6317 87.8345C68.289 91.7061 42.7797 75.8764 42.7797 75.8764" stroke="var(--no-results-cat-main-stroke)" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
                        <path d="M90.0275 60.7756L75.8035 56.7793L74.4511 61.5929L88.6751 65.5892L90.0275 60.7756Z" fill="var(--no-results-cat-eyes)"/>
                        <path d="M104.252 64.7716L90.0276 60.7754L88.6752 65.589L102.899 69.5853L104.252 64.7716Z" fill="var(--no-results-cat-eyes)"/>
                        <ellipse cx="78.7408" cy="128.32" rx="11.0582" ry="12.2539" transform="rotate(137.887 78.7408 128.32)" fill="var(--no-results-cat-paws)"/>
                        <ellipse cx="36.5743" cy="112.21" rx="11.0582" ry="12.2539" transform="rotate(66.4215 36.5743 112.21)" fill="var(--no-results-cat-paws)"/>
                        <path d="M13.9351 109.851C13.9351 109.851 23.352 111.271 25.5357 119.914C25.5357 119.914 34.3107 125.579 43.7484 119.567C52.1505 114.21 48.003 104.424 48.003 104.424C48.003 104.424 45.1687 89.6671 25.5093 86.512C18.5351 85.7884 13.1208 88.4222 9.76733 91.1155C4.8004 95.1046 3.28735 101.24 5.15314 103.842M40.5137 73.516C33.1408 75.8536 26.6978 79.9827 21.9833 85.5214" stroke="var(--no-results-cat-main-stroke)" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
                        <path d="M33.4201 122.057C33.4201 122.057 34.7334 117.42 29.8641 112.79" stroke="var(--no-results-cat-main-stroke)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round"/>
                        <path d="M40.0189 120.494C40.0189 120.494 40.8966 114.56 36.463 111.227" stroke="var(--no-results-cat-main-stroke)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round"/>
                        <path d="M45.3994 118.275C45.3994 118.275 46.7452 113.522 41.8435 109.008" stroke="var(--no-results-cat-main-stroke)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round"/>
                        <path d="M112 24C112 24 101.489 18.3827 91.859 29.1078" stroke="var(--no-results-cat-main-stroke)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round"/>
                        <path d="M85.8058 14.5693L82.1544 27.5661" stroke="var(--no-results-cat-main-stroke)" stroke-width="3"/>
                        <path d="M92.5672 136.338C92.9965 135.63 92.7702 134.707 92.0618 134.278C91.3533 133.849 90.4309 134.075 90.0015 134.783L92.5672 136.338ZM12.4824 111.334C12.8963 118.299 14.7607 132.199 19.1453 145.128C21.338 151.594 24.1835 157.885 27.8418 162.955C31.4987 168.024 36.0449 171.977 41.6582 173.554L42.4696 170.666C37.7336 169.335 33.6967 165.943 30.2747 161.2C26.8543 156.459 24.1254 150.472 21.9864 144.165C17.7076 131.548 15.8792 117.923 15.4771 111.156L12.4824 111.334ZM41.6582 173.554C52.7783 176.678 63.1969 171.086 71.8237 163.109C80.4891 155.095 87.7068 144.358 92.5672 136.338L90.0015 134.783C85.1796 142.74 78.1364 153.185 69.7869 160.906C61.3986 168.663 52.0481 173.357 42.4696 170.666L41.6582 173.554Z" fill="var(--no-results-cat-main-stroke)"/>
                        <path d="M14.7637 117.697C18.8415 123.517 32.8331 134.719 60.6765 140.984" stroke="var(--no-results-cat-main-stroke)" stroke-width="3" stroke-linecap="round"/>
                        <path d="M74.8309 142.364C77.8543 142.694 83.0737 142.602 86.6381 141.007" stroke="var(--no-results-cat-main-stroke)" stroke-width="3" stroke-linecap="round"/>
                        <path d="M56.5758 161.125C62.8929 153.748 67.6178 146.974 71.102 140.814" stroke="var(--no-results-cat-body-center)" stroke-width="8" stroke-linecap="round"/>
                        <path d="M58.8103 114.907C64.4623 116.495 70.3314 113.201 71.9193 107.549C73.5072 101.897 70.2127 96.0276 64.5607 94.4396C58.9088 92.8517 53.0397 96.1463 51.4517 101.798C49.8638 107.45 53.1584 113.319 58.8103 114.907Z" fill="var(--no-results-cat-medal)" stroke="var(--no-results-cat-main-stroke)" stroke-width="3" stroke-miterlimit="10"/>
                        <ellipse cx="85.5417" cy="4.65623" rx="2.39081" ry="2.64932" transform="rotate(66.4215 85.5417 4.65623)" fill="var(--no-results-cat-eyes)"/>
                        <path class="medal-star" fill-rule="evenodd" clip-rule="evenodd" d="M70.0298 105.747C66.8246 104.809 64.9531 101.49 65.8242 98.2847C64.8985 101.475 61.5726 103.333 58.3477 102.465C61.553 103.403 63.4245 106.721 62.5533 109.927C63.479 106.737 66.8049 104.879 70.0298 105.747Z" fill="var(--no-results-cat-medal-stars)"/>
                        <path fill-rule="evenodd" clip-rule="evenodd" d="M54.2039 106.719C56.0428 107.236 57.9516 106.178 58.493 104.353C58.0047 106.193 59.0834 108.09 60.9222 108.607C59.0834 108.09 57.1745 109.148 56.6331 110.973C57.1215 109.133 56.0428 107.236 54.2039 106.719M54.2039 106.719L54.2036 106.719L54.2039 106.719Z" fill="var(--no-results-cat-medal-stars)"/>
                        <circle cx="107.82" cy="187.314" r="1.5" fill="var(--no-results-cat-ground-stroke)"/>
                        <path d="M72.5669 25.6496C72.877 19.3872 70.8649 14.3081 70.8649 14.3081C63.8081 12.3255 56.4909 17.2395 53.7861 26.8668C52.8088 29.516 51.1474 35.7145 52.3202 39.3145" stroke="var(--no-results-cat-main-stroke)" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
                    </g>
                    <defs>
                        <linearGradient id="paint0_linear_20364_776416" x1="113" y1="131" x2="34" y2="36.5" gradientUnits="userSpaceOnUse">
                            <stop offset="0" stop-color="var(--no-results-cat-body-gradient-1)"/>
                            <stop offset="1" stop-color="var(--no-results-cat-body-gradient-2)"/>
                        </linearGradient>
                        <clipPath id="clip0_20364_776416">
                            <rect width="165" height="187.78" fill="white" transform="translate(11.7905 -27) rotate(15.6928)"/>
                        </clipPath>
                    </defs>
                    <path d="M56.3202 199.815C68.4695 199.815 78.3202 196.681 78.3202 192.815C78.3202 188.95 68.4695 185.815 56.3202 185.815C44.1708 185.815 34.3202 188.95 34.3202 192.815C34.3202 196.681 44.1708 199.815 56.3202 199.815Z" fill="var(--no-results-cat-shadow)"/>
                    <path d="M101.404 131.984C101.404 131.984 91.9539 130.804 87.5139 138.534C87.5139 138.534 77.5339 141.614 70.0739 133.274C63.4339 125.844 70.0739 117.544 70.0739 117.544C70.0739 117.544 76.7939 104.104 96.5739 106.384C103.484 107.574 107.984 111.574 110.484 115.074C114.187 120.258 113.984 126.574 111.484 128.574" stroke="var(--no-results-cat-main-stroke)" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
                    <path d="M79.3438 138.465C79.3438 138.465 79.3338 133.645 85.2738 130.505" stroke="var(--no-results-cat-main-stroke)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round"/>
                    <path d="M73.4138 135.175C73.4138 135.175 74.1738 129.225 79.3438 127.215" stroke="var(--no-results-cat-main-stroke)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round"/>
                    <path d="M68.8338 131.584C68.8338 131.584 68.8238 126.644 74.7638 123.624" stroke="var(--no-results-cat-main-stroke)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round"/>
                    <path class="moving-line reverse" d="M78.3201 198.314L75.8201 62.3145" stroke="var(--no-results-cat-body-center)" stroke-width="3" stroke-linecap="round" stroke-dasharray="16 8"/>
                    <path class="moving-line reverse" d="M203.217 198.504L106.32 67.8145" stroke="var(--no-results-cat-body-center)" stroke-width="3" stroke-linecap="round" stroke-dasharray="16 8"/>
                    <ellipse xmlns="http://www.w3.org/2000/svg" cx="137.5" cy="199.5" rx="60.5" ry="27.5" fill="var(--no-results-cat-light-circle)"/>
                    <path class="moving-flower-4" d="M119.846 147.031C123.759 147.775 131.555 151.236 131.44 159.124C127.917 160.16 120.666 159.191 119.846 147.031Z" fill="var(--no-results-cat-flower-leaf)" stroke="var(--no-results-cat-main-stroke)" stroke-width="3"/>
                    <path class="moving-flower-1" d="M123.198 155.363C122.396 155.154 121.577 155.635 121.368 156.437C121.16 157.239 121.641 158.058 122.443 158.266L123.198 155.363ZM142.795 198.588C143.908 192.594 144.301 183.272 141.823 174.722C139.333 166.128 133.869 158.137 123.198 155.363L122.443 158.266C131.771 160.692 136.641 167.618 138.942 175.557C141.256 183.54 140.899 192.369 139.845 198.041L142.795 198.588Z" fill="var(--no-results-cat-main-stroke)"/>
                    <path d="M114.32 179.976C116.878 187.155 125.83 200.919 141.175 198.542C142.198 191.611 138.26 178.194 114.32 179.976Z" fill="var(--no-results-cat-flower)" stroke="var(--no-results-cat-main-stroke)" stroke-width="3"/>
                    <path d="M153.576 181.323C153.667 186.134 151.454 196.255 141.882 198.254C139.737 194.384 139.073 185.58 153.576 181.323Z" fill="var(--no-results-cat-flower)" stroke="var(--no-results-cat-main-stroke)" stroke-width="3"/>
                    <path class="moving-flower-3" d="M117.342 157.486C119.581 160.78 125.857 166.556 133.049 163.313C132.598 159.669 128.825 153.401 117.342 157.486Z" fill="var(--no-results-cat-flower-leaf)" stroke="var(--no-results-cat-main-stroke)" stroke-width="3"/>
                    <path class="moving-flower-2" d="M129.092 145.843C132.185 148.352 137.412 155.093 133.574 161.986C129.98 161.229 124.053 156.941 129.092 145.843Z" fill="var(--no-results-cat-flower-leaf)" stroke="var(--no-results-cat-main-stroke)" stroke-width="3"/>
                    <circle cx="157.82" cy="195.314" r="1.5" fill="var(--no-results-cat-dot)"/>
                    <circle cx="175.82" cy="185.314" r="1.5" fill="var(--no-results-cat-dot)"/>
                    <circle cx="128.82" cy="202.314" r="1.5" fill="var(--no-results-cat-dot)"/>
                    <circle cx="98.8201" cy="192.314" r="1.5" fill="var(--no-results-cat-dot)"/>
                    <circle cx="121.82" cy="220.314" r="1.5" fill="var(--no-results-cat-dot)"/>
                    <circle cx="171.82" cy="204.314" r="1.5" fill="var(--no-results-cat-dot)"/>
                    <path d="M157.82 209.314C158.649 209.314 159.32 208.643 159.32 207.814C159.32 206.986 158.649 206.314 157.82 206.314V209.314ZM118.32 209.314H157.82V206.314H118.32V209.314Z" fill="var(--no-results-cat-dot)"/>
                    <path d="M100.32 207.814H108.32C108.32 205.814 109.22 201.814 112.82 201.814C116.42 201.814 116.987 205.814 116.82 207.814H120.32" stroke="var(--no-results-cat-main-stroke)" stroke-width="3" stroke-linecap="round"/>
                    <path class="moving-line" d="M103.5 144L111 174" stroke="var(--no-results-cat-rays-stroke)" stroke-width="3" stroke-linecap="round"/>
                    <path class="moving-line" d="M94.6205 63.4141L104.88 92.586" stroke="var(--no-results-cat-rays-stroke)" stroke-width="3" stroke-linecap="round"/>
                    <path class="moving-line" d="M91.8557 162L94.4011 192.818" stroke="var(--no-results-cat-rays-stroke)" stroke-width="3" stroke-linecap="round"/>
                    <path class="moving-line" d="M119.656 114.243L132.604 142.325" stroke="var(--no-results-cat-rays-stroke)" stroke-width="3" stroke-linecap="round"/>
                    <path class="moving-line" d="M152.289 157.744L166.659 185.126" stroke="var(--no-results-cat-rays-stroke)" stroke-width="3" stroke-linecap="round"/>
                </g>
                <defs>
                    <linearGradient id="paint0_linear_20364_776232" x1="81.2472" y1="13.9656" x2="90.6148" y2="11.2115" gradientUnits="userSpaceOnUse">
                        <stop offset="0" stop-color="#E5E5FA"/>
                        <stop offset="0.5" stop-color="#EAEAF7"/>
                        <stop offset="1" stop-color="#F6EEF5"/>
                    </linearGradient>
                    <clipPath id="clip0_20364_776232">
                        <rect width="198" height="234" fill="white"/>
                    </clipPath>
                    <clipPath id="clip1_20364_776232">
                        <rect width="165" height="187.78" fill="white" transform="translate(11.7905 -27) rotate(15.6928)"/>
                    </clipPath>
                </defs>
            </svg>
        `;
    }
}
