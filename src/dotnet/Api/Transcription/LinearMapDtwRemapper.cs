// Copyright (c) ActualChat
// DTW-based time map realigner with trimmed-core similarity.
// - Pure DTW alignment (monotonic), no edit-distance counts.
// - Int128 trigram signatures for robust cross-ASR matching.
// - Leading/trailing trim detection (optional per mode).
// - Similarity computed on trimmed core only.
// - Produces LinearMap whose X = character offset in NEW text, Y = seconds.

using System.Numerics;
using System.Text.RegularExpressions;

namespace ActualChat.Transcription
{
    // ---------------------------------------------------------------
    // Alignment mode: control trim behavior & DTW band defaults.
    // ---------------------------------------------------------------
    public enum AlignmentMode
    {
        RetranscribeSameAudio, // trims presumed zero, tighter band
        UserEditedTranscript // detect leading/trailing trims
    }

    // ---------------------------------------------------------------
    // Public entry: DTW remap + (optional) trims + core similarity gate.
    // Produces new LinearMap: X=new char offsets, Y=seconds.
    // ---------------------------------------------------------------
    public static class LinearMapDtwRemapper
    {
        public static LinearMap Remap(
            string oldText,
            string newText,
            LinearMap oldMap,
            AlignmentMode mode = AlignmentMode.RetranscribeSameAudio,
            int? bandWidth = null,
            float similarityThreshold = 0.70f) // Option B as requested
        {
            // Tokenize
            var oldToks = Tokenizer.TokenizeWithOffsets(oldText);
            var newToks = Tokenizer.TokenizeWithOffsets(newText);

            // Signatures
            var oldSig = new Int128[oldToks.Count];
            var newSig = new Int128[newToks.Count];
            for (int i = 0; i < oldToks.Count; i++) oldSig[i] = Trigram128.Signature(oldToks[i].Text);
            for (int j = 0; j < newToks.Count; j++) newSig[j] = Trigram128.Signature(newToks[j].Text);

            // DTW band defaults per mode (can be overridden)
            int bw = bandWidth ?? (mode == AlignmentMode.RetranscribeSameAudio ? 64 : 128);

            // DTW new->old mapping
            var newToOld = Dtw.Map(oldSig, newSig, bw);

            // Build alignment (trims + similarity on core)
            var alignment = AlignmentBuilder.Build(oldToks, newToks, newToOld, mode);

            // Reject if core is empty or too dissimilar
            if (alignment.CoreLength == 0 || alignment.Similarity < similarityThreshold)
                return LinearMap.Zero;

            // Sample old times once (old token starts)
            var oldTimes = MapSampling.SampleOldTokenTimes(oldMap, oldToks);

            // Build new points over the core: (x=new char offset, y=old time)
            var points = new List<Vector2>(alignment.CoreLength);
            for (int j = alignment.CoreStart; j <= alignment.CoreEnd; j++) {
                int iMap = alignment.NewToOld[j];
                if ((uint)iMap >= (uint)oldTimes.Length)
                    continue;

                float x = newToks[j].Start; // NEW char offset
                float y = oldTimes[iMap]; // OLD time at mapped token

                // Enforce strictly increasing X (safety; DTW + tokenization *should* ensure)
                if (points.Count > 0 && x <= points[^1].X)
                    continue;

                points.Add(new Vector2(x, y));
            }

            if (points.Count == 0)
                return LinearMap.Zero;

            // Light smoothing to remove single-token jitter without changing meaning
            var map = new LinearMap(CollectionsMarshal.AsSpan(points))
                .Simplify(new Vector2(0.01f, 0.005f)); // tweak if needed

            return map.IsValid() ? map : map.RequireValid();
        }

        // Nested types

