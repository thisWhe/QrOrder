(function () {
    const allergenOptions = [
        [1, "Gluten"], [2, "Kabuklular"], [4, "Yumurta"], [8, "Balik"],
        [16, "Yer fistigi"], [32, "Soya"], [64, "Sut"], [128, "Sert kabuklu yemisler"],
        [256, "Kereviz"], [512, "Hardal"], [1024, "Susam"], [2048, "Sulfit"],
        [4096, "Acibakla"], [8192, "Yumusakcalar"]
    ];

    async function readErrorResponse(response) {
        const text = await response.text();
        if (!text) return response.statusText || "Islem tamamlanamadi.";

        try {
            const problem = JSON.parse(text);
            return problem.detail || problem.title || response.statusText;
        } catch {
            return text;
        }
    }

    async function getJson(url) {
        const response = await fetch(url, { headers: { "Accept": "application/json" } });
        if (!response.ok) {
            throw new Error(await readErrorResponse(response));
        }
        return await response.json();
    }

    async function postJson(url, body) {
        const response = await fetch(url, {
            method: "POST",
            headers: {
                "Accept": "application/json",
                "Content-Type": "application/json"
            },
            body: JSON.stringify(body)
        });

        if (!response.ok) {
            throw new Error(await readErrorResponse(response));
        }

        return await response.json();
    }

    function money(value) {
        const amount = Number(value).toLocaleString("tr-TR", {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        });
        return `${amount} ₺`;
    }

    function isTerminalStatus(status) {
        return status === "Delivered" || status === "Canceled";
    }

    function statusText(status) {
        if (status === "New") return "Alindi";
        if (status === "Preparing") return "Hazirlaniyor";
        if (status === "Ready") return "Servise hazir";
        if (status === "Delivered") return "Teslim edildi";
        if (status === "Canceled") return "Iptal edildi";
        return status || "-";
    }

    function productAllergens(product) {
        const flags = Number(product.allergenFlags || 0);
        return allergenOptions.filter(([flag]) => (flags & flag) === flag).map(([, label]) => label);
    }

    function hasProductDetails(product) {
        return Boolean(
            product.ingredients || product.portionInfo || product.calories != null ||
            Number(product.allergenFlags || 0) || product.containsAlcohol || product.containsPork ||
            product.isVegetarian || product.isVegan
        );
    }

    function appendProductBadges(parent, product) {
        const labels = [];
        if (product.calories != null) labels.push(`${product.calories} kcal`);
        if (product.isVegan) labels.push("Vegan");
        else if (product.isVegetarian) labels.push("Vejetaryen");
        if (product.containsAlcohol) labels.push("Alkol icerir");
        if (product.containsPork) labels.push("Domuz kaynakli bilesen");
        if (Number(product.allergenFlags || 0)) labels.push("Alerjen bilgisi");
        if (labels.length === 0) return;

        const badges = document.createElement("div");
        badges.className = "product-info-badges";
        labels.forEach(label => {
            const badge = document.createElement("span");
            badge.textContent = label;
            badges.appendChild(badge);
        });
        parent.appendChild(badges);
    }

    function createProductDetails(product) {
        const details = document.createElement("div");
        details.className = "product-details";
        details.hidden = true;

        if (product.ingredients) appendProductDetail(details, "Temel icerikler", product.ingredients);
        if (product.portionInfo) appendProductDetail(details, "Porsiyon", product.portionInfo);
        if (product.calories != null) appendProductDetail(details, "Enerji", `${product.calories} kcal`);

        const allergens = productAllergens(product);
        if (allergens.length > 0) appendProductDetail(details, "Alerjenler", allergens.join(", "));
        if (product.containsAlcohol) appendProductDetail(details, "Bilgilendirme", "Alkol icerir.");
        if (product.containsPork) appendProductDetail(details, "Bilgilendirme", "Domuz kaynakli bilesen icerir.");
        if (product.isVegan) appendProductDetail(details, "Beslenme tercihi", "Vegan");
        else if (product.isVegetarian) appendProductDetail(details, "Beslenme tercihi", "Vejetaryen");

        return details;
    }

    function appendProductDetail(parent, label, value) {
        const row = document.createElement("p");
        const strong = document.createElement("strong");
        strong.textContent = `${label}: `;
        row.appendChild(strong);
        row.appendChild(document.createTextNode(value));
        parent.appendChild(row);
    }

    function slugify(value) {
        return String(value || "")
            .toLowerCase()
            .replace(/[^a-z0-9]+/g, "-")
            .replace(/^-+|-+$/g, "");
    }

    function isDefinitelyDesktopLayout() {
        const viewportWidth = Math.min(
            window.innerWidth || 9999,
            document.documentElement ? document.documentElement.clientWidth || 9999 : 9999,
            window.visualViewport ? window.visualViewport.width || 9999 : 9999
        );
        const isMobileAgent = /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i.test(navigator.userAgent);
        const isTouchDevice = navigator.maxTouchPoints && navigator.maxTouchPoints > 0;

        return viewportWidth >= 1200 && !isMobileAgent && !isTouchDevice;
    }

    function createState(root) {
        const tenantSlug = root.dataset.tenantSlug;
        const tableCode = root.dataset.tableCode;
        const pendingOrderKey = `pendingOrder:${tenantSlug}:${tableCode}`;

        return {
            root,
            tenantSlug,
            tableCode,
            sessionKey: `session:${tenantSlug}:${tableCode}`,
            tableNumberKey: `tableNumber:${tenantSlug}:${tableCode}`,
            orderKey: `order:${tenantSlug}:${tableCode}`,
            pendingOrderKey,
            sessionToken: localStorage.getItem(`session:${tenantSlug}:${tableCode}`),
            tableNumber: Number(localStorage.getItem(`tableNumber:${tenantSlug}:${tableCode}`) || "0"),
            activeOrderId: localStorage.getItem(`order:${tenantSlug}:${tableCode}`),
            pendingOrder: readPendingOrder(pendingOrderKey),
            orderStatus: "",
            realtimeConnected: false,
            realtimeSocket: null,
            realtimeReconnectTimer: null,
            menu: null,
            cart: [],
            customerNote: "",
            searchText: "",
            focusSearch: false,
            activeCategoryId: "all",
            viewMode: "menu",
            submittingOrder: false,
            callingService: false,
            error: "",
            success: ""
        };
    }

    function readPendingOrder(key) {
        try {
            const value = JSON.parse(localStorage.getItem(key) || "null");
            if (!value || typeof value.requestId !== "string" || !Array.isArray(value.items)) return null;
            return value;
        } catch {
            localStorage.removeItem(key);
            return null;
        }
    }

    function generateRequestId() {
        if (window.crypto && typeof window.crypto.randomUUID === "function") {
            return window.crypto.randomUUID();
        }

        return "xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx".replace(/[xy]/g, character => {
            const random = Math.floor(Math.random() * 16);
            const value = character === "x" ? random : (random & 0x3) | 0x8;
            return value.toString(16);
        });
    }

    function createPendingOrder(state) {
        if (state.pendingOrder) return state.pendingOrder;

        state.pendingOrder = {
            requestId: generateRequestId(),
            items: state.cart.map(item => ({
                productId: item.id,
                quantity: item.qty,
                note: null
            })),
            customerNote: state.customerNote || null
        };
        localStorage.setItem(state.pendingOrderKey, JSON.stringify(state.pendingOrder));
        return state.pendingOrder;
    }

    function clearPendingOrder(state) {
        state.pendingOrder = null;
        localStorage.removeItem(state.pendingOrderKey);
    }

    function invalidatePendingOrder(state) {
        if (!state.submittingOrder) clearPendingOrder(state);
    }

    function restoreCartFromPending(state) {
        if (!state.pendingOrder || state.cart.length > 0) return;

        state.cart = state.pendingOrder.items
            .filter(item => findProduct(state, item.productId))
            .map(item => ({ id: item.productId, qty: item.quantity }));
        state.customerNote = state.pendingOrder.customerNote || "";
    }

    async function ensureSession(state) {
        if (state.sessionToken && state.tableNumber > 0) {
            try {
                const currentSession = await postJson("/public/sessions/validate", {
                    tenantSlug: state.tenantSlug,
                    tableCode: state.tableCode,
                    sessionToken: state.sessionToken
                });

                state.sessionToken = currentSession.sessionToken;
                state.tableNumber = currentSession.tableNumber;
                localStorage.setItem(state.tableNumberKey, String(state.tableNumber));
                return;
            } catch {
                clearSession(state);
                clearActiveOrder(state);
            }
        }

        const session = await postJson("/public/sessions", {
            tenantSlug: state.tenantSlug,
            tableCode: state.tableCode
        });

        state.sessionToken = session.sessionToken;
        state.tableNumber = session.tableNumber;

        localStorage.setItem(state.sessionKey, state.sessionToken);
        localStorage.setItem(state.tableNumberKey, String(state.tableNumber));
    }

    function clearSession(state) {
        state.sessionToken = null;
        state.tableNumber = 0;
        localStorage.removeItem(state.sessionKey);
        localStorage.removeItem(state.tableNumberKey);
    }

    function closeRealtime(state) {
        if (state.realtimeReconnectTimer) {
            window.clearTimeout(state.realtimeReconnectTimer);
            state.realtimeReconnectTimer = null;
        }

        if (state.realtimeSocket) {
            state.realtimeSocket.close();
            state.realtimeSocket = null;
        }

        state.realtimeConnected = false;
    }

    function clearActiveOrder(state) {
        closeRealtime(state);
        state.activeOrderId = null;
        state.orderStatus = "";
        localStorage.removeItem(state.orderKey);
    }

    async function refreshOrderStatus(state) {
        if (!state.activeOrderId || !state.sessionToken) return;

        const status = await getJson(
            `/public/orders/${encodeURIComponent(state.tenantSlug)}/${encodeURIComponent(state.activeOrderId)}?sessionToken=${encodeURIComponent(state.sessionToken)}`
        );

        state.orderStatus = status.status;
        state.tableNumber = status.tableNumber;
        localStorage.setItem(state.tableNumberKey, String(state.tableNumber));

        if (isTerminalStatus(state.orderStatus)) {
            state.success = `Onceki siparis ${statusText(state.orderStatus)} durumunda tamamlandi. Yeni siparis verebilirsiniz.`;
            clearActiveOrder(state);
        }
    }

    async function negotiatePublicOrdersHub() {
        const response = await fetch("/hubs/public-orders/negotiate?negotiateVersion=1", {
            method: "POST",
            headers: { "Accept": "application/json" }
        });

        if (!response.ok) {
            throw new Error(await readErrorResponse(response));
        }

        return await response.json();
    }

    function signalRMessage(payload) {
        return `${JSON.stringify(payload)}\x1e`;
    }

    async function connectOrderHub(state) {
        if (!state.activeOrderId || !state.sessionToken) return;
        if (state.realtimeSocket && state.realtimeSocket.readyState === WebSocket.OPEN) return;
        if (state.realtimeSocket && state.realtimeSocket.readyState === WebSocket.CONNECTING) return;

        try {
            const negotiate = await negotiatePublicOrdersHub();
            const token = negotiate.connectionToken || negotiate.connectionId;
            const protocol = window.location.protocol === "https:" ? "wss" : "ws";
            const socket = new WebSocket(`${protocol}://${window.location.host}/hubs/public-orders?id=${encodeURIComponent(token)}`);

            state.realtimeSocket = socket;
            let handshakeCompleted = false;

            socket.addEventListener("open", () => {
                socket.send(signalRMessage({ protocol: "json", version: 1 }));
            });

            socket.addEventListener("message", event => {
                const messages = String(event.data)
                    .split("\x1e")
                    .filter(x => x.length > 0);

                for (const raw of messages) {
                    const message = JSON.parse(raw);

                    if (!handshakeCompleted) {
                        handshakeCompleted = true;
                        socket.send(signalRMessage({
                            type: 1,
                            target: "JoinOrderStatus",
                            arguments: [state.activeOrderId, state.tenantSlug, state.sessionToken]
                        }));

                        state.realtimeConnected = true;
                        render(state);
                        continue;
                    }

                    if (message.type === 1 && message.target === "OrderStatusChanged") {
                        const payload = message.arguments && message.arguments[0];
                        if (!payload || String(payload.orderId).toLowerCase() !== String(state.activeOrderId).toLowerCase()) {
                            continue;
                        }

                        state.orderStatus = payload.status;
                        state.tableNumber = payload.tableNumber;
                        state.success = `Siparis durumu guncellendi: ${payload.status}`;
                        localStorage.setItem(state.tableNumberKey, String(state.tableNumber));

                        if (isTerminalStatus(state.orderStatus)) {
                            state.success = `Siparis ${statusText(state.orderStatus)} durumunda tamamlandi. Yeni siparis verebilirsiniz.`;
                            clearActiveOrder(state);
                        }

                        render(state);
                    }
                }
            });

            socket.addEventListener("close", () => {
                state.realtimeConnected = false;
                state.realtimeSocket = null;
                render(state);

                if (state.activeOrderId && !state.realtimeReconnectTimer) {
                    state.realtimeReconnectTimer = window.setTimeout(() => {
                        state.realtimeReconnectTimer = null;
                        connectOrderHub(state);
                    }, 3000);
                }
            });

            socket.addEventListener("error", () => {
                state.realtimeConnected = false;
            });
        } catch {
            state.realtimeConnected = false;
        }
    }

    function addToCart(state, product) {
        if (state.submittingOrder) return;
        if (!isOrderingEnabled(state)) {
            state.error = "Su anda online siparis alinmiyor.";
            state.success = "";
            render(state);
            return;
        }

        if (product.isAvailable === false) {
            state.error = `${product.name} su an mevcut degil.`;
            state.success = "";
            render(state);
            return;
        }

        const existing = state.cart.find(x => x.id === product.id);
        if (existing) existing.qty += 1;
        else state.cart.push({ id: product.id, name: product.name, qty: 1 });
        invalidatePendingOrder(state);

        state.success = `${product.name} sepete eklendi.`;
        state.error = "";
        render(state);
    }

    function inc(state, id) {
        if (state.submittingOrder) return;
        const item = state.cart.find(x => x.id === id);
        if (!item) return;
        item.qty += 1;
        invalidatePendingOrder(state);
        state.success = `${item.name} adedi guncellendi.`;
        render(state);
    }

    function dec(state, id) {
        if (state.submittingOrder) return;
        const item = state.cart.find(x => x.id === id);
        if (!item) return;

        item.qty -= 1;
        if (item.qty <= 0) {
            state.cart = state.cart.filter(x => x.id !== id);
        }
        invalidatePendingOrder(state);

        state.success = `${item.name} adedi guncellendi.`;
        render(state);
    }

    function removeFromCart(state, id) {
        if (state.submittingOrder) return;
        state.cart = state.cart.filter(item => item.id !== id);
        invalidatePendingOrder(state);
        state.success = "Urun sepetten kaldirildi.";
        state.error = "";
        render(state);
    }

    function clearCart(state) {
        if (state.submittingOrder) return;
        state.cart = [];
        state.customerNote = "";
        invalidatePendingOrder(state);
        state.success = "Sepet temizlendi.";
        state.error = "";
        render(state);
    }

    async function submitOrder(state) {
        if (state.submittingOrder) return;

        state.error = "";
        state.success = "";

        if (!state.sessionToken) {
            state.error = "Session yok. Sayfayi yenileyin.";
            render(state);
            return;
        }

        if (state.cart.length === 0 && !state.pendingOrder) {
            state.error = "Sepet bos.";
            render(state);
            return;
        }

        if (!state.pendingOrder && !isOrderingEnabled(state)) {
            state.error = "Su anda online siparis alinmiyor.";
            render(state);
            return;
        }

        try {
            state.submittingOrder = true;
            render(state);
            await createOrder(state);
        } catch (error) {
            if (/invalid or expired session/i.test(error.message)) {
                try {
                    clearSession(state);
                    await ensureSession(state);
                    await createOrder(state);
                } catch (retryError) {
                    state.error = orderErrorMessage(retryError.message);
                }
            } else {
                state.error = orderErrorMessage(error.message);
            }
        } finally {
            state.submittingOrder = false;
        }

        render(state);
    }

    async function cancelOrder(state) {
        state.error = "";
        state.success = "";

        if (!state.activeOrderId || !state.sessionToken) {
            state.error = "Iptal edilecek aktif siparis bulunamadi.";
            render(state);
            return;
        }

        try {
            const canceled = await postJson(
                `/public/orders/${encodeURIComponent(state.tenantSlug)}/${encodeURIComponent(state.activeOrderId)}/cancel`,
                { sessionToken: state.sessionToken }
            );

            state.orderStatus = canceled.status;
            state.success = "Siparis iptal edildi. Yeni siparis verebilirsiniz.";
            clearActiveOrder(state);
        } catch (error) {
            state.error = `Siparis iptal edilemedi: ${error.message}`;
        }

        render(state);
    }

    async function createOrder(state) {
        const pendingOrder = createPendingOrder(state);
        const created = await postJson("/public/orders", {
            tenantSlug: state.tenantSlug,
            sessionToken: state.sessionToken,
            requestId: pendingOrder.requestId,
            items: pendingOrder.items,
            customerNote: pendingOrder.customerNote
        });

        closeRealtime(state);
        state.activeOrderId = created.orderId;
        localStorage.setItem(state.orderKey, state.activeOrderId);
        clearPendingOrder(state);
        state.cart = [];
        state.customerNote = "";
        state.success = `Siparis alindi. ID: ${state.activeOrderId}`;
        state.viewMode = "confirmation";

        try {
            await refreshOrderStatus(state);
        } catch {
            state.orderStatus = "New";
        }

        await connectOrderHub(state);
    }

    async function callService(state) {
        if (state.callingService) return;

        state.error = "";
        state.success = "";

        if (!state.sessionToken) {
            state.error = "Masa oturumu bulunamadi. Sayfayi yenileyin.";
            render(state);
            return;
        }

        try {
            state.callingService = true;
            render(state);

            await postJson("/public/service-calls", {
                tenantSlug: state.tenantSlug,
                sessionToken: state.sessionToken,
                message: null
            });

            state.success = "Garson cagrildi. En kisa surede masaniza gelecek.";
        } catch (error) {
            if (/invalid or expired session/i.test(error.message)) {
                try {
                    clearSession(state);
                    await ensureSession(state);
                    await postJson("/public/service-calls", {
                        tenantSlug: state.tenantSlug,
                        sessionToken: state.sessionToken,
                        message: null
                    });
                    state.success = "Garson cagrildi. En kisa surede masaniza gelecek.";
                } catch (retryError) {
                    state.error = serviceCallErrorMessage(retryError.message);
                }
            } else {
                state.error = serviceCallErrorMessage(error.message);
            }
        } finally {
            state.callingService = false;
        }

        render(state);
    }

    function render(state) {
        const totalItems = state.cart.reduce((sum, item) => sum + item.qty, 0);
        const totalAmount = state.cart.reduce((sum, item) => {
            const product = findProduct(state, item.id);
            return sum + (product ? Number(product.price) * item.qty : 0);
        }, 0);

        state.root.innerHTML = "";
        state.root.className = "public-shell";
        applyTenantTheme(state);

        if (state.viewMode === "cart") {
            state.root.appendChild(createCartPage(state, totalItems, totalAmount));
            return;
        }

        if (state.viewMode === "confirmation") {
            state.root.appendChild(createOrderConfirmation(state));
            return;
        }

        const header = document.createElement("header");
        header.className = "public-header";

        const logoUrl = state.menu && state.menu.logoUrl;
        const serviceButton = document.createElement("button");
        serviceButton.type = "button";
        serviceButton.className = "service-call-button";
        serviceButton.innerHTML = `${bellIconHtml()}<span>${state.callingService ? "Çağrılıyor" : "Garson Çağır"}</span>`;
        serviceButton.disabled = state.callingService;
        serviceButton.addEventListener("click", () => callService(state));

        const headerCart = document.createElement("button");
        headerCart.type = "button";
        headerCart.className = "header-cart-button";
        headerCart.setAttribute("aria-label", "Sepeti ac");
        headerCart.innerHTML = cartIconHtml(totalItems);
        headerCart.addEventListener("click", () => {
            state.viewMode = "cart";
            render(state);
        });

        const headerSpacer = document.createElement("span");
        headerSpacer.className = "header-spacer";
        header.appendChild(serviceButton);
        header.appendChild(headerSpacer);
        header.appendChild(headerCart);

        const hero = document.createElement("section");
        hero.className = "menu-hero";
        const heroImageUrl = "/assets/menu/cafe-hero.jpg";
        if (heroImageUrl) {
            hero.style.setProperty("--hero-image", `url("${String(heroImageUrl).replaceAll('"', '%22')}")`);
            hero.classList.add("has-image");
        }

        hero.appendChild(header);

        const heroCopy = document.createElement("div");
        heroCopy.className = "hero-copy";
        const heroLogo = document.createElement("span");
        heroLogo.className = `hero-brand-logo${logoUrl ? " has-logo" : ""}`;
        if (logoUrl) {
            const logo = document.createElement("img");
            logo.src = logoUrl;
            logo.alt = `${displayTenantName(state)} logosu`;
            heroLogo.appendChild(logo);
        } else {
            heroLogo.setAttribute("aria-hidden", "true");
            heroLogo.innerHTML = coffeeIconHtml();
        }
        heroCopy.appendChild(heroLogo);
        const heroTitle = document.createElement("h2");
        heroTitle.textContent = displayTenantName(state);
        heroCopy.appendChild(heroTitle);
        const heroDescription = document.createElement("span");
        heroDescription.textContent = "Taze lezzetler, sicak servis ve keyifli bir mola.";
        heroCopy.appendChild(heroDescription);
        const heroStatus = document.createElement("small");
        heroStatus.innerHTML = `<i></i> Acik &middot; Masa ${state.tableNumber || "-"}`;
        heroCopy.appendChild(heroStatus);
        hero.appendChild(heroCopy);

        const searchWrap = document.createElement("label");
        searchWrap.className = "menu-search";
        searchWrap.innerHTML = `<span aria-hidden="true">&#128269;</span>`;

        const search = document.createElement("input");
        search.type = "search";
        search.placeholder = "Menude ara...";
        search.value = state.searchText;
        search.addEventListener("input", () => {
            state.searchText = search.value;
            state.focusSearch = true;
            render(state);
        });

        searchWrap.appendChild(search);
        hero.appendChild(searchWrap);
        state.root.appendChild(hero);

        if (state.error) {
            const error = document.createElement("div");
            error.className = "notice error";
            error.textContent = state.error;
            state.root.appendChild(error);
        }

        if (state.success) {
            const success = document.createElement("div");
            success.className = "notice success";
            success.textContent = state.success;
            state.root.appendChild(success);
        }

        if (state.menu && !isOrderingEnabled(state)) {
            const orderingDisabled = document.createElement("div");
            orderingDisabled.className = "notice";
            orderingDisabled.textContent = state.menu.orderingStatusMessage || "Su anda online siparis alinmiyor. Menuyu inceleyebilirsiniz.";
            state.root.appendChild(orderingDisabled);
        }

        if (state.activeOrderId) {
            const order = document.createElement("section");
            order.className = "order-panel";

            const refresh = document.createElement("button");
            refresh.type = "button";
            refresh.className = "btn";
            refresh.textContent = "Guncelle";
            refresh.addEventListener("click", async () => {
                try {
                    await refreshOrderStatus(state);
                    render(state);
                } catch (error) {
                    state.error = `Siparis durumu alinamadi: ${error.message}`;
                    render(state);
                }
            });

            order.innerHTML = `<div class="panel-row"><div><strong>Aktif Siparis</strong><p class="muted">${String(state.activeOrderId).slice(0, 8)}</p></div><span class="status-pill">${statusText(state.orderStatus)}</span></div><div class="muted">Canli takip: ${state.realtimeConnected ? "Bagli" : "Beklemede"}</div>`;

            const actions = document.createElement("div");
            actions.className = "actions";
            actions.appendChild(refresh);

            if (state.orderStatus === "New") {
                const cancel = document.createElement("button");
                cancel.type = "button";
                cancel.className = "btn danger";
                cancel.textContent = "Siparisi Iptal Et";
                cancel.addEventListener("click", () => cancelOrder(state));
                actions.appendChild(cancel);
            }

            order.appendChild(actions);
            state.root.appendChild(order);
        }

        if (!state.menu || !state.menu.categories || state.menu.categories.length === 0) {
            const empty = document.createElement("div");
            empty.className = "notice";
            empty.textContent = "Menu bulunamadi.";
            state.root.appendChild(empty);
        } else {
            if (state.activeCategoryId === "all" && !state.searchText.trim()) {
                const bestSellers = Array.isArray(state.menu.bestSellers)
                    ? state.menu.bestSellers.filter(product => product && product.isAvailable !== false).slice(0, 6)
                    : [];
                if (bestSellers.length > 0) {
                    state.root.appendChild(createBestSellers(state, bestSellers));
                }
            }

            const nav = document.createElement("nav");
            nav.className = "category-nav";

            const allTab = document.createElement("button");
            allTab.type = "button";
            allTab.className = `category-tab ${state.activeCategoryId === "all" ? "active" : ""}`;
            allTab.innerHTML = `${categoryIconHtml("all")}<span>T\u00fcm\u00fc</span>`;
            allTab.addEventListener("click", () => {
                state.activeCategoryId = "all";
                render(state);
            });
            nav.appendChild(allTab);

            for (const category of state.menu.categories) {
                const tab = document.createElement("button");
                tab.type = "button";
                tab.className = `category-tab ${state.activeCategoryId === category.id ? "active" : ""}`;
                tab.innerHTML = categoryIconHtml(category.name);
                const tabLabel = document.createElement("span");
                tabLabel.textContent = category.name;
                tab.appendChild(tabLabel);
                tab.addEventListener("click", () => {
                    state.activeCategoryId = category.id;
                    render(state);
                });
                nav.appendChild(tab);
            }

            state.root.appendChild(nav);

            const visibleCategories = state.activeCategoryId === "all"
                ? state.menu.categories
                : state.menu.categories.filter(category => category.id === state.activeCategoryId);

            const query = state.searchText.trim().toLocaleLowerCase("tr-TR");

            for (const category of visibleCategories) {
                const products = query
                    ? category.products.filter(product => {
                        const text = `${product.name || ""} ${product.description || ""} ${product.ingredients || ""} ${category.name || ""}`.toLocaleLowerCase("tr-TR");
                        return text.includes(query);
                    })
                    : category.products;

                if (products.length === 0) {
                    continue;
                }

                const section = document.createElement("section");
                section.className = "category";
                section.id = `cat-${slugify(category.name)}`;

                const categoryHeading = document.createElement("div");
                categoryHeading.className = "category-heading";
                const categoryTitle = document.createElement("h2");
                categoryTitle.textContent = category.name;
                categoryHeading.appendChild(categoryTitle);

                if (state.activeCategoryId === "all") {
                    const viewCategory = document.createElement("button");
                    viewCategory.type = "button";
                    viewCategory.className = "category-view-button";
                    viewCategory.innerHTML = "Tumunu Gor <span aria-hidden=\"true\">&rsaquo;</span>";
                    viewCategory.addEventListener("click", () => {
                        state.activeCategoryId = category.id;
                        render(state);
                    });
                    categoryHeading.appendChild(viewCategory);
                }

                section.appendChild(categoryHeading);

                const list = document.createElement("div");
                list.className = "product-grid";
                for (const product of products) {
                    const item = document.createElement("article");
                    item.className = "product-card";

                    const visualData = productVisual(category.name, product.name);
                    const visual = document.createElement("div");
                    visual.className = `product-visual ${visualData.tone}`;
                    const fallback = document.createElement("span");
                    fallback.textContent = visualData.label;
                    visual.appendChild(fallback);

                    const imageUrl = productImageUrl(product, category.name);
                    if (imageUrl) {
                        const image = document.createElement("img");
                        image.src = imageUrl;
                        image.alt = product.name || "Urun gorseli";
                        image.loading = "lazy";
                        image.decoding = "async";
                        image.addEventListener("load", () => visual.classList.add("has-image"));
                        image.addEventListener("error", () => image.remove());
                        visual.appendChild(image);
                    }
                    item.appendChild(visual);

                    const content = document.createElement("div");
                    content.className = "product-content";

                    const productName = document.createElement("h3");
                    productName.textContent = product.name;
                    content.appendChild(productName);

                    if (product.isAvailable === false) {
                        const unavailable = document.createElement("span");
                        unavailable.className = "unavailable-pill";
                        unavailable.textContent = "Su an mevcut degil";
                        content.appendChild(unavailable);
                    }

                    if (product.description) {
                        const desc = document.createElement("p");
                        desc.textContent = product.description;
                        content.appendChild(desc);
                    }

                    const temperature = productTemperature(product.servingTemperature);
                    if (temperature) {
                        const meta = document.createElement("span");
                        meta.className = `product-meta ${temperature.className}`;
                        meta.textContent = temperature.label;
                        content.appendChild(meta);
                    }

                    if (state.menu.showProductDetails !== false) appendProductBadges(content, product);

                    let details = null;
                    if (state.menu.showProductDetails !== false && hasProductDetails(product)) {
                        details = createProductDetails(product);
                        const detailsButton = document.createElement("button");
                        detailsButton.type = "button";
                        detailsButton.className = "product-details-button";
                        detailsButton.textContent = "Icerik ve alerjenler";
                        detailsButton.setAttribute("aria-expanded", "false");
                        detailsButton.addEventListener("click", () => {
                            const willOpen = details.hidden;
                            details.hidden = !willOpen;
                            detailsButton.textContent = willOpen ? "Bilgileri kapat" : "Icerik ve alerjenler";
                            detailsButton.setAttribute("aria-expanded", willOpen ? "true" : "false");
                        });
                        content.appendChild(detailsButton);
                    }

                    const actions = document.createElement("div");
                    actions.className = "product-actions";

                    const price = document.createElement("span");
                    price.className = "price";
                    price.textContent = money(product.price);
                    actions.appendChild(price);

                    const button = document.createElement("button");
                    button.type = "button";
                    button.className = "add-button";
                    button.textContent = "+";
                    button.setAttribute("aria-label", `${product.name} sepete ekle`);
                    button.disabled = product.isAvailable === false || !isOrderingEnabled(state);
                    button.addEventListener("click", () => addToCart(state, product));
                    actions.appendChild(button);
                    item.appendChild(content);
                    item.appendChild(actions);
                    if (details) item.appendChild(details);

                    list.appendChild(item);
                }

                section.appendChild(list);
                state.root.appendChild(section);
            }
        }

        if (totalItems > 0) {
            const cartDock = document.createElement("button");
            cartDock.type = "button";
            cartDock.className = "menu-cart-dock";
            cartDock.innerHTML = `<span class="menu-cart-dock-icon">${cartIconHtml(0)}</span><span><strong>Sepetim (${totalItems})</strong><small>${money(totalAmount)}</small></span><b aria-hidden="true">&rarr;</b>`;
            cartDock.addEventListener("click", () => {
                state.viewMode = "cart";
                render(state);
                window.scrollTo({ top: 0, behavior: "auto" });
            });
            state.root.appendChild(cartDock);
        }

        const desktopCart = document.createElement("section");
        desktopCart.className = "cart-panel desktop-cart";
        desktopCart.appendChild(createCartTitle(totalItems));

        if (state.cart.length === 0) {
            const emptyCart = document.createElement("p");
            emptyCart.className = "empty";
            emptyCart.textContent = "Sepet bos.";
            desktopCart.appendChild(emptyCart);
            state.root.appendChild(desktopCart);
        } else {
            desktopCart.appendChild(createCartBody(state));
            desktopCart.appendChild(createSubmitRow(state, totalAmount));
            state.root.appendChild(desktopCart);
        }

        if (state.focusSearch) {
            state.focusSearch = false;
            window.requestAnimationFrame(() => {
                const searchInput = state.root.querySelector(".menu-search input");
                if (searchInput) {
                    searchInput.focus();
                    const length = searchInput.value.length;
                    searchInput.setSelectionRange(length, length);
                }
            });
        }
    }

    function createCartPage(state, totalItems, totalAmount) {
        const page = document.createElement("section");
        page.className = "cart-page";

        const header = document.createElement("header");
        header.className = "cart-page-header";

        const back = document.createElement("button");
        back.type = "button";
        back.className = "cart-page-icon-button";
        back.setAttribute("aria-label", "Menuye don");
        back.innerHTML = arrowLeftIconHtml();
        back.addEventListener("click", () => {
            state.viewMode = "menu";
            state.error = "";
            state.success = "";
            render(state);
        });

        const heading = document.createElement("div");
        heading.className = "cart-page-heading";
        heading.innerHTML = `<h1>Sepetim</h1><span class="cart-count-pill">${totalItems} urun</span>`;

        const clear = document.createElement("button");
        clear.type = "button";
        clear.className = "cart-page-icon-button";
        clear.setAttribute("aria-label", "Sepeti temizle");
        clear.innerHTML = trashIconHtml();
        clear.disabled = state.cart.length === 0;
        clear.addEventListener("click", () => clearCart(state));

        header.appendChild(back);
        header.appendChild(heading);
        header.appendChild(clear);
        page.appendChild(header);

        if (state.error) page.appendChild(createMessage(state.error, "error"));
        if (state.success) page.appendChild(createMessage(state.success, "success"));

        if (state.cart.length === 0) {
            const empty = document.createElement("div");
            empty.className = "cart-page-empty";
            empty.innerHTML = `${cartIconHtml(0)}<h2>Sepetiniz bos</h2><p>Menuden urun ekleyerek siparisinizi olusturabilirsiniz.</p>`;

            const menuButton = document.createElement("button");
            menuButton.type = "button";
            menuButton.className = "btn primary";
            menuButton.textContent = "Menuye Don";
            menuButton.addEventListener("click", () => {
                state.viewMode = "menu";
                render(state);
            });
            empty.appendChild(menuButton);
            page.appendChild(empty);
            return page;
        }

        page.appendChild(createCartBody(state));
        page.appendChild(createSubmitRow(state, totalAmount));
        return page;
    }

    function createOrderConfirmation(state) {
        const page = document.createElement("section");
        page.className = "confirmation-page";
        page.innerHTML = `<div class="confirmation-mark" aria-hidden="true">${checkIconHtml()}</div><h1>Siparisiniz Alindi!</h1><p>Siparisiniz <strong>#${String(state.activeOrderId || "").slice(0, 8)}</strong> numarasi ile alindi. Tesekkurler.</p>`;

        const tracking = document.createElement("button");
        tracking.type = "button";
        tracking.className = "btn primary confirmation-primary";
        tracking.textContent = "Siparis Takibi";
        tracking.addEventListener("click", () => {
            state.viewMode = "menu";
            render(state);
        });

        const home = document.createElement("button");
        home.type = "button";
        home.className = "confirmation-secondary";
        home.textContent = "Ana Sayfaya Don";
        home.addEventListener("click", () => {
            state.viewMode = "menu";
            render(state);
        });

        page.appendChild(tracking);
        page.appendChild(home);
        return page;
    }

    function createMessage(text, type) {
        const message = document.createElement("div");
        message.className = `notice ${type}`;
        message.textContent = text;
        return message;
    }

    function createBestSellers(state, products) {
        const section = document.createElement("section");
        section.className = "best-sellers";

        const heading = document.createElement("div");
        heading.className = "best-sellers-heading";
        heading.innerHTML = `<div><span aria-hidden="true">&#128293;</span><h2>En Cok Satanlar</h2></div><small>Favoriler</small>`;
        section.appendChild(heading);

        const scroller = document.createElement("div");
        scroller.className = "best-sellers-list";

        for (const product of products) {
            const category = findProductCategory(state, product.id);
            const visualData = productVisual(category, product.name);
            const card = document.createElement("article");
            card.className = "best-seller-card";

            const visual = document.createElement("div");
            visual.className = `best-seller-visual ${visualData.tone}`;
            visual.innerHTML = `<span>${escapeHtml(visualData.label)}</span><em>Populer</em>`;
            const imageUrl = productImageUrl(product, category);
            if (imageUrl) {
                const image = document.createElement("img");
                image.src = imageUrl;
                image.alt = product.name || "Urun gorseli";
                image.loading = "lazy";
                image.decoding = "async";
                image.addEventListener("load", () => visual.classList.add("has-image"));
                image.addEventListener("error", () => image.remove());
                visual.appendChild(image);
            }

            const info = document.createElement("div");
            info.className = "best-seller-info";
            const name = document.createElement("h3");
            name.textContent = product.name;
            const footer = document.createElement("div");
            const price = document.createElement("strong");
            price.textContent = money(product.price);
            const add = document.createElement("button");
            add.type = "button";
            add.className = "best-seller-add";
            add.textContent = "+";
            add.setAttribute("aria-label", `${product.name} sepete ekle`);
            add.disabled = !isOrderingEnabled(state);
            add.addEventListener("click", () => addToCart(state, product));
            footer.appendChild(price);
            footer.appendChild(add);
            info.appendChild(name);
            info.appendChild(footer);
            card.appendChild(visual);
            card.appendChild(info);
            scroller.appendChild(card);
        }

        section.appendChild(scroller);
        return section;
    }

    function findProductCategory(state, productId) {
        if (!state.menu || !Array.isArray(state.menu.categories)) return "";
        const category = state.menu.categories.find(item =>
            Array.isArray(item.products) && item.products.some(product => product.id === productId));
        return category ? category.name : "";
    }

    function productImageUrl(product, categoryName) {
        if (product && product.imageUrl) return product.imageUrl;

        const searchable = `${categoryName || ""} ${product && product.name ? product.name : ""}`.toLocaleLowerCase("tr-TR");
        if (/burger|hamburger/.test(searchable)) return "/assets/menu/burger.jpg";
        if (/tatli|tatlı|tiramisu|pasta|kek|sufle|cheesecake/.test(searchable)) return "/assets/menu/tiramisu.jpg";
        if (/kahve|coffee|espresso|americano|latte|cappuccino|drinks|icecek|içecek/.test(searchable)) return "/assets/menu/espresso.jpg";
        return null;
    }

    function escapeHtml(value) {
        return String(value || "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function displayTenantName(state) {
        return state.menu && state.menu.tenant
            ? state.menu.tenant
            : state.tenantSlug.replaceAll("-", " ");
    }

    function applyTenantTheme(state) {
        const primary = normalizeThemeColor(state.menu && state.menu.primaryColor, "#3D2113");
        const accent = normalizeThemeColor(state.menu && state.menu.accentColor, "#FFB51B");
        state.root.style.setProperty("--premium-coffee", primary);
        state.root.style.setProperty("--premium-coffee-2", primary);
        state.root.style.setProperty("--premium-amber", accent);
        state.root.style.setProperty("--premium-amber-dark", accent);
        state.root.style.setProperty("--primary", accent);
        state.root.style.setProperty("--coffee", primary);
    }

    function normalizeThemeColor(value, fallback) {
        const color = String(value || "").trim();
        return /^#[0-9A-Fa-f]{6}$/.test(color) ? color : fallback;
    }

    function firstProductImage(state) {
        if (!state.menu || !Array.isArray(state.menu.categories)) return null;

        for (const category of state.menu.categories) {
            const product = Array.isArray(category.products)
                ? category.products.find(item => item.imageUrl)
                : null;
            if (product) return product.imageUrl;
        }

        return null;
    }

    function coffeeIconHtml() {
        return `<svg viewBox="0 0 24 24" focusable="false"><path d="M4 7h13v6a6 6 0 0 1-6 6H9a5 5 0 0 1-5-5V7Zm13 2v5h1a2.5 2.5 0 0 0 0-5h-1ZM6 3c0 1 1 1.2 1 2.2M10 3c0 1 1 1.2 1 2.2M14 3c0 1 1 1.2 1 2.2M3 21h16"/></svg>`;
    }

    function bellIconHtml() {
        return `<span class="button-icon" aria-hidden="true"><svg viewBox="0 0 24 24" focusable="false"><path d="M18 8a6 6 0 0 0-12 0c0 7-3 7-3 9h18c0-2-3-2-3-9ZM10 21h4"/></svg></span>`;
    }

    function arrowLeftIconHtml() {
        return `<svg viewBox="0 0 24 24" focusable="false" aria-hidden="true"><path d="M19 12H5M11 18l-6-6 6-6"/></svg>`;
    }

    function trashIconHtml() {
        return `<svg viewBox="0 0 24 24" focusable="false" aria-hidden="true"><path d="M4 7h16M9 7V4h6v3M7 7l1 14h8l1-14M10 11v6M14 11v6"/></svg>`;
    }

    function checkIconHtml() {
        return `<svg viewBox="0 0 24 24" focusable="false"><path d="m6 12 4 4 8-9"/></svg>`;
    }

    function categoryIconHtml(categoryName) {
        const text = String(categoryName || "").toLocaleLowerCase("tr-TR");
        let path = `<rect x="4" y="4" width="6" height="6" rx="1"/><rect x="14" y="4" width="6" height="6" rx="1"/><rect x="4" y="14" width="6" height="6" rx="1"/><rect x="14" y="14" width="6" height="6" rx="1"/>`;

        if (/kahve|coffee|sicak|sıcak/.test(text)) {
            path = `<path d="M5 8h11v5a5 5 0 0 1-5 5H9a4 4 0 0 1-4-4V8Zm11 2h1.5a2 2 0 0 1 0 4H16M8 3v2M12 3v2"/>`;
        } else if (/icecek|içecek|drink|soguk|soğuk/.test(text)) {
            path = `<path d="M8 3h8l-1 18H9L8 3Zm1 5h6M12 3l3-2"/>`;
        } else if (/tatli|tatlı|dessert|pasta|cake/.test(text)) {
            path = `<path d="M4 11h16v9H4v-9Zm2 0c0-3 2.7-5 6-5s6 2 6 5M12 6V3M10 3h4"/>`;
        } else if (/burger|sandvic|sandviç/.test(text)) {
            path = `<path d="M4 10h16M5 10a7 7 0 0 1 14 0M4 14h16M5 18h14"/>`;
        } else if (/yemek|main|ana|et|tavuk/.test(text)) {
            path = `<path d="M5 4v7a3 3 0 0 0 3 3V4M6.5 4v6M16 4v17M16 4c3 2 3 7 0 9"/>`;
        } else if (/kahvalt|breakfast/.test(text)) {
            path = `<circle cx="12" cy="12" r="8"/><circle cx="12" cy="12" r="3"/>`;
        } else if (/corba|çorba|soup/.test(text)) {
            path = `<path d="M4 10h16c0 5-3 8-8 8s-8-3-8-8Zm3-4c0 1 1 1 1 2M12 5c0 1 1 1 1 2M17 6c0 1 1 1 1 2M7 21h10"/>`;
        }

        return `<span class="category-icon" aria-hidden="true"><svg viewBox="0 0 24 24" focusable="false">${path}</svg></span>`;
    }

    function productTemperature(value) {
        if (Number(value) === 1) return { label: "Sicak", className: "temperature-hot" };
        if (Number(value) === 2) return { label: "Soguk", className: "temperature-cold" };
        if (Number(value) === 3) return { label: "Sicak veya soguk", className: "temperature-both" };
        return null;
    }

    function productVisual(categoryName, productName) {
        const text = `${categoryName || ""} ${productName || ""}`.toLocaleLowerCase("tr-TR");

        if (/kahvalt|breakfast|yumurta|omlet/.test(text)) return { tone: "tone-breakfast", label: "K" };
        if (/burger|sandvic|sandwich/.test(text)) return { tone: "tone-burger", label: "B" };
        if (/icecek|içecek|drink|tea|çay|coffee|kahve|su|water/.test(text)) return { tone: "tone-drink", label: "İ" };
        if (/tatli|tatlı|dessert|cake|pasta/.test(text)) return { tone: "tone-dessert", label: "T" };
        if (/corba|çorba|soup/.test(text)) return { tone: "tone-soup", label: "Ç" };

        return { tone: "tone-main", label: productName ? productName.trim().charAt(0).toLocaleUpperCase("tr-TR") : "M" };
    }

    function cartIconHtml(totalItems) {
        return `<span class="cart-icon" aria-hidden="true"><svg viewBox="0 0 24 24" focusable="false"><path d="M7 18a2 2 0 1 0 0 4 2 2 0 0 0 0-4Zm10 0a2 2 0 1 0 0 4 2 2 0 0 0 0-4ZM3 3h2.2l2.1 10.5A3 3 0 0 0 10.2 16H17a3 3 0 0 0 2.8-1.9l2-5A1.5 1.5 0 0 0 20.4 7H7.1L6.6 4.6A2 2 0 0 0 4.6 3H3Z"/></svg></span>${totalItems > 0 ? `<span class="cart-badge">${totalItems}</span>` : ""}`;
    }

    function createCartTitle(totalItems) {
        const title = document.createElement("div");
        title.className = "cart-title";
        title.innerHTML = `<div><span class="cart-eyebrow">Siparis ozeti</span><h2>Sepetim</h2></div><strong>${totalItems} urun</strong>`;
        return title;
    }

    function createCartBody(state) {
        const cartBody = document.createElement("div");
        cartBody.className = "cart-body cart-page-body";

        const cartList = document.createElement("div");
        cartList.className = "cart-list";
        for (const cartItem of state.cart) {
            const product = findProduct(state, cartItem.id);
            const item = document.createElement("article");
            item.className = "cart-row cart-page-item";

            const visualData = productVisual("", cartItem.name);
            const visual = document.createElement("div");
            visual.className = `cart-item-visual ${visualData.tone}`;
            const fallback = document.createElement("span");
            fallback.textContent = visualData.label;
            visual.appendChild(fallback);

            if (product && product.imageUrl) {
                const image = document.createElement("img");
                image.src = product.imageUrl;
                image.alt = cartItem.name || "Urun gorseli";
                image.loading = "lazy";
                image.addEventListener("load", () => visual.classList.add("has-image"));
                image.addEventListener("error", () => image.remove());
                visual.appendChild(image);
            }

            const info = document.createElement("div");
            info.className = "cart-item-info";
            const name = document.createElement("h3");
            name.textContent = cartItem.name;
            info.appendChild(name);

            if (product && product.description) {
                const description = document.createElement("p");
                description.textContent = product.description;
                info.appendChild(description);
            }

            const qty = document.createElement("div");
            qty.className = "qty";

            const plus = document.createElement("button");
            plus.type = "button";
            plus.className = "btn icon";
            plus.textContent = "+";
            plus.addEventListener("click", () => inc(state, cartItem.id));

            const minus = document.createElement("button");
            minus.type = "button";
            minus.className = "btn icon";
            minus.textContent = "-";
            minus.addEventListener("click", () => dec(state, cartItem.id));

            const qtyText = document.createElement("strong");
            qtyText.textContent = String(cartItem.qty);

            qty.appendChild(minus);
            qty.appendChild(qtyText);
            qty.appendChild(plus);

            const controls = document.createElement("div");
            controls.className = "cart-item-controls";
            controls.appendChild(qty);

            const linePrice = document.createElement("strong");
            linePrice.className = "cart-item-price";
            linePrice.textContent = money((product ? Number(product.price) : 0) * cartItem.qty);
            controls.appendChild(linePrice);
            info.appendChild(controls);

            const remove = document.createElement("button");
            remove.type = "button";
            remove.className = "cart-item-remove";
            remove.setAttribute("aria-label", `${cartItem.name} urununu sepetten kaldir`);
            remove.innerHTML = "&times;";
            remove.addEventListener("click", () => removeFromCart(state, cartItem.id));

            item.appendChild(visual);
            item.appendChild(info);
            item.appendChild(remove);
            cartList.appendChild(item);
        }

        cartBody.appendChild(cartList);

        const noteWrap = document.createElement("label");
        noteWrap.className = "cart-note";
        const noteLabel = document.createElement("span");
        noteLabel.textContent = "Siparis notu";
        const note = document.createElement("textarea");
        note.placeholder = "Not (opsiyon)";
        note.value = state.customerNote;
        note.disabled = state.submittingOrder;
        note.addEventListener("input", () => {
            state.customerNote = note.value;
            invalidatePendingOrder(state);
        });
        noteWrap.appendChild(noteLabel);
        noteWrap.appendChild(note);
        cartBody.appendChild(noteWrap);

        return cartBody;
    }

    function createSubmitRow(state, totalAmount) {
        const submitRow = document.createElement("div");
        submitRow.className = "submit-row";
        submitRow.innerHTML = `<div class="cart-totals"><div><span>Ara Toplam</span><strong>${money(totalAmount)}</strong></div><div><span>Hizmet Bedeli</span><strong>${money(0)}</strong></div><div class="cart-total-final"><span>Toplam</span><strong>${money(totalAmount)}</strong></div></div>`;

        const submit = document.createElement("button");
        submit.type = "button";
        submit.className = "btn primary";
        submit.textContent = state.submittingOrder ? "Gonderiliyor" : "Siparisi Tamamla";
        submit.disabled = (!isOrderingEnabled(state) && !state.pendingOrder) || state.submittingOrder;
        submit.addEventListener("click", () => submitOrder(state));

        submitRow.appendChild(submit);
        return submitRow;
    }

    function findProduct(state, productId) {
        if (!state.menu || !state.menu.categories) return null;

        for (const category of state.menu.categories) {
            const product = category.products.find(x => x.id === productId);
            if (product) return product;
        }

        return null;
    }

    function isOrderingEnabled(state) {
        return !state.menu || state.menu.isOrderingEnabled !== false;
    }

    function orderErrorMessage(message) {
        if (/online ordering is currently disabled/i.test(message)) {
            return "Su anda online siparis alinmiyor. Lutfen personele danisin.";
        }

        if (/business is currently closed/i.test(message)) {
            return "Isletme su anda kapali. Menuyu inceleyebilirsiniz.";
        }

        if (/invalid or expired session/i.test(message)) {
            return "Masa oturumunuz yenilenemedi. Sayfayi yenileyip tekrar deneyin.";
        }

        if (/table is inactive/i.test(message)) {
            return "Bu masa su anda siparise kapali. Lutfen personele danisin.";
        }

        if (/some products are invalid|inactive|unavailable/i.test(message)) {
            return "Sepetinizde artik siparise uygun olmayan urun var. Lutfen sepeti guncelleyip tekrar deneyin.";
        }

        if (/empty order/i.test(message)) {
            return "Sepet bos. Lutfen urun ekleyin.";
        }

        return "Siparis gonderilemedi. Lutfen tekrar deneyin veya personele danisin.";
    }

    function serviceCallErrorMessage(message) {
        if (/invalid or expired session/i.test(message)) {
            return "Masa oturumunuz yenilenemedi. Sayfayi yenileyip tekrar deneyin.";
        }

        if (/table is inactive/i.test(message)) {
            return "Bu masa su anda servis cagrisina kapali. Lutfen personele danisin.";
        }

        return "Garson cagrilamadi. Lutfen tekrar deneyin veya personele seslenin.";
    }

    async function initRoot(root) {
        const state = createState(root);

        try {
            await ensureSession(state);
            state.menu = await getJson(`/public/menu/${encodeURIComponent(state.tenantSlug)}`);

            if (state.pendingOrder && !state.activeOrderId) {
                state.submittingOrder = true;
                try {
                    await createOrder(state);
                } catch (error) {
                    restoreCartFromPending(state);
                    state.error = `Onceki siparis dogrulanamadi: ${orderErrorMessage(error.message)}`;
                } finally {
                    state.submittingOrder = false;
                }
            } else if (state.activeOrderId) {
                await refreshOrderStatus(state);
                await connectOrderHub(state);
            }
        } catch (error) {
            state.error = `Sayfa yuklenemedi: ${error.message}`;
        }

        try {
            render(state);
        } catch (error) {
            state.root.className = "public-shell";
            state.root.innerHTML = "";
            state.root.appendChild(createMessage("Sayfa gosterilemedi. Lutfen yenileyip tekrar deneyin.", "error"));
            console.error("Public menu render failed", error);
        }
    }

    function init() {
        const root = document.getElementById("public-table-app");
        if (!root || root.dataset.initialized === "true") return;

        root.dataset.initialized = "true";
        initRoot(root);
    }

    document.addEventListener("DOMContentLoaded", init);
    window.addEventListener("load", init);
})();
