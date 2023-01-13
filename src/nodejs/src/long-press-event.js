// NOTE(AY): This file is currently unused.
// long-press.ts is used instead (it is based on this one).

/*!
 * long-press-event - v@version@
 * Pure JavaScript long-press-event
 * https://github.com/john-doherty/long-press-event
 * @author John Doherty <www.johndoherty.info>
 * @license MIT
 */
(function (window, document) {

    'use strict';

    const defaultDelay = '500'; // 500ms, must be string
    const ua = navigator.userAgent.toLowerCase();
    const isAndroid = ua.indexOf('android') > -1;
    const captureEventsOption = { capture: true };

    let timer = null;
    let startX = 0; // mouse x position when timer started
    let startY = 0; // mouse y position when timer started
    let maxDiffX = isAndroid ? 5 : 10; // max number of X pixels the mouse can move during long press before it is canceled
    let maxDiffY = isAndroid ? 5 : 10; // max number of Y pixels the mouse can move during long press before it is canceled
    let cancelNextClickAfterMouseUpEvent = false;
    let cancelNextClickEvent = false;

    const hasPointerEvents = (('PointerEvent' in window) || (window.navigator && 'msPointerEnabled' in window.navigator));
    const isTouch = (('ontouchstart' in window) || (navigator.MaxTouchPoints > 0) || (navigator.msMaxTouchPoints > 0));
    const pointerDown = hasPointerEvents ? 'pointerdown' : isTouch ? 'touchstart' : 'mousedown';
    const pointerMove = hasPointerEvents ? 'pointermove' : isTouch ? 'touchmove' : 'mousemove';
    const pointerUp = hasPointerEvents ? 'pointerup' : isTouch ? 'touchend' : 'mouseup';
    const pointerCancel = hasPointerEvents ? 'pointercancel' : isTouch ? 'touchcancel' : null;

    // console.log('long-press-event: attaching event handlers')
    document.addEventListener('click', onClick, captureEventsOption);
    document.addEventListener('contextmenu', onClick, captureEventsOption);
    document.addEventListener('wheel', clearLongPressTimer, captureEventsOption);
    document.addEventListener('scroll', clearLongPressTimer, captureEventsOption);
    document.addEventListener(pointerDown, onPointerDown, captureEventsOption);
    document.addEventListener(pointerMove, onPointerMove, captureEventsOption);
    document.addEventListener(pointerUp, onPointerUp, captureEventsOption);
    if (pointerCancel)
        document.addEventListener(pointerCancel, onPointerCancel, captureEventsOption);

}(window, document));