        // ---------------------------------------------------------------
        // Token: substring view (no custom equality). Implicit from string.
        // ---------------------------------------------------------------
        public readonly struct Token
        {
            public string Text { get; }
            public int Start { get; }
            public int Length { get; }

            public Token(string text, int start, int length)
            {
                Text = text;
                Start = start;
                Length = length;
            }

            // Standalone token covering the whole string
            public static implicit operator Token(string text)
                => new (text, 0, text.Length);

            public override string ToString()
            {
                try {
                    return $"{Text.AsSpan(Start, Length).ToString()} @ {Start}..{Start + Length - 1}";
                }
                catch {
                    return $"{Text} @ {Start}..{Start + Length - 1}";
                }
            }
        }

        // ---------------------------------------------------------------
        // Word tokenizer that preserves original offsets.
        // ---------------------------------------------------------------
        public static class Tokenizer
        {
            private static readonly Regex WordRegex =
                new (@"\p{L}[\p{L}\p{M}\p{N}'’_-]*",
                    RegexOptions.Compiled | RegexOptions.CultureInvariant);

            public static List<Token> TokenizeWithOffsets(string text)
            {
                var list = new List<Token>();
                if (string.IsNullOrEmpty(text))
                    return list;

                // NOTE: We normalize token TEXT to lowercase for robust matching,
                // but Start/Length remain positions into the original string.
                foreach (Match m in WordRegex.Matches(text.ToLowerInvariant()))
                    list.Add(new Token(m.Value, m.Index, m.Length));
                return list;
            }
        }

        // ---------------------------------------------------------------
        // Int128 trigram hashing (model-free, robust to small variations).
        // ---------------------------------------------------------------
        public static class Trigram128
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static int HashTrigram(char a, char b, char c)
            {
                int h = a;
                h = unchecked(h * 31 + b);
                h = unchecked(h * 31 + c);
                return h & 0x7F; // 0..127 -> bit position
            }

            public static Int128 Signature(string token)
            {
                if (string.IsNullOrEmpty(token) || token.Length < 3)
                    return Int128.Zero;

                var s = token.AsSpan();
                Int128 bits = Int128.Zero;
                for (int i = 0; i < s.Length - 2; i++) {
                    int pos = HashTrigram(s[i], s[i + 1], s[i + 2]);
                    bits |= (Int128.One << pos);
                }
                return bits;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static int HammingDistance(Int128 a, Int128 b)
            {
                Int128 x = a ^ b;
                ulong lo = (ulong)x;
                ulong hi = (ulong)(x >> 64);
                return BitOperations.PopCount(lo) + BitOperations.PopCount(hi);
            }
        }

        // ---------------------------------------------------------------
        // Alignment result (pure DTW + trims + core similarity).
        // ---------------------------------------------------------------
        public sealed class Alignment
        {
            public int[] NewToOld { get; }
            public int LeadingTrim { get; }
            public int TrailingTrim { get; }

            public int NewLength => NewToOld.Length;
            public int CoreStart => LeadingTrim;
            public int CoreEnd => NewLength - TrailingTrim - 1;
            public int CoreLength => Math.Max(0, CoreEnd - CoreStart + 1);

            // Similarity over trimmed core: % of exact text matches
            public double Similarity { get; }

            public Alignment(int[] newToOld, int leadingTrim, int trailingTrim, double similarity)
            {
                NewToOld = newToOld;
                LeadingTrim = leadingTrim;
                TrailingTrim = trailingTrim;
                Similarity = similarity;
            }
        }

