import { Link, Outlet } from '@tanstack/react-router';
import { useState } from 'react';
import { useMe } from '../api/hooks';
import { useAuth } from '../auth/AuthContext';
import { visibleNavItems } from '../navigation';
import { ProblemAlert } from './ProblemAlert';

/** Hoofdlayout: navigatie op basis van permissions; UI verbergt, de API dwingt af (docs/07 §1). */
export function Layout() {
  const auth = useAuth();
  const me = useMe();
  const [menuOpen, setMenuOpen] = useState(false);
  const items = visibleNavItems(me.data?.permissions ?? []);

  return (
    <div className="layout">
      <a className="skip-link" href="#inhoud">
        Naar de inhoud
      </a>
      <header className="sidebar">
        <div className="brand">
          <p className="brand-title">De Vrolijke Drammers</p>
          <p className="brand-subtitle">Beheerportal</p>
        </div>
        <button
          type="button"
          className="button secondary small menu-toggle"
          aria-expanded={menuOpen}
          aria-controls="hoofdmenu"
          onClick={() => setMenuOpen((v) => !v)}
        >
          Menu
        </button>
        <nav id="hoofdmenu" aria-label="Navigatie" className={menuOpen ? 'open' : undefined}>
          {me.isSuccess && items.length === 0 ? (
            <p className="muted">Geen beheerrechten</p>
          ) : (
            <ul>
              {items.map((item) => (
                <li key={item.to}>
                  <Link
                    to={item.to}
                    activeOptions={{ exact: item.to === '/' }}
                    activeProps={{ 'aria-current': 'page', className: 'active' }}
                    onClick={() => setMenuOpen(false)}
                  >
                    {item.label}
                  </Link>
                </li>
              ))}
            </ul>
          )}
        </nav>
        <div className="account">
          <p>{me.data?.displayName ?? auth.user?.name}</p>
          <button type="button" className="button secondary small" onClick={() => void auth.signOut()}>
            Afmelden
          </button>
        </div>
      </header>
      <main id="inhoud" tabIndex={-1}>
        {me.isError ? <ProblemAlert error={me.error} /> : null}
        {me.isSuccess && items.length === 0 ? <NoAccess /> : me.isSuccess ? <Outlet /> : <p>Laden…</p>}
      </main>
    </div>
  );
}

function NoAccess() {
  return (
    <section className="card">
      <h1>Geen beheerrechten</h1>
      <p>
        Je bent aangemeld, maar je account heeft geen rechten voor het beheerportal. Neem contact op met het bestuur als je
        denkt dat dit niet klopt.
      </p>
    </section>
  );
}
