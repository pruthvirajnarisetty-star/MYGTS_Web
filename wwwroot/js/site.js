// Global formatCurrency utility for MYGTS
window.formatCurrency = function(v) {
  const num = Number(v) || 0;
  return '₹' + num.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
};

(() => {
  const toggle = document.querySelector('[data-menu-toggle]');
  const sidebar = document.querySelector('.sidebar');
  const appShell = document.getElementById('appShell');
  const sidebarToggle = document.getElementById('sidebarToggle');

  toggle?.addEventListener('click', () => sidebar?.classList.toggle('open'));

  sidebarToggle?.addEventListener('click', () => {
    const isCollapsed = sidebar?.classList.toggle('collapsed');
    appShell?.classList.toggle('collapsed-sidebar', isCollapsed);
    localStorage.setItem('sidebar_collapsed', isCollapsed);
  });

  document.querySelectorAll('[data-auto-dismiss]').forEach((element) => {
    window.setTimeout(() => element.remove(), 5000);
  });

  const quantity = document.querySelector('[data-invoice-quantity]');
  const rate = document.querySelector('[data-invoice-rate]');
  const courier = document.querySelector('[data-invoice-courier]');
  const amount = document.querySelector('[data-invoice-amount]');
  const total = document.querySelector('[data-invoice-total]');
  const perDevice = document.querySelector('[data-invoice-per-device]');
  const calculateInvoice = () => {
    const q = Number(quantity?.value || 0);
    const r = Number(rate?.value || 0);
    const c = Number(courier?.value || 0);
    const a = q * r;
    const t = a * 1.18 + c;
    if (amount) amount.textContent = a.toLocaleString(undefined, { style: 'currency', currency: 'INR', minimumFractionDigits: 2, maximumFractionDigits: 2 });
    if (total) total.textContent = t.toLocaleString(undefined, { style: 'currency', currency: 'INR', minimumFractionDigits: 2, maximumFractionDigits: 2 });
    if (perDevice) perDevice.textContent = (q ? t / q : 0).toLocaleString(undefined, { style: 'currency', currency: 'INR', minimumFractionDigits: 2, maximumFractionDigits: 2 });
  };
  [quantity, rate, courier].forEach((field) => field?.addEventListener('input', calculateInvoice));
  calculateInvoice();
})();

