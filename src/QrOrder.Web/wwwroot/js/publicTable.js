(function () {
    async function getJson(url) {
        const response = await fetch(url, { headers: { "Accept": "application/json" } });
        if (!response.ok) {
            throw new Error(await response.text() || response.statusText);
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
            throw new Error(await response.text() || response.statusText);
        }

        return await response.json();
    }

    function money(value) {
        return Number(value).toLocaleString("tr-TR", {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        });
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

        return {
            root,
            tenantSlug,
            tableCode,
            sessionKey: `session:${tenantSlug}:${tableCode}`,
            tableNumberKey: `tableNumber:${tenantSlug}:${tableCode}`,
            orderKey: `order:${tenantSlug}:${tableCode}`,
            sessionToken: localStorage.getItem(`session:${tenantSlug}:${tableCode}`),
            tableNumber: Number(localStorage.getItem(`tableNumber:${tenantSlug}:${tableCode}`) || "0"),
            activeOrderId: localStorage.getItem(`order:${tenantSlug}:${tableCode}`),
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
            cartExpanded: false,
            scrollToCart: false,
            submittingOrder: false,
            callingService: false,
            error: "",
            success: ""
        };
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
            state.success = `Onceki siparis ${state.orderStatus} durumunda tamamlandi. Yeni siparis verebilirsiniz.`;
            clearActiveOrder(state);
        }
    }

    async function negotiatePublicOrdersHub() {
        const response = await fetch("/hubs/public-orders/negotiate?negotiateVersion=1", {
            method: "POST",
            headers: { "Accept": "application/json" }
        });

        if (!response.ok) {
            throw new Error(await response.text() || response.statusText);
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
                            state.success = `Siparis ${state.orderStatus} durumunda tamamlandi. Yeni siparis verebilirsiniz.`;
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

        state.success = `${product.name} sepete eklendi.`;
        state.error = "";
        render(state);
    }

    function inc(state, id) {
        const item = state.cart.find(x => x.id === id);
        if (!item) return;
        item.qty += 1;
        state.success = `${item.name} adedi guncellendi.`;
        render(state);
    }

    function dec(state, id) {
        const item = state.cart.find(x => x.id === id);
        if (!item) return;

        item.qty -= 1;
        if (item.qty <= 0) {
            state.cart = state.cart.filter(x => x.id !== id);
        }

        state.success = `${item.name} adedi guncellendi.`;
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

        if (state.cart.length === 0) {
            state.error = "Sepet bos.";
            render(state);
            return;
        }

        if (!isOrderingEnabled(state)) {
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
        const created = await postJson("/public/orders", {
            tenantSlug: state.tenantSlug,
            sessionToken: state.sessionToken,
            items: state.cart.map(x => ({
                productId: x.id,
                quantity: x.qty,
                note: null
            })),
            customerNote: state.customerNote || null
        });

        closeRealtime(state);
        state.activeOrderId = created.orderId;
        localStorage.setItem(state.orderKey, state.activeOrderId);
        state.cart = [];
        state.customerNote = "";
        state.success = `Siparis alindi. ID: ${state.activeOrderId}`;

        await refreshOrderStatus(state);
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

        const header = document.createElement("header");
        header.className = "public-header";
        header.innerHTML = `<div class="brand"><span class="location-label">QR Menu</span><h1>${displayTenantName(state)}</h1><p>Masa ${state.tableNumber || "-"}</p></div>`;

        const headerActions = document.createElement("div");
        headerActions.className = "header-actions";

        const serviceButton = document.createElement("button");
        serviceButton.type = "button";
        serviceButton.className = "service-call-button";
        serviceButton.textContent = state.callingService ? "Cagriliyor" : "Garson Cagir";
        serviceButton.disabled = state.callingService;
        serviceButton.addEventListener("click", () => callService(state));

        const headerCart = document.createElement("button");
        headerCart.type = "button";
        headerCart.className = "header-cart-button";
        headerCart.setAttribute("aria-label", "Sepeti ac");
        headerCart.innerHTML = cartIconHtml(totalItems);
        headerCart.addEventListener("click", () => {
            state.cartExpanded = true;
            state.scrollToCart = true;
            render(state);
        });

        headerActions.appendChild(serviceButton);
        headerActions.appendChild(headerCart);
        header.appendChild(headerActions);
        state.root.appendChild(header);

        const hero = document.createElement("section");
        hero.className = "menu-hero";
        hero.innerHTML = `<div><p class="hero-kicker">Bugunun Menusu</p><h2>Ne siparis etmek istersiniz?</h2><span>Masa ${state.tableNumber || "-"} icin siparisiniz direkt personele iletilir.</span></div>`;

        const searchWrap = document.createElement("label");
        searchWrap.className = "menu-search";
        searchWrap.innerHTML = `<span aria-hidden="true">⌕</span>`;

        const search = document.createElement("input");
        search.type = "search";
        search.placeholder = "Menu icinde ara";
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
            orderingDisabled.textContent = "Su anda online siparis alinmiyor. Menuyu inceleyebilirsiniz.";
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

        if (state.cartExpanded) {
            state.root.appendChild(createInlineCartPanel(state, totalItems, totalAmount));
        }

        if (!state.menu || !state.menu.categories || state.menu.categories.length === 0) {
            const empty = document.createElement("div");
            empty.className = "notice";
            empty.textContent = "Menu bulunamadi.";
            state.root.appendChild(empty);
        } else {
            const nav = document.createElement("nav");
            nav.className = "category-nav";

            const allTab = document.createElement("button");
            allTab.type = "button";
            allTab.className = `category-tab ${state.activeCategoryId === "all" ? "active" : ""}`;
            allTab.textContent = "T\u00fcm\u00fc";
            allTab.addEventListener("click", () => {
                state.activeCategoryId = "all";
                render(state);
            });
            nav.appendChild(allTab);

            for (const category of state.menu.categories) {
                const tab = document.createElement("button");
                tab.type = "button";
                tab.className = `category-tab ${state.activeCategoryId === category.id ? "active" : ""}`;
                tab.textContent = category.name;
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
                        const text = `${product.name || ""} ${product.description || ""} ${category.name || ""}`.toLocaleLowerCase("tr-TR");
                        return text.includes(query);
                    })
                    : category.products;

                if (products.length === 0) {
                    continue;
                }

                const section = document.createElement("section");
                section.className = "category";
                section.id = `cat-${slugify(category.name)}`;

                const categoryTitle = document.createElement("h2");
                categoryTitle.textContent = category.name;
                section.appendChild(categoryTitle);

                const list = document.createElement("div");
                list.className = "product-grid";
                for (const product of products) {
                    const item = document.createElement("article");
                    item.className = "product-card";

                    const visualData = productVisual(category.name, product.name);
                    const visual = document.createElement("div");
                    visual.className = `product-visual ${visualData.tone}`;
                    visual.innerHTML = `<span>${visualData.label}</span>`;
                    item.appendChild(visual);

                    const content = document.createElement("div");
                    content.className = "product-content";

                    const name = document.createElement("div");
                    name.className = "product-top";
                    name.innerHTML = `<h3>${product.name}</h3><span class="price">${money(product.price)}</span>`;
                    content.appendChild(name);

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

                    const button = document.createElement("button");
                    button.type = "button";
                    button.className = "add-button";
                    button.textContent = "+";
                    button.setAttribute("aria-label", `${product.name} sepete ekle`);
                    button.disabled = product.isAvailable === false || !isOrderingEnabled(state);
                    button.addEventListener("click", () => addToCart(state, product));
                    content.appendChild(button);
                    item.appendChild(content);

                    list.appendChild(item);
                }

                section.appendChild(list);
                state.root.appendChild(section);
            }
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

        if (state.scrollToCart) {
            state.scrollToCart = false;
            window.requestAnimationFrame(() => {
                const cartPanel = state.root.querySelector(".inline-cart-panel");
                if (cartPanel) {
                    cartPanel.scrollIntoView({ behavior: "smooth", block: "start" });
                }
            });
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

    function createInlineCartPanel(state, totalItems, totalAmount) {
        const panel = document.createElement("section");
        panel.className = "cart-panel inline-cart-panel";

        const title = createCartTitle(totalItems);
        const close = document.createElement("button");
        close.type = "button";
        close.className = "btn";
        close.textContent = "Kapat";
        close.addEventListener("click", () => {
            state.cartExpanded = false;
            state.scrollToCart = false;
            render(state);
        });
        title.appendChild(close);
        panel.appendChild(title);

        if (state.cart.length === 0) {
            const emptyCart = document.createElement("p");
            emptyCart.className = "empty";
            emptyCart.textContent = "Sepet bos.";
            panel.appendChild(emptyCart);
            return panel;
        }

        panel.appendChild(createSubmitRow(state, totalAmount));
        panel.appendChild(createCartBody(state));
        return panel;
    }

    function displayTenantName(state) {
        return state.menu && state.menu.tenant
            ? state.menu.tenant
            : state.tenantSlug.replaceAll("-", " ");
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
        title.innerHTML = `<h2>Sepet</h2><strong>${totalItems} urun</strong>`;
        return title;
    }

    function createCartBody(state) {
        const cartBody = document.createElement("div");
        cartBody.className = "cart-body";

        const cartList = document.createElement("div");
        cartList.className = "cart-list";
        for (const cartItem of state.cart) {
            const item = document.createElement("div");
            item.className = "cart-row";

            const name = document.createElement("strong");
            name.textContent = cartItem.name;

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
            item.appendChild(name);
            item.appendChild(qty);
            cartList.appendChild(item);
        }

        cartBody.appendChild(cartList);

        const note = document.createElement("textarea");
        note.placeholder = "Not (opsiyon)";
        note.value = state.customerNote;
        note.addEventListener("input", () => {
            state.customerNote = note.value;
        });
        cartBody.appendChild(note);

        return cartBody;
    }

    function createSubmitRow(state, totalAmount) {
        const submitRow = document.createElement("div");
        submitRow.className = "submit-row";
        submitRow.innerHTML = `<strong>Toplam ${money(totalAmount)}</strong>`;

        const submit = document.createElement("button");
        submit.type = "button";
        submit.className = "btn primary";
        submit.textContent = state.submittingOrder ? "Gonderiliyor" : "Siparis Ver";
        submit.disabled = !isOrderingEnabled(state) || state.submittingOrder;
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

            if (state.activeOrderId) {
                await refreshOrderStatus(state);
                await connectOrderHub(state);
            }
        } catch (error) {
            state.error = `Sayfa yuklenemedi: ${error.message}`;
        }

        render(state);
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
