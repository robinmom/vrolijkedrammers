import '@fontsource/inter/400.css';
import '@fontsource/inter/500.css';
import '@fontsource/inter/600.css';
import '@fontsource/poppins/600.css';
import '@fontsource/poppins/700.css';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { RouterProvider } from '@tanstack/react-router';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { ApiError } from './api/errors';
import { ApiProvider } from './api/ApiContext';
import { createAdminApi } from './api/client';
import { createMockAuth, createMsalAuth, type AuthService } from './auth/auth';
import { AuthProvider } from './auth/AuthContext';
import { isE2eMock, loadPortalConfig } from './config';
import { createAppRouter } from './router';
import { SignIn } from './SignIn';
import './styles.css';
import { applyTheme } from './theme';

applyTheme();

const container = document.getElementById('root');
if (!container) {
  throw new Error('Element #root ontbreekt in index.html');
}
const root = createRoot(container);

async function start() {
  let auth: AuthService;
  try {
    auth = isE2eMock ? createMockAuth() : await createMsalAuth(await loadPortalConfig());
  } catch (error) {
    root.render(<p role="alert">Het beheerportal kan niet starten: {error instanceof Error ? error.message : 'onbekende fout'}</p>);
    return;
  }

  if (!auth.user) {
    root.render(<SignIn onSignIn={() => void auth.signIn()} />);
    return;
  }

  const queryClient = new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        // Geen herhaling bij 401/403/404: dat verandert niet door opnieuw te proberen.
        retry: (count, error) => !(error instanceof ApiError && [401, 403, 404].includes(error.problem.status ?? 0)) && count < 2,
      },
    },
  });
  const router = createAppRouter();

  root.render(
    <StrictMode>
      <AuthProvider auth={auth}>
        <ApiProvider api={createAdminApi(auth)}>
          <QueryClientProvider client={queryClient}>
            <RouterProvider router={router} />
          </QueryClientProvider>
        </ApiProvider>
      </AuthProvider>
    </StrictMode>,
  );
}

void start();
