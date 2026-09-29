/** Eenvoudige, toegankelijke grafieken (SVG) voor het portal: taartdiagram en staafdiagram, met de cijfers als tekst. */

export interface Slice {
  label: string;
  value: number;
  /** CSS-kleur, bij voorkeur een variabele uit het thema. */
  color: string;
}

function arc(cx: number, cy: number, r: number, start: number, end: number) {
  const x1 = cx + r * Math.sin(start);
  const y1 = cy - r * Math.cos(start);
  const x2 = cx + r * Math.sin(end);
  const y2 = cy - r * Math.cos(end);
  const large = end - start > Math.PI ? 1 : 0;
  return `M ${cx} ${cy} L ${x1} ${y1} A ${r} ${r} 0 ${large} 1 ${x2} ${y2} Z`;
}

/** Taartdiagram met legenda; de schermlezer krijgt de verdeling als tekst. */
export function PieChart({ slices, size = 160, title }: { slices: Slice[]; size?: number; title: string }) {
  const total = slices.reduce((sum, s) => sum + s.value, 0);
  const r = size / 2;
  let angle = 0;
  const description = slices
    .map((s) => `${s.label}: ${s.value}${total ? ` (${Math.round((s.value / total) * 100)}%)` : ''}`)
    .join(', ');
  return (
    <figure className="chart pie-chart">
      <svg
        width={size}
        height={size}
        viewBox={`0 0 ${size} ${size}`}
        role="img"
        aria-label={`${title}. ${description}`}
      >
        {total === 0 ? (
          <circle cx={r} cy={r} r={r - 1} style={{ fill: 'var(--dvd-surface-muted)' }} />
        ) : (
          slices
            .filter((s) => s.value > 0)
            .map((s) => {
              const start = angle;
              angle += (s.value / total) * Math.PI * 2;
              return s.value === total ? (
                <circle key={s.label} cx={r} cy={r} r={r - 1} style={{ fill: s.color }} />
              ) : (
                <path key={s.label} d={arc(r, r, r - 1, start, angle)} style={{ fill: s.color }} />
              );
            })
        )}
      </svg>
      <figcaption>
        <ul className="legend">
          {slices.map((s) => (
            <li key={s.label}>
              <span className="swatch" style={{ background: s.color }} aria-hidden="true" />
              {s.label}: <strong>{s.value}</strong>
              {total ? <span className="muted"> ({Math.round((s.value / total) * 100)}%)</span> : null}
            </li>
          ))}
        </ul>
      </figcaption>
    </figure>
  );
}

export interface Bar {
  label: string;
  value: number;
  secondary?: number;
}

/** Staafdiagram (bijv. aankomsten per uur); optioneel een tweede, lichtere waarde (alle scans). */
export function BarChart({
  bars,
  title,
  primaryLabel,
  secondaryLabel,
}: {
  bars: Bar[];
  title: string;
  primaryLabel: string;
  secondaryLabel?: string;
}) {
  const max = Math.max(1, ...bars.map((b) => Math.max(b.value, b.secondary ?? 0)));
  return (
    <figure className="chart bar-chart">
      <div
        className="bars"
        role="img"
        aria-label={`${title}. ${bars.map((b) => `${b.label}: ${b.value} ${primaryLabel.toLowerCase()}`).join(', ')}`}
      >
        {bars.map((b) => (
          <div key={b.label} className="bar-column">
            <div className="bar-stack">
              {b.secondary !== undefined ? (
                <div
                  className="bar secondary"
                  style={{ height: `${(b.secondary / max) * 100}%` }}
                  title={`${b.secondary} ${secondaryLabel ?? ''}`}
                />
              ) : null}
              <div
                className="bar primary"
                style={{ height: `${(b.value / max) * 100}%` }}
                title={`${b.value} ${primaryLabel}`}
              />
            </div>
            <span className="bar-value">{b.value}</span>
            <span className="bar-label">{b.label}</span>
          </div>
        ))}
      </div>
      <figcaption className="legend-row">
        <span>
          <span className="swatch" style={{ background: 'var(--dvd-link-text)' }} aria-hidden="true" /> {primaryLabel}
        </span>
        {secondaryLabel ? (
          <span>
            <span className="swatch" style={{ background: 'var(--dvd-tint-blue)' }} aria-hidden="true" />{' '}
            {secondaryLabel}
          </span>
        ) : null}
      </figcaption>
    </figure>
  );
}
