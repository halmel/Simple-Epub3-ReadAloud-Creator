using System;
using System.Collections.Generic;

/// <summary>
/// Provides a collection of fuzzy scoring algorithms for comparing spans of values.
/// All methods return an integer percentage from 0 to 100.
/// </summary>
public static class FuzzyScoringAlgorithms
{
    // ---------- Jaccard Similarity ----------
    /// <summary>
    /// Computes the Jaccard similarity coefficient: |intersection| / |union|.
    /// </summary>
    /// <remarks>
    /// <para>Time complexity: O(n + m). Allocates a hash set of the smaller span.</para>
    /// <para><b>Benefits:</b> Classic set similarity, easy to interpret. Ignores duplicates and order.</para>
    /// <para><b>Disadvantages:</b> Does not consider element order; may be unsuitable when order matters.</para>
    /// </remarks>
    public static int ScoreJaccard(ReadOnlySpan<ushort> a, ReadOnlySpan<ushort> b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;

        // Determine which span is shorter without using tuples
        ReadOnlySpan<ushort> small, large;
        if (a.Length <= b.Length) { small = a; large = b; }
        else { small = b; large = a; }

        var setSmall = new HashSet<ushort>(small.Length);
        foreach (var item in small) setSmall.Add(item);

        int intersection = 0;
        foreach (var item in large)
            if (setSmall.Remove(item)) intersection++;

        int union = small.Length + large.Length - intersection;
        return (intersection * 100) / union;
    }

    /// <inheritdoc cref="ScoreJaccard(ReadOnlySpan{ushort}, ReadOnlySpan{ushort})"/>
    public static int ScoreJaccard(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;

        ReadOnlySpan<byte> small, large;
        if (a.Length <= b.Length) { small = a; large = b; }
        else { small = b; large = a; }

        var setSmall = new HashSet<byte>(small.Length);
        foreach (var item in small) setSmall.Add(item);

        int intersection = 0;
        foreach (var item in large)
            if (setSmall.Remove(item)) intersection++;

        int union = small.Length + large.Length - intersection;
        return (intersection * 100) / union;
    }

    // ---------- Dice Coefficient ----------
    /// <summary>
    /// Computes the Sørensen–Dice coefficient: (2 * |intersection|) / (|a| + |b|).
    /// </summary>
    /// <remarks>
    /// <para>Time complexity: O(n + m). Allocates a hash set of the smaller span.</para>
    /// <para><b>Benefits:</b> Emphasises common elements more than Jaccard. Often used in information retrieval.</para>
    /// <para><b>Disadvantages:</b> Still order‑insensitive and ignores duplicates beyond the first occurrence.</para>
    /// </remarks>
    public static int ScoreDice(ReadOnlySpan<ushort> a, ReadOnlySpan<ushort> b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;

        ReadOnlySpan<ushort> small, large;
        if (a.Length <= b.Length) { small = a; large = b; }
        else { small = b; large = a; }

        var setSmall = new HashSet<ushort>(small.Length);
        foreach (var item in small) setSmall.Add(item);

        int intersection = 0;
        foreach (var item in large)
            if (setSmall.Remove(item)) intersection++;

        return (2 * intersection * 100) / (a.Length + b.Length);
    }

    /// <inheritdoc cref="ScoreDice(ReadOnlySpan{ushort}, ReadOnlySpan{ushort})"/>
    public static int ScoreDice(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;

        ReadOnlySpan<byte> small, large;
        if (a.Length <= b.Length) { small = a; large = b; }
        else { small = b; large = a; }

        var setSmall = new HashSet<byte>(small.Length);
        foreach (var item in small) setSmall.Add(item);

        int intersection = 0;
        foreach (var item in large)
            if (setSmall.Remove(item)) intersection++;

        return (2 * intersection * 100) / (a.Length + b.Length);
    }

    // ---------- Containment in First Span ----------
    /// <summary>
    /// Measures what fraction of the first span is contained in the second.
    /// </summary>
    /// <remarks>
    /// <para>Time complexity: O(n + m). Allocates a hash set of the second span.</para>
    /// <para><b>Benefits:</b> Useful when you expect one set to be a subset of the other.</para>
    /// <para><b>Disadvantages:</b> Asymmetric; consider which span should be the reference.</para>
    /// </remarks>
    public static int ScoreContainmentFirst(ReadOnlySpan<ushort> a, ReadOnlySpan<ushort> b)
    {
        if (a.Length == 0) return 0;
        if (b.Length == 0) return 0;

        var setB = new HashSet<ushort>(b.Length);
        foreach (var item in b) setB.Add(item);

        int matchCount = 0;
        foreach (var item in a)
            if (setB.Remove(item)) matchCount++;

        return (matchCount * 100) / a.Length;
    }

    /// <inheritdoc cref="ScoreContainmentFirst(ReadOnlySpan{ushort}, ReadOnlySpan{ushort})"/>
    public static int ScoreContainmentFirst(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length == 0) return 0;
        if (b.Length == 0) return 0;

        var setB = new HashSet<byte>(b.Length);
        foreach (var item in b) setB.Add(item);

