import { useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { safeReturnUrl } from '../utils/safeReturnUrl';

export function RegisterPage() {
  const { register } = useAuth();
  const nav = useNavigate();
  const [searchParams] = useSearchParams();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [error, setError] = useState<string | null>(null);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      await register(email, password, displayName);
      nav(safeReturnUrl(searchParams.get('returnUrl')), { replace: true });
    } catch {
      setError('Kayıt başarısız. E-posta kullanımda olabilir.');
    }
  }

  return (
    <div className="card narrow">
      <h2>Kayıt ol</h2>
      <form onSubmit={onSubmit} className="form">
        <label>
          Görünen ad
          <input value={displayName} onChange={(e) => setDisplayName(e.target.value)} required maxLength={120} />
        </label>
        <label>
          E-posta
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required autoComplete="email" />
        </label>
        <label>
          Şifre (en az 8 karakter)
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            minLength={8}
            autoComplete="new-password"
          />
        </label>
        {error && <p className="error">{error}</p>}
        <button className="btn primary" type="submit">
          Kayıt ol
        </button>
      </form>
      <p>
        Zaten hesabın var mı?{' '}
        <Link
          to={
            searchParams.get('returnUrl')
              ? `/login?returnUrl=${encodeURIComponent(searchParams.get('returnUrl')!)}`
              : '/login'
          }
        >
          Giriş
        </Link>
      </p>
    </div>
  );
}
