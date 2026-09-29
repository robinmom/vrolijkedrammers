import { Link, useNavigate, useParams } from '@tanstack/react-router';
import { useEffect, useState, type FormEvent } from 'react';
import { useApi } from '../api/ApiContext';
import {
  useApiMutation,
  useMe,
  useNotification,
  useNotificationAudienceOptions,
  useNotifications,
  type AudiencePreview,
  type CreateNotificationRequest,
  type NotificationAudience,
  type NotificationCategory,
  type NotificationSummary,
} from '../api/hooks';
import { DataTable, columnHelper } from '../components/DataTable';
import { ConfirmDialog } from '../components/Dialog';
import { Checkbox, Field } from '../components/Field';
import { Icon } from '../components/Icon';
import { Pagination } from '../components/Pagination';
import { ProblemAlert, SuccessMessage } from '../components/ProblemAlert';
import { MemberPicker } from '../components/PublicationFields';
import { formatDateTime, fromLocalInput, notificationCategoryLabels, notificationStatusLabels, toLocalInput } from '../format';

const TITLE_MAX = 65;
const BODY_MAX = 240;

const statusTone: Record<string, string> = { Sent: 'ok', Scheduled: 'warn', PartiallyFailed: 'warn', Failed: 'error', Canceled: '' };

function StatusBadge({ status }: { status: string | null }) {
  return status ? <span className={`badge ${statusTone[status] ?? ''}`}>{notificationStatusLabels[status] ?? status}</span> : null;
}

const helper = columnHelper<NotificationSummary>();
const columns = [
  helper.accessor('title', {
    header: 'Melding',
    cell: (info) => (
      <Link to="/meldingen/$id" params={{ id: info.row.original.id }}>
        {info.getValue()}
      </Link>
    ),
  }),
  helper.accessor('category', { header: 'Categorie', cell: (info) => notificationCategoryLabels[info.getValue()] ?? info.getValue() }),
  helper.accessor('status', { header: 'Status', cell: (info) => <StatusBadge status={info.getValue()} /> }),
  helper.display({
    id: 'moment',
    header: 'Moment',
    cell: (info) => formatDateTime(info.row.original.sentAt ?? info.row.original.scheduledAt ?? info.row.original.createdAt),
  }),
  helper.accessor('senderName', { header: 'Door', cell: (info) => info.getValue() ?? (info.row.original.sourceType === 'News' ? 'Nieuws (automatisch)' : 'Systeem') }),
  helper.display({
    id: 'bereik',
    header: 'Ontvangers / afgeleverd / gelezen',
    cell: (info) => `${info.row.original.recipientCount} / ${info.row.original.deliveredCount} / ${info.row.original.readCount}`,
  }),
];

/** Pushmeldingen (fase 10): historie met statistiek; nieuwe melding via "Melding versturen". */
export function NotificationsPage() {
  const [page, setPage] = useState(1);
  const notifications = useNotifications(page);
  return (
    <>
      <div className="page-header">
        <div className="page-title">
          <h1>Meldingen</h1>
          <p className="page-subtitle">
            Pushmeldingen naar de app. Elke melding staat ook in de inbox van de ontvangers, ook als push bij hen uit staat. Afgeleverd
            wordt een kwartier na verzending bijgewerkt.
          </p>
        </div>
        <div className="actions">
          <Link to="/meldingen/nieuw" className="button">
            <Icon name="plus" size={18} /> Melding versturen
          </Link>
        </div>
      </div>
      <ProblemAlert error={notifications.error} />
      <DataTable
        caption="Meldingen"
        columns={columns}
        data={notifications.data?.items ?? []}
        emptyText="Nog geen meldingen verstuurd."
        footer={
          notifications.data ? (
            <Pagination
              page={notifications.data.page}
              pageSize={notifications.data.pageSize}
              totalCount={notifications.data.totalCount}
              onPage={setPage}
              noun="meldingen"
            />
          ) : null
        }
      />
    </>
  );
}

type AudienceMode = 'everyone' | 'members' | 'selection';

const emptyAudience: NotificationAudience = { everyone: false, members: false, dansgarde: false, roles: [], groups: [], memberIds: [] };

/** Vooraf ingevulde doelgroep vanuit een link, bijv. "Melding aan dansgarde" (?doelgroep=dansgarde) of een dansgroep (?groep=id). */
function initialSelection(): NotificationAudience {
  const params = new URLSearchParams(window.location.search);
  const group = params.get('groep');
  return { ...emptyAudience, dansgarde: params.get('doelgroep') === 'dansgarde', groups: group ? [group] : [] };
}

