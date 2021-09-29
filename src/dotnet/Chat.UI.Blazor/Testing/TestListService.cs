using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.UI.Blazor;
using Microsoft.Extensions.Logging;
using Stl.Collections;
using Stl.Fusion;
using Stl.Time.Internal;

namespace ActualChat.Chat.UI.Blazor.Testing
{
    public class TestListService
    {
        private static readonly string[] Words = {"best", "virtual", "scroll", "ever", "100%", "absolutely"};
        private readonly ILogger<TestListService> _log;
        private bool _resetToTop = false;
        private bool _resetToBottom = true;

        public TestListService(ILogger<TestListService> log) => _log = log;

        [ComputeMethod(AutoInvalidateTime = 2)]
        public virtual Task<Range<int>> GetListRange(CancellationToken cancellationToken)
        {
            var count = (int) Math.Round((CoarseClockHelper.Now - CoarseClockHelper.Start).TotalSeconds) / 2;
            return Task.FromResult(new Range<int>(-count / 2, 100 + count));
        }

        [ComputeMethod]
        public virtual async Task<VirtualListResponse<string>> GetItemKeys(
            VirtualListQuery query, CancellationToken cancellationToken)
        {
            var range = await GetListRange(cancellationToken);
            if (query.IncludedRange == default) {
                var key = _resetToBottom ? range.End : _resetToTop ? 0 : range.Start + (range.End - range.Start) / 2;
                query = query with { IncludedRange = new Range<string>(key.ToString(), (key + 20).ToString()) };
            }

            var start = int.Parse(query.IncludedRange.Start);
            if (query.ExpandStartBy > 0)
                start -= (int) query.ExpandStartBy;
            start = Math.Max(range.Start, start);

            var end = int.Parse(query.IncludedRange.End);
            if (query.ExpandEndBy > 0)
                end += (int) query.ExpandEndBy;
            end = Math.Min(range.End, end);

            var result = VirtualListResponse.New(
                Enumerable.Range(start, end - start + 1).Select(i => i.ToString()),
                item => item,
                start == range.Start,
                end == range.End);
            await Task.Delay(100, cancellationToken);
            return result;
        }

        [ComputeMethod]
        public virtual async Task<TestListItem> GetItem(string key, CancellationToken cancellationToken)
        {
            var intKey = int.Parse(key);
            var seed = await GetSeed(cancellationToken);
            var range = await GetListRange(cancellationToken);
            var rnd = new Random(intKey + (intKey + seed) / 10);
            var fontSize = 1 + rnd.NextDouble();
            if (fontSize > 1.95)
                fontSize = 3;
            var wordCount = rnd.Next(20);
            if (intKey == range.End)
                wordCount = await GetWordCount(cancellationToken);
            return new TestListItem(
                intKey,
                $"#{key}",
                Enumerable.Range(0, wordCount).Select(_ => Words[rnd.Next(Words.Length)]).ToDelimitedString(" "),
                fontSize);
        }

        // Protected methods

        [ComputeMethod(AutoInvalidateTime = 2.5)]
        protected virtual Task<int> GetSeed(CancellationToken cancellationToken)
            => Task.FromResult(
                (int) Math.Round((CoarseClockHelper.Now - CoarseClockHelper.Start).TotalMilliseconds * 67) % 101); // 0..100

        [ComputeMethod(AutoInvalidateTime = 0.05)]
        protected virtual Task<int> GetWordCount(CancellationToken cancellationToken)
            => Task.FromResult(
                (int) Math.Round((CoarseClockHelper.Now - CoarseClockHelper.Start).TotalMilliseconds) / 100 % 100); // 0..99
    }
}
