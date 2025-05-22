using ActualLab.Mathematics;

namespace ActualChat.Core.Server.UnitTests;

public class EnumerableExtTest
{
    [Fact]
    public void Merge_BothSequencesEmpty_ReturnsEmpty()
        => Merge([], []).Should().BeEmpty();

    [Fact]
    public void Merge_LeftOnlySequence_ReturnsLeftElementsWithDefaultRights()
        => Merge([1, 2, 3], [])
            .Should()
            .Equal((1, null), (2, null), (3, null));

    [Fact]
    public void Merge_RightOnlySequence_ReturnsRightElementsWithDefaultLefts()
        => Merge([], [4, 5, 6])
            .Should()
            .Equal((null, 4), (null, 5), (null, 6));

    [Fact]
    public void Merge_NoOverlappingElements_ReturnsInterleavedAsymetricPairs()
        => Merge([1, 3, 5], [2, 4, 6])
            .Should()
            .Equal(
                (1, null),
                (null, 2),
                (3, null),
                (null, 4),
                (5, null),
                (null, 6));

    [Fact]
    public void Merge_WithOverlappingElements_ReturnsCorrectMatchesAndSingles()
        => Merge([1, 2, 4], [2, 3, 4])
            .Should()
            .Equal(
                (1, null),
                (2, 2),
                (null, 3),
                (4, 4));

    [Fact]
    public void Merge_WithDuplicatesOnBothSides_ReturnsCartesianProductOfDuplicates()
        // Two 2’s on each side → 2 × 2 = 4 pairs
        => Merge([2, 2], [2, 2])
            .Should()
            .Equal(
                (2, 2),
                (2, 2),
                (2, 2),
                (2, 2));

    [Fact]
    public void Merge_DuplicatesOnlyOnLeft_AllLeftDuplicatesPairedWithSingleRightItem()
        // Left has three 1’s, right has one 1 → 3 × 1 = 3 pairs
        => Merge([1, 1, 1], [1])
            .Should()
            .Equal(
                (1, 1),
                (1, 1),
                (1, 1));

    [Fact]
    public void Merge_DuplicatesOnlyOnRight_AllRightDuplicatesPairedWithSingleLeftItem()
        // Left has one 1, right has three 1’s → 1 × 3 = 3 pairs
        => Merge([1], [1, 1, 1])
            .Should()
            .Equal(
                (1, 1),
                (1, 1),
                (1, 1));

    [Fact]
    public void Merge_MixedDuplicatesAndUniques_CreatesCartesianProductsAndKeepsOrdering()
        /*  Left : 1, 1, 2, 2
         *  Right: 1, 2, 2, 3
         *
         *  Expected:
         *    (1,1) ×2
         *    (2,2) ×4  (2 duplicates on each side)
         *    trailing right-only 3
         */
        => Merge([1, 1, 2, 2], [1, 2, 2, 3])
            .Should()
            .Equal(
                (1, 1),
                (1, 1),
                (2, 2),
                (2, 2),
                (2, 2),
                (2, 2),
                (null, 3));

    [Fact]
    public void Merge_LongRangesWithDuplicateStarts_ProducesCartesianProductAndSingles()
    {
        // Arrange
        IEnumerable<Range<long>> left =
        [
            new (0, 10),
            new (10, 15),
            new (15, 20),
            new (21, 25),
        ];

        IEnumerable<Range<long>> right =
        [
            new (10, 15),
            new (15, 30),
        ];

        // Act
        var result = left.Merge(
                right,
                (l, r) => l.IntersectWith(r).IsEmpty ? (int)(l.Start - r.Start) : 0)
            .ToList();

        // Assert
        result.Should().Equal(
            (new (0, 10), default),
            (new (10, 15), new (10, 15)),
            (new (15, 20),  new (15, 30)),
            (new (21, 25),  new (15, 30))
        );
    }

