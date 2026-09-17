using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace ActualChat.ContentCaching;

public sealed partial class FileSystemContentHandler
{
    private sealed record ResponseMetadata(
        HttpStatusCode StatusCode,
        string ReasonPhrase,
        Version Version,
        long? ExpectedLength,
        KeyValuePair<string, string[]>[] Headers,
        KeyValuePair<string, string[]>[] ContentHeaders)
    {
        public static ResponseMetadata FromResponse(HttpResponseMessage response)
        {
            var range = response.Content.Headers.ContentRange;
            var length = response.Content.Headers.ContentLength
                ?? (range is { HasRange: true } ? range.To - range.From + 1 : null);
            return new ResponseMetadata(response.StatusCode, response.ReasonPhrase ?? "", response.Version, length,
                Snapshot(response.Headers), Snapshot(response.Content.Headers));
        }

        public HttpResponseMessage CreateResponse(Stream body, long? length)
        {
            var response = new HttpResponseMessage(StatusCode) {
                ReasonPhrase = ReasonPhrase,
                Version = Version,
                Content = new StreamContent(body),
            };
            foreach (var header in Headers)
                response.Headers.TryAddWithoutValidation(header.Key, header.Value);
            foreach (var header in ContentHeaders)
                response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            response.Content.Headers.ContentLength = length;
            return response;
        }

        public bool Matches(HttpResponseMessage response)
        {
            var other = FromResponse(response);
            return StatusCode == other.StatusCode
                && (ExpectedLength == null || other.ExpectedLength == null || ExpectedLength == other.ExpectedLength)
                && Find(Headers, "ETag") == Find(other.Headers, "ETag")
                && Find(ContentHeaders, "Content-Range") == Find(other.ContentHeaders, "Content-Range")
                && Find(ContentHeaders, "Content-Type") == Find(other.ContentHeaders, "Content-Type");
        }

        public byte[] Serialize()
        {
            using var buffer = new MemoryStream();
            using var writer = new BinaryWriter(buffer, Encoding.UTF8, true);
            writer.Write((int)StatusCode);
            writer.Write(ReasonPhrase);
            writer.Write(Version.Major);
            writer.Write(Version.Minor);
            writer.Write(ExpectedLength ?? -1);
            WriteHeaders(writer, Headers);
            WriteHeaders(writer, ContentHeaders);
            return buffer.ToArray();
        }

        public static ResponseMetadata Deserialize(byte[] bytes)
        {
            using var buffer = new MemoryStream(bytes, false);
            using var reader = new BinaryReader(buffer, Encoding.UTF8, true);
            var status = (HttpStatusCode)reader.ReadInt32();
            var reason = reader.ReadString();
            var version = new Version(reader.ReadInt32(), reader.ReadInt32());
            var length = reader.ReadInt64();
            if (length < -1)
                throw new InvalidDataException("Invalid cached content length.");

            var metadata = new ResponseMetadata(status, reason, version, length < 0 ? null : length,
                ReadHeaders(reader), ReadHeaders(reader));
            if (buffer.Position != buffer.Length)
                throw new InvalidDataException("Trailing response metadata.");

            return metadata;
        }

        // Private methods

        // Set-Cookie never reaches disk, so a shared entry can't replay one reader's cookie
        private static KeyValuePair<string, string[]>[] Snapshot(HttpHeaders headers)
            => headers
                .Where(x => !x.Key.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase))
                .Select(x => KeyValuePair.Create(x.Key, x.Value.ToArray()))
                .ToArray();

        private static string? Find(KeyValuePair<string, string[]>[] headers, string name)
            => headers.FirstOrDefault(x => x.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value is { } values
                ? string.Join(", ", values)
                : null;

        private static void WriteHeaders(BinaryWriter writer, KeyValuePair<string, string[]>[] headers)
        {
            writer.Write(headers.Length);
            foreach (var header in headers) {
                writer.Write(header.Key);
                writer.Write(header.Value.Length);
                foreach (var value in header.Value)
                    writer.Write(value);
            }
        }

        private static KeyValuePair<string, string[]>[] ReadHeaders(BinaryReader reader)
        {
            var headers = new KeyValuePair<string, string[]>[ReadCount(reader)];
            for (var i = 0; i < headers.Length; i++) {
                var name = reader.ReadString();
                var values = new string[ReadCount(reader)];
                for (var j = 0; j < values.Length; j++)
                    values[j] = reader.ReadString();
                headers[i] = KeyValuePair.Create(name, values);
            }
            return headers;
        }

        private static int ReadCount(BinaryReader reader)
        {
            var count = reader.ReadInt32();
            if (count is < 0 or > 1024)
                throw new InvalidDataException("Invalid cached header count.");

            return count;
        }
    }
}
