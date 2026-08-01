import * as esbuild from 'esbuild';
import path from 'path';
import postcssPlugin from '@chialab/esbuild-plugin-postcss';
import * as fs from 'node:fs';
import readline from 'node:readline';

console.time('build');
const isProduction = process.argv.slice(2).includes('--production');
const isWatch = process.argv.slice(2).includes('--watch');
const mustAnalyze = process.argv.slice(2).includes('--analyze');
process.env.NODE_ENV = isProduction ? 'production' : 'development';

const outputPath = path.normalize(path.resolve(import.meta.dirname, './src/dotnet/App.Wasm/wwwroot/dist'));
const mauiOutputPath = path.normalize(path.resolve(import.meta.dirname, './src/dotnet/App.Maui/wwwroot/dist'));

async function copyAssets() {
    await fs.promises.mkdir(`${outputPath}/config`, { recursive: true });
    if (fs.existsSync('./firebase.config.json'))
        // only for local-dev build
        await fs.promises.copyFile('./firebase.config.json', `${outputPath}/config/firebase.config.js`, );
    // images/unused holds assets no code references anymore - kept in the repo, published nowhere.
    // images/webonly is published, but App.Maui.csproj drops it from the app packages.
    await fs.promises.cp('./src/nodejs/images', `${outputPath}/images`, {
        recursive: true,
        filter: (src) => path.basename(src) !== 'unused',
    });
    await fs.promises.cp('./resources/sounds/converted', `${outputPath}/sounds`, {
        recursive: true,
        filter: (src) => {
            const ext = path.extname(src).toLowerCase();
            return ext === '.webm' || ext === '.m4a' || fs.statSync(src).isDirectory();
        },
    });
}

await fs.promises.rm(outputPath, { recursive: true, force: true });
await fs.promises.rm(mauiOutputPath, { recursive: true, force: true });
await copyAssets();

const options = {
    entryPoints: [
        { out: 'bundle', in: './src/nodejs/index.ts' },
        { out: 'sw', in: './src/dotnet/UI.Blazor/ServiceWorkers/service-worker.ts' },
        { out: 'opusDecoderWorker', in: './src/dotnet/UI.Blazor.App/Components/AudioPlayer/workers/opus-decoder-worker-bootstrap.ts' },
        { out: 'opusEncoderWorker', in: './src/dotnet/UI.Blazor.App/Components/AudioRecorder/workers/opus-encoder-worker-bootstrap.ts' },
        { out: 'vadWorker', in: './src/dotnet/UI.Blazor.App/Components/AudioRecorder/workers/audio-vad-worker-bootstrap.ts' },
        { out: 'onDeviceAwakeWorker', in: './src/nodejs/src/on-device-awake-worker-bootstrap.ts' },
        { out: 'warmUpWorklet', in: './src/nodejs/src/worklets/warm-up-worklet-processor.ts' },
        { out: 'feederWorklet', in: './src/dotnet/UI.Blazor.App/Components/AudioPlayer/worklets/feeder-audio-worklet-processor.ts' },
        { out: 'opusEncoderWorklet', in: './src/dotnet/UI.Blazor.App/Components/AudioRecorder/worklets/opus-encoder-worklet-processor.ts' },
        { out: 'vadWorklet', in: './src/dotnet/UI.Blazor.App/Components/AudioRecorder/worklets/audio-vad-worklet-processor.ts' },
        { out: 'videoPlayerWorker', in: './src/dotnet/UI.Blazor.App/Services/Video/playback/player-worker-bootstrap.ts' },
        { out: 'videoRecorderWorker', in: './src/dotnet/UI.Blazor.App/Services/Video/sender/recorder-worker-bootstrap.ts' },
    ],
    bundle: true,
    platform: 'browser',
    format: 'esm',
    external: ['module', 'worker_threads'], // Don't try to bundle these
    target: 'es2022',
    // splitting: true,
    treeShaking: true,
    minify: isProduction,
    metafile: mustAnalyze,
    // Production ships no .map files - they were ~20MB raw / 4.8MB compressed in the Android APK,
    // and nothing reads them at runtime. keepNames pays ~1% of bundle size to keep class and
    // function identifiers through minification, so a raw stack trace still names its frames.
    // Switch to 'external' + a sentry-cli upload if we ever want line numbers back.
    keepNames: true,
    sourcemap: !isProduction,
    outdir: outputPath,
    tsconfig: './tsconfig.json',
    nodePaths: ['./node_modules'],
    assetNames: "assets/[ext]/[name]",
    loader: {
        '.css': 'css',
        '.eot': 'file',
        '.woff': 'file',
        '.woff2': 'file',
        '.ttf': 'file',
        '.otf': 'file',
        '.svg': 'file',
        '.wasm': 'file',
        '.wasm.map': 'file',
        '.jsep.mjs': 'file',
        '.onnx': 'file',
        '.ort': 'file',
    },
    plugins: [
        postcssPlugin(),
        {
            // Treat only this specific MJS as a file, keep all other .mjs as JS
            name: 'ort-wasm-simd-mjs-as-file',
            setup(build) {
                const target = path.normalize(
                    path.resolve(import.meta.dirname, 'src/dotnet/UI.Blazor.App/Components/AudioRecorder/workers/ort-wasm-simd.mjs')
                );
                build.onLoad({ filter: /\.mjs$/ }, async (args) => {
                    // Only intercept the exact file; let others proceed
                    if (path.normalize(args.path) !== target) return null;
                    const contents = await fs.promises.readFile(args.path);
                    return {
                        contents,
                        loader: 'file',
                    };
                });
            },
        },
    ],
};