/**
 * Melding opstellen (docs/02 §5.3): tekst, categorie, doelgroep met het aantal ontvangers vooraf, direct of gepland.
 * Voor "Iedereen" en "Alle leden" volgt een bevestiging (OQ-44: geen vier-ogenprincipe).
 */
export function NotificationComposerPage() {
  const api = useApi();
  const navigate = useNavigate();
  const me = useMe();
  const options = useNotificationAudienceOptions();
  const canPickMembers = (me.data?.permissions ?? []).includes('member.read') && options.data?.anyAudience;
  const [title, setTitle] = useState('');
  const [body, setBody] = useState('');
  const [category, setCategory] = useState<NotificationCategory>('Program');
  const [deepLink, setDeepLink] = useState('');
  const [mode, setMode] = useState<AudienceMode>('selection');
  const [selection, setSelection] = useState<NotificationAudience>(initialSelection);
  const [scheduledAt, setScheduledAt] = useState<string | null>(null);
  const [preview, setPreview] = useState<AudiencePreview | null>(null);
  const [confirm, setConfirm] = useState(false);

  const audience: NotificationAudience =
    mode === 'everyone' ? { ...emptyAudience, everyone: true } : mode === 'members' ? { ...emptyAudience, members: true } : selection;
  const empty =
    mode === 'selection' && !selection.dansgarde && !selection.roles?.length && !selection.groups?.length && !selection.memberIds?.length;
  const audienceKey = JSON.stringify(audience);

  useEffect(() => {
    if (empty) {
      setPreview(null);
      return;
    }
    let active = true;
    const timer = setTimeout(async () => {
      const { data } = await api.POST('/api/v1/admin/notifications/preview-audience', { body: { audience, category } });
      if (active) {
        setPreview(data ?? null);
      }
    }, 300);
    return () => {
      active = false;
      clearTimeout(timer);
    };
  }, [api, audienceKey, category, empty]);

  const send = useApiMutation(
    async (request: CreateNotificationRequest) => (await api.POST('/api/v1/admin/notifications', { body: request })).data?.id,
    [['notifications']],
  );

  function submit(event: FormEvent) {
    event.preventDefault();
    if (mode !== 'selection') {
      setConfirm(true);
    } else {
      doSend();
    }
  }

  function doSend() {
    send.mutate(
      { title, body, category, audience, deepLink: deepLink.trim() || null, scheduledAt },
      {
        onSuccess: (id) => {
          setConfirm(false);
          if (id) {
            void navigate({ to: '/meldingen/$id', params: { id } });
          }
        },
        onError: () => setConfirm(false),
      },
    );
  }

  const urgent = options.data?.urgent ?? false;
  const categories = (Object.keys(notificationCategoryLabels) as NotificationCategory[]).filter(
    (c) => c !== 'System' && (c !== 'Urgent' || urgent),
  );
  const toggle = (key: 'roles' | 'groups', value: string, checked: boolean) => {
    const current = selection[key] ?? [];
    setSelection({ ...selection, [key]: checked ? [...current, value] : current.filter((v) => v !== value) });
  };

  return (
    <>
      <Link to="/meldingen" className="back-link">
        <Icon name="terug" size={16} /> Meldingen
      </Link>
      <h1>Melding versturen</h1>
      <form className="card" onSubmit={submit}>
        <Field
          label="Titel"
          required
          maxLength={TITLE_MAX}
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          hint={`${title.length}/${TITLE_MAX} tekens`}
        />
        <div className="field">
          <label htmlFor="melding-tekst">Tekst</label>
          <textarea
            id="melding-tekst"
            rows={4}
            required
            maxLength={BODY_MAX}
            value={body}
            onChange={(e) => setBody(e.target.value)}
            aria-describedby="melding-tekst-hint"
          />
          <small id="melding-tekst-hint" className="muted">
            {body.length}/{BODY_MAX} tekens. Geen persoonsgegevens: de tekst gaat via de pushdienst van Expo.
          </small>
        </div>
        <div className="form-grid">
          <div className="field">
            <label htmlFor="melding-categorie">Categorie</label>
            <select id="melding-categorie" value={category} onChange={(e) => setCategory(e.target.value as NotificationCategory)}>
              {categories.map((c) => (
                <option key={c} value={c}>
                  {notificationCategoryLabels[c]}
                </option>
              ))}
            </select>
          </div>
          <Field
            label="Link in de app (optioneel)"
            placeholder="drammers://nieuws/…"
            value={deepLink}
            onChange={(e) => setDeepLink(e.target.value)}
            hint="Bijv. drammers://nieuws/{id}, drammers://activiteit/{id}, drammers://agenda, drammers://optocht of drammers://fotos/{id}. Leeg of onbekend: de melding opent de inbox."
          />
        </div>

        <fieldset>
          <legend>Doelgroep</legend>
          {options.data?.anyAudience ? (
            <>
              {urgent ? (
                <div className="checkbox">
                  <input type="radio" id="doelgroep-iedereen" name="doelgroep" checked={mode === 'everyone'} onChange={() => setMode('everyone')} />
                  <label htmlFor="doelgroep-iedereen">Iedereen (ook gasten met push aan)</label>
                </div>
              ) : null}
              <div className="checkbox">
                <input type="radio" id="doelgroep-leden" name="doelgroep" checked={mode === 'members'} onChange={() => setMode('members')} />
                <label htmlFor="doelgroep-leden">Alle leden</label>
              </div>
              <div className="checkbox">
                <input type="radio" id="doelgroep-selectie" name="doelgroep" checked={mode === 'selection'} onChange={() => setMode('selection')} />
                <label htmlFor="doelgroep-selectie">Selectie van rollen, groepen of leden</label>
              </div>
            </>
          ) : (
            <p className="muted">Je kunt meldingen versturen aan de groepen waar je zelf in zit.</p>
          )}
          {mode === 'selection' ? (
            <>
              {(options.data?.roles ?? []).length > 0 ? (
                <fieldset>
                  <legend>Rollen</legend>
                  {(options.data?.roles ?? []).map((role) => (
                    <Checkbox
                      key={role.code}
                      label={role.name}
                      checked={(selection.roles ?? []).includes(role.code)}
                      onChange={(e) => toggle('roles', role.code, e.target.checked)}
                    />
                  ))}
                </fieldset>
              ) : null}
              {options.data?.anyAudience ? (
                <Checkbox
                  label="Dansgarde (iedereen met groep Dansgarde in e-Boekhouden)"
                  checked={Boolean(selection.dansgarde)}
                  onChange={(e) => setSelection({ ...selection, dansgarde: e.target.checked })}
                />
              ) : null}
              <fieldset>
                <legend>Groepen</legend>
                {(options.data?.groups ?? []).length === 0 ? <p className="muted">Geen groepen.</p> : null}
                {(options.data?.groups ?? []).map((group) => (
                  <Checkbox
                    key={group.id}
                    label={group.name}
                    checked={(selection.groups ?? []).includes(group.id)}
                    onChange={(e) => toggle('groups', group.id, e.target.checked)}
                  />
                ))}
              </fieldset>
              {canPickMembers ? (
                <MemberPicker selected={selection.memberIds ?? []} onChange={(memberIds) => setSelection({ ...selection, memberIds })} />
              ) : null}
              <p className="muted">
                Bij de dansgarde, groepen en leden ontvangen ook de ouders/verzorgers (tot het kind 18 is), met "Namens [naam]" voor
                de titel.
              </p>
            </>
          ) : null}
          <p role="status" className="kpi-hint">
            {empty
              ? 'Kies een doelgroep.'
              : preview
                ? `${preview.accounts} ontvangers${preview.guests ? ` + ${preview.guests} gasten` : ''}; push naar ${preview.pushDevices} apparaten${
                    preview.optedOut ? ` (${preview.optedOut} hebben deze categorie uitgezet)` : ''
                  }.`
                : 'Aantal ontvangers berekenen…'}
          </p>
        </fieldset>

        <div className="form-grid">
          <Checkbox
            label="Later versturen"
            checked={scheduledAt !== null}
            onChange={(e) => setScheduledAt(e.target.checked ? new Date(Date.now() + 3_600_000).toISOString() : null)}
          />
          {scheduledAt !== null ? (
            <Field
              label="Versturen op"
              type="datetime-local"
              required
              value={toLocalInput(scheduledAt)}
              onChange={(e) => setScheduledAt(fromLocalInput(e.target.value))}
            />
          ) : null}
        </div>
        <ProblemAlert error={send.error} />
        <div className="actions">
          <button type="submit" className="button" disabled={send.isPending || empty || !title.trim() || !body.trim()}>
            {scheduledAt ? 'Inplannen' : 'Versturen'}
          </button>
        </div>
      </form>
      <ConfirmDialog
        open={confirm}
        title={mode === 'everyone' ? 'Melding aan iedereen?' : 'Melding aan alle leden?'}
        message={`"${title}" gaat ${scheduledAt ? `op ${formatDateTime(scheduledAt)} ` : ''}naar ${
          preview ? `${preview.accounts + preview.guests} ontvangers` : mode === 'everyone' ? 'iedereen' : 'alle leden'
        }. Een verstuurde melding kan niet worden teruggehaald.`}
        confirmLabel={scheduledAt ? 'Inplannen' : 'Versturen'}
        busy={send.isPending}
        onCancel={() => setConfirm(false)}
        onConfirm={doSend}
      />
    </>
  );
}

