import { createContext, useContext, type ReactNode } from 'react';
import type { AuthService } from './auth';

const AuthContext = createContext<AuthService | null>(null);

export function AuthProvider({ auth, children }: { auth: AuthService; children: ReactNode }) {
  return <AuthContext.Provider value={auth}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthService {
  const auth = useContext(AuthContext);
  if (!auth) {
    throw new Error('useAuth buiten AuthProvider');
  }
  return auth;
}
