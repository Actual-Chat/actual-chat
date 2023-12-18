import { OpusMediaRecorder } from "../../../Audio.UI.Blazor/Components/AudioRecorder/opus-media-recorder";

export class VideoTestPage {

    private static currentStream?: MediaStream = null;
    private static codec?: VideoCodec = null;
    private static coder?: VideoEncoder = null;
    private static offscreenCanvas: OffscreenCanvas = null;


    public static async startVideoCapture(): Promise<void> {
        const stream = await navigator.mediaDevices.getUserMedia({ video: true });
        const videoElement = document.getElementById('video') as HTMLVideoElement;
        const canvasElement = document.getElementById('canvas') as HTMLCanvasElement;
        videoElement.srcObject = stream;
        await videoElement.play();
        canvasElement.width = videoElement.videoWidth;
        canvasElement.height = videoElement.videoHeight;

        VideoTestPage.currentStream = stream;

        // Create an OffscreenCanvas and a 2D rendering context
        const videoTrack = stream.getVideoTracks()[0];

        // videoTrack.requestFrame();
        // stream.req
        // const frame = new VideoFrame(VideoTestPage.offscreenCanvas);
        // const canvas = VideoTestPage.offscreenCanvas = new OffscreenCanvas(videoElement.videoWidth, videoElement.videoHeight);
        const canvas = VideoTestPage.offscreenCanvas ?? ( VideoTestPage.offscreenCanvas = canvasElement.transferControlToOffscreen());
        const renderer = new WebGLRenderer(canvas);
        const coder = VideoTestPage.coder = new VideoEncoder({
            output: (chunk: EncodedVideoChunk, metadata: EncodedVideoChunkMetadata) => {
                console.log('CHUNK', chunk.byteLength, chunk, metadata);
            },
            error:(error: DOMException) => {
                console.warn('Error encoding', error);
            }
        });
        const coderConfig = {
            codec: 'av01.0.15M.10',
            height: canvas.height,
            width: canvas.width,
            hardwareAcceleration: 'prefer-hardware',
        } as VideoEncoderConfig;
        const encoderSupported = await VideoEncoder.isConfigSupported(coderConfig);
        if (!encoderSupported.supported) {
            console.warn('NOT SUPPORTED CODEC', coderConfig);
            coderConfig.codec = 'avc1.4d401f';
        }

        coder.configure(coderConfig);

        // const ctx: WebGL2RenderingContext  = VideoTestPage.offscreenCanvas.getContext('webgl2') as WebGL2RenderingContext;

        // const ctx: OffscreenCanvasRenderingContext2D = canvas.getContext('2d') as OffscreenCanvasRenderingContext2D;
        // ctx.getImageData()
        // coder.encode()
        // coder.encode()
        // canvas.capt
        // Video rendering function
        const renderVideo: VideoFrameRequestCallback = (now, metadata) => {
            // ctx.drawImage(videoElement, 0, 0, videoElement.videoWidth, videoElement.videoHeight);
            renderer.draw(videoElement);
            coder.encode(new VideoFrame(canvasElement, { timestamp: metadata.mediaTime }));
            videoElement.requestVideoFrameCallback(renderVideo);
        };

        // Request the next video frame
        videoElement.requestVideoFrameCallback(renderVideo);
    }

    public static async stopVideoCapture(): Promise<void> {
        const videoElement = document.getElementById('video') as HTMLVideoElement;
        videoElement.srcObject = null;
        videoElement.pause();
        await OpusMediaRecorder.stopStreamTracks(VideoTestPage.currentStream);
    }
}

export class VideoCodec {
    private encodedCallback: () => void;
    private frameCounter: number;
    private seqNo: number;
    private keyframeIndex: number;
    private deltaframeIndex: number;
    private encoder: VideoEncoder;
    public transformStream: TransformStream<VideoFrame, EncodedVideoChunk>;

    constructor() {
        this.transformStream = new TransformStream(
            {
                // Code from the original class definition goes here...
            }
        );
    }
}

class WebGLRenderer {
    private readonly canvas: OffscreenCanvas = null;
    private readonly ctx: WebGL2RenderingContext = null;

    static vertexShaderSource = `
    attribute vec2 xy;

    varying highp vec2 uv;

    void main(void) {
      gl_Position = vec4(xy, 0.0, 1.0);
      // Map vertex coordinates (-1 to +1) to UV coordinates (0 to 1).
      // UV coordinates are Y-flipped relative to vertex coordinates.
      uv = vec2((1.0 + xy.x) / 2.0, (1.0 - xy.y) / 2.0);
    }
  `;

    static fragmentShaderSource = `
    varying highp vec2 uv;

    uniform sampler2D texture;

    void main(void) {
      gl_FragColor = texture2D(texture, uv);
    }
  `;

    constructor(canvas: OffscreenCanvas) {
        this.canvas = canvas;
        const gl = this.ctx = canvas.getContext('webgl2') as WebGL2RenderingContext;

        const vertexShader = gl.createShader(gl.VERTEX_SHADER);
        gl.shaderSource(vertexShader, WebGLRenderer.vertexShaderSource);
        gl.compileShader(vertexShader);
        if (!gl.getShaderParameter(vertexShader, gl.COMPILE_STATUS)) {
            throw gl.getShaderInfoLog(vertexShader);
        }

        const fragmentShader = gl.createShader(gl.FRAGMENT_SHADER);
        gl.shaderSource(fragmentShader, WebGLRenderer.fragmentShaderSource);
        gl.compileShader(fragmentShader);
        if (!gl.getShaderParameter(fragmentShader, gl.COMPILE_STATUS)) {
            throw gl.getShaderInfoLog(fragmentShader);
        }

        const shaderProgram = gl.createProgram();
        gl.attachShader(shaderProgram, vertexShader);
        gl.attachShader(shaderProgram, fragmentShader);
        gl.linkProgram (shaderProgram );
        if (!gl.getProgramParameter(shaderProgram, gl.LINK_STATUS)) {
            throw gl.getProgramInfoLog(shaderProgram);
        }
        gl.useProgram(shaderProgram);

        // Vertex coordinates, clockwise from bottom-left.
        const vertexBuffer = gl.createBuffer();
        gl.bindBuffer(gl.ARRAY_BUFFER, vertexBuffer);
        gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([
            -1.0, -1.0,
            -1.0, +1.0,
            +1.0, +1.0,
            +1.0, -1.0
        ]), gl.STATIC_DRAW);

        const xyLocation = gl.getAttribLocation(shaderProgram, "xy");
        gl.vertexAttribPointer(xyLocation, 2, gl.FLOAT, false, 0, 0);
        gl.enableVertexAttribArray(xyLocation);

        // Create one texture to upload frames to.
        const texture = gl.createTexture();
        gl.bindTexture(gl.TEXTURE_2D, texture);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
        gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    }

    draw(frame: TexImageSource) {
        // this.canvas.width = frame.displayWidth;
        // this.canvas.height = frame.displayHeight;

        const gl = this.ctx;

        // Upload the frame.
        gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, frame);
        // frame.close();

        // Configure and clear the drawing area.
        gl.viewport(0, 0, gl.drawingBufferWidth, gl.drawingBufferHeight);
        gl.clearColor(1.0, 0.0, 0.0, 1.0);
        gl.clear(gl.COLOR_BUFFER_BIT);

        // Draw the frame.
        gl.drawArrays(gl.TRIANGLE_FAN, 0, 4);
    }
};
