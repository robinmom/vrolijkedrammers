/**
 * Leeg beheerportal-skelet (fase 0). Login, navigatie en beheerschermen volgen vanaf fase 4.
 */
export function App() {
  return (
    <div className="layout">
      <nav className="sidebar" aria-label="Navigatie">
        <h1>De Vrolijke Drammers</h1>
        <p>Beheerportal</p>
      </nav>
      <main>
        <h2>Dashboard</h2>
        <section className="card" aria-label="Status">
          <p className="muted">Het beheerportal is in opbouw. Inloggen en beheerschermen volgen in een volgende fase.</p>
        </section>
      </main>
    </div>
  );
}