// ── Search-term highlighting ──────────────────────────────────────────────────
// Walks text nodes inside `container` (CSS selector or Element) and wraps every
// occurrence of each term in <mark class="search-highlight">.
// Returns the total number of matches found.
function highlightQueryTerms(container, terms) {
  if (!terms || terms.length === 0) return 0;

  const root = typeof container === 'string'
    ? document.querySelector(container)
    : container;
  if (!root) return 0;

  // Build a single alternation regex from all non-empty terms (escaped)
  const escaped = terms
    .map(t => t.trim())
    .filter(t => t.length > 0)
    .map(t => t.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'));
  if (escaped.length === 0) return 0;

  const regex = new RegExp(`(${escaped.join('|')})`, 'gi');
  let matchCount = 0;

  // Collect text nodes first (avoid modifying the DOM while iterating)
  const textNodes = [];
  const walker = document.createTreeWalker(root, NodeFilter.SHOW_TEXT, {
    acceptNode(node) {
      // Skip nodes inside existing marks or script/style
      const parent = node.parentElement;
      if (!parent) return NodeFilter.FILTER_REJECT;
      const tag = parent.tagName;
      if (tag === 'SCRIPT' || tag === 'STYLE' || tag === 'MARK') return NodeFilter.FILTER_REJECT;
      if (node.nodeValue.trim() === '') return NodeFilter.FILTER_SKIP;
      return NodeFilter.FILTER_ACCEPT;
    }
  });
  let n;
  while ((n = walker.nextNode())) textNodes.push(n);

  textNodes.forEach(node => {
    const text = node.nodeValue;
    if (!regex.test(text)) return;
    regex.lastIndex = 0;

    const frag = document.createDocumentFragment();
    let last = 0, match;
    while ((match = regex.exec(text)) !== null) {
      if (match.index > last) frag.appendChild(document.createTextNode(text.slice(last, match.index)));
      const mark = document.createElement('mark');
      mark.className = 'search-highlight';
      mark.textContent = match[0];
      frag.appendChild(mark);
      matchCount++;
      last = regex.lastIndex;
    }
    if (last < text.length) frag.appendChild(document.createTextNode(text.slice(last)));
    node.parentNode.replaceChild(frag, node);
  });

  return matchCount;
}

// Removes all <mark class="search-highlight"> wrappers, restoring original text.
function clearSearchHighlights(container) {
  const root = typeof container === 'string'
    ? document.querySelector(container)
    : container;
  if (!root) return;
  root.querySelectorAll('mark.search-highlight').forEach(mark => {
    const text = document.createTextNode(mark.textContent);
    mark.parentNode.replaceChild(text, mark);
  });
  // Normalize adjacent text nodes
  root.normalize();
}

// Auto-trigger: if the page has a hidden #Query input with a value,
// highlight those terms in the first .data-table tbody on the page.
// Also initialises live/dynamic search for pages that have #QueryInput.
document.addEventListener('DOMContentLoaded', () => {
  const queryHidden  = document.getElementById('Query');
  const queryInput   = document.getElementById('QueryInput');
  const toolbar      = document.querySelector('form.toolbar');

  // ── Highlight matching terms on page load ──────────────────────────────────
  if (queryHidden && queryHidden.value.trim()) {
    const terms = queryHidden.value.split('|').map(t => t.trim()).filter(Boolean);
    if (terms.length > 0) {
      const tbody = document.querySelector('.data-table tbody');
      if (tbody) highlightQueryTerms(tbody, terms);
    }
  }

  // ── Live / dynamic search ──────────────────────────────────────────────────
  if (!queryInput || !toolbar) return;

  // Pre-populate the visible input when there is exactly one chip so typing
  // feels continuous (no need to retype the previous term from scratch).
  if (queryHidden && queryHidden.value && !queryHidden.value.includes('|')) {
    queryInput.value = queryHidden.value;
  }

  // Restore focus and cursor position if the user had active input during a page navigation
  if (sessionStorage.getItem('query_input_active') === '1') {
    sessionStorage.removeItem('query_input_active');
    try {
      queryInput.focus();
      const len = queryInput.value.length;
      queryInput.setSelectionRange(len, len);
    } catch (_) {}
  }

  let debounceTimer = null;
  let activeAbortController = null;
  let latestQueryToken = 0;

  function performLiveSearch() {
    const val = queryInput.value.trim();
    const tableSection = document.querySelector('section.card') || document.querySelector('.inventory-container');
    if (tableSection) tableSection.style.opacity = '0.65';

    if (activeAbortController) {
      activeAbortController.abort();
    }
    activeAbortController = new AbortController();
    const queryToken = ++latestQueryToken;

    // Determine queryToSend
    let queryToSend = val;
    if (queryHidden && queryHidden.value && queryHidden.value.includes('|')) {
      const chips = queryHidden.value.split('|').map(t => t.trim()).filter(Boolean);
      if (val && !chips.includes(val)) {
        chips.push(val);
      }
      queryToSend = chips.join('|');
    }

    // Build URL from form
    const formAction = toolbar.getAttribute('action') || window.location.pathname;
    const url = new URL(formAction, window.location.origin);
    const formData = new FormData(toolbar);

    for (const [key, value] of formData.entries()) {
      if (key === 'Query' || key === 'QueryInput' || key === 'Page' || key === '__RequestVerificationToken') continue;
      if (value !== '') url.searchParams.set(key, value);
    }

    if (queryToSend) {
      url.searchParams.set('Query', queryToSend);
    } else {
      url.searchParams.delete('Query');
    }
    url.searchParams.set('Page', '1');

    fetch(url.toString(), {
      signal: activeAbortController.signal,
      headers: { 'X-Requested-With': 'XMLHttpRequest' }
    })
    .then(response => {
      if (!response.ok) throw new Error('Network response not ok');
      return response.text();
    })
    .then(html => {
      if (queryToken !== latestQueryToken) return; // Discard stale response

      const parser = new DOMParser();
      const doc = parser.parseFromString(html, 'text/html');

      // 1. Update section.card if present (Devices, Transactions, Invoices)
      const newCard = doc.querySelector('section.card');
      const currentCard = document.querySelector('section.card');
      if (newCard && currentCard) {
        currentCard.innerHTML = newCard.innerHTML;
        currentCard.style.opacity = '1';
      }

      // 2. Update .inventory-container if present (Inventory)
      const newInv = doc.querySelector('.inventory-container');
      const currentInv = document.querySelector('.inventory-container');
      if (newInv && currentInv) {
        currentInv.innerHTML = newInv.innerHTML;
        currentInv.style.opacity = '1';

        const newStats = newInv.previousElementSibling;
        const currentStats = currentInv.previousElementSibling;
        if (newStats && currentStats) {
          currentStats.innerHTML = newStats.innerHTML;
        }
      }

      // 3. Update chips container in toolbar
      const newChips = doc.querySelector('form.toolbar .flex.flex-wrap.gap-2');
      const currentChips = toolbar.querySelector('.flex.flex-wrap.gap-2');
      if (currentChips && newChips) {
        currentChips.innerHTML = newChips.innerHTML;
      } else if (currentChips && !newChips) {
        currentChips.remove();
      } else if (!currentChips && newChips) {
        toolbar.appendChild(newChips.cloneNode(true));
      }

      // 4. Update hidden #Query
      if (queryHidden) {
        queryHidden.value = queryToSend;
      }

      // 5. Highlight query terms in table
      const terms = queryToSend ? queryToSend.split('|').map(t => t.trim()).filter(Boolean) : [];
      const tbody = document.querySelector('.data-table tbody');
      if (tbody && terms.length > 0) {
        highlightQueryTerms(tbody, terms);
      }

      // 6. Update URL in address bar without reload
      window.history.replaceState(null, '', url.toString());

      // 7. Update selection counts if present
      if (typeof window.onRowSelectionChange === 'function') {
        window.onRowSelectionChange();
      } else if (typeof window.updateSelectionBar === 'function') {
        window.updateSelectionBar();
      }
    })
    .catch(err => {
      if (err.name === 'AbortError') return;
      console.warn('Live search fetch error:', err);
      const section = document.querySelector('section.card') || document.querySelector('.inventory-container');
      if (section) section.style.opacity = '1';
    });
  }

  queryInput.addEventListener('input', () => {
    clearTimeout(debounceTimer);
    sessionStorage.setItem('query_input_active', '1');

    const tableSection = document.querySelector('section.card') || document.querySelector('.inventory-container');
    if (tableSection) tableSection.style.opacity = '0.65';

    debounceTimer = setTimeout(() => {
      performLiveSearch();
    }, 350);
  });

  queryInput.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') {
      e.preventDefault();
      clearTimeout(debounceTimer);
      performLiveSearch();
    }
  });

  queryInput.addEventListener('blur', () => {
    setTimeout(() => {
      if (document.activeElement !== queryInput) {
        sessionStorage.removeItem('query_input_active');
      }
    }, 500);
  });

  // Cancel debounce on normal form submit so we don't duplicate work
  toolbar.addEventListener('submit', () => clearTimeout(debounceTimer));
});

