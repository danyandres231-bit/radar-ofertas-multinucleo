namespace Radar.Core;

/// <summary>Juego del repositorio (catálogo).</summary>
public sealed class Juego
{
    public long Id { get; set; }
    public string Slug { get; set; } = "";
    public string Titulo { get; set; } = "";
    public List<string> TitulosAlt { get; set; } = new();
    public List<string> Plataformas { get; set; } = new();
    public string? FechaLanz { get; set; }
    public double? Msrp { get; set; }
    public string? ImagenUrl { get; set; }
    public string Tipo { get; set; } = "juego";
    public bool CasoDificil { get; set; }
    public string Origen { get; set; } = "metacritic:browse";
    public int? MetascoreCatalogo { get; set; }
}

/// <summary>Un resultado que devuelve una tienda (o una entrada de sitemap) y que hay que emparejar.</summary>
public sealed record Candidato(
    string Tienda,          // steam | gog | nintendo | gamestop
    string Titulo,
    string Url,
    string? Plataforma,
    double? Precio = null,
    double? PrecioRegular = null,
    string Formato = "digital",
    string? Imagen = null,
    double? ReseñasPct = null,
    string TipoProducto = "juego");

/// <summary>Candidato ya normalizado (se calcula una vez, en la etapa 0).</summary>
public sealed class CandidatoNorm
{
    public required Candidato C { get; init; }
    public required string Norm { get; init; }
    public required string[] Tokens { get; init; }
    public required int Secuela { get; init; }
    public required bool EsExtra { get; init; }
    public int? Anio { get; init; }   // año entre paréntesis en el título de la tienda, p. ej. "Resident Evil 2 (1998)"
}

public sealed record Emparejamiento(Candidato Candidato, double Similitud, bool Dudoso, string? Motivo);

/// <summary>Todo lo que se descargó y parseó para un juego (salida de las etapas 1 y 2).</summary>
public sealed class FichaJuego
{
    public required Juego Juego { get; init; }
    public List<Candidato> CandidatosBusqueda { get; } = new();   // Steam y GOG
    public int? Metascore { get; set; }
    public List<string> PlataformasMc { get; set; } = new();
    public string? FechaMc { get; set; }
    public string? ImagenMc { get; set; }
    public Duracion? Duracion { get; set; }
    public List<string> FuentesFallidas { get; } = new();
    // Salida de la etapa 3
    public List<Emparejamiento> Emparejados { get; } = new();
    public List<Emparejamiento> Dudosos { get; } = new();
    // Salida de la etapa 1b
    public List<Observacion> Observaciones { get; } = new();
}

public sealed record Duracion(string Fuente, double? PrincipalH, double? ExtrasH, double? CompletistaH);

public sealed record Observacion(
    string Tienda, double PrecioActual, double? PrecioRegular, bool EnOferta, string CriterioOferta,
    string Formato, string? Plataforma, string Url, string TituloTienda, double Similitud, bool Dudoso,
    DateTime ObtenidoEn);