        // ---------------------------------------------------------------
        // DTW mapper over Int128 signatures (monotonic; Sakoe–Chiba band).
        // Returns new->old monotonic index mapping.
        // ---------------------------------------------------------------
        public static class Dtw
        {
            public static int[] Map(ReadOnlySpan<Int128> oldSig, ReadOnlySpan<Int128> newSig, int bandWidth)
            {
                int n = oldSig.Length;
                int m = newSig.Length;

                const int inf = int.MaxValue / 4;
                var dp = new int[n + 1, m + 1];
                var step = new byte[n + 1, m + 1]; // 1=diag, 2=up, 3=left

                for (int i = 0; i <= n; i++)
                for (int j = 0; j <= m; j++)
                    dp[i, j] = inf;

                dp[0, 0] = 0;

                for (int i = 1; i <= n; i++) {
                    int jMin = Math.Max(1, i - bandWidth);
                    int jMax = Math.Min(m, i + bandWidth);

                    for (int j = jMin; j <= jMax; j++) {
                        int cost = Trigram128.HammingDistance(oldSig[i - 1], newSig[j - 1]);

                        int best = dp[i - 1, j - 1];
                        byte which = 1;
                        if (dp[i - 1, j] < best) {
                            best = dp[i - 1, j];
                            which = 2;
                        }
                        if (dp[i, j - 1] < best) {
                            best = dp[i, j - 1];
                            which = 3;
                        }

                        dp[i, j] = best + cost;
                        step[i, j] = which;
                    }
                }

                // Traceback to construct a monotonic mapping: new j -> old i
                var newToOld = new int[m];
                int ii = n, jj = m;

                while (jj > 0) {
                    if (ii > 0 && step[ii, jj] == 1) {
                        newToOld[jj - 1] = ii - 1;
                        ii--;
                        jj--;
                    }
                    else if (ii > 0 && step[ii, jj] == 2) {
                        ii--; // skip old (deletion in old)
                    }
                    else {
                        // insertion in new -> map to nearest old (monotonic)
                        newToOld[jj - 1] = Math.Max(ii - 1, 0);
                        jj--;
                    }
                }

                return newToOld;
            }
        }

        // ---------------------------------------------------------------
        // Trim detection and core similarity on top of DTW mapping.
        // ---------------------------------------------------------------
        public static class AlignmentBuilder
        {
            public static Alignment Build(
                IReadOnlyList<Token> oldTokens,
                IReadOnlyList<Token> newTokens,
                int[] newToOld,
                AlignmentMode mode)
            {
                int n = oldTokens.Count;
                int m = newTokens.Count;

                int leadingTrim = 0, trailingTrim = 0;

                if (mode == AlignmentMode.UserEditedTranscript && n > 0 && m > 0) {
                    // Count new-prefix tokens until the first exact text match along mapping.
                    while (leadingTrim < m) {
                        int map = newToOld[leadingTrim];
                        if (map >= 0 && map < n && OrdinalEquals(newTokens[leadingTrim].Text, oldTokens[map].Text))
                            break;

                        leadingTrim++;
                    }

                    // Count new-suffix tokens (from end) until the last exact text match along mapping.
                    while (trailingTrim < m - leadingTrim) {
                        int jIdx = m - 1 - trailingTrim;
                        int map = newToOld[jIdx];
                        if (map >= 0 && map < n && OrdinalEquals(newTokens[jIdx].Text, oldTokens[map].Text))
                            break;

                        trailingTrim++;
                    }
                }
                // RetranscribeSameAudio -> trims presumed zero.

                // Similarity over the *core* region only.
                int coreStart = leadingTrim;
                int coreEnd = m - trailingTrim - 1;
                int coreLen = Math.Max(0, coreEnd - coreStart + 1);

                int coreMatches = 0;
                if (coreLen > 0) {
                    for (int j = coreStart; j <= coreEnd; j++) {
                        int iMap = newToOld[j];
                        if (iMap >= 0 && iMap < n && OrdinalEquals(newTokens[j].Text, oldTokens[iMap].Text))
                            coreMatches++;
                    }
                }

                double similarity = coreLen == 0 ? 1.0 : (double)coreMatches / coreLen;
                return new Alignment(newToOld, leadingTrim, trailingTrim, similarity);
            }
        }

        // ---------------------------------------------------------------
        // Sample old LinearMap at old token char offsets -> per-token times.
        // ---------------------------------------------------------------
        public static class MapSampling
        {
            public static float[] SampleOldTokenTimes(LinearMap oldMap, IReadOnlyList<Token> oldTokens)
            {
                int n = oldTokens.Count;
                var times = new float[n];
                for (int i = 0; i < n; i++) {
                    float x = oldTokens[i].Start; // old token start (char offset)
                    times[i] = oldMap.Map(x);
                }
                return times;
            }
        }
    }
}
