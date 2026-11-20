/*! instant-page.js v1.0.0 - (C) Instant Navigation Preloader for MYGTS */
(function () {
    'use strict';
    if (!('fetch' in window)) return;

    var prefetched = new Set();
    var timer = null;

    function prefetch(url) {
        if (!url || prefetched.has(url)) return;
        if (url.indexOf('/Account/Logout') !== -1 || url.indexOf('/Export') !== -1) return;
        prefetched.add(url);

        var link = document.createElement('link');
        link.rel = 'prefetch';
        link.href = url;
        link.as = 'document';
        document.head.appendChild(link);
    }

    function isEligible(a) {
        if (!a || a.tagName !== 'A' || !a.href) return false;
        if (a.origin !== window.location.origin) return false;
        if (a.hasAttribute('download') || a.target === '_blank') return false;
        var p = a.pathname || '';
        if (p === window.location.pathname) return false;
        // Only prefetch navigation module links
        return p.indexOf('/DMG') === 0 || p.indexOf('/RTO') === 0 || p.indexOf('/Alerts') === 0 || p.indexOf('/Users') === 0 || p.indexOf('/Backups') === 0;
    }

    document.addEventListener('mouseover', function (e) {
        var a = e.target.closest('a');
        if (!isEligible(a)) return;
        timer = setTimeout(function () {
            prefetch(a.href);
        }, 65);
    }, { passive: true });

    document.addEventListener('mouseout', function (e) {
        if (timer) {
            clearTimeout(timer);
            timer = null;
        }
    }, { passive: true });

    document.addEventListener('touchstart', function (e) {
        var a = e.target.closest('a');
        if (isEligible(a)) {
            prefetch(a.href);
        }
    }, { passive: true });
})();

