# ADR-003 · Definición de "estar en oferta" · 15/11/2026

**Contexto.** No todas las tiendas muestran un precio regular tachado. El MSRP del repositorio no siempre refleja el precio normal actual (juegos viejos bajan de precio de forma permanente).

**Alternativas.**
- **Solo MSRP:** simple, pero marca como oferta cualquier juego viejo.
- **Solo precio tachado del sitio:** es lo que la tienda considera oferta, pero no existe en todas.
- **Combinado.**

**Decisión.** Un precio está en oferta si:
1. el sitio muestra un precio regular y `actual < regular`, con descuento ≥ 5 %; **o**
2. el sitio no muestra precio regular y `actual ≤ 0,85 × MSRP`.

**Consecuencias.** Se evitan falsas ofertas de juegos viejos. Se guarda el criterio que se usó (`criterio_oferta`) para poder auditarlo.

**Estado.** Aceptada.
