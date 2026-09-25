/** RFC 9457 ProblemDetails zoals de API ze teruggeeft (docs/05 §1). */
export interface Problem {
  status?: number;
  title?: string;
  detail?: string;
  code?: string;
  traceId?: string;
  errors?: Record<string, string[]>;
}

export class ApiError extends Error {
  constructor(public readonly problem: Problem) {
    super(problem.detail ?? problem.title ?? 'Er ging iets mis');
    this.name = 'ApiError';
  }
}

/** Nederlandse tekst voor de gebruiker; valt terug op standaardteksten per status. */
export function describeProblem(error: unknown): string {
  if (error instanceof ApiError) {
    const { problem } = error;
    if (problem.errors) {
      return Object.values(problem.errors).flat().join(' ');
    }
    if (problem.detail) {
      return problem.detail;
    }
    switch (problem.status) {
      case 401:
        return 'Je bent niet (meer) aangemeld.';
      case 403:
        return 'Je hebt geen rechten voor deze actie.';
      case 404:
        return 'Niet gevonden.';
      default:
        return 'Er ging iets mis. Probeer het opnieuw.';
    }
  }
  return error instanceof Error ? error.message : 'Er ging iets mis. Probeer het opnieuw.';
}
