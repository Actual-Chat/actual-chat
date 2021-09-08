using System;

namespace ActualChat.UI.Blazor.Internal
{
    public interface IVirtualListItemSizeEstimator
    {
        double EstimatedSize { get; }
        void AddObservedSize(double size);
        void RemoveObservedSize(double size);
    }

    public class VirtualListItemSizeEstimator : IVirtualListItemSizeEstimator
    {
        private long _observationCount;
        private double _sizeSum;

        public long ObservationCountResetThreshold { get; }
        public long ObservationCountResetValue { get; }
        public double EstimatedSize => _sizeSum / _observationCount;

        public VirtualListItemSizeEstimator() : this(1000, 900) { }
        public VirtualListItemSizeEstimator(long observationCountResetThreshold, long observationCountResetValue)
        {
            if (observationCountResetThreshold < 1)
                throw new ArgumentOutOfRangeException(nameof(observationCountResetThreshold));
            if (observationCountResetValue < 1)
                throw new ArgumentOutOfRangeException(nameof(observationCountResetValue));
            ObservationCountResetThreshold = observationCountResetThreshold;
            ObservationCountResetValue = observationCountResetValue;
        }

        public void AddObservedSize(double size)
        {
            _sizeSum += size;
            _observationCount++;
            if (_observationCount >= ObservationCountResetThreshold)
                _observationCount = ObservationCountResetValue;
        }

        public void RemoveObservedSize(double size)
        {
            if (_observationCount <= 0)
                return;
            _sizeSum -= size;
            _observationCount--;
        }
    }
}
