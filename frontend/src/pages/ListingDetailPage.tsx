import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { useAuth } from '../context/AuthContext';

type Detail = {
  id: string;
  title: string;
  description: string;
  price: number | null;
  currency: string;
  listingType: string;
  categoryName: string;
  cityName?: string | null;
  districtName?: string | null;
  attributes: Record<string, string>;
  imageUrls: string[];
  viewCount: number;
};

export function ListingDetailPage() {
  const { id } = useParams();
  const { user } = useAuth();
  const [detail, setDetail] = useState<Detail | null>(null);
  const [msg, setMsg] = useState('');
  const [sent, setSent] = useState(false);

  useEffect(() => {
    if (!id) return;
    api.get<Detail>(`/api/listings/${id}`).then((r) => setDetail(r.data));
  }, [id]);

  async function sendMessage(e: FormEvent) {
    e.preventDefault();
    if (!id || !msg.trim()) return;
    await api.post('/api/messages', { listingId: id, body: msg });
    setMsg('');
    setSent(true);
  }

  if (!detail) return <p>Yükleniyor…</p>;

  return (
    <div className="detail">
      <nav className="breadcrumb">
        <Link to="/listings">İlanlar</Link> / <span>{detail.title}</span>
      </nav>
      <div className="detail-grid">
        <section className="gallery">
          {detail.imageUrls.length === 0 ? (
            <div className="placeholder">Görsel yok</div>
          ) : (
            detail.imageUrls.map((u) => <img key={u} src={u} alt="" />)
          )}
        </section>
        <section className="detail-main">
          <h1>{detail.title}</h1>
          <p className="price big">
            {detail.price != null ? `${detail.price.toLocaleString('tr-TR')} ${detail.currency}` : 'Fiyat sorunuz'}
          </p>
          <p className="muted">
            {detail.cityName ?? ''}
            {detail.districtName ? ` · ${detail.districtName}` : ''} · {detail.categoryName} · {detail.listingType} · Görüntülenme:{' '}
            {detail.viewCount}
          </p>
          <div className="description">{detail.description}</div>
          <table className="attr-table">
            <tbody>
              {Object.entries(detail.attributes).map(([k, v]) => (
                <tr key={k}>
                  <th>{k}</th>
                  <td>{v}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {user ? (
            <form className="form inline" onSubmit={sendMessage}>
              <label>
                Satıcıya mesaj
                <textarea value={msg} onChange={(e) => setMsg(e.target.value)} rows={3} placeholder="Merhaba, ilanınız hâlâ güncel mi?" />
              </label>
              <button className="btn primary" type="submit">
                Gönder
              </button>
              {sent && <span className="ok">Mesaj gönderildi.</span>}
            </form>
          ) : (
            <p>
              Mesaj için <Link to="/login">giriş yapın</Link>.
            </p>
          )}
        </section>
      </div>
    </div>
  );
}
