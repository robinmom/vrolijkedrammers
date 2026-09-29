import type { components } from '@drammers/api-client';

type Issue = components['schemas']['ImportIssue'];

/**
 * Fouten van een import (fase 12c/16) als tabel: de regel in het bestand met opgave, startnummer en naam zoals ze
 * daar staan, wat er mis is en wat je eraan doet. Bij één fout wordt niets ingelezen.
 */
export function ImportErrors({
  errors,
  withRegistrationNumber = true,
}: {
  errors: Issue[];
  withRegistrationNumber?: boolean;
}) {
  if (errors.length === 0) {
    return null;
  }
  return (
    <div className="alert alert-error" role="alert">
      <strong>
        {errors.length} fout{errors.length === 1 ? '' : 'en'}: er wordt niets ingelezen. Pas het bestand aan en kies het
        opnieuw.
      </strong>
      <div className="table-scroll" tabIndex={0} role="region" aria-label="Fouten in het bestand">
        <table className="table compact">
          <caption className="visually-hidden">Fouten in het bestand</caption>
          <thead>
            <tr>
              <th scope="col">Regel</th>
              {withRegistrationNumber ? <th scope="col">Opgave</th> : null}
              <th scope="col">Startnummer</th>
              <th scope="col">Naam in bestand</th>
              <th scope="col">Waarom</th>
              <th scope="col">Wat te doen</th>
            </tr>
          </thead>
          <tbody>
            {errors.map((e, i) => (
              <tr key={i}>
                <td>{e.row || '–'}</td>
                {withRegistrationNumber ? <td>{e.registrationNumber ?? '–'}</td> : null}
                <td>{e.startNumber ?? '–'}</td>
                <td>{e.groupName ?? <em>geen</em>}</td>
                <td>{e.message}</td>
                <td>{e.advice ?? '–'}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
