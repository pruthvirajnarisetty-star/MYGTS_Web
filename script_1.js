
    window.formatCurrency = window.formatCurrency || function(v) {
        const num = Number(v) || 0;
        return '₹' + num.toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    };
    const formatCurrency = window.formatCurrency;

    function getCurrentMode() {
        const el = document.getElementById('CurrentMode');
        return (el && el.value) ? el.value : 'Standard';
    }

    window.updateExtrasAmountVisibility = function() {
        const extSelect = document.getElementById('extras-select');
        const fExtAmt = document.getElementById('field-extras-amount');
        if (!extSelect || !fExtAmt) return;
        const val = (extSelect.value || '').trim();
        const hasExtras = val && val.toLowerCase() !== 'none' && val !== '0' && val !== '';
        fExtAmt.style.display = hasExtras ? 'block' : 'none';
        const amtInput = document.getElementById('ExtrasAmount');
        if (!hasExtras && amtInput) {
            amtInput.value = '0';
        }
        if (window.calculateTransactionTotals) window.calculateTransactionTotals();
    };

    const currentModeInput = document.getElementById('CurrentMode');
    const activationInput = document.getElementById('activation-date');
    const expiryInput = document.getElementById('new-expiry');
    const paymentFor = document.getElementById('payment-for');
    const typeSelect = document.getElementById('type-select');
    const fDate = document.getElementById('field-date');
    const fVehicle = document.getElementById('field-vehicle');
    const fVendor = document.getElementById('field-vendor');
    const fInvoiceId = document.getElementById('field-invoice-id');
    const fAmount = document.getElementById('field-amount');
    const fTotal = document.getElementById('field-total');
    const fUtr = document.getElementById('field-utr');
    const fPaidTo = document.getElementById('field-paid-to');
    const fPaymentMode = document.getElementById('field-payment-mode');
    const fActivation = document.getElementById('field-activation');
    const fExpiry = document.getElementById('field-expiry');
    const fBatchId = document.getElementById('field-batch-id');
    const stdToggle = document.getElementById('standard-toggle');
    const mergeToggle = document.getElementById('merge-toggle');
    const bulkToggle = document.getElementById('bulk-toggle');
    const addBulkRowBtn = document.getElementById('add-bulk-row');

    function parseDateParts(str) {
        if (!str) return null;
        let m = str.match(/^(\d{4})[-\/.](\d{1,2})[-\/.](\d{1,2})/);
        if (m) {
            return { y: parseInt(m[1], 10), m: parseInt(m[2], 10) - 1, d: parseInt(m[3], 10) };
        }
        m = str.match(/^(\d{1,2})[-\/.](\d{1,2})[-\/.](\d{4})/);
        if (m) {
            return { y: parseInt(m[3], 10), m: parseInt(m[2], 10) - 1, d: parseInt(m[1], 10) };
        }
        const d = new Date(str);
        if (!isNaN(d.getTime())) {
            return { y: d.getFullYear(), m: d.getMonth(), d: d.getDate() };
        }
        return null;
    }

    function formatToIso(str) {
        const p = parseDateParts(str);
        if (!p) return '';
        return `${p.y}-${String(p.m + 1).padStart(2, '0')}-${String(p.d).padStart(2, '0')}`;
    }

    function calculateExpiryDate(activationDateStr) {
        const p = parseDateParts(activationDateStr);
        if (!p) return '';
        const monthsToAdd = parseInt('@(ViewBag.ActivationExpiryMonths ?? "12")') || 12;
        const daysToAdd = parseInt('@(ViewBag.ActivationExpiryDays ?? "0")');
        const effectiveDays = isNaN(daysToAdd) ? 0 : daysToAdd;
        const dt = new Date(p.y, p.m + monthsToAdd, p.d + effectiveDays);
        if (isNaN(dt.getTime())) return '';
        const resY = dt.getFullYear();
        const resM = String(dt.getMonth() + 1).padStart(2, '0');
        const resD = String(dt.getDate()).padStart(2, '0');
        return `${resY}-${resM}-${resD}`;
    }

    function updateExpiry(force) {
        if (!activationInput || !expiryInput) return;
        const pFor = (paymentFor && paymentFor.value ? paymentFor.value : '').toLowerCase();
        if (pFor === 'other' || pFor === 'others') {
            return;
        }
        if (expiryInput.dataset.manual === 'true' && force !== true) {
            return;
        }
        const isRechargeOrRenewal = pFor.includes('recharge') || pFor.includes('renewal');
        if (isRechargeOrRenewal && expiryInput.value && force !== true) {
            return;
        }
        const val = activationInput.value;
        if (!val) {
            if (force === true) expiryInput.value = '';
            return;
        }
        // DMG: Always 1 year from activation date for all payment options except Other
        const p = parseDateParts(val);
        if (p) {
            const dt = new Date(p.y + 1, p.m, p.d);
            const resY = dt.getFullYear();
            const resM = String(dt.getMonth() + 1).padStart(2, '0');
            const resD = String(dt.getDate()).padStart(2, '0');
            expiryInput.value = `${resY}-${resM}-${resD}`;
        }
    }

    if (expiryInput) {
        expiryInput.addEventListener("input", function() { expiryInput.dataset.manual = 'true'; });
        expiryInput.addEventListener("change", function() { expiryInput.dataset.manual = 'true'; });
    }

    if (activationInput) {
        activationInput.addEventListener("input", () => updateExpiry());
        activationInput.addEventListener("change", () => updateExpiry());
        activationInput.addEventListener("keyup", () => updateExpiry());
        activationInput.addEventListener("blur", () => updateExpiry());
    }
    if (paymentFor) {
        paymentFor.addEventListener("change", () => {
            const v = vehicleInput ? vehicleInput.value.trim() : '';
            if (v && typeof fetchDeviceInfo === 'function') {
                fetchDeviceInfo(v, false);
            }
            updateExpiry(true);
            calculateTransactionTotals();
        });
    }
    if (typeSelect) {
        typeSelect.addEventListener("change", () => {
            updateExpiry(true);
            calculateTransactionTotals();
        });
    }

    // Default calculate expiry date on initial load if activation date is present AND expiry date is not already set
    if (activationInput && activationInput.value && (!expiryInput || !expiryInput.value)) {
        updateExpiry();
    }

    const fMergeGrid = document.getElementById("field-merge-grid");
    const fBulkGrid = document.getElementById("field-bulk-vehicles");
    
    const amountDisplay = document.querySelector('[data-txn-amount]');
    const costDisplay = document.querySelector('[data-txn-cost]');
    const adminCostDisplay = document.querySelector('[data-txn-admin-cost]');
    const profitDisplay = document.querySelector('[data-txn-profit]');
    const amountBreakdown = document.getElementById('txn-amount-breakdown');
    const adminCostBadge = document.getElementById('admin-cost-badge');

    window.currentDeviceCost = 0;

    const operatorRates = {
        'taisys (mygts)': { recharge: parseFloat('@ViewBag.CompanyRechargeRate') || 1298, reactivation: parseFloat('@ViewBag.CompanyReactRate') || 1534 },
        'taisys (babu)': { recharge: parseFloat('@ViewBag.BluemineRechargeRate') || 1156.40, reactivation: parseFloat('@ViewBag.BluemineReactRate') || 1392.40 },
        'smt': { recharge: parseFloat('@ViewBag.SmtRechargeRate') || 1298, reactivation: parseFloat('@ViewBag.SmtReactRate') || 1534 },
        'sensorise': { recharge: parseFloat('@ViewBag.SensoriseRechargeRate') || 1298, reactivation: parseFloat('@ViewBag.SensoriseReactRate') || 1534 }
    };

    function getOperatorCost(opName, type) {
        if (!opName) return 0;
        const clean = opName.toLowerCase().trim();
        if (clean.includes('babu') || clean.includes('bluemine')) return operatorRates['taisys (babu)'][type] || 0;
        if (clean.includes('smt')) return operatorRates['smt'][type] || 0;
        if (clean.includes('sensorise')) return operatorRates['sensorise'][type] || 0;
        if (clean.includes('taisys') || clean.includes('mygts')) return operatorRates['taisys (mygts)'][type] || 0;
        return 0;
    }

    window.onNetworkOperatorChange = function(op) {
        window.currentDeviceNetworkOperator = op;
        if (window.calculateTransactionTotals) {
            window.calculateTransactionTotals();
        }
    };

    const vehicleInput = document.querySelector('[name="VehicleNo"]');
    const vendorInput = document.querySelector('[name="Vendor"]');
    const invoiceIdInput = document.querySelector('[name="InvoiceId"]');
    const imeiInput = document.getElementById('txn-imei') || document.querySelector('[name="Imei"]');
    const vendorSelect = document.getElementById("vendorSelect");
    const vendorCustomWrap = document.getElementById("vendorCustomWrap");
    const btnToggleCustom = document.getElementById("btnToggleCustomVendor");

    function setVendorAuto(vendorName) {
        if (!vendorName) {
            if (vendorSelect) {
                vendorSelect.value = '';
                const sfdTrigger = vendorSelect.closest('.sfd-container')?.querySelector('.sfd-trigger-text');
                if (sfdTrigger) sfdTrigger.textContent = '-- Select Vendor from List --';
                if (typeof vendorSelect._sfdSync === 'function') vendorSelect._sfdSync();
            }
            if (vendorInput) vendorInput.value = '';
            if (window.updateAutoBatchId) window.updateAutoBatchId();
            return;
        }
        const cleanV = vendorName.trim();
        if (vendorInput) vendorInput.value = cleanV;
        if (vendorSelect) {
            let matched = false;
            for (let i = 0; i < vendorSelect.options.length; i++) {
                if (vendorSelect.options[i].value.trim().toLowerCase() === cleanV.toLowerCase()) {
                    vendorSelect.selectedIndex = i;
                    matched = true;
                    break;
                }
            }
            if (!matched) {
                for (let i = 0; i < vendorSelect.options.length; i++) {
                    if (vendorSelect.options[i].text.trim().toLowerCase() === cleanV.toLowerCase()) {
                        vendorSelect.selectedIndex = i;
                        matched = true;
                        break;
                    }
                }
            }
            if (!matched) {
                const opt = new Option(cleanV, cleanV, true, true);
                vendorSelect.add(opt);
                vendorSelect.value = cleanV;
            }

            const selText = (vendorSelect.selectedIndex >= 0 && vendorSelect.options[vendorSelect.selectedIndex]) 
                ? vendorSelect.options[vendorSelect.selectedIndex].textContent 
                : cleanV;
            const sfdTrigger = vendorSelect.closest('.sfd-container')?.querySelector('.sfd-trigger-text');
            if (sfdTrigger) sfdTrigger.textContent = selText;
            if (typeof vendorSelect._sfdSync === 'function') {
                vendorSelect._sfdSync();
            }
            vendorSelect.dispatchEvent(new Event('change', { bubbles: true }));
        }
        if (vendorCustomWrap) vendorCustomWrap.style.display = 'none';
        if (btnToggleCustom) btnToggleCustom.textContent = '+ New Vendor';
        if (window.updateAutoBatchId) window.updateAutoBatchId();
    }

    document.addEventListener("DOMContentLoaded", function() {
        const initV = (vendorInput && vendorInput.value) || (vendorSelect && vendorSelect.value);
        if (initV) {
            setVendorAuto(initV);
            setTimeout(() => setVendorAuto(initV), 50);
        }
    });
    
    if (vehicleInput) {
        const urlParams = new URLSearchParams(window.location.search);
        const rechargeIdParam = urlParams.get('rechargeId') || '';
    const fetchDeviceInfo = function(v, isUserInput = false) {
            const urlParamsNow = new URLSearchParams(window.location.search);
            const devIdParam = urlParamsNow.get('deviceId') || '';
            const imeiQueryParam = urlParamsNow.get('imei') || '';
            const imeiVal = (imeiInput ? imeiInput.value : '') || imeiQueryParam;
            let queryUrl = '/@currentBranch/Transactions/GetDeviceInfo?vehicleNo=' + encodeURIComponent(v) + (rechargeIdParam ? ('&rechargeId=' + encodeURIComponent(rechargeIdParam)) : '');
            if (devIdParam) queryUrl += '&deviceId=' + encodeURIComponent(devIdParam);
            if (imeiVal) queryUrl += '&imei=' + encodeURIComponent(imeiVal);
            const pForEl = document.getElementById('payment-for') || document.querySelector('[name="PaymentFor"]');
            if (pForEl && pForEl.value) queryUrl += '&paymentFor=' + encodeURIComponent(pForEl.value);
            fetch(queryUrl, { credentials: 'same-origin' })
                .then(r => r.json())
                .then(data => {
                    if (data && (data.isFound === false || data.IsFound === false)) {
                        let err = document.getElementById('vehicle-error');
                        if(err) err.style.display = 'block';
                        const lcInput = document.getElementById('txn-lifecycle');
                        if (lcInput) {
                            lcInput.value = '';
                        }
                        window.currentDeviceCost = parseFloat('@ViewBag.CostPerDevice') || 0;
                        window.currentDeviceAdminCost = null;
                    } else if (data) {
                        let err = document.getElementById('vehicle-error');
                        if(err) err.style.display = 'none';
                        const lcInput = document.getElementById('txn-lifecycle');
                        if (lcInput) {
                            lcInput.value = (data.status || data.Status || '').trim();
                        }
                        const vendor = data.vendor || data.Vendor;
                        const invoiceId = data.invoiceId || data.InvoiceId;
                        const imei = data.imei || data.Imei || '';
                        const unitCost = data.unitCost || data.UnitCost;
                        const dAdmin = data.adminCost !== undefined && data.adminCost !== null ? parseFloat(data.adminCost) : null;
                        window.currentDeviceAdminCost = (dAdmin !== null && !isNaN(dAdmin) && dAdmin > 0) ? dAdmin : null;
                        const typeVal = data.type || data.Type;
                        const actDate = data.activationDate || data.ActivationDate;
                        const expDate = data.latestExpiryDate || data.LatestExpiryDate;
                        if (vendor) {
                            setVendorAuto(vendor);
                        }
                        if (invoiceIdInput && invoiceId) { invoiceIdInput.value = invoiceId; invoiceIdInput.readOnly = true; }
                        if (imeiInput) { imeiInput.value = imei; }
                        const typeSelectEl = document.getElementById('type-select');
                        if (typeSelectEl && typeVal) { 
                            typeSelectEl.value = typeVal; 
                            typeSelectEl.style.pointerEvents = 'none';
                            typeSelectEl.style.opacity = '0.7';
                            typeSelectEl.style.backgroundColor = '#e9ecef';
                        }
                        window.currentDeviceCost = parseFloat(unitCost) || 0;
                        if (window.currentDeviceCost === 0 && invoiceId) {
                            fetch('/@currentBranch/Transactions/GetInvoiceCost?invoiceId=' + encodeURIComponent(invoiceId), { credentials: 'same-origin' })
                                .then(res => res.json())
                                .then(invData => {
                                    if (invData && invData.unitCost > 0) {
                                        window.currentDeviceCost = parseFloat(invData.unitCost) || 0;
                                        calculateTransactionTotals();
                                    }
                                })
                                .catch(e => console.error(e));
                        }
                        const netOp = (data.networkOperator || data.NetworkOperator || '').trim();
                        window.currentDeviceNetworkOperator = netOp;
                        // Auto-fill network operator field
                        const netOpInput = document.getElementById('txn-network-operator');
                        if (netOpInput && netOp) {
                            let matched = false;
                            for (let opt of netOpInput.options) {
                                if (opt.value && (opt.value.toLowerCase() === netOp.toLowerCase() || netOp.toLowerCase().includes(opt.value.toLowerCase()) || opt.value.toLowerCase().includes(netOp.toLowerCase()))) {
                                    netOpInput.value = opt.value;
                                    matched = true;
                                    break;
                                }
                            }
                            if (!matched && netOp) {
                                netOpInput.add(new Option(netOp, netOp, true, true));
                            }
                        }
                        window.currentDeviceRechargeCost = parseFloat(data.rechargeCost) || 0;
                        window.currentDeviceReactivationCost = parseFloat(data.reactivationCost) || 0;
                        const devStat = (data.status || data.Status || '').toLowerCase();
                        const isTaisys = netOp.toLowerCase().includes('taisys');
                        if (isTaisys && devStat.includes('suspen') && expDate) {
                            const p = parseDateParts(expDate);
                            if (p) {
                                const nextDt = new Date(p.y, p.m, p.d + 1);
                                const isoNext = `${nextDt.getFullYear()}-${String(nextDt.getMonth() + 1).padStart(2, '0')}-${String(nextDt.getDate()).padStart(2, '0')}`;
                                if (activationInput && (isUserInput || !activationInput.value)) activationInput.value = isoNext;
                                if (expiryInput && (isUserInput || !expiryInput.value)) expiryInput.value = calculateExpiryDate(isoNext, false);
                            }
                        } else if (isTaisys && (devStat.includes('terminat') || devStat.includes('dead'))) {
                            const today = new Date();
                            const todayIso = `${today.getFullYear()}-${String(today.getMonth() + 1).padStart(2, '0')}-${String(today.getDate()).padStart(2, '0')}`;
                            if (activationInput && (isUserInput || !activationInput.value)) activationInput.value = todayIso;
                            if (expiryInput && (isUserInput || !expiryInput.value)) expiryInput.value = calculateExpiryDate(todayIso, false);
                        } else {
                            const pForCheck = (paymentFor && paymentFor.value ? paymentFor.value : '').toLowerCase();
                            const isActMode = pForCheck.includes('activation') || pForCheck.includes('new');
                            const isRechargeMode = pForCheck.includes('recharge') || pForCheck.includes('renewal') || pForCheck.includes('reactivation');

                            if (isRechargeMode) {
                                const txnDateVal = (document.getElementById('txn-date')?.value || new Date().toISOString().split('T')[0]).trim();
                                const todayIso = new Date().toISOString().split('T')[0];
                                const baseAct = txnDateVal || todayIso;
                                if (expDate) {
                                    const parsedExp = formatToIso(expDate);
                                    if (expiryInput) expiryInput.value = parsedExp;
                                    const pExp = parseDateParts(parsedExp);
                                    if (pExp && activationInput) {
                                        const dtAct = new Date(pExp.y - 1, pExp.m, pExp.d);
                                        activationInput.value = `${dtAct.getFullYear()}-${String(dtAct.getMonth() + 1).padStart(2, '0')}-${String(dtAct.getDate()).padStart(2, '0')}`;
                                    }
                                } else {
                                    if (activationInput) activationInput.value = baseAct;
                                    if (expiryInput) expiryInput.value = calculateExpiryDate(baseAct);
                                }
                            } else if (isActMode) {
                                const txnDateVal = (document.getElementById('txn-date')?.value || new Date().toISOString().split('T')[0]).trim();
                                if (activationInput && (isUserInput || !activationInput.value)) {
                                    activationInput.value = txnDateVal || (actDate ? formatToIso(actDate) : '');
                                }
                                updateExpiry(true);
                            } else {
                                if (actDate && activationInput && (isUserInput || !activationInput.value)) {
                                    activationInput.value = formatToIso(actDate);
                                }
                                if (expDate && expiryInput) {
                                    if (isUserInput || !expiryInput.value) {
                                        expiryInput.value = formatToIso(expDate);
                                    }
                                } else if (activationInput && activationInput.value && (!expiryInput || !expiryInput.value)) {
                                    updateExpiry(true);
                                }
                            }
                        }
                    }
                }
                calculateTransactionTotals();
            })
            .catch(e => console.error(e));
        };

        let debounceTimer;
        const onVehicleChange = function(isTyping) {
            clearTimeout(debounceTimer);
            const v = vehicleInput.value.trim();
            if (!v) {
                let err = document.getElementById('vehicle-error');
                if(err) err.style.display = 'none';
                setVendorAuto('');
                if (invoiceIdInput) { invoiceIdInput.value = ''; invoiceIdInput.readOnly = false; }
                if (imeiInput) { imeiInput.value = ''; }
                const lcInput = document.getElementById('txn-lifecycle');
                if (lcInput) lcInput.value = '';
                window.currentDeviceCost = parseFloat('@ViewBag.CostPerDevice') || 0;
                calculateTransactionTotals();
                return;
            }
            if (isTyping) {
                debounceTimer = setTimeout(() => fetchDeviceInfo(v, true), 300);
            } else {
                fetchDeviceInfo(v, true);
            }
        };

        vehicleInput.addEventListener('input', () => onVehicleChange(true));
        vehicleInput.addEventListener('change', () => onVehicleChange(false));
        vehicleInput.addEventListener('blur', () => onVehicleChange(false));

        const initialV = vehicleInput.value.trim();
        const initUrlParams = new URLSearchParams(window.location.search);
        if (initialV || initUrlParams.get('deviceId') || initUrlParams.get('imei')) {
            fetchDeviceInfo(initialV, false);
        }
    }

    if (imeiInput) {
        let imeiDebounceTimer;
        imeiInput.addEventListener('input', function() {
            clearTimeout(imeiDebounceTimer);
            const imeiVal = this.value.trim();
            if (imeiVal && imeiVal.length >= 8) {
                imeiDebounceTimer = setTimeout(() => {
                    const currentVeh = vehicleInput ? vehicleInput.value.trim() : '';
                    if (typeof fetchDeviceInfo === 'function') fetchDeviceInfo(currentVeh, true);
                }, 400);
            }
        });
        imeiInput.addEventListener('change', function() {
            const currentVeh = vehicleInput ? vehicleInput.value.trim() : '';
            if (typeof fetchDeviceInfo === 'function') fetchDeviceInfo(currentVeh, false);
        });
    }

    if (invoiceIdInput) {
        let invDebounceTimer;
        invoiceIdInput.addEventListener('input', function() {
            clearTimeout(invDebounceTimer);
            const inv = this.value.trim();
            if (!inv) {
                return;
            }
            invDebounceTimer = setTimeout(() => {
                fetch('/@currentBranch/Transactions/GetInvoiceCost?invoiceId=' + encodeURIComponent(inv), { credentials: 'same-origin' })
                    .then(res => res.json())
                    .then(data => {
                        if (data && data.unitCost > 0) {
                            window.currentDeviceCost = parseFloat(data.unitCost) || 0;
                            calculateTransactionTotals();
                        }
                    })
                    .catch(e => console.error(e));
            }, 500);
        });
    }

    
    // Vendor selection & Auto Batch ID logic
    const vendorPrefixMap = @Html.Raw(System.Text.Json.JsonSerializer.Serialize(ViewBag.VendorPrefixMap ?? new Dictionary<string, string>()));
    const batchInput = document.getElementById("BatchId");
    const dateInput = document.getElementById("txn-date");
    // vendorSelect, vendorInput, vendorCustomWrap, btnToggleCustom declared above

    let isBatchManuallyEdited = false;
    if (batchInput) {
        batchInput.addEventListener("input", function() {
            isBatchManuallyEdited = this.value.trim().length > 0;
        });
    }

    function generateFallbackPrefix(name) {
        if (!name || !name.trim()) return "VEN";
        const parts = name.trim().split(/[\s\-_\.\/]+/).filter(Boolean);
        if (parts.length === 0) return "VEN";
        if (parts.length >= 3) return (parts[0][0] + parts[1][0] + parts[2][0]).toUpperCase();
        if (parts.length === 2) {
            if (parts[0].length === 3) return parts[0].toUpperCase();
            if (parts[1].length >= 2) return (parts[0][0] + parts[1][0] + parts[1][1]).toUpperCase();
            return (parts[0][0] + parts[1][0] + 'X').toUpperCase();
        }
        const clean = parts[0].replace(/[^a-zA-Z0-9]/g, '');
        return (clean.length >= 3 ? clean.substring(0, 3) : clean.padEnd(3, 'X')).toUpperCase();
    }

    function getVendorPrefix(vendorName) {
        if (!vendorName) return "VEN";
        const trimmed = vendorName.trim();
        if (vendorPrefixMap) {
            if (vendorPrefixMap[trimmed]) return vendorPrefixMap[trimmed].toUpperCase();
            const lower = trimmed.toLowerCase();
            for (const k in vendorPrefixMap) {
                if (k.toLowerCase() === lower && vendorPrefixMap[k]) return vendorPrefixMap[k].toUpperCase();
            }
        }
        return generateFallbackPrefix(trimmed);
    }

    function formatDateDigits(dateVal) {
        if (!dateVal) return "";
        const str = dateVal.trim();
        if (str.includes('-')) {
            const parts = str.split('-');
            if (parts.length === 3) {
                if (parts[0].length === 4) return parts[2].padStart(2, '0') + parts[1].padStart(2, '0') + parts[0];
                if (parts[2].length === 4) return parts[0].padStart(2, '0') + parts[1].padStart(2, '0') + parts[2];
            }
        }
        return str.replace(/\D/g, '');
    }

    window.updateAutoBatchId = function() {
        if (!batchInput) return;
        const currentVendor = (vendorCustomWrap && vendorCustomWrap.style.display !== 'none' && vendorInput.value)
            ? vendorInput.value
            : ((vendorSelect && vendorSelect.value) || (vendorInput && vendorInput.value) || "");
        const dateVal = dateInput ? dateInput.value : "";
        const prefix = getVendorPrefix(currentVendor);
        const dateDigits = formatDateDigits(dateVal);
        if (prefix && dateDigits) {
            batchInput.value = `${prefix} - ${dateDigits}`;
        } else if (prefix) {
            batchInput.value = `${prefix} - `;
        }
    };

    window.toggleCustomVendorInput = function() {
        if (vendorCustomWrap.style.display === 'none') {
            vendorCustomWrap.style.display = 'block';
            if (vendorSelect) vendorSelect.value = '';
            if (btnToggleCustom) btnToggleCustom.textContent = '← Choose from List';
            if (vendorInput) vendorInput.focus();
        } else {
            vendorCustomWrap.style.display = 'none';
            if (btnToggleCustom) btnToggleCustom.textContent = '+ New Vendor';
        }
        window.updateAutoBatchId();
    };

    window.onVendorSelectChange = function(val) {
        if (val && vendorInput) {
            vendorInput.value = val;
            if (vendorCustomWrap) vendorCustomWrap.style.display = 'none';
            if (btnToggleCustom) btnToggleCustom.textContent = '+ New Vendor';
        }
        window.updateAutoBatchId();
        window.checkVendorAdvanceCredit(val);
    };

    window.onVendorInputChange = function(val) {
        window.updateAutoBatchId();
        window.checkVendorAdvanceCredit(val);
    };

    window.toggleAdvanceCredit = function(isApplied) {
        const wrap = document.getElementById('advAmountInputWrap');
        if (wrap) wrap.style.opacity = isApplied ? '1' : '0.4';
        const amtInput = document.getElementById('AdvanceCreditAmount');
        if (amtInput) {
            amtInput.disabled = !isApplied;
            if (isApplied && (!amtInput.value || parseFloat(amtInput.value) <= 0)) {
                const bal = parseFloat(document.getElementById('AvailableAdvanceBalance')?.value) || 0;
                amtInput.value = bal;
            }
        }
        if (window.calculateTransactionTotals) window.calculateTransactionTotals();
        if (window.calculateRemaining) window.calculateRemaining();
    };

    window.onAdvanceAmountChange = function() {
        const amtInput = document.getElementById('AdvanceCreditAmount');
        const bal = parseFloat(document.getElementById('AvailableAdvanceBalance')?.value) || 0;
        if (amtInput) {
            let val = parseFloat(amtInput.value) || 0;
            if (val > bal) {
                val = bal;
                amtInput.value = bal;
            }
        }
        if (window.calculateTransactionTotals) window.calculateTransactionTotals();
        if (window.calculateRemaining) window.calculateRemaining();
    };

    window.checkVendorAdvanceCredit = function(vendor) {
        if (!vendor || !vendor.trim()) {
            const sec = document.getElementById('advanceCreditSection');
            if (sec) sec.style.display = 'none';
            return;
        }
        fetch(`/DMG/Transactions/GetVendorCredit?vendor=${encodeURIComponent(vendor.trim())}`)
            .then(res => res.json())
            .then(data => {
                const sec = document.getElementById('advanceCreditSection');
                if (!sec) return;
                if (data && data.hasCredit && data.remainingBalance > 0) {
                    const nameEl = document.getElementById('advVendorNameText');
                    if (nameEl) nameEl.textContent = vendor.trim();
                    const badgeEl = document.getElementById('advAvailableAmtBadge');
                    if (badgeEl) badgeEl.textContent = formatCurrency(data.remainingBalance);
                    const dtEl = document.getElementById('advDateText');
                    if (dtEl) dtEl.textContent = data.date || '—';
                    const mdEl = document.getElementById('advModeText');
                    if (mdEl) mdEl.textContent = data.paymentMode || '—';
                    const ptEl = document.getElementById('advPaidToText');
                    if (ptEl) ptEl.textContent = data.paidTo || '—';
                    const utrEl = document.getElementById('advUtrText');
                    if (utrEl) utrEl.textContent = data.utr || '—';

                    document.getElementById('AdvanceCreditId').value = data.creditId || '';
                    document.getElementById('AdvanceCreditUtr').value = data.utr || '';
                    document.getElementById('AdvanceCreditDate').value = data.date || '';
                    document.getElementById('AdvanceCreditMode').value = data.paymentMode || '';
                    document.getElementById('AdvanceCreditPaidTo').value = data.paidTo || '';
                    document.getElementById('AvailableAdvanceBalance').value = data.remainingBalance || 0;

                    // User requested: "make the default on and show the amount on the side to see how much is there"
                    const chk = document.getElementById('ApplyAdvanceCredit');
                    if (chk) chk.checked = true;
                    const amtInput = document.getElementById('AdvanceCreditAmount');
                    if (amtInput) {
                        amtInput.disabled = false;
                        amtInput.value = data.remainingBalance;
                    }
                    const wrap = document.getElementById('advAmountInputWrap');
                    if (wrap) wrap.style.opacity = '1';

                    sec.style.display = 'block';
                } else {
                    sec.style.display = 'none';
                    const chk = document.getElementById('ApplyAdvanceCredit');
                    if (chk) chk.checked = false;
                    const amtInput = document.getElementById('AdvanceCreditAmount');
                    if (amtInput) amtInput.value = '';
                    document.getElementById('AdvanceCreditId').value = '';
                }
                if (window.calculateTransactionTotals) window.calculateTransactionTotals();
                if (window.calculateRemaining) window.calculateRemaining();
            })
            .catch(() => {});
    };

    if (dateInput) {
        dateInput.addEventListener("change", function() {
            if (!isBatchManuallyEdited) window.updateAutoBatchId();
        });
    }

    window.calculateRemaining = function() {
        const rawTotal = parseFloat(document.getElementById('TotalPayment')?.value) || 0;
        const advChk = document.getElementById('ApplyAdvanceCredit');
        const advAmt = (advChk && advChk.checked) ? (parseFloat(document.getElementById('AdvanceCreditAmount')?.value) || 0) : 0;
        const total = rawTotal + advAmt;

        let allocated = 0;
        document.querySelectorAll('.bulk-amount-input').forEach(input => {
            allocated += parseFloat(input.value) || 0;
        });
        const unallocated = total - allocated;
        const unallocatedInput = document.getElementById("bulk-unallocated");
        if (unallocatedInput) {
            unallocatedInput.value = formatCurrency(unallocated);
            unallocatedInput.style.color = unallocated < 0 ? "var(--danger)" : "var(--text)";
        }
        const traceAllocated = document.getElementById("trace-allocated");
        const traceUnallocated = document.getElementById("trace-unallocated");
        const traceTotal = document.getElementById("trace-total");
        if (traceAllocated) traceAllocated.textContent = formatCurrency(allocated);
        if (traceTotal) {
            traceTotal.textContent = advAmt > 0 ? `${formatCurrency(total)} (${formatCurrency(rawTotal)} + ${formatCurrency(advAmt)} adv)` : formatCurrency(total);
        }
        if (traceUnallocated) {
            traceUnallocated.textContent = formatCurrency(unallocated);
            traceUnallocated.style.color = unallocated < 0 ? "var(--danger)" : (unallocated === 0 ? "var(--success)" : "var(--text)");
            if (unallocated > 0) {
                traceUnallocated.title = "Excess balance will be saved to vendor advance credit";
            }
        }
    };

    window.reindexBulkRows = function() {
        const rows = document.querySelectorAll('.bulk-row');
        rows.forEach((row, idx) => {
            const veh = row.querySelector('.bulk-vehicle-input');
            const pf = row.querySelector('.bulk-payment-for');
            const amt = row.querySelector('.bulk-amount-input');
            const ext = row.querySelector('.bulk-extras-select');
            const extAmt = row.querySelector('.bulk-extras-amount');
            const op = row.querySelector('.bulk-operator-input');
            const act = row.querySelector('.bulk-activation-input');
            const exp = row.querySelector('.bulk-expiry-input');
            if (veh) veh.name = `BulkAllocations[${idx}].VehicleNo`;
            if (op) op.name = `BulkAllocations[${idx}].NetworkOperator`;
            if (pf) pf.name = `BulkAllocations[${idx}].PaymentFor`;
            if (amt) amt.name = `BulkAllocations[${idx}].Amount`;
            if (ext) ext.name = `BulkAllocations[${idx}].Extras`;
            if (extAmt) extAmt.name = `BulkAllocations[${idx}].ExtrasAmount`;
            if (act) act.name = `BulkAllocations[${idx}].ActivationDate`;
            if (exp) exp.name = `BulkAllocations[${idx}].NewExpiryDate`;
        });
    };

    window.distributeEvenly = function() {
        const rows = document.querySelectorAll('.bulk-row');
        if (rows.length === 0) return;
        const totalInput = document.getElementById('TotalPayment');
    if (totalInput) totalInput.addEventListener('input', function() { calculateTransactionTotals(); window.calculateRemaining && window.calculateRemaining(); });
        const total = parseFloat(totalInput?.value) || 0;
        if (total <= 0) {
            alert('Please enter a valid Total Payment first.');
            totalInput?.focus();
            return;
        }
        const perRow = Math.floor((total / rows.length) * 100) / 100;
        let sum = 0;
        rows.forEach((r, idx) => {
            const input = r.querySelector('.bulk-amount-input');
            if (idx === rows.length - 1) {
                input.value = (total - sum).toFixed(2);
            } else {
                input.value = perRow.toFixed(2);
                sum += perRow;
            }
        });
        calculateTransactionTotals();
        window.calculateRemaining();
    };

    window.setSameAmountForAll = function() {
        const val = prompt('Enter payment amount per vehicle (₹):', '2000');
        if (!val) return;
        const amt = parseFloat(val);
        if (isNaN(amt) || amt <= 0) return;
        const rows = document.querySelectorAll('.bulk-row');
        rows.forEach(r => {
            const input = r.querySelector('.bulk-amount-input');
            if (input) input.value = amt.toFixed(2);
        });
        const total = (amt * rows.length).toFixed(2);
        const totalInput = document.getElementById('TotalPayment');
    if (totalInput) totalInput.addEventListener('input', function() { calculateTransactionTotals(); window.calculateRemaining && window.calculateRemaining(); });
        if (totalInput && (!totalInput.value || parseFloat(totalInput.value) === 0)) {
            totalInput.value = total;
        }
        calculateTransactionTotals();
        window.calculateRemaining();
    };

    window.openQuickPasteModal = function() {
        const text = prompt('Paste vehicle registration numbers (separated by commas, spaces, or lines):');
        if (!text) return;
        const vehicles = text.split(/[\r\n,;\t\s]+/).map(v => v.trim()).filter(v => v.length >= 4);
        if (vehicles.length === 0) {
            alert('No valid vehicle numbers found.');
            return;
        }
        const firstRowVeh = document.querySelector('.bulk-row .bulk-vehicle-input');
        let startIdx = 0;
        if (firstRowVeh && !firstRowVeh.value.trim()) {
            firstRowVeh.value = vehicles[0];
            const firstRow = firstRowVeh.closest('.bulk-row');
            const vehInput = firstRow.querySelector('.bulk-vehicle-input');
            vehInput.dispatchEvent(new Event('change'));
            startIdx = 1;
        }
        for (let i = startIdx; i < vehicles.length; i++) {
            addBulkVehicleRow(vehicles[i], '');
        }
        window.reindexBulkRows();
        calculateTransactionTotals();
        window.calculateRemaining();
    };

    function addBulkVehicleRow(vehNo, amt) {
        const bulkRowsContainer = document.getElementById("bulk-rows-container");
        if (!bulkRowsContainer) return;
        const row = document.createElement("div");
        row.className = "bulk-row";
        row.style.display = "grid";
        row.style.gridTemplateColumns = "minmax(130px, 1.4fr) minmax(90px, 0.9fr) minmax(95px, 0.9fr) minmax(105px, 1fr) minmax(90px, 0.9fr) minmax(110px, 1.1fr) minmax(130px, 1.2fr) minmax(130px, 1.2fr) 36px";
        row.style.gap = "8px";
        row.style.alignItems = "center";
        
        const count = document.querySelectorAll('.bulk-row').length;
        const defExtras = document.getElementById('extras-select') ? document.getElementById('extras-select').value : "None";
        const firstPf = document.querySelector('.bulk-payment-for') ? document.querySelector('.bulk-payment-for').value : "None";
        row.innerHTML = `
            <input name="BulkAllocations[${count}].VehicleNo" type="text" class="input bulk-vehicle-input" value="${vehNo || ''}" placeholder="e.g. KA34C8377" required autocomplete="off" />
            <input type="text" class="input bulk-lifecycle-input" readonly style="background: var(--line); font-size: 0.78rem; font-weight: 600; padding: 6px 8px;" placeholder="Auto-filled" />
            <input name="BulkAllocations[${count}].NetworkOperator" type="text" class="input bulk-operator-input" readonly style="background: var(--line); font-size: 0.78rem; font-weight: 600; padding: 6px 8px;" placeholder="Operator" />
            <select name="BulkAllocations[${count}].PaymentFor" class="select bulk-payment-for" style="font-size: 0.78rem; padding: 6px 4px;">
                <option value="None" ${firstPf === 'None' ? 'selected' : ''}>None</option>
                <option value="Recharge" ${firstPf === 'Recharge' ? 'selected' : ''}>Recharge</option>
                <option value="Activation" ${firstPf === 'Activation' ? 'selected' : ''}>Activation</option>
                <option value="Reactivation" ${firstPf === 'Reactivation' ? 'selected' : ''}>Reactivation</option>
            </select>
            <input name="BulkAllocations[${count}].Amount" type="number" step="0.01" class="input bulk-amount-input" value="${amt || ''}" placeholder="Amount" required />
            <div style="display: flex; gap: 4px; align-items: center;">
                <select name="BulkAllocations[${count}].Extras" class="select bulk-extras-select" style="min-width: 65px; font-size: 0.78rem;" onchange="this.nextElementSibling.style.display = (this.value && this.value !== 'None') ? 'inline-block' : 'none';">
                    <option value="None" ${defExtras === 'None' ? 'selected' : ''}>None</option>
                    <option value="DMG" ${defExtras === 'DMG' ? 'selected' : ''}>DMG</option>
                    <option value="Tracking" ${defExtras === 'Tracking' ? 'selected' : ''}>Tracking</option>
                </select>
                <input name="BulkAllocations[${count}].ExtrasAmount" type="number" step="0.01" class="input bulk-extras-amount" placeholder="Deduct" style="width: 60px; display: none; font-size: 0.78rem;" value="0" />
            </div>
            <input name="BulkAllocations[${count}].ActivationDate" type="date" class="input bulk-activation-input" title="Activation Date" style="font-size: 0.78rem; padding: 6px 4px;" />
            <input name="BulkAllocations[${count}].NewExpiryDate" type="date" class="input bulk-expiry-input" title="New Expiry Date" style="font-size: 0.78rem; padding: 6px 4px;" />
            <button type="button" class="deleteButton" style="width: 30px; height: 30px; justify-self: center;" onclick="if(document.querySelectorAll('.bulk-row').length > 1) { this.closest('.bulk-row').remove(); window.reindexBulkRows(); calculateTransactionTotals(); window.calculateRemaining && window.calculateRemaining(); }">
              <svg viewBox="0 0 448 512" class="bin"><path d="M135.2 17.7L128 32H32C14.3 32 0 46.3 0 64S14.3 96 32 96H416c17.7 0 32-14.3 32-32s-14.3-32-32-32H320l-7.2-14.3C307.4 6.8 296.3 0 284.2 0H163.8c-12.1 0-23.2 6.8-28.6 17.7zM416 128H32L53.2 467c1.6 25.3 22.6 45 47.9 45H346.9c25.3 0 46.3-19.7 47.9-45L416 128z"></path></svg>
            </button>
        `;
        bulkRowsContainer.appendChild(row);
        bindBulkRowEvents(row);
        if (vehNo) {
            const vehInp = row.querySelector('.bulk-vehicle-input');
            vehInp.dispatchEvent(new Event('change'));
        }
    }
    
    function calculateTransactionTotals() {
        window.calculateTransactionTotals = calculateTransactionTotals;
        let amount = 0;
        let total = 0;
        let profit = 0;
        
        const val = paymentFor ? paymentFor.value : '';
        const isActivation = (val === "Activation");
        const extrasElement = document.getElementById('ExtrasAmount');
        const extrasAmount = extrasElement ? (parseFloat(extrasElement.value) || 0) : 0;
        const defaultAdminCost = parseFloat('@(ViewBag.AdminCost ?? "0")') || 0;
        const effectiveAdminCost = (window.currentDeviceAdminCost !== null && window.currentDeviceAdminCost !== undefined)
            ? window.currentDeviceAdminCost
            : defaultAdminCost;
        const adminCostConfig = isActivation ? effectiveAdminCost : 0;
        let transactionCost = 0;

        const chkManual = document.getElementById('chkManualDeviceCost');
        const manualCostInput = document.getElementById('ManualDeviceCost');
        const isManualCost = chkManual && chkManual.checked;

        if (isManualCost && manualCostInput && manualCostInput.value !== '' && !isNaN(parseFloat(manualCostInput.value))) {
            transactionCost = Math.max(0, parseFloat(manualCostInput.value));
        } else {
            if (isActivation) {
                transactionCost = window.currentDeviceCost || 0;
                if (typeSelect && '@currentBranch' === 'RTO') {
                    const branchType = typeSelect.value;
                    const commCost = parseFloat('@ViewBag.CommissionCost') || 0;
                    const simCost = branchType === 'Old' ? (parseFloat('@ViewBag.SimCost1Year') || 0) : (parseFloat('@ViewBag.SimCost2Years') || 0);
                    transactionCost += (commCost + simCost);
                }
            } else if (val === "Reactivation") {
                const netOpEl = document.getElementById('txn-network-operator');
                const curOp = (netOpEl ? netOpEl.value : '') || window.currentDeviceNetworkOperator || '';
                const opReact = getOperatorCost(curOp, 'reactivation');
                transactionCost = opReact > 0 ? opReact : (window.currentDeviceReactivationCost > 0 ? window.currentDeviceReactivationCost : (parseFloat('@ViewBag.CompanyReactRate') || 1534));
            } else if (val === "Recharge" || val === "Renewal" || val.includes("Recharge") || val.includes("Renewal")) {
                const netOpEl = document.getElementById('txn-network-operator');
                const curOp = (netOpEl ? netOpEl.value : '') || window.currentDeviceNetworkOperator || '';
                const opRecharge = getOperatorCost(curOp, 'recharge');
                transactionCost = opRecharge > 0 ? opRecharge : (window.currentDeviceRechargeCost > 0 ? window.currentDeviceRechargeCost : (parseFloat('@ViewBag.CompanyRechargeRate') || 1298));
            } else {
                transactionCost = window.currentDeviceCost || 0;
            }
            
            // Add Admin Cost directly to Total Device Cost before deduction from Base Amount (hidden from separate view)
            transactionCost += adminCostConfig;
        }

        const curMode = getCurrentMode();

        if (curMode === "MergePartPayments") {
            total = parseFloat(fTotal.querySelector('input').value) || 0;
            let mergedSum = 0;
            document.querySelectorAll('.merge-row').forEach(row => {
                const rAmount = parseFloat(row.querySelector('.amount-input').value) || 0;
                mergedSum += rAmount;
            });
            const extrasElement = document.getElementById('ExtrasAmount');
            const extras = extrasElement ? (parseFloat(extrasElement.value) || 0) : 0;
            amount = mergedSum;
            profit = (mergedSum > 0) ? (Math.max(0, mergedSum - extras) - transactionCost) : 0;

                        const remainderCard = document.getElementById('txn-remainder-card');
            const remainderTitle = document.getElementById('txn-remainder-title');
            const remainderAmount = document.getElementById('txn-remainder-amount');
            const remainderSub = document.getElementById('txn-remainder-sub');
            if (remainderCard) {
                remainderCard.style.display = 'block';
                const diffRem = total - mergedSum;
                if (diffRem > 0) {
                    remainderCard.style.border = '2px solid #ef4444';
                    remainderCard.style.background = 'rgba(239, 68, 68, 0.08)';
                    if (remainderTitle) remainderTitle.textContent = 'Remainder (To Collect)';
                    if (remainderAmount) { remainderAmount.textContent = formatCurrency(diffRem); remainderAmount.style.color = '#dc2626'; }
                    if (remainderSub) { remainderSub.textContent = `Remind customer: ${formatCurrency(diffRem)} remaining`; remainderSub.style.color = '#dc2626'; }
                } else if (diffRem === 0) {
                    remainderCard.style.border = '2px solid #22c55e';
                    remainderCard.style.background = 'rgba(34, 197, 94, 0.08)';
                    if (remainderTitle) remainderTitle.textContent = 'Remainder (Balanced)';
                    if (remainderAmount) { remainderAmount.textContent = '₹0.00'; remainderAmount.style.color = '#16a34a'; }
                    if (remainderSub) { remainderSub.textContent = 'Fully Paid'; remainderSub.style.color = '#16a34a'; }
                } else {
                    const overRem = Math.abs(diffRem);
                    remainderCard.style.border = '2px solid #eab308';
                    remainderCard.style.background = 'rgba(234, 179, 8, 0.08)';
                    if (remainderTitle) remainderTitle.textContent = 'Remainder (Overpaid)';
                    if (remainderAmount) { remainderAmount.textContent = `+${formatCurrency(overRem)}`; remainderAmount.style.color = '#ca8a04'; }
                    if (remainderSub) { remainderSub.textContent = `Overpaid by ${formatCurrency(overRem)}`; remainderSub.style.color = '#ca8a04'; }
                }
            }
const balanceCard = document.getElementById('merge-balance-card');
            const balanceText = document.getElementById('merge-balance-text');
            const balanceSub = document.getElementById('merge-balance-sub');
            if (balanceCard && total > 0) {
                balanceCard.style.display = 'block';
                const diff = total - mergedSum;
                if (diff > 0) {
                    balanceCard.style.background = 'rgba(239, 68, 68, 0.08)';
                    balanceCard.style.border = '1px solid #ef4444';
                    balanceCard.style.color = '#b91c1c';
                    balanceText.innerHTML = `⚠️ Remaining Balance: ${formatCurrency(diff)} (Total: ${formatCurrency(total)} — Received: ${formatCurrency(mergedSum)})`;
                    balanceSub.innerHTML = `🔔 A reminder will be automatically added to Reminders to follow up with the customer.`;
                } else if (diff === 0) {
                    balanceCard.style.background = 'rgba(34, 197, 94, 0.08)';
                    balanceCard.style.border = '1px solid #22c55e';
                    balanceCard.style.color = '#15803d';
                    balanceText.innerHTML = `✅ Full Payment Collected: ${formatCurrency(mergedSum)}`;
                    balanceSub.innerHTML = `All payments match expected total.`;
                } else {
                    const over = Math.abs(diff);
                    balanceCard.style.background = 'rgba(234, 179, 8, 0.08)';
                    balanceCard.style.border = '1px solid #eab308';
                    balanceCard.style.color = '#a16207';
                    balanceText.innerHTML = `⚠️ Overpayment of ${formatCurrency(over)} (Received: ${formatCurrency(mergedSum)} > Total Expected: ${formatCurrency(total)})`;
                    balanceSub.innerHTML = ``;
                }
            } else if (balanceCard) {
                balanceCard.style.display = 'none';
            }
        } else if (curMode === "BulkAllocation") {
            total = parseFloat(fTotal.querySelector('input').value) || 0;
            let bulkRowCount = 0;
            let bulkBase = 0;
            let bulkSubtracted = 0;
            let bulkCost = 0;
            amount = 0;
            profit = 0;
            document.querySelectorAll('.bulk-row').forEach(row => {
                const rowAmount = parseFloat(row.querySelector('.bulk-amount-input').value) || 0;
                const rowExtrasElement = row.querySelector('.bulk-extras-amount');
                const rowExtras = rowExtrasElement ? (parseFloat(rowExtrasElement.value) || 0) : 0;
                const rowPfSelect = row.querySelector('.bulk-payment-for');
                const rowPf = (rowPfSelect ? rowPfSelect.value : val) || 'None';
                const rowIsActivation = (rowPf === 'Activation');
                let rowAdminCost = 0;
                let rowUnitCost = 0;

                if (rowIsActivation) {
                    rowUnitCost = row.dataset.unitCost ? parseFloat(row.dataset.unitCost) : window.currentDeviceCost;
                    const devAdmin = row.dataset.adminCost ? parseFloat(row.dataset.adminCost) : null;
                    rowAdminCost = (devAdmin !== null && !isNaN(devAdmin) && devAdmin > 0) ? devAdmin : defaultAdminCost;
                } else if (rowPf === 'Reactivation') {
                    rowUnitCost = (row.dataset.reactivationCost && parseFloat(row.dataset.reactivationCost) > 0)
                        ? parseFloat(row.dataset.reactivationCost)
                        : (parseFloat('@ViewBag.PriceReactivation') || 0);
                } else {
                    rowUnitCost = (row.dataset.rechargeCost && parseFloat(row.dataset.rechargeCost) > 0)
                        ? parseFloat(row.dataset.rechargeCost)
                        : (parseFloat('@ViewBag.PriceRecharge') || 0);
                }

                bulkBase += rowAmount;
                bulkSubtracted += rowExtras;
                bulkCost += rowUnitCost;
                amount += (rowAmount - rowExtras);
                profit += (rowAmount - rowExtras - rowAdminCost) - rowUnitCost;
                bulkRowCount++;
            });

            const tBulkAmt = document.getElementById("trace-bulk-amount");
            const tBulkSub = document.getElementById("trace-bulk-subtracted");
            const tBulkCost = document.getElementById("trace-bulk-cost");
            const tBulkProfit = document.getElementById("trace-bulk-profit");
            const tBulkAmtBreakdown = document.getElementById("trace-bulk-amount-breakdown");
            const tBulkSubBreakdown = document.getElementById("trace-bulk-subtracted-breakdown");
            const tBulkProfitMargin = document.getElementById("trace-bulk-profit-margin");

            if (tBulkAmt) tBulkAmt.textContent = formatCurrency(bulkBase);
            if (tBulkSub) tBulkSub.textContent = formatCurrency(bulkSubtracted);
            if (tBulkCost) tBulkCost.textContent = formatCurrency(bulkCost);
            if (tBulkAmtBreakdown) tBulkAmtBreakdown.textContent = `Allocated across ${bulkRowCount} vehicle${bulkRowCount === 1 ? '' : 's'}`;
            if (tBulkSubBreakdown) tBulkSubBreakdown.textContent = bulkSubtracted > 0 ? `${formatCurrency(bulkSubtracted)} deductions` : 'No deductions';
            if (tBulkProfit) {
                tBulkProfit.textContent = formatCurrency(profit);
                if (profit < 0) tBulkProfit.style.color = "var(--danger)";
                else if (profit > 0) tBulkProfit.style.color = "var(--success)";
                else tBulkProfit.style.color = "inherit";
            }
            if (tBulkProfitMargin) {
                const bMargin = bulkBase > 0 ? ((profit / bulkBase) * 100).toFixed(1) : "0.0";
                tBulkProfitMargin.textContent = `${bMargin}% net margin (${formatCurrency(bulkBase - bulkSubtracted)} net)`;
            }
        } else {
            const baseAmount = parseFloat(fAmount.querySelector('input').value) || 0;
            const extrasElement = document.getElementById('ExtrasAmount');
            const extras = extrasElement ? (parseFloat(extrasElement.value) || 0) : 0;
            amount = baseAmount;
            total = baseAmount;
            const excludingDmg = Math.max(0, baseAmount - extras);
            profit = (baseAmount > 0) ? (excludingDmg - transactionCost) : 0;
            if (amountBreakdown) {
                amountBreakdown.style.display = "none";
            }
        }

        if(amountDisplay) amountDisplay.textContent = formatCurrency(amount);
        if(costDisplay) costDisplay.textContent = formatCurrency(transactionCost);
        if(adminCostDisplay) adminCostDisplay.textContent = formatCurrency(adminCostConfig);
        const adminCostCard = document.getElementById('txn-admin-cost-card');
        if(adminCostCard) adminCostCard.style.display = 'none'; // User explicitly requested to hide admin cost
        if(adminCostBadge) adminCostBadge.style.display = 'none'; // User explicitly requested to hide admin cost
        if(profitDisplay) {
            profitDisplay.textContent = formatCurrency(profit);
            if(profit < 0) profitDisplay.style.color = "var(--danger)";
            else if(profit > 0) profitDisplay.style.color = "var(--success)";
            else profitDisplay.style.color = "inherit";
        }

        // Show extras card only when extras are applied
        const extrasCardEl = document.getElementById('txn-extras-card');
        const extrasDisplay = document.querySelector('[data-txn-extras]');
        if (extrasCardEl && extrasDisplay) {
            const extrasAmt = document.getElementById('ExtrasAmount');
            const extVal = extrasAmt ? (parseFloat(extrasAmt.value) || 0) : 0;
            if (extVal > 0) {
                extrasCardEl.style.display = '';
                extrasDisplay.textContent = '- ' + formatCurrency(extVal);
            } else {
                extrasCardEl.style.display = 'none';
            }
        }

        if (amountBreakdown) {
            amountBreakdown.style.display = "none";
        }
    }

    window.toggleManualDeviceCost = function(isManual) {
        const box = document.getElementById('box-manual-device-cost');
        if (box) {
            box.style.display = isManual ? 'block' : 'none';
        }
        if (isManual) {
            const input = document.getElementById('ManualDeviceCost');
            if (input && !input.value) {
                const curCost = window.currentDeviceCost || 0;
                if (curCost > 0) input.value = curCost.toFixed(2);
            }
        }
        if (window.calculateTransactionTotals) {
            window.calculateTransactionTotals();
        }
    };

    function updateVisibility() {
        const isMerge = mergeToggle && mergeToggle.checked;
        const isBulk = bulkToggle && bulkToggle.checked;
        const fNetOp = document.getElementById('field-network-operator');
        const fExt = document.getElementById('field-extras');
        const fExtAmt = document.getElementById('field-extras-amount');
        const extSelect = document.getElementById('extras-select');

        if (isMerge) {
            currentModeInput.value = "MergePartPayments";
        if (fDate) fDate.style.display = "none";
            fVehicle.style.display = "block";
            fVendor.style.display = "block";
            fInvoiceId.style.display = "block";
            fAmount.style.display = "none";
            fTotal.style.display = "block";
            if (fTotal.querySelector('input')) fTotal.querySelector('input').disabled = false;
            fUtr.style.display = "none";
            fPaidTo.style.display = "none";
            fPaymentMode.style.display = "none";
            fActivation.style.display = "block";
            fExpiry.style.display = "block";
            if (fNetOp) fNetOp.style.display = 'block';
            if (fExt) fExt.style.display = 'block';
            if (fExtAmt) fExtAmt.style.display = (extSelect && extSelect.value && extSelect.value.toLowerCase() !== 'none' && extSelect.value !== '0') ? 'block' : 'none';
            
            fMergeGrid.style.display = "block";
            fBulkGrid.style.display = "none";

            const standardTrace = document.getElementById("standard-trace-grid");
            const bulkTrace = document.getElementById("bulk-trace-grid");
            if (standardTrace) standardTrace.style.display = "grid";
            if (bulkTrace) bulkTrace.style.display = "none";
            if (fBatchId) fBatchId.style.display = "none";
        } else if (isBulk) {
            currentModeInput.value = "BulkAllocation";
        if (fDate) fDate.style.display = "block"; const remCardBlk = document.getElementById('txn-remainder-card'); if (remCardBlk) remCardBlk.style.display = 'none';
            fVehicle.style.display = "none";
            fVendor.style.display = "block";
            fInvoiceId.style.display = "none";
            fAmount.style.display = "none";
            fTotal.style.display = "block";
            if (fBatchId) fBatchId.style.display = "block";
            fUtr.style.display = "block";
            fPaidTo.style.display = "block";
            fPaymentMode.style.display = "block";
            fActivation.style.display = "none";
            fExpiry.style.display = "none";
            if (fNetOp) fNetOp.style.display = 'none';
            if (fExt) fExt.style.display = 'none';
            if (fExtAmt) fExtAmt.style.display = 'none';
            
            fMergeGrid.style.display = "none";
            fBulkGrid.style.display = "block";

            const standardTrace = document.getElementById("standard-trace-grid");
            const bulkTrace = document.getElementById("bulk-trace-grid");
            if (standardTrace) standardTrace.style.display = "none";
            if (bulkTrace) bulkTrace.style.display = "grid";
            if (window.updateAutoBatchId) window.updateAutoBatchId();
            if (window.calculateRemaining) window.calculateRemaining();
        } else {
            currentModeInput.value = "Standard";
        if (fDate) fDate.style.display = "block"; const remCardStd = document.getElementById('txn-remainder-card'); if (remCardStd) remCardStd.style.display = 'none';
            fVehicle.style.display = "block";
            fVendor.style.display = "block";
            fInvoiceId.style.display = "block";
            fAmount.style.display = "block";
            fTotal.style.display = "none";
            if (fTotal.querySelector('input')) fTotal.querySelector('input').disabled = true;
            fUtr.style.display = "block";
            fPaidTo.style.display = "block";
            fPaymentMode.style.display = "block";
            fActivation.style.display = "block";
            fExpiry.style.display = "block";
            if (fNetOp) fNetOp.style.display = 'block';
            if (fExt) fExt.style.display = 'block';
            if (fExtAmt) fExtAmt.style.display = (extSelect && extSelect.value && extSelect.value.toLowerCase() !== 'none' && extSelect.value !== '0') ? 'block' : 'none';
            
            fMergeGrid.style.display = "none";
            fBulkGrid.style.display = "none";

            const standardTrace = document.getElementById("standard-trace-grid");
            const bulkTrace = document.getElementById("bulk-trace-grid");
            if (standardTrace) standardTrace.style.display = "grid";
            if (bulkTrace) bulkTrace.style.display = "none";
            if (fBatchId) fBatchId.style.display = "none";
        }
        
        // Dynamically toggle disabled attribute based on visibility
        document.querySelectorAll('.form-grid input, .form-grid select, .form-grid textarea').forEach(el => {
            if(el.id === 'batch-id' || el.name === 'Notes' || el.id === 'CurrentMode' || el.classList.contains('remaining-input') || el.type === 'checkbox' || el.type === 'radio' || el.id === 'ExtrasAmount' || el.id === 'txn-network-operator' || el.id === 'txn-lifecycle' || el.id === 'txn-imei' || el.id === 'InvoiceId' || el.classList.contains('sfd-search-input')) return;
            
            const field = el.closest('.field, .field.full, .advanced-row');
            if (field && field.style.display === 'none') {
                el.disabled = true;
                el.required = false;
            } else {
                // If inside custom vendor wrap that is hidden, keep disabled
                if (el.id === 'vendorInput' && document.getElementById('vendorCustomWrap')?.style.display === 'none') {
                    el.disabled = false; // keep enabled so value submits
                    el.required = false;
                } else if (!el.classList.contains('sfd-search-input')) {
                    el.disabled = false;
                }
            }
        });
        calculateTransactionTotals();
    }

    window.setTransactionMode = function(mode) {
        const currentModeInput = document.getElementById('CurrentMode');
        if (currentModeInput) currentModeInput.value = mode;

        const stdToggle = document.getElementById('standard-toggle');
        const mergeToggle = document.getElementById('merge-toggle');
        const bulkToggle = document.getElementById('bulk-toggle');

        if (stdToggle) stdToggle.checked = (mode === 'Standard');
        if (mergeToggle) mergeToggle.checked = (mode === 'MergePartPayments');
        if (bulkToggle) bulkToggle.checked = (mode === 'BulkAllocation');

        try {
            const url = new URL(window.location.href);
            const modeParam = mode === 'MergePartPayments' ? 'merge' : (mode === 'BulkAllocation' ? 'bulk' : 'standard');
            url.searchParams.set('mode', modeParam);
            window.history.replaceState({}, '', url.toString());
            sessionStorage.setItem('dmg_create_txn_mode', modeParam);
        } catch(e) {}

        updateVisibility();
    };

    const urlParamsMode = new URLSearchParams(window.location.search);
    const qMode = (urlParamsMode.get('mode') || urlParamsMode.get('type') || sessionStorage.getItem('dmg_create_txn_mode') || '@(Model?.CurrentMode.ToString() ?? "Standard")').toLowerCase();
    if (qMode.includes('merge')) {
        setTransactionMode('MergePartPayments');
    } else if (qMode.includes('bulk')) {
        setTransactionMode('BulkAllocation');
    } else {
        setTransactionMode('Standard');
    }

    document.querySelectorAll('input[name="TransactionTypeSelector"]').forEach(radio => {
        radio.addEventListener("change", function() {
            setTransactionMode(this.value);
        });
    });

    // Dynamic grid logic
    const addRowBtn = document.getElementById("add-merge-row");
    const rowsContainer = document.getElementById("merge-rows-container");
    let rowCount = 1;

    addRowBtn.addEventListener("click", function() {
        const row = document.createElement("div");
        row.className = "merge-row";
        row.style.display = "flex";
        row.style.gap = "8px";
        row.style.alignItems = "center";
        row.style.flexWrap = "wrap";
        
        row.innerHTML = `
            <input name="MergePayments[${rowCount}].Date" type="date" class="input date-input" style="width: 140px;" value="${new Date().toISOString().split('T')[0]}" required title="Payment Date" />
            <select name="MergePayments[${rowCount}].PaymentMode" class="select payment-mode-input" style="width: 120px;" required>
                <option value="">Mode</option>
                <option value="UPI">UPI</option>
                <option value="Cash">Cash</option>
                <option value="Banking">Banking</option>
            </select>
            <input name="MergePayments[${rowCount}].Amount" type="number" step="0.01" class="input amount-input" placeholder="Amount" style="width: 100px;" />
            <div style="flex: 1; min-width: 150px; display: flex; flex-direction: column;">
                <input name="MergePayments[${rowCount}].Utr" type="text" class="input utr-input" placeholder="UTR" />
                <span class="utr-balance-msg" style="color: var(--danger); font-size: 0.75rem; margin-top: 4px; display: none;"></span>
            </div>
            <input name="MergePayments[${rowCount}].PaidTo" type="text" class="input" placeholder="Paid To" style="width: 120px;" />
            <button type="button" class="deleteButton" style="width: 32px; height: 32px;" onclick="if(document.querySelectorAll('.merge-row').length > 1) this.parentElement.remove()">
              <svg viewBox="0 0 448 512" class="bin"><path d="M135.2 17.7L128 32H32C14.3 32 0 46.3 0 64S14.3 96 32 96H416c17.7 0 32-14.3 32-32s-14.3-32-32-32H320l-7.2-14.3C307.4 6.8 296.3 0 284.2 0H163.8c-12.1 0-23.2 6.8-28.6 17.7zM416 128H32L53.2 467c1.6 25.3 22.6 45 47.9 45H346.9c25.3 0 46.3-19.7 47.9-45L416 128z"></path></svg>
            </button>
        `;
        rowsContainer.appendChild(row);
        bindRowEvents(row);
        rowCount++;
    });

    // Bulk Allocation Grid Logic
    const bulkRowsContainer = document.getElementById("bulk-rows-container");
    let bulkRowCount = 1;

    function bindBulkRowEvents(row) {
        const actInput = row.querySelector('.bulk-activation-input');
        const expInput = row.querySelector('.bulk-expiry-input');
        const vehInput = row.querySelector('.bulk-vehicle-input');
        const amtInput = row.querySelector('.bulk-amount-input');
        const extrasAmountEl = row.querySelector('.bulk-extras-amount');
        const lcInput = row.querySelector('.bulk-lifecycle-input');
        const opInput = row.querySelector('.bulk-operator-input');
        const pfSelect = row.querySelector('.bulk-payment-for');

        if (amtInput) {
            amtInput.addEventListener("input", function() {
                calculateTransactionTotals();
                window.calculateRemaining && window.calculateRemaining();
            });
        }
        if (extrasAmountEl) {
            extrasAmountEl.addEventListener("input", function() {
                calculateTransactionTotals();
                window.calculateRemaining && window.calculateRemaining();
            });
        }
        if (pfSelect) {
            pfSelect.addEventListener("change", function() {
                calculateTransactionTotals();
            });
        }

        const updateRowExpiry = function() {
            if (!actInput || !expInput) return;
            if (!actInput.value) { expInput.value = ''; return; }
            const isRtoNew = false;
            const d = new Date(actInput.value);
            if (isNaN(d.getTime())) return;
            d.setFullYear(d.getFullYear() + (isRtoNew ? 2 : 1));
            expInput.value = d.toISOString().split('T')[0];
        };

        if (actInput) {
            actInput.addEventListener("change", updateRowExpiry);
        }

        if (vehInput) {
            let bDebounce;
            const triggerLookup = function(v) {
                if (!v || v.length < 3) return;
                fetch('/@currentBranch/Transactions/GetDeviceInfo?vehicleNo=' + encodeURIComponent(v), { credentials: 'same-origin' })
                    .then(r => r.json())
                    .then(data => {
                        if (data && (data.isFound || data.IsFound)) {
                            const bAct = data.activationDate || data.ActivationDate;
                            const bExp = data.latestExpiryDate || data.LatestExpiryDate;
                            const status = (data.status || data.Status || '').trim();
                            const netOp = (data.networkOperator || data.NetworkOperator || '').trim();

                            if (lcInput) lcInput.value = status;
                            if (opInput) opInput.value = netOp;

                            row.dataset.unitCost = data.unitCost || 0;
                            row.dataset.networkOperator = netOp;
                            row.dataset.rechargeCost = data.rechargeCost || 0;
                            row.dataset.reactivationCost = data.reactivationCost || 0;
                            row.dataset.adminCost = data.adminCost || 0;

                            if (bAct && actInput && (!actInput.value || row.dataset.autoFilled === 'true')) {
                                actInput.value = formatToIso(bAct);
                            }
                            if (bExp && expInput && (!expInput.value || row.dataset.autoFilled === 'true')) {
                                expInput.value = formatToIso(bExp);
                            }
                            row.dataset.autoFilled = 'true';
                            calculateTransactionTotals();
                        }
                    })
                    .catch(e => console.error(e));
            };

            vehInput.addEventListener('input', function() {
                clearTimeout(bDebounce);
                const v = this.value.trim();
                bDebounce = setTimeout(() => triggerLookup(v), 400);
            });
            vehInput.addEventListener('change', function() {
                triggerLookup(this.value.trim());
            });
            vehInput.addEventListener('blur', function() {
                if (!row.dataset.autoFilled) {
                    triggerLookup(this.value.trim());
                }
            });
        }
    }

    document.querySelectorAll('.bulk-row').forEach(r => bindBulkRowEvents(r));

    // addBulkRowBtn already bound above

    fTotal.querySelector('input').addEventListener("input", calculateTransactionTotals);
    document.querySelector('.bulk-amount-input').addEventListener("input", calculateTransactionTotals);



    function bindRowEvents(row) {
        const inputs = row.querySelectorAll('.utr-input, .amount-input');
        inputs.forEach(input => {
            input.addEventListener('input', () => {
                setTimeout(() => {
                    calculateTransactionTotals();
                }, 100); // debounce slightly
            });
        });
    }

    document.querySelectorAll('.merge-row').forEach(bindRowEvents);

    window.clearMergePayments = function() {
        const container = document.getElementById('merge-rows-container');
        if (!container) return;
        container.innerHTML = '';
        const row = document.createElement('div');
        row.className = 'merge-row';
        row.style.cssText = 'display: flex; gap: 8px; align-items: center; flex-wrap: wrap;';
        row.innerHTML = `
            <input name="MergePayments[0].Date" type="date" class="input date-input" style="width: 140px;" value="${new Date().toISOString().split('T')[0]}" required title="Payment Date" />
            <select name="MergePayments[0].PaymentMode" class="select payment-mode-input" style="width: 120px;" required>
                <option value="">Mode</option>
                <option value="UPI">UPI</option>
                <option value="Cash">Cash</option>
                <option value="Banking">Banking</option>
            </select>
            <input name="MergePayments[0].Amount" type="number" step="0.01" class="input amount-input" placeholder="Amount" style="width: 100px;" />
            <div style="flex: 1; min-width: 150px; display: flex; flex-direction: column;">
                <input name="MergePayments[0].Utr" type="text" class="input utr-input" placeholder="UTR" />
                <span class="utr-balance-msg" style="color: var(--danger); font-size: 0.75rem; margin-top: 4px; display: none;"></span>
            </div>
            <input name="MergePayments[0].PaidTo" type="text" class="input" placeholder="Paid To" style="width: 120px;" />
            <button type="button" class="deleteButton" style="width: 32px; height: 32px;" onclick="if(document.querySelectorAll('.merge-row').length > 1) this.parentElement.remove()">
              <svg viewBox="0 0 448 512" class="bin"><path d="M135.2 17.7L128 32H32C14.3 32 0 46.3 0 64S14.3 96 32 96H416c17.7 0 32-14.3 32-32s-14.3-32-32-32H320l-7.2-14.3C307.4 6.8 296.3 0 284.2 0H163.8c-12.1 0-23.2 6.8-28.6 17.7zM416 128H32L53.2 467c1.6 25.3 22.6 45 47.9 45H346.9c25.3 0 46.3-19.7 47.9-45L416 128z"></path></svg>
            </button>
        `;
        container.appendChild(row);
        bindRowEvents(row);
        rowCount = 1;
        calculateTransactionTotals();
    };

    window.setSamePaymentForForAll = function() {
        const firstSel = document.querySelector('.bulk-payment-for');
        if (!firstSel) { alert('No bulk rows found.'); return; }
        const val = firstSel.value;
        document.querySelectorAll('.bulk-payment-for').forEach(sel => { sel.value = val; });
        calculateTransactionTotals();
    };

    // Dynamic Extras amount toggle & deduction
    window.updateExtrasAmountVisibility = function() {
        const extSelect = document.getElementById('extras-select');
        const fExtAmt = document.getElementById('field-extras-amount');
        if (!extSelect || !fExtAmt) return;
        const val = (extSelect.value || '').trim();
        const hasExtras = val && val.toLowerCase() !== 'none' && val !== '0' && val !== '';
        fExtAmt.style.display = hasExtras ? 'block' : 'none';
        const amtInput = document.getElementById('ExtrasAmount');
        if (!hasExtras && amtInput) {
            amtInput.value = '0';
        }
        calculateTransactionTotals();
    };

    const extrasSelectGlobal = document.getElementById('extras-select');
    if (extrasSelectGlobal) {
        extrasSelectGlobal.addEventListener('change', window.updateExtrasAmountVisibility);
        extrasSelectGlobal.addEventListener('input', window.updateExtrasAmountVisibility);
    }
    document.addEventListener('change', function(e) {
        if (e.target && (e.target.id === 'extras-select' || e.target.name === 'Extras')) {
            window.updateExtrasAmountVisibility();
        }
    });
    const extrasAmountGlobal = document.getElementById('ExtrasAmount');
    if (extrasAmountGlobal) {
        extrasAmountGlobal.addEventListener('input', calculateTransactionTotals);
    }
    window.updateExtrasAmountVisibility();

    // Initialize extras field visibility based on current paymentFor value
    (function initExtrasVisibility() {
        const pVal = paymentFor ? paymentFor.value : '';
        const fExt = document.getElementById('field-extras');
        const fNetOp = document.getElementById('field-network-operator');
        if (fExt) fExt.style.display = (getCurrentMode() === 'BulkAllocation') ? 'none' : 'block';
        if (fNetOp) fNetOp.style.display = (pVal === 'Recharge' || pVal === 'Reactivation') ? 'block' : 'none';
    })();

    // Bind addBulkRowBtn
    if (addBulkRowBtn) {
        addBulkRowBtn.addEventListener("click", function() {
            addBulkVehicleRow('', '');
            window.reindexBulkRows();
        });
    }
    
    // Bind listeners for standard fields
    fAmount.querySelector('input').addEventListener('input', calculateTransactionTotals);
    fTotal.querySelector('input').addEventListener('input', calculateTransactionTotals);

    // Form submission validation & vendor synchronization guard
    const mainForm = document.querySelector('form.form-card');
    if (mainForm) {
        mainForm.addEventListener('submit', function(e) {
            // 1. Sync Vendor input with vendorSelect dropdown
            const vSel = document.getElementById('vendorSelect');
            const vInp = document.getElementById('vendorInput');
            const vWrap = document.getElementById('vendorCustomWrap');
            if (vSel && vInp && (!vWrap || vWrap.style.display === 'none') && vSel.value) {
                vInp.value = vSel.value;
                vInp.disabled = false;
            }

            // 2. Mode specific validation
            const modeEl = document.getElementById('CurrentMode');
            const curMode = modeEl ? modeEl.value : '';
            if (curMode === 'MergePartPayments') {
                const veh = document.getElementById('vehicle-input');
                if (!veh || !veh.value.trim()) {
                    alert('Please enter a vehicle number.');
                    veh?.focus();
                    e.preventDefault();
                    return false;
                }

                let totalValidAmt = 0;
                let hasValidRow = false;
                document.querySelectorAll('.merge-row').forEach(row => {
                    const amtInput = row.querySelector('.amount-input');
                    const modeInput = row.querySelector('.payment-mode-input');
                    const amt = parseFloat(amtInput?.value) || 0;
                    if (amt > 0 && modeInput && modeInput.value) {
                        hasValidRow = true;
                        totalValidAmt += amt;
                    }
                });

                if (!hasValidRow) {
                    alert('Please specify at least one payment with an amount and payment mode.');
                    e.preventDefault();
                    return false;
                }
            } else if (curMode === 'Standard') {
                const veh = document.getElementById('vehicle-input');
                if (!veh || !veh.value.trim()) {
                    alert('Please enter a vehicle number.');
                    veh?.focus();
                    e.preventDefault();
                    return false;
                }
                const amt = document.querySelector('#field-amount input');
                if (!amt || !amt.value || parseFloat(amt.value) <= 0) {
                    alert('Please enter a valid transaction amount.');
                    amt?.focus();
                    e.preventDefault();
                    return false;
                }
            }
        });
    }

    // Auto-check advance credit on initial page load if vendor is pre-filled
    (function initVendorAdvanceCheck() {
        const vSel = document.getElementById('vendorSelect');
        const vInp = document.getElementById('vendorInput');
        const initialVendor = (vSel && vSel.value) ? vSel.value : (vInp ? vInp.value : '');
        if (initialVendor && window.checkVendorAdvanceCredit) {
            window.checkVendorAdvanceCredit(initialVendor);
        }
    })();