// ==========================================================================
// Searchable Filter Dropdowns (SFD) — Global for all dropdowns with > 6 options
// ==========================================================================
const SFD_OPTION_THRESHOLD = 6;

function initSearchableFilterDropdowns(root) {
  const containerRoot = (root instanceof HTMLElement || root instanceof Document) ? root : document;
  const selects = containerRoot.querySelectorAll('select');

  selects.forEach(select => {
    // Skip if developer explicitly disabled SFD on this select
    if (select.hasAttribute('data-no-sfd') || select.dataset.noSfd === 'true') return;
    // Skip multi-select elements
    if (select.multiple) return;
    // Skip if inside a template
    if (select.closest('template')) return;

    // Check count of options
    const optionCount = select.options.length;
    const shouldBeSearchable = optionCount > SFD_OPTION_THRESHOLD || select.dataset.searchableFilter === 'true';

    if (shouldBeSearchable) {
      if (!select.dataset.sfdInitialized) {
        createSearchableFilterDropdown(select);
      } else {
        // If already initialized, sync if options or disabled status changed
        if (typeof select._sfdSync === 'function') {
          select._sfdSync();
        }
      }
    } else {
      // If it was previously initialized but options shrank to <= 6 (and not forced)
      if (select.dataset.sfdInitialized === 'true' && select.dataset.searchableFilter !== 'true') {
        destroySearchableFilterDropdown(select);
      }
    }
  });
}

function destroySearchableFilterDropdown(select) {
  if (!select || !select.dataset.sfdInitialized) return;
  const container = select.closest('.sfd-container');
  if (container && container.parentNode) {
    select.classList.remove('sfd-native-hidden');
    delete select.dataset.sfdInitialized;
    if (select._sfdObserver) {
      select._sfdObserver.disconnect();
      delete select._sfdObserver;
    }
    delete select._sfdSync;
    container.parentNode.insertBefore(select, container);
    container.remove();
  }
}

