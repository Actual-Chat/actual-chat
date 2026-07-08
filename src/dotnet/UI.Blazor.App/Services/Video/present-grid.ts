import { DeviceInfo } from 'device-info';
import { VIDEO } from 'app-constants';

// Shared present grid: all video surfaces (remote tiles AND the self-preview,
// across worker realms) write on one wall-clock slot grid, so N streams cost
// max(fps) full-screen WebView redraws instead of sum(fps). Anchored to
// performance.timeOrigin so realms agree on slots. Mobile-only — that's where
// each invalidation redraws the whole functor and the device is thermal-bound.

const GRID_EPS_MS = 1;

export function isPresentGridEnabled(): boolean {
    return DeviceInfo.isMobile;
}

export function presentGridMs(): number {
    // Lazy: VIDEO is populated by initAppConstants after module load.
    // eslint-disable-next-line @typescript-eslint/no-unnecessary-condition
    const fps = DeviceInfo.isMobile ? VIDEO?.mobileFrameRate : VIDEO?.frameRate;
    return 1000 / (fps || (DeviceInfo.isMobile ? 24 : 30));
}

// First grid slot at or after `atMs`, in the caller realm's performance.now()
// domain. `atMs` values already on a slot (within GRID_EPS_MS) stay put.
export function alignToPresentGrid(atMs: number): number {
    const gridMs = presentGridMs();
    const abs = performance.timeOrigin + atMs;
    const slot = Math.ceil((abs - GRID_EPS_MS) / gridMs) * gridMs;
    return slot - performance.timeOrigin;
}
