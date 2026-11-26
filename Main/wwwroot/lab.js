(() => {
    const pending = document.querySelector('#pending');
    const result = document.querySelector('#result');
    const live = document.querySelector('#live-workloads');
    const note = document.querySelector('#poll-note');
    const number = new Intl.NumberFormat();
    let timer;
    let currentPoll;

    function setPending(visible) {
        if (pending) pending.hidden = !visible;
        if (visible) result?.setAttribute('aria-busy', 'true');
        else result?.removeAttribute('aria-busy');
    }

    document.addEventListener('click', event => {
        const link = event.target.closest?.('a[href]');
        if (!link || event.defaultPrevented || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey
            || link.hasAttribute('download') || (link.target && link.target !== '_self')) return;
        const url = new URL(link.href);
        if (url.origin === location.origin && !(url.hash && url.pathname === location.pathname && url.search === location.search)) {
            setPending(true);
        }
    });

    document.addEventListener('submit', event => {
        if (!event.defaultPrevented && event.target.matches('form[method="post"]')) setPending(true);
    });

    async function poll() {
        if (!live?.dataset.statusUrl || !note || currentPoll) return;
        const controller = new AbortController();
        currentPoll = controller;
        const timeout = setTimeout(() => controller.abort(), 5000);
        let interval = 3000;
        try {
            const response = await fetch(live.dataset.statusUrl, { cache: 'no-store', signal: controller.signal });
            if (!response.ok) throw new Error('Status unavailable');
            const status = await response.json();
            if (currentPoll !== controller) return;
            const fields = {
                pid: status.pid,
                cpuTarget: `${status.targetCpuPercent}% across ${status.cpuWorkers} workers${status.cpuStopping ? ' — stopping' : ''}`,
                cpuSample: status.measuredProcessCpuPercent == null ? 'Not yet available' : `${status.measuredProcessCpuPercent.toFixed(2)}%`,
                cpuTime: `${Math.ceil(status.cpuSecondsRemaining)} s`,
                managed: number.format(status.managedBytes),
                native: number.format(status.nativeBytes),
                memoryTotal: `${number.format(status.retainedBytes)} / ${number.format(status.memoryCapBytes)}`,
                memoryTime: `${Math.ceil(status.memorySecondsRemaining)} s`,
                memoryProgress: status.memoryAllocating
                    ? (status.stopping ? 'Cancelling allocation…' : 'Allocating and touching 500,000,000 bytes…')
                    : status.memoryNotice || 'Idle'
            };
            for (const [field, value] of Object.entries(fields)) {
                const element = live.querySelector(`[data-field="${field}"]`);
                if (element) element.textContent = value;
            }
            note.textContent = '';
            if (status.cpuWorkers || status.memoryAllocating || status.retainedBytes || status.stopping) interval = 1000;
        } catch {
            if (currentPoll !== controller) return;
            note.textContent = 'Status refresh unavailable; showing the last sample.';
            interval = 5000;
        } finally {
            clearTimeout(timeout);
            if (currentPoll === controller) {
                currentPoll = undefined;
                timer = setTimeout(poll, interval);
            }
        }
    }

    window.addEventListener('pageshow', () => {
        setPending(false);
        clearTimeout(timer);
        void poll();
    });
    window.addEventListener('pagehide', () => {
        clearTimeout(timer);
        const controller = currentPoll;
        currentPoll = undefined;
        controller?.abort();
    });
})();
