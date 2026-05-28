using ActualChat.Audio;
using CoreML;
using Foundation;

namespace ActualChat.App.Maui.Audio;

public sealed class CoreMLVoiceActivityDetector(IServiceProvider services) : VoiceActivityDetector(services)
{
    private MLModel? _model;

    // Reusable CoreML buffers to avoid per-call allocations and copies
    private MLMultiArray? _inputArr;   // (B,512) with B=1 here
    private MLMultiArray? _contextArr; // (1,64)
    private MLMultiArray? _stateArr;   // (2,1,128) stacked [h;c]

    public override bool IsInitialized => _model is not null;

    public override Task EnsureInitialized(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (IsInitialized) {
            Reset();
            return Task.CompletedTask;
        }

        // Inputs (batched interface):
        //   input:   (B, 512) float32  - B sequential 512-sample frames (we use B=1 here)
        //   state:   (2, 1, 128) float32 - stacked LSTM state: [h; c]
        //   context: (1, 64) float32   - previous tail context (last 64 samples)
        // Outputs:
        //   output:   (B, 1) float32   - VAD score per frame
        //   stateN:   (2, 1, 128) float32 - next stacked state after B steps
        //   contextN: (1, 64) float32  - next tail context (last 64 from last frame)
        var modelUrl = NSBundle.MainBundle.GetUrlForResource("vad_batched_fp16", "mlmodelc");
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (modelUrl is null) {
            Log.LogError("CoreML VAD model not found in app bundle");
            throw StandardError.Configuration("CoreML VAD model not found in app bundle.");
        }

        var config = new MLModelConfiguration {
            ComputeUnits = MLComputeUnits.All,
            // Allow low precision (float16) if supported by the device
            AllowLowPrecisionAccumulationOnGpu = true,
        };
        var model = MLModel.Create(modelUrl, config, out var error);
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (model is null || error is not null) {
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            Log.LogError(error?.LocalizedDescription ?? "Unknown error");
            throw StandardError.Configuration("Failed to load CoreML VAD model.");
        }
        _model = model;

        // Allocate reusable MLMultiArrays
        static NSNumber[] Shape(params int[] dims) => dims.Select(NSNumber.FromInt32).ToArray();
        _inputArr = new MLMultiArray(Shape(1, 512), MLMultiArrayDataType.Float32, out var e1);
        if (e1 is not null) throw new InvalidOperationException(e1.LocalizedDescription);
        _contextArr = new MLMultiArray(Shape(1, 64), MLMultiArrayDataType.Float32, out var e2);
        if (e2 is not null) throw new InvalidOperationException(e2.LocalizedDescription);
        _stateArr = new MLMultiArray(Shape(2, 1, 128), MLMultiArrayDataType.Float32, out var e3);
        if (e3 is not null) throw new InvalidOperationException(e3.LocalizedDescription);

        // Ensure initial state is zeros
        Reset();
        return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (!disposing)
            return;

        _model.DisposeSilently();
        _model = null;
        _inputArr.DisposeSilently();
        _contextArr.DisposeSilently();
        _stateArr.DisposeSilently();
        _inputArr = null;
        _contextArr = null;
        _stateArr = null;
    }

    public override void Reset()
    {
        base.Reset();
        // Zero the recurrent state/context in reusable arrays
        unsafe {
            if (_contextArr is not null)
                new Span<float>((float*)_contextArr.DataPointer, 64).Clear();
            if (_stateArr is not null)
                new Span<float>((float*)_stateArr.DataPointer, 2 * 1 * 128).Clear();
        }
    }

    protected override float[]? AppendChunkInternal(ReadOnlySpan<float> monoPcm)
    {
        // Support batched inference: monoPcm must contain N * WindowSamples samples
        if (monoPcm.Length % WindowSamples != 0)
            throw StandardError.Constraint(nameof(monoPcm), $"Expected N*{WindowSamples} samples, got {monoPcm.Length}");

        var model = _model;
        if (model is null) {
            Log.LogError("CoreMLVoiceActivityDetector is not initialized, call EnsureInitialized() first");
            return null; // Not initialized yet
        }

        var contextArr = _contextArr!;
        var stateArr = _stateArr!;

        var frames = monoPcm.Length / WindowSamples;

        // Prepare or reuse input MLMultiArray matching current frames count
        var inputArr = _inputArr!;
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (inputArr is null || (int)inputArr.Count != monoPcm.Length) {
            static NSNumber[] Shape(params int[] dims) => dims.Select(NSNumber.FromInt32).ToArray();
            _inputArr.DisposeSilently();
            _inputArr = new MLMultiArray(Shape(frames, WindowSamples), MLMultiArrayDataType.Float32, out var nsError);
            if (nsError is not null)
                throw new InvalidOperationException(nsError.LocalizedDescription);

            inputArr = _inputArr!;
        }
        // Copy all frame samples into the reusable array
        unsafe {
            var dst = new Span<float>((float*)inputArr.DataPointer, monoPcm.Length);
            monoPcm.CopyTo(dst);
        }

        // Build feature provider (batched interface)
        // iOS 16/17/18 CoreML may require an extra "state_workaround" input name for certain converted models.
        // Provide it as an alias to "state"; extra features are ignored if the model doesn't need them.
        var keys = new[] { (NSString)"input", (NSString)"state", (NSString)"context", (NSString)"state_workaround" };
        var values = new NSObject[] { inputArr, stateArr, contextArr, stateArr };
        using var nsDict = new NSDictionary<NSString, NSObject>(keys, values);
        var inputProvider = new MLDictionaryFeatureProvider(nsDict, out var dictErr);
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (dictErr is not null) {
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            Log.LogError(dictErr.LocalizedDescription);
            return null;
        }

        var prediction = model.GetPrediction(inputProvider, out var predErr);
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (predErr is not null || prediction is null) {
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            Log.LogError(predErr?.LocalizedDescription ?? "CoreML prediction failed");
            return null;
        }

        // Extract outputs
        var outputVal = prediction.GetFeatureValue("output");
        var contextNVal = prediction.GetFeatureValue("contextN");
        var stateNVal = prediction.GetFeatureValue("stateN");

        var outputArr = outputVal!.MultiArrayValue;
        var contextNArr = contextNVal!.MultiArrayValue;
        var stateNArr = stateNVal!.MultiArrayValue;

        unsafe {
            // Update reusable state arrays directly from outputs for next call
            if (contextNArr is not null) {
                var src = new ReadOnlySpan<float>((float*)contextNArr.DataPointer, 64);
                var dst = new Span<float>((float*)contextArr.DataPointer, 64);
                src.CopyTo(dst);
            }
            if (stateNArr is not null) {
                var src = new ReadOnlySpan<float>((float*)stateNArr.DataPointer, 2 * 1 * 128);
                var dst = new Span<float>((float*)stateArr.DataPointer, 2 * 1 * 128);
                src.CopyTo(dst);
            }

            // Extract probabilities for each frame (output shape is [B,1])
            if (outputArr is null)
                return null;
            var outLen = (int)outputArr.Count; // should be B or B*1
            var n = Math.Min(frames, outLen);
            var srcOut = new ReadOnlySpan<float>((float*)outputArr.DataPointer, n);
            var probs = new float[n];
            srcOut.CopyTo(probs);
            return probs;
        }
    }
}
