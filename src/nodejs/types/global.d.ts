declare global {

    interface AudioWorkletProcessor {
        // https://developer.mozilla.org/en-US/docs/Web/API/AudioWorkletProcessor/process
        process(inputs: Float32Array[][], outputs: Float32Array[][], parameters: Record<string, Float32Array>): boolean;
        readonly port: MessagePort;
        // https://developer.mozilla.org/en-US/docs/Web/API/AudioWorkletProcessor/AudioWorkletProcessor
        // eslint-disable-next-line @typescript-eslint/no-misused-new
        new(options?: AudioWorkletNodeOptions): AudioWorkletProcessor;
    }

    // File System Access API (not yet in standard lib)
    // https://developer.mozilla.org/en-US/docs/Web/API/File_System_Access_API

    type FileSystemPermissionMode = 'read' | 'readwrite';

    interface FileSystemHandlePermissionDescriptor {
        mode?: FileSystemPermissionMode;
    }

    interface FileSystemHandle {
        queryPermission(descriptor?: FileSystemHandlePermissionDescriptor): Promise<PermissionState>;
        requestPermission(descriptor?: FileSystemHandlePermissionDescriptor): Promise<PermissionState>;
    }

    interface OpenFilePickerOptions {
        multiple?: boolean;
        excludeAcceptAllOption?: boolean;
        types?: FilePickerAcceptType[];
        startIn?: FileSystemHandle | string;
    }

    interface FilePickerAcceptType {
        description?: string;
        accept: Record<string, string | string[]>;
    }
}

export { };
