# Registro de cambios de diseño

Cada desviación respecto al diseño del Hito 1 (`docs/hito1-diseno.md`), con su motivo.

| Fecha | Cambio | Motivo | ADR |
|---|---|---|---|
| 15/11/2026 | GOG se consulta por su API de catálogo y no por HTML | Su HTML de búsqueda no filtra en el servidor | ADR-011 |
| 15/11/2026 | Steam: se usa el endpoint `search/results` (HTML dentro de JSON) en lugar de la página completa | 7 veces menos bytes con el mismo HTML de resultados | ADR-005 |
| 15/11/2026 | Calificación de usuarios de Steam se toma de la misma búsqueda de Steam | Evita una solicitud extra por juego | ADR-005 |
| 15/11/2026 | Etapa 1b explícita para fichas de Nintendo y GameStop | Los sitemaps no traen precio | ADR-012 |
| 15/11/2026 | Catálogo: 630 juegos (611 de Metacritic + 56 casos difíciles manuales, con repetidos) | Llegar con margen a 500 juegos publicables | — |
