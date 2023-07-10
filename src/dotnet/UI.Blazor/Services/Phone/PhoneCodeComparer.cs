namespace ActualChat.UI.Blazor.Services;

public class PhoneCodeComparer : IEqualityComparer<string>, IEqualityComparer<PhoneCode>
{
    public static readonly PhoneCodeComparer Instance = new ();

    public bool Equals(string? x, string? y)
    {
        if (x is null && y is null)
            return true;

        if (x is null || y is null)
            return false;

        return OrdinalEquals(Phone.Normalize(x), Phone.Normalize(y));
    }

    public int GetHashCode(string obj)
        => Phone.Normalize(obj).OrdinalHashCode();

    public bool Equals(PhoneCode? x, PhoneCode? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (ReferenceEquals(x, null)) return false;
        if (ReferenceEquals(y, null)) return false;
        if (x.GetType() != y.GetType()) return false;

        return Equals(x.Code, x.Code);
    }

    public int GetHashCode(PhoneCode obj)
        => GetHashCode(obj.Code);
}
