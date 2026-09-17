using ActualChat.Audio;

namespace ActualChat.Core.Server.UnitTests.Audio;

public class VoiceOverMixerTest
{
    private const int FrameLength = VoiceOverMixer.FrameLength;
    private const float DuckGain = 0.25f;
    private const int HoldFrameCount = 3;
    private const int RampSampleCount = 2 * FrameLength; // two frames, so the ramp is observable per frame

    [Fact]
    public void OriginalShouldPassThroughAtFullGainWithoutDub()
    {
        // arrange
        var mixer = NewMixer();
        var original = Constant(1000);
        var output = new short[FrameLength];

        // act
        mixer.Mix(original, output, isDubSpeakingElsewhere: false);

        // assert
        output.Should().AllBeEquivalentTo((short)1000);
        mixer.IsDubSpeaking.Should().BeFalse();
    }

    [Fact]
    public void DubShouldBeSummedAndTheOriginalDuckedOverTheRamp()
    {
        // arrange
        var mixer = NewMixer();
        mixer.AddDubPcm(Bytes(Constant(2000, 3 * FrameLength)));
        var original = Constant(1000);
        var output = new short[FrameLength];

        // act - three frames of dub: the ramp spans the first two
        mixer.Mix(original, output, false);
        var first = output.ToArray();
        mixer.Mix(original, output, false);
        var second = output.ToArray();
        mixer.Mix(original, output, false);
        var third = output.ToArray();

        // assert - the original's gain falls from 1.0 towards 0.25 sample by sample, then holds
        first[0].Should().BeCloseTo((short)(2000 + 1000), 20);
        first[^1].Should().BeLessThan(first[0]);
        second[^1].Should().BeCloseTo((short)(2000 + 250), 20);
        third.Should().AllSatisfy(x => x.Should().BeCloseTo((short)2250, 5));
        mixer.IsDubSpeaking.Should().BeTrue();
        mixer.HasDubAudio.Should().BeFalse();
    }

    [Fact]
    public void DuckShouldHoldOverAShortGapAndReleaseAfterALongOne()
    {
        // arrange - one frame of dub, then silence
        var mixer = NewMixer();
        mixer.AddDubPcm(Bytes(Constant(2000)));
        var original = Constant(1000);
        var output = new short[FrameLength];
        mixer.Mix(original, output, false); // S = 0: the dub frame itself, hold counts from here
        mixer.Mix(original, output, false); // S = 1: ramp completes here (2 frames)

        // act - S = 2..HoldFrameCount (HoldFrameCount - 1 more frames) without dub keep the duck,
        // the next one (S = HoldFrameCount + 1) starts the release
        var heldGains = new List<short>();
        for (var i = 0; i < HoldFrameCount - 1; i++) {
            mixer.Mix(original, output, false);
            heldGains.Add(output[^1]);
        }
        var isSpeakingAfterHold = mixer.IsDubSpeaking;
        mixer.Mix(original, output, false);
        var releasing = output[^1];
        mixer.Mix(original, output, false);
        var released = output[^1];

        // assert
        heldGains.Should().AllSatisfy(x => x.Should().BeCloseTo((short)250, 5));
        isSpeakingAfterHold.Should().BeTrue();
        releasing.Should().BeGreaterThan((short)250).And.BeLessThan((short)1000);
        released.Should().BeCloseTo((short)1000, 5);
        mixer.IsDubSpeaking.Should().BeFalse();
    }

    [Fact]
    public void SpeakingElsewhereShouldDuckWithoutDubSamples()
    {
        // arrange
        var mixer = NewMixer();
        var original = Constant(1000);
        var output = new short[FrameLength];

        // act
        mixer.Mix(original, output, isDubSpeakingElsewhere: true);
        mixer.Mix(original, output, isDubSpeakingElsewhere: true);

        // assert
        output[^1].Should().BeCloseTo((short)250, 5);
        mixer.IsDubSpeaking.Should().BeTrue();
    }

    [Fact]
    public void OnlyAConsumedDubFrameShouldCountAsOwnDubAudio()
    {
        // arrange
        var mixer = NewMixer();
        mixer.AddDubPcm(Bytes(Constant(2000)));
        var original = Constant(1000);
        var output = new short[FrameLength];

        // act
        var isElsewhereOwn = mixer.Mix(original, output, isDubSpeakingElsewhere: true);
        var isElsewhereOwnAgain = mixer.Mix(original, output, isDubSpeakingElsewhere: true);
        var isHoldOwn = mixer.Mix(original, output, isDubSpeakingElsewhere: false);

        // assert - the duck is on throughout, but only the call that mixed a dub frame reports it
        isElsewhereOwn.Should().BeTrue("the first call consumed the one dub frame");
        isElsewhereOwnAgain.Should().BeFalse("a duck forced from elsewhere is not this mixer's own speech");
        isHoldOwn.Should().BeFalse("the hold keeps the duck on without any dub audio");
        mixer.IsDubSpeaking.Should().BeTrue();
    }

