// SPA本体: ビュー切替・ライブラリ/プレイリスト/お気に入り/最近再生の描画
const App = {
    view: 'library',
    currentWork: null,   // 作品詳細を開いている間のキャッシュ

    async init() {
        Player.init();

        // ナビ (サイドバー + モバイルタブ)
        document.querySelectorAll('.nav-item, .tab-item').forEach(el => {
            el.onclick = () => this.switchView(el.dataset.view);
        });
        document.getElementById('btnLogout').onclick = async () => {
            await API.logout().catch(() => { });
            this.showLogin();
        };

        // 検索/ソート
        let searchTimer = null;
        document.getElementById('searchInput').oninput = () => {
            clearTimeout(searchTimer);
            searchTimer = setTimeout(() => this.loadLibrary(), 250);
        };
        document.getElementById('sortSelect').onchange = () => this.loadLibrary();

        // ログイン
        document.getElementById('loginForm').onsubmit = async (e) => {
            e.preventDefault();
            const errEl = document.getElementById('loginError');
            errEl.textContent = '';
            try {
                await API.login(
                    document.getElementById('loginUser').value,
                    document.getElementById('loginPass').value);
                this.showApp();
                this.switchView('library');
            } catch (err) {
                errEl.textContent = err.message === 'unauthorized' ? 'ログインに失敗しました' : err.message;
            }
        };

        // 認証状態で分岐
        try {
            const st = await API.authStatus();
            if (st.authRequired && !st.authenticated) {
                this.showLogin();
            } else {
                this.showApp();
                this.switchView('library');
            }
        } catch {
            this.showLogin();
        }
    },

    showLogin() {
        document.getElementById('loginOverlay').classList.remove('hidden');
        document.getElementById('appShell').classList.add('hidden');
    },

    showApp() {
        document.getElementById('loginOverlay').classList.add('hidden');
        document.getElementById('appShell').classList.remove('hidden');
    },

    switchView(view) {
        this.view = view;
        this.currentWork = null;
        document.querySelectorAll('.nav-item, .tab-item').forEach(el =>
            el.classList.toggle('active', el.dataset.view === view));
        const titles = { library: 'ライブラリ', playlists: 'プレイリスト', favorites: 'お気に入り', recent: '最近再生' };
        document.getElementById('viewTitle').textContent = titles[view] || '';
        document.getElementById('libraryControls').style.display = view === 'library' ? '' : 'none';

        if (view === 'library') this.loadLibrary();
        else if (view === 'playlists') this.loadPlaylists();
        else if (view === 'favorites') this.loadTrackListView(API.favorites, '⭐', 'お気に入りはまだありません');
        else if (view === 'recent') this.loadTrackListView(API.recent, '🕒', '再生履歴はまだありません');
    },

    content() { return document.getElementById('content'); },

    // ---------------- ライブラリ ----------------

    async loadLibrary() {
        const box = this.content();
        box.innerHTML = '<div class="loading">読み込み中</div>';
        try {
            const q = document.getElementById('searchInput').value;
            const sort = document.getElementById('sortSelect').value;
            const data = await API.works(q, sort);
            if (data.results.length === 0) {
                box.innerHTML = '<div class="empty-state"><div class="big">📭</div>作品が見つかりません</div>';
                return;
            }
            box.innerHTML = `<div class="work-grid">${data.results.map(w => this.workCard(w)).join('')}</div>`;
            box.querySelectorAll('.work-card').forEach(card => {
                card.onclick = () => this.openWork(parseInt(card.dataset.id));
            });
        } catch (err) {
            if (err.message !== 'unauthorized')
                box.innerHTML = `<div class="empty-state">エラー: ${esc(err.message)}</div>`;
        }
    },

    workCard(w) {
        const thumb = w.hasThumbnail
            ? `<img class="work-thumb" src="${API.thumbUrl(w.id)}" loading="lazy" alt="">`
            : `<div class="work-thumb-ph">🎧</div>`;
        return `
            <div class="work-card" data-id="${w.id}">
                <div class="work-thumb-wrap">${thumb}<div class="work-play-badge">▶</div></div>
                <div class="work-meta">
                    <div class="work-title">${esc(w.title)}</div>
                    <div class="work-circle">${esc(w.circleName)}</div>
                    <div class="work-info-line">${w.trackCount}曲${w.releaseDate ? ' · ' + w.releaseDate : ''}</div>
                </div>
            </div>`;
    },

    // ---------------- 作品詳細 ----------------

    async openWork(id) {
        const box = this.content();
        box.innerHTML = '<div class="loading">読み込み中</div>';
        try {
            const data = await API.work(id);
            this.currentWork = data;
            const w = data.work;

            const thumb = w.hasThumbnail
                ? `<img class="detail-thumb" src="${API.thumbUrl(w.id)}" alt="">`
                : `<div class="detail-thumb work-thumb-ph" style="width:260px">🎧</div>`;

            const actors = w.voiceActors.length
                ? `<div class="detail-row">CV: ${w.voiceActors.map(esc).join(' / ')}</div>` : '';
            const tags = w.genreTags.length
                ? `<div class="detail-row">${w.genreTags.map(t => `<span class="tag-chip">${esc(t)}</span>`).join('')}</div>` : '';

            box.innerHTML = `
                <a class="back-link">← ライブラリに戻る</a>
                <div class="detail-head">
                    ${thumb}
                    <div class="detail-info">
                        <div class="detail-title">${esc(w.title)}</div>
                        <div class="detail-circle">${esc(w.circleName)}</div>
                        ${actors}
                        <div class="detail-row">${w.trackCount}曲${w.releaseDate ? ' · 販売日 ' + w.releaseDate : ''}${w.productId ? ' · ' + esc(w.productId) : ''}</div>
                        ${tags}
                        <div class="detail-actions">
                            <button class="btn-primary" id="btnPlayAll">▶ 全曲再生</button>
                            <button class="btn-ghost" id="btnShufflePlay">🔀 シャッフル再生</button>
                        </div>
                    </div>
                </div>
                <div class="track-tree" id="trackTree"></div>`;

            box.querySelector('.back-link').onclick = () => this.switchView('library');
            document.getElementById('btnPlayAll').onclick = () => this.playWork(data, 0, false);
            document.getElementById('btnShufflePlay').onclick = () => this.playWork(data, 0, true);
            this.renderTrackTree(data);
        } catch (err) {
            if (err.message !== 'unauthorized')
                box.innerHTML = `<div class="empty-state">エラー: ${esc(err.message)}</div>`;
        }
    },

    playWork(data, startIndex, shuffle) {
        Player.shuffle = shuffle;
        document.getElementById('npShuffle').classList.toggle('on', shuffle);
        Player.playQueue(data.tracks.map(t => ({
            id: t.id,
            title: t.title,
            sub: data.work.title,
            workId: data.work.id,
            isFavorite: t.isFavorite,
        })), startIndex);
    },

    /** relPath からフォルダツリーを構築して描画 (実フォルダ構造をそのまま反映) */
    renderTrackTree(data) {
        const root = { folders: new Map(), tracks: [] };
        data.tracks.forEach((t, i) => {
            const parts = t.relPath.split('/');
            let node = root;
            for (let d = 0; d < parts.length - 1; d++) {
                if (!node.folders.has(parts[d])) node.folders.set(parts[d], { folders: new Map(), tracks: [] });
                node = node.folders.get(parts[d]);
            }
            node.tracks.push({ ...t, queueIndex: i, fileName: parts[parts.length - 1] });
        });

        const box = document.getElementById('trackTree');
        box.innerHTML = this.renderTreeNode(root);

        box.querySelectorAll('.tree-folder').forEach(el => {
            el.onclick = () => {
                el.classList.toggle('open');
                el.nextElementSibling?.classList.toggle('collapsed');
            };
        });
        box.querySelectorAll('.track-row').forEach(row => {
            row.onclick = (e) => {
                if (e.target.closest('.track-fav')) return;
                this.playWork(this.currentWork, parseInt(row.dataset.qi), Player.shuffle);
            };
        });
        box.querySelectorAll('.track-fav').forEach(btn => {
            btn.onclick = async (e) => {
                e.stopPropagation();
                const id = parseInt(btn.dataset.tid);
                try {
                    const r = await API.toggleFavorite(id);
                    btn.textContent = r.favorite ? '★' : '☆';
                    btn.classList.toggle('on', r.favorite);
                    const t = this.currentWork?.tracks.find(x => x.id === id);
                    if (t) t.isFavorite = r.favorite;
                } catch { }
            };
        });
        this.highlightPlaying();
    },

    renderTreeNode(node) {
        let html = '';
        for (const [name, child] of node.folders) {
            html += `
                <div class="tree-folder open"><span class="chev">▶</span>📁 ${esc(name)}</div>
                <div class="tree-children">${this.renderTreeNode(child)}</div>`;
        }
        html += node.tracks.map(t => `
            <div class="track-row" data-qi="${t.queueIndex}" data-tid="${t.id}">
                <span class="track-icon">🎵</span>
                <span class="track-name">${esc(t.fileName)}</span>
                <span class="track-dur">${fmtTime(t.durationMs / 1000)}</span>
                <span class="track-fav ${t.isFavorite ? 'on' : ''}" data-tid="${t.id}">${t.isFavorite ? '★' : '☆'}</span>
            </div>`).join('');
        return html;
    },

    // ---------------- プレイリスト ----------------

    async loadPlaylists() {
        const box = this.content();
        box.innerHTML = '<div class="loading">読み込み中</div>';
        try {
            const data = await API.playlists();
            if (data.results.length === 0) {
                box.innerHTML = '<div class="empty-state"><div class="big">🎵</div>プレイリストはまだありません<br>アプリ側で作成できます</div>';
                return;
            }
            box.innerHTML = `<div class="pl-list">${data.results.map(p => `
                <div class="pl-card" data-id="${p.id}">
                    <div class="pl-icon">🎵</div>
                    <div>
                        <div class="pl-name">${esc(p.name)}</div>
                        <div class="pl-meta">${p.trackCount}曲 · ${fmtTime(p.totalDurationMs / 1000)}</div>
                    </div>
                </div>`).join('')}</div>`;
            box.querySelectorAll('.pl-card').forEach(card => {
                const p = data.results.find(x => x.id === parseInt(card.dataset.id));
                card.onclick = () => this.openPlaylist(p);
            });
        } catch (err) {
            if (err.message !== 'unauthorized')
                box.innerHTML = `<div class="empty-state">エラー: ${esc(err.message)}</div>`;
        }
    },

    async openPlaylist(playlist) {
        const box = this.content();
        box.innerHTML = '<div class="loading">読み込み中</div>';
        try {
            const data = await API.playlistTracks(playlist.id);
            const tracks = data.results;
            box.innerHTML = `
                <a class="back-link">← プレイリスト一覧に戻る</a>
                <div class="detail-head">
                    <div class="pl-icon" style="width:72px;height:72px;font-size:32px;border-radius:18px">🎵</div>
                    <div class="detail-info">
                        <div class="detail-title">${esc(playlist.name)}</div>
                        <div class="detail-row">${tracks.length}曲 · ${fmtTime(playlist.totalDurationMs / 1000)}</div>
                        <div class="detail-actions">
                            <button class="btn-primary" id="btnPlayAll">▶ 再生</button>
                            <button class="btn-ghost" id="btnShufflePlay">🔀 シャッフル</button>
                        </div>
                    </div>
                </div>
                <div class="track-list" id="plTracks"></div>`;
            box.querySelector('.back-link').onclick = () => this.switchView('playlists');

            const toQueue = () => tracks.map(t => ({
                id: t.id, title: t.title, sub: t.workTitle, workId: t.workId, isFavorite: t.isFavorite,
            }));
            document.getElementById('btnPlayAll').onclick = () => {
                Player.shuffle = false;
                document.getElementById('npShuffle').classList.remove('on');
                Player.playQueue(toQueue(), 0);
            };
            document.getElementById('btnShufflePlay').onclick = () => {
                Player.shuffle = true;
                document.getElementById('npShuffle').classList.add('on');
                Player.playQueue(toQueue(), 0);
            };
            this.renderTrackList(document.getElementById('plTracks'), tracks);
        } catch (err) {
            if (err.message !== 'unauthorized')
                box.innerHTML = `<div class="empty-state">エラー: ${esc(err.message)}</div>`;
        }
    },

    // ---------------- お気に入り / 最近再生 ----------------

    async loadTrackListView(fetcher, icon, emptyMsg) {
        const box = this.content();
        box.innerHTML = '<div class="loading">読み込み中</div>';
        try {
            const data = await fetcher();
            if (data.results.length === 0) {
                box.innerHTML = `<div class="empty-state"><div class="big">${icon}</div>${emptyMsg}</div>`;
                return;
            }
            box.innerHTML = '<div class="track-list" id="tlBox"></div>';
            this.renderTrackList(document.getElementById('tlBox'), data.results);
        } catch (err) {
            if (err.message !== 'unauthorized')
                box.innerHTML = `<div class="empty-state">エラー: ${esc(err.message)}</div>`;
        }
    },

    /** 作品横断のトラック一覧 (行クリックでその一覧をキューとして再生) */
    renderTrackList(box, tracks) {
        box.innerHTML = tracks.map((t, i) => `
            <div class="track-row" data-i="${i}" data-tid="${t.id}">
                <span class="track-icon">🎵</span>
                <span style="flex:1;min-width:0">
                    <span class="track-name" style="display:block">${esc(t.title)}</span>
                    <span class="track-work">${esc(t.workTitle || '')}</span>
                </span>
                <span class="track-dur">${fmtTime(t.durationMs / 1000)}</span>
                <span class="track-fav ${t.isFavorite ? 'on' : ''}" data-tid="${t.id}">${t.isFavorite ? '★' : '☆'}</span>
            </div>`).join('');

        box.querySelectorAll('.track-row').forEach(row => {
            row.onclick = (e) => {
                if (e.target.closest('.track-fav')) return;
                Player.playQueue(tracks.map(t => ({
                    id: t.id, title: t.title, sub: t.workTitle, workId: t.workId, isFavorite: t.isFavorite,
                })), parseInt(row.dataset.i));
            };
        });
        box.querySelectorAll('.track-fav').forEach(btn => {
            btn.onclick = async (e) => {
                e.stopPropagation();
                const id = parseInt(btn.dataset.tid);
                try {
                    const r = await API.toggleFavorite(id);
                    btn.textContent = r.favorite ? '★' : '☆';
                    btn.classList.toggle('on', r.favorite);
                } catch { }
            };
        });
        this.highlightPlaying();
    },

    // ---------------- プレーヤー連携 ----------------

    onTrackChanged(track) {
        this.highlightPlaying();
    },

    onFavoriteChanged(trackId, fav) {
        document.querySelectorAll(`.track-fav[data-tid="${trackId}"]`).forEach(btn => {
            btn.textContent = fav ? '★' : '☆';
            btn.classList.toggle('on', fav);
        });
    },

    highlightPlaying() {
        const cur = Player.current();
        document.querySelectorAll('.content .track-row').forEach(row => {
            const isPlaying = cur && parseInt(row.dataset.tid) === cur.id;
            row.classList.toggle('playing', !!isPlaying);
            const icon = row.querySelector('.track-icon');
            if (!icon) return;
            if (isPlaying) {
                icon.outerHTML = '<span class="eq track-icon"><span></span><span></span><span></span></span>';
            } else if (row.querySelector('.eq')) {
                row.querySelector('.eq').outerHTML = '<span class="track-icon">🎵</span>';
            }
        });
    },
};

document.addEventListener('DOMContentLoaded', () => App.init());
