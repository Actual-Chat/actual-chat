using System;
using System.Text.Json.Serialization;
using Stl.Collections;

namespace ActualChat
{
    [Serializable]
    public readonly struct LinearMap
    {
        public double[] SourcePoints { get; }
        public double[] TargetPoints { get; }

        [JsonIgnore, Newtonsoft.Json.JsonIgnore]
        public int Length => SourcePoints.Length;
        [JsonIgnore, Newtonsoft.Json.JsonIgnore]
        public (double Min, double Max) SourceRange => (SourcePoints[0], SourcePoints[^1]);
        [JsonIgnore, Newtonsoft.Json.JsonIgnore]
        public (double Min, double Max) TargetRange => (TargetPoints[0], TargetPoints[^1]);

        [JsonConstructor, Newtonsoft.Json.JsonConstructor]
        public LinearMap(double[] sourcePoints, double[] targetPoints)
        {
            SourcePoints = sourcePoints;
            TargetPoints = targetPoints;
        }

        public override string ToString()
            => $"{GetType().Name}({{{SourcePoints.ToDelimitedString()}}} -> {{{TargetPoints.ToDelimitedString()}}})";

        public double? Map(double value)
        {
            var leIndex = SourcePoints.IndexOfLowerOrEqual(value);
            if (leIndex < 0)
                return null;
            var leValue = SourcePoints[leIndex];
            if (leIndex == Length - 1) {
                // ReSharper disable once CompareOfFloatsByEqualityOperator
                return leValue == value ? TargetPoints[leIndex] : null;
            }

            var geIndex = leIndex + 1;
            var geValue = SourcePoints[geIndex];
            var factor = (value - leValue) / (geValue - leValue);
            var tleValue = TargetPoints[leIndex];
            var tgeValue = TargetPoints[geIndex];
            return tleValue + (tgeValue - tleValue) * factor;
        }

        public LinearMap Invert()
            => new(TargetPoints, SourcePoints);

        public bool IsValid()
            => TargetPoints.Length == SourcePoints.Length
                && SourcePoints.IsStrictlyIncreasingSequence();

        public bool IsInvertible()
            => TargetPoints.IsStrictlyIncreasingSequence();

        public LinearMap Validate(bool requireInvertible = false)
        {
            if (!IsValid())
                throw new InvalidOperationException($"Invalid {GetType().Name}.");
            if (requireInvertible && !IsInvertible())
                throw new InvalidOperationException($"Invalid {GetType().Name}.");
            return this;
        }
    }
}
