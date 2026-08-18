(() => {
    const arm = () => {
        const list = document.querySelector('.virtual-list.infinite-list');
        if (!list || !list.scrollController) return false;
        if (window.__vlt && window.__vlt.list === list) return true;
        if (window.__vlt) window.__vlt.stop();
        const c = list.querySelector('.c-virtual-container');
        const sc = list.scrollController;
        const ty = () => new DOMMatrix(getComputedStyle(c).transform).m42;
        const rows = [], events = [];
        const t0 = performance.now();
        let on = true;
        const meanY = (e) => { let s = 0; for (const t of e.touches) s += t.clientY; return e.touches.length ? Math.round(s / e.touches.length) : null; };
        const onTouch = (e) => {
            if (!on || events.length > 6000) return;

            const s = sc.getDebugState();
            events.push({ t: Math.round(performance.now() - t0), type: e.type, n: e.touches.length, y: meanY(e),
                top: Math.round(list.scrollTop), phase: s.phase, decision: s.decision });
        };
        for (const type of ['touchstart', 'touchmove', 'touchend', 'touchcancel'])
            document.addEventListener(type, onTouch, { passive: true, capture: true });
        // Sampled the moment the controller writes its transform, and otherwise once per frame. A
        // recorder that only samples on its own rAF can run before the controller's and see the new
        // scrollTop against last frame's transform - a phantom step of exactly the scroll delta on
        // every moving frame. Sampling from onTransform sees the pair as it was written.
        let lastSampleT = -1;
        // `authoritative` is a sample taken from onTransform: it replaces a rAF sample of the same
        // frame, since that one may have run before the controller wrote.
        const sample = (authoritative) => {
            if (!on) return;
            if (!document.contains(list)) { on = false; window.__vlt = null; return; }
            const now = performance.now();
            if (now - lastSampleT < 4) { if (authoritative && rows.length) rows.pop(); else return; }
            lastSampleT = now;
            if (rows.length < 12000) {
                const s = sc.getDebugState(); const lim = sc.getEffectiveScrollLimits();
                const r = c.getBoundingClientRect(); const lr = list.getBoundingClientRect();
                const transform = ty(); const band = sc.bandOffset;
                rows.push({ t: Math.round(performance.now() - t0), top: Math.round(list.scrollTop * 10) / 10,
                    tf: Math.round(transform * 10) / 10, base: Math.round((transform - band) * 10) / 10,
                    phase: s.phase, vis: Math.round(s.visible * 10) / 10,
                    band: Math.round(band * 10) / 10, decision: s.decision,
                    spr: Math.round((s.springVisible || 0) * 10) / 10, sp: Math.round((s.scrollSpeed || 0) * 1000),
                    drift: Math.round(s.drift * 10) / 10, lock: s.locked ? 1 : 0,
                    min: Math.round(lim.min), max: Math.round(lim.max),
                    cy: Math.round((r.top - lr.top) * 10) / 10, ch: Math.round(r.height), cl: list.clientHeight });
            }
        };
        let transformSampleQueued = false;
        const prevOnTransform = sc.onTransform;
        sc.onTransform = () => {
            if (prevOnTransform) prevOnTransform();
            if (transformSampleQueued) return;
            transformSampleQueued = true;
            queueMicrotask(() => { transformSampleQueued = false; sample(true); });
        };
        const frame = () => { if (!on) return; sample(false); requestAnimationFrame(frame); };
        requestAnimationFrame(frame);
        window.__vlt = { list, rows, events, stop() {
            sc.onTransform = prevOnTransform; on = false;
            for (const type of ['touchstart', 'touchmove', 'touchend', 'touchcancel']) document.removeEventListener(type, onTouch, { capture: true }); } };
        return true;
    };
    // The list is created after load, and re-created on navigation; keep trying until it is there,
    // and re-arm whenever the one being recorded goes away.
    const loop = () => { arm(); setTimeout(loop, 500); };
    loop();
})();
