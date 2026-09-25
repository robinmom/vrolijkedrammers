/** Welkomstscherm vóór het aanmelden (e-mail + eenmalige code via Entra External ID). */
export function SignIn({ onSignIn }: { onSignIn: () => void }) {
  return (
    <main className="signin">
      <section className="card">
        <h1>Beheerportal De Vrolijke Drammers</h1>
        <p>Meld je aan met je e-mailadres. Je ontvangt een eenmalige code per e-mail.</p>
        <button type="button" className="button" onClick={onSignIn}>
          Aanmelden
        </button>
      </section>
    </main>
  );
}
