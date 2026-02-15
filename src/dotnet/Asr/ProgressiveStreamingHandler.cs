namespace ActualChat.Asr;

/// <summary>
/// Sliding-window progressive streaming handler for live transcription.
/// Port of samples/parakeet-v3-streaming/source/src/utils/progressive-streaming.js.
/// </summary>
public sealed class ProgressiveStreamingHandler(
    ParakeetModel model,
    float maxWindowSize = 15.0f,
    float sentenceBuffer = 2.0f,
    int sampleRate = 16000)
{
    private readonly List<string> _fixedSentences = [];
    private float _fixedEndTime;
    private int _lastTranscribedLength;

    /// <summary>
    /// Transcribes audio incrementally. Call repeatedly with a growing audio buffer.
    /// </summary>
    public PartialTranscription TranscribeIncremental(float[] audio)
    {
        var currentLength = audio.Length;
        var fixedText = string.Join(' ', _fixedSentences);

        // Need at least 500ms of audio
        if (currentLength < sampleRate * 0.5f)
            return new PartialTranscription(fixedText, "", (float)currentLength / sampleRate, false);

        // Skip if no new audio
        if (currentLength == _lastTranscribedLength)
            return new PartialTranscription(fixedText, "", (float)currentLength / sampleRate, false);

        _lastTranscribedLength = currentLength;

        // Extract window from last fixed sentence end to current position
        var windowStartSamples = (int)(_fixedEndTime * sampleRate);
        var windowLength = currentLength - windowStartSamples;
        if (windowLength <= 0)
            return new PartialTranscription(fixedText, "", (float)currentLength / sampleRate, false);

        var audioWindow = new float[windowLength];
        Array.Copy(audio, windowStartSamples, audioWindow, 0, windowLength);

        var windowDuration = (float)windowLength / sampleRate;

        // Transcribe current window
        var result = model.Transcribe(audioWindow);

        // If window exceeds max size and has multiple sentences, fix early sentences
        if (windowDuration >= maxWindowSize) {
            var sentences = result.GetSentences();
            if (sentences.Count > 1) {
                var cutoffTime = windowDuration - sentenceBuffer;
                var newFixedSentences = new List<string>();
                var newFixedEndTime = _fixedEndTime;

                foreach (var sentence in sentences) {
                    if (sentence.EndTime < cutoffTime) {
                        newFixedSentences.Add(sentence.Text.Trim());
                        newFixedEndTime = _fixedEndTime + sentence.EndTime;
                    }
                    else {
                        break;
                    }
                }

                if (newFixedSentences.Count > 0) {
                    _fixedSentences.AddRange(newFixedSentences);
                    _fixedEndTime = newFixedEndTime;

                    // Re-transcribe from new fixed point
                    var newWindowStart = (int)(_fixedEndTime * sampleRate);
                    var newWindowLen = currentLength - newWindowStart;
                    if (newWindowLen > 0) {
                        var newWindow = new float[newWindowLen];
                        Array.Copy(audio, newWindowStart, newWindow, 0, newWindowLen);
                        result = model.Transcribe(newWindow);
                    }

                    fixedText = string.Join(" ", _fixedSentences);
                }
            }
        }

        var activeText = result.Text.Trim();
        var timestamp = (float)audio.Length / sampleRate;

        return new PartialTranscription(fixedText, activeText, timestamp, false);
    }

    /// <summary>Finalizes the transcription by combining fixed + active text.</summary>
    public string Finalize(float[] audio)
    {
        var result = TranscribeIncremental(audio);
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(result.FixedText))
            parts.Add(result.FixedText);
        if (!string.IsNullOrEmpty(result.ActiveText))
            parts.Add(result.ActiveText);
        return string.Join(' ', parts);
    }
}
