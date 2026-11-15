# ADR-009 · Modo replay · 15/11/2026

**Contexto.** Las mediciones deben ser reproducibles; la red real varía y los sitios bloquean.

**Alternativas.**
- **Servidor HTTP local** que sirve los archivos: realista, pero agrega un proceso y uso de CPU del propio servidor en la misma máquina.
- **Adaptador dentro del proceso** (`HttpMessageHandler` propio) que lee las respuestas guardadas y espera la latencia simulada con `Task.Delay`.

**Decisión.** Adaptador `ReplayHandler : HttpMessageHandler`. En modo captura, `CaptureHandler` guarda cada respuesta en `data/replay/<sha1(url)>.bin` + índice. En replay se devuelve el mismo contenido tras una latencia `fija` o `fija ± jitter` (configurable). El resto del código es idéntico al modo vivo: solo cambia el handler inyectado en el `HttpClient`.

**Consecuencias.** La espera simulada no ocupa hilos (igual que la red real). No mide costos de TLS ni del kernel; se compara con una ejecución en vivo (pregunta 7).

**Estado.** Aceptada.
