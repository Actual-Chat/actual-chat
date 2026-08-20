// A conversation's collapsed header renders for two different reasons: the user collapsed the block,
// or the block simply came into view. Only the first should stop the list animating item heights - a
// collapse tears the expanded items out, and what replaces them lands on keys the list last saw
// somewhere else, which its diff reads as an appearance and grows in from nothing.
//
// The toggle arms the conversation on click, which is the only point early enough: the list decides
// appearances synchronously with the render, before any observer sees the new markup. The collapsed
// header's render script - which does run inside that render pass - spends the arm.

import { delayAsync } from 'actuallab-core';
import { InfiniteList } from '../../UI.Blazor/Components/VirtualList/infinite-list';

const ArmAttribute = 'data-collapse-arm';
const QuietMs = 200;
// A click that never reaches a render must not leave an arm behind for the next appearance to spend.
const ArmTtlMs = 1000;

const armed = new Map<string, number>();

document.addEventListener('click', (event: Event) => {
    const target = event.target as HTMLElement | null;
    const id = target?.closest<HTMLElement>(`[${ArmAttribute}]`)?.getAttribute(ArmAttribute);
    if (id)
        armed.set(id, performance.now());
}, { capture: true, passive: true });

InfiniteList.registerRenderScript('collapsed-header', (list, _element, value) => {
    const armedAt = armed.get(value);
    if (armedAt === undefined)
        return;

    armed.delete(value);
    if (performance.now() - armedAt > ArmTtlMs)
        return;

    list.suspendHeightAnimationsUntil(delayAsync(QuietMs));
});
