using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Radar.Core.Matching;

/// <summary>Normaliza títulos: minúsculas, sin tildes ni puntuación, romanos → arábigos, sin sufijos de plataforma/edición.</summary>
public static partial class Normalizador
{
    static readonly string[] SufijosPlataforma =
    {
        "nintendo switch 2", "nintendo switch", "switch 2", "switch", "playstation 5", "playstation 4", "ps5", "ps4",
        "xbox series x s", "xbox series x", "xbox series", "xbox one", "xbox", "pc", "windows", "mac", "steam", "digital", "us"
    };
    static readonly string[] SufijosEdicion =
    {
        "game of the year edition", "goty edition", "goty", "deluxe edition", "definitive edition", "complete edition",
        "ultimate edition", "gold edition", "standard edition", "digital deluxe edition", "special edition", "enhanced edition",
        "collectors edition", "launch edition", "edition", "remastered", "remaster", "directors cut", "the final cut", "final cut"
    };
    static readonly HashSet<string> PalabrasExtra = new() { "toolkit", "editor", "artbook", "dlc", "pack", "bundle", "season", "pass", "soundtrack", "expansion", "upgrade", "costume", "skin", "currency", "coins", "points", "demo", "trial" };
    static readonly HashSet<string> Vacias = new() { "the", "a", "an", "of", "and", "y", "de", "la", "el" };
    static readonly Dictionary<string, string> Romanos = new()
    {
        ["ii"] = "2", ["iii"] = "3", ["iv"] = "4", ["v"] = "5", ["vi"] = "6", ["vii"] = "7", ["viii"] = "8",
        ["ix"] = "9", ["x"] = "10", ["xi"] = "11", ["xii"] = "12", ["xiii"] = "13", ["xiv"] = "14", ["xv"] = "15", ["xvi"] = "16"
    };

    [GeneratedRegex(@"[^a-z0-9 ]+")] private static partial Regex NoAlfanum();
    [GeneratedRegex(@"\s+")] private static partial Regex Espacios();
    [GeneratedRegex(@"\(\d{4}\)")] private static partial Regex AnioParentesis();

    public static string QuitarTildes(string s)
    {
        var norm = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(norm.Length);
        foreach (var ch in norm)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Forma normalizada completa (con sufijos de edición quitados).</summary>
    public static string Normalizar(string titulo, bool quitarEdicion = true)
    {
        var s = QuitarTildes(titulo.ToLowerInvariant());
        s = s.Replace("&", " and ").Replace("'", "").Replace("’", "").Replace("®", "").Replace("™", "");
        s = AnioParentesis().Replace(s, " ");
        s = NoAlfanum().Replace(s, " ");
        s = Espacios().Replace(s, " ").Trim();
        var palabras = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(p => Romanos.TryGetValue(p, out var n) && p != "x" ? n : p).ToList();
        s = string.Join(' ', palabras);
        bool cambio = true;
        while (cambio)
        {
            cambio = false;
            foreach (var suf in SufijosPlataforma.Concat(quitarEdicion ? SufijosEdicion : Array.Empty<string>()))
            {
                if (s.EndsWith(" " + suf) && s.Length > suf.Length + 2) { s = s[..^(suf.Length + 1)].TrimEnd(); cambio = true; }
            }
        }
        return s;
    }

    public static string[] Tokens(string norm) =>
        norm.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => !Vacias.Contains(t)).Distinct().ToArray();

    /// <summary>Número de secuela (último número "suelto" del título, 0 si no hay). Ignora años.</summary>
    public static int Secuela(string norm)
    {
        var toks = norm.Split(' ');
        for (int i = toks.Length - 1; i >= 0; i--)
            if (int.TryParse(toks[i], out var n) && n > 0 && n < 100) return n;
        return 0;
    }

    public static bool EsExtra(string norm) => norm.Split(' ').Any(PalabrasExtra.Contains);

    [GeneratedRegex(@"\((19|20)(\d{2})\)")] private static partial Regex AnioEnTitulo();

    public static int? Anio(string titulo) { var m = AnioEnTitulo().Match(titulo); return m.Success ? int.Parse(m.Groups[1].Value + m.Groups[2].Value) : null; }

    public static CandidatoNorm Preparar(Candidato c)
    {
        var n = Normalizar(c.Titulo);
        return new CandidatoNorm { C = c, Norm = n, Tokens = Tokens(n), Secuela = Secuela(n), EsExtra = EsExtra(n) || c.TipoProducto != "juego", Anio = Anio(c.Titulo) };
    }
}
