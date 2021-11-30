using ActualChat.Mathematics;

namespace ActualChat.Core.UnitTests
{
    public class LinearMapTest : TestBase
    {
        public LinearMapTest(ITestOutputHelper @out) : base(@out) { }

        [Fact]
        public void BasicTest()
        {
            var map = new LinearMap(new[] {0.0}, new[] {1.0}).Validate();
            map.Map(-1).Should().BeNull();
            map.Map(1).Should().BeNull();
            map.Map(0).Should().Be(1.0);

            map = new LinearMap(new[] {0.0, 1}, new[] {1.0, 3}).Validate();
            map.Map(-0.1).Should().BeNull();
            map.Map(1.1).Should().BeNull();
            map.Map(0).Should().Be(1.0);
            map.Map(1).Should().Be(3.0);
            map.Map(0.5).Should().Be(2.0);

            map = new LinearMap(new[] {0.0, 1, 11}, new[] {1.0, 3, 4}).Validate();
            map.Map(-0.1).Should().BeNull();
            map.Map(11.1).Should().BeNull();
            map.Map(0).Should().Be(1.0);
            map.Map(1).Should().Be(3.0);
            map.Map(11).Should().Be(4.0);
            map.Map(0.5).Should().Be(2.0);
            map.Map(6).Should().Be(3.5);
        }
        [Fact]
        public void ActualMapTest()
        {
            var json = "{\"sourcePoints\":[0,4,18,20,25,27,37,46,53,57,64,74,81,93,98],\"targetPoints\":[0,1.8,2.4,3.2,3.4,4.2,4.3,5.4,5.5,6.9,7.4,7.6,8.9,9.9,10.5]}";
            var map = SystemJsonSerializer.Default.Read<LinearMap>(json);
            map.Length.Should().BeGreaterThan(10);
            var last = 0d;
            for (var i = 0; i <= 98; i++) {
                var current = map.Map(i)!.Value;
                current.Should().BeGreaterOrEqualTo(last);
                last = current;
            }
        }

        [Fact]
        public void WrongMapTest()
        {
            Assert.Throws<InvalidOperationException>(() => {
                new LinearMap(Array.Empty<double>(), new[] {1.0}).Validate();
            });
            Assert.Throws<InvalidOperationException>(() => {
                new LinearMap(new[] {1.0}, Array.Empty<double>()).Validate();
            });
            Assert.Throws<InvalidOperationException>(() => {
                new LinearMap(new[] {1.0, 1.0}, new[] {1.0, 2.0}).Validate();
            });
            new LinearMap(new[] {1.0, 2.0}, new[] {2.0, 2.0}).Validate();
            Assert.Throws<InvalidOperationException>(() => {
                new LinearMap(new[] {1.0, 2.0}, new[] {2.0, 2.0}).Validate(true);
            });
            Assert.Throws<InvalidOperationException>(() => {
                new LinearMap(new[] {1.0, 2.0}, new[] {2.0, -2.0}).Validate(true);
            });
        }
    }
}
