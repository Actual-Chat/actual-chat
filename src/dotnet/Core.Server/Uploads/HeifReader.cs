using System.Buffers.Binary;

namespace ActualChat.Uploads;

/// <summary>
/// Reads what the server needs from a HEIC/HEIF file - which ImageSharp can't open - straight from its
/// ISO BMFF boxes: the primary image's display size and where the Exif/XMP metadata items are stored.
/// </summary>
public static class HeifReader
{
    private static ReadOnlySpan<byte> XmpContentType => "application/rdf+xml"u8;

    public static Size2D? ReadDisplaySize(ReadOnlySpan<byte> data)
    {
        if (!TryReadMeta(data, out var meta) || meta.PrimaryItemId is not { } primaryId)
            return null;

        Size2D? size = null;
        var isRotatedSideways = false;
        foreach (var property in meta.GetItemProperties(primaryId)) {
            var body = data[property.Body];
            if (property.Type == "ispe" && body.Length >= 12)
                size = new Size2D(
                    (int)BinaryPrimitives.ReadUInt32BigEndian(body[4..]),
                    (int)BinaryPrimitives.ReadUInt32BigEndian(body[8..]));
            else if (property.Type == "irot" && body.Length >= 1)
                isRotatedSideways = (body[0] & 1) == 1;
        }
        if (size is not { Width: > 0, Height: > 0 } s)
            return null;

        return isRotatedSideways ? new Size2D(s.Height, s.Width) : s;
    }

    // Returns the absolute file ranges of the Exif and XMP items; an item stored in a way the
    // caller can't overwrite in place (inside another item) makes the whole result null
    public static List<(Range Range, bool IsExif)>? GetMetadataRanges(ReadOnlySpan<byte> data, bool mustKeepHdrXmp)
    {
        if (!TryReadMeta(data, out var meta))
            return null;

        var ranges = new List<(Range Range, bool IsExif)>();
        foreach (var (itemId, isExif) in meta.MetadataItems) {
            if (!meta.Locations.TryGetValue(itemId, out var location))
                continue;
            if (location.ConstructionMethod > 1 || (location.ConstructionMethod == 1 && !meta.HasIdat))
                return null;

            var baseOffset = location.ConstructionMethod == 1 ? meta.IdatBody.Start.Value : 0L;
            var itemRanges = new List<Range>();
            foreach (var (offset, length) in location.Extents) {
                var start = baseOffset + offset;
                if (start < 0 || length < 0 || start + length > data.Length)
                    return null;

                itemRanges.Add(new Range((int)start, (int)(start + length)));
            }
            if (!isExif && mustKeepHdrXmp && ContainsHdrNamespace(data, itemRanges))
                continue;

            ranges.AddRange(itemRanges.Select(r => (r, isExif)));
        }
        return ranges;
    }

    // Private methods

    private static bool TryReadMeta(ReadOnlySpan<byte> data, out Meta meta)
    {
        meta = new Meta();
        try {
            foreach (var box in ReadBoxes(data, 0, data.Length)) {
                if (box.Type != "meta")
                    continue;

                // meta is a FullBox: version + flags precede its children
                foreach (var child in ReadBoxes(data, box.Body.Start.Value + 4, box.Body.End.Value))
                    meta.Read(data, child);
                return meta.PrimaryItemId is not null;
            }
            return false;
        }
        catch (Exception e) when (e is ArgumentOutOfRangeException or IndexOutOfRangeException) {
            // Box sizes and counts come from the file itself, so a truncated one lands here
            return false;
        }
    }

    private static bool ContainsHdrNamespace(ReadOnlySpan<byte> data, List<Range> ranges)
    {
        foreach (var range in ranges)
            if (data[range].IndexOf("hdrgm"u8) >= 0)
                return true;

        return false;
    }

    private static List<Box> ReadBoxes(ReadOnlySpan<byte> data, int start, int end)
    {
        var boxes = new List<Box>();
        var offset = start;
        while (offset + 8 <= end) {
            var size = (long)BinaryPrimitives.ReadUInt32BigEndian(data[offset..]);
            var type = System.Text.Encoding.ASCII.GetString(data.Slice(offset + 4, 4));
            var headerLength = 8;
            if (size == 1) {
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(data[(offset + 8)..]);
                headerLength = 16;
            }
            else if (size == 0)
                size = end - offset;
            if (size < headerLength || offset + size > end)
                break;

            boxes.Add(new Box(type, new Range(offset + headerLength, (int)(offset + size))));
            offset += (int)size;
        }
        return boxes;
    }

    private static long ReadUInt(ReadOnlySpan<byte> data, ref int offset, int size)
    {
        var value = size switch {
            0 => 0L,
            2 => BinaryPrimitives.ReadUInt16BigEndian(data[offset..]),
            4 => BinaryPrimitives.ReadUInt32BigEndian(data[offset..]),
            8 => (long)BinaryPrimitives.ReadUInt64BigEndian(data[offset..]),
            _ => throw new ArgumentOutOfRangeException(nameof(size)),
        };
        offset += size;
        return value;
    }

    // Nested types

    private readonly record struct Box(string Type, Range Body);

