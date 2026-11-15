using System.Text.Json;
using System.Text.RegularExpressions;
using Radar.Core;
using Radar.Scraping.Http;

namespace Radar.Scraping.Fuentes;

/// <summary>How Long To Beat: solo fichas /game/&lt;id&gt; con ID conocido (data/hltb_ids.csv). El buscador /api está prohibido por robots.txt (ADR-006).</summary>
public static partial class Hltb
{
    [GeneratedRegex(@"<h4>(Main Story|Main \+ Sides|Main \+ Extras|Completionist)</h4><h5>([^<]+)</h5>")] private static partial Regex RxTiempo();

    public static double? Horas(string s)
    {
        s = s.Replace("½", ".5").Replace("Hours", "").Replace("Mins", "").Trim();
        return double.TryParse(s, System.Globalization.NumberStyles.Float, Util.Inv, out var v) ? v : null;
    }

    public static Duracion? Parsear(string html)
    {
        double? a = null, b = null, c = null;
        foreach (Match m in RxTiempo().Matches(html))
        {
            var h = Horas(m.Groups[2].Value);
            switch (m.Groups[1].Value) { case "Main Story": a ??= h; break; case "Completionist": c ??= h; break; default: b ??= h; break; }
        }
        return a is null && b is null && c is null ? null : new Duracion("howlongtobeat", a, b, c);
    }

    public static async Task<Duracion?> FichaAsync(Descargador d, string id, CancellationToken ct) =>
        Parsear(await d.GetAsync($"https://howlongtobeat.com/game/{id}", ct));
}

/// <summary>IGDB (respaldo documentado de HLTB). Credenciales por variables de entorno IGDB_CLIENT_ID / IGDB_CLIENT_SECRET.</summary>
public sealed class Igdb
{
    readonly Descargador _d;
    readonly string? _id, _secret;
    string? _token;
    readonly SemaphoreSlim _tokenLock = new(1, 1);

    public Igdb(Descargador d)
    {
        _d = d;
        _id = Environment.GetEnvironmentVariable("IGDB_CLIENT_ID");
        _secret = Environment.GetEnvironmentVariable("IGDB_CLIENT_SECRET");
    }

    /// <summary>En replay no hacen falta credenciales: las respuestas ya están capturadas.</summary>
    public bool Disponible(bool replay) => replay || (!string.IsNullOrEmpty(_id) && !string.IsNullOrEmpty(_secret));

    async Task<Dictionary<string, string>> Headers(CancellationToken ct)
    {
        if (_token is null)
        {
            await _tokenLock.WaitAsync(ct);
            try
            {
                if (_token is null)
                {
                    var json = await _d.PostAsync($"https://id.twitch.tv/oauth2/token?client_id={_id}&client_secret={_secret}&grant_type=client_credentials", "", null, ct, privado: true);
                    _token = JsonDocument.Parse(json).RootElement.GetProperty("access_token").GetString();
                }
            }
            finally { _tokenLock.Release(); }
        }
        return new() { ["Client-ID"] = _id ?? "replay", ["Authorization"] = "Bearer " + (_token ?? "replay") };
    }

    public async Task<Duracion?> DuracionAsync(string titulo, CancellationToken ct)
    {
        // En replay la clave de la respuesta no depende del token (va en headers, no en URL/cuerpo).
        var h = string.IsNullOrEmpty(_id) ? new Dictionary<string, string>() : await Headers(ct);
        var q = titulo.Replace("\"", "");
        var juegos = await _d.PostAsync("https://api.igdb.com/v4/games", $"search \"{q}\"; fields id,name; where version_parent = null; limit 1;", h, ct);
        var arr = JsonDocument.Parse(juegos).RootElement;
        if (arr.GetArrayLength() == 0) return null;
        var id = arr[0].GetProperty("id").GetInt64();
        var ttb = await _d.PostAsync("https://api.igdb.com/v4/game_time_to_beats", $"fields hastily,normally,completely; where game_id = {id};", h, ct);
        var t = JsonDocument.Parse(ttb).RootElement;
        if (t.GetArrayLength() == 0) return null;
        double? H(string p) => t[0].TryGetProperty(p, out var v) ? Math.Round(v.GetDouble() / 3600.0, 1) : null;
        return new Duracion("igdb", H("hastily"), H("normally"), H("completely"));
    }
}
