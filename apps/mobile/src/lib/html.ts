/**
 * Zet de gesanitizede HTML van de API (Markdown → Markdig → HtmlSanitizer, fase 5) om in blokken met opgemaakte
 * tekstdelen. Alleen de tags die Markdown oplevert worden herkend; onbekende tags worden genegeerd, hun tekst blijft.
 * Er wordt nooit HTML uitgevoerd of in een WebView geladen.
 */
export interface Span {
  text: string;
  bold?: boolean;
  italic?: boolean;
  href?: string;
}

export interface Block {
  type: 'paragraph' | 'heading' | 'listItem' | 'quote';
  /** Opsommingsteken: "•" of "1.". */
  marker?: string;
  spans: Span[];
}

const entities: Record<string, string> = { amp: '&', lt: '<', gt: '>', quot: '"', apos: "'", nbsp: ' ' };

export function decodeEntities(text: string): string {
  return text.replace(/&(#x[0-9a-f]+|#\d+|[a-z]+);/gi, (match, code: string) => {
    if (code[0] === '#') {
      const n = code[1]?.toLowerCase() === 'x' ? Number.parseInt(code.slice(2), 16) : Number.parseInt(code.slice(1), 10);
      return Number.isFinite(n) ? String.fromCodePoint(n) : match;
    }
    return entities[code.toLowerCase()] ?? match;
  });
}

/** Alleen http(s)- en mailto-links zijn klikbaar. */
const safeHref = (href: string | undefined) => (href && /^(https?:|mailto:)/i.test(href) ? href : undefined);

export function parseHtml(html: string): Block[] {
  const blocks: Block[] = [];
  const lists: { ordered: boolean; count: number }[] = [];
  let current: Block | null = null;
  let bold = 0;
  let italic = 0;
  let quote = 0;
  const links: (string | undefined)[] = [];

  const open = (type: Block['type'], marker?: string) => {
    current = { type, marker, spans: [] };
    blocks.push(current);
  };
  const close = () => {
    current = null;
  };

  for (const token of html.match(/<[^>]+>|[^<]+/g) ?? []) {
    if (!token.startsWith('<')) {
      const text = decodeEntities(token);
      if (!current) {
        if (!text.trim()) {
          continue;
        }
        open(quote > 0 ? 'quote' : 'paragraph');
      }
      const block = current as Block | null;
      block?.spans.push({ text, bold: bold > 0 || undefined, italic: italic > 0 || undefined, href: links.at(-1) });
      continue;
    }

    const match = /^<\s*(\/?)\s*([a-z0-9]+)([^>]*)>$/i.exec(token);
    if (!match) {
      continue;
    }
    const [, slash, name = '', attributes = ''] = match;
    const closing = slash === '/';
    const tag = name.toLowerCase();
    switch (tag) {
      case 'p':
      case 'div':
        if (closing) close();
        else open(quote > 0 ? 'quote' : 'paragraph');
        break;
      case 'h1':
      case 'h2':
      case 'h3':
      case 'h4':
      case 'h5':
      case 'h6':
        if (closing) close();
        else open('heading');
        break;
      case 'ul':
      case 'ol':
        if (closing) lists.pop();
        else lists.push({ ordered: tag === 'ol', count: 0 });
        close();
        break;
      case 'li': {
        if (closing) {
          close();
          break;
        }
        const list = lists.at(-1);
        if (list) list.count++;
        open('listItem', list?.ordered ? `${list.count}.` : '•');
        break;
      }
      case 'blockquote':
        quote += closing ? -1 : 1;
        close();
        break;
      case 'br':
        (current as Block | null)?.spans.push({ text: '\n' });
        break;
      case 'strong':
      case 'b':
        bold += closing ? -1 : 1;
        break;
      case 'em':
      case 'i':
        italic += closing ? -1 : 1;
        break;
      case 'a':
        if (closing) links.pop();
        else links.push(safeHref(/href\s*=\s*"([^"]*)"/i.exec(attributes)?.[1]));
        break;
      default:
        break;
    }
  }

  // Regeleinden uit de HTML-bron worden spaties; witruimte aan begin en eind van een blok vervalt; lege blokken ook.
  return blocks
    .map((block) => {
      const spans = block.spans.map((s) => (s.text === '\n' ? s : { ...s, text: s.text.replace(/\s*\n\s*/g, ' ') }));
      const first = spans[0];
      if (first) spans[0] = { ...first, text: first.text.trimStart() };
      const last = spans[spans.length - 1];
      if (last) spans[spans.length - 1] = { ...last, text: last.text.trimEnd() };
      return { ...block, spans: spans.filter((s) => s.text.length > 0) };
    })
    .filter((block) => block.spans.length > 0);
}
