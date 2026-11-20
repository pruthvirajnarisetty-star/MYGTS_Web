/**
 * MYGTS Custom Dropdown Manager
 * Enables dynamic custom options on dropdown fields across the application,
 * persisted to the database per branch without requiring code changes in an IDE.
 */
const CustomDropdownManager = (() => {
    let _cachedOptions = {};
    let _activeCategory = null;
    let _activeTitle = null;
    let _activeTriggerSelect = null;
    let _isInitialized = false;

    // Standard category registry & friendly names
    const CATEGORY_MAP = {
        'devicename': { category: 'DeviceName', title: 'Device Name' },
        'devicetype': { category: 'DeviceName', title: 'Device Type' },
        'singlemodel.devicetype': { category: 'DeviceName', title: 'Device Type' },
        'multiplemodel.devicetype': { category: 'DeviceName', title: 'Device Type' },
        'networkoperator': { category: 'NetworkOperator', title: 'Network Operator' },
        'singlemodel.networkoperator': { category: 'NetworkOperator', title: 'Network Operator' },
        'multiplemodel.networkoperator': { category: 'NetworkOperator', title: 'Network Operator' },
        'aoperator': { category: 'NetworkOperator', title: 'Network Operator' },
        'eoperator': { category: 'NetworkOperator', title: 'Network Operator' },
        'paymentmode': { category: 'PaymentMode', title: 'Payment Mode' },
        'paymentfor': { category: 'PaymentFor', title: 'Payment For' },
        'extras': { category: 'Extras', title: 'Extras' },
        'proofofaddress': { category: 'ProofOfAddress', title: 'Proof of Address' },
        'lifecyclestatus': { category: 'LifecycleStatus', title: 'Lifecycle Status' },
        'lifecycle': { category: 'LifecycleStatus', title: 'Lifecycle Status' },
        'cdaccertificate': { category: 'CdacCertificate', title: 'CDAC Certificate' },
        'vldtcertificate': { category: 'VldtCertificate', title: 'VLDT Certificate' },
        'actiontype': { category: 'CompanyRechargeActionType', title: 'Action Type' },
        'bulkdefaulttype': { category: 'CompanyRechargeActionType', title: 'Action Type' },
        'type-select': { category: 'TransactionType', title: 'Transaction Type' }
    };

    function resolveCategory(selectEl) {
        // 1. Explicit data-custom-dropdown attribute
        if (selectEl.dataset.customDropdown) {
            const cat = selectEl.dataset.customDropdown;
            const title = selectEl.dataset.customTitle || cat;
            return { category: cat, title: title };
        }

        // 2. Resolve by name or ID
        const rawName = (selectEl.name || selectEl.id || '').toLowerCase();
        
        // Strip array indices like BulkAllocations[0].Extras -> extras
        const cleanName = rawName.replace(/^.*[\.\[\]_]/, '').toLowerCase();
        
        if (CATEGORY_MAP[rawName]) return CATEGORY_MAP[rawName];
        if (CATEGORY_MAP[cleanName]) return CATEGORY_MAP[cleanName];

        // Specific partial matches
        if (rawName.includes('devicename') || rawName.includes('devicetype')) {
            return { category: 'DeviceName', title: 'Device Name' };
        }
        if (rawName.includes('operator')) {
            return { category: 'NetworkOperator', title: 'Network Operator' };
        }
        if (rawName.includes('paymentmode')) {
            return { category: 'PaymentMode', title: 'Payment Mode' };
        }
        if (rawName.includes('paymentfor')) {
            return { category: 'PaymentFor', title: 'Payment For' };
        }
        if (rawName.includes('extras')) {
            return { category: 'Extras', title: 'Extras' };
        }
        if (rawName.includes('proofofaddress')) {
            return { category: 'ProofOfAddress', title: 'Proof of Address' };
        }
        if (rawName.includes('lifecycle')) {
            return { category: 'LifecycleStatus', title: 'Lifecycle Status' };
        }
        if (rawName.includes('certificate')) {
            if (rawName.includes('cdac')) return { category: 'CdacCertificate', title: 'CDAC Certificate' };
            if (rawName.includes('vldt')) return { category: 'VldtCertificate', title: 'VLDT Certificate' };
        }

        return null;
    }

    async function loadOptions() {
        try {
            const branchMatch = (document.cookie.match(/mygts_branch=([^;]+)/) || [])[1];
            const branch = branchMatch ? decodeURIComponent(branchMatch) : 'DMG';
            let res = await fetch('/DropdownOptions/GetAll', { credentials: 'same-origin' });
            if (!res.ok) {
                res = await fetch(`/${branch}/DropdownOptions/GetAll`, { credentials: 'same-origin' });
            }
            if (!res.ok) return;
            const contentType = res.headers.get('content-type') || '';
            if (!contentType.includes('application/json')) return;
            const data = await res.json();
            if (data && data.success && data.options) {
                _cachedOptions = data.options;
            }
        } catch (err) {
            console.error('Failed to load custom dropdown options:', err);
        }
    }

    function enhanceSelect(selectEl) {
        if (!selectEl || selectEl.dataset.customEnhanced === 'true') return;
        
        // Skip filter / sort / pagination dropdowns and security Role dropdowns
        if (selectEl.id === 'PageSize' || selectEl.name === 'PageSize' || 
            selectEl.id === 'vendorSortSelect' || selectEl.id === 'filterStatus' || 
            selectEl.id === 'filterPayment' || selectEl.id === 'filterOperator' ||
            selectEl.id === 'Role' || selectEl.name === 'Role' || selectEl.id === 'RoleSelect' ||
            selectEl.classList.contains('conflict-select') || selectEl.classList.contains('conflict-action-select')) {
            return;
        }

        const info = resolveCategory(selectEl);
        if (!info) return;

        selectEl.dataset.customDropdown = info.category;
        selectEl.dataset.customTitle = info.title;
        selectEl.dataset.customEnhanced = 'true';

        // Remember existing value
        const initialVal = selectEl.value;

        // Remember previous valid value for reverting if user cancels
        selectEl.dataset.prevVal = initialVal;

        // Collect existing options to avoid duplicate injection
        const existingValues = new Set();
        Array.from(selectEl.options).forEach(opt => {
            if (opt.value !== '__ADD_NEW__') {
                existingValues.add(opt.value.trim().toLowerCase());
            }
        });

        // Inject custom options from cache
        const customItems = _cachedOptions[info.category] || [];
        customItems.forEach(item => {
            if (!existingValues.has(item.value.trim().toLowerCase())) {
                const opt = document.createElement('option');
                opt.value = item.value;
                opt.textContent = item.value;
                opt.dataset.customId = item.id;
                selectEl.appendChild(opt);
                existingValues.add(item.value.trim().toLowerCase());
            }
        });

        // Ensure bottom "+ Add Custom Option..." entry exists
        let addOpt = selectEl.querySelector('option[value="__ADD_NEW__"]');
        if (!addOpt) {
            addOpt = document.createElement('option');
            addOpt.value = '__ADD_NEW__';
            addOpt.className = 'add-custom-option-choice';
            addOpt.textContent = '+ Add Custom Option...';
            selectEl.appendChild(addOpt);
        } else {
            // Keep it at the very bottom and ensure uniform label
            addOpt.textContent = '+ Add Custom Option...';
            selectEl.appendChild(addOpt);
        }

        // Restore initial selection if it was set
        if (initialVal) {
            selectEl.value = initialVal;
        }

        // Intercept selection of '__ADD_NEW__'
        selectEl.addEventListener('change', (e) => {
            if (selectEl.value === '__ADD_NEW__') {
                // Revert to previous value
                selectEl.value = selectEl.dataset.prevVal || '';
                openModal(info.category, info.title, selectEl);
            } else {
                selectEl.dataset.prevVal = selectEl.value;
            }
        });

        // Clean up any previously attached inline buttons/wrappers if present
        const parent = selectEl.parentElement;
        if (parent && parent.classList.contains('custom-dropdown-wrap')) {
            const btn = parent.querySelector('.btn-custom-dropdown-add');
            if (btn) btn.remove();
            parent.replaceWith(selectEl);
        }
    }

    function openModal(category, title, triggerSelect) {
        _activeCategory = category;
        _activeTitle = title;
        _activeTriggerSelect = triggerSelect;

        const overlay = document.getElementById('customDropdownModalOverlay');
        const titleEl = document.getElementById('customDropdownModalTitle');
        const subtitleEl = document.getElementById('customDropdownModalSubtitle');
        const inputEl = document.getElementById('customDropdownNewInput');
        const alertEl = document.getElementById('customDropdownAlert');

        if (!overlay) return;

        titleEl.textContent = `Manage Options: ${title}`;
        subtitleEl.textContent = `Options added here are saved to the database and available to all users.`;
        inputEl.value = '';
        alertEl.style.display = 'none';

        renderModalCustomList();
        renderModalDefaults();

        overlay.style.display = 'flex';
        setTimeout(() => inputEl.focus(), 100);
    }

    function closeModal() {
        const overlay = document.getElementById('customDropdownModalOverlay');
        if (overlay) {
            overlay.style.display = 'none';
        }
        _activeCategory = null;
        _activeTitle = null;
        _activeTriggerSelect = null;
    }

    function handleOverlayClick(e) {
        if (e.target && e.target.id === 'customDropdownModalOverlay') {
            closeModal();
        }
    }

    function renderModalCustomList() {
        const listEl = document.getElementById('customDropdownExistingList');
        const badgeEl = document.getElementById('customDropdownCountBadge');
        if (!listEl) return;

        const items = _cachedOptions[_activeCategory] || [];
        badgeEl.textContent = `${items.length} custom`;

        if (items.length === 0) {
            listEl.innerHTML = `<div style="padding: 12px; text-align: center; color: var(--muted, #64748b); font-size: 0.82rem;">No custom options added yet for this field.</div>`;
            return;
        }

        listEl.innerHTML = '';
        items.forEach(item => {
            const row = document.createElement('div');
            row.className = 'custom-dropdown-item-row';
            row.innerHTML = `
                <span>${escapeHtml(item.value)}</span>
                <button type="button" class="custom-dropdown-item-delete" title="Delete this option" onclick="CustomDropdownManager.deleteOption('${item.id}', '${escapeHtml(item.value)}')">
                    <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                        <polyline points="3 6 5 6 21 6"></polyline>
                        <path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path>
                    </svg>
                </button>
            `;
            listEl.appendChild(row);
        });
    }

    function renderModalDefaults() {
        const badgesContainer = document.getElementById('customDropdownDefaultsBadges');
        if (!badgesContainer) return;

        badgesContainer.innerHTML = '';

        if (!_activeTriggerSelect) return;

        const defaults = [];
        Array.from(_activeTriggerSelect.options).forEach(opt => {
            if (opt.value && opt.value !== '__ADD_NEW__' && !opt.dataset.customId) {
                defaults.push(opt.textContent.trim());
            }
        });

        if (defaults.length === 0) {
            badgesContainer.innerHTML = `<span style="font-size: 0.75rem; color: var(--muted, #64748b);">None</span>`;
            return;
        }

        defaults.forEach(d => {
            const chip = document.createElement('span');
            chip.style.cssText = 'font-size: 0.72rem; padding: 2px 7px; background: rgba(0,0,0,0.05); border-radius: 4px; color: var(--ink, #334155); font-weight: 500;';
            chip.textContent = d;
            badgesContainer.appendChild(chip);
        });
    }

    async function submitNewOption(e) {
        if (e) e.preventDefault();

        const inputEl = document.getElementById('customDropdownNewInput');
        const alertEl = document.getElementById('customDropdownAlert');
        const submitBtn = document.getElementById('customDropdownSubmitBtn');

        const val = inputEl.value.trim();
        if (!val) return;

        submitBtn.disabled = true;
        submitBtn.textContent = 'Adding...';
        alertEl.style.display = 'none';

        try {
            const res = await fetch('/DropdownOptions/Add', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'same-origin',
                body: JSON.stringify({ category: _activeCategory, value: val })
            });

            const data = await res.json();

            if (!res.ok || !data.success) {
                showAlert(data.message || 'Failed to add option.', 'error');
                submitBtn.disabled = false;
                submitBtn.textContent = '+ Add Option';
                return;
            }

            // Success! Add to cache
            if (!_cachedOptions[_activeCategory]) {
                _cachedOptions[_activeCategory] = [];
            }
            _cachedOptions[_activeCategory].push({ id: data.id, value: data.value });

            // Propagate new option to all matching selects on this page
            syncOptionToPageSelects(_activeCategory, data.id, data.value);

            // Select this option on trigger select
            if (_activeTriggerSelect) {
                _activeTriggerSelect.value = data.value;
                _activeTriggerSelect.dataset.prevVal = data.value;
                _activeTriggerSelect.dispatchEvent(new Event('change', { bubbles: true }));
                _activeTriggerSelect.dispatchEvent(new Event('input', { bubbles: true }));
            }

            showAlert(`Option "${data.value}" added successfully!`, 'success');
            renderModalCustomList();
            inputEl.value = '';

            setTimeout(() => {
                closeModal();
            }, 700);

        } catch (err) {
            showAlert('Network error occurred while saving option.', 'error');
        } finally {
            submitBtn.disabled = false;
            submitBtn.textContent = '+ Add Option';
        }
    }

    async function deleteOption(id, val) {
        if (!confirm(`Are you sure you want to remove the option "${val}"?`)) return;

        try {
            const res = await fetch('/DropdownOptions/Delete', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                credentials: 'same-origin',
                body: JSON.stringify({ id: id })
            });

            const data = await res.json();
            if (res.ok && data.success) {
                // Remove from cache
                if (_cachedOptions[_activeCategory]) {
                    _cachedOptions[_activeCategory] = _cachedOptions[_activeCategory].filter(x => x.id !== id);
                }

                // Remove from page selects
                removeOptionFromPageSelects(_activeCategory, id, val);
                renderModalCustomList();
                showAlert(`Option "${val}" removed.`, 'success');
            } else {
                showAlert(data.message || 'Failed to delete option.', 'error');
            }
        } catch (err) {
            showAlert('Network error occurred while deleting option.', 'error');
        }
    }

    function syncOptionToPageSelects(category, id, value) {
        const selects = document.querySelectorAll(`select[data-custom-dropdown="${category}"]`);
        selects.forEach(sel => {
            // Check if already in options
            let exists = Array.from(sel.options).some(o => o.value.toLowerCase() === value.toLowerCase() && o.value !== '__ADD_NEW__');
            if (!exists) {
                const opt = document.createElement('option');
                opt.value = value;
                opt.textContent = value;
                opt.dataset.customId = id;

                // Insert right before the '__ADD_NEW__' option
                const addNewOpt = sel.querySelector('option[value="__ADD_NEW__"]');
                if (addNewOpt) {
                    sel.insertBefore(opt, addNewOpt);
                } else {
                    sel.appendChild(opt);
                }
            }
        });
    }

    function removeOptionFromPageSelects(category, id, value) {
        const selects = document.querySelectorAll(`select[data-custom-dropdown="${category}"]`);
        selects.forEach(sel => {
            Array.from(sel.options).forEach(opt => {
                if (opt.dataset.customId === id || opt.value.toLowerCase() === value.toLowerCase()) {
                    if (sel.value === opt.value) {
                        sel.value = '';
                        sel.dataset.prevVal = '';
                        sel.dispatchEvent(new Event('change', { bubbles: true }));
                    }
                    opt.remove();
                }
            });
        });
    }

    function showAlert(msg, type) {
        const alertEl = document.getElementById('customDropdownAlert');
        if (!alertEl) return;
        alertEl.style.display = 'block';
        if (type === 'success') {
            alertEl.style.background = '#ecfdf5';
            alertEl.style.color = '#065f46';
            alertEl.style.border = '1px solid #a7f3d0';
        } else {
            alertEl.style.background = '#fef2f2';
            alertEl.style.color = '#991b1b';
            alertEl.style.border = '1px solid #fecaca';
        }
        alertEl.textContent = msg;
    }

    function escapeHtml(str) {
        if (!str) return '';
        return String(str)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    function scanAndEnhanceAll() {
        document.querySelectorAll('select').forEach(enhanceSelect);
    }

    async function init() {
        if (_isInitialized) {
            scanAndEnhanceAll();
            return;
        }
        _isInitialized = true;

        await loadOptions();
        scanAndEnhanceAll();

        // Observe DOM for dynamically inserted elements (like new rows in Bulk Allocations)
        const observer = new MutationObserver((mutations) => {
            let foundNewSelect = false;
            mutations.forEach(m => {
                m.addedNodes.forEach(node => {
                    if (node.nodeType === Node.ELEMENT_NODE) {
                        if (node.tagName === 'SELECT') {
                            enhanceSelect(node);
                        } else if (node.querySelectorAll) {
                            node.querySelectorAll('select').forEach(sel => {
                                enhanceSelect(sel);
                            });
                        }
                    }
                });
            });
        });

        observer.observe(document.body, { childList: true, subtree: true });
    }

    return {
        init,
        enhanceSelect,
        refresh: scanAndEnhanceAll,
        openModal,
        closeModal,
        handleOverlayClick,
        submitNewOption,
        deleteOption
    };
})();

// Auto-initialize on DOM ready
if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', () => CustomDropdownManager.init());
} else {
    CustomDropdownManager.init();
}
