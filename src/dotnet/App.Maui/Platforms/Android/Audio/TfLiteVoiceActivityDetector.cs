using Java.Nio;
using Java.Nio.Channels;
using ActualChat.Audio;
using Xamarin.TensorFlow.Lite;

namespace ActualChat.App.Maui.Audio;

public sealed class TfLiteVoiceActivityDetector(IServiceProvider services)
    : VoiceActivityDetector(services)
{
    private Interpreter? _interpreter;
    private int _lastFrames = -1;           // Track current batch size for ResizeInput

    // Persistent recurrent state/context (flat arrays)
    private readonly float[] _state = new float[2 * 1 * 128];
    private readonly float[] _context = new float[ContextSamples];

    // Pooled buffer for frame input to avoid allocations per call
    private float[]? _frameBuffer; // rented from ArrayPool

    // Reusable ByteBuffers for inputs and outputs to avoid allocations
    private ByteBuffer? _framesInputBuffer;
    private ByteBuffer? _stateInputBuffer;
    private ByteBuffer? _contextInputBuffer;
    private ByteBuffer? _probsOutputBuffer;
    private ByteBuffer? _stateOutputBuffer;
    private ByteBuffer? _contextOutputBuffer;

    // Float views over the buffers above: AsFloatBuffer() leaks an undisposed JNI global ref per call
    private FloatBuffer? _framesInputView;
    private FloatBuffer? _stateInputView;
    private FloatBuffer? _contextInputView;
    private FloatBuffer? _probsOutputView;
    private FloatBuffer? _stateOutputView;
    private FloatBuffer? _contextOutputView;

    // Cached tensor indices to avoid lookup loops per inference
    private int _framesInputIdx = -1;
    private int _stateInputIdx = -1;
    private int _contextInputIdx = -1;
    private int _probsOutputIdx = -1;
    private int _stateOutputIdx = -1;
    private int _contextOutputIdx = -1;

    // Reusable inputs array and outputs dictionary to avoid allocations
    private Java.Lang.Object[]? _inputsArray;
    private readonly Dictionary<int, Java.Lang.Object> _outputsDict = new(3);

    public override bool IsInitialized => _interpreter != null;

    public override Task EnsureInitialized(CancellationToken cancellationToken = default)
    {
        if (IsInitialized) {
            Reset();
            return Task.CompletedTask;
        }

        // Load model with zero-copy MappedByteBuffer from raw resource
        var res = Android.App.Application.Context.Resources!;
        using var afd = res.OpenRawResourceFd(Resource.Raw.vad_batched_fp16);
        using var inputStream = new Java.IO.FileInputStream(afd!.FileDescriptor);
        var channel = inputStream.Channel;
        var modelBuffer = channel!.Map(FileChannel.MapMode.ReadOnly, afd.StartOffset, afd.DeclaredLength);
        var options = new Interpreter.Options()
            .SetNumThreads(1)!
            .SetUseXNNPACK(true); // Accelerate CPU fallback ops
        _interpreter = new Interpreter(modelBuffer, (Interpreter.Options)options!);
        Log.LogInformation("VAD initialized with XNNPACK");

        // Cache tensor indices
        CacheTensorIndices();

        // Preallocate inputs array (fixed size)
        _inputsArray = new Java.Lang.Object[_interpreter!.InputTensorCount];

        // Ensure clean recurrent state
        Reset();
        return Task.CompletedTask;
    }

    public override void Reset()
    {
        base.Reset();
        Array.Clear(_state, 0, _state.Length);
        Array.Clear(_context, 0, _context.Length);
        _lastFrames = -1;
    }

    protected override void Dispose(bool disposing)
    {
        ReleaseFrameBuffer();
        _interpreter?.Close();
        _interpreter?.Dispose();
        _interpreter = null;

        // Disposed rather than dropped: each holds a JNI global ref until someone releases it.
        DisposeBuffer(ref _framesInputView, ref _framesInputBuffer);
        DisposeBuffer(ref _stateInputView, ref _stateInputBuffer);
        DisposeBuffer(ref _contextInputView, ref _contextInputBuffer);
        DisposeBuffer(ref _probsOutputView, ref _probsOutputBuffer);
        DisposeBuffer(ref _stateOutputView, ref _stateOutputBuffer);
        DisposeBuffer(ref _contextOutputView, ref _contextOutputBuffer);
        _inputsArray = null;
    }

    protected override float[]? AppendChunkInternal(ReadOnlySpan<float> monoPcm)
    {
        var interpreter = _interpreter;
        if (interpreter == null)
            return null;  // Not initialized

        if (monoPcm.Length % WindowSamples != 0)
            throw new ArgumentException($"AppendChunkInternal expects N*{WindowSamples} samples.", nameof(monoPcm));

        int frames = monoPcm.Length / WindowSamples;
        if (frames == 0)
            return [];

        // Resize input for dynamic batch if changed and (re)allocate tensors
        if (_lastFrames != frames) {
            interpreter.ResizeInput(_framesInputIdx, [frames, WindowSamples]);
            interpreter.AllocateTensors();
            _lastFrames = frames;
        }

        // Prepare reusable buffers (create/grow if needed, set limits for dynamic)
        PrepareInputOutputBuffers(frames, interpreter);

        // Copy data to input buffers (rewind and put)
        CopyDataToInputBuffers(monoPcm);

        // Set inputs array
        _inputsArray![_framesInputIdx] = _framesInputBuffer!;
        _inputsArray[_stateInputIdx] = _stateInputBuffer!;
        _inputsArray[_contextInputIdx] = _contextInputBuffer!;

        // Set outputs dict (clear and add)
        _outputsDict.Clear();
        _outputsDict[_probsOutputIdx] = _probsOutputBuffer!;
        _outputsDict[_stateOutputIdx] = _stateOutputBuffer!;
        _outputsDict[_contextOutputIdx] = _contextOutputBuffer!;

        interpreter.RunForMultipleInputsOutputs(_inputsArray, _outputsDict);

        // Read results
        var probs = new float[frames];
        ReadFloatView(_probsOutputView!, probs, frames);
        ReadFloatView(_stateOutputView!, _state, _state.Length);
        ReadFloatView(_contextOutputView!, _context, _context.Length);

        return probs;
    }

    private void CacheTensorIndices()
    {
        var interpreter = _interpreter!;
        // Input indices
        for (int i = 0; i < interpreter.InputTensorCount; i++) {
            var sh = interpreter.GetInputTensor(i)!.Shape()!;
            if (sh is [_, WindowSamples])
                _framesInputIdx = i;
            else if (sh is [2, 1, 128])
                _stateInputIdx = i;
            else if (sh is [1, ContextSamples])
                _contextInputIdx = i;
        }
        if (_framesInputIdx < 0 || _stateInputIdx < 0 || _contextInputIdx < 0)
            throw new InvalidOperationException("Unexpected TfLite VAD input shapes.");

        // Output indices
        for (int i = 0; i < interpreter.OutputTensorCount; i++) {
            var sh = interpreter.GetOutputTensor(i)!.Shape()!;
            if (sh is [_, 1])
                _probsOutputIdx = i;
            else if (sh is [2, 1, 128])
                _stateOutputIdx = i;
            else if (sh is [1, ContextSamples])
                _contextOutputIdx = i;
        }
        if (_probsOutputIdx < 0 || _stateOutputIdx < 0 || _contextOutputIdx < 0)
            throw new InvalidOperationException("Unexpected TfLite VAD output shapes.");
    }

    private void PrepareInputOutputBuffers(int frames, Interpreter interpreter)
    {
        // Fixed buffers (create once if null)
        var stateTensor = interpreter.GetInputTensor(_stateInputIdx)!;
        if (_stateInputBuffer == null) {
            _stateInputBuffer = CreateEmptyBufferForTensor(stateTensor);
            _stateInputView = CreateFloatView(_stateInputBuffer);
        }
        else {
            _stateInputBuffer.Position(0);
            _stateInputBuffer.Limit(stateTensor.NumBytes());
        }

        var contextTensor = interpreter.GetInputTensor(_contextInputIdx)!;
        if (_contextInputBuffer == null) {
            _contextInputBuffer = CreateEmptyBufferForTensor(contextTensor);
            _contextInputView = CreateFloatView(_contextInputBuffer);
        }
        else {
            _contextInputBuffer.Position(0);
            _contextInputBuffer.Limit(contextTensor.NumBytes());
        }

        var stateOutTensor = interpreter.GetOutputTensor(_stateOutputIdx)!;
        if (_stateOutputBuffer == null) {
            _stateOutputBuffer = CreateEmptyBufferForTensor(stateOutTensor);
            _stateOutputView = CreateFloatView(_stateOutputBuffer);
        }
        else {
            _stateOutputBuffer.Position(0);
            _stateOutputBuffer.Limit(stateOutTensor.NumBytes());
        }

        var contextOutTensor = interpreter.GetOutputTensor(_contextOutputIdx)!;
        if (_contextOutputBuffer == null) {
            _contextOutputBuffer = CreateEmptyBufferForTensor(contextOutTensor);
            _contextOutputView = CreateFloatView(_contextOutputBuffer);
        }
        else {
            _contextOutputBuffer.Position(0);
            _contextOutputBuffer.Limit(contextOutTensor.NumBytes());
        }

        // Dynamic buffers (grow if needed, set limit)
        var framesInputTensor = interpreter.GetInputTensor(_framesInputIdx);
        var currentInputShape = framesInputTensor!.Shape()!;
        if (currentInputShape[0] != frames) {
            interpreter.ResizeInput(_framesInputIdx, [frames, WindowSamples]);
            interpreter.AllocateTensors();
            Log.LogInformation("VAD resized input tensor to {Frames}x{WindowSamples}", frames, WindowSamples);
        }
        var framesBytes = sizeof(float) * frames * WindowSamples;
        if (_framesInputBuffer == null || _framesInputBuffer.Capacity() < framesBytes) {
            var newCapacity = Math.Max(framesBytes, (_framesInputBuffer?.Capacity() ?? 0) * 2);
            _framesInputBuffer = ByteBuffer.AllocateDirect(newCapacity).Order(ByteOrder.NativeOrder()!);
            _framesInputView = CreateFloatView(_framesInputBuffer);
        }
        _framesInputBuffer.Position(0);
        _framesInputBuffer.Limit(framesBytes);

        var probsBytes = sizeof(float) * frames * 1;
        if (_probsOutputBuffer == null || _probsOutputBuffer.Capacity() < probsBytes) {
            var newCapacity = Math.Max(probsBytes, (_probsOutputBuffer?.Capacity() ?? 0) * 2);
            _probsOutputBuffer = ByteBuffer.AllocateDirect(newCapacity).Order(ByteOrder.NativeOrder()!);
            _probsOutputView = CreateFloatView(_probsOutputBuffer);
        }
        _probsOutputBuffer.Position(0);
        _probsOutputBuffer.Limit(probsBytes);
    }

    private void CopyDataToInputBuffers(ReadOnlySpan<float> monoPcm)
    {
        // Frames input
        var monoPcmLength = monoPcm.Length;
        EnsureFrameBuffer(monoPcmLength);
        monoPcm.CopyTo(_frameBuffer!.AsSpan(0, monoPcmLength));
        var framesFb = _framesInputView!;
        framesFb.Position(0);
        framesFb.Put(_frameBuffer, 0, monoPcmLength);
        _framesInputBuffer!.Position(0);

        // State input
        var stateFb = _stateInputView!;
        stateFb.Position(0);
        stateFb.Put(_state);
        _stateInputBuffer!.Position(0);

        // Context input
        var contextFb = _contextInputView!;
        contextFb.Position(0);
        contextFb.Put(_context);
        _contextInputBuffer!.Position(0);
    }

    // Rent/release frames array to avoid allocations
    private void EnsureFrameBuffer(int length)
    {
        if (_frameBuffer is not null && _frameBuffer.Length >= length)
            return;

        ReleaseFrameBuffer();
        _frameBuffer = ArrayPools.SharedFloatPool.Rent(length);
    }

    private void ReleaseFrameBuffer()
    {
        if (_frameBuffer is null)
            return;

        ArrayPools.SharedFloatPool.Return(_frameBuffer);
        _frameBuffer = null;
    }

    private static ByteBuffer CreateEmptyBufferForTensor(ITensor tensor)
    {
        var byteBuffer = ByteBuffer.AllocateDirect(tensor.NumBytes())
            .Order(ByteOrder.NativeOrder()!);
        byteBuffer.Position(0);
        return byteBuffer;
    }

    private static FloatBuffer CreateFloatView(ByteBuffer buffer)
    {
        // Spans the whole capacity, so the per-inference Limit() changes can't invalidate the view.
        buffer.Position(0);
        buffer.Limit(buffer.Capacity());
        return buffer.AsFloatBuffer()!;
    }

    private static void ReadFloatView(FloatBuffer view, float[] destination, int length)
    {
        view.Position(0);
        view.Get(destination, 0, length);
    }

    private static void DisposeBuffer(ref FloatBuffer? view, ref ByteBuffer? buffer)
    {
        view?.Dispose();
        view = null;
        buffer?.Dispose();
        buffer = null;
    }
}
