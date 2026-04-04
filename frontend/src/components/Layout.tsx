import { Link, NavLink, Outlet } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import { IconFacebook, IconHeart, IconInstagram, IconLinkedIn, IconMegaphone, IconMessage, IconUser, IconX } from './icons';

export function Layout() {
  const { user, logout } = useAuth();

  return (
    <div className="site">
      <header className="site-header">
        <div className="site-header__inner">
          <Link to="/" className="site-logo">
            <IconMegaphone className="site-logo__icon" />
            <span className="site-logo__text">Hemen İlan Ver</span>
          </Link>

          <nav className="site-nav">
            <NavLink to="/" className={({ isActive }) => 'site-nav__link' + (isActive ? ' is-active' : '')} end>
              Ana Sayfa
            </NavLink>
            <NavLink to="/search" className={({ isActive }) => 'site-nav__link' + (isActive ? ' is-active' : '')}>
              Arama
            </NavLink>
            <NavLink to="/create" className={({ isActive }) => 'site-nav__link' + (isActive ? ' is-active' : '')}>
              Yeni İlan
            </NavLink>
            <NavLink to="/admin" className={({ isActive }) => 'site-nav__link' + (isActive ? ' is-active' : '')}>
              Yönetim
            </NavLink>
            {user ? (
              <button type="button" className="site-nav__link site-nav__link--btn" onClick={logout}>
                Çıkış
              </button>
            ) : (
              <Link to="/login" className="site-nav__link">
                Giriş
              </Link>
            )}
          </nav>

          <div className="site-header__tools">
            <span className="site-lang">tr TR</span>
            <button type="button" className="site-icon-btn" title="Profil" aria-label="Profil">
              <IconUser />
            </button>
            <Link to="/listings" className="site-icon-btn" title="Mesajlar" aria-label="Mesajlar">
              <IconMessage />
            </Link>
            <button type="button" className="site-icon-btn" title="Favoriler" aria-label="Favoriler">
              <IconHeart />
            </button>
            <Link to="/create" className="site-btn-ilan">
              İlan Ver
            </Link>
          </div>
        </div>
      </header>

      <main className="site-main">
        <div className="site-main__pad">
          <Outlet />
        </div>
      </main>

      <footer className="site-footer">
        <div className="site-footer__inner">
          <div className="site-footer__col">
            <h4>Hakkında</h4>
            <p className="site-footer__muted">
              Hemen İlan Ver, yapay zeka destekli arama ve güvenli mesajlaşma ile ilan deneyimini sadeleştirir.
            </p>
          </div>
          <div className="site-footer__col">
            <h4>Popüler Kategoriler</h4>
            <ul className="site-footer__list">
              <li>
                <Link to="/listings">Araç</Link>
              </li>
              <li>
                <Link to="/listings">Emlak</Link>
              </li>
              <li>
                <Link to="/listings">Evcil Hayvan</Link>
              </li>
            </ul>
          </div>
          <div className="site-footer__col">
            <h4>Platform</h4>
            <ul className="site-footer__list">
              <li>
                <Link to="/listings">İlanları Keşfet</Link>
              </li>
              <li>
                <Link to="/search">Detaylı Arama</Link>
              </li>
              <li>
                <Link to="/create">İlan Ver</Link>
              </li>
            </ul>
          </div>
          <div className="site-footer__col">
            <h4>İletişim</h4>
            <p>
              <a href="mailto:destek@hemenilanver.local">Bize Yazın</a>
            </p>
          </div>
          <div className="site-footer__col site-footer__col--social">
            <h4>Bizi Takip Et</h4>
            <div className="site-footer__social">
              <a href="#" aria-label="Facebook" className="site-footer__social-link">
                <IconFacebook />
              </a>
              <a href="#" aria-label="LinkedIn" className="site-footer__social-link">
                <IconLinkedIn />
              </a>
              <a href="#" aria-label="X" className="site-footer__social-link">
                <IconX />
              </a>
              <a href="#" aria-label="Instagram" className="site-footer__social-link">
                <IconInstagram />
              </a>
            </div>
            <div className="site-footer__badges">
              <span className="site-footer__badge">Google Play</span>
              <span className="site-footer__badge">App Store</span>
            </div>
          </div>
        </div>
        <div className="site-footer__bottom">© {new Date().getFullYear()} Hemen İlan Ver</div>
      </footer>
    </div>
  );
}
