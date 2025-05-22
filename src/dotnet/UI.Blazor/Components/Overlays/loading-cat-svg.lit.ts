import {customElement, property} from "lit/decorators.js";
import {css, html, LitElement} from "lit";

@customElement('loading-cat-svg')
class LoadingCatSvg extends LitElement {
    static styles = [css`
        :host {
        }
        @keyframes twinkle {
            0%, 100% {
                filter: blur(2px) opacity(0);
            }
            50% {
                filter: blur(0px) opacity(1);
            }
        }
        .twinkle-star {
            transform: scale(0.8);
            animation: twinkle 5s infinite ease-in-out;
        }
        .twinkle-star.delayed {
            filter: blur(2px) opacity(0);
        }
        .star-1 { animation-delay: 0.5s; }
        .star-2 { animation-delay: 1.75s; }
        .star-3 { animation-delay: 3.0s; }
        .star-4 { animation-delay: 4.25s; }
        .star-5 { animation-delay: 1.0s; }
        .star-6 { animation-delay: 3.5s; }
        .star-7 { animation-delay: 1.0s; }
        .star-8 { animation-delay: 3.5s; }
    `];
    protected render(): unknown {
        return html`
            <svg width="165" height="257" viewBox="0 0 165 257" fill="none" xmlns="http://www.w3.org/2000/svg">
                <path d="M115 59.6H161.7C153.8 32.1 131.9 10.5 104.3 2.9V48.9C104.3 54.8 109.1 59.6 115 59.6Z" fill="url(#paint0_linear_20922_59374)"/>
                <path d="M89.6 48.9V0.3C87.2 0.1 84.9 0 82.5 0C44.9 0 13.1 25.2 3.2 59.6H78.8C84.7 59.6 89.6 54.8 89.6 48.9Z" fill="url(#paint1_linear_20922_59374)"/>
                <path d="M104.3 85.1V162C139.3 152.4 165 120.5 165 82.5C165 79.8 164.9 77 164.6 74.4H115C109.1 74.3 104.3 79.2 104.3 85.1Z" fill="url(#paint2_linear_20922_59374)"/>
                <path d="M78.8 74.3H0.4C0.1 77 0 79.7 0 82.5C0 128 36.9 165 82.5 165C84.9 165 87.3 164.9 89.6 164.7V85.1C89.6 79.2 84.7 74.3 78.8 74.3Z" fill="url(#paint3_linear_20922_59374)"/>
                <g clip-path="url(#clip0_20922_59374)"
                   style="transform: translate(0, 26px) scale(0.6);"
                   class="star-1 twinkle-star">
                    <path d="M46.5 36L43.2 29.9L37 26.5L43.1 23.2L46.5 17L49.9 23.1L56 26.5L49.9 29.9L46.5 36Z"
                          fill="var(--loading-cat-stars)"/>
                </g>
                <g clip-path="url(#clip0_20922_59374)"
                    style="transform: translate(50px, 0) scale(0.6);"
                    class="star-2 twinkle-star delayed">
                    <path
                        d="M46.5 36L43.2 29.9L37 26.5L43.1 23.2L46.5 17L49.9 23.1L56 26.5L49.9 29.9L46.5 36Z"
                        fill="var(--loading-cat-stars)"
                    />
                </g>
                <g clip-path="url(#clip1_20922_59374)"
                   style="transform: translate(6px, -6px) scale(0.6);"
                   class="star-3 twinkle-star">
                    <path d="M67.5 55L64.2 48.9L58 45.5L64.1 42.2L67.5 36L70.9 42.1L77 45.5L70.9 48.9L67.5 55Z" fill="var(--loading-cat-stars)"/>
                </g>
                <g clip-path="url(#clip1_20922_59374)"
                   style="transform: translate(28px, 18px) scale(0.6);"
                   class="star-4 twinkle-star delayed">
                    <path d="M67.5 55L64.2 48.9L58 45.5L64.1 42.2L67.5 36L70.9 42.1L77 45.5L70.9 48.9L67.5 55Z" fill="var(--loading-cat-stars)"/>
                </g>
                <g clip-path="url(#clip2_20922_59374)"
                   style="transform: translate(32px, -6px) scale(0.6);"
                   class="star-5 twinkle-star">
                    <path d="M138.5 53L135.2 46.9L129 43.5L135.1 40.2L138.5 34L141.9 40.1L148 43.5L141.9 46.9L138.5 53Z"
                          fill="var(--loading-cat-stars)"/>
                </g>
                <g clip-path="url(#clip2_20922_59374)"
                   style="transform: translate(54px, 20px) scale(0.6);"
                   class="star-6 twinkle-star delayed">
                    <path d="M138.5 53L135.2 46.9L129 43.5L135.1 40.2L138.5 34L141.9 40.1L148 43.5L141.9 46.9L138.5 53Z"
                          fill="var(--loading-cat-stars)"/>
                </g>
                <g clip-path="url(#clip3_20922_59374)"
                   style="transform: translate(54px, 30px) scale(0.6);"
                   class="star-7 twinkle-star">
                    <path d="M120.5 106L117.2 99.9L111 96.5L117.1 93.2L120.5 87L123.9 93.1L130 96.5L123.9 99.9L120.5 106Z"
                          fill="var(--loading-cat-stars)"/>
                </g>
                <g clip-path="url(#clip3_20922_59374)"
                   style="transform: translate(74px, 46px) scale(0.6);"
                   class="star-8 twinkle-star delayed">
                    <path d="M120.5 106L117.2 99.9L111 96.5L117.1 93.2L120.5 87L123.9 93.1L130 96.5L123.9 99.9L120.5 106Z"
                          fill="var(--loading-cat-stars)"/>
                </g>
                <path d="M133.75 145.6V136.1L130.25 131.1L124.75 129.1L117.75 134.1V148.6L123.25 165.6L132.25 181.1L139.75 194.1L143.75 208.1L141.25 222.1L135.75 230.6L126.75 236.1L114.75 238.1L101.25 236.1L88.75 230.6L99.25 225.6L105.75 222.1L114.75 213.6L120.25 204.1L121.25 191.1L117.75 182.1L112.25 174.6L103.75 168.6L105.75 160.1V148.6L101.25 131.1L95.75 119.1L87.25 111.6L92.25 104.6L93.75 95.0996V87.0996L92.25 81.5996L93.75 74.5996V66.0996L90.25 58.5996L87.25 61.0996L82.75 62.5996L79.75 67.5996L74.75 66.0996L67.25 64.5996H59.25L53.75 66.0996L48.75 61.0996L42.75 58.5996L40.75 64.5996L38.75 73.5996L41.75 81.5996L38.75 87.0996V95.0996L40.75 102.1L45.75 110.1L40.75 116.1L34.75 124.6L29.75 138.6L27.75 152.1L29.75 168.6L19.25 177.6L13.75 188.6L15.25 205.6L23.75 218.1L35.75 225.6L53.25 232.6H65.25L73.75 240.6L88.75 248.6L102.75 254.6H117.75L131.75 251.6L142.75 245.1L151.75 236.1L157.25 222.1L159.25 207.1L155.25 191.1L151.75 182.1L146.25 171.6L139.75 161.6L135.75 152.1L133.75 145.6Z" fill="url(#paint4_linear_20922_59374)"/>
                <path d="M93.8002 162.9C110.7 169.2 122.3 181.6 122.3 195.8C122.3 211.3 108.6 224.6 89.2002 230.2" stroke="var(--loading-cat-lines-1)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M134.6 144.1C134.4 142.5 134.3 141 134.2 139.4C133.5 143.2 130.2 146.1 126.2 146.1C122 146.1 118.6 142.9 118.2 138.9C118.3 145.4 119.4 152.2 121.7 158.9C126.8 156.9 131.6 154.5 136.1 151.6C135.5 149.1 135 146.6 134.6 144.1Z" fill="var(--loading-cat-tail)"/>
                <path d="M134.3 139.5C134.4 139.1 134.4 138.6 134.4 138.1C134.4 137.7 134.4 137.3 134.3 136.9C134.3 137.7 134.3 138.6 134.3 139.5Z" fill="var(--loading-cat-lines-1)" stroke="var(--loading-cat-lines-1)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M118.3 137.201C118.3 137.501 118.2 137.801 118.2 138.101C118.2 138.401 118.2 138.701 118.2 138.901C118.3 138.401 118.3 137.801 118.3 137.201Z" fill="var(--loading-cat-lines-1)" stroke="var(--loading-cat-lines-1)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M54.5005 67C54.5005 67 61.5005 83 46.0005 91.1" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M61.1997 66C61.1997 66 65.9997 86.7 52.6997 97.5" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M79.9998 67.5C79.9998 67.5 74 82.5 88.8004 91.1" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M73.9007 66C73.9007 66 69.1007 86.7 82.4007 97.5" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M67.4009 64.6016V96.5016" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M18.2993 182.199C18.2993 182.199 26.6993 175.899 34.9993 179.799" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M94.6997 178C94.6997 178 103.8 175.6 111.4 179.9" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M15.6001 190.299C15.6001 190.299 26.0001 183.999 36.4001 187.899" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M94.4009 186.4C94.4009 186.4 106.001 182.7 115.201 188.9" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M15.1997 200.699C15.1997 200.699 25.3997 193.299 36.5997 195.899" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M93.5005 193.398C93.5005 193.398 105.9 191.498 114.7 198.998" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M19.7993 211.9C19.7993 211.9 28.6993 203 40.2993 203.9" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M111.8 208.3C111.8 208.3 101.8 200.6 90.5005 202.9" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M27.5005 219.5C27.5005 219.5 34.7005 211.4 44.3005 212.6" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M86.0005 212.8C86.0005 212.8 96.6005 210.7 103.6 217.4" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M36.4009 225.8C36.4009 225.8 42.1009 219.3 49.8009 220.3" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M81.0005 218.099C81.0005 218.099 89.5005 216.599 94.9005 222.099" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M55.8999 72.3004C55.8999 65.5004 49.1999 59.9004 43.8999 59.9004C43.8999 59.9004 35.0999 70.8004 43.8999 84.6004C43.8999 84.6004 45.6999 84.4004 47.8999 83.5004" stroke="var(--loading-cat-lines-1)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M55.2001 232.001C31.9001 227.901 14.6001 213.301 14.6001 195.901C14.6001 181.701 24.2005 169.8 41.0005 163.5" stroke="var(--loading-cat-lines-1)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M47.2001 111.601C42.5001 106.601 39.6001 99.9012 39.6001 92.5012C39.6001 77.1012 52.1001 64.7012 67.4001 64.7012C82.7001 64.7012 95.2001 77.2012 95.2001 92.5012C95.2001 97.3012 94.0001 101.901 91.8001 105.901" stroke="var(--loading-cat-lines-1)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M55.8999 72.3004C55.8999 65.5004 49.1999 59.9004 43.8999 59.9004C43.8999 59.9004 35.0999 70.8004 43.8999 84.6004C43.8999 84.6004 45.6999 84.4004 47.8999 83.5004" fill="var(--loading-cat-stars)"/>
                <path d="M55.8999 72.3004C55.8999 65.5004 49.1999 59.9004 43.8999 59.9004C43.8999 59.9004 35.0999 70.8004 43.8999 84.6004C43.8999 84.6004 45.6999 84.4004 47.8999 83.5004" stroke="var(--loading-cat-lines-1)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M64.7 231.5C64.7 231.5 60.1 226.2 58 223L64.7 231.5Z" fill="var(--loading-cat-lines-1)"/>
                <path d="M64.7 231.5C64.7 231.5 60.1 226.2 58 223" stroke="var(--loading-cat-lines-1)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M40.9009 77.9016C40.9009 77.9016 46.4009 78.6016 49.8009 76.1016L40.9009 77.9016Z" fill="var(--loading-cat-lines-2)"/>
                <path d="M40.9009 77.9016C40.9009 77.9016 46.4009 78.6016 49.8009 76.1016" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M40.1997 70.3004C40.1997 70.3004 45.5997 71.8004 49.2997 69.9004L40.1997 70.3004Z" fill="var(--loading-cat-lines-2)"/>
                <path d="M40.1997 70.3004C40.1997 70.3004 45.5997 71.8004 49.2997 69.9004" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M55.9001 72.3004C55.9001 65.5004 49.2001 59.9004 43.9001 59.9004C43.9001 59.9004 35.1001 70.8004 43.9001 84.6004C43.9001 84.6004 45.7001 84.4004 47.9001 83.5004" stroke="var(--loading-cat-lines-1)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M78.9001 72.3004C78.9001 65.5004 85.6001 59.9004 90.9001 59.9004C90.9001 59.9004 99.7001 70.8004 90.9001 84.6004C90.9001 84.6004 88.8001 84.4004 86.3001 83.3004" fill="var(--loading-cat-stars)"/>
                <path d="M78.9001 72.3004C78.9001 65.5004 85.6001 59.9004 90.9001 59.9004C90.9001 59.9004 99.7001 70.8004 90.9001 84.6004C90.9001 84.6004 88.8001 84.4004 86.3001 83.3004" stroke="var(--loading-cat-lines-1)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M80.7993 66.1992C80.7993 66.1992 85.3993 69.3992 89.4993 68.7992L80.7993 66.1992Z" fill="var(--loading-cat-lines-2)"/>
                <path d="M80.7993 66.1992C80.7993 66.1992 85.3993 69.3992 89.4993 68.7992" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M79.5005 72.6992C79.5005 72.6992 84.1005 75.8992 88.2005 75.2992L79.5005 72.6992Z" fill="var(--loading-cat-lines-2)"/>
                <path d="M79.5005 72.6992C79.5005 72.6992 84.1005 75.8992 88.2005 75.2992" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M78.9009 72.3004C78.9009 65.5004 85.6009 59.9004 90.9009 59.9004C90.9009 59.9004 99.7009 70.8004 90.9009 84.6004C90.9009 84.6004 88.8009 84.4004 86.3009 83.3004" stroke="var(--loading-cat-lines-1)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M64.8 131.801C64.8 131.801 58.7 164.901 64.5 219.201" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M70.3 131.801C70.3 131.801 76.6001 165.701 70.9001 220.101" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M74.5 131.801C74.5 131.801 96.7 166.601 75.9 219.001" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M60.5 131.801C60.5 131.801 38.3 166.601 59.1 219.001" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M93.2992 126.102C90.5992 132.202 83.1992 135.502 83.1992 135.502" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M41.5996 117.301C41.5996 117.301 41.5996 129.001 49.3996 132.901" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M86.7988 142.201C86.7988 142.201 94.0988 138.901 96.8988 132.801L86.7988 142.201Z" fill="var(--loading-cat-lines-2)"/>
                <path d="M86.7988 142.201C86.7988 142.201 94.0988 138.901 96.8988 132.801" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M90.4004 150.2C90.4004 150.2 96.7004 147.3 99.8004 142L90.4004 150.2Z" fill="var(--loading-cat-lines-2)"/>
                <path d="M90.4004 150.2C90.4004 150.2 96.7004 147.3 99.8004 142" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M92.7988 159.001C92.7988 159.001 97.8988 156.701 101.099 152.201L92.7988 159.001Z" fill="var(--loading-cat-lines21)"/>
                <path d="M92.7988 159.001C92.7988 159.001 97.8988 156.701 101.099 152.201" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M37 124C37 124 37 135.7 44.8 139.6" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M33.0996 132.102C33.0996 132.102 33.0996 143.802 40.8996 147.702" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M30.4004 141.801C30.4004 141.801 30.4004 153.501 38.2004 157.401" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M76.3992 105.5C76.3992 105.5 77.1992 116 74.6992 126.9L76.3992 105.5Z" fill="var(--loading-cat-lines-2)"/>
                <path d="M76.3992 105.5C76.3992 105.5 77.1992 116 74.6992 126.9" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M84.8996 109.602C84.8996 109.602 85.1996 117.402 82.5996 128.302L84.8996 109.602Z" fill="var(--loading-cat-lines-2)"/>
                <path d="M84.8996 109.602C84.8996 109.602 85.1996 117.402 82.5996 128.302" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M49.9995 109.102C49.9995 109.102 49.7995 118.302 54.0995 128.202L49.9995 109.102Z" fill="var(--loading-cat-lines-2)"/>
                <path d="M49.9995 109.102C49.9995 109.102 49.7995 118.302 54.0995 128.202" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M58.7997 105.5C58.7997 105.5 57.9997 115.7 60.5997 126.6L58.7997 105.5Z" fill="var(--loading-cat-lines-2)"/>
                <path d="M58.7997 105.5C58.7997 105.5 57.9997 115.7 60.5997 126.6" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M67.2992 104.301L67.1992 126.801" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M55.2992 133.301C46.2992 141.201 40.1992 157.501 40.1992 176.401C40.1992 193.701 45.3992 208.801 53.0992 217.301" stroke="var(--loading-cat-lines-1)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M86.6993 142.301C91.5993 151.001 94.6993 163.001 94.6993 176.301C94.6993 193.601 89.4993 208.701 81.7993 217.201" stroke="var(--loading-cat-lines-1)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M87 247.6C87 247.6 87 240.1 93 237.9" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M96.1 250.9C96.1 250.9 95.1 243.4 100.7 240.4" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M105.7 253.4C105.7 253.4 103.8 246.1 109.1 242.5" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M116.7 254.599C116.7 254.599 113.1 247.999 117.3 243.199" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M128.6 253.2C128.6 253.2 123.2 247.9 125.8 242.1" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M139.4 248.9C139.4 248.9 132.7 245.4 133.6 239.1" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M126.3 146.2C130.3 146.2 133.6 143.3 134.3 139.5C134.3 138.6 134.3 137.8 134.3 136.9C133.7 133 130.4 130 126.3 130C122.1 130 118.7 133.1 118.3 137.2C118.3 137.8 118.3 138.4 118.3 138.9C118.7 143 122.2 146.2 126.3 146.2Z" fill="var(--loading-cat-tail)" stroke="var(--loading-cat-lines-1)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M69.2999 235.7C69.2999 235.7 68.3999 225.7 78.0999 224.4" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M73.7 239.3C73.7 239.3 74.7 231 81.8 230.1" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M80.7 243.899C80.7 243.899 80.7 236.399 86.7 234.199" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M147.7 241.999C147.7 241.999 140.4 239.999 139.8 233.699" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M154.7 233C154.7 233 147.2 233 144.9 227" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M158.5 223.101C158.5 223.101 151.1 224.701 147.7 219.301" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M159.8 212.799C159.8 212.799 152.6 215.199 148.7 210.199" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M159 202.9C159 202.9 152.2 206.1 147.6 201.6" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M157 192.4C157 192.4 151.3 197.3 145.7 194.3" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M153.2 183C153.2 183 148 188.4 142.1 185.9" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M148.7 175.4C148.7 175.4 143.5 180.8 137.6 178.3" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M144.1 168.199C144.1 168.199 138.9 173.599 133 171.099" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M140.1 161.1C140.1 161.1 134.9 166.5 129 164" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M136.8 152.699C136.8 152.699 131.6 158.099 125.7 155.599" stroke="var(--loading-cat-lines-2)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M74.9 219.8C64 221 63 229.7 63 229.7C89.5 258.6 127.3 262 147.2 242.1C164.1 225.2 163.1 196 146.6 171.6C140.1 163.1 135.9 153.5 134.7 144.2C134.5 142.6 134.4 141.1 134.3 139.5C133.6 143.3 130.3 146.2 126.3 146.2C122.1 146.2 118.7 143 118.3 139C118.4 151.9 123 165.8 131.7 178.3C137.8 186.6 141.9 196 143.1 205C144.5 215.6 142 224.8 136 230.8C129.8 237 121.6 238.3 115.8 238.3C105.1 238.3 93.5 234 83.4 226.5" stroke="var(--loading-cat-lines-1)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <path d="M30.6004 168.7C29.5004 164 28.9004 159 28.9004 153.9C28.9004 125.8 46.1004 103 67.4004 103C88.7004 103 105.9 125.8 105.9 153.9C105.9 156.6 105.7 159.3 105.4 162" stroke="var(--loading-cat-lines-1)" stroke-width="3.0734" stroke-miterlimit="10" stroke-linecap="round" stroke-linejoin="round"/>
                <defs>
                    <linearGradient id="paint0_linear_20922_59374" x1="117" y1="5.5" x2="115.854" y2="170" gradientUnits="userSpaceOnUse">
                        <stop offset="0" stop-color="var(--loading-cat-window-gradient-1)"/>
                        <stop offset="1" stop-color="var(--loading-cat-window-gradient-2)"/>
                    </linearGradient>
                    <linearGradient id="paint1_linear_20922_59374" x1="117" y1="5.5" x2="115.854" y2="170" gradientUnits="userSpaceOnUse">
                        <stop offset="0" stop-color="var(--loading-cat-window-gradient-1)"/>
                        <stop offset="1" stop-color="var(--loading-cat-window-gradient-2)"/>
                    </linearGradient>
                    <linearGradient id="paint2_linear_20922_59374" x1="117" y1="5.5" x2="115.854" y2="170" gradientUnits="userSpaceOnUse">
                        <stop offset="0" stop-color="var(--loading-cat-window-gradient-1)"/>
                        <stop offset="1" stop-color="var(--loading-cat-window-gradient-2)"/>
                    </linearGradient>
                    <linearGradient id="paint3_linear_20922_59374" x1="117" y1="5.5" x2="115.854" y2="170" gradientUnits="userSpaceOnUse">
                        <stop offset="0" stop-color="var(--loading-cat-window-gradient-1)"/>
                        <stop offset="1" stop-color="var(--loading-cat-window-gradient-2)"/>
                    </linearGradient>
                    <linearGradient id="paint4_linear_20922_59374" x1="86.6587" y1="70.6988" x2="93.0559" y2="256.503" gradientUnits="userSpaceOnUse">
                        <stop offset="0" stop-color="var(--loading-cat-body-gradient-1)"/>
                        <stop offset="1" stop-color="var(--loading-cat-body-gradient-2)"/>
                    </linearGradient>
                    <clipPath id="clip0_20922_59374">
                        <rect width="19" height="19" fill="var(--loading-cat-stars)" transform="translate(37 17)"/>
                    </clipPath>
                    <clipPath id="clip1_20922_59374">
                        <rect width="19" height="19" fill="var(--loading-cat-stars)" transform="translate(58 36)"/>
                    </clipPath>
                    <clipPath id="clip2_20922_59374">
                        <rect width="19" height="19" fill="var(--loading-cat-stars)" transform="translate(129 34)"/>
                    </clipPath>
                    <clipPath id="clip3_20922_59374">
                        <rect width="19" height="19" fill="var(--loading-cat-stars)" transform="translate(111 87)"/>
                    </clipPath>
                </defs>
            </svg>
        `;
    }
}
