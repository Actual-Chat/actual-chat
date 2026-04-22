/**
 * Vitest globalSetup for E2E tests.
 *
 * AC_E2E_SERVER env var:
 * - "auto" (default): probe health; use the existing server if reachable, otherwise
 *   start a managed one. Handles both Docker (host watch server already up) and
 *   direct host runs (no server yet).
 * - "external": verify server is reachable, warn if not
 * - "managed": start pre-built server, wait for health, stop on teardown
 *
 * Managed mode expects dotnet build + npm build done already (uses --no-build).
 */

import { type ChildProcess, spawn } from 'child_process';
import * as fs from 'fs';
import * as path from 'path';

process.env.NODE_TLS_REJECT_UNAUTHORIZED ??= '0';

let serverProcess: ChildProcess | null = null;

function loadEnvFile(): Record<string, string> {
    const envPath = path.resolve(process.cwd(), '.env');
    const result: Record<string, string> = {};
    if (fs.existsSync(envPath)) {
        for (const line of fs.readFileSync(envPath, 'utf-8').split('\n')) {
            const m = /^([^#=]+)=(.+)$/.exec(line);
            if (m) result[m[1].trim()] = m[2].trim();
        }
    }
    return result;
}

const envFile = loadEnvFile();
const baseUrl = (process.env.HostSettings__BaseUri ?? envFile['HostSettings__BaseUri']) || 'https://local.voxt.ai';
const probeUrl = `${baseUrl}/healthz/live`;

async function waitForHealth(url: string, timeoutMs: number): Promise<void> {
    const start = Date.now();
    while (Date.now() - start < timeoutMs) {
        try {
            const resp = await fetch(url, { signal: AbortSignal.timeout(3000) });
            if (resp.ok) return;
        } catch { /* not ready */ }
        await new Promise(r => setTimeout(r, 1000));
    }
    throw new Error(`Server did not become healthy at ${url} within ${timeoutMs / 1000}s`);
}

async function isHealthy(url: string, timeoutMs = 3000): Promise<boolean> {
    try {
        const resp = await fetch(url, { signal: AbortSignal.timeout(timeoutMs) });
        return resp.ok;
    } catch {
        return false;
    }
}

export async function setup() {
    let mode = (process.env.AC_E2E_SERVER ?? 'auto').toLowerCase();

    if (mode === 'auto') {
        mode = await isHealthy(probeUrl) ? 'external' : 'managed';
        console.log(`[e2e] AC_E2E_SERVER=auto → ${mode}`);
    }

    if (mode === 'managed') {
        console.log(`[e2e] Starting managed server at ${baseUrl}...`);

        const logDir = path.join(process.cwd(), 'tmp');
        if (!fs.existsSync(logDir)) fs.mkdirSync(logDir, { recursive: true });
        const logStream = fs.createWriteStream(path.join(logDir, 'e2e-server.log'));

        serverProcess = spawn('dotnet', ['run', '--no-build', '--project', 'src/dotnet/App.Server'], {
            env: {
                ...process.env,
                ASPNETCORE_ENVIRONMENT: 'Development',
            },
            stdio: ['ignore', 'pipe', 'pipe'],
            detached: true,
        });
        serverProcess.stdout?.pipe(logStream);
        serverProcess.stderr?.pipe(logStream);
        serverProcess.on('exit', (code) => {
            if (code !== null && code !== 0)
                console.error(`[e2e] Server exited with code ${code}`);
        });

        await waitForHealth(probeUrl, 120_000);
        console.log(`[e2e] Server is ready`);
    } else {
        if (await isHealthy(probeUrl, 5000))
            console.log(`[e2e] Server is reachable at ${baseUrl}`);
        else
            console.warn(`[e2e] Server at ${baseUrl} is not reachable — tests may fail`);
    }

    // Warm up the SPA root so the first test doesn't pay cold-start cost (Blazor JIT,
    // DB seeding, etc). /healthz/live returns long before the app is fully rendered.
    try {
        await fetch(baseUrl, { signal: AbortSignal.timeout(30_000) });
        console.log(`[e2e] SPA warmed up`);
    } catch (e) {
        console.warn(`[e2e] Warmup fetch failed: ${(e as Error).message}`);
    }
}

export async function teardown() {
    if (!serverProcess?.pid) return;

    console.log('[e2e] Stopping managed server...');
    const pid = serverProcess.pid;
    serverProcess = null;

    try { process.kill(-pid, 'SIGTERM'); } catch { /* already dead */ }
    await new Promise(r => setTimeout(r, 2000));
    try { process.kill(-pid, 'SIGKILL'); } catch { /* already dead */ }
}
