namespace ActualChat.Mathematics.Internal
{
    internal sealed class DoubleSizeMeasure : SizeMeasure<double, double>
    {
        public override double GetDistance(double start, double end) => end - start;
        public override double AddOffset(double point, double offset) => point + offset;

        public override double Add(double first, double second) => first + second;
        public override double Subtract(double first, double second) => first - second;
        public override double Multiply(double size, double multiplier) => size * multiplier;

        public override double Modulo(double size, double modulo)
        {
            var result = size % modulo;
            return result >= 0 ? result : modulo + result;
        }
    }
}
