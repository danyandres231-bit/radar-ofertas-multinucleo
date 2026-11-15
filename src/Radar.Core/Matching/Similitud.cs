namespace Radar.Core.Matching;

/// <summary>Medidas de similitud implementadas a mano (ADR-008).</summary>
public static class Similitud
{
    public static double JaroWinkler(string s1, string s2, double p = 0.1)
    {
        if (s1.Length == 0 && s2.Length == 0) return 1;
        if (s1.Length == 0 || s2.Length == 0) return 0;
        int ventana = Math.Max(0, Math.Max(s1.Length, s2.Length) / 2 - 1);
        Span<bool> m1 = s1.Length <= 256 ? stackalloc bool[s1.Length] : new bool[s1.Length];
        Span<bool> m2 = s2.Length <= 256 ? stackalloc bool[s2.Length] : new bool[s2.Length];
        int coinc = 0;
        for (int i = 0; i < s1.Length; i++)
        {
            int ini = Math.Max(0, i - ventana), fin = Math.Min(i + ventana + 1, s2.Length);
            for (int j = ini; j < fin; j++)
            {
                if (m2[j] || s1[i] != s2[j]) continue;
                m1[i] = m2[j] = true; coinc++; break;
            }
        }
        if (coinc == 0) return 0;
        int trans = 0, k = 0;
        for (int i = 0; i < s1.Length; i++)
        {
            if (!m1[i]) continue;
            while (!m2[k]) k++;
            if (s1[i] != s2[k]) trans++;
            k++;
        }
        double m = coinc;
        double jaro = (m / s1.Length + m / s2.Length + (m - trans / 2.0) / m) / 3.0;
        int prefijo = 0;
        for (int i = 0; i < Math.Min(4, Math.Min(s1.Length, s2.Length)) && s1[i] == s2[i]; i++) prefijo++;
        return jaro + prefijo * p * (1 - jaro);
    }

    /// <summary>Coeficiente de Sørensen-Dice sobre conjuntos de tokens.</summary>
    public static double Dice(string[] a, string[] b)
    {
        if (a.Length == 0 && b.Length == 0) return 1;
        if (a.Length == 0 || b.Length == 0) return 0;
        int comunes = 0;
        foreach (var x in a)
            foreach (var y in b)
                if (x == y) { comunes++; break; }
        return 2.0 * comunes / (a.Length + b.Length);
    }

    public static int Levenshtein(string a, string b)
    {
        var prev = new int[b.Length + 1]; var cur = new int[b.Length + 1];
        for (int j = 0; j <= b.Length; j++) prev[j] = j;
        for (int i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (int j = 1; j <= b.Length; j++)
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }

    public static double Combinada(string normA, string[] tokA, string normB, string[] tokB) =>
        0.6 * JaroWinkler(normA, normB) + 0.4 * Dice(tokA, tokB);
}
