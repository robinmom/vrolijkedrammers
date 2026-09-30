// Website De Vrolijke Drammers (fase 21c): kleine verbeteringen bovenop pagina's die ook zonder JavaScript werken.
(() => {
  'use strict';

  // Uitklapmenu's (<details>): één tegelijk open, dicht bij een klik ernaast of met Escape.
  const dropdowns = [...document.querySelectorAll('details.dropdown, details.menu-toggle')];
  dropdowns.forEach((d) =>
    d.addEventListener('toggle', () => {
      if (d.open) dropdowns.filter((o) => o !== d).forEach((o) => (o.open = false));
    }),
  );
  document.addEventListener('click', (e) => {
    dropdowns.filter((d) => d.open && !d.contains(e.target)).forEach((d) => (d.open = false));
  });

  // Prinsvenster (?prins=…): sluiten met Escape of een klik op de achtergrond; focus naar het venster.
  const backdrop = document.querySelector('.modal-backdrop');
  const closeLink = document.querySelector('[data-modal-close]');
  if (backdrop && closeLink) {
    backdrop.querySelector('.modal')?.setAttribute('tabindex', '-1');
    backdrop.querySelector('.modal')?.focus();
    backdrop.addEventListener('click', (e) => {
      if (e.target === backdrop) location.href = closeLink.href;
    });
  }

  document.addEventListener('keydown', (e) => {
    if (e.key !== 'Escape') return;
    dropdowns.forEach((d) => (d.open = false));
    if (closeLink) location.href = closeLink.href;
  });

  // Lightbox voor een fotoalbum: groot bekijken, vegen of pijltjestoetsen naar de volgende; zonder JS opent de foto zelf.
  const gallery = document.querySelector('[data-gallery]');
  const box = document.querySelector('[data-lightbox]');
  if (!gallery || !box) return;
  const links = [...gallery.querySelectorAll('a[data-index]')];
  const image = box.querySelector('[data-image]');
  const caption = box.querySelector('[data-caption]');
  const counter = box.querySelector('[data-counter]');
  let current = 0;
  let opener = null;

  const show = (index) => {
    current = (index + links.length) % links.length;
    const link = links[current];
    image.src = link.href;
    image.alt = link.querySelector('img')?.alt ?? '';
    caption.textContent = link.dataset.caption ?? '';
    counter.textContent = `${current + 1} / ${links.length}`;
    // Alvast de volgende laden, zodat vegen vlot gaat.
    const next = links[(current + 1) % links.length];
    if (next) new Image().src = next.href;
  };
  const open = (index, from) => {
    opener = from;
    box.hidden = false;
    document.body.style.overflow = 'hidden';
    show(index);
    box.querySelector('[data-close]').focus();
  };
  const close = () => {
    box.hidden = true;
    document.body.style.overflow = '';
    image.removeAttribute('src');
    opener?.focus();
  };

  links.forEach((link) =>
    link.addEventListener('click', (e) => {
      e.preventDefault();
      open(Number(link.dataset.index), link);
    }),
  );
  box.querySelector('[data-close]').addEventListener('click', close);
  box.querySelector('[data-prev]').addEventListener('click', () => show(current - 1));
  box.querySelector('[data-next]').addEventListener('click', () => show(current + 1));
  box.addEventListener('keydown', (e) => {
    if (e.key === 'ArrowLeft') show(current - 1);
    else if (e.key === 'ArrowRight') show(current + 1);
    else if (e.key === 'Escape') close();
    else if (e.key === 'Tab') {
      // Focus binnen de lightbox houden.
      const focusable = [...box.querySelectorAll('button')].filter((b) => b.offsetParent !== null);
      const first = focusable[0];
      const last = focusable[focusable.length - 1];
      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault();
        first.focus();
      }
    }
  });

  // Vegen op de telefoon.
  let startX = null;
  box.addEventListener('touchstart', (e) => (startX = e.touches[0].clientX), { passive: true });
  box.addEventListener(
    'touchend',
    (e) => {
      if (startX === null) return;
      const dx = e.changedTouches[0].clientX - startX;
      if (Math.abs(dx) > 50) show(current + (dx < 0 ? 1 : -1));
      startX = null;
    },
    { passive: true },
  );
})();
