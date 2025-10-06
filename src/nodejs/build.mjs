import * as esbuild from 'esbuild';
import path from 'path';
import postcssPlugin from '@chialab/esbuild-plugin-postcss';
import * as fs from 'node:fs';

console.time('build');
const isProduction = process.argv.slice(2).includes('--production');
const isWatch = process.argv.slice(2).includes('--watch');
const mustAnalyze = process.argv.slice(2).includes('--analyze');
process.env.NODE_ENV = isProduction ? 'production' : 'development';

const outputPath = path.normalize(path.resolve(import.meta.dirname, '../dotnet/App.Wasm/wwwroot/dist'));
const mauiOutputPath = path.normalize(path.resolve(import.meta.dirname, '../dotnet/App.Maui/wwwroot/dist'));

await fs.promises.rm(outputPath, { recursive: true, force: true });
await fs.promises.rm(mauiOutputPath, { recursive: true, force: true });
await fs.promises.mkdir(`${outputPath}/config`, { recursive: true });
if (fs.existsSync('../../firebase.config.json'))
    // only for local-dev build
    await fs.promises.copyFile('../../firebase.config.json', `${outputPath}/config/firebase.config.js`, );
await fs.promises.cp('./images', `${outputPath}/images`, { recursive: true });
await fs.promises.cp('./../dotnet/UI.Blazor/Services/TuneUI/sounds', `${outputPath}/sounds`, { recursive: true });

const options = {
    entryPoints: [
        { out: 'bundle', in: './index.ts' },
        { out: 'sw', in: './../dotnet/UI.Blazor/ServiceWorkers/service-worker.ts' },
        { out: 'opusDecoderWorker', in: './../dotnet/UI.Blazor.App/Components/AudioPlayer/workers/opus-decoder-worker.ts' },
        { out: 'opusEncoderWorker', in: './../dotnet/UI.Blazor.App/Components/AudioRecorder/workers/opus-encoder-worker.ts' },
        { out: 'vadWorker', in: './../dotnet/UI.Blazor.App/Components/AudioRecorder/workers/audio-vad-worker.ts' },
        { out: 'onDeviceAwakeWorker', in: './src/on-device-awake-worker.ts' },
        { out: 'warmUpWorklet', in: './src/worklets/warm-up-worklet-processor.ts' },
        { out: 'feederWorklet', in: './../dotnet/UI.Blazor.App/Components/AudioPlayer/worklets/feeder-audio-worklet-processor.ts' },
        { out: 'opusEncoderWorklet', in: './../dotnet/UI.Blazor.App/Components/AudioRecorder/worklets/opus-encoder-worklet-processor.ts' },
        { out: 'vadWorklet', in: './../dotnet/UI.Blazor.App/Components/AudioRecorder/worklets/audio-vad-worklet-processor.ts' },
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
    sourcemap: true,
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
                    path.resolve(import.meta.dirname, '../dotnet/UI.Blazor.App/Components/AudioRecorder/workers/ort-wasm-simd.mjs')
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

if (isWatch) {
    const ctx = await esbuild.context(options);
    await ctx.watch();
}
else {
    console.log('Building, mode:', isProduction ? 'production' : 'development');
    const result = await esbuild.build(options);
    await fs.promises.cp(outputPath, mauiOutputPath, { recursive: true });
    if (mustAnalyze)
        console.log(await esbuild.analyzeMetafile(result.metafile, {
            verbose: true,
        }));
}
console.timeEnd('build');