    private sealed record ItemLocation(int ConstructionMethod, List<(long Offset, long Length)> Extents);

    private sealed class Meta
    {
        private readonly List<Box> _properties = [];
        private readonly Dictionary<long, List<int>> _associations = [];

        public long? PrimaryItemId { get; private set; }
        public List<(long ItemId, bool IsExif)> MetadataItems { get; } = [];
        public Dictionary<long, ItemLocation> Locations { get; } = [];
        public Range IdatBody { get; private set; }
        public bool HasIdat { get; private set; }

        public void Read(ReadOnlySpan<byte> data, Box box)
        {
            var body = data[box.Body];
            switch (box.Type) {
            case "pitm":
                var pitmOffset = 4;
                PrimaryItemId = ReadUInt(body, ref pitmOffset, body[0] == 0 ? 2 : 4);
                break;
            case "iinf":
                var iinfStart = box.Body.Start.Value + (body[0] == 0 ? 6 : 8);
                foreach (var entry in ReadBoxes(data, iinfStart, box.Body.End.Value))
                    if (entry.Type == "infe")
                        ReadItemInfo(data[entry.Body]);
                break;
            case "iloc":
                ReadLocations(body);
                break;
            case "idat":
                IdatBody = box.Body;
                HasIdat = true;
                break;
            case "iprp":
                foreach (var child in ReadBoxes(data, box.Body.Start.Value, box.Body.End.Value))
                    if (child.Type == "ipco")
                        _properties.AddRange(ReadBoxes(data, child.Body.Start.Value, child.Body.End.Value));
                    else if (child.Type == "ipma")
                        ReadAssociations(data[child.Body]);
                break;
            }
        }

        public IEnumerable<Box> GetItemProperties(long itemId)
            // Property indexes are 1-based; 0 means "no property"
            => _associations.TryGetValue(itemId, out var indexes)
                ? indexes.Where(i => i > 0 && i <= _properties.Count).Select(i => _properties[i - 1]).ToList()
                : [];

        private void ReadItemInfo(ReadOnlySpan<byte> infe)
        {
            // Only version 2+ entries carry an item type, and every current encoder writes those
            var version = infe[0];
            if (version < 2)
                return;

            var offset = 4;
            var itemId = ReadUInt(infe, ref offset, version == 2 ? 2 : 4);
            offset += 2; // item_protection_index
            var itemType = infe.Slice(offset, 4);
            offset += 4;
            if (itemType.SequenceEqual("Exif"u8)) {
                MetadataItems.Add((itemId, true));
                return;
            }
            if (!itemType.SequenceEqual("mime"u8))
                return;

            // item_name and content_type are null-terminated strings
            var rest = infe[offset..];
            var nameEnd = rest.IndexOf((byte)0);
            if (nameEnd >= 0 && rest[(nameEnd + 1)..].StartsWith(XmpContentType))
                MetadataItems.Add((itemId, false));
        }

        private void ReadLocations(ReadOnlySpan<byte> iloc)
        {
            var version = iloc[0];
            var offsetSize = iloc[4] >> 4;
            var lengthSize = iloc[4] & 0xF;
            var baseOffsetSize = iloc[5] >> 4;
            var indexSize = version is 1 or 2 ? iloc[5] & 0xF : 0;
            var offset = 6;
            var itemCount = ReadUInt(iloc, ref offset, version < 2 ? 2 : 4);
            for (var i = 0; i < itemCount; i++) {
                var itemId = ReadUInt(iloc, ref offset, version < 2 ? 2 : 4);
                var constructionMethod = 0;
                if (version is 1 or 2) {
                    constructionMethod = iloc[offset + 1] & 0xF;
                    offset += 2;
                }
                offset += 2; // data_reference_index
                var baseOffset = ReadUInt(iloc, ref offset, baseOffsetSize);
                var extentCount = ReadUInt(iloc, ref offset, 2);
                var extents = new List<(long Offset, long Length)>();
                for (var e = 0; e < extentCount; e++) {
                    offset += indexSize;
                    var extentOffset = ReadUInt(iloc, ref offset, offsetSize);
                    var extentLength = ReadUInt(iloc, ref offset, lengthSize);
                    extents.Add((baseOffset + extentOffset, extentLength));
                }
                Locations[itemId] = new ItemLocation(constructionMethod, extents);
            }
        }

        private void ReadAssociations(ReadOnlySpan<byte> ipma)
        {
            var version = ipma[0];
            var isWideIndex = (ipma[3] & 1) == 1;
            var offset = 4;
            var entryCount = ReadUInt(ipma, ref offset, 4);
            for (var i = 0; i < entryCount; i++) {
                var itemId = ReadUInt(ipma, ref offset, version < 1 ? 2 : 4);
                var count = ipma[offset++];
                var indexes = new List<int>(count);
                for (var a = 0; a < count; a++)
                    indexes.Add(isWideIndex
                        ? (int)(ReadUInt(ipma, ref offset, 2) & 0x7FFF)
                        : ipma[offset++] & 0x7F);
                _associations[itemId] = indexes;
            }
        }
    }
}