/** Detail met doelgroep, afleverstatistiek en (zolang gepland) annuleren. */
export function NotificationDetailPage() {
  const { id } = useParams({ from: '/meldingen/$id' });
  const api = useApi();
  const notification = useNotification(id);
  const [confirmCancel, setConfirmCancel] = useState(false);
  const [message, setMessage] = useState<string | null>(null);
  const cancel = useApiMutation(
    () => api.POST('/api/v1/admin/notifications/{id}/cancel', { params: { path: { id } } }),
    [['notification', id], ['notifications']],
  );
  if (!notification.data) {
    return <>{notification.error ? <ProblemAlert error={notification.error} /> : <p>Laden…</p>}</>;
  }
  const n = notification.data;
  const s = n.summary;
  return (
    <>
      <Link to="/meldingen" className="back-link">
        <Icon name="terug" size={16} /> Meldingen
      </Link>
      <div className="page-header">
        <div className="page-title">
          <h1>
            {s.title} <StatusBadge status={s.status} />
          </h1>
          <p className="page-subtitle">
            {notificationCategoryLabels[s.category] ?? s.category} · {s.senderName ?? (s.sourceType === 'News' ? 'Nieuws (automatisch)' : 'Systeem')} ·{' '}
            {s.status === 'Scheduled' ? `gepland op ${formatDateTime(s.scheduledAt)}` : formatDateTime(s.sentAt ?? s.createdAt)}
          </p>
        </div>
        {s.status === 'Scheduled' ? (
          <div className="actions">
            <button type="button" className="button secondary" onClick={() => setConfirmCancel(true)}>
              Melding annuleren
            </button>
          </div>
        ) : null}
      </div>
      <SuccessMessage message={message} />
      <ProblemAlert error={cancel.error} />
      <section className="kpis" aria-label="Statistiek">
        {(
          [
            ['Ontvangers', s.recipientCount],
            ['Pushberichten', s.pushCount],
            ['Afgeleverd', s.deliveredCount],
            ['Mislukt', s.failedCount],
            ['Gelezen', s.readCount],
          ] as const
        ).map(([label, value]) => (
          <div key={label} className="kpi">
            <span className="kpi-label">{label}</span>
            <span className="kpi-value">{value}</span>
          </div>
        ))}
      </section>
      <section className="card" aria-labelledby="inhoud">
        <h2 id="inhoud">Inhoud</h2>
        <p>{n.body}</p>
        {n.deepLink ? <p className="muted">Opent in de app: {n.deepLink}</p> : null}
      </section>
      <section className="card" aria-labelledby="doelgroep">
        <h2 id="doelgroep">Doelgroep</h2>
        <ul className="list">
          {n.audienceLabels.map((label) => (
            <li key={label}>{label}</li>
          ))}
        </ul>
        {n.optedOut || n.noDevice ? (
          <p className="muted">
            Alleen in de inbox (geen push): {n.optedOut} met deze categorie uitgezet, {n.noDevice} zonder apparaat met push.
          </p>
        ) : null}
      </section>
      <ConfirmDialog
        open={confirmCancel}
        title="Geplande melding annuleren?"
        message="De melding gaat dan niet uit."
        confirmLabel="Ja, niet versturen"
        busy={cancel.isPending}
        onCancel={() => setConfirmCancel(false)}
        onConfirm={() =>
          cancel.mutate(undefined, {
            onSettled: () => setConfirmCancel(false),
            onSuccess: () => setMessage('De melding is geannuleerd.'),
          })
        }
      />
    </>
  );
}
