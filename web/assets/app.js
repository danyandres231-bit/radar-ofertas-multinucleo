// Utilidades comunes del sitio (sin frameworks).
export const $ = (s, r = document) => r.querySelector(s);
export const dinero = v => v == null ? '—' : v.toLocaleString('en-US', { style: 'currency', currency: 'USD' });
export const fecha = iso => iso ? new Date(iso).toLocaleString('es-CR', { dateStyle: 'medium', timeStyle: 'short' }) : '—';
export const fechaCorta = iso => iso ? new Date(iso).toLocaleDateString('es-CR', { dateStyle: 'medium' }) : '—';
export const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
export const claseScore = v => v >= 85 ? 's-alto' : v >= 70 ? 's-medio' : 's-bajo';
export async function json(url) { const r = await fetch(url, { cache: 'no-cache' }); if (!r.ok) throw new Error(url + ' ' + r.status); return r.json(); }

// Título que se arma letra por letra (las palabras no se cortan; un <span> de acento entra como bloque).
export function armar(el) {
  if (!el) return;
  const partes = [...el.childNodes];
  el.textContent = '';
  let i = 0;
  const letra = (txt, destino) => {
    const s = document.createElement('span');
    s.className = 'l'; s.textContent = txt; s.style.animationDelay = (i++ * 28) + 'ms';
    destino.appendChild(s); return s;
  };
  for (const n of partes) {
    if (n.nodeType === 1) {               // elemento (p. ej. .acento con degradado): entra completo
      const c = n.cloneNode(true); c.classList.add('l'); c.style.animationDelay = (i * 28) + 'ms'; i += 6;
      el.appendChild(c); continue;
    }
    for (const trozo of n.textContent.split(/(\s+)/)) {
      if (!trozo) continue;
      if (/^\s+$/.test(trozo)) { el.appendChild(document.createTextNode(' ')); continue; }
      const palabra = document.createElement('span');
      palabra.className = 'palabra';
      for (const ch of trozo) letra(ch, palabra);
      el.appendChild(palabra);
    }
  }
  el.classList.add('armar');
}

// Tarjetas con tilt 3D y brillo que sigue el cursor (delegación de eventos).
export function tilt(contenedor) {
  if (matchMedia('(prefers-reduced-motion: reduce)').matches || matchMedia('(hover: none)').matches) return;
  contenedor.addEventListener('pointermove', e => {
    const t = e.target.closest('.tarjeta'); if (!t) return;
    const r = t.getBoundingClientRect(), x = (e.clientX - r.left) / r.width, y = (e.clientY - r.top) / r.height;
    t.style.setProperty('--ry', ((x - .5) * 8).toFixed(2) + 'deg');
    t.style.setProperty('--rx', ((.5 - y) * 8).toFixed(2) + 'deg');
    t.style.setProperty('--mx', (x * 100) + '%'); t.style.setProperty('--my', (y * 100) + '%');
  });
  contenedor.addEventListener('pointerout', e => {
    const t = e.target.closest('.tarjeta'); if (t && !t.contains(e.relatedTarget)) { t.style.removeProperty('--rx'); t.style.removeProperty('--ry'); }
  });
}

// Tema claro/oscuro (preferencia guardada solo en este navegador).
export function tema() {
  const b = $('.tema'); if (!b) return;
  try { const t = localStorage.getItem('tema'); if (t) document.documentElement.dataset.theme = t; } catch {}
  b.addEventListener('click', () => {
    const oscuro = getComputedStyle(document.documentElement).getPropertyValue('--abismo').trim() === '#0b1426';
    const nuevo = oscuro ? 'light' : 'dark';
    document.documentElement.dataset.theme = nuevo;
    try { localStorage.setItem('tema', nuevo); } catch {}
  });
}

