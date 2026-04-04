import { useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';

export function LoginPage() {
  const { login } = useAuth();
  const nav = useNavigate();
  const [email, setEmail] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState<string | null>(null);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      await login(email, password);
      nav('/');
    } catch {
      setError('Giriş başarısız. Bilgilerinizi kontrol edin.');
    }
  }

  return (
    <div className="card narrow">
      <h2>Giriş</h2>
      <form onSubmit={onSubmit} className="form">
        <label>
          E-posta
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required autoComplete="email" />
        </label>
        <label>
          Şifre
          <input
            type="password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            autoComplete="current-password"
          />
        </label>
        {error && <p className="error">{error}</p>}
        <button className="btn primary" type="submit">
          Giriş yap
        </button>
      </form>
      <p className="muted">
        Demo: <code>demo@hemenilanver.local</code> / <code>Demo12345!</code>
      </p>
      <p>
        Hesabın yok mu? <Link to="/register">Kayıt ol</Link>
      </p>
    </div>
  );
}
