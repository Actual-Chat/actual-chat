namespace ActualChat.UI.Blazor.Components;

public enum BodyVariant
{
    Default = 0, // 1rem inset, 1rem between children
    Flush, // 1rem inset, no gap between children: a list that reads as one block
    Tiles, // settings-style tiled rows
    List, // half the horizontal inset: rows carry the other half, so their text lines up with the title
}
