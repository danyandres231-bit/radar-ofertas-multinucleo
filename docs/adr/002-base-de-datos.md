# ADR-002 · Base de datos · 15/11/2026

**Contexto.** Se necesita guardar 500+ juegos, títulos alternativos y **cada observación de precio** (historial). La ejecución programada corre en GitHub Actions, sin servidor propio.

**Alternativas.**
- **SQLite en un archivo dentro del repositorio:** sin servidor, gratis, viaja con el repo y conserva el historial entre ejecuciones. Desventaja: un solo escritor a la vez.
- **PostgreSQL en la nube (Supabase/Neon):** acceso concurrente. Desventaja: credenciales, dependencia externa y latencia de red que contamina las mediciones.
- **Firebase:** sin esquema; difícil hacer consultas de historial.

**Decisión.** SQLite (`data/radar.db`) con `Microsoft.Data.Sqlite`. Solo la etapa 4 (Persistir) escribe, desde un único hilo consumidor.

**Consecuencias.** No hay contención en la BD; el archivo crece con el historial (se estima < 50 MB al semestre).

**Estado.** Aceptada.
