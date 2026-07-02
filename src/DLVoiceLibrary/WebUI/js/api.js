// APIクライアント。401はログイン画面へ誘導する
const API = {
    async request(path, options = {}) {
        const res = await fetch(path, {
            headers: { 'Content-Type': 'application/json' },
            credentials: 'same-origin',
            ...options,
        });
        if (res.status === 401) {
            App.showLogin();
            throw new Error('unauthorized');
        }
        if (!res.ok) {
            let msg = `HTTP ${res.status}`;
            try { msg = (await res.json()).error || msg; } catch { }
            throw new Error(msg);
        }
        return res.json();
    },

    authStatus: () => API.request('/api/auth/status'),
    login: (username, password) => API.request('/api/auth/login', { method: 'POST', body: JSON.stringify({ username, password }) }),
    logout: () => API.request('/api/auth/logout', { method: 'POST' }),

    works: (q, sort) => API.request(`/api/works?q=${encodeURIComponent(q || '')}&sort=${encodeURIComponent(sort || '')}`),
    work: (id) => API.request(`/api/works/${id}`),
    thumbUrl: (id) => `/api/works/${id}/thumbnail`,
    streamUrl: (id) => `/api/tracks/${id}/stream`,

    toggleFavorite: (id) => API.request(`/api/tracks/${id}/favorite`, { method: 'POST' }),
    trackPlayed: (id) => API.request(`/api/tracks/${id}/played`, { method: 'POST' }),
    favorites: () => API.request('/api/favorites'),
    recent: () => API.request('/api/recent'),

    playlists: () => API.request('/api/playlists'),
    playlistTracks: (id) => API.request(`/api/playlists/${id}/tracks`),
};

function fmtTime(sec) {
    if (!isFinite(sec) || sec < 0) sec = 0;
    const m = Math.floor(sec / 60);
    const s = Math.floor(sec % 60);
    const h = Math.floor(m / 60);
    return h > 0 ? `${h}:${String(m % 60).padStart(2, '0')}:${String(s).padStart(2, '0')}`
                 : `${m}:${String(s).padStart(2, '0')}`;
}

function esc(s) {
    return String(s ?? '').replace(/[&<>"']/g, c =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}
