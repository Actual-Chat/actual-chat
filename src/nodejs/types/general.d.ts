interface EmscriptenLoaderOptions {
    locateFile: (filename: string) => string;
    wasmBinary: ArrayBuffer;
}
