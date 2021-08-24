namespace ActualChat.Mathematics
{
    public sealed class DoubleSizeMeasure : ISizeMeasure<double, double>
    {
        public static DoubleSizeMeasure Instance { get; } = new();

        public double GetDistance(double start, double end) => end - start;
        public double AddOffset(double point, double offset) => point + offset;

        public double Add(double first, double second) => first + second;
        public double Subtract(double first, double second) => first - second;
        public double Multiply(double size, double multiplier) => size * multiplier;
        public double Modulo(double size, double modulo) => size % modulo;
    }
}
