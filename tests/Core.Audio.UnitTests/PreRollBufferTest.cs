using ActualChat.Audio;

namespace ActualChat.Core.Audio.UnitTests;

public class PreRollBufferTest
{
    private const int SampleRate = 48_000;

    [Fact]
    public void AppendedSamplesDrainInOrder()
    {
        // arrange
        var buffer = new PreRollBuffer(7, SampleRate, 16);

        // act
        buffer.TryAppend([1f, 2f, 3f]).Should().BeTrue();
        buffer.TryAppend([4f, 5f]).Should().BeTrue();
        var drained = buffer.TryDrain(7, 1);

        // assert
        drained.Should().Equal([1f, 2f, 3f, 4f, 5f]);
    }

    [Fact]
    public void DrainingWithAForeignTokenReturnsNothing()
    {
        // arrange
        var buffer = new PreRollBuffer(7, SampleRate, 16);
        buffer.TryAppend([1f, 2f, 3f]);

        // act
        var drained = buffer.TryDrain(8, 1);

        // assert
        drained.Should().BeNull();
        buffer.Count.Should().Be(3);
    }

    [Fact]
    public void ASecondDrainReturnsNothing()
    {
        // arrange
        var buffer = new PreRollBuffer(7, SampleRate, 16);
        buffer.TryAppend([1f, 2f, 3f]);

        // act
        var first = buffer.TryDrain(7, 1);
        var second = buffer.TryDrain(7, 1);

        // assert
        first.Should().NotBeNull();
        second.Should().BeNull();
    }

    [Fact]
    public void OverflowKeepsTheNewestSamples()
    {
        // arrange
        var buffer = new PreRollBuffer(7, SampleRate, 4);
        buffer.TryAppend([1f, 2f]);

        // act
        var isAppended = buffer.TryAppend([3f, 4f, 5f]);

        // assert
        // A slow boot loses the first words, not the whole utterance.
        isAppended.Should().BeTrue();
        buffer.IsOverflowed.Should().BeTrue();
        buffer.Count.Should().Be(4);
        buffer.TryDrain(7, 1).Should().Equal([2f, 3f, 4f, 5f]);
    }

    [Fact]
    public void AnAppendLongerThanTheBufferKeepsItsTail()
    {
        // arrange
        var buffer = new PreRollBuffer(7, SampleRate, 2);

        // act
        var isAppended = buffer.TryAppend([1f, 2f, 3f]);

        // assert
        isAppended.Should().BeTrue();
        buffer.IsOverflowed.Should().BeTrue();
        buffer.TryDrain(7, 1).Should().Equal([2f, 3f]);
    }

    [Fact]
    public void AppendingAfterOverflowKeepsWorking()
    {
        // arrange
        var buffer = new PreRollBuffer(7, SampleRate, 2);
        buffer.TryAppend([1f, 2f, 3f]);

        // act
        var isAppended = buffer.TryAppend([4f]);

        // assert
        // No poisoning: the buffer that overflowed at second 8 of a 20 s boot must still hold
        // the last 8 s when the recorder finally arrives.
        isAppended.Should().BeTrue();
        buffer.Count.Should().Be(2);
        buffer.TryDrain(7, 1).Should().Equal([3f, 4f]);
    }

    [Fact]
    public void AnAppendThatWrapsTheRingStaysInOrder()
    {
        // arrange
        var buffer = new PreRollBuffer(7, SampleRate, 5);
        buffer.TryAppend([1f, 2f, 3f, 4f]);

        // act
        buffer.TryAppend([5f, 6f, 7f]);

        // assert
        buffer.TryDrain(7, 1).Should().Equal([3f, 4f, 5f, 6f, 7f]);
    }

    [Fact]
    public void ARepeatedlyOverflowedBufferStaysInOrder()
    {
        // arrange
        var buffer = new PreRollBuffer(7, SampleRate, 4);

        // act
        for (var i = 1; i <= 10; i++)
            buffer.TryAppend([i * 10f, i * 10f + 1f]);

        // assert
        buffer.TryDrain(7, 1).Should().Equal([90f, 91f, 100f, 101f]);
    }

    [Fact]
    public void TooLittleAudioIsNotDrained()
    {
        // arrange
        var buffer = new PreRollBuffer(7, SampleRate, 16);
        buffer.TryAppend([1f, 2f]);

        // act
        var drained = buffer.TryDrain(7, 3);

        // assert
        drained.Should().BeNull();
        // Not consumed: more audio may still arrive before the recorder exists.
        buffer.TryAppend([3f]).Should().BeTrue();
        buffer.TryDrain(7, 3).Should().Equal([1f, 2f, 3f]);
    }

    [Fact]
    public void DurationFollowsTheSampleRate()
    {
        // arrange
        var buffer = new PreRollBuffer(7, SampleRate, SampleRate);

        // act
        buffer.TryAppend(new float[SampleRate / 2]);

        // assert
        buffer.Duration.Should().Be(TimeSpan.FromSeconds(0.5));
    }

    [Fact]
    public void AnEmptyAppendIsANoOp()
    {
        // arrange
        var buffer = new PreRollBuffer(7, SampleRate, 4);

        // act
        var isAppended = buffer.TryAppend([]);

        // assert
        isAppended.Should().BeTrue();
        buffer.IsOverflowed.Should().BeFalse();
        buffer.Count.Should().Be(0);
    }
}
