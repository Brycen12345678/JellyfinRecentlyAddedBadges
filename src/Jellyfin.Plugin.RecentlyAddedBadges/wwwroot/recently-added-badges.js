(function () {
    'use strict';

    const pluginId = 'a9b5c3e1-7d42-4f86-9a31-6c2e8b7d4f10';
    const cache = new Map();
    let config = null;
    let libraryPaths = [];
    let observer = null;
    let scanQueued = false;

    function currentApi() {
        return window.ApiClient || window.ServerConnections?.currentApiClient?.();
    }

    function getItem(userId, id) {
        const key = String(id).replace(/-/g, '').toLowerCase();
        if (!cache.has(key)) {
            const api = currentApi();
            cache.set(key, api.ajax({
                type: 'GET',
                url: api.getUrl('/Items/' + encodeURIComponent(id), {
                    Fields: 'DateCreated,Path,Type'
                }),
                dataType: 'json'
            }).catch(() => null));
            if (cache.size > 2500) cache.clear();
        }
        return cache.get(key);
    }

    function normalizePath(path) {
        return String(path || '').replace(/\\/g, '/').replace(/\/{2,}/g, '/').replace(/\/$/, '');
    }

    function isInEnabledLibrary(item) {
        const itemPath = normalizePath(item?.Path);
        if (!itemPath) return false;
        return libraryPaths.some(root =>
            itemPath === root || itemPath.startsWith(root + '/')
        );
    }

    function removeBadge(card) {
        card.querySelectorAll('.rab-badge, .rab-overlay-container').forEach(node => node.remove());
    }

    function positionMatches(element, position) {
        // Jellyfin Enhanced exposes the intended corner explicitly. Computed
        // styles may report both opposing inset values, so do not use them
        // to determine the corner when this attribute is available.
        const corner = element.getAttribute('data-je-corner');
        if (corner) return corner === position;

        const style = window.getComputedStyle(element);
        const top = style.top !== 'auto';
        const bottom = style.bottom !== 'auto';
        const left = style.left !== 'auto';
        const right = style.right !== 'auto';
        return (position.startsWith('top') ? top && !bottom : bottom && !top)
            && (position.endsWith('left') ? left && !right : right && !left);
    }

    function addBadgeToQualityContainer(card, qualityContainer) {
        const existing = card.querySelector('.rab-badge');
        if (existing && existing.parentElement === qualityContainer) return;
        if (existing) existing.remove();
        card.querySelectorAll('.rab-overlay-container').forEach(node => node.remove());

        const badge = document.createElement('div');
        badge.className = 'quality-overlay-label rab-badge';
        badge.textContent = 'New';
        badge.style.background = config.BadgeBackgroundColor || '#E53935';
        badge.style.color = '#fff';
        badge.style.borderRadius = '0';
        badge.style.fontWeight = '700';
        badge.style.display = 'inline-flex';
        badge.style.alignItems = 'center';
        badge.style.justifyContent = 'center';
        badge.style.padding = '5px 13px';
        badge.style.lineHeight = '1.1';
        badge.style.clipPath = 'polygon(50% 0%, 58% 9%, 69% 2%, 75% 13%, 88% 10%, 90% 23%, 100% 32%, 93% 43%, 100% 55%, 91% 64%, 89% 78%, 76% 80%, 70% 94%, 59% 90%, 50% 100%, 41% 90%, 30% 94%, 24% 80%, 11% 78%, 9% 64%, 0% 55%, 7% 43%, 0% 32%, 10% 23%, 12% 10%, 25% 13%, 31% 2%, 42% 9%)';
        badge.style.order = '-1';
        qualityContainer.prepend(badge);
    }

    function addStandaloneBadge(card, host) {
        let container = card.querySelector('.rab-overlay-container');
        if (container && container.parentElement !== host) {
            host.appendChild(container);
        }
        if (container) return;

        const position = config.BadgePosition || 'top-left';
        container = document.createElement('div');
        container.className = 'rab-overlay-container';
        Object.assign(container.style, {
            position: 'absolute',
            display: 'flex',
            flexDirection: 'column',
            gap: '4px',
            alignItems: position.endsWith('right') ? 'flex-end' : 'flex-start',
            top: position.startsWith('top') ? '0' : 'auto',
            bottom: position.startsWith('bottom') ? '0' : 'auto',
            left: position.endsWith('left') ? '0' : 'auto',
            right: position.endsWith('right') ? '0' : 'auto',
            maxWidth: 'calc(100% - 12px)',
            zIndex: '100',
            pointerEvents: 'none',
            padding: '6px'
        });

        const badge = document.createElement('div');
        badge.className = 'quality-overlay-label rab-badge';
        badge.textContent = 'New';
        Object.assign(badge.style, {
            background: config.BadgeBackgroundColor || '#E53935',
            color: '#fff',
            borderRadius: '0',
            fontWeight: '700',
            fontSize: '12px',
            lineHeight: '1.1',
            padding: '5px 13px',
            display: 'inline-flex',
            alignItems: 'center',
            justifyContent: 'center',
            clipPath: 'polygon(50% 0%, 58% 9%, 69% 2%, 75% 13%, 88% 10%, 90% 23%, 100% 32%, 93% 43%, 100% 55%, 91% 64%, 89% 78%, 76% 80%, 70% 94%, 59% 90%, 50% 100%, 41% 90%, 30% 94%, 24% 80%, 11% 78%, 9% 64%, 0% 55%, 7% 43%, 0% 32%, 10% 23%, 12% 10%, 25% 13%, 31% 2%, 42% 9%)',
            boxSizing: 'border-box'
        });
        container.appendChild(badge);
        host.appendChild(container);
    }

    function placeBadge(card) {
        const scalable = card.querySelector('.cardScalable') || card;
        const host = scalable.querySelector('.je-tag-host') || scalable;
        const position = config.BadgePosition || 'top-left';
        const qualityContainer = Array.from(host.querySelectorAll('.quality-overlay-container'))
            .find(element => positionMatches(element, position));

        if (qualityContainer) {
            addBadgeToQualityContainer(card, qualityContainer);
        } else {
            const existing = card.querySelector('.rab-badge');
            if (existing) existing.remove();
            addStandaloneBadge(card, host);
        }
    }

    async function updateCard(card, userId) {
        if (!card.isConnected || !config) return;
        const id = card.getAttribute('data-id');
        if (!id) return;

        const item = await getItem(userId, id);
        if (!card.isConnected) return;
        if (!item || !['Movie', 'Series'].includes(item.Type) || !item.DateCreated) {
            removeBadge(card);
            return;
        }

        const added = new Date(item.DateCreated).getTime();
        const age = Date.now() - added;
        const days = Math.max(1, Math.min(365, Number(config.BadgeDurationDays) || 7));
        if (!Number.isFinite(added) || age < 0 || age >= days * 86400000) {
            removeBadge(card);
            return;
        }

        const parentLibraryEnabled = isInEnabledLibrary(item);
        if (!card.isConnected) return;
        if (!parentLibraryEnabled) {
            removeBadge(card);
            return;
        }

        placeBadge(card);
    }

    function queueScan() {
        if (scanQueued) return;
        scanQueued = true;
        requestAnimationFrame(() => {
            scanQueued = false;
            const api = currentApi();
            const userId = api?.getCurrentUserId?.();
            if (!userId || !config) return;
            document.querySelectorAll('.card[data-id]').forEach(card => {
                updateCard(card, userId).catch(() => {});
            });
        });
    }

    async function initialize() {
        const api = currentApi();
        if (!api) return false;
        try {
            config = await api.getPluginConfiguration(pluginId);
            const libraries = await api.getVirtualFolders();
            const enabledIds = new Set((config.EnabledLibraryIds || [])
                .map(id => String(id).replace(/-/g, '').toLowerCase()));
            libraryPaths = (libraries || [])
                .filter(folder => enabledIds.has(String(folder.ItemId || '').replace(/-/g, '').toLowerCase()))
                .flatMap(folder => folder.Locations || folder.LibraryOptions?.PathInfos?.map(info => info.Path) || [])
                .map(normalizePath)
                .filter(Boolean);
        } catch (error) {
            console.warn('[Recently Added Badges] Could not load plugin configuration or libraries.', error);
            return false;
        }

        if (observer) observer.disconnect();
        observer = new MutationObserver(() => queueScan());
        observer.observe(document.body, { childList: true, subtree: true });
        queueScan();
        console.log('[Recently Added Badges] Web poster badges initialized.');
        return true;
    }

    function start() {
        let attempts = 0;
        const tryStart = () => {
            initialize().then(ok => {
                if (ok) return;
                attempts++;
                if (attempts < 30) setTimeout(tryStart, 1000);
            });
        };
        tryStart();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', start, { once: true });
    } else {
        start();
    }
})();