function createSearchableFilterDropdown(select) {
  if (select.dataset.sfdInitialized) return;
  select.dataset.sfdInitialized = 'true';
  select.classList.add('sfd-native-hidden');

  const container = document.createElement('div');
  container.className = 'sfd-container';
  
  // Inherit compact sizing for pagination and small selects
  if (select.classList.contains('select-sm') || select.classList.contains('small') || select.closest('.table-pagination-nav, .top-page-select-wrap')) {
    container.classList.add('sfd-sm');
  }

  // Preserve full width in form groups and modals
  const computed = window.getComputedStyle(select);
  const isBlockOrFull = select.style.width === '100%' 
                     || select.classList.contains('select')
                     || select.closest('.form-group, .form-field, .modal-body, .rc-modal-body, .modal-field, .field-wrap');
  
  const isToolbar = select.closest('.toolbar, .filter-grid, .rc-filter-bar, .vendors-toolbar, .table-pagination-nav, .data-table, .search-bar');
  
  if (isBlockOrFull && !isToolbar) {
    container.classList.add('sfd-full-width');
  }

  if (select.style.minWidth) container.style.minWidth = select.style.minWidth;
  if (select.style.maxWidth) container.style.maxWidth = select.style.maxWidth;
  if (select.style.width && select.style.width !== '100%') container.style.width = select.style.width;

  select.parentNode.insertBefore(container, select);
  container.appendChild(select);

  // Trigger button
  const trigger = document.createElement('button');
  trigger.type = 'button';
  trigger.className = 'sfd-trigger';
  trigger.setAttribute('aria-haspopup', 'listbox');
  trigger.setAttribute('aria-expanded', 'false');

  if (select.style.height) trigger.style.height = select.style.height;
  if (select.style.minHeight) trigger.style.minHeight = select.style.minHeight;
  if (select.style.borderRadius) trigger.style.borderRadius = select.style.borderRadius;
  if (select.style.fontSize) trigger.style.fontSize = select.style.fontSize;
  if (select.style.padding && !container.classList.contains('sfd-sm')) trigger.style.padding = select.style.padding;

  const triggerText = document.createElement('span');
  triggerText.className = 'sfd-trigger-text';

  const triggerArrow = document.createElement('span');
  triggerArrow.className = 'sfd-trigger-arrow';
  triggerArrow.innerHTML = `
    <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
      <polyline points="6 9 12 15 18 9"></polyline>
    </svg>
  `;

  trigger.appendChild(triggerText);
  trigger.appendChild(triggerArrow);
  container.appendChild(trigger);

  // Dropdown menu
  const menu = document.createElement('div');
  menu.className = 'sfd-menu';

  // Search box
  const searchBox = document.createElement('div');
  searchBox.className = 'sfd-search-box';
  searchBox.innerHTML = `
    <svg class="sfd-search-icon" width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round">
      <circle cx="11" cy="11" r="8"></circle>
      <line x1="21" y1="21" x2="16.65" y2="16.65"></line>
    </svg>
    <input type="text" class="sfd-search-input" placeholder="Search..." autocomplete="off" spellcheck="false" />
    <button type="button" class="sfd-clear-btn" style="display:none;" title="Clear">&times;</button>
  `;

  const searchInput = searchBox.querySelector('.sfd-search-input');
  const clearBtn = searchBox.querySelector('.sfd-clear-btn');

  // Options list
  const optionsList = document.createElement('div');
  optionsList.className = 'sfd-options-list';
  optionsList.setAttribute('role', 'listbox');

  // No results notice
  const noResults = document.createElement('div');
  noResults.className = 'sfd-no-results';
  noResults.style.display = 'none';
  noResults.textContent = 'No matching options';

  menu.appendChild(searchBox);
  menu.appendChild(optionsList);
  menu.appendChild(noResults);
  container.appendChild(menu);

  let activeHighlightedIndex = -1;

  function syncDisabledState() {
    const isLocked = select.disabled || select.hasAttribute('readonly') || select.style.pointerEvents === 'none';
    trigger.disabled = select.disabled;
    if (isLocked) {
      container.classList.add('is-disabled');
      trigger.style.pointerEvents = 'none';
      trigger.style.backgroundColor = '#f3f4f6';
      trigger.style.opacity = '0.8';
    } else {
      container.classList.remove('is-disabled');
      trigger.style.pointerEvents = '';
      trigger.style.backgroundColor = '';
      trigger.style.opacity = '';
    }
  }

  function renderOptions() {
    optionsList.innerHTML = '';
    const currentVal = select.value;
    activeHighlightedIndex = -1;

    function renderItem(opt, idx) {
      const optEl = document.createElement('div');
      optEl.className = 'sfd-option';
      optEl.setAttribute('role', 'option');
      optEl.dataset.value = opt.value;
      optEl.dataset.index = idx;

      if (opt.value === '__ADD_NEW__' || opt.classList.contains('add-custom-option-choice')) {
        optEl.classList.add('sfd-option-add-new');
      }

      const isSel = opt.selected || opt.value === currentVal;
      if (isSel) optEl.classList.add('is-selected');

      const labelSpan = document.createElement('span');
      labelSpan.className = 'sfd-option-label';
      labelSpan.textContent = opt.textContent;
      optEl.appendChild(labelSpan);

      if (isSel) {
        optEl.insertAdjacentHTML('beforeend', `
          <svg class="sfd-check-icon" width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><polyline points="20 6 9 17 4 12"/></svg>
        `);
      }

      optEl.addEventListener('click', (e) => {
        e.stopPropagation();
        applySelection(opt.value, opt.textContent);
      });

      optionsList.appendChild(optEl);
    }

    let globalIdx = 0;
    Array.from(select.children).forEach(child => {
      if (child.tagName === 'OPTGROUP') {
        const header = document.createElement('div');
        header.className = 'sfd-optgroup-label';
        header.textContent = child.label;
        optionsList.appendChild(header);

        Array.from(child.children).forEach(opt => {
          renderItem(opt, globalIdx++);
        });
      } else if (child.tagName === 'OPTION') {
        renderItem(child, globalIdx++);
      }
    });

    const activeOpt = select.options[select.selectedIndex] || select.options[0];
    triggerText.textContent = activeOpt ? activeOpt.textContent : 'Select...';
    syncSelectedClass();
  }

  function applySelection(val, text) {
    const changed = select.value !== val;
    select.value = val;
    if (changed || val === '__ADD_NEW__') {
      select.dispatchEvent(new Event('change', { bubbles: true }));
      select.dispatchEvent(new Event('input', { bubbles: true }));
      if (typeof select.onchange === 'function') {
        select.onchange();
      }
    }
    triggerText.textContent = text;
    syncSelectedClass();
    closeMenu();
    container.classList.remove('is-invalid');
  }

  function syncSelectedClass() {
    const val = select.value;
    const items = optionsList.querySelectorAll('.sfd-option');
    items.forEach(item => {
      const isMatch = item.dataset.value === val;
      item.classList.toggle('is-selected', isMatch);
      const check = item.querySelector('.sfd-check-icon');
      if (isMatch && !check) {
        item.insertAdjacentHTML('beforeend', `
          <svg class="sfd-check-icon" width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5"><polyline points="20 6 9 17 4 12"/></svg>
        `);
      } else if (!isMatch && check) {
        check.remove();
      }
    });
    const activeOpt = select.options[select.selectedIndex];
    if (activeOpt) triggerText.textContent = activeOpt.textContent;

    // Highlight filtered dropdowns in theme blue
    const isFilterable = !select.name?.includes('Page') && !select.id?.includes('Page') && select.id !== 'PageSize' && select.id !== 'PageSelect';
    const isFiltered = isFilterable && Boolean(val && val.trim() !== '' && val.toLowerCase() !== 'all');
    container.classList.toggle('is-filtered', isFiltered);
    trigger.classList.toggle('is-filtered', isFiltered);
    const parentField = container.closest('.field');
    if (parentField && isFilterable) {
      parentField.classList.toggle('has-active-filter', isFiltered);
    }
  }

  function filterOptions(term) {
    const q = term.trim().toLowerCase();
    clearBtn.style.display = q ? 'block' : 'none';
    let matchCount = 0;
    const items = optionsList.querySelectorAll('.sfd-option');
    activeHighlightedIndex = -1;

    items.forEach(item => {
      const text = item.textContent.toLowerCase();
      const isAddNew = item.classList.contains('sfd-option-add-new');
      const matched = !q || text.includes(q) || isAddNew;
      item.style.display = matched ? 'flex' : 'none';
      item.classList.remove('is-highlighted');
      if (matched && !isAddNew) matchCount++;
    });

    optionsList.querySelectorAll('.sfd-optgroup-label').forEach(header => {
      let next = header.nextElementSibling;
      let hasVisible = false;
      while (next && !next.classList.contains('sfd-optgroup-label')) {
        if (next.classList.contains('sfd-option') && next.style.display !== 'none') {
          hasVisible = true;
          break;
        }
        next = next.nextElementSibling;
      }
      header.style.display = hasVisible ? 'block' : 'none';
    });

    noResults.style.display = (matchCount === 0 && !optionsList.querySelector('.sfd-option-add-new')) ? 'block' : 'none';
  }

  function getVisibleOptions() {
    return Array.from(optionsList.querySelectorAll('.sfd-option')).filter(i => i.style.display !== 'none');
  }

  function highlightOption(index) {
    const visible = getVisibleOptions();
    visible.forEach((el, i) => {
      if (i === index) {
        el.classList.add('is-highlighted');
        el.scrollIntoView({ block: 'nearest' });
      } else {
        el.classList.remove('is-highlighted');
      }
    });
    activeHighlightedIndex = index;
  }

  function openMenu() {
    if (container.classList.contains('is-disabled')) return;

    document.querySelectorAll('.sfd-container.is-open').forEach(c => {
      if (c !== container) {
        c.classList.remove('is-open');
        const t = c.querySelector('.sfd-trigger');
        if (t) t.setAttribute('aria-expanded', 'false');
      }
    });

    renderOptions();
    searchInput.value = '';
    searchInput.placeholder = `Search ${select.options.length} options...`;
    filterOptions('');

    const rect = container.getBoundingClientRect();
    if (rect.left + 280 > window.innerWidth) {
      menu.classList.add('align-right');
    } else {
      menu.classList.remove('align-right');
    }

    const spaceBelow = window.innerHeight - rect.bottom;
    const spaceAbove = rect.top;
    if (spaceBelow < 280 && spaceAbove > spaceBelow) {
      menu.classList.add('dropup');
    } else {
      menu.classList.remove('dropup');
    }

    container.classList.add('is-open');
    trigger.setAttribute('aria-expanded', 'true');

    setTimeout(() => {
      searchInput.focus();
      const selEl = optionsList.querySelector('.sfd-option.is-selected');
      if (selEl) selEl.scrollIntoView({ block: 'nearest' });
    }, 30);
  }

  function closeMenu() {
    container.classList.remove('is-open');
    trigger.setAttribute('aria-expanded', 'false');
  }

  function toggleMenu() {
    if (container.classList.contains('is-open')) {
      closeMenu();
    } else {
      openMenu();
    }
  }

  trigger.addEventListener('click', (e) => {
    e.preventDefault();
    e.stopPropagation();
    toggleMenu();
  });

  searchInput.addEventListener('input', () => {
    filterOptions(searchInput.value);
  });

  searchInput.addEventListener('keydown', (e) => {
    const visible = getVisibleOptions();
    if (e.key === 'ArrowDown') {
      e.preventDefault();
      let next = activeHighlightedIndex + 1;
      if (next >= visible.length) next = 0;
      highlightOption(next);
    } else if (e.key === 'ArrowUp') {
      e.preventDefault();
      let prev = activeHighlightedIndex - 1;
      if (prev < 0) prev = visible.length - 1;
      highlightOption(prev);
    } else if (e.key === 'Enter') {
      e.preventDefault();
      if (activeHighlightedIndex >= 0 && activeHighlightedIndex < visible.length) {
        const item = visible[activeHighlightedIndex];
        applySelection(item.dataset.value, item.querySelector('.sfd-option-label').textContent);
      } else if (visible.length > 0) {
        const item = visible[0];
        applySelection(item.dataset.value, item.querySelector('.sfd-option-label').textContent);
      }
    } else if (e.key === 'Escape') {
      e.preventDefault();
      closeMenu();
      trigger.focus();
    } else if (e.key === 'Tab') {
      closeMenu();
    }
  });

  clearBtn.addEventListener('click', (e) => {
    e.stopPropagation();
    searchInput.value = '';
    filterOptions('');
    searchInput.focus();
  });

  select.addEventListener('change', () => {
    syncSelectedClass();
    container.classList.remove('is-invalid');
  });

  select.addEventListener('invalid', () => {
    container.classList.add('is-invalid');
  });

  const observer = new MutationObserver(() => {
    const count = select.options.length;
    if (count <= SFD_OPTION_THRESHOLD && select.dataset.searchableFilter !== 'true') {
      destroySearchableFilterDropdown(select);
      return;
    }
    renderOptions();
    syncSelectedClass();
    syncDisabledState();
  });
  observer.observe(select, { childList: true, subtree: true, attributes: true, attributeFilter: ['disabled', 'readonly', 'style', 'class'] });
  select._sfdObserver = observer;
  select._sfdSync = () => { renderOptions(); syncSelectedClass(); syncDisabledState(); };

  syncDisabledState();
  renderOptions();
}

