# Migrate TypeScript `moduleResolution` from `node` to `bundler`

## Goal

Switch the project's TypeScript module resolution from the legacy `"node"` algorithm to `"bundler"` so that TS honors `package.json` `exports` subpaths (e.g. `'onnxruntime-web/wasm'`) — matching what the runtime bundler already does.

## Motivation

Currently `tsconfig.json` uses:

```json
"module": "es2020",
"moduleResolution": "node"
```

`moduleResolution: "node"` is the legacy Node.js resolution algorithm and **does not read** the `exports` field in `package.json`. It only does filesystem lookups under `node_modules/<pkg>/<subpath>.{ts,d.ts,...}`.

This means modern packages that publish only via `exports` are invisible to TS even though they work fine at runtime.

### Concrete symptom

In `src/dotnet/UI.Blazor.App/Components/AudioRecorder/workers/audio-vad.ts`:

```ts
import * as ort from 'onnxruntime-web/wasm';
```

produces:

```
TS2307: Cannot find module 'onnxruntime-web/wasm' or its corresponding type declarations.
```

even though `onnxruntime-web@1.24.1` correctly publishes `./wasm` via:

```json
"./wasm": {
  "types": "./types.d.ts",
  "import": { "default": "./dist/ort.wasm.bundle.min.mjs" },
  "require": "./dist/ort.min.js"
}
```

The runtime bundler (esbuild/webpack) honors this and ships the lean WASM-only build (`ort.wasm.bundle.min.mjs`, smaller than the full bundle that includes WebGL/WebGPU EPs). Only TS type-checking is broken.

A workaround shim was added at `src/nodejs/types/modules.d.ts`:

```ts
declare module 'onnxruntime-web/wasm' {
    export * from 'onnxruntime-web';
}
```

This unblocks type-checking but leaves the underlying tooling mismatch in place. Future similar imports will hit the same wall.

## Why `"bundler"`

| Option | `exports` aware? | Forces `.js` extensions on relative imports? | Fits this project? |
|---|---|---|---|
| `node` (current) | No | No | Causes the bug |
| `node16` / `nodenext` | Yes | **Yes** | Too disruptive — would require editing every relative import in the codebase |
| `bundler` | Yes | No | Designed for this scenario (TS source compiled and fed to esbuild/webpack/vite) |

`"bundler"` is purpose-built for codebases like this: TypeScript source that's bundled for the browser. It mirrors what the bundler does at runtime, without forcing a sweeping migration to explicit `.js` extensions.

Requirements:
- TypeScript ≥ 5.0 (project already satisfies this).
- `module` must be set to `"esnext"` or `"preserve"` — the current `"es2020"` is incompatible with `"bundler"` and TS will error out otherwise.

## Plan

### 1. Update `tsconfig.json`

```diff
-    "moduleResolution": "node",
+    "moduleResolution": "bundler",
-    "module": "es2020",
+    "module": "esnext",
```

Other compiler options stay as-is.

### 2. Remove the workaround shim

In `src/nodejs/types/modules.d.ts`, delete:

```ts
declare module 'onnxruntime-web/wasm' {
    export * from 'onnxruntime-web';
}
```

The original import in `audio-vad.ts:7` should now resolve natively.

### 3. Verify

Run from the project root:

```bash
npm run build:Verify
```

This runs `tsc --noEmit`, `eslint`, and the debug build. Expect the `audio-vad.ts` error to be gone and zero new errors.

### 4. Triage any latent issues

`bundler` resolution is stricter than `node` in a few places. Likely surfacing patterns:

- Imports that worked under `node` because of loose path resolution but were technically wrong (e.g. importing internal package files not in `exports`). Fix by importing from the public entry point.
- `import` of a CJS module that lacks proper `default` export — usually fixed by the `esModuleInterop: true` we already have.
- `paths` mapping ambiguities. The current `tsconfig.json` has a `*` fallback that maps to several roots; if `bundler` complains, narrow the mapping to the specific subpath.

Each issue should be a small, isolated fix — no broad rewrites expected.

### 5. (Optional, follow-up) Audit other commented-out `onnxruntime-web` imports

`src/dotnet/UI.Blazor.App/Services/Video/tensor-utils.ts` and
`src/dotnet/UI.Blazor.App/Services/Video/workers/video-processing.ts`
have commented `import * as ort from 'onnxruntime-web'` lines. When/if image segmentation is re-enabled, consider switching these to `'onnxruntime-web/wasm'` too for the smaller bundle, now that the type checker accepts the subpath.

## Non-goals

- Switching to `"node16"`/`"nodenext"`.
- Reorganizing the `paths` config or `rootDirs`.
- Changing the runtime bundler configuration — it already does the right thing.

## Risk

Low. The change is two lines in `tsconfig.json` plus removing a 3-line shim. The runtime build is unaffected because it does not consult `tsconfig.json` for resolution. If the verify step surfaces unexpected errors, the change is fully reversible by restoring the two settings and the shim.
