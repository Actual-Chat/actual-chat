import {customElement, property} from "lit/decorators.js";
import {css, html, LitElement} from "lit";

@customElement('empty-search-chat-svg')
class EmptySearchChatSvg extends LitElement {
    static styles = [css`
        :host {
        }
        @keyframes wink {
            0%, 45%, 55%, 100% {
                transform: translate(0, 0);
            }
            50% {
                transform: translate(0, 5px);
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
        @keyframes rotating-shadow {
            0%, 100% {
                transform: translate(-2px, 0px);
            }
            25% {
                transform: translate(0px, 1px);
            }
            50% {
                transform: translate(2px, 0px);
            }
            75% {
                transform: translate(0px, -1px);
            }
        }
        .left-eye {
            animation: wink 4s ease-in-out infinite;
        }
        .medal-star {
            transform-box: fill-box;
            transform-origin: center;
            animation: star-pulse 1.5s ease-in-out infinite;
        }
        .cat-shadow {
            animation: rotating-shadow 2s linear infinite;
        }
    `];
    protected render(): unknown {
        return html`
            <svg width="166" height="222" viewBox="0 0 166 222" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M82.5 222C128.06 222 165 185.06 165 139.5C165 93.94 128.06 57 82.5 57C36.94 57 0 93.94 0 139.5C0 185.06 36.94 222 82.5 222Z" fill="url(#search-cat-background-gradient)"/>
                <defs>
                    <linearGradient id="search-cat-background-gradient" x1="115.854" y1="-5.49172" x2="73.2856" y2="158.475" gradientUnits="userSpaceOnUse">
                        <stop offset="0" stop-color="var(--search-cat-background-gradient-1)"/>
                        <stop offset="1" stop-color="var(--search-cat-background-gradient-2)"/>
                    </linearGradient>
                </defs>
                <path d="M47.5 40L51.5 50.5L47.5 57.5V71.5L56 88.5L47.5 97L41.5 106.5L33.5 111L29.5 117.5C28 118.167 25.8 121 29 127C32.2 133 37.3333 132.833 39.5 132L45 146L60 169.5L70.5 180L80.5 184L92 182.5L102 176L111 163L120.5 141.5L124.5 127V116.5L127.5 114.5L137 105L141 95.5L141.5 93V64.5L145 52.5V42L137 32.5L127.5 31L118 35L113 22L102 23.5L95.5 29.5L92 35H77L70.5 26.5L62.5 23.5H54.5L50.5 28L47.5 40Z" fill="url(#search-cat-background-gradient-1)"/>
                <defs>
                    <linearGradient id="search-cat-background-gradient-1" x1="131" y1="30.5" x2="48.5" y2="184" gradientUnits="userSpaceOnUse">
                        <stop offset="0" stop-color="var(--search-cat-background-gradient-4)"/>
                        <stop offset="1" stop-color="var(--search-cat-background-gradient-3)"/>
                    </linearGradient>
                </defs>
                <ellipse cx="62.5133" cy="127.317" rx="11.0582" ry="12.2539" transform="rotate(50.7287 62.5133 127.317)" fill="var(--search-cat-paws)"/>
                <path d="M107 89C113.466 93.2447 118.552 98.9626 121.593 105.57C124 111.5 126.374 118.3 123.5 127.5" stroke="var(--search-cat-border)" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M99 36.8696L109.484 45C107.387 35.9128 110.323 25.8696 112 23C112 23 100.258 23.4783 99 36.8696Z" fill="var(--search-cat-border)"/>
                <path d="M67 37.8696L56.5161 46C58.6129 36.9128 55.6774 26.8696 54 24C54 24 65.7419 24.4783 67 37.8696Z" fill="var(--search-cat-border)"/>
                <path d="M66 36.2391L55.5161 44C57.6129 35.3258 54.6774 25.7391 53 23C53 23 64.7419 23.4565 66 36.2391Z" fill="var(--search-cat-paws)"/>
                <path d="M98 37.1304L110.097 46C107.677 36.0866 111.065 25.1304 113 22C113 22 99.4516 22.5217 98 37.1304Z" fill="var(--search-cat-paws)"/>
                <path d="M58.67 90.6604C52.7 84.6104 47.21 76.5104 47.21 67.5604C47.21 48.9404 63.1 33.9404 82.54 33.9404C101.98 33.9404 117.87 49.0604 117.87 67.5604C117.87 73.3604 116.34 78.9304 113.55 83.7604" stroke="var(--search-cat-border)" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
                <path fill-rule="evenodd" clip-rule="evenodd" d="M112.12 50.0607C114.508 55.5094 121.037 59.3967 128.695 59.3512C138.339 59.294 146.12 53.0202 146.074 45.3383C146.029 37.6564 138.174 31.4754 128.53 31.5326C124.584 31.556 120.951 32.6197 118.038 34.3945C118.657 35.8082 118.998 37.327 119.008 38.9096C119.035 43.4491 116.328 47.4969 112.12 50.0607Z" fill="var(--search-cat-paws)"/>
                <path d="M66.73 36.41C66.73 29.94 60.83 23.79 53.5 23.79C53.5 23.79 42.78 37.86 51.79 50.98" stroke="var(--search-cat-border)" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
                <circle cx="84" cy="13" r="6" fill="url(#paint0_linear_20263_769562)" fill-opacity="0.5"/>
                <path d="M100.919 89C104.849 83.8944 106.574 78.1367 106.929 72.5" stroke="var(--search-cat-chest)" stroke-width="8" stroke-linecap="round"/>
                <path d="M40.0799 131.17C40.0799 131.17 49.5299 129.99 53.9699 137.72C53.9699 137.72 63.95 140.8 71.41 132.46C78.05 125.03 71.41 116.73 71.41 116.73C71.41 116.73 64.6899 103.29 44.9099 105.57C37.9999 106.76 33.4999 110.76 30.9999 114.26C27.2971 119.444 27.4999 125.76 29.9999 127.76M55.8399 89C49.3741 93.2447 44.2881 98.9626 41.2474 105.57" stroke="var(--search-cat-border)" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M113.25 50.9898C124.29 37.3898 112.74 21.8398 112.74 21.8398C105.41 21.8398 99.42 29.1698 99.42 37.3898" stroke="var(--search-cat-border)" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M82.29 75.7105C96.9035 75.7105 108.75 69.1111 108.75 60.9705C108.75 52.8298 96.9035 46.2305 82.29 46.2305C67.6766 46.2305 55.83 52.8298 55.83 60.9705C55.83 69.1111 67.6766 75.7105 82.29 75.7105Z" fill="var(--search-cat-border)"/>
                <path d="M135.24 58.4504C133.44 58.7604 131.43 58.9304 129.3 58.9304C121.54 58.9304 115.25 56.6304 115.25 53.7904C115.25 50.9504 121.54 48.6504 129.3 48.6504C137.06 48.6504 143.35 50.9504 143.35 53.7904" stroke="var(--search-cat-border)" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M83.5 20C87.0899 20 90 17.0898 90 13.5C90 9.91015 87.0899 7 83.5 7C79.9102 7 77 9.91015 77 13.5C77 17.0898 79.9102 20 83.5 20Z" stroke="var(--search-cat-border)" stroke-width="3" stroke-miterlimit="10"/>
                <path d="M55.83 97.83C55.83 97.83 82.5001 108.5 114.5 96C116.36 95.42 107 89.5 107 89.5C87.5001 99 58.66 90.66 58.66 90.66" stroke="var(--search-cat-border)" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M118.15 35.2904C118.15 35.2904 122.93 30.3104 131.34 31.3104C141.1 32.4704 145.37 39.0104 145.37 46.4604C145.37 53.9104 139.94 58.4004 141.35 74.3104C142.672 89.2259 141.647 106.523 124.903 114.552" stroke="var(--search-cat-border)" stroke-width="3" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M66 60H61V65H66V60Z" fill="var(--search-cat-paws)"/>
                <path d="M71 55H66V60H71V55Z" fill="var(--search-cat-paws)" class="left-eye"/>
                <path d="M76 60H71V65H76V60Z" fill="var(--search-cat-paws)"/>
                <path d="M94 60H89V65H94V60Z" fill="var(--search-cat-paws)"/>
                <path d="M99 55H94V60H99V55Z" fill="var(--search-cat-paws)" class="left-eye"/>
                <path d="M104 60H99V65H104V60Z" fill="var(--search-cat-paws)"/>
                <path d="M85 68H80V73H85V68Z" fill="var(--search-cat-paws)"/>
                <path d="M62.14 137.65C62.14 137.65 62.15 132.83 56.21 129.69" stroke="var(--search-cat-border)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round"/>
                <path d="M68.07 134.36C68.07 134.36 67.31 128.41 62.14 126.4" stroke="var(--search-cat-border)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round"/>
                <path d="M72.65 130.77C72.65 130.77 72.66 125.83 66.72 122.81" stroke="var(--search-cat-border)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round"/>
                <path d="M123.39 49.1399V42.6299" stroke="var(--search-cat-border)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round"/>
                <path d="M130.5 47.97V41.46" stroke="var(--search-cat-border)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round"/>
                <path d="M137.45 49.1399V42.6299" stroke="var(--search-cat-border)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round"/>
                <path d="M53.5 23.7996C53.5 23.7996 65.92 19.4996 74.36 31.1796" stroke="var(--search-cat-border)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round"/>
                <path d="M112.59 21.5896C112.59 21.5896 99.63 19.4296 93.26 32.3596" stroke="var(--search-cat-border)" stroke-width="3" stroke-miterlimit="10" stroke-linecap="round"/>
                <path d="M83.5 20V33.5" stroke="var(--search-cat-border)" stroke-width="3"/>
                <path d="M122.945 135.401C123.167 134.603 122.7 133.776 121.901 133.555C121.103 133.333 120.276 133.8 120.055 134.599L122.945 135.401ZM39.0825 132.991C41.365 139.584 46.9196 152.462 54.6377 163.723C58.4974 169.354 62.9384 174.641 67.8319 178.533C72.7234 182.424 78.1694 185 84 185V182C79.0806 182 74.2766 179.826 69.6994 176.185C65.1241 172.546 60.8776 167.521 57.1123 162.027C49.5804 151.038 44.135 138.416 41.9175 132.009L39.0825 132.991ZM84 185C95.5507 185 104.068 176.798 110.216 166.785C116.391 156.726 120.435 144.437 122.945 135.401L120.055 134.599C117.565 143.563 113.609 155.524 107.659 165.215C101.682 174.952 93.9493 182 84 182V185Z" fill="var(--search-cat-border)"/>
                <path d="M43 138.5C48.5 143 65 150 93.5 148.5" stroke="var(--search-cat-border)" stroke-width="3" stroke-linecap="round"/>
                <path d="M107.5 146C110.5 145.5 115.5 144 118.5 141.5" stroke="var(--search-cat-border)" stroke-width="3" stroke-linecap="round"/>
                <path d="M100.5 107C106 115.5 111 134.5 95 169" stroke="var(--search-cat-chest)" stroke-width="8" stroke-linecap="round"/>
                <path d="M84.65 123.9C90.5208 123.9 95.28 119.14 95.28 113.27C95.28 107.399 90.5208 102.64 84.65 102.64C78.7792 102.64 74.02 107.399 74.02 113.27C74.02 119.14 78.7792 123.9 84.65 123.9Z" fill="var(--search-cat-paws)" stroke="var(--search-cat-border)" stroke-width="3" stroke-miterlimit="10"/>
                <ellipse cx="80.5644" cy="10.5279" rx="2.39081" ry="2.64932" transform="rotate(50.7287 80.5644 10.5279)" fill="var(--search-cat-paws)"/>
                <path class="medal-star" fill-rule="evenodd" clip-rule="evenodd" d="M92.9738 112.047C89.6342 112.01 86.9349 109.322 86.9066 106C86.8782 109.322 84.1789 112.01 80.8393 112.047C84.1789 112.083 86.8783 114.771 86.9066 118.093C86.9349 114.771 89.6342 112.083 92.9738 112.047Z" fill="var(--search-cat-border)"/>
                <path fill-rule="evenodd" clip-rule="evenodd" d="M78.0007 117.263C79.9108 117.263 81.4624 115.729 81.49 113.825C81.5175 115.729 83.0691 117.263 84.9792 117.263C83.0691 117.263 81.5175 118.798 81.49 120.701C81.4624 118.798 79.9108 117.263 78.0008 117.263M78.0007 117.263L78.0004 117.263L78.0007 117.263Z" fill="var(--search-cat-border)"/>
                <path class="cat-shadow" d="M84 212C96.1493 212 106 208.866 106 205C106 201.134 96.1493 198 84 198C71.8507 198 62 201.134 62 205C62 208.866 71.8507 212 84 212Z" fill="var(--search-cat-shadow)"/>
                <defs>
                    <linearGradient id="paint0_linear_20263_769562" x1="77.0667" y1="19" x2="86.4343" y2="16.2458" gradientUnits="userSpaceOnUse">
                        <stop offset="0" stop-color="#E5E5FA"/>
                        <stop offset="0.5" stop-color="#EAEAF7"/>
                        <stop offset="1" stop-color="#F6EEF5"/>
                    </linearGradient>
                </defs>
            </svg>
        `;
    }
}