    [Fact]
    public void Merge_LongRanges_Test()
    {
        const string left = """
            [
                { "Start": 1, "End": 27 },
                { "Start": 28, "End": 31 },
                { "Start": 149, "End": 169 },
                { "Start": 228, "End": 266 },
                { "Start": 304, "End": 317 },
                { "Start": 1226, "End": 1234 },
                { "Start": 1238, "End": 1264 },
                { "Start": 1947, "End": 2560 },
                { "Start": 3840, "End": 4239 },
                { "Start": 4241, "End": 4656 },
                { "Start": 4657, "End": 4669 },
                { "Start": 4670, "End": 4925 },
                { "Start": 4926, "End": 5120 }
            ]
            """;

        const string right = """
            [
                { "Start": 1, "End": 140 },
                { "Start": 236, "End": 283 },
                { "Start": 295, "End": 339 },
                { "Start": 711, "End": 892 },
                { "Start": 966, "End": 1236 },
                { "Start": 1991, "End": 2343 },
                { "Start": 2491, "End": 2687 },
                { "Start": 4001, "End": 4195 },
                { "Start": 4198, "End": 4462 },
                { "Start": 4462, "End": 4678 },
                { "Start": 4678, "End": 4923 },
                { "Start": 4923, "End": 5085 },
                { "Start": 5100, "End": 5449 }
            ]
            """;

        var leftRanges = JsonSerializer.Deserialize<Range<long>[]>(left);
        var rightRanges = JsonSerializer.Deserialize<Range<long>[]>(right);
        var result = leftRanges.Merge(
                rightRanges,
                l => l,
                r => r,
                (l, r) => l.IntersectWith(r).IsEmpty ? (int)(l.Start - r.Start) : 0)
            .ToList();
        result.Should().Equal([
            (new Range<long>(1, 27), new Range<long>(1, 140)),
            (new Range<long>(28, 31), new Range<long>(1, 140)),
            (new Range<long>(149, 169), new Range<long>()),
            (new Range<long>(228, 266), new Range<long>(236, 283)),
            (new Range<long>(304, 317), new Range<long>(295, 339)),
            (new Range<long>(), new Range<long>(711, 892)),
            (new Range<long>(1226, 1234), new Range<long>(966, 1236)),
            (new Range<long>(1238, 1264), new Range<long>()),
            (new Range<long>(1947, 2560), new Range<long>(1991, 2343)),
            (new Range<long>(1947, 2560), new Range<long>(2491, 2687)),
            (new Range<long>(3840, 4239), new Range<long>(4001, 4195)),
            (new Range<long>(3840, 4239), new Range<long>(4198, 4462)), // Issue starts there
            (new Range<long>(4241, 4656), new Range<long>(4198, 4462)),
            (new Range<long>(4241, 4656), new Range<long>(4462, 4678)),
            (new Range<long>(4657, 4669), new Range<long>(4462, 4678)),
            (new Range<long>(4670, 4925), new Range<long>(4462, 4678)),
            (new Range<long>(4670, 4925), new Range<long>(4678, 4923)),
            (new Range<long>(4670, 4925), new Range<long>(4923, 5085)),
            (new Range<long>(4926, 5120), new Range<long>(4923, 5085)),
            (new Range<long>(4926, 5120), new Range<long>(5100, 5449)),
        ]);
    }

    [Fact]
    public void Merge_LongRanges_ShouldReturnMatchedPairsForLastRight()
    {
        const string left = """
            [
                {"Start":885,"End":892},
                {"Start":893,"End":980},
                {"Start":981,"End":1000},
                {"Start":1001,"End":1024},
                {"Start":1213,"End":1225},
                {"Start":1226,"End":1234},
                {"Start":1235,"End":1236},
                {"Start":1238,"End":1264}
            ]
            """;

        const string right = """
             [
                {"Start":711,"End":892},
                {"Start":966,"End":1236}
             ]
             """;

        var leftRanges = JsonSerializer.Deserialize<Range<long>[]>(left);
        var rightRanges = JsonSerializer.Deserialize<Range<long>[]>(right);
        var result = leftRanges.Merge(
                rightRanges,
                l => l,
                r => r,
                (l, r) => l.IntersectWith(r).IsEmpty ? (int)(l.Start - r.Start) : 0)
            .ToList();
        /*result.Should().Equal([
            (new Range<long>(885, 892), new Range<long>(711, 892)),
            (new Range<long>(893, 980), new Range<long>(711, 892)),
            (new Range<long>(981, 1000), new Range<long>(966, 1236)),
            (new Range<long>(1001, 1024), new Range<long>(966, 1236)),
            (new Range<long>(1213, 1225), new Range<long>(966, 1236)),
            (new Range<long>(1226, 1234), new Range<long>(966, 1236)),
            (new Range<long>(1235, 1236), new Range<long>(966, 1236)),
            (new Range<long>(1238, 1264), new Range<long>()),

        ]);*/
    }

    [Fact]
    public void Merge_LongRanges_ReducedTest()
    {
        const string left = """
            [
                { "Start": 3840, "End": 4239 },
                { "Start": 4241, "End": 4656 },
                { "Start": 4657, "End": 4669 },
                { "Start": 4670, "End": 4925 }
            ]
            """;

        const string right = """
            [
                { "Start": 4001, "End": 4195 },
                { "Start": 4198, "End": 4462 },
                { "Start": 4462, "End": 4678 },
                { "Start": 4678, "End": 4923 },
                { "Start": 4923, "End": 5085 }
            ]
            """;

        var leftRanges = JsonSerializer.Deserialize<Range<long>[]>(left);
        var rightRanges = JsonSerializer.Deserialize<Range<long>[]>(right);
        var result = leftRanges.Merge(
                rightRanges,
                l => l,
                r => r,
                (l, r) => l.IntersectWith(r).IsEmpty ? (int)(l.Start - r.Start) : 0)
            .ToList();
        result.Should().Equal([
            (new Range<long>(3840, 4239), new Range<long>(4001, 4195)),
            (new Range<long>(3840, 4239), new Range<long>(4198, 4462)), // Issue starts there
            (new Range<long>(4241, 4656), new Range<long>(4198, 4462)),
            (new Range<long>(4241, 4656), new Range<long>(4462, 4678)),
            (new Range<long>(4657, 4669), new Range<long>(4462, 4678)),
            (new Range<long>(4670, 4925), new Range<long>(4462, 4678)),
            (new Range<long>(4670, 4925), new Range<long>(4678, 4923)),
            (new Range<long>(4670, 4925), new Range<long>(4923, 5085)),
        ]);
    }

    private static IReadOnlyList<(int? Left, int? Right)> Merge(
        IEnumerable<int> left,
        IEnumerable<int> right)
        => left.Merge(right)
            .Select(t => (t.Left == 0 ? (int?)null : t.Left, t.Right == 0 ? (int?)null : t.Right))
            .ToList();
}
