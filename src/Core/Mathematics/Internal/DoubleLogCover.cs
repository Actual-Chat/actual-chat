using System;
using System.Linq;
using Stl;

namespace ActualChat.Mathematics.Internal
{
    public sealed class DoubleLogCover : LogCover<double, double>
    {
        public DoubleLogCover()
        {
            MinTileSize = 1;
            MaxTileSize = 1024 * 1024;
            Measure = SizeMeasure.Double;
        }

        protected override double[] GetTileSizes()
            => Enumerable.Range(0, int.MaxValue)
                .Select(i => MinTileSize * Math.Pow(TileSizeFactor, i))
                .TakeWhile(size => size <= MaxTileSize)
                .ToArray();
    }
}

