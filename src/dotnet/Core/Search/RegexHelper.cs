using Cysharp.Text;

namespace ActualChat.Search;

public static class RegexHelper
{
    private const byte Q = 5;    // quantifier
    private const byte S = 4;    // ordinary stopper
    private const byte Z = 3;    // ScanBlank stopper
    private const byte X = 2;    // whitespace
    private const byte E = 1;    // should be escaped

    // This code is copied from RegexParser
    private static ReadOnlySpan<byte> Category => new byte[] {
        // 0  1  2  3  4  5  6  7  8  9  A  B  C  D  E  F  0  1  2  3  4  5  6  7  8  9  A  B  C  D  E  F
        0, 0, 0, 0, 0, 0, 0, 0, 0, X, X, 0, X, X, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
        //    !  "  #  $  %  &  '  (  )  *  +  ,  -  .  /  0  1  2  3  4  5  6  7  8  9  :  ;  <  =  >  ?
        X, 0, 0, Z, S, 0, 0, 0, S, S, Q, Q, 0, 0, S, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, Q,
        // @  A  B  C  D  E  F  G  H  I  J  K  L  M  N  O  P  Q  R  S  T  U  V  W  X  Y  Z  [  \  ]  ^  _
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, S, S, 0, S, 0,
        // '  a  b  c  d  e  f  g  h  i  j  k  l  m  n  o  p  q  r  s  t  u  v  w  x  y  z  {  |  }  ~
        0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, Q, S, 0, 0, 0};

    public static bool IsMetaChar(char ch)
        => ch <= '|' && Category[ch] >= E;

    public static void Escape(ReadOnlySpan<char> text, ref Utf16ValueStringBuilder sb)
    {
        foreach (var c in text)
            Escape(c, ref sb);
    }

    public static void Escape(char c, ref Utf16ValueStringBuilder sb)
    {
        if (!IsMetaChar(c)) {
            sb.Append(c);
            return;
        }
        sb.Append('\\');
        sb.Append(c switch {
            '\n' => 'n',
            '\r' => 'r',
            '\t' => 't',
            '\f' => 'f',
            _ => c,
        });
    }
}
