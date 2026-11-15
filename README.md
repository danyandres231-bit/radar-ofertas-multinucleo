# Radar de Ofertas Multinúcleo

III Proyecto Programado · IC-3101 Arquitectura de Computadoras · TEC Campus San Carlos · II Semestre 2026

Sitio público de ofertas de videojuegos alimentado por un pipeline de web scraping **secuencial, concurrente y paralelo** en C# (.NET 8), con historial de precios, panel de métricas y un banco de pruebas reproducible (modo replay).

- Sitio: _(URL de Vercel)_
- Diseño (Hito 1): [`docs/hito1-diseno.md`](docs/hito1-diseno.md) · ADRs: [`docs/adr/`](docs/adr/) · Informe: [`docs/informe.md`](docs/informe.md)

## Estructura

| Carpeta | Qué hay |
|---|---|
| `src/Radar.Core` | Modelos, base de datos SQLite, definición de oferta y **emparejamiento** (Jaro-Winkler + Dice, escrito a mano) |
| `src/Radar.Scraping` | Cliente HTTP robusto (timeout, backoff + jitter, semáforo por dominio, caché, captura/replay) y una clase por fuente |
| `src/Radar.Pipeline` | Programa principal: etapas 0 → 5 con `Channel<T>` acotados y los tres modos |
| `src/Radar.Bench` | Barridos de workers, speedup, Karp–Flatt, contención y condición de carrera |
| `web/` | Sitio estático (HTML + JS) que publica Vercel |
| `data/` | `radar.db` (historial), CSV editables (`juegos_extra.csv`, `juegos_excluidos.csv`, `hltb_ids.csv`), `replay/` y `bench/` |
| `docs/` | Acta, tablero, seguimiento, ADRs, cambios de diseño, bitácora de IA e informe |

## Requisitos

- .NET 8 SDK (Visual Studio 2022 17.8 o superior, o `dotnet` en consola).
- Abrir `RadarOfertas.sln` en Visual Studio. Proyecto de inicio: `Radar.Pipeline`. Los argumentos se ponen en *Propiedades → Depurar → Argumentos de la línea de comandos*.
- **Directorio de trabajo = raíz del repositorio** (donde está `RadarOfertas.sln`). En Visual Studio: *Propiedades → Depurar → Directorio de trabajo*.

## Cómo ejecutar

Todos los comandos se corren desde la raíz del repositorio.

```bash
# Etapa 0: construir o actualizar el catálogo (Metacritic + data/juegos_extra.csv)
dotnet run --project src/Radar.Pipeline -c Release -- descubrir

# Modo vivo (contra los sitios reales), en cada uno de los tres modos
dotnet run --project src/Radar.Pipeline -c Release -- ejecutar --modo seq
dotnet run --project src/Radar.Pipeline -c Release -- ejecutar --modo conc --io 16
dotnet run --project src/Radar.Pipeline -c Release -- ejecutar --modo par  --io 16 --cpu 2

# Capturar respuestas reales para el modo replay (se guardan en data/replay)
dotnet run --project src/Radar.Pipeline -c Release -- ejecutar --modo par --capturar

# Modo replay (mediciones reproducibles): latencia simulada fija o con variación
dotnet run --project src/Radar.Pipeline -c Release -- ejecutar --modo par --io 16 --cpu 2 --replay --latencia 100
dotnet run --project src/Radar.Pipeline -c Release -- ejecutar --modo par --io 16 --cpu 2 --replay --latencia 100 --jitter 40

# Solo regenerar los JSON del sitio
dotnet run --project src/Radar.Pipeline -c Release -- publicar
```

Opciones: `--modo seq|conc|par`, `--io N` (workers de E/S), `--cpu N` (workers de emparejamiento), `--replay`, `--latencia ms`, `--jitter ms`, `--capturar`, `--limite N` (solo los primeros N juegos), `--umbral 0.88`, `--sin-publicar`, `--sin-persistir`, `--sin-cache`. Los valores por defecto están en `config/radar.json`.

En modo replay **no se escribe** en la base de datos de producción si se usa `--sin-persistir` (recomendado para pruebas).

## Banco de pruebas

```bash
dotnet run --project src/Radar.Bench -c Release -- todo                 # todo (~2 h): contención, carrera, descarga, emparejamiento, pipeline
dotnet run --project src/Radar.Bench -c Release -- descarga --latencia 100 --reps 5
dotnet run --project src/Radar.Bench -c Release -- emparejar --reps 5
dotnet run --project src/Radar.Bench -c Release -- pipeline --latencia 400 --reps 3
dotnet run --project src/Radar.Bench -c Release -- contencion
dotnet run --project src/Radar.Bench -c Release -- carrera
dotnet run --project src/Radar.Bench -c Release -- validar             # precisión y exhaustividad del emparejamiento
```

Resultados en `data/bench/resultados.json` (también se copian a `web/data/bench.json` para el panel de métricas).

## Actualización programada

`.github/workflows/actualizar.yml` corre el pipeline cada 12 horas en GitHub Actions, guarda `data/radar.db` y `web/data/*.json` con un commit y Vercel vuelve a publicar el sitio.

Secretos del repositorio (*Settings → Secrets and variables → Actions*):

| Secreto | Para qué |
|---|---|
| `IGDB_CLIENT_ID`, `IGDB_CLIENT_SECRET` | Duraciones desde IGDB (respaldo de How Long To Beat, ADR-006). Se obtienen gratis en dev.twitch.tv |
| `VERCEL_DEPLOY_HOOK` (opcional) | URL de un *Deploy Hook* de Vercel, por si Vercel no publica solo los commits del bot |

## Scraping responsable

User-Agent propio (`RadarOfertasTEC/1.0`), límite de conexiones por dominio, caché de 12 h, reintentos con backoff y respeto de robots.txt (ver ADR-005 y ADR-006). No se evaden CAPTCHAs, no se usan proxies, no se inicia sesión y no se recolectan datos personales.
