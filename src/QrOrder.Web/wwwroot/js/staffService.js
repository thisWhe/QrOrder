(function () {
    const storageKey = "staff:service";
    const status = {
        ready: 2,
        delivered: 3,
        canceled: 4
    };

    function createState(root) {
        const saved = JSON.parse(localStorage.getItem(storageKey) || "{}");
        return {
            root,
            tenantSlug: saved.tenantSlug || "demo-cafe",
            email: saved.email || "service@demo.com",
            password: "",
            token: saved.token || "",
            readyOrders: [],
            serviceCalls: [],
            knownReadyOrderIds: null,
            knownServiceCallIds: null,
            soundEnabled: saved.soundEnabled === true,
            audioContext: null,
            loading: false,
            realtimeConnected: false,
            socket: null,
            error: "",
            info: ""
        };
    }

    function saveSession(state) {
        localStorage.setItem(storageKey, JSON.stringify({
            tenantSlug: state.tenantSlug,
            email: state.email,
            token: state.token,
            soundEnabled: state.soundEnabled
        }));
    }

    function clearSession(state) {
        if (state.socket) {
            state.socket.close();
        }

        state.token = "";
        state.socket = null;
        state.realtimeConnected = false;
        localStorage.removeItem(storageKey);
    }

    async function requestJson(url, options) {
        const response = await fetch(url, options);
        if (!response.ok) {
            throw new Error(friendlyHttpError(response.status, await response.text() || response.statusText));
        }

        if (response.status === 204) return null;
        return await response.json();
    }

    function friendlyHttpError(status, message) {
        const text = String(message || "");
        if (status === 401 || /unauthorized/i.test(text)) return "Oturum gecersiz veya giris bilgileri hatali.";
        if (status === 403) return "Bu islem icin yetkiniz yok.";
        if (status === 409) return "Bu siparisin durumu baska bir ekrandan degismis olabilir. Listeyi yenileyin.";
        if (status >= 500 || isTechnicalMessage(text)) return "Sunucuda beklenmeyen bir hata olustu.";
        return text.length > 160 ? "Islem tamamlanamadi. Lutfen tekrar deneyin." : text || "Islem tamamlanamadi.";
    }

    function normalizeErrorMessage(message) {
        const text = String(message || "").trim();
        if (!text) return "Islem tamamlanamadi.";
        if (isAuthError(text)) return "Oturum gecersiz. Tekrar giris yapin.";
        if (/failed to fetch|network/i.test(text)) return "Sunucuya ulasilamadi. Uygulamanin calistigindan emin olun.";
        if (isTechnicalMessage(text) || text.length > 160) return "Islem tamamlanamadi. Lutfen tekrar deneyin.";
        return text;
    }

    function contextError(prefix, message) {
        return `${prefix}: ${normalizeErrorMessage(message)}`;
    }

    function isTechnicalMessage(message) {
        return /^System\.|Microsoft\.| at |HEADERS|stack trace|Exception:|InvalidOperationException|NotSupportedException|SqlException/i.test(String(message || ""));
    }

    async function login(state) {
        state.error = "";
        state.loading = true;
        render(state);

        try {
            const result = await requestJson("/staff/auth/login", {
                method: "POST",
                headers: {
                    "Accept": "application/json",
                    "Content-Type": "application/json"
                },
                body: JSON.stringify({
                    tenantSlug: state.tenantSlug,
                    email: state.email,
                    password: state.password
                })
            });

            state.token = result.token;
            state.password = "";
            saveSession(state);
            await loadDashboard(state);
            connectRealtime(state);
        } catch (error) {
            state.error = loginErrorMessage(error.message);
        } finally {
            state.loading = false;
            render(state);
        }
    }

    function loginErrorMessage(message) {
        if (isAuthError(message)) {
            return "Giris basarisiz. Isletme, e-posta veya sifre hatali.";
        }

        if (/failed to fetch|network/i.test(message)) {
            return "Sunucuya ulasilamadi. Uygulamanin calistigindan emin olun.";
        }

        return "Giris basarisiz. Bilgileri kontrol edip tekrar deneyin.";
    }

    function isAuthError(message) {
        return /unauthorized|oturum gecersiz|giris bilgileri hatali|staff token/i.test(String(message || ""));
    }

    async function authorizedJson(state, url, options) {
        const headers = Object.assign({
            "Accept": "application/json",
            "Authorization": `Bearer ${state.token}`
        }, options && options.headers ? options.headers : {});

        return await requestJson(url, Object.assign({}, options, { headers }));
    }

    async function loadOrders(state) {
        if (!state.token) return;

        await loadDashboard(state);
    }

    async function loadDashboard(state) {
        if (!state.token) return;

        state.error = "";
        state.loading = true;

        try {
            const previousOrderIds = state.knownReadyOrderIds;
            const previousCallIds = state.knownServiceCallIds;
            const orders = await authorizedJson(state, "/staff/orders?status=2", {});
            const calls = await authorizedJson(state, "/staff/service-calls?activeOnly=true", {});
            const nextOrderIds = new Set(orders.map(order => String(order.id)));
            const nextCallIds = new Set(calls.map(call => String(call.id)));

            state.readyOrders = orders;
            state.serviceCalls = calls;
            state.knownReadyOrderIds = nextOrderIds;
            state.knownServiceCallIds = nextCallIds;
            state.info = `Son guncelleme: ${new Date().toLocaleTimeString("tr-TR")}`;

            const hasNewOrder = previousOrderIds && orders.some(order => !previousOrderIds.has(String(order.id)));
            const hasNewCall = previousCallIds && calls.some(call => !previousCallIds.has(String(call.id)));
            if (state.soundEnabled && (hasNewOrder || hasNewCall)) {
                playServiceAlert(state);
            }
        } catch (error) {
            if (isAuthError(error.message)) {
                clearSession(state);
                state.error = "Oturum gecersiz. Tekrar giris yapin.";
            } else {
                state.error = contextError("Servis verileri alinamadi", error.message);
            }
        } finally {
            state.loading = false;
            render(state);
        }
    }

    async function enableSound(state) {
        state.error = "";

        try {
            state.audioContext = state.audioContext || new (window.AudioContext || window.webkitAudioContext)();
            if (state.audioContext.state === "suspended") {
                await state.audioContext.resume();
            }

            state.soundEnabled = true;
            saveSession(state);
            playServiceAlert(state);
        } catch (error) {
            state.soundEnabled = false;
            state.error = contextError("Ses acilamadi", error.message);
        }

        render(state);
    }

    function disableSound(state) {
        state.soundEnabled = false;
        saveSession(state);
        render(state);
    }

    function playServiceAlert(state) {
        try {
            const AudioContextType = window.AudioContext || window.webkitAudioContext;
            if (!AudioContextType) return;

            state.audioContext = state.audioContext || new AudioContextType();
            const context = state.audioContext;
            if (context.state === "suspended") return;

            playTone(context, 880, 0.00, 0.12);
            playTone(context, 1175, 0.16, 0.14);
        } catch {
            // Ses uyarisi yardimci ozellik; hata siparis ekranini bozmamali.
        }
    }

    function playTone(context, frequency, delay, duration) {
        const oscillator = context.createOscillator();
        const gain = context.createGain();
        const start = context.currentTime + delay;
        const end = start + duration;

        oscillator.type = "sine";
        oscillator.frequency.setValueAtTime(frequency, start);
        gain.gain.setValueAtTime(0.0001, start);
        gain.gain.exponentialRampToValueAtTime(0.18, start + 0.015);
        gain.gain.exponentialRampToValueAtTime(0.0001, end);

        oscillator.connect(gain);
        gain.connect(context.destination);
        oscillator.start(start);
        oscillator.stop(end + 0.02);
    }

    async function updateStatus(state, orderId, nextStatus) {
        state.error = "";

        try {
            await authorizedJson(state, `/staff/orders/${encodeURIComponent(orderId)}/status`, {
                method: "PATCH",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ status: nextStatus })
            });

            await loadDashboard(state);
        } catch (error) {
            state.error = contextError("Durum guncellenemedi", error.message);
            render(state);
        }
    }

    async function completeServiceCall(state, callId) {
        state.error = "";

        try {
            await authorizedJson(state, `/staff/service-calls/${encodeURIComponent(callId)}/complete`, {
                method: "PATCH"
            });

            await loadDashboard(state);
        } catch (error) {
            state.error = contextError("Garson cagrisi kapatilamadi", error.message);
            render(state);
        }
    }

    async function negotiate(state) {
        return await authorizedJson(state, "/hubs/staff-orders/negotiate?negotiateVersion=1", {
            method: "POST"
        });
    }

    function signalRMessage(payload) {
        return `${JSON.stringify(payload)}\x1e`;
    }

    async function connectRealtime(state) {
        if (!state.token || state.socket) return;

        try {
            const result = await negotiate(state);
            const token = result.connectionToken || result.connectionId;
            const protocol = window.location.protocol === "https:" ? "wss" : "ws";
            const socket = new WebSocket(`${protocol}://${window.location.host}/hubs/staff-orders?id=${encodeURIComponent(token)}&access_token=${encodeURIComponent(state.token)}`);

            state.socket = socket;

            socket.addEventListener("open", () => {
                socket.send(signalRMessage({ protocol: "json", version: 1 }));
            });

            socket.addEventListener("message", event => {
                const messages = String(event.data).split("\x1e").filter(Boolean);
                for (const raw of messages) {
                    const message = JSON.parse(raw);
                    if (message.type === 1 && (
                        message.target === "OrderCreated" ||
                        message.target === "OrderStatusChanged" ||
                        message.target === "ServiceCallCreated" ||
                        message.target === "ServiceCallCompleted")) {
                        loadDashboard(state);
                    } else {
                        state.realtimeConnected = true;
                        render(state);
                    }
                }
            });

            socket.addEventListener("close", () => {
                state.socket = null;
                state.realtimeConnected = false;
                render(state);

                if (state.token) {
                    window.setTimeout(() => connectRealtime(state), 3000);
                }
            });

            socket.addEventListener("error", () => {
                state.realtimeConnected = false;
            });
        } catch {
            state.realtimeConnected = false;
        }
    }

    function money(value) {
        return Number(value).toLocaleString("tr-TR", {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        });
    }

    function orderAge(createdAt) {
        const minutes = Math.max(0, Math.floor((Date.now() - new Date(createdAt).getTime()) / 60000));
        if (minutes < 1) return "Simdi hazir";
        if (minutes < 60) return `${minutes} dk once`;

        const hours = Math.floor(minutes / 60);
        const remainingMinutes = minutes % 60;
        return remainingMinutes === 0
            ? `${hours} sa once`
            : `${hours} sa ${remainingMinutes} dk once`;
    }

    function callAge(createdAt) {
        const minutes = Math.max(0, Math.floor((Date.now() - new Date(createdAt).getTime()) / 60000));
        if (minutes < 1) return "Simdi cagrildi";
        if (minutes < 60) return `${minutes} dk once`;

        const hours = Math.floor(minutes / 60);
        const remainingMinutes = minutes % 60;
        return remainingMinutes === 0
            ? `${hours} sa once`
            : `${hours} sa ${remainingMinutes} dk once`;
    }

    function sortByOldest(orders) {
        return [...orders].sort((a, b) => new Date(a.createdAt) - new Date(b.createdAt));
    }

    function renderLogin(state) {
        state.root.innerHTML = "";

        const panel = document.createElement("section");
        panel.className = "login-panel";
        panel.innerHTML = "<h1>Servis Girisi</h1>";

        panel.appendChild(inputField("Isletme", state.tenantSlug, value => state.tenantSlug = value));
        panel.appendChild(inputField("E-posta", state.email, value => state.email = value));
        panel.appendChild(inputField("Sifre", state.password, value => state.password = value, "password"));

        if (state.error) {
            const error = document.createElement("div");
            error.className = "alert error";
            error.textContent = state.error;
            panel.appendChild(error);
        }

        const button = document.createElement("button");
        button.type = "button";
        button.className = "button primary";
        button.textContent = state.loading ? "Giris yapiliyor" : "Giris Yap";
        button.disabled = state.loading;
        button.addEventListener("click", () => login(state));
        panel.appendChild(button);

        state.root.appendChild(panel);
    }

    function inputField(labelText, value, onInput, type) {
        const field = document.createElement("label");
        field.className = "field";

        const label = document.createElement("span");
        label.textContent = labelText;

        const input = document.createElement("input");
        input.type = type || "text";
        input.value = value;
        input.addEventListener("input", () => onInput(input.value));

        field.appendChild(label);
        field.appendChild(input);
        return field;
    }

    function render(state) {
        if (!state.token) {
            renderLogin(state);
            return;
        }

        state.root.innerHTML = "";

        const header = document.createElement("header");
        header.className = "kitchen-header";

        const titleWrap = document.createElement("div");
        titleWrap.innerHTML = `<h1 class="kitchen-title">Servis Ekrani</h1><div class="kitchen-meta"><span class="status-dot ${state.realtimeConnected ? "connected" : ""}"></span><span>${state.realtimeConnected ? "Canli" : "Beklemede"}</span><span>${state.info || ""}</span></div>`;

        const toolbar = document.createElement("div");
        toolbar.className = "toolbar";

        const refresh = document.createElement("button");
        refresh.type = "button";
        refresh.className = "button";
        refresh.textContent = state.loading ? "Yukleniyor" : "Yenile";
        refresh.disabled = state.loading;
        refresh.addEventListener("click", () => loadDashboard(state));

        const sound = document.createElement("button");
        sound.type = "button";
        sound.className = `button ${state.soundEnabled ? "sound-on" : ""}`;
        sound.textContent = state.soundEnabled ? "Ses Acik" : "Sesi Ac";
        sound.addEventListener("click", () => {
            if (state.soundEnabled) disableSound(state);
            else enableSound(state);
        });

        const logout = document.createElement("button");
        logout.type = "button";
        logout.className = "button";
        logout.textContent = "Cikis";
        logout.addEventListener("click", () => {
            clearSession(state);
            render(state);
        });

        toolbar.appendChild(refresh);
        toolbar.appendChild(sound);
        toolbar.appendChild(logout);
        header.appendChild(titleWrap);
        header.appendChild(toolbar);
        state.root.appendChild(header);

        if (state.error) {
            const error = document.createElement("div");
            error.className = "alert error";
            error.style.margin = "16px";
            error.textContent = state.error;
            state.root.appendChild(error);
        }

        const board = document.createElement("section");
        board.className = "board";
        board.classList.add("service-board");
        board.appendChild(renderLane(state, "Servise Hazir Siparisler", sortByOldest(state.readyOrders)));
        board.appendChild(renderServiceCallLane(state));
        state.root.appendChild(board);
    }

    function renderLane(state, title, orders) {
        const lane = document.createElement("section");
        lane.className = "lane";

        const head = document.createElement("div");
        head.className = "lane-head";
        head.innerHTML = `<h2>${title}</h2><span class="count-pill">${orders.length}</span>`;
        lane.appendChild(head);

        const list = document.createElement("div");
        list.className = "order-list";

        if (orders.length === 0) {
            const empty = document.createElement("div");
            empty.className = "empty";
            empty.textContent = "Servise hazir siparis yok.";
            list.appendChild(empty);
        } else {
            for (const order of orders) {
                list.appendChild(renderOrderCard(state, order));
            }
        }

        lane.appendChild(list);
        return lane;
    }

    function renderServiceCallLane(state) {
        const lane = document.createElement("section");
        lane.className = "lane";

        const head = document.createElement("div");
        head.className = "lane-head";
        head.innerHTML = `<h2>Garson Cagrilari</h2><span class="count-pill">${state.serviceCalls.length}</span>`;
        lane.appendChild(head);

        const list = document.createElement("div");
        list.className = "order-list";

        if (state.serviceCalls.length === 0) {
            const empty = document.createElement("div");
            empty.className = "empty";
            empty.textContent = "Aktif garson cagrisi yok.";
            list.appendChild(empty);
        } else {
            for (const call of sortByOldest(state.serviceCalls)) {
                list.appendChild(renderServiceCallCard(state, call));
            }
        }

        lane.appendChild(list);
        return lane;
    }

    function renderServiceCallCard(state, call) {
        const card = document.createElement("article");
        card.className = "order-card service-call-card fresh";

        const createdAt = new Date(call.createdAt);

        const top = document.createElement("div");
        top.className = "order-top";
        top.innerHTML = `<div><div class="table-no service-table">Masa ${call.tableNumber}</div><div class="order-time">${createdAt.toLocaleTimeString("tr-TR")} - ${callAge(call.createdAt)}</div></div><div class="order-state"><b>Garson Cagrisi</b><div class="order-id">${String(call.id).slice(0, 8)}</div></div>`;
        card.appendChild(top);

        if (call.message) {
            const note = document.createElement("div");
            note.className = "customer-note";
            note.innerHTML = `<strong>Not</strong><span>${escapeHtml(call.message)}</span>`;
            card.appendChild(note);
        }

        const actions = document.createElement("div");
        actions.className = "order-actions";

        const complete = document.createElement("button");
        complete.type = "button";
        complete.className = "button primary service-done";
        complete.textContent = "Cagri Tamamlandi";
        complete.addEventListener("click", () => completeServiceCall(state, call.id));
        actions.appendChild(complete);

        card.appendChild(actions);
        return card;
    }

    function renderOrderCard(state, order) {
        const card = document.createElement("article");
        card.className = "order-card";

        const createdAt = new Date(order.createdAt);
        const items = order.items || [];

        const top = document.createElement("div");
        top.className = "order-top";
        top.innerHTML = `<div><div class="table-no service-table">Masa ${order.tableNumber}</div><div class="order-time">${createdAt.toLocaleTimeString("tr-TR")} · ${orderAge(order.createdAt)}</div></div><div class="order-state"><b>Hazir</b><div class="order-id">${String(order.id).slice(0, 8)}</div></div>`;
        card.appendChild(top);

        if (order.customerNote) {
            const note = document.createElement("div");
            note.className = "customer-note";
            note.innerHTML = `<strong>Musteri Notu</strong><span>${escapeHtml(order.customerNote)}</span>`;
            card.appendChild(note);
        }

        const list = document.createElement("ul");
        list.className = "items";

        for (const item of items) {
            const li = document.createElement("li");
            const name = document.createElement("span");
            name.innerHTML = `<b>${escapeHtml(item.productNameSnapshot)}</b>${item.itemNote ? `<span class="item-note">${escapeHtml(item.itemNote)}</span>` : ""}`;
            const qty = document.createElement("b");
            qty.textContent = `x${item.quantity}`;
            li.appendChild(name);
            li.appendChild(qty);
            list.appendChild(li);
        }

        card.appendChild(list);

        const total = document.createElement("div");
        total.innerHTML = `<b>Toplam:</b> ${money(order.totalAmount)}`;
        card.appendChild(total);

        const actions = document.createElement("div");
        actions.className = "order-actions";

        const delivered = document.createElement("button");
        delivered.type = "button";
        delivered.className = "button primary service-done";
        delivered.textContent = "Teslim Edildi";
        delivered.addEventListener("click", () => updateStatus(state, order.id, status.delivered));
        actions.appendChild(delivered);

        const cancel = document.createElement("button");
        cancel.type = "button";
        cancel.className = "button danger";
        cancel.textContent = "Iptal";
        cancel.addEventListener("click", () => updateStatus(state, order.id, status.canceled));
        actions.appendChild(cancel);

        card.appendChild(actions);
        return card;
    }

    function escapeHtml(value) {
        return String(value || "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function init() {
        const root = document.getElementById("service-app");
        if (!root || root.dataset.initialized === "true") return;

        root.dataset.initialized = "true";
        const state = createState(root);
        render(state);

        if (state.token) {
            loadDashboard(state);
            connectRealtime(state);
        }
    }

    document.addEventListener("DOMContentLoaded", init);
    window.addEventListener("load", init);
})();
