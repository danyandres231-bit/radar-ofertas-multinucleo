namespace Radar.Core.Matching;

/// <summary>Etapa 3: decide qué candidato de cada tienda corresponde a cada juego.</summary>
public sealed class Emparejador
{
    public double Umbral { get; }
    public double MargenDudoso { get; }

    public Emparejador(double umbral = 0.88, double margenDudoso = 0.04)
    { Umbral = umbral; MargenDudoso = margenDudoso; }

    sealed record Clave(string[] Tokens, string Norm, int Secuela);

    /// <summary>Puntaje de un candidato contra un juego (máximo sobre título canónico y alternativos), con reglas duras.</summary>
    public (double score, string? motivo) Puntuar(IReadOnlyList<(string norm, string[] tok, int sec)> claves, bool juegoEsExtra, CandidatoNorm c, int? anioJuego = null)
    {
        double mejor = 0; int secMejor = 0;
        foreach (var (norm, tok, sec) in claves)
        {
            double s = Similitud.Combinada(norm, tok, c.Norm, c.Tokens);
            if (s > mejor) { mejor = s; secMejor = sec; }
        }
        string? motivo = null;
        if (secMejor != c.Secuela) { mejor = Math.Min(mejor, Umbral - 0.05); motivo = "numero_secuela_distinto"; }
        // Remake vs. original: la tienda pone el año, p. ej. "Resident Evil 4 (2005)" frente al remake de 2023.
        else if (c.Anio is int a && anioJuego is int b && Math.Abs(a - b) > 1) { mejor = Math.Min(mejor, Umbral - 0.05); motivo = "anio_distinto"; }
        else if (c.EsExtra && !juegoEsExtra) motivo = "dlc_o_bundle";
        return (mejor, motivo);
    }

    public static int? AnioDe(string? fecha) => fecha is { Length: >= 4 } && int.TryParse(fecha[..4], out var a) ? a : null;

    public static IReadOnlyList<(string, string[], int)> ClavesDe(Juego j)
    {
        var l = new List<(string, string[], int)>();
        foreach (var t in j.TitulosAlt.Prepend(j.Titulo).Distinct())
        {
            var n = Normalizador.Normalizar(t);
            l.Add((n, Normalizador.Tokens(n), Normalizador.Secuela(n)));
        }
        return l;
    }

    /// <summary>Empareja un juego contra TODOS los candidatos (globales de sitemaps + los de su búsqueda).</summary>
    public void Emparejar(FichaJuego f, IReadOnlyList<CandidatoNorm> globales, IReadOnlyList<CandidatoNorm> propios)
    {
        var claves = ClavesDe(f.Juego);
        int? anio = AnioDe(f.Juego.FechaLanz ?? f.FechaMc);
        bool juegoExtra = f.Juego.Tipo != "juego";
        var plataformas = new HashSet<string>(f.PlataformasMc.Concat(f.Juego.Plataformas).Select(Plataformas.Canonica));

        // mejor por (tienda, plataforma)
        var mejores = new Dictionary<(string, string), (CandidatoNorm c, double s, string? motivo)>();
        void Evaluar(CandidatoNorm c)
        {
            var (s, motivo) = Puntuar(claves, juegoExtra, c, anio);
            if (s < Umbral - MargenDudoso) return;
            var plat = Plataformas.Canonica(c.C.Plataforma ?? "pc");
            if (c.C.Tienda is "nintendo" or "gamestop" && plataformas.Count > 0 && !plataformas.Contains(plat)) return;
            var key = (c.C.Tienda, c.C.Tienda is "steam" or "gog" ? "pc" : plat + "|" + c.C.Formato);
            if (!mejores.TryGetValue(key, out var prev) || s > prev.s || (s == prev.s && string.CompareOrdinal(c.C.Url, prev.c.C.Url) < 0))
                mejores[key] = (c, s, motivo);
        }
        for (int i = 0; i < globales.Count; i++) Evaluar(globales[i]);
        for (int i = 0; i < propios.Count; i++) Evaluar(propios[i]);

        foreach (var kv in mejores.OrderBy(k => k.Key.Item1, StringComparer.Ordinal).ThenBy(k => k.Key.Item2, StringComparer.Ordinal))
        {
            var (c, s, motivo) = kv.Value;
            bool cercaUmbral = Math.Abs(s - Umbral) < MargenDudoso;
            bool dudoso = cercaUmbral || motivo != null;
            var e = new Emparejamiento(c.C, Math.Round(s, 4), dudoso, motivo ?? (cercaUmbral ? "cerca_del_umbral" : null));
            if (s >= Umbral && motivo == null) f.Emparejados.Add(e);
            else f.Dudosos.Add(e);
        }
    }
}

public static class Plataformas
{
    public static string Canonica(string p)
    {
        var s = p.ToLowerInvariant();
        if (s.Contains("switch 2") || s.Contains("switch-2")) return "switch2";
        if (s.Contains("switch")) return "switch";
        if (s.Contains("playstation 5") || s.Contains("playstation-5") || s == "ps5") return "ps5";
        if (s.Contains("playstation 4") || s.Contains("playstation-4") || s == "ps4") return "ps4";
        if (s.Contains("series")) return "xbox-series";
        if (s.Contains("xbox one") || s.Contains("xbox-one")) return "xbox-one";
        if (s.Contains("pc") || s.Contains("windows") || s.Contains("steam")) return "pc";
        return s;
    }

    public static string Nombre(string canon) => canon switch
    {
        "switch" => "Nintendo Switch", "switch2" => "Nintendo Switch 2", "ps5" => "PlayStation 5", "ps4" => "PlayStation 4",
        "xbox-series" => "Xbox Series X|S", "xbox-one" => "Xbox One", "pc" => "PC", _ => canon
    };
}
