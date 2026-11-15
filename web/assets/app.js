// Utilidades comunes del sitio (sin frameworks).
export const $ = (s, r = document) => r.querySelector(s);
export const dinero = v => v == null ? '—' : v.toLocaleString('en-US', { style: 'currency', currency: 'USD' });
export const fecha = iso => iso ? new Date(iso).toLocaleString('es-CR', { dateStyle: 'medium', timeStyle: 'short' }) : '—';
export const fechaCorta = iso => iso ? new Date(iso).toLocaleDateString('es-CR', { dateStyle: 'medium' }) : '—';
export const esc = s => String(s ?? '').replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
export const claseScore = v => v >= 85 ? 's-alto' : v >= 70 ? 's-medio' : 's-bajo';
export async function json(url) { const r = await fetch(url, { cache: 'no-cache' }); if (!r.ok) throw new Error(url + ' ' + r.status); return r.json(); }

// Título que se arma letra por letra.
export function armar(el) {
  if (!el) return;
  const partes = [...el.childNodes];
  el.textContent = '';
  let i = 0;
  for (const n of partes) {
    const destino = n.nodeType === 1 ? el.appendChild(n.cloneNode(false)) : el;
    for (const ch of n.textContent) {
      const s = document.createElement('span');
      s.className = 'l'; s.textContent = ch; s.style.animationDelay = (i++ * 28) + 'ms';
      destino.appendChild(s);
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
