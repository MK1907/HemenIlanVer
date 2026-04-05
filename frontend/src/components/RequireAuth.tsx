import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { safeInternalPath } from '../utils/safeReturnUrl';

export function RequireAuth({ children }: { children: React.ReactNode }) {
  const { user } = useAuth();
  const location = useLocation();

  if (!user) {
    const back = safeInternalPath(`${location.pathname}${location.search}`);
    return <Navigate to={`/login?returnUrl=${encodeURIComponent(back)}`} replace />;
  }

  return <>{children}</>;
}
