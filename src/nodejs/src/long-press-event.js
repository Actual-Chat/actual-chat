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

    // patch CustomEvent to allow constructor creation (IE/Chrome)
    if (typeof window.CustomEvent !== 'function') {
        window.CustomEvent = function (event, params) {
            params = params || { bubbles: false, cancelable: false, detail: undefined };
            let evt = document.createEvent('CustomEvent');
            evt.initCustomEvent(event, params.bubbles, params.cancelable, params.detail);
            return evt;
        };

        window.CustomEvent.prototype = window.Event.prototype;
    }

    let defaultRequestAnimationFrame = window.requestAnimationFrame
        || window.webkitRequestAnimationFrame
        || window.mozRequestAnimationFrame
        || window.oRequestAnimationFrame
        || window.msRequestAnimationFrame;
    let defaultCancelAnimationFrame = window.cancelAnimationFrame
        || window.webkitCancelAnimationFrame
        || window.webkitCancelRequestAnimationFrame
        || window.mozCancelRequestAnimationFrame
        || window.oCancelRequestAnimationFrame
        || window.msCancelRequestAnimationFrame;
    if (!(defaultCancelAnimationFrame && !defaultCancelAnimationFrame)) {
        defaultRequestAnimationFrame = null;
        defaultCancelAnimationFrame = null;
    }
    // console.log('RAF:', defaultRequestAnimationFrame, defaultCancelAnimationFrame);

    let requestAnimationFrame = function (callback) {
        if (defaultRequestAnimationFrame)
            return defaultRequestAnimationFrame(callback);
        else
            window.setTimeout(callback, 1000 / 60);
    };

    // Behaves the same as setTimeout except uses requestAnimationFrame() where possible for better performance
    function requestTimeout(fn, delay) {
        if (!defaultRequestAnimationFrame)
            return {
                timeout: window.setTimeout(fn, delay),
            }

        const start = new Date().getTime();
        const handle = {
            value: null,
        };

        const loop = function () {
            const delta = new Date().getTime() - start;
            if (delta >= delay)
                fn.call();
            else
                handle.value = requestAnimationFrame(loop);
        };

        handle.value = requestAnimationFrame(loop);
        return handle;
    }

    function clearRequestTimeout(handle) {
        if (handle?.timeout)
            clearTimeout(handle.timeout);
        else if (handle?.value)
            defaultCancelAnimationFrame(handle.value);
    }

    /**
     * Fires the 'long-press' event on element
     * @param {MouseEvent|PointerEvent|TouchEvent} originalEvent The original event being fired
     * @returns {void}
     */
    function fireLongPressEvent(originalEvent) {
        clearLongPressTimer();

        originalEvent = unifyEvent(originalEvent);
        let event = new CustomEvent('long-press', {
            bubbles: true,
            cancelable: true,

            // custom event data (legacy)
            detail: {
                clientX: originalEvent.clientX,
                clientY: originalEvent.clientY
            },

            // add coordinate data that would typically acompany a touch/click event
            clientX: originalEvent.clientX,
            clientY: originalEvent.clientY,
            offsetX: originalEvent.offsetX,
            offsetY: originalEvent.offsetY,
            pageX: originalEvent.pageX,
            pageY: originalEvent.pageY,
            screenX: originalEvent.screenX,
            screenY: originalEvent.screenY
        });

        const mustHandleDefault = this.dispatchEvent(event);
        const mustCancelNextClick = !mustHandleDefault || !event.cancelBubble;
        cancelNextClickEvent = mustCancelNextClick;
        cancelNextClickAfterMouseUpEvent = mustCancelNextClick;
    }

    /**
     * consolidates mouse, touch, and Pointer events
     * @param {MouseEvent|PointerEvent|TouchEvent} e The original event being fired
     * @returns {MouseEvent|PointerEvent|Touch}
     */
    function unifyEvent(e) {
        return e.changedTouches !== undefined ? e.changedTouches[0] : e;
    }

    function startLongPressTimer(e) {
        clearLongPressTimer(e);

        const el = e.target;
        const delayText = getNearestAttributeValue(el, 'data-long-press-delay', defaultDelay);
        const delay = parseInt(delayText);
        timer = requestTimeout(fireLongPressEvent.bind(el, e), delay);
    }

    function clearLongPressTimer(e) {
        clearRequestTimeout(timer);
        timer = null;
    }

    function cancelEvent(e) {
        e.stopPropagation();
        e.stopImmediatePropagation();
        e.preventDefault();
    }

    function onPointerDown(e) {
        // NOTE(AY): Only primary button should trigger long presses!
        if (e.button !== 0)
            return;

        startX = e.clientX;
        startY = e.clientY;
        startLongPressTimer(e);
    }

    function onPointerMove(e) {
        if (e.button !== 0)
            return;

        // calculate total number of pixels the pointer has moved
        const diffX = Math.abs(startX - e.clientX);
        const diffY = Math.abs(startY - e.clientY);

        // if pointer has moved more than allowed, cancel the long-press timer and therefore the event
        if (diffX >= maxDiffX || diffY >= maxDiffY)
            clearLongPressTimer(e);
    }

    function onPointerUp(e) {
        clearLongPressTimer();

        if (!cancelNextClickAfterMouseUpEvent)
            return;

        cancelNextClickAfterMouseUpEvent = false;
        cancelNextClickEvent = true;
        // Let's stop click cancellation anyway in 200ms
        requestTimeout(function() {
            cancelNextClickEvent = false;
        }, 100);
    }

    function onPointerCancel(e) {
        clearLongPressTimer();

        cancelNextClickAfterMouseUpEvent = false;
        cancelNextClickEvent = false;
    }

    function onClick(e) {
        clearLongPressTimer();

        if (cancelNextClickEvent) {
            // console.log('long-press: cancelling click:', e)
            cancelNextClickEvent = false;
            cancelEvent(e);
        }
    }

    function getNearestAttributeValue(el, attributeName, defaultValue) {
        // walk up the dom tree looking for data-action and data-trigger
        while (el && el !== document.documentElement) {
            const value = el.getAttribute(attributeName);
            if (value)
                return value;

            el = el.parentNode;
        }
        return defaultValue;
    }

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
