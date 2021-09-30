using Stl.Testing;
using Xunit.Abstractions;

namespace ActualChat.Transcription.UnitTests
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

            var r1 = cutter.CutMemoized(("раз", 1.86));
            r1.Text.Should().Be("раз");
            r1.TextIndex.Should().Be(0);
            r1.StartOffset.Should().Be(0);
            r1.Duration.Should().Be(1.86);
            var r2 = cutter.CutMemoized(("работа", 1.98));
            r2.Text.Should().Be("бота");
            r2.TextIndex.Should().Be(2);
            r2.StartOffset.Should().Be(1.24);
            r2.Duration.Should().Be(0.74);
            var r3 = cutter.CutMemoized(("разбор", 2.04));
            r3.Text.Should().Be("збор");
            r3.TextIndex.Should().Be(2);
            r3.StartOffset.Should().Be(0.66);
            r3.Duration.Should().Be(1.38);
            cutter.CutMemoized(("работа", 2.22));
            cutter.CutMemoized(("раз-два-три", 2.28));
            cutter.CutMemoized(("раз-два-три-четыре", 2.76));
            var r7 = cutter.CutMemoized(("раз-два-три-четыре", 3.36));
            r7.Text.Should().Be("раз-два-три-четыре");
            cutter.CutMemoized(("раз-два-три-четыре-пять", 3.42));
            var r9 = cutter.CutMemoized(("раз-два-три-четыре-пять", 4.02));
            r9.Text.Should().Be("раз-два-три-четыре-пять");
            cutter.CutMemoized(("раз-два-три-четыре-пять", 4.32));
            var r11 = cutter.CutMemoized(("раз-два-три-четыре-пять Вышел", 4.92));
            r11.Text.Should().Be(" Вышел");
            var r12 = cutter.CutMemoized(("раз-два-три-четыре-пять Вышел зайчик", 5.52));
            r12.Text.Should().Be(" зайчик");
        }

        [Fact]
        public void RepeatTest()
        {
            var cutter = new StablePrefixCutter();

            cutter.CutMemoized(("разбор", 1.4));
            cutter.CutMemoized(("раз два", 1.460));
            cutter.CutMemoized(("раз-два-три", 1.580));
            cutter.CutMemoized(("раз-два-три", 2.180));
            cutter.CutMemoized(("раз два три раз", 2.660));
            cutter.CutMemoized(("раз-два-три раз-два", 3.020));
            cutter.CutMemoized(("раз-два-три раз-два-три", 3.20));
            var r8 = cutter.CutMemoized(("раз-два-три", 3.620));
            r8.TextIndex.Should().BeGreaterThan(10);
            cutter.CutMemoized(("раз-два-три раз-два-три", 3.8000));
            cutter.CutMemoized(("раз-два-три раз-два-три", 6.380));
            cutter.CutMemoized(("раз-два-три, раз-два-три", 6.500));
            cutter.CutMemoized(("раз-два-три, раз-два-три,", 6.500));
            cutter.CutMemoized(new TranscriptFragment{ Text = "раз-два-три, раз-два-три, кто-то", StartOffset = 0, Duration = 6.100, IsFinal = true});
            var r16 = cutter.CutMemoized(new TranscriptFragment{ Text = " был", StartOffset = 6.100, Duration = 0.500, IsFinal = true });
            r16.Text.Should().Be(" был");
            r16.TextIndex.Should().Be(32);
            r16.StartOffset.Should().Be(6.1d);
        }
    }
}
