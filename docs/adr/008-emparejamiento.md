# ADR-008 · Algoritmo de emparejamiento de títulos · 15/11/2026

**Contexto.** Cada tienda nombra distinto el mismo juego ("Zelda BOTW" vs "The Legend of Zelda: Breath of the Wild – Nintendo Switch"). Hay remasterizaciones, ediciones, secuelas con números romanos, DLC y bundles.

**Alternativas.**
- **Levenshtein normalizado:** sensible al orden y a sufijos largos.
- **Jaro-Winkler:** premia prefijos comunes; falla con palabras reordenadas.
- **Similitud por tokens (Jaccard / Sørensen-Dice):** robusta al orden; ignora errores de letras.
- **Combinación.**

**Decisión.** `score = 0,6 · JaroWinkler(a, b) + 0,4 · Dice(tokens(a), tokens(b))`, implementado a mano en `Radar.Core/Matching`. Antes se normaliza: minúsculas, sin tildes ni puntuación, números romanos → arábigos (II → 2), sin sufijos de plataforma/edición ("nintendo switch", "ps5", "deluxe edition", "goty"…). Reglas duras: si los números de secuela difieren → no empareja; si el candidato contiene "dlc", "pack", "bundle", "season pass" y el juego no → caso dudoso. Umbral inicial 0,88; se ajusta con 60 pares validados a mano (precisión y exhaustividad).

**Consecuencias.** Costo O(n·m) por par de títulos; con decenas de miles de candidatos de los sitemaps la etapa es CPU-intensiva y paralelizable por juego.

**Estado.** Aceptada (el umbral final se registra al validar).
