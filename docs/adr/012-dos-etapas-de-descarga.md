# ADR-012 · Etapa 1b: descarga de fichas después del emparejamiento · 15/11/2026

**Contexto.** Nintendo eShop y GameStop no tienen un buscador utilizable (JavaScript o prohibido por robots.txt). Sus productos se conocen por el sitemap, que trae título y URL pero no precio.

**Alternativas.**
- Descargar todas las fichas del sitemap (~47 000): miles de solicitudes innecesarias, contra el scraping responsable.
- **Emparejar primero contra el sitemap y luego descargar solo las fichas emparejadas** (máximo 3 por tienda y juego).

**Decisión.** El pipeline queda 0 → 1 → 2 → 3 → **1b** → 4 → 5. La etapa 1b es de E/S y usa los mismos workers de E/S.

**Consecuencias.** Unas 2–3 solicitudes extra por juego en lugar de miles. El emparejamiento compara cada juego contra los ~47 000 candidatos de los sitemaps más los de Steam y GOG: una carga de CPU real y medible.

**Estado.** Aceptada.
