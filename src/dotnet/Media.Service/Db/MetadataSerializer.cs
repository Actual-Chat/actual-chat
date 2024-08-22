using ActualLab.Collections.Internal;

namespace ActualChat.Media.Db;

public static class MetadataSerializer
{
    public static PropertyBag Read(string? metadataJson)
    {
        if (metadataJson.IsNullOrEmpty())
            return default;

        var items = NewtonsoftJsonSerializer.Default.Read<ImmutableOptionSet>(metadataJson).Items;
        if (items.Count == 0)
            return default;

        var newItems = new PropertyBagItem[items.Count];
        var i = 0;
        foreach (var (key, value) in items)
            newItems[i++] = new PropertyBagItem(key, TypeDecoratingUniSerialized.New(value));
        return new PropertyBag(newItems);
    }

    public static string Write(PropertyBag metadata)
    {
        if (metadata.Count == 0)
            return "";

        var optionSet = ImmutableOptionSet.Empty;
        foreach (var item in metadata.Items)
            optionSet = optionSet.Set(item.Key, item.Value);

        return NewtonsoftJsonSerializer.Default.Write(optionSet);
    }
}