// ---------- Datos del sitio: un solo paquete comprimido (data/sitio.bin = JSON compacto + gzip) ----------
const TIENDAS = ['steam', 'gog', 'nintendo', 'gamestop'];
const NOMBRE_TIENDA = { steam: 'Steam', gog: 'GOG', nintendo: 'Nintendo eShop', gamestop: 'GameStop' };
const PREFIJO = ['https://store.steampowered.com/', 'https://www.gog.com/en/game/', 'https://www.nintendo.com/us/store/products/', 'https://www.gamestop.com/'];
let _datos;
async function paquete() {
  if (_datos) return _datos;
  const partes = await Promise.all([0, 1, 2].map(async i => {
    const r = await fetch(`/data/sitio.${i}.bin`, { cache: 'no-cache' });
    if (!r.ok) throw new Error('sitio.' + i + '.bin ' + r.status);
    return new Uint8Array(await r.arrayBuffer());
  }));
  const todo = new Uint8Array(partes.reduce((n, p) => n + p.length, 0));
  let o = 0; for (const p of partes) { todo.set(p, o); o += p.length; }
  const flujo = new Blob([todo]).stream().pipeThrough(new DecompressionStream('gzip'));
  _datos = JSON.parse(await new Response(flujo).text());
  return _datos;
}
const pct = (a, r) => r > a ? Math.round(100 * (1 - a / r)) : 0;
function expandir(d, x) {
  const [id, titulo, img, tipo, fechaLanz, msrp, minimo, meta, steamU, pr, hist, du, desact] = x;
  const precios = pr.map(([t, actual, regular, fis, pl, u, dud, of, fe, viejo]) => ({
    tienda: TIENDAS[t], tiendaNombre: NOMBRE_TIENDA[TIENDAS[t]], actual, regular, descuento: pct(actual, regular ?? 0), enOferta: !!of,
    formato: fis ? 'fisico' : 'digital', plataforma: d.pl[pl] || null, url: u.startsWith('http') ? u : PREFIJO[t] + u, dudoso: !!dud,
    obtenidoEn: d.fe[fe], desactualizado: !!viejo }));
  const imagen = img ? (img.startsWith('~') ? 'https://www.metacritic.com/a/img/catalog/provider/' + img.slice(1) : img) : null;
  return {
    id, titulo, imagen, tipo, fechaLanz, msrp, minimoHistorico: minimo, precios,
    calificaciones: [meta != null && { f: 'metacritic', v: meta }, steamU != null && { f: 'steam_usuarios', v: steamU }].filter(Boolean),
    historial: hist.map(([t, pts]) => ({ tienda: TIENDAS[t], nombre: NOMBRE_TIENDA[TIENDAS[t]], puntos: pts.map(([fecha, precio]) => ({ fecha, precio })) })),
    duracion: du ? { fuente: du[0], principal: du[1], extras: du[2], completista: du[3], obtenidoEn: du[4] } : null,
    desactualizado: desact.map(([fuente, desde]) => ({ fuente, desde })),
    plataformas: [...new Set(precios.map(p => p.plataforma).filter(Boolean))].sort(),
    metascore: meta, steamUsuarios: steamU
  };
}
export async function catalogo() {
  const d = await paquete();
  const juegos = d.j.map(x => {
    const g = expandir(d, x), m = g.precios[0];
    return { ...g, mejor: m, tiendas: [...new Set(g.precios.map(p => p.tienda))].sort(), formatos: [...new Set(g.precios.map(p => p.formato))].sort(),
      descuentoMax: Math.max(...g.precios.map(p => p.descuento)), enOferta: g.precios.some(p => p.enOferta),
      minimoHistorico: m.actual <= g.minimoHistorico + 0.001, minimoValor: g.minimoHistorico, desactualizado: g.desactualizado.length > 0 };
  });
  return { actualizado: d.a, juegos };
}
export async function detalle(id) {
  const d = await paquete();
  const x = d.j.find(j => String(j[0]) === String(id));
  if (!x) throw new Error('no existe ' + id);
  return expandir(d, x);
}
export async function metricas() {
  const m = (await paquete()).m;
  return { ...m,
    noPublicados: m.noPublicados.map(([titulo, motivo]) => ({ titulo, motivo, tiendas: [] })),
    dudosos: m.dudosos.map(([titulo, tienda, tituloTienda, similitud, motivo]) => ({ titulo, tienda, tituloTienda, similitud, motivo })),
    fallos: m.fallos.map(([titulo, fuente, error]) => ({ titulo, fuente, error })) };
}
export async function bench() { const b = (await paquete()).b; if (!b) throw new Error('sin mediciones'); return b; }