// Global click-outside handler for SFD
document.addEventListener('click', (e) => {
  if (!e.target.closest('.sfd-container')) {
    document.querySelectorAll('.sfd-container.is-open').forEach(c => {
      c.classList.remove('is-open');
      const t = c.querySelector('.sfd-trigger');
      if (t) t.setAttribute('aria-expanded', 'false');
    });
  }
});

// Global Escape key handler
document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape') {
    document.querySelectorAll('.sfd-container.is-open').forEach(c => {
      c.classList.remove('is-open');
      const t = c.querySelector('.sfd-trigger');
      if (t) {
        t.setAttribute('aria-expanded', 'false');
        t.focus();
      }
    });
  }
});

// Automatically observe DOM changes to enhance newly added selects
let _sfdObserver = null;
function setupSfdObserver() {
  if (_sfdObserver) return;
  _sfdObserver = new MutationObserver((mutations) => {
    let shouldCheck = false;
    for (const m of mutations) {
      if (m.addedNodes && m.addedNodes.length > 0) {
        for (const node of m.addedNodes) {
          if (node.nodeType === 1) { // Node.ELEMENT_NODE
            if (node.tagName === 'SELECT' || (node.querySelector && node.querySelector('select'))) {
              shouldCheck = true;
              break;
            }
          }
        }
      }
      if (shouldCheck) break;
    }
    if (shouldCheck) {
      initSearchableFilterDropdowns();
    }
  });
  _sfdObserver.observe(document.body, { childList: true, subtree: true });
}

