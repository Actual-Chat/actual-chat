using ActualChat.Chat.ML;

namespace ActualChat.MLSearch.IntegrationTests.Indexing.ChatContent;

public class EmbeddingsTest(ITestOutputHelper @out) : TestBase(@out)
{
    private readonly EmbeddingsCalculator _embeddingCalculator = new (new EmbeddingSettings());

    [Fact(Skip = "Run explicitly")]
    public async Task CompareVectors()
    {
        var x = await _embeddingCalculator.CalculateVector("Hello", CancellationToken.None);
        x.Should().NotBeEmpty();

        var similarity1 = _embeddingCalculator.CosineSimilarity(x, x);
        similarity1.Should().Be(1.0);

        var y = _embeddingCalculator.Normalize(x);
        var similarity2 = _embeddingCalculator.CosineSimilarity(y, y);
        similarity2.Should().Be(1.0);
    }

    [Fact(Skip = "Run explicitly")]
    public async Task CompareDocs()
    {
        string[] docs = [
            "what is the capital of China?",
            "how to implement quick sort in python?",
            "北京",
            "快排算法介绍",
            "Beijing",
            "Пекин"
        ];

        var vectors = await docs.Select(c => _embeddingCalculator.CalculateVector(c, CancellationToken.None)).Collect();
        var vectors2 = vectors.Select(_embeddingCalculator.Normalize).ToArray();
        for (var i = 0; i < vectors2.Length - 1; i++) {
            for (int j = i + 1; j < vectors2.Length; j++) {
                var similarity1 = _embeddingCalculator.CosineSimilarity(_embeddingCalculator.Normalize(vectors2[i]), _embeddingCalculator.Normalize(vectors2[j]));
                WriteLine($"Similarity('{docs[i]}' vs. '{docs[j]}'): " + similarity1);
            }
        }
    }

    [Theory(Skip = "Run explicitly")]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ProcessChatHistory(bool addAuthor)
    {
        var entries = new[] {
            new Entry(AuthorNick.AndreyY, "дипграм на проде не работает - это нормально?"),
            new Entry(AuthorNick.Frol, "так вроде не обновляли прод?"),
            new Entry(AuthorNick.AndreyY, "то есть нормально?"),
            new Entry(AuthorNick.Frol, "видимо да"),
            new Entry(AuthorNick.DmitriiF, "настройка в settings есть"),
            new Entry(AuthorNick.Frol, "на проде еще нет ее"),
            new Entry(AuthorNick.AlexeyK, "Там чуть поменялось у них что-то"),
            new Entry(AuthorNick.AlexeyK, "Скорее всего после обновления прода заработает"),

            new Entry(AuthorNick.Frol, "мне казалось, что у нас только один кластер остался"),
            new Entry(AuthorNick.AlexeyK, "вообще его нет"),
            new Entry(AuthorNick.Frol, "там вообще много ошибок в логах"),
            new Entry(AuthorNick.Frol, "и опентелеметрия гадит активно ошибками"),
            new Entry(AuthorNick.AlexeyK, "говно на палке"),
        };

        var prevVectors = new List<double[]>();
        int windowSize = 5;
        for (int i = 0; i < entries.Length + windowSize - 1; i++) {
            if (i > 0)
                WriteLine("");
            var range = Enumerable.Range(i - windowSize + 1, windowSize).ToArray();
            var fragmentEntries = range
                .Where(c => c >= 0 && c < entries.Length)
                .Select(c => entries[c])
                .ToArray();
            var fragment = EntriesToText(fragmentEntries, addAuthor);
            WriteLine("Iteration " + i);
            WriteLine("Fragment:");
            WriteLine(fragment);
            var vector = await _embeddingCalculator.CalculateVector(fragment, CancellationToken.None);
            vector = _embeddingCalculator.Normalize(vector);
            // if (prevVector.Length > 0) {
            //     var similarity1 = CosineSimilarity(prevVector, vector);
            //     WriteLine("Similarity with prev:" + similarity1);
            // }
            if (prevVectors.Count > 0) {
                var similarities = new List<double>();
                foreach (var prevVector in prevVectors)
                {
                    var similarity = _embeddingCalculator.CosineSimilarity(prevVector, vector);
                    similarities.Add(similarity);
                }
                WriteLine("Similarity with prevs:");
                WriteLine(string.Join(" ", similarities.Select((c, i) => new { Index = i, Value = c }).Reverse().Select(c => $"{c.Value} [{c.Index}]")));
            }
            prevVectors.Add(vector);
        }
    }

    private string EntriesToText(IEnumerable<Entry> entries, bool addAuthor)
    {
        var sb = ActualLab.Text.StringBuilderExt.Acquire();
        foreach (var entry in entries) {
            if (sb.Length > 0)
                sb.AppendLine();
            if (addAuthor) {
                sb.Append(GetAuthor(entry.AuthorId));
                sb.Append(" : ");
            }
            sb.Append(entry.Text);
        }
        return sb.ToStringAndRelease();
    }

    private string GetAuthor(AuthorNick authorId)
    {
        switch (authorId) {
            case AuthorNick.AndreyY: return "Andrey Y";
            case AuthorNick.Frol: return "Frol";
            case AuthorNick.DmitriiF: return "Dmitrii Filippov";
            case AuthorNick.AlexeyK: return "Alexey Kochetov";
            default:
                throw new ArgumentOutOfRangeException(authorId.ToString());
        }
    }

    private enum AuthorNick {
        AndreyY,
        Frol,
        DmitriiF,
        AlexeyK
    }

    private record Entry(AuthorNick AuthorId, string Text);
}
