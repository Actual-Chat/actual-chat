using ActualChat.Transcription;
using FluentAssertions;
using Stl.Testing;
using Xunit;
using Xunit.Abstractions;

namespace ActualChat.Tests.Transcription
{
    public class StablePrefixCutterTest : TestBase
    {
        public StablePrefixCutterTest(ITestOutputHelper @out) : base(@out)
        {
        }

        [Fact]
        public void BasicTest()
        {
            var cutter = new StablePrefixCutter();

            var r1 = cutter.CutMemoized(new("раз", 1.86));
            r1.Text.Should().Be("раз");
            r1.TextIndex.Should().Be(0);
            r1.StartOffset.Should().Be(0);
            r1.Duration.Should().Be(1.86);
            var r2 = cutter.CutMemoized(new("работа", 1.98));
            r2.Text.Should().Be("бота");
            r2.TextIndex.Should().Be(2);
            r2.StartOffset.Should().Be(1.24);
            r2.Duration.Should().Be(0.74);
            var r3 = cutter.CutMemoized(new("разбор", 2.04));
            r3.Text.Should().Be("збор");
            r3.TextIndex.Should().Be(2);
            r3.StartOffset.Should().Be(0.66);
            r3.Duration.Should().Be(1.38);
            cutter.CutMemoized(new("работа", 2.22));
            cutter.CutMemoized(new("раз-два-три", 2.28));
            cutter.CutMemoized(new("раз-два-три-четыре", 2.76));
            var r7 = cutter.CutMemoized(new("раз-два-три-четыре", 3.36));
            r7.Text.Should().Be("раз-два-три-четыре");
            cutter.CutMemoized(new("раз-два-три-четыре-пять", 3.42));
            var r9 = cutter.CutMemoized(new("раз-два-три-четыре-пять", 4.02));
            r9.Text.Should().Be("раз-два-три-четыре-пять");
            cutter.CutMemoized(new("раз-два-три-четыре-пять", 4.32));
            var r11 = cutter.CutMemoized(new("раз-два-три-четыре-пять Вышел", 4.92));
            r11.Text.Should().Be(" Вышел");
            var r12 = cutter.CutMemoized(new("раз-два-три-четыре-пять Вышел зайчик", 5.52));
            r12.Text.Should().Be(" зайчик");
        }
    }
}