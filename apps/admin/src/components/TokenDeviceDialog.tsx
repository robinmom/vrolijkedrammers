import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { useApi } from '../api/ApiContext';
import { useApiMutation } from '../api/hooks';
import { SALES_KEYS, type SaleOrderRow } from '../api/sales';
import { formatDateTime } from '../format';
import { Dialog } from './Dialog';
import { ProblemAlert } from './ProblemAlert';

/**
 * Munten zitten vast aan het toestel van de aankoop. Het bestuur kan ze één keer naar een ander aangemeld toestel van
 * hetzelfde lid verplaatsen (bijv. bij een kapotte of verloren telefoon), met een reden in de auditlog. Geef een `key`
 * per bestelling mee, zodat keuze en reden per bestelling opnieuw beginnen.
 */
export function TokenDeviceDialog({
  order,
  onClose,
  onMessage,
}: {
  order: SaleOrderRow | null;
  onClose: () => void;
  onMessage: (message: string) => void;
}) {
  const api = useApi();
  const [deviceId, setDeviceId] = useState('');
  const [reason, setReason] = useState('');
  const info = useQuery({
    queryKey: ['sales', 'token-device', order?.id ?? ''],
    enabled: order !== null,
    queryFn: async () =>
      (await api.GET('/api/v1/admin/sales/orders/{id}/token-device', { params: { path: { id: order!.id } } })).data!,
  });
  const move = useApiMutation(
    (v: { id: string; deviceId: string; reason: string }) =>
      api.POST('/api/v1/admin/sales/orders/{id}/token-device', {
        params: { path: { id: v.id } },
        body: { deviceId: v.deviceId, reason: v.reason },
      }),
    SALES_KEYS,
  );

  const data = info.data;
  return (
    <Dialog open={order !== null} title={order ? `Munten ${order.number}: toestel` : ''} onClose={onClose}>
      <ProblemAlert error={info.error} />
      {data ? (
        <form
          onSubmit={(e) => {
            e.preventDefault();
            if (!order || !deviceId) return;
            move.mutate(
              { id: order.id, deviceId, reason: reason.trim() },
              {
                onSuccess: () => {
                  onMessage(`Munten van ${order.number} verplaatst. Dit kan niet nog een keer.`);
                  onClose();
                },
              },
            );
          }}
        >
          <p>
            Gekoppeld aan: <strong>{data.deviceName ?? 'nog geen toestel'}</strong>
            {data.deviceName && !data.deviceActive ? ' (afgemeld)' : ''}
          </p>
          {data.movedAt ? (
            <p>
              Al verplaatst op {formatDateTime(data.movedAt)}
              {data.movedBy ? ` door ${data.movedBy}` : ''}. Nog een keer verplaatsen kan niet.
            </p>
          ) : !data.canMove ? (
            <p>Deze munten zijn al afgehaald of geannuleerd.</p>
          ) : data.options.length === 0 ? (
            <p>
              Het lid heeft geen ander aangemeld toestel. Laat het lid eerst inloggen in de app op het nieuwe toestel.
            </p>
          ) : (
            <>
              <p>
                Munten zitten vast aan het toestel van de aankoop. Je kunt ze <strong>één keer</strong> naar een ander
                toestel van het lid verplaatsen, bijvoorbeeld bij een kapotte of verloren telefoon. Codes op het oude
                toestel werken daarna niet meer.
              </p>
              <div className="field">
                <label htmlFor="munten-toestel">Nieuw toestel</label>
                <select id="munten-toestel" value={deviceId} onChange={(e) => setDeviceId(e.target.value)} required>
                  <option value="">Kies een toestel</option>
                  {data.options.map((d) => (
                    <option key={d.id} value={d.id}>
                      {d.name} (laatst gezien {formatDateTime(d.lastSeenAt)})
                    </option>
                  ))}
                </select>
              </div>
              <div className="field">
                <label htmlFor="munten-reden">Reden</label>
                <textarea
                  id="munten-reden"
                  rows={2}
                  minLength={5}
                  maxLength={500}
                  required
                  value={reason}
                  onChange={(e) => setReason(e.target.value)}
                />
              </div>
            </>
          )}
          <ProblemAlert error={move.error} />
          <div className="actions">
            <button type="button" className="button secondary" onClick={onClose}>
              Terug
            </button>
            {data.canMove && data.options.length > 0 ? (
              <button
                type="submit"
                className="button danger"
                disabled={move.isPending || !deviceId || reason.trim().length < 5}
              >
                Verplaatsen
              </button>
            ) : null}
          </div>
        </form>
      ) : null}
    </Dialog>
  );
}
