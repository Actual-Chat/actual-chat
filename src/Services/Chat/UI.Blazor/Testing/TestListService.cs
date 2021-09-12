using System;
using System.Collections.Generic;
using System.Linq;
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
        private bool _resetToBottom = false;

        public TestListService(ILogger<TestListService> log) => _log = log;

        [ComputeMethod(AutoInvalidateTime = 1)]
        public virtual async Task<VirtualListResponse<TestListItem>> GetItems(
            VirtualListQuery query, CancellationToken cancellationToken = default)
        {
            var seed = (int) Math.Floor((CoarseClockHelper.Now - CoarseClockHelper.Start).TotalSeconds);
            _log.LogInformation("GetItems({Query}), seed = {Seed}", query, seed);
            var veryLastKey = 50 + seed * 2;
            var veryFistKey = 0 - seed / 2;
            if (query.IncludedRange == default) {
                var key = _resetToBottom ? veryLastKey : _resetToTop ? 0 : veryLastKey / 2;
                query = query with { IncludedRange = new Range<string>(key.ToString(), (key + 20).ToString()) };
            }

            var startKey = int.Parse(query.IncludedRange.Start);
            if (query.ExpandStartBy > 0)
                startKey -= (int) query.ExpandStartBy;
            startKey = Math.Max(veryFistKey, startKey);

            var endKey = int.Parse(query.IncludedRange.End);
            if (query.ExpandEndBy > 0)
                endKey += (int) query.ExpandEndBy;
            endKey = Math.Min(veryLastKey, endKey);

            var result = VirtualListResponse.New(
                Enumerable
                    .Range(startKey, endKey - startKey + 1)
                    .Select(key => CreateItem(key, seed)),
                item => item.Key.ToString(),
                startKey == veryFistKey,
                endKey == veryLastKey);
            await Task.Delay(100, cancellationToken);
            return result;

        }

        private TestListItem CreateItem(int key, int seed)
        {
            var rnd = new Random(key + (key + seed) / 10);
            return new TestListItem(
                key,
                $"#{key}",
                Enumerable.Range(0, rnd.Next(20)).Select(_ => Words[rnd.Next(Words.Length)]).ToDelimitedString(" "),
                1 + rnd.NextDouble());
        }
    }
}
