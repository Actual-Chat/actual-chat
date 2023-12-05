﻿// We use script here to make sure this overlay isn't used as content image by crawlers
(function() {
    const overlay = document.getElementById('until-ui-is-ready');
    if (overlay) {
        /*
        const userAgent = (navigator?.userAgent ?? '').toLowerCase();
        const isChromium = userAgent.indexOf('chrome') >= 0;
        const isWebKit = !isChromium && userAgent.indexOf('webkit') >= 0;
        if (isWebKit) {
            overlay.remove(); // No loading overlay on iOS
            return;
        }
        */
        overlay.innerHTML = `
            <div class="c-content">
                <img draggable="false" src="/dist/images/landing/ac-icon-light.svg" alt="Loading...">
                <div class="progress">
                    <div class="progress-value"></div>
                </div>
            </div>
        `;
    }
})();
