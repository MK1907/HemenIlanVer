import { useState } from 'react';
import type { FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';

export function SearchPage() {
  const nav = useNavigate();
  const [prompt, setPrompt] = useState('');
  const [loading, setLoading] = useState(false);

  function onSubmit(e: FormEvent) {
    e.preventDefault();
    if (!prompt.trim()) return;
    setLoading(true);
    const qs = new URLSearchParams();
    qs.set('q', prompt.trim());
    qs.set('searchMode', 'hybrid');
    nav({ pathname: '/listings', search: qs.toString() });
    setLoading(false);
  }

  return (
    <div className="card">
      <h2>Akıllı Arama (RAG)</h2>
      <p className="muted">
        Ne aradığınızı doğal dilde yazın. Yapay zeka anlam bazlı eşleştirme yaparak en uygun ilanları bulur.
      </p>
      <form className="form" onSubmit={onSubmit}>
        <label>
          Arama cümleniz
          <textarea
            value={prompt}
            onChange={(e) => setPrompt(e.target.value)}
            rows={3}
            placeholder="Örn: İstanbul'da 1 milyon altı otomatik Egea arıyorum, kilometresi düşük olsun"
          />
        </label>
        <button className="btn primary" type="submit" disabled={loading || !prompt.trim()}>
          {loading ? 'Aranıyor…' : 'Ara'}
        </button>
      </form>
    </div>
  );
}
