import { InfiniteList } from '../../../src/dotnet/UI.Blazor/Components/VirtualList/infinite-list';
import { VirtualListEdge } from '../../../src/dotnet/UI.Blazor/Components/VirtualList/ts/virtual-list-edge';
import {
    VirtualListRenderDirection,
} from '../../../src/dotnet/UI.Blazor/Components/VirtualList/ts/virtual-list-render-direction';
import type {
    VirtualListRenderState,
} from '../../../src/dotnet/UI.Blazor/Components/VirtualList/ts/virtual-list-render-state';
import type { DotNet } from '@microsoft/dotnet-js-interop';
import { Range } from '../../../src/dotnet/UI.Blazor/Components/VirtualList/ts/range';

export function createList() {
    const root = document.createElement('div');
    root.className = 'virtual-list infinite-list';
    root.style.cssText = 'display:flex;height:600px;overflow-y:scroll;overflow-anchor:none;contain:strict;';
    root.innerHTML = '<div class="data render-index" data-render-index="0" style="display:none"></div>'
        + '<div class="data render-state" style="display:none"></div>'
        + '<div class="c-wrapper" style="position:relative;flex:none;width:100%;min-height:100%">'
        + '<ul class="c-virtual-container" style="position:absolute;display:flex;flex-direction:column;'
        + 'width:100%;margin:0;padding:0;list-style:none;overflow-anchor:none">'
        + '<li class="c-spacer-start" style="flex:none"></li>'
        + '<li class="c-end-anchor" style="height:48px;flex:none"></li>'
        + '<li class="c-spacer-end" style="flex:none"></li></ul></div>';
    const container = root.querySelector('.c-virtual-container')!;
    const end = root.querySelector('.c-end-anchor')!;
    for (let key = 1; key <= 30; key++) {
        const item = document.createElement('li');
        item.className = 'item';
        item.dataset.key = String(key);
        item.style.cssText = 'height:120px;flex:none;';
        item.textContent = String(key);
        container.insertBefore(item, end);
    }
    const initial: VirtualListRenderState = {
        renderIndex: 0,
        keyRange: new Range('1', '30'),
        count: 30,
        beforeCount: 0,
        afterCount: 0,
        estimatedCount: 30,
        hasVeryFirstItem: true,
        hasVeryLastItem: true,
        scrollToKey: '30',
    };
    root.querySelector('.render-state')!.textContent = JSON.stringify(initial);
    document.body.append(root);
    const calls: { method: string; args: unknown[] }[] = [];
    const backend = {
        invokeMethodAsync: (method: string, ...args: unknown[]) => {
            calls.push({ method, args });
            return Promise.resolve();
        },
    } as unknown as DotNet.DotNetObject;
    const list = InfiniteList.create(
        root, backend, 'regression', VirtualListEdge.End, VirtualListRenderDirection.Reverse, false,
        1500, 2, 5);
    return {
        list, root, initial, calls,
        navigate(key: string) {
            list['applyRenderIntent']({ ...initial, scrollToKey: key, scrollToKeyInTheMiddle: true });
        },
        position(key: string) {
            return root.querySelector(`[data-key="${key}"]`)!.getBoundingClientRect().top;
        },
    };
}

export async function nextFrame(): Promise<void> {
    await new Promise<void>(resolve => requestAnimationFrame(() => resolve()));
}