// Auto-run on DOM ready and expose globally
document.addEventListener('DOMContentLoaded', () => {
  initSearchableFilterDropdowns();
  setupSfdObserver();
});
window.initSearchableFilterDropdowns = initSearchableFilterDropdowns;

// Re-check on modal triggers
document.addEventListener('click', (e) => {
  if (e.target.closest('[data-bs-toggle="modal"], [onclick*="Modal"], [onclick*="modal"], .modal-trigger')) {
    setTimeout(initSearchableFilterDropdowns, 100);
  }
});

// ── Session Idle Timeout (15 minutes of inactivity across all tabs) ───────────
(() => {
  const path = window.location.pathname.toLowerCase();
  if (path.includes('/account/login') || path.includes('/account/logout') || path.includes('/account/denied')) return;

  const TIMEOUT_MS = 15 * 60 * 1000; // 15 minutes of inactivity
  const CHECK_INTERVAL_MS = 10 * 1000; // Check every 10 seconds
  const THROTTLE_MS = 2000; // Throttle storage writes to once every 2 seconds
  const STORAGE_KEY = 'mygts_last_activity';

  let lastLocalTouch = 0;

  function recordActivity() {
    const now = Date.now();
    if (now - lastLocalTouch > THROTTLE_MS) {
      lastLocalTouch = now;
      try {
        localStorage.setItem(STORAGE_KEY, now.toString());
      } catch (_) {}
    }
  }

  // Record initial activity upon page navigation/load
  try {
    localStorage.setItem(STORAGE_KEY, Date.now().toString());
  } catch (_) {}

  // Listen to user interaction events across the page
  const activityEvents = ['mousemove', 'mousedown', 'keydown', 'scroll', 'touchstart', 'click', 'wheel'];
  activityEvents.forEach(evt => {
    window.addEventListener(evt, recordActivity, { passive: true });
  });

  // Cross-tab synchronization: when another tab records activity, update our local touch
  window.addEventListener('storage', (e) => {
    if (e.key === STORAGE_KEY) {
      lastLocalTouch = parseInt(e.newValue || '0', 10) || Date.now();
    }
  });

  // Check periodically if the user has been truly idle across ALL tabs
  setInterval(() => {
    let lastActive = parseInt(localStorage.getItem(STORAGE_KEY) || '0', 10);
    if (!lastActive || isNaN(lastActive)) {
      lastActive = Date.now();
      try { localStorage.setItem(STORAGE_KEY, lastActive.toString()); } catch (_) {}
    }

    const idleTime = Date.now() - lastActive;
    if (idleTime >= TIMEOUT_MS) {
      // User has been completely idle across all tabs for 15 minutes
      try {
        localStorage.removeItem(STORAGE_KEY);
      } catch (_) {}
      window.location.replace('/Account/Logout');
    }
  }, CHECK_INTERVAL_MS);
})();

