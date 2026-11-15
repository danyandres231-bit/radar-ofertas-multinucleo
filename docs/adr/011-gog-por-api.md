# ADR-011 · GOG por API de catálogo en lugar de HTML · 15/11/2026

**Contexto.** Al implementar el scraper se comprobó que la página `gog.com/en/games?query=…` **no filtra en el servidor**: devuelve siempre el mismo catálogo general y el filtrado lo hace JavaScript en el navegador. Con el HTML no se puede buscar un juego.

**Alternativas.**
- Usar un navegador real (Playwright) para que corra el JavaScript: lento, pesado y más carga para GOG.
- **API pública de catálogo** `catalog.gog.com/v1/catalog?query=…` (la misma que usa su página).

**Decisión.** GOG se consulta por su API de catálogo. Sigue habiendo 4 fuentes por HTML (Steam, Nintendo, GameStop y Metacritic), de ellas 3 tiendas, así que se cumple el mínimo del enunciado (3 fuentes por HTML, 2 tiendas).

**Consecuencias.** Menos datos por solicitud (JSON) y menos tiempo de parseo. Reemplaza la fila de GOG del ADR-005.

**Estado.** Aceptada. Reemplaza parcialmente al ADR-005.
