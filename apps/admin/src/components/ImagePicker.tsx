import { useId, useState } from 'react';
import type { Schemas } from '../api/hooks';
import { uploadJson } from '../api/upload';
import { useAuth } from '../auth/AuthContext';
import { ProblemAlert } from './ProblemAlert';

type UploadedImage = Schemas['UploadedImageResponse'];

/**
 * Afbeelding kiezen, ook vóór het eerste opslaan (fase 21a): het bestand wordt meteen geüpload (virusscan, herschalen,
 * zonder metadata) en het pad gaat bij het opslaan mee. `onChange('')` betekent: afbeelding weghalen.
 */
export function ImagePicker({
  label,
  uploadPath,
  currentUrl,
  onChange,
  round = false,
}: {
  label: string;
  uploadPath: '/api/v1/admin/website/images' | '/api/v1/admin/news/images';
  /** De opgeslagen afbeelding (voorbeeld zolang er niets nieuws is gekozen). */
  currentUrl: string | null | undefined;
  onChange: (path: string) => void;
  round?: boolean;
}) {
  const auth = useAuth();
  const id = useId();
  const [preview, setPreview] = useState<string | null | undefined>(undefined);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<unknown>(null);
  const shown = preview === undefined ? currentUrl : preview;

  async function choose(file: File) {
    setError(null);
    setBusy(true);
    const data = new FormData();
    data.append('file', file);
    try {
      const uploaded = await uploadJson<UploadedImage>(auth, uploadPath, data);
      setPreview(uploaded.url);
      onChange(uploaded.path);
    } catch (e) {
      setError(e);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="image-picker">
      {shown ? (
        <img src={shown} alt="" className={round ? 'preview preview-round' : 'preview'} />
      ) : (
        <p className="muted">Nog geen afbeelding.</p>
      )}
      <div className="field">
        <label htmlFor={id}>{label} (JPEG, PNG of WebP, max. 10 MB)</label>
        <input
          id={id}
          type="file"
          accept="image/jpeg,image/png,image/webp"
          disabled={busy}
          onChange={(e) => {
            const file = e.target.files?.[0];
            if (file) void choose(file);
            e.target.value = '';
          }}
        />
        {busy ? (
          <small className="muted" role="status">
            Afbeelding wordt geüpload…
          </small>
        ) : null}
      </div>
      {shown ? (
        <button
          type="button"
          className="button secondary"
          onClick={() => {
            setPreview(null);
            onChange('');
          }}
        >
          Afbeelding weghalen
        </button>
      ) : null}
      <ProblemAlert error={error} />
    </div>
  );
}
