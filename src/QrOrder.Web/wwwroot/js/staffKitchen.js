(function () {
    const storageKey = "staff:kitchen";
    const status = {
        new: 0,
        preparing: 1,
        ready: 2,
        canceled: 4
    };

    function createState(root) {
        const saved = JSON.parse(localStorage.getItem(storageKey) || "{}");
        return {
            root,
            tenantSlug: saved.tenantSlug || "demo-cafe",
            email: saved.email || "kitchen@demo.com",
            password: "",
            token: saved.token || "",
            newOrders: [],
            preparingOrders: [],
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
            token: state.token
        }));
    }

    function clearSession(state) {
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
            await loadOrders(state);
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

        state.error = "";
        state.loading = true;

        try {
            const newOrders = await authorizedJson(state, "/staff/orders?status=0", {});
            const preparingOrders = await authorizedJson(state, "/staff/orders?status=1", {});

            state.newOrders = newOrders;
            state.preparingOrders = preparingOrders;
            state.info = `Son guncelleme: ${new Date().toLocaleTimeString("tr-TR")}`;
        } catch (error) {
            if (isAuthError(error.message)) {
                clearSession(state);
                state.error = "Oturum gecersiz. Tekrar giris yapin.";
            } else {
                state.error = contextError("Siparisler alinamadi", error.message);
            }
        } finally {
            state.loading = false;
            render(state);
        }
    }

    async function updateStatus(state, orderId, nextStatus) {
        state.error = "";

        try {
            await authorizedJson(state, `/staff/orders/${encodeURIComponent(orderId)}/status`, {
                method: "PATCH",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ status: nextStatus })
            });

            await loadOrders(state);
        } catch (error) {
            state.error = contextError("Durum guncellenemedi", error.message);
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
                    if (message.type === 1 && (message.target === "OrderCreated" || message.target === "OrderStatusChanged")) {
                        loadOrders(state);
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

    function statusLabel(value) {
        if (value === status.new) return "Yeni";
        if (value === status.preparing) return "Hazirlaniyor";
        if (value === status.ready) return "Hazir";
        if (value === status.canceled) return "Iptal";
        return String(value);
    }

    function orderAge(createdAt) {
        const minutes = Math.max(0, Math.floor((Date.now() - new Date(createdAt).getTime()) / 60000));
        if (minutes < 1) return "Simdi geldi";
        if (minutes < 60) return `${minutes} dk once`;

        const hours = Math.floor(minutes / 60);
        const remainingMinutes = minutes % 60;
        return remainingMinutes === 0
            ? `${hours} sa once`
            : `${hours} sa ${remainingMinutes} dk once`;
    }

    function isFreshOrder(createdAt) {
        return Date.now() - new Date(createdAt).getTime() < 2 * 60 * 1000;
    }

    function renderLogin(state) {
        state.root.innerHTML = "";

        const panel = document.createElement("section");
        panel.className = "login-panel";
        panel.innerHTML = "<h1>Mutfak Girisi</h1>";

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
        titleWrap.innerHTML = `<h1 class="kitchen-title">Mutfak Ekrani</h1><div class="kitchen-meta"><span class="status-dot ${state.realtimeConnected ? "connected" : ""}"></span><span>${state.realtimeConnected ? "Canli" : "Beklemede"}</span><span>${state.info || ""}</span></div>`;

        const toolbar = document.createElement("div");
        toolbar.className = "toolbar";

        const refresh = document.createElement("button");
        refresh.type = "button";
        refresh.className = "button";
        refresh.textContent = state.loading ? "Yukleniyor" : "Yenile";
        refresh.disabled = state.loading;
        refresh.addEventListener("click", () => loadOrders(state));

        const logout = document.createElement("button");
        logout.type = "button";
        logout.className = "button";
        logout.textContent = "Cikis";
        logout.addEventListener("click", () => {
            clearSession(state);
            render(state);
        });

        toolbar.appendChild(refresh);
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
        board.appendChild(renderLane(state, "Yeni Siparisler", sortByOldest(state.newOrders), status.preparing));
        board.appendChild(renderLane(state, "Hazirlaniyor", sortByOldest(state.preparingOrders), status.ready));
        state.root.appendChild(board);
    }

    function sortByOldest(orders) {
        return [...orders].sort((a, b) => new Date(a.createdAt) - new Date(b.createdAt));
    }

    function renderLane(state, title, orders, nextStatus) {
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
            empty.textContent = "Siparis yok.";
            list.appendChild(empty);
        } else {
            for (const order of orders) {
                list.appendChild(renderOrderCard(state, order, nextStatus));
            }
        }

        lane.appendChild(list);
        return lane;
    }

    function renderOrderCard(state, order, nextStatus) {
        const card = document.createElement("article");
        card.className = `order-card ${isFreshOrder(order.createdAt) ? "fresh" : ""}`;

        const createdAt = new Date(order.createdAt);
        const items = order.items || [];

        const top = document.createElement("div");
        top.className = "order-top";
        top.innerHTML = `<div><div class="table-no">Masa ${order.tableNumber}</div><div class="order-time">${createdAt.toLocaleTimeString("tr-TR")} · ${orderAge(order.createdAt)}</div></div><div class="order-state"><b>${statusLabel(order.status)}</b><div class="order-id">${String(order.id).slice(0, 8)}</div></div>`;
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

        const next = document.createElement("button");
        next.type = "button";
        next.className = "button primary";
        next.textContent = nextStatus === status.preparing ? "Hazirlamaya Al" : "Hazir";
        next.addEventListener("click", () => updateStatus(state, order.id, nextStatus));
        actions.appendChild(next);

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
        const root = document.getElementById("kitchen-app");
        if (!root || root.dataset.initialized === "true") return;

        root.dataset.initialized = "true";
        const state = createState(root);
        render(state);

        if (state.token) {
            loadOrders(state);
            connectRealtime(state);
        }
    }

    document.addEventListener("DOMContentLoaded", init);
    window.addEventListener("load", init);
})();
