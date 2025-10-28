using ActualChat.App.Maui.Services.Recording;
using ActualChat.Audio;
using CoreML;
using Foundation;

namespace ActualChat.App.Maui.Audio;

public sealed class CoreMLVoiceActivityDetector(IServiceProvider services) : VoiceActivityDetector(services)
{
    private MLModel? _model;

    // Reusable CoreML buffers to avoid per-call allocations and copies
    private MLMultiArray? _inputArr;   // (1,512)
    private MLMultiArray? _contextArr; // (1,64)
    private MLMultiArray? _hArr;       // (1,1,128)
    private MLMultiArray? _cArr;       // (1,1,128)

    private ILogger Log { get; } = services.LogFor<CoreMLVoiceActivityDetector>();

    public override bool IsInitialized => _model is not null;

    public override Task EnsureInitialized(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (IsInitialized) {
            Reset();
            return Task.CompletedTask;
        }

        // Inputs:
        //   input: (1, 512) float32 - current audio chunk
        //   context: (1, 64) float32 - previous rolling context
        //   h: (1, 1, 128) float32 - previous LSTM hidden state
        //   c: (1, 1, 128) float32 - previous LSTM cell state
        // Outputs:
        //   score: (1, 1) float32 - VAD score
        //   contextN: (1, 64) float32 - updated context
        //   hn: (1, 1, 128) float32 - updated hidden state
        //   cn: (1, 1, 128) float32 - updated cell state
        var modelUrl = NSBundle.MainBundle.GetUrlForResource("vad_stateless", "mlmodelc");
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
        _hArr = new MLMultiArray(Shape(1, 1, 128), MLMultiArrayDataType.Float32, out var e3);
        if (e3 is not null) throw new InvalidOperationException(e3.LocalizedDescription);
        _cArr = new MLMultiArray(Shape(1, 1, 128), MLMultiArrayDataType.Float32, out var e4);
        if (e4 is not null) throw new InvalidOperationException(e4.LocalizedDescription);

        // Ensure initial state is zeros
        unsafe {
            if (_contextArr is not null) new Span<float>((float*)_contextArr.DataPointer, 64).Clear();
            if (_hArr is not null) new Span<float>((float*)_hArr.DataPointer, 128).Clear();
            if (_cArr is not null) new Span<float>((float*)_cArr.DataPointer, 128).Clear();
        }

        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        base.Dispose();
        _model.DisposeSilently();
        _model = null;
        _inputArr.DisposeSilently();
        _contextArr.DisposeSilently();
        _hArr.DisposeSilently();
        _cArr.DisposeSilently();
        _inputArr = null;
        _contextArr = null;
        _hArr = null;
        _cArr = null;
    }

    public override void Reset()
    {
        base.Reset();
        // Zero the recurrent state/context in reusable arrays
        unsafe {
            if (_contextArr is not null) new Span<float>((float*)_contextArr.DataPointer, 64).Clear();
            if (_hArr is not null) new Span<float>((float*)_hArr.DataPointer, 128).Clear();
            if (_cArr is not null) new Span<float>((float*)_cArr.DataPointer, 128).Clear();
        }
    }

    protected override float? AppendChunkInternal(ReadOnlySpan<float> monoPcm)
    {
        if (monoPcm.Length != WindowSamples)
            throw StandardError.Constraint(nameof(monoPcm), $"Expected length {WindowSamples}, got {monoPcm.Length}");

        var model = _model;
        if (model is null) {
            Log.LogError("CoreMLVoiceActivityDetector is not initialized, call EnsureInitialized() first");
            return null; // Not initialized yet
        }

        var inputArr = _inputArr!;
        var contextArr = _contextArr!;
        var hArr = _hArr!;
        var cArr = _cArr!;

        // Copy input samples directly into reusable MLMultiArray
        unsafe {
            var dst = new Span<float>((float*)inputArr.DataPointer, WindowSamples);
            monoPcm.CopyTo(dst);
        }

        // Build feature provider
        var keys = new[] { (NSString)"input", (NSString)"context", (NSString)"h", (NSString)"c" };
        var values = new NSObject[] { inputArr, contextArr, hArr, cArr };
        using var nsDict = new NSDictionary<NSString, NSObject>(keys, values);
        var inputProvider = new MLDictionaryFeatureProvider(nsDict, out var dictErr);
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (dictErr is not null) {
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            Log.LogError(dictErr.LocalizedDescription);
            return null;
        }

        var output = model.GetPrediction(inputProvider, out var predErr);
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (predErr is not null || output is null) {
            // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
            Log.LogError(predErr?.LocalizedDescription ?? "CoreML prediction failed");
            return null;
        }

        // Extract outputs
        var scoreVal = output.GetFeatureValue("score");
        var contextNVal = output.GetFeatureValue("contextN");
        var hnVal = output.GetFeatureValue("hn");
        var cnVal = output.GetFeatureValue("cn");

        var scoreArr = scoreVal!.MultiArrayValue;
        var contextNArr = contextNVal!.MultiArrayValue;
        var hnArr = hnVal!.MultiArrayValue;
        var cnArr = cnVal!.MultiArrayValue;

        unsafe {
            // score is (1,1)
            float score = 0f;
            if (scoreArr is not null)
                score = *((float*)scoreArr.DataPointer);

            // Update reusable state arrays directly from outputs for next call
            if (contextNArr is not null) {
                var src = new ReadOnlySpan<float>((float*)contextNArr.DataPointer, 64);
                var dst = new Span<float>((float*)contextArr.DataPointer, 64);
                src.CopyTo(dst);
            }
            if (hnArr is not null) {
                var src = new ReadOnlySpan<float>((float*)hnArr.DataPointer, 128);
                var dst = new Span<float>((float*)hArr.DataPointer, 128);
                src.CopyTo(dst);
            }
            if (cnArr is not null) {
                var src = new ReadOnlySpan<float>((float*)cnArr.DataPointer, 128);
                var dst = new Span<float>((float*)cArr.DataPointer, 128);
                src.CopyTo(dst);
            }

            return score;
        }
    }
}
