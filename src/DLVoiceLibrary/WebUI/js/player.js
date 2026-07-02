// プレーヤー: キュー管理 + HTML5 Audio + Media Session API (ロック画面/Bluetoothコントロール)
const Player = {
    audio: null,
    queue: [],          // [{ id, title, sub, workId }]
    index: -1,
    repeat: 'off',      // off | all | one
    shuffle: false,
    _origOrder: null,   // シャッフル解除時の復元用
    _playedReported: false,

    init() {
        this.audio = document.getElementById('audio');
        this.audio.volume = 1;

        this.audio.addEventListener('timeupdate', () => this.onTime());
        this.audio.addEventListener('ended', () => this.onEnded());
        this.audio.addEventListener('play', () => this.updatePlayState());
        this.audio.addEventListener('pause', () => this.updatePlayState());
        this.audio.addEventListener('error', () => {
            const cur = this.current();
            if (cur) console.warn('audio error for track', cur.id);
        });

        // ミニプレーヤー
        document.getElementById('miniPlay').onclick = () => this.toggle();
        document.getElementById('miniPrev').onclick = () => this.prev();
        document.getElementById('miniNext').onclick = () => this.next();
        document.getElementById('miniInfo').onclick = () => this.openNowPlaying();
        document.getElementById('miniExpand').onclick = () => this.openNowPlaying();
        document.getElementById('miniThumb').onclick = () => this.openNowPlaying();
        document.querySelector('.mini-progress').onclick = (e) => {
            const rect = e.currentTarget.getBoundingClientRect();
            this.seekRatio((e.clientX - rect.left) / rect.width);
        };

        // Now Playing
        document.getElementById('npClose').onclick = () => this.closeNowPlaying();
        document.getElementById('npPlay').onclick = () => this.toggle();
        document.getElementById('npPrev').onclick = () => this.prev();
        document.getElementById('npNext').onclick = () => this.next();
        document.getElementById('npShuffle').onclick = () => this.toggleShuffle();
        document.getElementById('npRepeat').onclick = () => this.cycleRepeat();
        document.getElementById('npFav').onclick = () => this.toggleFavCurrent();
        document.getElementById('npRate').onchange = (e) => { this.audio.playbackRate = parseFloat(e.target.value); };
        document.getElementById('npVolume').oninput = (e) => { this.audio.volume = e.target.value / 100; };
        const seek = document.getElementById('npSeek');
        seek.oninput = () => {
            if (this.audio.duration) this.audio.currentTime = seek.value / 1000 * this.audio.duration;
        };

        // Media Session (ロック画面・Bluetoothボタン)
        if ('mediaSession' in navigator) {
            navigator.mediaSession.setActionHandler('play', () => this.audio.play());
            navigator.mediaSession.setActionHandler('pause', () => this.audio.pause());
            navigator.mediaSession.setActionHandler('previoustrack', () => this.prev());
            navigator.mediaSession.setActionHandler('nexttrack', () => this.next());
            navigator.mediaSession.setActionHandler('seekto', (d) => {
                if (d.seekTime != null) this.audio.currentTime = d.seekTime;
            });
        }
    },

    current() { return this.index >= 0 ? this.queue[this.index] : null; },

    /** キューを積んで指定位置から再生 */
    playQueue(tracks, startIndex = 0) {
        this.queue = tracks.slice();
        this._origOrder = null;
        if (this.shuffle) {
            this._origOrder = this.queue.slice();
            const first = this.queue.splice(startIndex, 1);
            shuffleArray(this.queue);
            this.queue = first.concat(this.queue);
            startIndex = 0;
        }
        this.playAt(startIndex);
        document.getElementById('miniPlayer').classList.remove('hidden');
    },

    playAt(i) {
        if (i < 0 || i >= this.queue.length) return;
        this.index = i;
        const t = this.queue[i];
        this._playedReported = false;
        this.audio.src = API.streamUrl(t.id);
        this.audio.playbackRate = parseFloat(document.getElementById('npRate').value);
        this.audio.play().catch(() => { });
        this.renderTrackInfo();
        this.renderQueue();
        App.onTrackChanged(t);
    },

    toggle() {
        if (!this.audio.src) return;
        this.audio.paused ? this.audio.play() : this.audio.pause();
    },

    prev() {
        if (this.audio.currentTime > 3) { this.audio.currentTime = 0; return; }
        if (this.index > 0) this.playAt(this.index - 1);
        else this.audio.currentTime = 0;
    },

    next() {
        if (this.index < this.queue.length - 1) this.playAt(this.index + 1);
        else if (this.repeat === 'all' && this.queue.length > 0) this.playAt(0);
    },

    onEnded() {
        if (this.repeat === 'one') { this.audio.currentTime = 0; this.audio.play(); return; }
        this.next();
    },

    onTime() {
        const cur = this.audio.currentTime, dur = this.audio.duration || 0;
        const ratio = dur ? cur / dur : 0;
        document.getElementById('miniProgressFill').style.width = `${ratio * 100}%`;
        document.getElementById('miniTime').textContent = `${fmtTime(cur)} / ${fmtTime(dur)}`;
        const np = document.getElementById('nowPlaying');
        if (!np.classList.contains('hidden')) {
            document.getElementById('npTimeCur').textContent = fmtTime(cur);
            document.getElementById('npTimeDur').textContent = fmtTime(dur);
            const seek = document.getElementById('npSeek');
            if (document.activeElement !== seek) seek.value = ratio * 1000;
        }
        // 30秒以上 or 半分以上再生したら履歴に記録 (1トラック1回)
        if (!this._playedReported && dur > 0 && (cur > 30 || cur / dur > 0.5)) {
            this._playedReported = true;
            const t = this.current();
            if (t) API.trackPlayed(t.id).catch(() => { });
        }
    },

    seekRatio(r) {
        if (this.audio.duration) this.audio.currentTime = Math.max(0, Math.min(1, r)) * this.audio.duration;
    },

    updatePlayState() {
        const playing = !this.audio.paused;
        document.getElementById('miniPlay').textContent = playing ? '⏸' : '▶';
        document.getElementById('npPlay').textContent = playing ? '⏸' : '▶';
        document.querySelectorAll('.eq').forEach(el => el.classList.toggle('paused', !playing));
        if ('mediaSession' in navigator) {
            navigator.mediaSession.playbackState = playing ? 'playing' : 'paused';
        }
    },

    renderTrackInfo() {
        const t = this.current();
        if (!t) return;
        document.getElementById('miniTitle').textContent = t.title;
        document.getElementById('miniSub').textContent = t.sub || '';
        document.getElementById('npTitle').textContent = t.title;
        document.getElementById('npSub').textContent = t.sub || '';

        const thumbUrl = t.workId ? API.thumbUrl(t.workId) : '';
        const mini = document.getElementById('miniThumb');
        const np = document.getElementById('npThumb');
        mini.src = thumbUrl; np.src = thumbUrl;
        document.getElementById('npBg').style.backgroundImage = thumbUrl ? `url("${thumbUrl}")` : '';

        this.updateFavButton();

        if ('mediaSession' in navigator) {
            navigator.mediaSession.metadata = new MediaMetadata({
                title: t.title,
                artist: t.sub || 'DLVoiceLibrary',
                artwork: thumbUrl ? [{ src: thumbUrl, sizes: '512x512' }] : [],
            });
        }
    },

    renderQueue() {
        const box = document.getElementById('npQueue');
        if (this.queue.length === 0) { box.innerHTML = ''; return; }
        let html = `<div class="np-queue-head">キュー (${this.index + 1}/${this.queue.length})</div>`;
        html += this.queue.map((t, i) => `
            <div class="track-row ${i === this.index ? 'playing' : ''}" data-qi="${i}">
                ${i === this.index
                    ? '<span class="eq"><span></span><span></span><span></span></span>'
                    : `<span class="track-icon">${i + 1}</span>`}
                <span class="track-name">${esc(t.title)}</span>
            </div>`).join('');
        box.innerHTML = html;
        box.querySelectorAll('.track-row').forEach(row => {
            row.onclick = () => this.playAt(parseInt(row.dataset.qi));
        });
    },

    toggleShuffle() {
        this.shuffle = !this.shuffle;
        document.getElementById('npShuffle').classList.toggle('on', this.shuffle);
        if (this.queue.length === 0) return;
        const cur = this.current();
        if (this.shuffle) {
            this._origOrder = this.queue.slice();
            const rest = this.queue.filter((_, i) => i !== this.index);
            shuffleArray(rest);
            this.queue = [cur, ...rest];
            this.index = 0;
        } else if (this._origOrder) {
            this.queue = this._origOrder;
            this._origOrder = null;
            this.index = this.queue.findIndex(t => t.id === cur.id);
        }
        this.renderQueue();
    },

    cycleRepeat() {
        this.repeat = this.repeat === 'off' ? 'all' : this.repeat === 'all' ? 'one' : 'off';
        const btn = document.getElementById('npRepeat');
        btn.classList.toggle('on', this.repeat !== 'off');
        btn.textContent = this.repeat === 'one' ? '🔂' : '🔁';
    },

    async toggleFavCurrent() {
        const t = this.current();
        if (!t) return;
        try {
            const r = await API.toggleFavorite(t.id);
            t.isFavorite = r.favorite;
            this.updateFavButton();
            App.onFavoriteChanged(t.id, r.favorite);
        } catch { }
    },

    updateFavButton() {
        const t = this.current();
        const btn = document.getElementById('npFav');
        btn.textContent = t?.isFavorite ? '★' : '☆';
        btn.classList.toggle('on', !!t?.isFavorite);
    },

    openNowPlaying() {
        if (!this.current()) return;
        document.getElementById('nowPlaying').classList.remove('hidden');
        this.renderQueue();
    },

    closeNowPlaying() {
        document.getElementById('nowPlaying').classList.add('hidden');
    },
};

function shuffleArray(a) {
    for (let i = a.length - 1; i > 0; i--) {
        const j = Math.floor(Math.random() * (i + 1));
        [a[i], a[j]] = [a[j], a[i]];
    }
}
