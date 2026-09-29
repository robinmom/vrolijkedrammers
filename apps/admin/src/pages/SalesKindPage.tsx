import { useState } from 'react';
import { kindPages, type SaleProductKind } from '../api/sales';
import { SuccessMessage } from '../components/ProblemAlert';
import { OrderButtons, OrdersCard, ProductsCard, SalesKpis } from '../components/SaleSections';

/**
 * Een pagina onder Verkoop (fase 19) voor één soort product: dagkaarten, kaarten voor activiteiten of munten. Met de
 * kerncijfers, de producten (instellen, wachtlijst) en de bestellingen. De pronkzitting heeft een eigen pagina met het
 * overzicht per avond.
 */
export function SalesKindPage({ kind }: { kind: Exclude<SaleProductKind, 'Pronkzitting'> }) {
  const [message, setMessage] = useState<string | null>(null);
  const page = kindPages[kind];
  return (
    <>
      <div className="page-header">
        <div className="page-title">
          <h1>{page.title}</h1>
          <p className="page-subtitle">{page.subtitle} Niets wordt terugbetaald.</p>
        </div>
        <div className="actions">
          <OrderButtons kind={kind} onMessage={setMessage} />
        </div>
      </div>
      <SuccessMessage message={message} />
      <SalesKpis kind={kind} />
      <ProductsCard kind={kind} title={kind === 'Tokens' ? 'Munten' : 'Producten'} onMessage={setMessage} />
      <OrdersCard kind={kind} onMessage={setMessage} />
    </>
  );
}

export const DayTicketsPage = () => <SalesKindPage kind="DayTicket" />;
export const EventTicketsPage = () => <SalesKindPage kind="EventTicket" />;
export const TokensPage = () => <SalesKindPage kind="Tokens" />;