        int matchCount = 0;
        foreach (var item in a)
            if (setB.Remove(item)) matchCount++;

        return (matchCount * 100) / a.Length;
    }

    // ---------- Longest Common Subsequence (LCS) ----------
    /// <summary>
    /// Computes the length of the longest common subsequence (LCS) normalised by the smaller span length.
    /// </summary>
    /// <remarks>
    /// <para>Time complexity: O(n * m). Uses O(min(n,m)) memory via rolling arrays.</para>
    /// <para><b>Benefits:</b> Order‑sensitive, allows gaps. Good for sequences where order matters.</para>
    /// <para><b>Disadvantages:</b> Quadratic time; may be slow for long spans. Does not require contiguity.</para>
    /// </remarks>
    public static int ScoreLCS(ReadOnlySpan<ushort> a, ReadOnlySpan<ushort> b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;

        int n = a.Length, m = b.Length;
        int[,] dp = new int[2, m + 1];

        for (int i = 1; i <= n; i++)
        {
            int cur = i & 1;
            int prev = (i - 1) & 1;
            for (int j = 1; j <= m; j++)
            {
                if (a[i - 1] == b[j - 1])
                    dp[cur, j] = dp[prev, j - 1] + 1;
                else
                    dp[cur, j] = Math.Max(dp[prev, j], dp[cur, j - 1]);
            }
        }

        int lcsLength = dp[n & 1, m];
        int denominator = Math.Min(n, m);
        return (lcsLength * 100) / denominator;
    }

    /// <inheritdoc cref="ScoreLCS(ReadOnlySpan{ushort}, ReadOnlySpan{ushort})"/>
    public static int ScoreLCS(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;

        int n = a.Length, m = b.Length;
        int[,] dp = new int[2, m + 1];

        for (int i = 1; i <= n; i++)
        {
            int cur = i & 1;
            int prev = (i - 1) & 1;
            for (int j = 1; j <= m; j++)
            {
                if (a[i - 1] == b[j - 1])
                    dp[cur, j] = dp[prev, j - 1] + 1;
                else
                    dp[cur, j] = Math.Max(dp[prev, j], dp[cur, j - 1]);
            }
        }

        int lcsLength = dp[n & 1, m];
        int denominator = Math.Min(n, m);
        return (lcsLength * 100) / denominator;
    }

    // ---------- Longest Common Substring (contiguous) ----------
    /// <summary>
    /// Finds the length of the longest contiguous substring common to both spans,
    /// normalised by the smaller span length.
    /// </summary>
    /// <remarks>
    /// <para>Time complexity: O(n * m). Uses O(min(n,m)) memory via rolling arrays.</para>
    /// <para><b>Benefits:</b> Order‑sensitive, requires contiguity. Good for exact block matching.</para>
    /// <para><b>Disadvantages:</b> Quadratic time; may be overly strict if only approximate order matters.</para>
    /// </remarks>
    public static int ScoreLongestCommonSubstring(ReadOnlySpan<ushort> a, ReadOnlySpan<ushort> b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;

        int n = a.Length, m = b.Length;
        int maxLen = 0;
        int[,] dp = new int[2, m + 1];

        for (int i = 1; i <= n; i++)
        {
            int cur = i & 1;
            int prev = (i - 1) & 1;
            for (int j = 1; j <= m; j++)
            {
                if (a[i - 1] == b[j - 1])
                {
                    dp[cur, j] = dp[prev, j - 1] + 1;
                    if (dp[cur, j] > maxLen) maxLen = dp[cur, j];
                }
                else
                {
                    dp[cur, j] = 0;
                }
            }
        }

        int denominator = Math.Min(n, m);
        return (maxLen * 100) / denominator;
    }

    /// <inheritdoc cref="ScoreLongestCommonSubstring(ReadOnlySpan{ushort}, ReadOnlySpan{ushort})"/>
    public static int ScoreLongestCommonSubstring(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;

        int n = a.Length, m = b.Length;
        int maxLen = 0;
        int[,] dp = new int[2, m + 1];

        for (int i = 1; i <= n; i++)
        {
            int cur = i & 1;
            int prev = (i - 1) & 1;
            for (int j = 1; j <= m; j++)
            {
                if (a[i - 1] == b[j - 1])
                {
                    dp[cur, j] = dp[prev, j - 1] + 1;
                    if (dp[cur, j] > maxLen) maxLen = dp[cur, j];
                }
                else
                {
                    dp[cur, j] = 0;
                }
            }
        }

        int denominator = Math.Min(n, m);
        return (maxLen * 100) / denominator;
    }

    // ---------- Normalised Edit Distance (Levenshtein) ----------
    /// <summary>
    /// Converts the Levenshtein edit distance into a similarity percentage.
    /// Similarity = (maxLen - distance) / maxLen.
    /// </summary>
    /// <remarks>
    /// <para>Time complexity: O(n * m). Uses O(min(n,m)) memory via two arrays.</para>
    /// <para><b>Benefits:</b> Accounts for insertions, deletions, and substitutions. Order‑sensitive.</para>
    /// <para><b>Disadvantages:</b> Quadratic time; may be too granular for long spans.</para>
    /// </remarks>
    public static int ScoreEditDistance(ReadOnlySpan<ushort> a, ReadOnlySpan<ushort> b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;

        int n = a.Length, m = b.Length;
        int[] prev = new int[m + 1];
        int[] cur = new int[m + 1];

        for (int j = 0; j <= m; j++) prev[j] = j;

        for (int i = 1; i <= n; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= m; j++)
            {
                int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                cur[j] = Math.Min(
                    Math.Min(prev[j] + 1, cur[j - 1] + 1),
                    prev[j - 1] + cost);
            }
            // swap references
            int[] tmp = prev;
            prev = cur;
            cur = tmp;
        }

        int distance = prev[m];
        int maxLen = Math.Max(n, m);
        return ((maxLen - distance) * 100) / maxLen;
    }

    /// <inheritdoc cref="ScoreEditDistance(ReadOnlySpan{ushort}, ReadOnlySpan{ushort})"/>
    public static int ScoreEditDistance(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;

        int n = a.Length, m = b.Length;
        int[] prev = new int[m + 1];
        int[] cur = new int[m + 1];

        for (int j = 0; j <= m; j++) prev[j] = j;

        for (int i = 1; i <= n; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= m; j++)
            {
                int cost = (a[i - 1] == b[j - 1]) ? 0 : 1;
                cur[j] = Math.Min(
                    Math.Min(prev[j] + 1, cur[j - 1] + 1),
                    prev[j - 1] + cost);
            }
            int[] tmp = prev;
            prev = cur;
            cur = tmp;
        }

        int distance = prev[m];
        int maxLen = Math.Max(n, m);
        return ((maxLen - distance) * 100) / maxLen;
    }

    // ---------- Ratcliff/Obershelp (difflib style) ----------
    /// <summary>
    /// Implements the Ratcliff/Obershelp pattern‑matching algorithm (as used in Python's difflib).
    /// Recursively finds the longest common contiguous substring and sums matches on both sides.
    /// </summary>
    /// <remarks>
    /// <para>Time complexity: O(n^3) in worst case, but often faster in practice.</para>
    /// <para><b>Benefits:</b> Intuitive for humans; gives high scores to sequences with long common blocks.</para>
    /// <para><b>Disadvantages:</b> Can be slow for long spans; recursive depth may be large.</para>
    /// </remarks>
    public static int ScoreRatcliff(ReadOnlySpan<ushort> a, ReadOnlySpan<ushort> b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;

        int matchLen = FindLongestCommonSubstring(a, b, out int aStart, out int bStart);
        if (matchLen == 0) return 0;

        int leftSimilarity = ScoreRatcliff(
            a.Slice(0, aStart),
            b.Slice(0, bStart));
        int rightSimilarity = ScoreRatcliff(
            a.Slice(aStart + matchLen),
            b.Slice(bStart + matchLen));

        int totalMatch = matchLen + leftSimilarity + rightSimilarity;
        int denominator = (a.Length + b.Length) / 2;  // average length
        return (totalMatch * 100) / denominator;
    }

    /// <inheritdoc cref="ScoreRatcliff(ReadOnlySpan{ushort}, ReadOnlySpan{ushort})"/>
    public static int ScoreRatcliff(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        if (a.Length == 0 || b.Length == 0) return 0;

        int matchLen = FindLongestCommonSubstring(a, b, out int aStart, out int bStart);
        if (matchLen == 0) return 0;

        int leftSimilarity = ScoreRatcliff(
            a.Slice(0, aStart),
            b.Slice(0, bStart));
        int rightSimilarity = ScoreRatcliff(
            a.Slice(aStart + matchLen),
            b.Slice(bStart + matchLen));

        int totalMatch = matchLen + leftSimilarity + rightSimilarity;
        int denominator = (a.Length + b.Length) / 2;
        return (totalMatch * 100) / denominator;
    }

    // Helper for Ratcliff: finds the longest common contiguous substring and its positions.
    private static int FindLongestCommonSubstring(
        ReadOnlySpan<ushort> a,
        ReadOnlySpan<ushort> b,
        out int bestA,
        out int bestB)
    {
        int bestLen = 0;
        bestA = 0; bestB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            for (int j = 0; j < b.Length; j++)
            {
                int k = 0;
                while (i + k < a.Length && j + k < b.Length && a[i + k] == b[j + k])
                    k++;
                if (k > bestLen)
                {
                    bestLen = k;
                    bestA = i;
                    bestB = j;
                }
            }
        }
        return bestLen;
    }

    private static int FindLongestCommonSubstring(
        ReadOnlySpan<byte> a,
        ReadOnlySpan<byte> b,
        out int bestA,
        out int bestB)
    {
        int bestLen = 0;
        bestA = 0; bestB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            for (int j = 0; j < b.Length; j++)
            {
                int k = 0;
                while (i + k < a.Length && j + k < b.Length && a[i + k] == b[j + k])
                    k++;
                if (k > bestLen)
                {
                    bestLen = k;
                    bestA = i;
                    bestB = j;
                }
            }
        }
        return bestLen;
    }
}