    [Fact]
    public void EndedOriginalShouldGiveTheDubAlone()
    {
        // arrange
        var mixer = NewMixer();
        mixer.AddDubPcm(Bytes(Constant(2000)));
        var output = new short[FrameLength];

        // act
        mixer.Mix(ReadOnlySpan<short>.Empty, output, false);

        // assert
        output.Should().AllBeEquivalentTo((short)2000);
    }

    [Fact]
    public void SumShouldClampToShortRange()
    {
        // arrange
        var mixer = NewMixer();
        mixer.AddDubPcm(Bytes(Constant(30000)));
        var output = new short[FrameLength];

        // act - the ramp starts at full gain, so the first sample is 30000 + 30000
        mixer.Mix(Constant(30000), output, false);

        // assert
        output[0].Should().Be(short.MaxValue);
    }

    [Fact]
    public void DubPcmShouldBeSlicedAcrossFrames()
    {
        // arrange - one and a half frames of dub, delivered as odd-sized chunks
        var mixer = NewMixer();
        var pcm = Bytes(Constant(2000, FrameLength * 3 / 2));
        mixer.AddDubPcm(pcm.AsSpan(0, 1000));
        mixer.AddDubPcm(pcm.AsSpan(1000));
        var output = new short[FrameLength];

        // act
        mixer.Mix(ReadOnlySpan<short>.Empty, output, false);
        var first = output.ToArray();
        mixer.Mix(ReadOnlySpan<short>.Empty, output, false);
        var second = output.ToArray();

        // assert - the second frame is half dub, half silence
        first.Should().AllBeEquivalentTo((short)2000);
        second[..(FrameLength / 2)].Should().AllBeEquivalentTo((short)2000);
        second[(FrameLength / 2)..].Should().AllBeEquivalentTo((short)0);
        mixer.HasDubAudio.Should().BeFalse();
    }

    [Fact]
    public void DubPcmShouldPairAnOddTrailingByteAcrossChunks()
    {
        // arrange - FrameLength + 1 samples of dub, split at an odd byte offset: the extra sample's
        // low byte arrives with the first frame, its high byte arrives in a separate chunk
        var mixer = NewMixer();
        var pcm = Bytes(Constant(2000, FrameLength + 1));
        mixer.AddDubPcm(pcm.AsSpan(0, FrameLength * sizeof(short) + 1));
        var output = new short[FrameLength];

        // act
        mixer.Mix(ReadOnlySpan<short>.Empty, output, false);
        var first = output.ToArray();
        mixer.AddDubPcm(pcm.AsSpan(FrameLength * sizeof(short) + 1));
        mixer.Mix(ReadOnlySpan<short>.Empty, output, false);
        var second = output.ToArray();

        // assert - the first frame is full dub, the second is the odd sample once its byte pairs up, then silence
        first.Should().AllBeEquivalentTo((short)2000);
        second[0].Should().Be((short)2000);
        second[1..].Should().AllBeEquivalentTo((short)0);
    }

    [Fact]
    public void LoneTrailingByteShouldNotCountAsDubAudio()
    {
        // arrange
        var mixer = NewMixer();
        var pcm = Bytes(Constant(2000, 1));

        // act
        mixer.AddDubPcm(pcm.AsSpan(0, 1));
        var hasAudioAfterOneByte = mixer.HasDubAudio;
        mixer.AddDubPcm(pcm.AsSpan(1, 1));
        var hasAudioAfterTwoBytes = mixer.HasDubAudio;

        // assert
        hasAudioAfterOneByte.Should().BeFalse("a lone byte can never become a sample on its own");
        hasAudioAfterTwoBytes.Should().BeTrue();
    }

    // Private methods

    private static VoiceOverMixer NewMixer()
        => new(DuckGain, HoldFrameCount, RampSampleCount);

    private static short[] Constant(short value, int length = FrameLength)
    {
        var samples = new short[length];
        Array.Fill(samples, value);
        return samples;
    }

    private static byte[] Bytes(short[] samples)
        => MemoryMarshal.AsBytes(samples.AsSpan()).ToArray();
}
