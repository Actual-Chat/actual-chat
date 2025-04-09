using System.Text;
using System.Xml;
using System.Xml.Serialization;

namespace ActualChat.Chat;

public class LanguageDetectionSerializer(IServiceProvider services)
{
    [field: AllowNull, MaybeNull]
    private ILogger Log => field ??= services.LogFor(GetType());

    public string SerializeRequest(IReadOnlyList<string> texts)
    {
        var sb = new StringBuilder();
        using var writer = XmlWriter.Create(sb, new () { Indent = true, IndentChars = " ", OmitXmlDeclaration = true});
        Request.Serializer.Serialize(writer, Request.From(texts));
        return sb.ToString();
    }

    public IReadOnlyList<Language[]> DeserializeResponse(string content, int expectedCount)
    {
        try {
            using var stringReader = new StringReader(content);
            using var xmlReader = new XmlTextReader(stringReader);
            xmlReader.Namespaces = false;
            xmlReader.DtdProcessing = DtdProcessing.Ignore;
            var response = (Response?)Response.Serializer.Deserialize(xmlReader);
            if (response is null || response.Languages.Count == 0)
                return Empty(expectedCount);

            var resultMap =
                response.Languages.ToDictionary(x => x.Id, x => x.Languages.Select(Language.ParseOrNone).Distinct().ToArray());
            return [..Enumerable.Range(1, expectedCount).Select(i => resultMap.GetValueOrDefault(i, []))];
        }
        catch (Exception e) {
            Log.LogWarning(e, "Could not deserialize language detection response. \n [[{Response}]]", content);
            return Empty(expectedCount);
        }
    }

    private static IReadOnlyList<Language[]> Empty(int expectedCount)
        => Enumerable.Repeat(Array.Empty<Language>(), expectedCount).ToArray();

    // Nested types

    [XmlRoot("request", Namespace = "")]
    public sealed class Request
    {
        public static readonly XmlSerializer Serializer = new(typeof(Request));

        [XmlElement("e")]
        public List<Entry> Entries { get; set; } = [];

        public static Request From(IEnumerable<string> texts)
            => new() { Entries = [..texts.Select((x, i) => new Entry { Id = i + 1, Content = x })] };
    }

    public sealed class Entry
    {
        [XmlAttribute("id")]
        public int Id { get; set; }

        [XmlText]
        public string Content { get; set; } = "";
    }

    [XmlRoot("response", Namespace = "")]
    public sealed class Response
    {
        public static readonly XmlSerializer Serializer = new(typeof(Response));

        [XmlElement("e")]
        public List<DetectedLanguages> Languages { get; set; } = [];
    }

    public sealed class DetectedLanguages
    {
        [XmlAttribute("id")]
        public int Id { get; set; }

        [XmlElement("l")]
        public List<string> Languages { get; set; } = [];
    }
}