// ── Dynamic KPI text auto-fitting (Uniform across all cards in each grid) ───
// Ensures all KPI cards in a grid maintain a large, bold, uniform font size,
// scaled appropriately for the current resolution while guaranteeing no dots (...)
let _isFittingKpis = false;
function fitKpiValues() {
  if (_isFittingKpis) return;
  _isFittingKpis = true;

  try {
    const grids = document.querySelectorAll('.kpi-grid');
    const vw = window.innerWidth || document.documentElement.clientWidth || 1200;
    
    // Resolution-aware minimum uniform floor so text is NEVER tiny
    const uniformFloor = vw >= 1400 ? 26 : (vw >= 1024 ? 24 : (vw >= 640 ? 20 : 17));

    if (grids.length > 0) {
      grids.forEach(grid => {
        const elements = grid.querySelectorAll('.kpi-value');
        if (!elements.length) return;

        // Pass 1: Reset inline styles so full CSS clamp/media query resolution styles apply
        elements.forEach(el => {
          el.style.textOverflow = 'clip';
          el.style.fontSize = '';
        });

        // Pass 2: Inspect if any card overflows its container width
        let baseCssSize = 0;
        let minRatio = 1.0;
        let anyOverflow = false;

        elements.forEach(el => {
          const parent = el.closest('.kpi-card') || el.parentElement;
          if (!parent) return;

          const style = window.getComputedStyle(parent);
          const padLeft = parseFloat(style.paddingLeft) || 0;
          const padRight = parseFloat(style.paddingRight) || 0;
          const availableWidth = parent.clientWidth - padLeft - padRight - 4;

          if (availableWidth <= 0) return;

          const currentSize = parseFloat(window.getComputedStyle(el).fontSize);
          if (currentSize > baseCssSize) baseCssSize = currentSize;

          const textWidth = el.scrollWidth;
          if (textWidth > availableWidth + 1) {
            anyOverflow = true;
            const ratio = (availableWidth / textWidth) * 0.98;
            if (ratio < minRatio) {
              minRatio = ratio;
            }
          }
        });

        // Pass 3: If any element overflows, calculate an optimal uniform size
        if (anyOverflow && baseCssSize > 0) {
          let uniformSize = Math.max(uniformFloor, Math.floor(baseCssSize * minRatio * 10) / 10);
          elements.forEach(el => {
            el.style.fontSize = uniformSize + 'px';
          });

          // Pass 4: In extreme cases where a single astronomical number still overflows at uniformFloor,
          // scale only that specific card so all other cards maintain their large uniform font
          elements.forEach(el => {
            const parent = el.closest('.kpi-card') || el.parentElement;
            if (!parent) return;
            const style = window.getComputedStyle(parent);
            const maxW = parent.clientWidth - (parseFloat(style.paddingLeft) || 0) - (parseFloat(style.paddingRight) || 0) - 4;
            if (el.scrollWidth > maxW + 1 && maxW > 0) {
              const current = parseFloat(window.getComputedStyle(el).fontSize);
              const customRatio = (maxW / el.scrollWidth) * 0.98;
              const individualSize = Math.max(14, Math.floor(current * customRatio * 10) / 10);
              el.style.fontSize = individualSize + 'px';
            }
          });
        }
      });
    }

    // Standalone .kpi-value elements outside of .kpi-grid
    document.querySelectorAll('.kpi-card:not(.kpi-grid .kpi-card) .kpi-value').forEach(el => {
      el.style.textOverflow = 'clip';
      el.style.fontSize = '';
      const parent = el.closest('.kpi-card') || el.parentElement;
      if (!parent) return;
      const s = window.getComputedStyle(parent);
      const maxW = parent.clientWidth - (parseFloat(s.paddingLeft) || 0) - (parseFloat(s.paddingRight) || 0) - 4;
      if (maxW <= 0) return;
      if (el.scrollWidth > maxW + 1) {
        const cur = parseFloat(window.getComputedStyle(el).fontSize);
        const target = Math.max(16, Math.floor((cur * (maxW / el.scrollWidth) * 0.98) * 10) / 10);
        el.style.fontSize = target + 'px';
      }
    });
  } finally {
    _isFittingKpis = false;
  }
}

window.fitKpiValues = fitKpiValues;

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', fitKpiValues);
} else {
  fitKpiValues();
}

window.addEventListener('load', fitKpiValues);

let _kpiResizeTimer;
window.addEventListener('resize', () => {
  clearTimeout(_kpiResizeTimer);
  _kpiResizeTimer = setTimeout(fitKpiValues, 80);
});

