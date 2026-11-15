# Registro de cambios de diseño

Cada desviación respecto al diseño del Hito 1 (`docs/hito1-diseno.md`), con su motivo.

| Fecha | Cambio | Motivo | ADR |
|---|---|---|---|
| 15/11/2026 | GOG se consulta por su API de catálogo y no por HTML | Su HTML de búsqueda no filtra en el servidor | ADR-011 |
| 15/11/2026 | Steam: se usa el endpoint `search/results` (HTML dentro de JSON) en lugar de la página completa | 7 veces menos bytes con el mismo HTML de resultados | ADR-005 |
| 15/11/2026 | Calificación de usuarios de Steam se toma de la misma búsqueda de Steam | Evita una solicitud extra por juego | ADR-005 |
| 15/11/2026 | Etapa 1b explícita para fichas de Nintendo y GameStop | Los sitemaps no traen precio | ADR-012 |
| 15/11/2026 | Catálogo: 630 juegos (611 de Metacritic + 56 casos difíciles manuales, con repetidos) | Llegar con margen a 500 juegos publicables | — |
| 15/11/2026 | Regla "año distinto" en el emparejamiento: si la tienda pone el año entre paréntesis y difiere del juego, no se empareja | La validación encontró "Resident Evil 4 (2005)" emparejado con el remake de 2023 | ADR-008 |
| 15/11/2026 | Palabras de "no juego" ampliadas (toolkit, editor, artbook) | "Baldur's Gate 3 Toolkit" pasaba el umbral | ADR-008 |
| 15/11/2026 | Steam con intervalo mínimo de 1,5 s entre solicitudes en vivo | En la primera captura Steam respondió 862 veces HTTP 429 | ADR-004 |
| 15/11/2026 | GOG: un HTTP 400 se trata como "sin resultados" | GOG responde 400 cuando la búsqueda no encuentra nada | ADR-011 |
| 15/11/2026 | GameStop: si no hay precio nuevo, se publica el de usado marcado "(usado)" | Muchos juegos viejos solo se venden usados | ADR-005 |
| 15/11/2026 | Nota: las mediciones del pipeline se tomaron antes de la regla "año distinto"; esa regla agrega una comparación de enteros por candidato (costo despreciable) | Transparencia de las mediciones | — |
