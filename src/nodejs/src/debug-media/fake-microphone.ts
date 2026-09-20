import { registerDebugSurface } from 'debug-surface';

/*
 * `debugUI.fake.mic` - a microphone the debug harness speaks into. Once enabled,
 * `getUserMedia({ audio })` returns a stream fed by decoded clips instead of the real
 * device, so recording, VAD and transcription run for real without anyone dictating.
 * Nothing is replaced until `enable()` is called, so a developer's own mic keeps working.
 */

/** What `debugUI.fake.mic.getState()` reports. */
export interface FakeMicrophoneState {
    isEnabled: boolean;
    contextState: AudioContextState | 'none';
    hasStream: boolean;
    // How many times getUserMedia has handed out the fake stream
    streamRequests: number;
    clips: string[];
}

const clips = new Map<string, AudioBuffer>();
let context: AudioContext | null = null;
let destination: MediaStreamAudioDestinationNode | null = null;
let source: AudioBufferSourceNode | null = null;
let isEnabled = false;
let streamRequests = 0;

export function initFakeMicrophoneDebugConsole(): void {
    registerDebugSurface('fake', {
        mic: { enable, load, say, stop, getState },
    });
}

// Private methods

function enable(): void {
    if (isEnabled)
        return;

    const mediaDevices = navigator.mediaDevices;
    const getUserMedia = mediaDevices.getUserMedia.bind(mediaDevices) as MediaDevices['getUserMedia'];
    const enumerateDevices = mediaDevices.enumerateDevices.bind(mediaDevices) as MediaDevices['enumerateDevices'];
    mediaDevices.getUserMedia = async (constraints?: MediaStreamConstraints): Promise<MediaStream> => {
        if (!constraints?.audio)
            return getUserMedia(constraints);

        // A new destination per call: the permission check stops the tracks it's given,
        // and that must not end the stream the recorder asks for next
        const audioContext = await getContext();
        destination = audioContext.createMediaStreamDestination();
        streamRequests++;
        if (!constraints.video)
            return destination.stream;

        const video = await getUserMedia({ video: constraints.video });
        return new MediaStream([...destination.stream.getAudioTracks(), ...video.getVideoTracks()]);
    };
    mediaDevices.enumerateDevices = async (): Promise<MediaDeviceInfo[]> => {
        const devices = await enumerateDevices().catch(() => [] as MediaDeviceInfo[]);
        // The recorder looks for the 'default' input and the permission check for a non-empty id
        return devices.some(x => x.kind === 'audioinput' && x.deviceId === 'default')
            ? devices
            : [...devices, fakeInputDevice()];
    };
    isEnabled = true;
}

async function load(name: string, base64: string): Promise<number> {
    const audioContext = await getContext();
    const binary = atob(base64);
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++)
        bytes[i] = binary.charCodeAt(i);

    const buffer = await audioContext.decodeAudioData(bytes.buffer);
    clips.set(name, buffer);
    return buffer.duration;
}

/** Plays a loaded clip into the stream the recorder holds; resolves with its duration once it ends. */
async function say(name: string): Promise<number> {
    const buffer = clips.get(name);
    if (!buffer)
        throw new Error(`debugUI.fake.mic: no clip named '${name}' - load it first.`);
    if (!destination)
        throw new Error('debugUI.fake.mic: nothing has asked for the microphone yet - start recording first.');

    const audioContext = await getContext();
    stop();
    const node = audioContext.createBufferSource();
    node.buffer = buffer;
    node.connect(destination);
    source = node;
    return new Promise<number>(resolve => {
        node.onended = () => {
            if (source === node)
                source = null;
            resolve(buffer.duration);
        };
        node.start();
    });
}

function stop(): void {
    if (!source)
        return;

    try {
        source.stop();
    }
    catch {
        // Already stopped
    }
    source = null;
}

function getState(): FakeMicrophoneState {
    return {
        isEnabled,
        contextState: context?.state ?? 'none',
        hasStream: destination !== null,
        streamRequests,
        clips: [...clips.keys()],
    };
}

async function getContext(): Promise<AudioContext> {
    context ??= new AudioContext();
    if (context.state === 'suspended')
        await context.resume().catch(() => undefined);
    return context;
}

function fakeInputDevice(): MediaDeviceInfo {
    const info = {
        deviceId: 'default',
        groupId: 'fake-microphone',
        kind: 'audioinput' as MediaDeviceKind,
        label: 'Fake microphone',
    };
    return { ...info, toJSON: () => info };
}
