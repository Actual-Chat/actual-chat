namespace Sample;

public class Rules
{
    private volatile int _flag;

    public int Guarded(int x)
    {
        if (x < 0) return 0;
        if (x == 0)
            return 1;
        if (x == 1)
            return 2;

        return x;
    }

    public void Braced() {
        Console.WriteLine("brace should be on the next line");
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Category", "Rule:Justification is long")] public int Tagged { get; }
}
