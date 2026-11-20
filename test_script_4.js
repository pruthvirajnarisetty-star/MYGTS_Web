

    window.currentUserWhatsAppPref = "Ask";



    function getEffectiveWaPref() {

        const local = localStorage.getItem('mygts_whatsapp_pref');

        if (local === 'Web' || local === 'App' || local === 'Ask') return local;

        if (window.currentUserWhatsAppPref === 'Web' || window.currentUserWhatsAppPref === 'App') {

            return window.currentUserWhatsAppPref;

        }

        return 'Ask';

    }



    function updateWaBadgeInNav(pref) {

        const badge = document.getElementById('userWaPrefBadge');

        if (badge) {

            badge.textContent = pref === 'Web' ? 'Web' : (pref === 'App' ? 'App' : 'Ask');

        }

    }



    function getWhatsAppUrls(rawUrl) {

        if (!rawUrl) return { web: 'https://web.whatsapp.com/send', app: 'whatsapp://send' };

        let clean = rawUrl.trim();

        if (!clean || (!clean.startsWith('http') && !clean.startsWith('whatsapp:'))) {

            return { web: 'https://web.whatsapp.com/send', app: 'whatsapp://send' };

        }

        let web = clean;

        let app = clean;



        if (clean.startsWith('whatsapp://')) {

            app = clean;

            web = clean.replace('whatsapp://send', 'https://web.whatsapp.com/send');

        } else if (clean.includes('web.whatsapp.com/send')) {

            web = clean;

            app = clean.replace('https://web.whatsapp.com/send', 'whatsapp://send');

        } else if (clean.includes('wa.me/')) {

            const parts = clean.split('wa.me/');

            if (parts.length > 1) {

                const after = parts[1];

                const qIdx = after.indexOf('?');

                let phone = qIdx >= 0 ? after.substring(0, qIdx) : after;

                let query = qIdx >= 0 ? after.substring(qIdx) : '';

                phone = phone.replace(/\D/g, '');

                if (phone) {

                    web = `https://web.whatsapp.com/send?phone=${phone}${query ? '&' + query.substring(1) : ''}`;

                    app = `whatsapp://send?phone=${phone}${query ? '&' + query.substring(1) : ''}`;

                } else {

                    web = `https://web.whatsapp.com/send${query}`;

                    app = `whatsapp://send${query}`;

                }

            }

        } else if (clean.includes('api.whatsapp.com/send')) {

            web = clean.replace('https://api.whatsapp.com/send', 'https://web.whatsapp.com/send');

            app = clean.replace('https://api.whatsapp.com/send', 'whatsapp://send');

        }



        return { web, app };

    }



    function launchWhatsAppApp(appUrl) {

        if (!appUrl || !appUrl.startsWith('whatsapp:')) {

            appUrl = 'whatsapp://send';

        }

        const a = document.createElement('a');

        a.href = appUrl;

        a.style.display = 'none';

        a.dataset.waBypass = 'true';

        document.body.appendChild(a);

        a.click();

        setTimeout(() => { a.remove(); }, 500);

    }



    let pendingWaPayload = null;



    function openWhatsAppMessage(target) {

        let rawUrl = typeof target === 'string' ? target : (target ? target.url : '');

        if (!rawUrl && target && target.text) {

            const phone = (target.phone || '').replace(/\D/g, '');

            const encText = encodeURIComponent(target.text);

            rawUrl = phone ? `https://web.whatsapp.com/send?phone=${phone}&text=${encText}` : `https://web.whatsapp.com/send?text=${encText}`;

        }

        if (!rawUrl || (!rawUrl.startsWith('http') && !rawUrl.startsWith('whatsapp:'))) {

            rawUrl = 'https://web.whatsapp.com/send';

        }



        const urls = getWhatsAppUrls(rawUrl);

        const pref = getEffectiveWaPref();



        if (pref === 'Web') {

            window.open(urls.web, '_blank');

            return;

        }

        if (pref === 'App') {

            launchWhatsAppApp(urls.app);

            return;

        }



        pendingWaPayload = urls;

        const overlay = document.getElementById('waClientChoiceModalOverlay');

        const chk = document.getElementById('waRememberChoiceCheckbox');

        if (chk) chk.checked = false;

        if (overlay) {

            overlay.style.display = 'flex';

            overlay.classList.add('open');

        }

    }



    function selectWhatsAppClient(choice) {

        const urls = pendingWaPayload;

        closeWaChoiceModal();



        const remember = document.getElementById('waRememberChoiceCheckbox')?.checked;

        if (remember) {

            setAndPersistWaPreference(choice);

        }



        if (urls) {

            if (choice === 'Web') {

                window.open(urls.web, '_blank');

            } else {

                launchWhatsAppApp(urls.app);

            }

        }

        pendingWaPayload = null;

    }



    function closeWaChoiceModal() {

        const overlay = document.getElementById('waClientChoiceModalOverlay');

        if (overlay) {

            overlay.classList.remove('open');

            overlay.style.display = 'none';

        }

        pendingWaPayload = null;

    }



    function handleWaChoiceOverlayClick(e) {

        if (e.target.id === 'waClientChoiceModalOverlay') {

            closeWaChoiceModal();

        }

    }



    function setAndPersistWaPreference(choice) {

        window.currentUserWhatsAppPref = choice;

        try { localStorage.setItem('mygts_whatsapp_pref', choice); } catch(e) {}

        updateWaBadgeInNav(choice);



        fetch('/Account/UpdateWhatsAppPreference', {

            method: 'POST',

            headers: {

                'Content-Type': 'application/json',

                'RequestVerificationToken': document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''

            },

            body: JSON.stringify({ preference: choice })

        }).catch(() => {});

    }



    function openWhatsAppPreferenceModal() {

        const overlay = document.getElementById('waUserPrefModalOverlay');

        const pref = getEffectiveWaPref();

        const radios = document.querySelectorAll('input[name="profileWaPref"]');

        radios.forEach(r => {

            r.checked = (r.value === pref);

        });

        const userToggle = document.getElementById('user-profile-toggle');

        if (userToggle) userToggle.checked = false;



        if (overlay) {

            overlay.style.display = 'flex';

            overlay.classList.add('open');

        }

    }



    function closeWaUserPrefModal() {

        const overlay = document.getElementById('waUserPrefModalOverlay');

        if (overlay) {

            overlay.classList.remove('open');

            overlay.style.display = 'none';

        }

    }



    function handleWaUserPrefOverlayClick(e) {

        if (e.target.id === 'waUserPrefModalOverlay') {

            closeWaUserPrefModal();

        }

    }



    function saveProfileWaPreference() {

        const selected = document.querySelector('input[name="profileWaPref"]:checked')?.value || 'Ask';

        setAndPersistWaPreference(selected);

        closeWaUserPrefModal();

        if (typeof Swal !== 'undefined') {

            Swal.fire({

                toast: true,

                position: 'bottom-end',

                icon: 'success',

                title: selected === 'Ask' ? 'WhatsApp preference: Always Ask' : (selected === 'Web' ? 'Saved: Always open in WhatsApp Web' : 'Saved: Always open in WhatsApp Desktop App'),

                showConfirmButton: false,

                timer: 3000,

                timerProgressBar: true

            });

        }

    }



    document.addEventListener('DOMContentLoaded', function() {

        updateWaBadgeInNav(getEffectiveWaPref());

    });



    // Global interceptor for all WhatsApp links

    document.addEventListener('click', function(e) {

        const a = e.target.closest('a.button-wa, a.button-wa-alert, a.btn-wa-action, a[href*="web.whatsapp.com"], a[href*="wa.me"], a[href*="whatsapp://"], a[href*="api.whatsapp.com"]');

        if (a) {

            e.preventDefault();

            e.stopPropagation();

            closeWaPortal();

            if (typeof closeWaPicker === 'function') closeWaPicker(a);



            let href = (a.getAttribute('href') || a.href || '').trim();

            if (!href || href === '#' || href === 'javascript:void(0)' || href === 'null' || href === 'undefined' || (!href.startsWith('http') && !href.startsWith('whatsapp:'))) {

                href = 'https://web.whatsapp.com/send';

            }

            openWhatsAppMessage(href);

        }

    }, true);



    function dismissLifecycleShift(vehicleNo, shiftType) {

        try {

            localStorage.setItem('dismissed_shift_' + vehicleNo + '_' + shiftType, '1');

        } catch (e) {}

        const item = document.getElementById('shift-item-' + vehicleNo + '-' + shiftType);

        if (item) {

            item.style.opacity = '0';

            item.style.transform = 'translateX(20px)';

            setTimeout(() => {

                item.remove();

                updateAlertBadgeCount();

            }, 200);

        }

    }



    let activeWaPortal = null;

    let activeWaBtn = null;



    function escapeWaAttr(str) {

        if (!str) return '';

        return String(str)

            .replace(/\\/g, '\\\\')

            .replace(/'/g, "\\'")

            .replace(/"/g, '&quot;')

            .replace(/[\r\n]+/g, ' ');

    }



    function handleWaPortalClick(url, contactType, contactName, phone) {

        closeWaPortal();

        let targetUrl = (url || '').trim();

        if (!targetUrl || targetUrl === 'null' || targetUrl === 'undefined' || targetUrl === '#' || (!targetUrl.startsWith('http') && !targetUrl.startsWith('whatsapp:'))) {

            const clean = (phone || '').replace(/\D/g, '');

            if (clean) {

                const finalPhone = clean.length === 10 ? '91' + clean : clean;

                targetUrl = `https://web.whatsapp.com/send?phone=${finalPhone}`;

            } else {

                targetUrl = `https://web.whatsapp.com/send`;

            }

        }

        openWhatsAppMessage(targetUrl);

    }



    function openWaPickerPortal(btn, ownerUrl, vendorUrl, ownerName, cellNo, vendorName, vendorPhone) {

        if (activeWaPortal && activeWaBtn === btn) {

            closeWaPortal();

            return;

        }

        closeWaPortal();



        if (!ownerUrl && btn.dataset) {

            ownerUrl = btn.dataset.ownerUrl || '';

            vendorUrl = btn.dataset.vendorUrl || '';

            ownerName = btn.dataset.ownerName || '';

            cellNo = btn.dataset.cellNo || '';

            vendorName = btn.dataset.vendorName || '';

            vendorPhone = btn.dataset.vendorPhone || '';

        }



        const dropdown = document.createElement('div');

        dropdown.id = 'wa-portal-dropdown';

        dropdown.className = 'wa-portal-dropdown';



        const vendorSub = vendorPhone ? vendorPhone : (vendorName ? 'Opens WhatsApp — select contact' : 'Opens WhatsApp');



        dropdown.innerHTML = `

            <div class="wa-portal-header">Send message to</div>

            <button type="button" class="wa-portal-item" onclick="handleWaPortalClick('${escapeWaAttr(ownerUrl)}', 'Owner', '${escapeWaAttr(ownerName)}', '${escapeWaAttr(cellNo)}')">

                <span style="font-weight:600; font-size:0.82rem; display:flex; align-items:center; gap:5px;">

                    <span>👤</span> Owner${ownerName ? ' — ' + escapeWaHtml(ownerName) : ''}

                </span>

                <span class="wa-portal-subtitle">${escapeWaHtml(cellNo) || 'Opens WhatsApp — select contact'}</span>

            </button>

            <button type="button" class="wa-portal-item" onclick="handleWaPortalClick('${escapeWaAttr(vendorUrl)}', 'Vendor', '${escapeWaAttr(vendorName)}', '${escapeWaAttr(vendorPhone)}')">

                <span style="font-weight:600; font-size:0.82rem; display:flex; align-items:center; gap:5px;">

                    <span>🏪</span> Vendor${vendorName ? ' — ' + escapeWaHtml(vendorName) : ''}

                </span>

                <span class="wa-portal-subtitle">${escapeWaHtml(vendorSub)}</span>

            </button>

        `;



        document.body.appendChild(dropdown);

        activeWaPortal = dropdown;

        activeWaBtn = btn;



        // Position dropdown relative to the button

        const rect = btn.getBoundingClientRect();

        const dropdownHeight = dropdown.offsetHeight || 130;

        const spaceBelow = window.innerHeight - rect.bottom;



        if (spaceBelow >= dropdownHeight + 10 || rect.top < dropdownHeight) {

            dropdown.style.top = (rect.bottom + 6) + 'px';

        } else {

            dropdown.style.top = Math.max(10, rect.top - dropdownHeight - 6) + 'px';

        }



        const dropdownWidth = dropdown.offsetWidth || 240;

        if (rect.left + dropdownWidth <= window.innerWidth - 10) {

            dropdown.style.left = Math.max(10, rect.left) + 'px';

        } else {

            dropdown.style.right = Math.max(10, window.innerWidth - rect.right) + 'px';

        }

    }



    function closeWaPortal() {

        if (activeWaPortal) {

            activeWaPortal.remove();

            activeWaPortal = null;

            activeWaBtn = null;

        }

    }



    function escapeWaHtml(str) {

        if (!str) return '';

        const div = document.createElement('div');

        div.textContent = str;

        return div.innerHTML;

    }



    function toggleWaPicker(btn) {

        const dropdown = btn.nextElementSibling;

        if (!dropdown) return;

        const isOpen = dropdown.style.display !== 'none';

        document.querySelectorAll('.wa-picker-dropdown').forEach(d => d.style.display = 'none');

        if (!isOpen) dropdown.style.display = 'block';

    }



    function closeWaPicker(link) {

        const dropdown = link.closest('.wa-picker-dropdown');

        if (dropdown) dropdown.style.display = 'none';

    }



    document.addEventListener('click', function(e) {

        if (activeWaPortal) {

            if (!activeWaPortal.contains(e.target) && !activeWaBtn?.contains(e.target)) {

                closeWaPortal();

            }

        }

        if (!e.target.closest('.wa-picker')) {

            document.querySelectorAll('.wa-picker-dropdown').forEach(d => d.style.display = 'none');

        }

    });



    window.addEventListener('resize', closeWaPortal);

    window.addEventListener('scroll', closeWaPortal, true);



    function dismissNotification(vehicleNo) {

        const item = document.getElementById('alert-item-' + vehicleNo) || document.getElementById('urgent-dev-' + vehicleNo);

        if (item) {

            item.style.opacity = '0';

            item.style.transform = 'translateX(20px)';

            setTimeout(() => {

                item.remove();

                updateAlertBadgeCount();

            }, 200);

        }

    }



    function markVehicleInformed(vehicleNo) {

        const selector = `#alert-item-${CSS.escape(vehicleNo)}, #urgent-dev-${CSS.escape(vehicleNo)}, #profile-exp-${CSS.escape(vehicleNo)}`;

        document.querySelectorAll(selector).forEach(item => {

            item.style.opacity = '0';

            item.style.transform = 'translateX(20px)';

            setTimeout(() => {

                item.remove();

                updateAlertBadgeCount();

            }, 200);

        });

        const formData = new FormData();

        formData.append('vehicleNo', vehicleNo);

        fetch('/Alerts/MarkInformed', {

            method: 'POST',

            body: formData

        }).then(res => {

            if (res.ok) {

                pollAlertsNow();

            }

        }).catch(() => {});

    }



    // --- Batch WhatsApp Functions ---

    let batchVendorsCache = [];

    let batchCurrentVehicles = [];

    let isFetchingBatchVendors = false;



    function getBatchBranch() {

        const p = (window.location.pathname || '').toLowerCase();

        if (p.startsWith('/rto')) return 'RTO';

        if (p.startsWith('/dmg')) return 'DMG';

        const match = document.cookie.match(/(?:^|;\s*)mygts_branch=([^;]+)/);

        if (match && match[1]) {

            const b = decodeURIComponent(match[1]).toUpperCase();

            if (b === 'RTO') return 'RTO';

            if (b === 'DMG') return 'DMG';

        }

        return 'DMG';

    }



    function populateBatchVendorDropdown(initialVendor) {

        const select = document.getElementById('batchVendorSelect');

        if (!select) return;

        let optionsHtml = '<option value="">-- Choose Vendor --</option>';

        let foundInitial = false;



        if (Array.isArray(batchVendorsCache) && batchVendorsCache.length > 0) {

            batchVendorsCache.forEach(v => {

                if (!v || !v.vendor) return;

                const vName = String(v.vendor).trim();

                const isSelected = Boolean(initialVendor && vName.toLowerCase() === String(initialVendor).trim().toLowerCase());

                if (isSelected) foundInitial = true;

                const dueBadge = (v.expiringCount && v.expiringCount > 0) ? ` (${v.expiringCount} due)` : ((v.totalCount && v.totalCount > 0) ? ` (${v.totalCount} veh)` : '');

                optionsHtml += `<option value="${escapeWaHtml(vName)}" data-phone="${escapeWaHtml(v.phone || '')}" ${isSelected ? 'selected' : ''}>${escapeWaHtml(vName)}${dueBadge}</option>`;

            });

        }



        if (initialVendor && !foundInitial) {

            optionsHtml += `<option value="${escapeWaHtml(initialVendor)}" data-phone="" selected>${escapeWaHtml(initialVendor)}</option>`;

        }

        select.innerHTML = optionsHtml;



        const opt = select.options[select.selectedIndex];

        const phoneInput = document.getElementById('batchVendorPhone');

        if (phoneInput) {

            if (opt && opt.dataset.phone) {

                phoneInput.value = opt.dataset.phone;

            } else if (!opt || !opt.value) {

                phoneInput.value = '';

            }

        }

    }



    function fetchBatchVendors(initialVendor, callback) {

        if (isFetchingBatchVendors) return;

        isFetchingBatchVendors = true;

        const branch = getBatchBranch();

        const urls = [

            `/${branch}/Alerts/GetExpiringVendorsSummary`,

            `/Alerts/GetExpiringVendorsSummary?branch=${branch}`

        ];



        function tryFetch(idx) {

            if (idx >= urls.length) {

                isFetchingBatchVendors = false;

                if (callback) callback();

                return;

            }

            fetch(urls[idx], { headers: { 'Accept': 'application/json' } })

                .then(r => {

                    if (!r.ok) throw new Error('Status ' + r.status);

                    const ct = r.headers.get('content-type') || '';

                    if (!ct.includes('application/json')) throw new Error('Not JSON');

                    return r.json();

                })

                .then(data => {

                    isFetchingBatchVendors = false;

                    if (Array.isArray(data) && data.length > 0) {

                        batchVendorsCache = data;

                        populateBatchVendorDropdown(initialVendor);

                    }

                    if (callback) callback();

                })

                .catch(() => {

                    tryFetch(idx + 1);

                });

        }

        tryFetch(0);

    }



    function openBatchWhatsAppModal(initialVendor) {

        const overlay = document.getElementById('batchWaModalOverlay');

        if (!overlay) return;

        overlay.classList.add('open');



        const select = document.getElementById('batchVendorSelect');



        if (batchVendorsCache && batchVendorsCache.length > 0) {

            populateBatchVendorDropdown(initialVendor);

            if (initialVendor) {

                loadBatchVehiclesForVendor(initialVendor);

            } else if (select && select.value) {

                loadBatchVehiclesForVendor(select.value);

            } else {

                batchCurrentVehicles = [];

                renderBatchVehicleQueue();

                updateBatchMessagePreview();

            }

        } else {

            if (select) {

                select.innerHTML = '<option value="">Loading vendors...</option>';

            }

            fetchBatchVendors(initialVendor, () => {

                const sel = document.getElementById('batchVendorSelect');

                if (initialVendor) {

                    loadBatchVehiclesForVendor(initialVendor);

                } else if (sel && sel.value) {

                    loadBatchVehiclesForVendor(sel.value);

                } else {

                    batchCurrentVehicles = [];

                    renderBatchVehicleQueue();

                    updateBatchMessagePreview();

                }

            });

        }

    }



    // Preload vendors when DOM is ready so modal dropdown is pre-populated

    if (document.readyState === 'loading') {

        document.addEventListener('DOMContentLoaded', () => { setTimeout(() => fetchBatchVendors(), 400); });

    } else {

        setTimeout(() => fetchBatchVendors(), 400);

    }



    function closeBatchWhatsAppModal() {

        const overlay = document.getElementById('batchWaModalOverlay');

        if (overlay) overlay.classList.remove('open');

    }



    function handleBatchWaOverlayClick(e) {

        if (e.target.id === 'batchWaModalOverlay') {

            closeBatchWhatsAppModal();

        }

    }



    function onBatchVendorSelectChanged() {

        const select = document.getElementById('batchVendorSelect');

        const selectedVendor = select ? select.value : '';

        const phoneInput = document.getElementById('batchVendorPhone');

        if (!selectedVendor) {

            batchCurrentVehicles = [];

            if (phoneInput) phoneInput.value = '';

            renderBatchVehicleQueue();

            updateBatchMessagePreview();

            return;

        }



        const opt = select.options[select.selectedIndex];

        if (phoneInput && opt && opt.dataset.phone) {

            phoneInput.value = opt.dataset.phone;

        } else if (phoneInput) {

            phoneInput.value = '';

        }



        loadBatchVehiclesForVendor(selectedVendor);

    }



    function loadBatchVehiclesForVendor(vendorName) {

        if (!vendorName) return;

        const phoneInput = document.getElementById('batchVendorPhone');

        const branch = getBatchBranch();

        const urls = [

            `/${branch}/Alerts/GetVendorExpiringVehicles?vendor=${encodeURIComponent(vendorName)}`,

            `/Alerts/GetVendorExpiringVehicles?vendor=${encodeURIComponent(vendorName)}&branch=${branch}`

        ];



        function tryFetch(idx) {

            if (idx >= urls.length) {

                batchCurrentVehicles = [];

                renderBatchVehicleQueue();

                updateBatchMessagePreview();

                return;

            }

            fetch(urls[idx], { headers: { 'Accept': 'application/json' } })

                .then(r => {

                    if (!r.ok) throw new Error('Status ' + r.status);

                    return r.json();

                })

                .then(data => {

                    const list = (data && data.devices) ? data.devices : (Array.isArray(data) ? data : []);

                    if (data && data.phone && phoneInput) {

                        phoneInput.value = data.phone;

                        const sel = document.getElementById('batchVendorSelect');

                        if (sel && sel.selectedIndex >= 0) {

                            sel.options[sel.selectedIndex].dataset.phone = data.phone;

                        }

                    }

                    batchCurrentVehicles = list.map(v => {

                        let tag = v.expiryDate || 'N/A';

                        let tagClass = '';

                        if (v.daysLeft !== undefined && v.daysLeft < 0) {

                            tag = `${v.expiryDate || ''} (Expired ${Math.abs(v.daysLeft)}d ago)`;

                            tagClass = 'urgent-today';

                        } else if (v.daysLeft === 0) {

                            tag = `${v.expiryDate || ''} (Expires Today)`;

                            tagClass = 'urgent-today';

                        }

                        return {

                            vehicleNo: v.vehicleNo,

                            expiryDate: v.expiryDate,

                            displayTag: tag,

                            tagClass: tagClass,

                            status: v.status || 'Active',

                            selected: true

                        };

                    });

                    renderBatchVehicleQueue();

                    updateBatchMessagePreview();

                })

                .catch(() => tryFetch(idx + 1));

        }

        tryFetch(0);

    }



    function renderBatchVehicleQueue() {

        const listEl = document.getElementById('batchVehicleList');

        const countBadge = document.getElementById('batchVehicleCountBadge');

        if (!listEl) return;



        if (batchCurrentVehicles.length === 0) {

            listEl.innerHTML = '<div style="padding: 12px; text-align: center; color: var(--muted); font-size: 0.8rem;">No vehicles due for renewal found for this vendor. Use "+ Add Another Vehicle" below.</div>';

            if (countBadge) countBadge.textContent = '0 selected';

            return;

        }



        const selectedCount = batchCurrentVehicles.filter(v => v.selected).length;

        if (countBadge) countBadge.textContent = `${selectedCount} / ${batchCurrentVehicles.length} selected`;



        let html = '';

        batchCurrentVehicles.forEach((v, idx) => {

            html += `

            <div class="batch-vehicle-item">

                <label>

                    <input type="checkbox" ${v.selected ? 'checked' : ''} onchange="toggleBatchVehicle(${idx}, this.checked)" />

                    <strong style="color: var(--text-main); font-size: 0.85rem;">${escapeWaHtml(v.vehicleNo)}</strong>

                    <span class="expiry-tag ${v.tagClass || ''}" style="font-size: 0.7rem;">${escapeWaHtml(v.displayTag || v.expiryDate || 'N/A')}</span>

                </label>

                <button type="button" class="btn-dismiss-alert" onclick="removeBatchVehicle(${idx})" title="Remove vehicle from message" style="font-size: 1rem;">&times;</button>

            </div>`;

        });

        listEl.innerHTML = html;

    }



    function toggleBatchVehicle(idx, isChecked) {

        if (batchCurrentVehicles[idx]) {

            batchCurrentVehicles[idx].selected = isChecked;

            renderBatchVehicleQueue();

            updateBatchMessagePreview();

        }

    }



    function removeBatchVehicle(idx) {

        batchCurrentVehicles.splice(idx, 1);

        renderBatchVehicleQueue();

        updateBatchMessagePreview();

    }



    function addManualVehicleToBatch() {

        const vehInput = document.getElementById('batchManualVehNo');

        const expInput = document.getElementById('batchManualExpiry');

        const veh = (vehInput?.value || '').trim().toUpperCase();

        const exp = (expInput?.value || '').trim();



        if (!veh) {

            alert('Please enter a vehicle number.');

            return;

        }



        let formattedDate = exp;

        if (exp) {

            const d = new Date(exp);

            if (!isNaN(d.getTime())) {

                const months = ['Jan','Feb','Mar','Apr','May','Jun','Jul','Aug','Sep','Oct','Nov','Dec'];

                formattedDate = `${d.getDate()} ${months[d.getMonth()]} ${d.getFullYear()}`;

            }

        } else {

            formattedDate = 'Immediate';

        }



        batchCurrentVehicles.push({

            vehicleNo: veh,

            expiryDate: formattedDate,

            selected: true

        });



        if (vehInput) vehInput.value = '';

        if (expInput) expInput.value = '';

        renderBatchVehicleQueue();

        updateBatchMessagePreview();

    }



    function updateBatchMessagePreview() {

        const previewEl = document.getElementById('batchMessagePreview');

        if (!previewEl) return;



        const select = document.getElementById('batchVendorSelect');

        const vendorName = (select?.value || 'Valued Partner').trim();

        const selectedVehicles = batchCurrentVehicles.filter(v => v.selected);

        const count = selectedVehicles.length;



        if (count === 0) {

            previewEl.value = 'Please select at least 1 vehicle to generate message preview.';

            return;

        }



        let enList = '';

        let knList = '';

        selectedVehicles.forEach((v, i) => {

            enList += `${i + 1}. *${v.vehicleNo}* — Expiry: *${v.expiryDate}*\n`;

            knList += `${i + 1}. *${v.vehicleNo}* — ಮುಕ್ತಾಯ ದಿನಾಂಕ: *${v.expiryDate}*\n`;

        });



        const msg = `*MYGTS SERVICES*\n\u200B\nDear ${vendorName},\n\u200B\nThe following (${count}) vehicles under your account are due for *One State One GPS* recharge renewal:\n\u200B\n${enList}\u200B\nKindly ensure renewals are completed before the expiry dates to avoid GPS tracking interruption.\n\u200B\nThank you,\n*MYGTS SERVICES*\n\u200B\n--------------------------------\n\u200B\n*ಮೈ ಜಿಟಿಎಸ್ ಸರ್ವಿಸಸ್ (MYGTS SERVICES)*\n\u200B\nಪ್ರಿಯ ${vendorName},\n\u200B\nನಿಮ್ಮ ಖಾತೆಯಡಿ ಇರುವ ಈ ಕೆಳಗಿನ (${count}) ವಾಹನಗಳ ಒಂದು ರಾಜ್ಯ ಒಂದು ಜಿಪಿಎಸ್ (One State One GPS) ರಿಚಾರ್ಜ್ ಶೀಘ್ರದಲ್ಲೇ ಮುಕ್ತಾಯಗೊಳ್ಳಲಿದೆ:\n\u200B\n${knList}\u200B\nಜಿಪಿಎಸ್ ಟ್ರ್ಯಾಕಿಂಗ್ ಸೇವೆಗಳಲ್ಲಿ ಯಾವುದೇ ಅಡಚಣೆಯನ್ನು ತಪ್ಪಿಸಲು ದಯವಿಟ್ಟು ಮುಕ್ತಾಯದ ದಿನಾಂಕದ ಮೊದಲು ರಿಚಾರ್ಜ್ ಅನ್ನು ನವೀಕರಿಸಿ (Renew).\n\u200B\nಧನ್ಯವಾದಗಳು,\n*ಮೈ ಜಿಟಿಎಸ್ ಸರ್ವಿಸಸ್ (MYGTS SERVICES)*`;



        previewEl.value = msg;

    }



    function copyBatchMessageText() {

        const previewEl = document.getElementById('batchMessagePreview');

        if (!previewEl || !previewEl.value) return;

        navigator.clipboard.writeText(previewEl.value).then(() => {

            alert('Bilingual WhatsApp message copied to clipboard!');

        }).catch(() => {

            previewEl.select();

            document.execCommand('copy');

            alert('Bilingual WhatsApp message copied to clipboard!');

        });

    }



    function sendBatchWhatsApp() {

        const previewEl = document.getElementById('batchMessagePreview');

        const msg = previewEl?.value || '';

        if (!msg || batchCurrentVehicles.filter(v => v.selected).length === 0) {

            alert('Please select at least 1 vehicle before sending.');

            return;

        }

        const rawPhone = (document.getElementById('batchVendorPhone')?.value || '').trim();

        let cleanPhone = rawPhone.replace(/\D/g, '');

        if (cleanPhone.length === 10) cleanPhone = '91' + cleanPhone;



        const url = cleanPhone

            ? `https://web.whatsapp.com/send?phone=${cleanPhone}&text=${encodeURIComponent(msg)}`

            : `https://web.whatsapp.com/send?text=${encodeURIComponent(msg)}`;



        openWhatsAppMessage(url);

    }



    function exportBatchVehiclesExcel() {

        const select = document.getElementById('batchVendorSelect');

        const vendor = select ? select.value : '';

        if (!vendor) {

            alert('Please select a vendor first to export expiring vehicles.');

            return;

        }

        window.location.href = `/Vendors/ExportExpiring?id=${encodeURIComponent(vendor)}`;

    }



    function dismissFollowupAlert(id) {

        const item = document.getElementById('followup-item-' + id);

        if (item) {

            item.style.opacity = '0';

            item.style.transform = 'translateX(20px)';

            setTimeout(() => {

                item.remove();

                updateAlertBadgeCount();

            }, 200);

        }

    }



    function completeFollowupAlert(id) {

        const item = document.getElementById('followup-item-' + id);

        if (item) {

            item.style.opacity = '0';

            item.style.transform = 'translateX(20px)';

            setTimeout(() => {

                item.remove();

                updateAlertBadgeCount();

            }, 200);

        }

        fetch('/Alerts/CompleteFollowup?id=' + encodeURIComponent(id), { method: 'POST' })

            .then(res => {

                if (res.ok) pollAlertsNow();

            })

            .catch(() => {});

    }



    function clearAllNotifications() {

        const list = document.querySelector('.notification-list');

        if (list) {

            list.innerHTML = `

                <div style="padding: 28px 16px; text-align: center; color: var(--muted); font-size: 0.85rem;">

                    <div style="font-size: 1.8rem; margin-bottom: 6px;">✨</div>

                    <strong style="color: var(--text-main);">All notifications cleared</strong>

                    <div style="font-size: 0.75rem; margin-top: 2px;">You're all caught up!</div>

                </div>`;

        }

        const bell = document.querySelector('.notification');

        if (bell) {

            bell.classList.remove('has-alerts');

            bell.removeAttribute('data-count');

        }

        const pill = document.getElementById('headerAlertCount');

        if (pill) pill.remove();

    }



    function dismissRechargeAlert(id) {

        const item = document.getElementById('recharge-item-' + id);

        if (item) {

            item.style.opacity = '0';

            item.style.transform = 'translateX(20px)';

            setTimeout(() => {

                item.remove();

                updateAlertBadgeCount();

            }, 200);

        }

    }



    function updateAlertBadgeCount() {

        const remaining = document.querySelectorAll('.notification-item').length;

        const bell = document.querySelector('.notification');

        const pill = document.getElementById('headerAlertCount');

        if (remaining > 0) {

            if (bell) {

                bell.setAttribute('data-count', remaining);

                bell.classList.add('has-alerts');

            }

            if (pill) pill.textContent = remaining + ' DUE';

        } else {

            if (bell) {

                bell.removeAttribute('data-count');

                bell.classList.remove('has-alerts');

            }

            if (pill) pill.remove();

            const list = document.querySelector('.notification-list');

            if (list) {

                list.innerHTML = `

                    <div style="padding: 28px 16px; text-align: center; color: var(--muted); font-size: 0.85rem;">

                        <div style="font-size: 1.8rem; margin-bottom: 6px;">✨</div>

                        <strong style="color: var(--text-main);">All notifications cleared</strong>

                        <div style="font-size: 0.75rem; margin-top: 2px;">You're all caught up!</div>

                    </div>`;

            }

        }

        ['overdue', 'shifts', 'expiring', 'followups', 'pending-recharges', 'unpaid-recharges'].forEach(sec => {

            const body = document.getElementById('section-body-' + sec);

            const headerTitle = document.querySelector(`.notification-section-divider[data-section="${sec}"] .section-title`);

            if (body && headerTitle) {

                const count = body.querySelectorAll('.notification-item').length;

                if (count === 0) {

                    const secContainer = body.closest('.notification-section');

                    if (secContainer) secContainer.style.display = 'none';

                } else {

                    headerTitle.innerHTML = headerTitle.innerHTML.replace(/\(\d+\)/, `(${count})`);

                }

            }

        });

    }



    // Collapsible accordion state for alert sections (collapsed by default)

    window._alertSectionStates = window._alertSectionStates || {

        overdue: false,

        shifts: false,

        expiring: false,

        followups: false,

        'pending-recharges': false,

        'unpaid-recharges': false

    };



    function toggleAlertSection(sectionId, headerEl) {

        const cleanId = String(sectionId || '').replace(/^(section-body-|sec-)/, '');

        const body = document.getElementById('section-body-' + cleanId) 

                  || document.getElementById('sec-' + cleanId) 

                  || document.getElementById(sectionId);

        const header = headerEl 

                    || document.querySelector(`.notification-section-divider[data-section="${cleanId}"]`) 

                    || (body ? body.closest('.notification-section')?.querySelector('.notification-section-divider') : null);

        if (!body) return;

        const isClosed = (body.style.display === 'none' || window.getComputedStyle(body).display === 'none');

        body.style.display = isClosed ? 'block' : 'none';

        if (header) {

            header.setAttribute('aria-expanded', isClosed ? 'true' : 'false');

        }

        window._alertSectionStates = window._alertSectionStates || {};

        window._alertSectionStates[cleanId] = isClosed;

    }



    function onSectionHeaderKeydown(e, sectionId) {

        if (e.key === 'Enter' || e.key === ' ') {

            e.preventDefault();

            toggleAlertSection(sectionId, e.currentTarget);

        }

    }



    function filterAlerts(query) {

        const q = (query || '').trim().toLowerCase();

        const clearBtn = document.getElementById('alertSearchClear');

        if (clearBtn) clearBtn.style.display = q ? 'block' : 'none';



        const list = document.querySelector('.notification-list');

        if (!list) return;



        let noMatchEl = document.getElementById('alertNoMatchNotice');



        if (!q) {

            list.querySelectorAll('.notification-section').forEach(sec => {

                sec.style.display = '';

                const secId = sec.getAttribute('data-section-id');

                const body = sec.querySelector('.notification-section-body');

                const isOpen = window._alertSectionStates && window._alertSectionStates[secId];

                if (body) body.style.display = isOpen ? 'block' : 'none';

                const divider = sec.querySelector('.notification-section-divider');

                if (divider) divider.setAttribute('aria-expanded', isOpen ? 'true' : 'false');

            });

            list.querySelectorAll('.notification-item').forEach(item => {

                item.style.display = '';

            });

            if (noMatchEl) noMatchEl.style.display = 'none';

            return;

        }



        let totalMatches = 0;

        const sections = list.querySelectorAll('.notification-section');



        sections.forEach(sec => {

            const items = sec.querySelectorAll('.notification-item');

            let secMatchCount = 0;



            items.forEach(item => {

                const text = item.textContent.toLowerCase();

                if (text.includes(q)) {

                    item.style.display = '';

                    secMatchCount++;

                    totalMatches++;

                } else {

                    item.style.display = 'none';

                }

            });



            const body = sec.querySelector('.notification-section-body');

            const divider = sec.querySelector('.notification-section-divider');



            if (secMatchCount > 0) {

                sec.style.display = '';

                if (body) body.style.display = 'block';

                if (divider) divider.setAttribute('aria-expanded', 'true');

            } else {

                sec.style.display = 'none';

            }

        });



        if (totalMatches === 0) {

            if (!noMatchEl) {

                noMatchEl = document.createElement('div');

                noMatchEl.id = 'alertNoMatchNotice';

                noMatchEl.style.cssText = 'padding: 24px 16px; text-align: center; color: var(--muted); font-size: 0.85rem;';

                list.appendChild(noMatchEl);

            }

            noMatchEl.innerHTML = `

                <div style="font-size: 1.5rem; margin-bottom: 4px;">🔍</div>

                <strong style="color: var(--text-main);">No matching alerts</strong>

                <div style="font-size: 0.75rem; margin-top: 4px;">No alerts match &ldquo;${escapeWaHtml(q)}&rdquo;</div>

            `;

            noMatchEl.style.display = 'block';

        } else if (noMatchEl) {

            noMatchEl.style.display = 'none';

        }

    }



    function clearAlertSearch() {

        const input = document.getElementById('alertSearchInput');

        if (input) {

            input.value = '';

            input.focus();

        }

        filterAlerts('');

    }



    // Live Alert Synchronization Feed across all users

    let lastAlertDataJson = '';

    let isPollingAlerts = false;



    function pollAlertsNow() {

        if (isPollingAlerts) return;

        isPollingAlerts = true;

        fetch('/Alerts/Feed')

            .then(res => res.ok ? res.json() : null)

            .then(data => {

                isPollingAlerts = false;

                if (!data) return;

                const jsonStr = JSON.stringify(data);

                if (jsonStr === lastAlertDataJson) return;

                lastAlertDataJson = jsonStr;

                renderLiveAlerts(data);

            })

            .catch(() => { isPollingAlerts = false; });

    }



    function renderLiveAlerts(data) {

        const bell = document.querySelector('.notification');

        const count = data.count || 0;



        if (bell) {

            if (count > 0) {

                bell.setAttribute('data-count', count);

                bell.classList.add('has-alerts');

                bell.title = count + " alert(s) active";

            } else {

                bell.removeAttribute('data-count');

                bell.classList.remove('has-alerts');

                bell.title = "No active alerts";

            }

        }



        const titleWrap = document.querySelector('.notification-title-wrap');

        let pill = document.getElementById('headerAlertCount');

        if (count > 0) {

            if (!pill && titleWrap) {

                pill = document.createElement('span');

                pill.className = 'alert-pill-count';

                pill.id = 'headerAlertCount';

                titleWrap.appendChild(pill);

            }

            if (pill) pill.textContent = count + ' DUE';

        } else if (pill) {

            pill.remove();

        }



        const subtitleEl = document.querySelector('.notification-subtitle');

        if (subtitleEl) {

            if (data.isExecutive) {

                if (data.followupsCount > 0) {

                    subtitleEl.textContent = `${(data.overdueCount || 0)} overdue, ${data.devicesCount} expiring, ${data.followupsCount} follow-up(s)`;

                } else if (data.overdueCount > 0) {

                    subtitleEl.textContent = `${data.overdueCount} overdue, ${data.devicesCount} expiring in ${data.branch}`;

                } else if (count > 0) {

                    subtitleEl.textContent = `${count} alert(s) in ${data.branch}`;

                } else {

                    subtitleEl.textContent = 'All devices and follow-ups are up to date';

                }

            } else {

                subtitleEl.textContent = data.followupsCount > 0

                    ? `${data.followupsCount} alert(s) addressed to you`

                    : 'No alerts addressed to you';

            }

        }



        const list = document.querySelector('.notification-list');

        if (!list) return;



        const activeShifts = (data.lifecycleShifts || []).filter(s => {

            try {

                return !localStorage.getItem('dismissed_shift_' + s.vehicleNo + '_' + s.shiftType);

            } catch (e) { return true; }

        });



        const overdueList = data.overdueDevices || [];

        const expiringList = data.devices || [];

        const followupsList = data.followups || [];

        const pendingRcList = data.pendingRecharges || [];

        const unpaidRcList = data.unpaidRecharges || [];



        if (count === 0 && overdueList.length === 0 && activeShifts.length === 0 && followupsList.length === 0 && pendingRcList.length === 0 && unpaidRcList.length === 0) {

            list.innerHTML = `

                <div style="padding: 24px 16px; text-align: center; color: var(--muted); font-size: 0.85rem;">

                    <div style="font-size: 1.8rem; margin-bottom: 6px;">🎉</div>

                    <strong style="color: var(--text-main);">No alerts or overdue devices</strong>

                    <div style="font-size: 0.75rem; margin-top: 2px;">Subscriptions and follow-ups across ${escapeWaHtml(data.branch)} branch are in good standing.</div>

                </div>`;

            return;

        }



        let html = '';



        const isOpenOverdue = !!window._alertSectionStates['overdue'];

        if (overdueList.length > 0) {

            html += `

            <div class="notification-section" data-section-id="overdue">

                <div class="notification-section-divider urgent-divider" data-section="overdue" onclick="toggleAlertSection('overdue', this)" onkeydown="onSectionHeaderKeydown(event, 'overdue')" role="button" aria-expanded="${isOpenOverdue}" tabindex="0">

                    <span class="section-title">🚨 Overdue &amp; Expired (${overdueList.length})</span>

                    <span class="section-chevron">

                        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"></polyline></svg>

                    </span>

                </div>

                <div class="notification-section-body" id="section-body-overdue" style="display: ${isOpenOverdue ? 'block' : 'none'};">`;



            overdueList.forEach(dev => {

                html += `

                <div class="notification-item urgent-overdue" id="alert-item-${escapeWaHtml(dev.vehicleNo)}">

                    <div style="display: flex; justify-content: space-between; align-items: flex-start; gap: 8px;">

                        <div>

                            <div style="font-weight: 700; font-size: 0.88rem; color: var(--text-main); display: flex; align-items: center; gap: 6px; flex-wrap: wrap;">

                                <span>${escapeWaHtml(dev.vehicleNo)}</span>

                                <span class="expiry-tag">${escapeWaHtml(dev.urgencyLabel)}</span>

                            </div>

                            <div style="font-size: 0.78rem; color: var(--muted); margin-top: 3px;">

                                ${escapeWaHtml(dev.owner || 'No Owner Assigned')}

                                ${dev.cellNo ? ` &bull; ${escapeWaHtml(dev.cellNo)}` : ''}

                                ${dev.vendor ? ` &bull; ${escapeWaHtml(dev.vendor)}` : ''}

                            </div>

                        </div>

                        <button type="button" class="btn-dismiss-alert" onclick="dismissNotification('${escapeWaHtml(dev.vehicleNo)}')" title="Dismiss notification">&times;</button>

                    </div>

                    <div class="notification-actions">

                        <div class="wa-picker" style="position: relative; display: inline-flex;">

                            <button type="button" class="button-wa-alert"

                                data-owner-url="${escapeWaHtml(dev.waOwnerUrl)}"

                                data-vendor-url="${escapeWaHtml(dev.waVendorUrl)}"

                                data-owner-name="${escapeWaHtml(dev.owner)}"

                                data-cell-no="${escapeWaHtml(dev.cellNo)}"

                                data-vendor-name="${escapeWaHtml(dev.vendor)}"

                                data-vendor-phone="${escapeWaHtml(dev.vendorPhone)}"

                                onclick="openWaPickerPortal(this)" title="Send WhatsApp message">

                                <svg width="13" height="13" viewBox="0 0 24 24" fill="currentColor"><path d="M.057 24l1.687-6.163c-1.041-1.804-1.588-3.849-1.587-5.946.003-6.556 5.338-11.891 11.893-11.891 3.181.001 6.167 1.24 8.413 3.488 2.245 2.248 3.481 5.236 3.48 8.414-.003 6.557-5.338 11.892-11.893 11.892-1.99-.001-3.951-.5-5.688-1.448l-6.305 1.654zm6.597-3.807c1.676.995 3.276 1.591 5.392 1.592 5.448 0 9.886-4.434 9.889-9.885.002-5.462-4.415-9.89-9.881-9.892-5.452 0-9.887 4.434-9.889 9.884-.001 2.225.651 3.891 1.746 5.634l-.999 3.648 3.742-.981zm11.387-5.464c-.074-.124-.272-.198-.57-.347-.297-.149-1.758-.868-2.031-.967-.272-.099-.47-.149-.669.149-.198.297-.768.967-.941 1.165-.173.198-.347.223-.644.074-.297-.149-1.255-.462-2.39-1.475-.883-.788-1.48-1.761-1.653-2.059-.173-.297-.018-.458.13-.606.134-.133.297-.347.446-.521.151-.172.2-.296.3-.495.099-.198.05-.372-.025-.521-.075-.148-.669-1.611-.916-2.206-.242-.579-.487-.501-.669-.51l-.57-.01c-.198 0-.52.074-.792.372s-1.04 1.016-1.04 2.479 1.065 2.876 1.213 3.074c.149.198 2.095 3.2 5.076 4.487.709.306 1.263.489 1.694.626.712.226 1.36.194 1.872.118.571-.085 1.758-.719 2.006-1.413.248-.695.248-1.29.173-1.414z"/></svg>

                                WhatsApp ▾

                            </button>

                        </div>

                        <button type="button" class="button-informed" onclick="markVehicleInformed('${escapeWaHtml(dev.vehicleNo)}')" title="Snooze alert until tomorrow">

                            Informed

                        </button>

                        <a href="/Transactions/Create?VehicleNo=${encodeURIComponent(dev.vehicleNo)}" class="button-renew">

                            Collect / Renew

                        </a>

                    </div>

                </div>`;

            });



            html += `

                </div>

            </div>`;

        }



        const isOpenShifts = !!window._alertSectionStates['shifts'];

        if (activeShifts.length > 0) {

            html += `

            <div class="notification-section" data-section-id="shifts">

                <div class="notification-section-divider shift-divider" data-section="shifts" onclick="toggleAlertSection('shifts', this)" onkeydown="onSectionHeaderKeydown(event, 'shifts')" role="button" aria-expanded="${isOpenShifts}" tabindex="0">

                    <span class="section-title">⚡ Recent Lifecycle Shifts (${activeShifts.length})</span>

                    <span class="section-chevron">

                        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"></polyline></svg>

                    </span>

                </div>

                <div class="notification-section-body" id="section-body-shifts" style="display: ${isOpenShifts ? 'block' : 'none'};">`;



            activeShifts.forEach(s => {

                html += `

                <div class="notification-item lifecycle-shift-item" id="shift-item-${escapeWaHtml(s.vehicleNo)}-${escapeWaHtml(s.shiftType)}">

                    <div style="display: flex; justify-content: space-between; align-items: flex-start; gap: 8px;">

                        <div style="flex: 1; min-width: 0;">

                            <div style="font-weight: 700; font-size: 0.88rem; color: var(--text-main); display: flex; align-items: center; gap: 6px; flex-wrap: wrap;">

                                <span>${escapeWaHtml(s.vehicleNo)}</span>

                                <span class="shift-badge shift-${escapeWaHtml(s.shiftType.toLowerCase())}">⚡ ${escapeWaHtml(s.fromStatus)} &rarr; ${escapeWaHtml(s.toStatus)}</span>

                            </div>

                            <div style="font-size: 0.77rem; color: var(--muted); margin-top: 3px;">

                                ${escapeWaHtml(s.description)} &bull; Expired: ${escapeWaHtml(s.expiryStr)} (${s.daysPast}d ago)

                            </div>

                            ${(s.owner || s.vendor) ? `

                            <div style="font-size: 0.73rem; color: var(--muted); margin-top: 2px;">

                                ${escapeWaHtml(s.owner || '')} ${s.vendor ? `(${escapeWaHtml(s.vendor)})` : ''}

                            </div>` : ''}

                        </div>

                        <button type="button" class="btn-dismiss-alert" onclick="dismissLifecycleShift('${escapeWaHtml(s.vehicleNo)}', '${escapeWaHtml(s.shiftType)}')" title="Dismiss notification">&times;</button>

                    </div>

                    <div class="notification-actions" style="margin-top: 8px;">

                        <a href="${escapeWaHtml(s.filterUrl)}" class="button-filter-shift">

                            View ${escapeWaHtml(s.toStatus)} Devices &rarr;

                        </a>

                        ${s.shiftType === 'Suspension' ? `

                        <a href="/Transactions/Create?VehicleNo=${encodeURIComponent(s.vehicleNo)}" class="button-renew">

                            Collect / Renew

                        </a>` : ''}

                    </div>

                </div>`;

            });



            html += `

                </div>

            </div>`;

        }



        const isOpenExpiring = !!window._alertSectionStates['expiring'];

        if (expiringList.length > 0) {

            html += `

            <div class="notification-section" data-section-id="expiring">

                <div class="notification-section-divider expiring-divider" data-section="expiring" onclick="toggleAlertSection('expiring', this)" onkeydown="onSectionHeaderKeydown(event, 'expiring')" role="button" aria-expanded="${isOpenExpiring}" tabindex="0">

                    <span class="section-title">⏰ Expiring This Week (${expiringList.length})</span>

                    <span class="section-chevron">

                        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"></polyline></svg>

                    </span>

                </div>

                <div class="notification-section-body" id="section-body-expiring" style="display: ${isOpenExpiring ? 'block' : 'none'};">`;



            expiringList.forEach(dev => {

                html += `

                <div class="notification-item ${dev.urgencyClass}" id="alert-item-${escapeWaHtml(dev.vehicleNo)}">

                    <div style="display: flex; justify-content: space-between; align-items: flex-start; gap: 8px;">

                        <div>

                            <div style="font-weight: 700; font-size: 0.88rem; color: var(--text-main); display: flex; align-items: center; gap: 6px; flex-wrap: wrap;">

                                <span>${escapeWaHtml(dev.vehicleNo)}</span>

                                <span class="expiry-tag">${escapeWaHtml(dev.urgencyLabel)}</span>

                            </div>

                            <div style="font-size: 0.78rem; color: var(--muted); margin-top: 3px;">

                                ${escapeWaHtml(dev.owner || 'No Owner Assigned')}

                                ${dev.cellNo ? ` &bull; ${escapeWaHtml(dev.cellNo)}` : ''}

                                ${dev.vendor ? ` &bull; ${escapeWaHtml(dev.vendor)}` : ''}

                            </div>

                        </div>

                        <button type="button" class="btn-dismiss-alert" onclick="dismissNotification('${escapeWaHtml(dev.vehicleNo)}')" title="Dismiss notification">&times;</button>

                    </div>

                    <div class="notification-actions">

                        <div class="wa-picker" style="position: relative; display: inline-flex;">

                            <button type="button" class="button-wa-alert"

                                data-owner-url="${escapeWaHtml(dev.waOwnerUrl)}"

                                data-vendor-url="${escapeWaHtml(dev.waVendorUrl)}"

                                data-owner-name="${escapeWaHtml(dev.owner)}"

                                data-cell-no="${escapeWaHtml(dev.cellNo)}"

                                data-vendor-name="${escapeWaHtml(dev.vendor)}"

                                data-vendor-phone="${escapeWaHtml(dev.vendorPhone)}"

                                onclick="openWaPickerPortal(this)" title="Send WhatsApp message">

                                <svg width="13" height="13" viewBox="0 0 24 24" fill="currentColor"><path d="M.057 24l1.687-6.163c-1.041-1.804-1.588-3.849-1.587-5.946.003-6.556 5.338-11.891 11.893-11.891 3.181.001 6.167 1.24 8.413 3.488 2.245 2.248 3.481 5.236 3.48 8.414-.003 6.557-5.338 11.892-11.893 11.892-1.99-.001-3.951-.5-5.688-1.448l-6.305 1.654zm6.597-3.807c1.676.995 3.276 1.591 5.392 1.592 5.448 0 9.886-4.434 9.889-9.885.002-5.462-4.415-9.89-9.881-9.892-5.452 0-9.887 4.434-9.889 9.884-.001 2.225.651 3.891 1.746 5.634l-.999 3.648 3.742-.981zm11.387-5.464c-.074-.124-.272-.198-.57-.347-.297-.149-1.758-.868-2.031-.967-.272-.099-.47-.149-.669.149-.198.297-.768.967-.941 1.165-.173.198-.347.223-.644.074-.297-.149-1.255-.462-2.39-1.475-.883-.788-1.48-1.761-1.653-2.059-.173-.297-.018-.458.13-.606.134-.133.297-.347.446-.521.151-.172.2-.296.3-.495.099-.198.05-.372-.025-.521-.075-.148-.669-1.611-.916-2.206-.242-.579-.487-.501-.669-.51l-.57-.01c-.198 0-.52.074-.792.372s-1.04 1.016-1.04 2.479 1.065 2.876 1.213 3.074c.149.198 2.095 3.2 5.076 4.487.709.306 1.263.489 1.694.626.712.226 1.36.194 1.872.118.571-.085 1.758-.719 2.006-1.413.248-.695.248-1.29.173-1.414z"/></svg>

                                WhatsApp ▾

                            </button>

                        </div>

                        <button type="button" class="button-informed" onclick="markVehicleInformed('${escapeWaHtml(dev.vehicleNo)}')" title="Snooze alert until tomorrow">

                            Informed

                        </button>

                        <a href="/Transactions/Create?VehicleNo=${encodeURIComponent(dev.vehicleNo)}" class="button-renew">

                            Collect / Renew

                        </a>

                    </div>

                </div>`;

            });



            html += `

                </div>

            </div>`;

        }



        const isOpenFollowups = !!window._alertSectionStates['followups'];

        if (followupsList.length > 0) {

            html += `

            <div class="notification-section" data-section-id="followups">

                <div class="notification-section-divider followups-divider" data-section="followups" onclick="toggleAlertSection('followups', this)" onkeydown="onSectionHeaderKeydown(event, 'followups')" role="button" aria-expanded="${isOpenFollowups}" tabindex="0">

                    <span class="section-title">📋 Pending Follow-ups (${followupsList.length})</span>

                    <span class="section-chevron">

                        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"></polyline></svg>

                    </span>

                </div>

                <div class="notification-section-body" id="section-body-followups" style="display: ${isOpenFollowups ? 'block' : 'none'};">`;



            followupsList.forEach(f => {

                html += `

                <div class="notification-item followup-item ${f.urgencyClass}" id="followup-item-${escapeWaHtml(f.id)}">

                    <div style="display: flex; justify-content: space-between; align-items: flex-start; gap: 8px;">

                        <div style="flex: 1; min-width: 0;">

                            <div style="font-weight: 700; font-size: 0.88rem; color: var(--text-main); display: flex; align-items: center; gap: 6px; flex-wrap: wrap;">

                                <span>📋 ${escapeWaHtml(f.title)}</span>

                                <span class="expiry-tag followup-tag">${escapeWaHtml(f.dueLabel)}</span>

                                ${f.targetAudience && f.targetAudience !== '@all' ? `<span class="audience-tag" title="Target Audience: ${escapeWaHtml(f.targetAudience)}">👁️ ${escapeWaHtml(f.targetAudience)}</span>` : ''}

                            </div>

                            ${f.details ? `<div style="font-size: 0.76rem; color: var(--muted); margin-top: 3px; word-break: break-word;">${escapeWaHtml(f.details)}</div>` : ''}

                            <div style="font-size: 0.72rem; color: var(--muted); margin-top: 3px;">Due: ${escapeWaHtml(f.dueDate)}</div>

                        </div>

                        <button type="button" class="btn-dismiss-alert" onclick="dismissFollowupAlert('${escapeWaHtml(f.id)}')" title="Dismiss notification">&times;</button>

                    </div>

                    <div class="notification-actions" style="margin-top: 8px;">

                        ${data.canEditReminders ? `

                        <button type="button" class="btn-complete-followup" onclick="completeFollowupAlert('${escapeWaHtml(f.id)}')">

                            <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" style="margin-right: 4px;"><polyline points="20 6 9 17 4 12"/></svg>

                            Mark Done

                        </button>` : ''}

                        ${data.canViewReminders ? `

                        <a href="/Reminders" class="button-renew" style="font-size: 0.74rem; padding: 4px 9px;">

                            View Reminders &rarr;

                        </a>` : ''}

                    </div>

                </div>`;

            });



            html += `

                </div>

            </div>`;

        }



        const isOpenPendingRc = !!window._alertSectionStates['pending-recharges'];

        if (pendingRcList.length > 0) {

            html += `

            <div class="notification-section" data-section-id="pending-recharges">

                <div class="notification-section-divider pending-recharge-divider" data-section="pending-recharges" onclick="toggleAlertSection('pending-recharges', this)" onkeydown="onSectionHeaderKeydown(event, 'pending-recharges')" role="button" aria-expanded="${isOpenPendingRc}" tabindex="0">

                    <span class="section-title">⚡ Pending Recharges (${pendingRcList.length})</span>

                    <span class="section-chevron">

                        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"></polyline></svg>

                    </span>

                </div>

                <div class="notification-section-body" id="section-body-pending-recharges" style="display: ${isOpenPendingRc ? 'block' : 'none'};">`;



            pendingRcList.forEach(rc => {

                const title = rc.vehicleNo || rc.imei || rc.iccid || 'Device';

                html += `

                <div class="notification-item urgent-today" id="recharge-item-${escapeWaHtml(rc.id)}">

                    <div style="display: flex; justify-content: space-between; align-items: flex-start; gap: 8px;">

                        <div>

                            <div style="font-weight: 700; font-size: 0.88rem; color: var(--text-main); display: flex; align-items: center; gap: 6px; flex-wrap: wrap;">

                                <span>⚡ ${escapeWaHtml(title)}</span>

                                <span class="expiry-tag" style="background: #fef3c7; color: #b45309; border-color: #fcd34d;">PENDING ACTIVATION</span>

                                ${rc.networkOperator ? `<span class="badge" style="font-size: 0.7rem; background: #e0f2fe; color: #0369a1;">${escapeWaHtml(rc.networkOperator)}</span>` : ''}

                            </div>

                            <div style="font-size: 0.78rem; color: var(--muted); margin-top: 3px;">

                                ${escapeWaHtml(rc.vendor || 'Vendor')} &bull; Scheduled: ${escapeWaHtml(rc.rechargeDate || 'Immediate')}

                            </div>

                        </div>

                        <button type="button" class="btn-dismiss-alert" onclick="dismissRechargeAlert('${escapeWaHtml(rc.id)}')" title="Dismiss notification">&times;</button>

                    </div>

                    <div class="notification-actions" style="margin-top: 8px;">

                        <a href="/Recharges" class="button-renew" style="background: #d97706; border-color: #b45309;">

                            Process in Recharges &rarr;

                        </a>

                    </div>

                </div>`;

            });



            html += `

                </div>

            </div>`;

        }



        const isOpenUnpaidRc = !!window._alertSectionStates['unpaid-recharges'];

        if (unpaidRcList.length > 0) {

            html += `

            <div class="notification-section" data-section-id="unpaid-recharges">

                <div class="notification-section-divider unpaid-recharge-divider" data-section="unpaid-recharges" onclick="toggleAlertSection('unpaid-recharges', this)" onkeydown="onSectionHeaderKeydown(event, 'unpaid-recharges')" role="button" aria-expanded="${isOpenUnpaidRc}" tabindex="0">

                    <span class="section-title">💳 Unpaid Recharges (${unpaidRcList.length})</span>

                    <span class="section-chevron">

                        <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"></polyline></svg>

                    </span>

                </div>

                <div class="notification-section-body" id="section-body-unpaid-recharges" style="display: ${isOpenUnpaidRc ? 'block' : 'none'};">`;



            unpaidRcList.forEach(rc => {

                const title = rc.vehicleNo || rc.imei || rc.iccid || 'Device';

                html += `

                <div class="notification-item urgent-overdue" id="recharge-item-${escapeWaHtml(rc.id)}">

                    <div style="display: flex; justify-content: space-between; align-items: flex-start; gap: 8px;">

                        <div>

                            <div style="font-weight: 700; font-size: 0.88rem; color: var(--text-main); display: flex; align-items: center; gap: 6px; flex-wrap: wrap;">

                                <span>💳 ${escapeWaHtml(title)}</span>

                                <span class="expiry-tag" style="background: #fee2e2; color: #dc2626; border-color: #fca5a5;">PAYMENT DUE</span>

                                ${rc.networkOperator ? `<span class="badge" style="font-size: 0.7rem; background: #e0f2fe; color: #0369a1;">${escapeWaHtml(rc.networkOperator)}</span>` : ''}

                            </div>

                            <div style="font-size: 0.78rem; color: var(--muted); margin-top: 3px;">

                                ${escapeWaHtml(rc.vendor || 'Vendor')} &bull; Processed: ${escapeWaHtml(rc.rechargeDate || '')}

                            </div>

                        </div>

                        <button type="button" class="btn-dismiss-alert" onclick="dismissRechargeAlert('${escapeWaHtml(rc.id)}')" title="Dismiss notification">&times;</button>

                    </div>

                    <div class="notification-actions" style="margin-top: 8px;">

                        <div class="wa-picker" style="position: relative; display: inline-flex;">

                            <button type="button" class="button-wa-alert"

                                data-owner-url="${escapeWaHtml(rc.waOwnerUrl || '')}"

                                data-vendor-url="${escapeWaHtml(rc.waVendorUrl || '')}"

                                data-owner-name="${escapeWaHtml(rc.owner || '')}"

                                data-cell-no="${escapeWaHtml(rc.cellNo || '')}"

                                data-vendor-name="${escapeWaHtml(rc.vendor || '')}"

                                data-vendor-phone="${escapeWaHtml(rc.vendorPhone || '')}"

                                onclick="openWaPickerPortal(this)" title="Send WhatsApp message">

                                <svg width="13" height="13" viewBox="0 0 24 24" fill="currentColor"><path d="M.057 24l1.687-6.163c-1.041-1.804-1.588-3.849-1.587-5.946.003-6.556 5.338-11.891 11.893-11.891 3.181.001 6.167 1.24 8.413 3.488 2.245 2.248 3.481 5.236 3.48 8.414-.003 6.557-5.338 11.892-11.893 11.892-1.99-.001-3.951-.5-5.688-1.448l-6.305 1.654zm6.597-3.807c1.676.995 3.276 1.591 5.392 1.592 5.448 0 9.886-4.434 9.889-9.885.002-5.462-4.415-9.89-9.881-9.892-5.452 0-9.887 4.434-9.889 9.884-.001 2.225.651 3.891 1.746 5.634l-.999 3.648 3.742-.981zm11.387-5.464c-.074-.124-.272-.198-.57-.347-.297-.149-1.758-.868-2.031-.967-.272-.099-.47-.149-.669.149-.198.297-.768.967-.941 1.165-.173.198-.347.223-.644.074-.297-.149-1.255-.462-2.39-1.475-.883-.788-1.48-1.761-1.653-2.059-.173-.297-.018-.458.13-.606.134-.133.297-.347.446-.521.151-.172.2-.296.3-.495.099-.198.05-.372-.025-.521-.075-.148-.669-1.611-.916-2.206-.242-.579-.487-.501-.669-.51l-.57-.01c-.198 0-.52.074-.792.372s-1.04 1.016-1.04 2.479 1.065 2.876 1.213 3.074c.149.198 2.095 3.2 5.076 4.487.709.306 1.263.489 1.694.626.712.226 1.36.194 1.872.118.571-.085 1.758-.719 2.006-1.413.248-.695.248-1.29.173-1.414z"/></svg>

                                WhatsApp ▾

                            </button>

                        </div>

                        ${rc.vehicleNo ? `

                        <a href="/Transactions/Create?VehicleNo=${encodeURIComponent(rc.vehicleNo)}&PaymentFor=None" class="button-renew">

                            Collect Payment

                        </a>` : ''}

                        <a href="/Recharges" class="btn-micro-archive" style="font-size: 0.74rem; text-decoration: none; padding: 4px 8px; border-radius: 4px; border: 1px solid var(--border); color: var(--text-main);">

                            View in Recharges &rarr;

                        </a>

                    </div>

                </div>`;

            });



            html += `

                </div>

            </div>`;

        }



        list.innerHTML = html;



        const alertSearchInp = document.getElementById('alertSearchInput');

        if (alertSearchInp && alertSearchInp.value.trim()) {

            filterAlerts(alertSearchInp.value);

        }

    }



    // Periodic live synchronization every 8 seconds

    setInterval(pollAlertsNow, 8000);

    window.addEventListener('focus', pollAlertsNow);

    const notifToggle = document.getElementById('notification-toggle');

    if (notifToggle) {

        notifToggle.addEventListener('change', function() {

            if (this.checked) {

                pollAlertsNow();

                setTimeout(() => {

                    const searchInp = document.getElementById('alertSearchInput');

                    if (searchInp) searchInp.focus();

                }, 50);

            }

        });

    }



    // Remove preload class after page renders to re-enable transitions

    document.addEventListener("DOMContentLoaded", function() {

        setTimeout(() => document.body.classList.remove('preload'), 50);

        

        // Back to top logic

        const backToTop = document.getElementById("backToTop");

        if (backToTop) {

            window.addEventListener("scroll", () => {

                if (window.scrollY > 300) {

                    backToTop.classList.add("visible");

                } else {

                    backToTop.classList.remove("visible");

                }

            });

            backToTop.addEventListener("click", () => {

                window.scrollTo({ top: 0, behavior: "smooth" });

            });

        }



        // Notification drawer & User dropdown click-outside auto-close

        document.addEventListener('click', function(e) {

            const notifCenter = document.querySelector('.notification-center');

            const notifToggle = document.getElementById('notification-toggle');

            if (notifToggle && notifToggle.checked && notifCenter && !notifCenter.contains(e.target)) {

                notifToggle.checked = false;

            }



            const userDropdown = document.querySelector('.user-profile-dropdown');

            const userToggle = document.getElementById('user-profile-toggle');

            if (userToggle && userToggle.checked && userDropdown && !userDropdown.contains(e.target)) {

                userToggle.checked = false;

            }

        });

        // ── Data-security: block browser shortcuts that could expose sensitive data ──

        (function () {

            // Shortcuts to block: key => label shown in toast

            const BLOCKED = {

                's':       { ctrl: true,  shift: false, label: 'Save Page (Ctrl+S)'        },

                'p':       { ctrl: true,  shift: false, label: 'Print (Ctrl+P)'             },

                'u':       { ctrl: true,  shift: false, label: 'View Source (Ctrl+U)'       },

                'F12':     { ctrl: false, shift: false, label: 'DevTools (F12)'             },

                'i':       { ctrl: true,  shift: true,  label: 'DevTools (Ctrl+Shift+I)'   },

                'j':       { ctrl: true,  shift: true,  label: 'Console (Ctrl+Shift+J)'    },

                'c':       { ctrl: true,  shift: true,  label: 'Inspect (Ctrl+Shift+C)'    },

            };



            // Toast element (created once, reused)

            let _toast = null;

            let _toastTimer = null;

            function showSecurityToast(label) {

                if (!_toast) {

                    _toast = document.createElement('div');

                    _toast.style.cssText = [

                        'position:fixed', 'bottom:24px', 'left:50%', 'transform:translateX(-50%) translateY(12px)',

                        'background:#1e293b', 'color:#f1f5f9', 'font-size:13px', 'font-family:inherit',

                        'padding:10px 20px', 'border-radius:8px', 'box-shadow:0 4px 20px rgba(0,0,0,0.35)',

                        'z-index:99999', 'pointer-events:none', 'opacity:0',

                        'transition:opacity 0.2s ease, transform 0.2s ease',

                        'display:flex', 'align-items:center', 'gap:10px', 'white-space:nowrap'

                    ].join(';');

                    document.body.appendChild(_toast);

                }

                _toast.innerHTML = '<svg width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" viewBox="0 0 24 24"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>'

                               + '<span><strong>' + label + '</strong> is disabled for data security.</span>';

                _toast.style.opacity  = '1';

                _toast.style.transform = 'translateX(-50%) translateY(0)';

                clearTimeout(_toastTimer);

                _toastTimer = setTimeout(function () {

                    _toast.style.opacity  = '0';

                    _toast.style.transform = 'translateX(-50%) translateY(12px)';

                }, 2500);

            }



            document.addEventListener('keydown', function (e) {

                var key = e.key;

                for (var k in BLOCKED) {

                    var rule = BLOCKED[k];

                    var keyMatch   = (key === k) || (key.toLowerCase() === k.toLowerCase());

                    var ctrlMatch  = rule.ctrl  ? (e.ctrlKey || e.metaKey) : true;

                    var shiftMatch = rule.shift ? e.shiftKey : (rule.shift === false ? !e.shiftKey || rule.ctrl === false : true);



                    // Simplified exact match

                    var ctrl  = e.ctrlKey || e.metaKey;

                    var shift = e.shiftKey;

                    var match = false;



                    if (k === 'F12')  match = key === 'F12';

                    else if (rule.shift) match = ctrl && shift && key.toLowerCase() === k;

                    else               match = ctrl && !shift && key.toLowerCase() === k;



                    if (match) {

                        e.preventDefault();

                        e.stopPropagation();

                        showSecurityToast(rule.label);

                        return false;

                    }

                }

            }, true); // capture phase so we intercept before browser acts

        })();



        // Floating / Frozen Bottom Horizontal Scrollbar for Wide Data Tables

        (function setupFloatingHorizontalScrollbar() {

            var bar = document.getElementById('floatingHScrollBar');

            if (!bar) {

                bar = document.createElement('div');

                bar.id = 'floatingHScrollBar';

                bar.className = 'floating-hscrollbar';

                bar.setAttribute('aria-hidden', 'true');

                bar.innerHTML = '<div id="floatingHScrollInner" class="floating-hscrollbar-inner"></div>';

                document.body.appendChild(bar);

            }

            var inner = bar.firstElementChild;



            var activeTarget = null;

            var isSyncingToTarget = false;

            var isSyncingToBar = false;



            bar.addEventListener('scroll', function () {

                if (!activeTarget || isSyncingToBar) return;

                isSyncingToTarget = true;

                activeTarget.scrollLeft = bar.scrollLeft;

                requestAnimationFrame(function () {

                    isSyncingToTarget = false;

                });

            }, { passive: true });



            function onTargetScroll(e) {

                if (e.target !== activeTarget || isSyncingToTarget) return;

                isSyncingToBar = true;

                bar.scrollLeft = activeTarget.scrollLeft;

                requestAnimationFrame(function () {

                    isSyncingToBar = false;

                });

            }



            function updateScrollbar() {

                var candidates = document.querySelectorAll('.table-wrap, [style*="overflow-x"], .vendors-table-card > div, .dsh-table-container > div');

                var bestTarget = null;

                var maxVisibleHeight = 0;

                var vh = window.innerHeight;

                var vw = window.innerWidth;



                for (var i = 0; i < candidates.length; i++) {

                    var el = candidates[i];

                    if (el.offsetWidth === 0 || el.offsetHeight === 0) continue;

                    if (el.scrollWidth <= el.clientWidth + 2) continue;



                    var rect = el.getBoundingClientRect();

                    // Must be in viewport vertically

                    if (rect.bottom <= 40 || rect.top >= vh) continue;



                    // Natural scrollbar of table is below viewport fold

                    if (rect.bottom <= vh) continue;

                    if (rect.top >= vh - 20) continue;



                    var visibleTop = Math.max(0, rect.top);

                    var visibleBottom = Math.min(vh, rect.bottom);

                    var visibleHeight = visibleBottom - visibleTop;



                    if (visibleHeight > maxVisibleHeight) {

                        maxVisibleHeight = visibleHeight;

                        bestTarget = el;

                    }

                }



                if (bestTarget) {

                    if (activeTarget !== bestTarget) {

                        if (activeTarget) {

                            activeTarget.removeEventListener('scroll', onTargetScroll);

                        }

                        activeTarget = bestTarget;

                        activeTarget.addEventListener('scroll', onTargetScroll, { passive: true });

                    }



                    var rect = activeTarget.getBoundingClientRect();

                    var sidebar = document.querySelector('.sidebar');

                    var sidebarRight = sidebar ? sidebar.getBoundingClientRect().right : 0;

                    var leftPos = Math.max(rect.left, sidebarRight, 0);

                    var rightPos = Math.min(rect.right, vw);

                    var width = Math.max(0, rightPos - leftPos);



                    bar.style.left = leftPos + 'px';

                    bar.style.width = width + 'px';

                    inner.style.width = activeTarget.scrollWidth + 'px';



                    if (!isSyncingToTarget) {

                        bar.scrollLeft = activeTarget.scrollLeft;

                    }

                    bar.classList.add('visible');

                } else {

                    if (activeTarget) {

                        activeTarget.removeEventListener('scroll', onTargetScroll);

                        activeTarget = null;

                    }

                    bar.classList.remove('visible');

                }

            }



            var ticking = false;

            function requestUpdate() {

                if (!ticking) {

                    requestAnimationFrame(function () {

                        updateScrollbar();

                        ticking = false;

                    });

                    ticking = true;

                }

            }



            window.addEventListener('scroll', requestUpdate, { passive: true });

            window.addEventListener('resize', requestUpdate, { passive: true });



            // Removed body mutation observer to prevent constant reflows



            setInterval(requestUpdate, 1000);

            requestUpdate();

        })();



    }); // end DOMContentLoaded

