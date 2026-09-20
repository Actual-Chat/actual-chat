import * as fs from 'fs';
import * as path from 'path';
import type { Page } from 'playwright';
import type { DebugUIRoot } from './debug-ui-api';
import { clickRecorder, getRecorderState } from './page-state';

// npm scripts run from the repo root, the same base tests/ts/e2e/helpers.ts reads .env from
const ClipFolder = path.resolve(process.cwd(), 'tests/Transcription.IntegrationTests/data');

/**
 * Speech clips the fake microphone can say, by name. They are the transcription tests'
 * own recordings - Opus in WebM, which Chrome decodes as is.
 */
export const DefaultClips: Readonly<Record<string, string>> = {
    one: path.join(ClipFolder, '1.webm'),
    two: path.join(ClipFolder, '2.webm'),
    three: path.join(ClipFolder, '3.webm'),
    four: path.join(ClipFolder, '4.webm'),
    ay: path.join(ClipFolder, '0000-AY.webm'),
    ak: path.join(ClipFolder, '0004-AK.webm'),
};

/** Replaces the page's microphone and loads the clips; returns each clip's duration in seconds. */
export async function enableMicrophone(
    page: Page,
    clips: Readonly<Record<string, string>> = DefaultClips,
): Promise<Map<string, number>> {
    const hasMic = await page.evaluate(() => !!(globalThis as DebugUIRoot).debugUI?.fake.mic);
    if (!hasMic)
        throw new Error('debugUI.fake.mic is missing - the page is on an older bundle.');

    // A recording restored on page load already holds the real microphone
    await stopRecording(page);
    await page.evaluate(() => (globalThis as DebugUIRoot).debugUI!.fake.mic!.enable());
    const durations = new Map<string, number>();
    for (const [name, filePath] of Object.entries(clips)) {
        const base64 = fs.readFileSync(filePath).toString('base64');
        const duration = await page.evaluate(
            ({ clip, data }) => (globalThis as DebugUIRoot).debugUI!.fake.mic!.load(clip, data),
            { clip: name, data: base64 });
        durations.set(name, duration);
    }
    return durations;
}

/** Plays a clip into the microphone; resolves once it has been said. */
export function say(page: Page, clip: string): Promise<number> {
    return page.evaluate((name: string) => (globalThis as DebugUIRoot).debugUI!.fake.mic!.say(name), clip);
}

/** Starts recording with a real click, which is also what resumes the suspended AudioContexts. */
export async function startRecording(page: Page, timeoutMs = 10_000): Promise<void> {
    if (await getRecorderState(page) === 'on')
        return;

    const getStreamRequests = () => page.evaluate(
        () => (globalThis as DebugUIRoot).debugUI?.fake.mic?.getState().streamRequests ?? 0);
    const streamRequests = await getStreamRequests();
    await clickRecorder(page);
    await waitForRecorderState(page, 'on', timeoutMs);
    // The button turns on before the recorder asks for the microphone, and a clip said
    // before that goes nowhere
    const deadline = Date.now() + timeoutMs;
    while (await getStreamRequests() === streamRequests) {
        if (Date.now() > deadline)
            throw new Error(`The recorder didn't ask for the microphone within ${timeoutMs}ms.`);

        await page.waitForTimeout(100);
    }
    // The permission check may have asked first, and the recorder's own request follows it
    await page.waitForTimeout(500);
}

/** Recording is billed per second, so every scenario that starts it must reach this. */
export async function stopRecording(page: Page, timeoutMs = 10_000): Promise<void> {
    await page.evaluate(() => (globalThis as DebugUIRoot).debugUI?.fake.mic?.stop()).catch(() => undefined);
    if (await getRecorderState(page) !== 'on')
        return;

    await clickRecorder(page);
    await waitForRecorderState(page, 'off', timeoutMs);
}

// Private methods

async function waitForRecorderState(page: Page, state: 'on' | 'off', timeoutMs: number): Promise<void> {
    const deadline = Date.now() + timeoutMs;
    while (Date.now() < deadline) {
        if (await getRecorderState(page) === state)
            return;

        await page.waitForTimeout(200);
    }
    throw new Error(`The recorder didn't turn ${state} within ${timeoutMs}ms.`);
}