// build
if (isWatch) {
    const ctx = await esbuild.context(options);
    await ctx.watch();

    // Watch .razor/.cshtml files for Tailwind class changes.
    // The postcss-watch-plugin emits dir-dependency messages for these,
    // but @chialab/esbuild-plugin-postcss ignores dir-dependency (only handles dependency).
    // So we watch them directly and trigger a rebuild when they change.
    const razorDirs = fs.readdirSync('./src/dotnet/', { withFileTypes: true })
        .filter(d => d.isDirectory() && d.name.includes('UI.Blazor'))
        .map(d => `./src/dotnet/${d.name}`)
        .concat('./src/dotnet/App.Server');
    let rebuildTimer = null;
    for (const dir of razorDirs) {
        fs.watch(dir, { recursive: true }, (_eventType, filename) => {
            if (filename?.endsWith('.razor') || filename?.endsWith('.cshtml')) {
                // Debounce rapid changes (e.g., save-all)
                clearTimeout(rebuildTimer);
                rebuildTimer = setTimeout(() => ctx.rebuild().catch(() => {}), 100);
            }
        });
    }

    // Manual rebuild on keypress — useful when FS change notifications don't
    // propagate (e.g. files edited from inside Docker/WSL while watcher runs on Windows host).
    if (process.stdin.isTTY) {
        readline.emitKeypressEvents(process.stdin);
        process.stdin.setRawMode(true);
        process.stdin.resume();
        const runRebuild = async (label, extra) => {
            const t0 = Date.now();
            console.log(`[${label}] starting...`);
            try {
                if (extra) await extra();
                await ctx.rebuild();
                console.log(`[${label}] done in ${Date.now() - t0}ms`);
            } catch (e) {
                console.error(`[${label}] failed:`, e?.message ?? e);
            }
        };
        process.stdin.on('keypress', (_str, key) => {
            if (key.ctrl && key.name === 'c') process.exit(0);
            if (key.name === 'r' || key.name === 'space') runRebuild('rebuild');
            else if (key.name === 'f') runRebuild('full rebuild', copyAssets);
        });
        console.log('Watching... Press R / Space for rebuild, F for full rebuild + asset copy, Ctrl+C to quit.');
    }
}
else {
    console.log('Building, mode:', isProduction ? 'production' : 'development');
    const result = await esbuild.build(options);
    await fs.promises.cp(outputPath, mauiOutputPath, { recursive: true });
    // required sounds must be bundled explicitly
    await fs.promises.rm(`${mauiOutputPath}/sounds`, { recursive: true, force: true });
    if (mustAnalyze)
        console.log(await esbuild.analyzeMetafile(result.metafile, {
            verbose: true,
        }));
}
console.timeEnd('build');