if (document.fonts && document.fonts.ready) {
  document.fonts.ready.then(fitKpiValues);
}

if (window.ResizeObserver) {
  const _kpiRo = new ResizeObserver(() => {
    clearTimeout(_kpiResizeTimer);
    _kpiResizeTimer = setTimeout(fitKpiValues, 60);
  });
  const mainShell = document.querySelector('.main-content') || document.body;
  _kpiRo.observe(mainShell);
}


// Global uppercase input handler - caps locks all text input dynamically (except user credentials)
document.addEventListener('input', function(e) {
  const t = e.target;
  if (!t || (t.tagName !== 'INPUT' && t.tagName !== 'TEXTAREA')) return;

  // Check if input is for user credentials
  const isCredential = t.type === 'password' ||
                       t.type === 'email' ||
                       (t.name && /email|password|username|plainpassword/i.test(t.name)) ||
                       (t.id && /email|password|username/i.test(t.id)) ||
                       (t.autocomplete && /username|password|email/i.test(t.autocomplete)) ||
                       (t.classList && (t.classList.contains('user-email') || t.classList.contains('user-password') || t.classList.contains('credential-input'))) ||
                       Boolean(t.closest('.login-card, #createUserModal, form[action*="Login" i], form[action*="Users" i]'));

  if (isCredential) {
    return; // Leave user credentials alone (case preserved)
  }

  if ((t.type === 'text' || t.type === 'search' || !t.type) &&
      t.type !== 'file' && t.type !== 'date') {
    const start = t.selectionStart;
    const end = t.selectionEnd;
    if (t.value && t.value !== t.value.toUpperCase()) {
      t.value = t.value.toUpperCase();
      if (start !== null && end !== null) {
        try { t.setSelectionRange(start, end); } catch(err) {}
      }
    }
  }
}, true);


// ── Active Filter Highlighting ───────────────────────────────────────────────
function updateActiveFilterStates() {
  const toolbars = document.querySelectorAll('.toolbar, .filter-grid');
  toolbars.forEach(container => {
    let hasAnyFilter = false;

    // Check all selects in container
    container.querySelectorAll('select').forEach(select => {
      // Exclude pagination dropdowns
      const name = select.getAttribute('name') || '';
      const id = select.id || '';
      if (name.toLowerCase() === 'pagesize' || name.toLowerCase() === 'page' || id === 'PageSelect' || select.hasAttribute('data-pagination')) {
        return;
      }

      const val = select.value ? select.value.trim() : '';
      const field = select.closest('.field') || select.parentElement;
      if (val !== '') {
        select.classList.add('filter-active');
        field?.classList.add('has-active-filter');
        hasAnyFilter = true;
      } else {
        select.classList.remove('filter-active');
        field?.classList.remove('has-active-filter');
      }
    });

    // Check search / text inputs
    container.querySelectorAll('input[type="text"]:not([data-pagination]), input[type="search"], input[type="date"]').forEach(input => {
      const field = input.closest('.field') || input.parentElement;
      const form = input.closest('form');
      const hiddenQuery = form?.querySelector('#Query');
      const hasQueryChips = Boolean(form?.querySelector('.flex-wrap span') || document.querySelector('.flex-wrap span'));
      const val = input.value ? input.value.trim() : '';
      const hiddenVal = hiddenQuery?.value ? hiddenQuery.value.trim() : '';

      if (val !== '' || hiddenVal !== '' || (input.id === 'QueryInput' && hasQueryChips)) {
        input.classList.add('filter-active');
        field?.classList.add('has-active-filter');
        hasAnyFilter = true;
      } else {
        input.classList.remove('filter-active');
        field?.classList.remove('has-active-filter');
      }
    });

    // Check hidden query if present in form
    const form = container.closest('form') || container;
    const hiddenQuery = form.querySelector('#Query');
    if (hiddenQuery && hiddenQuery.value && hiddenQuery.value.trim() !== '') {
      hasAnyFilter = true;
      const searchField = form.querySelector('.toolbar-search');
      searchField?.classList.add('has-active-filter');
      const queryInput = searchField?.querySelector('#QueryInput');
      queryInput?.classList.add('filter-active');
    }

    // Highlight "Clear filters" button if any filter is active
    const clearBtn = form.querySelector('a[href*="clear=true"], a[title*="Reset all filters"]');
    if (clearBtn) {
      if (hasAnyFilter) {
        clearBtn.classList.add('clear-filters-active');
      } else {
        clearBtn.classList.remove('clear-filters-active');
      }
    }
  });
}

if (document.readyState === 'loading') {
  document.addEventListener('DOMContentLoaded', updateActiveFilterStates);
} else {
  updateActiveFilterStates();
}

window.addEventListener('load', updateActiveFilterStates);

document.addEventListener('change', function(e) {
  if (e.target && (e.target.closest('.toolbar') || e.target.closest('.filter-grid'))) {
    updateActiveFilterStates();
  }
});

document.addEventListener('input', function(e) {
  if (e.target && (e.target.closest('.toolbar') || e.target.closest('.filter-grid'))) {
    updateActiveFilterStates();
  }
});
