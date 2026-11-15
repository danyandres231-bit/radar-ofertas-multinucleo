# ADR-007 · Hosting del sitio y actualización programada · 15/11/2026

**Contexto.** El sitio debe estar en una URL pública y regenerarse solo. El pipeline tarda minutos y usa núcleos; no puede correr dentro de una función serverless.

**Alternativas.**
- **Sitio estático en Vercel + pipeline en GitHub Actions:** gratis, el pipeline corre en una máquina de GitHub, hace commit de los JSON y Vercel publica.
- **GitHub Pages:** igual de válido, menos control de cabeceras y despliegues de vista previa.
- **Servidor propio (VPS):** costo y mantenimiento.

**Decisión.** Sitio estático (HTML + JS, sin framework) en `web/`, publicado en Vercel desde el repositorio. GitHub Actions corre el pipeline cada 12 h (`cron: "17 */12 * * *"`), actualiza `data/radar.db` y `web/data/*.json` y hace commit.

**Consecuencias.** El sitio nunca depende de que el pipeline esté vivo. La fecha de la última ejecución se muestra en el encabezado.

**Estado.** Aceptada.
