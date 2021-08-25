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

            cutter.CutMemoized(new("раз", 1.86));
            cutter.CutMemoized(new("работа", 1.98));
            cutter.CutMemoized(new("разбор", 2.04));
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
            r11.Text.Should().Be("Вышел");
            var r12 = cutter.CutMemoized(new("раз-два-три-четыре-пять Вышел зайчик", 5.52));
            r12.Text.Should().Be("зайчик");
        }
    }
}