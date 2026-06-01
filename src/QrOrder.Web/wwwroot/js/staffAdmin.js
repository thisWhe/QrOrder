(function () {
    const storageKey = "staff:admin";

    function createState(root) {
        const saved = JSON.parse(localStorage.getItem(storageKey) || "{}");
        return {
            root,
            tenantSlug: saved.tenantSlug || "demo-cafe",
            email: saved.email || "admin@demo.com",
            password: "",
            token: saved.token || "",
            tenant: null,
            tenantForm: { name: "", slug: "", isOrderingEnabled: true, tableSessionHours: 12 },
            activeTab: "products",
            categories: [],
            products: [],
            tables: [],
            orders: [],
            serviceCalls: [],
            users: [],
            tableSearch: "",
            categoryForm: emptyCategoryForm(),
            productForm: emptyProductForm(),
            tableForm: { displayNumber: "" },
            userForm: { email: "", password: "", role: "Service" },
            passwordForm: { currentPassword: "", newPassword: "" },
            loading: false,
            error: "",
            success: ""
        };
    }

    function emptyCategoryForm() {
        return { id: "", name: "", sortOrder: 0, isActive: true };
    }

    function emptyProductForm() {
        return { id: "", categoryId: "", name: "", description: "", price: "", sortOrder: 0, isActive: true, isAvailable: true };
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
        state.tenant = null;
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

        if (status === 401 || /unauthorized/i.test(text)) {
            return "Oturum gecersiz veya giris bilgileri hatali.";
        }

        if (status === 403) {
            return "Bu islem icin yetkiniz yok.";
        }

        if (status === 409) {
            if (/active orders/i.test(text)) return "Bu masada aktif siparis var. Once siparisi tamamlayin veya iptal edin.";
            if (/displaynumber already exists/i.test(text)) return "Bu masa numarasi zaten kullaniliyor.";
            if (/already exists|duplicate|unique/i.test(text)) return "Bu kayit zaten var.";
            return "Bu islem mevcut verilerle cakisti.";
        }

        if (status === 400) {
            if (/password/i.test(text)) return "Sifre kurallara uygun degil. Daha guclu bir sifre deneyin.";
            if (/email/i.test(text)) return "E-posta adresini kontrol edin.";
            return "Gonderilen bilgiler hatali. Lutfen alanlari kontrol edin.";
        }

        if (status >= 500 || isTechnicalMessage(text)) {
            return "Sunucuda beklenmeyen bir hata olustu. Lutfen tekrar deneyin.";
        }

        if (text.length > 180) {
            return "Islem tamamlanamadi. Bilgileri kontrol edip tekrar deneyin.";
        }

        return text || "Islem tamamlanamadi.";
    }

    function normalizeErrorMessage(message) {
        const text = String(message || "").trim();
        if (!text) return "Islem tamamlanamadi.";
        if (isAuthError(text)) return "Oturum gecersiz. Tekrar giris yapin.";
        if (/failed to fetch|network/i.test(text)) return "Sunucuya ulasilamadi. Uygulamanin calistigindan emin olun.";
        if (/active orders/i.test(text)) return "Bu masada aktif siparis var. Once siparisi tamamlayin veya iptal edin.";
        if (/displaynumber already exists/i.test(text)) return "Bu masa numarasi zaten kullaniliyor.";
        if (/password/i.test(text) && /invalid|requires|too short|failed|error/i.test(text)) return "Sifre kurallara uygun degil. Daha guclu bir sifre deneyin.";
        if (/already exists|duplicate|unique/i.test(text)) return "Bu kayit zaten var.";
        if (isTechnicalMessage(text) || text.length > 180) return "Islem tamamlanamadi. Lutfen tekrar deneyin.";
        return text;
    }

    function contextError(prefix, message) {
        return `${prefix}: ${normalizeErrorMessage(message)}`;
    }

    function isTechnicalMessage(message) {
        return /^System\.|Microsoft\.| at |HEADERS|stack trace|Exception:|InvalidOperationException|NotSupportedException|SqlException/i.test(String(message || ""));
    }

    async function authorizedJson(state, url, options) {
        const headers = Object.assign({
            "Accept": "application/json",
            "Authorization": `Bearer ${state.token}`
        }, options && options.headers ? options.headers : {});

        return await requestJson(url, Object.assign({}, options, { headers }));
    }

    async function login(state) {
        state.error = "";
        state.success = "";
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
            await loadAll(state);
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

    async function loadAll(state) {
        if (!state.token) return;

        state.error = "";
        state.loading = true;

        try {
            const tenant = await authorizedJson(state, "/staff/tenant/me", {});
            const categories = await authorizedJson(state, "/staff/categories", {});
            const products = await authorizedJson(state, "/staff/products", {});
            const tables = await authorizedJson(state, "/staff/tables", {});
            const orders = await authorizedJson(state, "/staff/orders?includeClosed=true", {});
            const serviceCalls = await authorizedJson(state, "/staff/service-calls?activeOnly=false", {});
            const users = await authorizedJson(state, "/staff/users", {});

            state.tenant = tenant;
            state.tenantForm = {
                name: tenant.name || "",
                slug: tenant.slug || "",
                isOrderingEnabled: tenant.isOrderingEnabled !== false,
                tableSessionHours: tenant.tableSessionHours || 12
            };
            state.categories = categories;
            state.products = products;
            state.tables = await withQrUrls(state, tables);
            state.orders = orders;
            state.serviceCalls = serviceCalls;
            state.users = users;
        } catch (error) {
            if (isAuthError(error.message)) {
                clearSession(state);
                state.error = "Oturum gecersiz. Tekrar giris yapin.";
            } else {
                state.error = contextError("Veriler alinamadi", error.message);
            }
        } finally {
            state.loading = false;
            render(state);
        }
    }

    async function withQrUrls(state, tables) {
        const tenantSlug = state.tenant ? state.tenant.slug : state.tenantSlug;
        const result = [];

        for (const table of tables) {
            try {
                const qr = await authorizedJson(state, `/staff/tables/${encodeURIComponent(table.id)}/qr-url?tenantSlug=${encodeURIComponent(tenantSlug)}`, {});
                result.push(Object.assign({}, table, { qrUrl: qr.url }));
            } catch {
                result.push(Object.assign({}, table, { qrUrl: "" }));
            }
        }

        return result;
    }

    async function saveCategory(state) {
        state.error = "";
        state.success = "";

        const body = {
            name: state.categoryForm.name,
            sortOrder: Number(state.categoryForm.sortOrder || 0),
            isActive: Boolean(state.categoryForm.isActive)
        };

        try {
            if (state.categoryForm.id) {
                await authorizedJson(state, `/staff/categories/${encodeURIComponent(state.categoryForm.id)}`, {
                    method: "PUT",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify(body)
                });
                state.success = "Kategori guncellendi.";
            } else {
                await authorizedJson(state, "/staff/categories", {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({ name: body.name, sortOrder: body.sortOrder })
                });
                state.success = "Kategori eklendi.";
            }

            state.categoryForm = emptyCategoryForm();
            await loadAll(state);
        } catch (error) {
            state.error = contextError("Kategori kaydedilemedi", error.message);
            render(state);
        }
    }

    async function setCategoryStatus(state, categoryId, isActive) {
        state.error = "";
        state.success = "";

        try {
            await authorizedJson(state, `/staff/categories/${encodeURIComponent(categoryId)}/status`, {
                method: "PATCH",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ isActive })
            });

            state.success = isActive
                ? "Kategori tekrar aktif edildi."
                : "Kategori pasife alindi. Musteri menusunde gorunmez.";

            if (state.categoryForm.id === categoryId) {
                state.categoryForm.isActive = isActive;
            }

            await loadAll(state);
        } catch (error) {
            state.error = contextError("Kategori durumu degistirilemedi", error.message);
            render(state);
        }
    }

    async function saveProduct(state) {
        state.error = "";
        state.success = "";

        const body = {
            categoryId: state.productForm.categoryId,
            name: state.productForm.name,
            description: state.productForm.description || null,
            price: Number(String(state.productForm.price).replace(",", ".")),
            sortOrder: Number(state.productForm.sortOrder || 0),
            isActive: Boolean(state.productForm.isActive),
            isAvailable: Boolean(state.productForm.isAvailable)
        };

        try {
            if (state.productForm.id) {
                await authorizedJson(state, `/staff/products/${encodeURIComponent(state.productForm.id)}`, {
                    method: "PUT",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify(body)
                });
                state.success = "Urun guncellendi.";
            } else {
                await authorizedJson(state, "/staff/products", {
                    method: "POST",
                    headers: { "Content-Type": "application/json" },
                    body: JSON.stringify({
                        categoryId: body.categoryId,
                        name: body.name,
                        description: body.description,
                        price: body.price,
                        sortOrder: body.sortOrder
                    })
                });
                state.success = "Urun eklendi.";
            }

            state.productForm = emptyProductForm();
            await loadAll(state);
        } catch (error) {
            state.error = contextError("Urun kaydedilemedi", error.message);
            render(state);
        }
    }

    async function setProductStatus(state, productId, isActive) {
        state.error = "";
        state.success = "";

        try {
            await authorizedJson(state, `/staff/products/${encodeURIComponent(productId)}/status`, {
                method: "PATCH",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ isActive })
            });

            state.success = isActive
                ? "Urun tekrar aktif edildi."
                : "Urun pasife alindi. Musteri menusunde gorunmez.";

            if (state.productForm.id === productId) {
                state.productForm.isActive = isActive;
            }

            await loadAll(state);
        } catch (error) {
            state.error = contextError("Urun durumu degistirilemedi", error.message);
            render(state);
        }
    }

    async function setProductAvailability(state, productId, isAvailable) {
        state.error = "";
        state.success = "";

        try {
            await authorizedJson(state, `/staff/products/${encodeURIComponent(productId)}/availability`, {
                method: "PATCH",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ isAvailable })
            });

            state.success = isAvailable
                ? "Urun stokta var olarak isaretlendi."
                : "Urun stokta yok olarak isaretlendi. Musteri gorur ama siparis veremez.";

            if (state.productForm.id === productId) {
                state.productForm.isAvailable = isAvailable;
            }

            await loadAll(state);
        } catch (error) {
            state.error = contextError("Stok durumu degistirilemedi", error.message);
            render(state);
        }
    }

    async function createTable(state) {
        state.error = "";
        state.success = "";

        try {
            await authorizedJson(state, "/staff/tables", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ displayNumber: Number(state.tableForm.displayNumber) })
            });

            state.tableForm.displayNumber = "";
            state.success = "Masa olusturuldu.";
            await loadAll(state);
        } catch (error) {
            state.error = contextError("Masa olusturulamadi", error.message);
            render(state);
        }
    }

    async function saveTenantSettings(state) {
        state.error = "";
        state.success = "";

        const tableSessionHours = Number(state.tenantForm.tableSessionHours || 0);
        if (!state.tenantForm.name.trim()) {
            state.error = "Isletme adi bos olamaz.";
            render(state);
            return;
        }

        if (!Number.isInteger(tableSessionHours) || tableSessionHours < 1 || tableSessionHours > 24) {
            state.error = "Masa oturumu 1 ile 24 saat arasinda olmali.";
            render(state);
            return;
        }

        try {
            await authorizedJson(state, "/staff/tenant/settings", {
                method: "PUT",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    name: state.tenantForm.name,
                    isOrderingEnabled: Boolean(state.tenantForm.isOrderingEnabled),
                    tableSessionHours
                })
            });

            state.success = "Isletme ayarlari guncellendi.";
            await loadAll(state);
        } catch (error) {
            state.error = contextError("Ayarlar kaydedilemedi", error.message);
            render(state);
        }
    }

    async function createUser(state) {
        state.error = "";
        state.success = "";

        if (!state.userForm.email.trim() || !state.userForm.password.trim()) {
            state.error = "E-posta ve sifre zorunlu.";
            render(state);
            return;
        }

        try {
            await authorizedJson(state, "/staff/users", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    email: state.userForm.email,
                    password: state.userForm.password,
                    role: state.userForm.role
                })
            });

            state.userForm = { email: "", password: "", role: "Service" };
            state.success = "Personel kullanicisi olusturuldu.";
            await loadAll(state);
        } catch (error) {
            state.error = contextError("Kullanici olusturulamadi", error.message);
            render(state);
        }
    }

    async function resetUserPassword(state, user) {
        const password = window.prompt(`${user.email} icin yeni sifre`);
        if (password === null) return;

        if (!password.trim()) {
            state.error = "Yeni sifre bos olamaz.";
            state.success = "";
            render(state);
            return;
        }

        try {
            await authorizedJson(state, `/staff/users/${encodeURIComponent(user.id)}/password`, {
                method: "PATCH",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ password })
            });

            state.success = "Kullanici sifresi guncellendi.";
            await loadAll(state);
        } catch (error) {
            state.error = contextError("Sifre guncellenemedi", error.message);
            render(state);
        }
    }

    async function deleteUser(state, user) {
        if (!window.confirm(`${user.email} kullanicisi silinsin mi?`)) return;

        try {
            await authorizedJson(state, `/staff/users/${encodeURIComponent(user.id)}`, {
                method: "DELETE"
            });

            state.success = "Kullanici silindi.";
            await loadAll(state);
        } catch (error) {
            state.error = contextError("Kullanici silinemedi", error.message);
            render(state);
        }
    }

    async function changeOwnPassword(state) {
        state.error = "";
        state.success = "";

        if (!state.passwordForm.currentPassword || !state.passwordForm.newPassword) {
            state.error = "Mevcut sifre ve yeni sifre zorunlu.";
            render(state);
            return;
        }

        try {
            await authorizedJson(state, "/staff/auth/password", {
                method: "PATCH",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({
                    currentPassword: state.passwordForm.currentPassword,
                    newPassword: state.passwordForm.newPassword
                })
            });

            state.passwordForm = { currentPassword: "", newPassword: "" };
            state.success = "Sifreniz guncellendi. Yeni sifreyle tekrar giris yapabilirsiniz.";
            await loadAll(state);
        } catch (error) {
            state.error = contextError("Sifre degistirilemedi", error.message);
            render(state);
        }
    }

    async function setTableStatus(state, id, isActive) {
        state.error = "";
        state.success = "";

        try {
            await authorizedJson(state, `/staff/tables/${encodeURIComponent(id)}/status`, {
                method: "PATCH",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ isActive })
            });

            state.success = isActive ? "Masa tekrar aktif edildi." : "Masa pasife alindi.";
            await loadAll(state);
        } catch (error) {
            state.error = contextError("Masa guncellenemedi", error.message);
            render(state);
        }
    }

    async function updateTableDisplayNumber(state, table, displayNumber) {
        state.error = "";
        state.success = "";

        try {
            await authorizedJson(state, `/staff/tables/${encodeURIComponent(table.id)}/display-number`, {
                method: "PATCH",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ displayNumber })
            });

            state.success = `Masa ${table.displayNumber}, Masa ${displayNumber} olarak guncellendi. QR link degismedi.`;
            await loadAll(state);
        } catch (error) {
            state.error = contextError("Masa numarasi guncellenemedi", error.message);
            render(state);
        }
    }

    function promptTableDisplayNumber(state, table) {
        const value = window.prompt("Yeni masa numarasi", String(table.displayNumber));
        if (value === null) return;

        const displayNumber = Number(value);
        if (!Number.isInteger(displayNumber) || displayNumber <= 0) {
            state.error = "Masa numarasi pozitif tam sayi olmali.";
            state.success = "";
            render(state);
            return;
        }

        if (displayNumber === table.displayNumber) return;

        updateTableDisplayNumber(state, table, displayNumber);
    }

    async function copyText(state, value, successMessage) {
        state.error = "";
        state.success = "";

        try {
            await navigator.clipboard.writeText(value);
            state.success = successMessage;
            render(state);
        } catch {
            state.error = "Kopyalama basarisiz oldu. Linki elle secip kopyalayin.";
            render(state);
        }
    }

    async function fetchQrPng(state, table) {
        const tenantSlug = state.tenant ? state.tenant.slug : state.tenantSlug;
        const response = await fetch(`/staff/tables/${encodeURIComponent(table.id)}/qr-png?tenantSlug=${encodeURIComponent(tenantSlug)}`, {
            headers: {
                "Authorization": `Bearer ${state.token}`
            }
        });

        if (!response.ok) {
            throw new Error(await response.text() || response.statusText);
        }

        return await response.blob();
    }

    async function openQrPng(state, table) {
        state.error = "";

        try {
            const blob = await fetchQrPng(state, table);
            const url = URL.createObjectURL(blob);
            window.open(url, "_blank", "noreferrer");
            window.setTimeout(() => URL.revokeObjectURL(url), 30000);
        } catch (error) {
            state.error = contextError("QR gorseli acilamadi", error.message);
            render(state);
        }
    }

    async function downloadQrPng(state, table) {
        state.error = "";

        try {
            const blob = await fetchQrPng(state, table);
            const url = URL.createObjectURL(blob);
            const link = document.createElement("a");
            link.href = url;
            link.download = `masa-${table.displayNumber}-qr.png`;
            document.body.appendChild(link);
            link.click();
            link.remove();
            window.setTimeout(() => URL.revokeObjectURL(url), 30000);
            state.success = `Masa ${table.displayNumber} QR indirildi.`;
            render(state);
        } catch (error) {
            state.error = contextError("QR indirilemedi", error.message);
            render(state);
        }
    }

    function money(value) {
        return Number(value).toLocaleString("tr-TR", {
            minimumFractionDigits: 2,
            maximumFractionDigits: 2
        });
    }

    function isClosedStatus(status) {
        const value = Number(status);
        return value === 3 || value === 4;
    }

    function orderStatusText(status) {
        const value = Number(status);
        if (value === 0) return "Yeni";
        if (value === 1) return "Hazirlaniyor";
        if (value === 2) return "Hazir";
        if (value === 3) return "Teslim Edildi";
        if (value === 4) return "Iptal";
        return String(status);
    }

    function orderStatusClass(status) {
        const value = Number(status);
        if (value === 0) return "new";
        if (value === 1) return "preparing";
        if (value === 2) return "ready";
        if (value === 3) return "delivered";
        if (value === 4) return "canceled";
        return "";
    }

    function getTodayOrders(orders) {
        const now = new Date();
        return orders.filter(order => {
            const date = new Date(order.createdAt);
            return date.getFullYear() === now.getFullYear()
                && date.getMonth() === now.getMonth()
                && date.getDate() === now.getDate();
        });
    }

    function topSellingProducts(orders) {
        const totals = new Map();

        for (const order of orders) {
            for (const item of order.items || []) {
                const name = item.productNameSnapshot || "Urun";
                totals.set(name, (totals.get(name) || 0) + Number(item.quantity || 0));
            }
        }

        return [...totals.entries()]
            .map(([name, quantity]) => ({ name, quantity }))
            .sort((a, b) => b.quantity - a.quantity || a.name.localeCompare(b.name, "tr"));
    }

    function getTodayServiceCalls(calls) {
        const now = new Date();
        return calls.filter(call => {
            const date = new Date(call.createdAt);
            return date.getFullYear() === now.getFullYear()
                && date.getMonth() === now.getMonth()
                && date.getDate() === now.getDate();
        });
    }

    function serviceCallStatusText(status) {
        return Number(status) === 1 ? "Tamamlandi" : "Acik";
    }

    function serviceCallStatusClass(status) {
        return Number(status) === 1 ? "delivered" : "ready";
    }

    function serviceCallDuration(call) {
        if (!call.completedAt) return "-";

        const minutes = Math.max(0, Math.round((new Date(call.completedAt).getTime() - new Date(call.createdAt).getTime()) / 60000));
        if (minutes < 1) return "1 dk altinda";
        if (minutes < 60) return `${minutes} dk`;

        const hours = Math.floor(minutes / 60);
        const remainingMinutes = minutes % 60;
        return remainingMinutes === 0
            ? `${hours} sa`
            : `${hours} sa ${remainingMinutes} dk`;
    }

    function renderLogin(state) {
        state.root.innerHTML = "";

        const panel = document.createElement("section");
        panel.className = "login-panel";
        panel.innerHTML = "<h1>Admin Paneli</h1><p>Isletme menusu, masalar ve QR linkleri</p>";

        panel.appendChild(field("Isletme", input(state.tenantSlug, value => state.tenantSlug = value)));
        panel.appendChild(field("E-posta", input(state.email, value => state.email = value)));
        panel.appendChild(field("Sifre", input(state.password, value => state.password = value, "password")));
        appendNotices(panel, state);

        const button = document.createElement("button");
        button.type = "button";
        button.className = "button primary";
        button.textContent = state.loading ? "Giris yapiliyor" : "Giris Yap";
        button.disabled = state.loading;
        button.addEventListener("click", () => login(state));
        panel.appendChild(button);

        state.root.appendChild(panel);
    }

    function render(state) {
        if (!state.token) {
            renderLogin(state);
            return;
        }

        state.root.innerHTML = "";
        state.root.appendChild(renderHeader(state));
        state.root.appendChild(renderTabs(state));

        const content = document.createElement("section");
        content.className = "content";
        appendNotices(content, state);
        content.appendChild(renderSummary(state));

        if (state.activeTab === "products") content.appendChild(renderProducts(state));
        if (state.activeTab === "categories") content.appendChild(renderCategories(state));
        if (state.activeTab === "tables") content.appendChild(renderTables(state));
        if (state.activeTab === "orders") content.appendChild(renderOrders(state));
        if (state.activeTab === "serviceCalls") content.appendChild(renderServiceCalls(state));
        if (state.activeTab === "users") content.appendChild(renderUsers(state));
        if (state.activeTab === "settings") content.appendChild(renderSettings(state));

        state.root.appendChild(content);
    }

    function renderHeader(state) {
        const header = document.createElement("header");
        header.className = "admin-header";

        const title = document.createElement("div");
        title.className = "brand";
        title.innerHTML = `<h1>${state.tenant ? state.tenant.name : "Admin Paneli"}</h1><p>${state.tenant ? state.tenant.slug : state.tenantSlug}</p>`;

        const toolbar = document.createElement("div");
        toolbar.className = "toolbar";

        const refresh = button(state.loading ? "Yukleniyor" : "Yenile", "button", () => loadAll(state));
        refresh.disabled = state.loading;

        const logout = button("Cikis", "button", () => {
            clearSession(state);
            render(state);
        });

        toolbar.appendChild(refresh);
        toolbar.appendChild(logout);
        header.appendChild(title);
        header.appendChild(toolbar);
        return header;
    }

    function renderTabs(state) {
        const tabs = document.createElement("nav");
        tabs.className = "tabs";

        for (const item of [
            ["products", "Urunler"],
            ["categories", "Kategoriler"],
            ["tables", "Masalar ve QR"],
            ["orders", "Siparisler"],
            ["serviceCalls", "Servis Cagrilari"],
            ["users", "Kullanicilar"],
            ["settings", "Ayarlar"]
        ]) {
            const tab = button(item[1], `tab ${state.activeTab === item[0] ? "active" : ""}`, () => {
                state.activeTab = item[0];
                render(state);
            });
            tabs.appendChild(tab);
        }

        return tabs;
    }

    function renderSummary(state) {
        const grid = document.createElement("section");
        grid.className = "summary-grid";

        grid.appendChild(summaryCard("Kategori", state.categories.length));
        grid.appendChild(summaryCard("Aktif Urun", state.products.filter(x => x.isActive).length));
        grid.appendChild(summaryCard("Masa", state.tables.length));
        grid.appendChild(summaryCard("Aktif Masa", state.tables.filter(x => x.isActive).length));
        grid.appendChild(summaryCard("Aktif Siparisli Masa", state.tables.filter(x => x.hasActiveOrder).length));
        grid.appendChild(summaryCard("Acik Servis Cagrisi", state.serviceCalls.filter(x => Number(x.status) === 0).length));
        return grid;
    }

    function renderCategories(state) {
        const wrap = document.createElement("section");
        wrap.className = "grid-two";
        wrap.appendChild(categoryFormPanel(state));
        wrap.appendChild(categoryListPanel(state));
        return wrap;
    }

    function categoryFormPanel(state) {
        const panel = panelShell(state.categoryForm.id ? "Kategori Duzenle" : "Yeni Kategori", "Menude gorunecek bolumleri yonetin.");
        const body = panel.querySelector(".panel-body");

        body.appendChild(field("Kategori adi", input(state.categoryForm.name, value => state.categoryForm.name = value)));
        body.appendChild(field("Siralama", input(state.categoryForm.sortOrder, value => state.categoryForm.sortOrder = value, "number")));

        if (state.categoryForm.id) {
            body.appendChild(field("Durum", select(state.categoryForm.isActive ? "true" : "false", [
                ["true", "Aktif"],
                ["false", "Pasif"]
            ], value => state.categoryForm.isActive = value === "true")));
        }

        const actions = document.createElement("div");
        actions.className = "form-actions";
        actions.appendChild(button(state.categoryForm.id ? "Guncelle" : "Ekle", "button primary", () => saveCategory(state)));
        actions.appendChild(button("Temizle", "button", () => {
            state.categoryForm = emptyCategoryForm();
            render(state);
        }));
        body.appendChild(actions);

        return panel;
    }

    function categoryListPanel(state) {
        const panel = panelShell("Kategori Listesi", "Siralamayi ve aktiflik durumunu buradan takip edin.");
        const body = panel.querySelector(".panel-body");

        if (state.categories.length === 0) {
            body.appendChild(empty("Kategori bulunamadi."));
            return panel;
        }

        const table = tableShell(["Ad", "Sira", "Durum", ""]);
        for (const category of state.categories) {
            const tr = document.createElement("tr");
            tr.innerHTML = `<td><strong>${escapeHtml(category.name)}</strong></td><td>${category.sortOrder}</td><td>${status(category.isActive)}</td>`;
            const actions = document.createElement("td");
            actions.className = "row-actions";
            actions.appendChild(button("Duzenle", "button", () => {
                state.categoryForm = {
                    id: category.id,
                    name: category.name,
                    sortOrder: category.sortOrder,
                    isActive: category.isActive
                };
                render(state);
            }));

            actions.appendChild(button(category.isActive ? "Pasife Al" : "Aktife Al", category.isActive ? "button danger" : "button primary", () => {
                setCategoryStatus(state, category.id, !category.isActive);
            }));

            tr.appendChild(actions);
            table.querySelector("tbody").appendChild(tr);
        }

        body.appendChild(wrapTable(table));
        return panel;
    }

    function renderProducts(state) {
        const wrap = document.createElement("section");
        wrap.className = "grid-two";
        wrap.appendChild(productFormPanel(state));
        wrap.appendChild(productListPanel(state));
        return wrap;
    }

    function productFormPanel(state) {
        const panel = panelShell(state.productForm.id ? "Urun Duzenle" : "Yeni Urun", "Fiyat, kategori ve aktiflik bilgisini yonetin.");
        const body = panel.querySelector(".panel-body");

        body.appendChild(field("Kategori", select(state.productForm.categoryId, state.categories.filter(x => x.isActive).map(x => [x.id, x.name]), value => state.productForm.categoryId = value)));
        body.appendChild(field("Urun adi", input(state.productForm.name, value => state.productForm.name = value)));
        body.appendChild(field("Aciklama", textarea(state.productForm.description, value => state.productForm.description = value)));
        body.appendChild(field("Fiyat", input(state.productForm.price, value => state.productForm.price = value, "number", "0.01")));
        body.appendChild(field("Sira", input(state.productForm.sortOrder, value => state.productForm.sortOrder = value, "number", "1")));

        if (state.productForm.id) {
            body.appendChild(field("Durum", select(state.productForm.isActive ? "true" : "false", [
                ["true", "Aktif"],
                ["false", "Pasif"]
            ], value => state.productForm.isActive = value === "true")));

            body.appendChild(field("Stok", select(state.productForm.isAvailable ? "true" : "false", [
                ["true", "Stokta Var"],
                ["false", "Stokta Yok"]
            ], value => state.productForm.isAvailable = value === "true")));
        }

        const actions = document.createElement("div");
        actions.className = "form-actions";
        actions.appendChild(button(state.productForm.id ? "Guncelle" : "Ekle", "button primary", () => saveProduct(state)));
        actions.appendChild(button("Temizle", "button", () => {
            state.productForm = emptyProductForm();
            render(state);
        }));
        body.appendChild(actions);

        return panel;
    }

    function productListPanel(state) {
        const panel = panelShell("Urun Listesi", "Menude gorunen ve pasif urunleri birlikte takip edin.");
        const body = panel.querySelector(".panel-body");

        if (state.products.length === 0) {
            body.appendChild(empty("Urun bulunamadi."));
            return panel;
        }

        const table = tableShell(["Sira", "Urun", "Kategori", "Fiyat", "Durum", "Stok", ""]);
        for (const product of state.products) {
            const tr = document.createElement("tr");
            tr.innerHTML = `<td>${Number(product.sortOrder || 0)}</td><td><strong>${escapeHtml(product.name)}</strong><div class="muted">${escapeHtml(product.description || "")}</div></td><td>${escapeHtml(product.categoryName)}</td><td>${money(product.price)}</td><td>${status(product.isActive)}</td><td>${stockStatus(product.isAvailable)}</td>`;
            const actions = document.createElement("td");
            actions.className = "row-actions";
            actions.appendChild(button("Duzenle", "button", () => {
                state.productForm = {
                    id: product.id,
                    categoryId: product.categoryId,
                    name: product.name,
                    description: product.description || "",
                    price: product.price,
                    sortOrder: product.sortOrder || 0,
                    isActive: product.isActive,
                    isAvailable: product.isAvailable
                };
                render(state);
            }));

            actions.appendChild(button(product.isActive ? "Pasife Al" : "Aktife Al", product.isActive ? "button danger" : "button primary", () => {
                setProductStatus(state, product.id, !product.isActive);
            }));

            actions.appendChild(button(product.isAvailable ? "Stokta Yok" : "Stokta Var", product.isAvailable ? "button warning" : "button primary", () => {
                setProductAvailability(state, product.id, !product.isAvailable);
            }));

            tr.appendChild(actions);
            table.querySelector("tbody").appendChild(tr);
        }

        body.appendChild(wrapTable(table));
        return panel;
    }

    function renderTables(state) {
        const wrap = document.createElement("section");
        wrap.className = "grid-two";
        wrap.appendChild(tableFormPanel(state));
        wrap.appendChild(tableListPanel(state));
        return wrap;
    }

    function renderOrders(state) {
        const wrap = document.createElement("section");
        wrap.className = "orders-view";
        wrap.appendChild(orderReportPanel(state));
        wrap.appendChild(orderListPanel(state));
        return wrap;
    }

    function renderServiceCalls(state) {
        const wrap = document.createElement("section");
        wrap.className = "orders-view";
        wrap.appendChild(serviceCallReportPanel(state));
        wrap.appendChild(serviceCallListPanel(state));
        return wrap;
    }

    function renderSettings(state) {
        const wrap = document.createElement("section");
        wrap.className = "grid-two";

        const panel = panelShell("Isletme Ayarlari", "Musteri menusu ve masa oturumu ayarlarini yonetin.");
        const body = panel.querySelector(".panel-body");

        body.appendChild(field("Isletme adi", input(state.tenantForm.name, value => state.tenantForm.name = value)));

        const slug = input(state.tenantForm.slug, () => {});
        slug.disabled = true;
        body.appendChild(field("Slug", slug));

        body.appendChild(field("Online siparis", select(state.tenantForm.isOrderingEnabled ? "true" : "false", [
            ["true", "Acik"],
            ["false", "Kapali"]
        ], value => state.tenantForm.isOrderingEnabled = value === "true")));

        body.appendChild(field("Masa oturumu (saat)", input(state.tenantForm.tableSessionHours, value => state.tenantForm.tableSessionHours = value, "number", "1")));

        const actions = document.createElement("div");
        actions.className = "form-actions";
        actions.appendChild(button("Ayarlari Kaydet", "button primary", () => saveTenantSettings(state)));
        body.appendChild(actions);

        const info = panelShell("Notlar", "Bu ayarlar canli musteriyi etkiler.");
        const infoBody = info.querySelector(".panel-body");
        const list = document.createElement("div");
        list.className = "settings-notes";
        list.innerHTML = `
            <p><strong>Slug</strong> QR linklerinin parcasidir, bu yuzden simdilik kilitli.</p>
            <p><strong>Online siparis kapaliysa</strong> menu gorunur ama musteri sepete ekleyemez ve siparis gonderemez.</p>
            <p><strong>Masa oturumu</strong> QR ile acilan sayfanin kac saat geceri kalacagini belirler.</p>`;
        infoBody.appendChild(list);

        wrap.appendChild(panel);
        wrap.appendChild(info);
        return wrap;
    }

    function renderUsers(state) {
        const wrap = document.createElement("section");
        wrap.className = "grid-two";
        wrap.appendChild(userFormPanel(state));
        wrap.appendChild(userListPanel(state));
        return wrap;
    }

    function userFormPanel(state) {
        const panel = panelShell("Personel Kullanicisi", "Kitchen veya Service rolunde giris hesabi olusturun.");
        const body = panel.querySelector(".panel-body");

        body.appendChild(field("E-posta", input(state.userForm.email, value => state.userForm.email = value)));
        body.appendChild(field("Sifre", input(state.userForm.password, value => state.userForm.password = value, "password")));
        body.appendChild(field("Rol", select(state.userForm.role, [
            ["Service", "Service"],
            ["Kitchen", "Kitchen"]
        ], value => state.userForm.role = value)));

        const actions = document.createElement("div");
        actions.className = "form-actions";
        actions.appendChild(button("Kullanici Olustur", "button primary", () => createUser(state)));
        body.appendChild(actions);

        const passwordPanel = panelShell("Admin Sifresi", "Kendi admin sifrenizi buradan degistirebilirsiniz.");
        const passwordBody = passwordPanel.querySelector(".panel-body");
        passwordBody.appendChild(field("Mevcut sifre", input(state.passwordForm.currentPassword, value => state.passwordForm.currentPassword = value, "password")));
        passwordBody.appendChild(field("Yeni sifre", input(state.passwordForm.newPassword, value => state.passwordForm.newPassword = value, "password")));
        passwordBody.appendChild(button("Sifremi Degistir", "button primary", () => changeOwnPassword(state)));

        const column = document.createElement("div");
        column.className = "stack";
        column.appendChild(panel);
        column.appendChild(passwordPanel);
        return column;
    }

    function userListPanel(state) {
        const panel = panelShell("Kullanici Listesi", "Admin kullanicisi silinemez; personel sifresi buradan sifirlanabilir.");
        const body = panel.querySelector(".panel-body");

        if (state.users.length === 0) {
            body.appendChild(empty("Kullanici bulunamadi."));
            return panel;
        }

        const table = tableShell(["E-posta", "Rol", ""]);
        for (const user of state.users) {
            const roles = user.roles || [];
            const isAdmin = roles.includes("Admin");
            const tr = document.createElement("tr");
            tr.innerHTML = `<td><strong>${escapeHtml(user.email)}</strong></td><td>${roles.map(roleBadge).join(" ")}</td>`;

            const actions = document.createElement("td");
            actions.className = "row-actions";
            actions.appendChild(button("Sifre Sifirla", "button", () => resetUserPassword(state, user)));

            if (!isAdmin) {
                actions.appendChild(button("Sil", "button danger", () => deleteUser(state, user)));
            }

            tr.appendChild(actions);
            table.querySelector("tbody").appendChild(tr);
        }

        body.appendChild(wrapTable(table));
        return panel;
    }

    function orderReportPanel(state) {
        const panel = panelShell("Siparis Raporu", "Odeme sistemi olmadigi icin tutar, iptal edilmeyen siparis toplamidir.");
        const body = panel.querySelector(".panel-body");
        const todayOrders = getTodayOrders(state.orders);
        const activeOrders = state.orders.filter(x => !isClosedStatus(x.status));
        const completedOrders = state.orders.filter(x => Number(x.status) === 3);
        const canceledOrders = state.orders.filter(x => Number(x.status) === 4);
        const todayAmount = todayOrders
            .filter(x => Number(x.status) !== 4)
            .reduce((sum, order) => sum + Number(order.totalAmount || 0), 0);

        const cards = document.createElement("div");
        cards.className = "summary-grid report-grid";
        cards.appendChild(summaryCard("Bugunku Siparis", todayOrders.length));
        cards.appendChild(summaryCard("Aktif Siparis", activeOrders.length));
        cards.appendChild(summaryCard("Tamamlanan", completedOrders.length));
        cards.appendChild(summaryCard("Iptal", canceledOrders.length));
        cards.appendChild(summaryCard("Gunluk Siparis Tutari", money(todayAmount)));
        cards.appendChild(summaryCard("Toplam Siparis", state.orders.length));
        body.appendChild(cards);

        const sellers = topSellingProducts(todayOrders.filter(x => Number(x.status) !== 4));
        const sellersPanel = document.createElement("div");
        sellersPanel.className = "best-sellers";
        sellersPanel.innerHTML = "<h3>Bugun En Cok Satilanlar</h3>";

        if (sellers.length === 0) {
            sellersPanel.appendChild(empty("Bugun satis verisi yok."));
        } else {
            const list = document.createElement("ol");
            for (const item of sellers.slice(0, 6)) {
                const li = document.createElement("li");
                li.innerHTML = `<span>${escapeHtml(item.name)}</span><strong>${item.quantity} adet</strong>`;
                list.appendChild(li);
            }
            sellersPanel.appendChild(list);
        }

        body.appendChild(sellersPanel);
        return panel;
    }

    function orderListPanel(state) {
        const panel = panelShell("Siparis Gecmisi", "Aktif, tamamlanan ve iptal edilen siparisleri birlikte gosterir.");
        const body = panel.querySelector(".panel-body");

        if (state.orders.length === 0) {
            body.appendChild(empty("Siparis bulunamadi."));
            return panel;
        }

        const list = document.createElement("div");
        list.className = "admin-order-list";

        for (const order of [...state.orders].sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt))) {
            list.appendChild(orderCard(order));
        }

        body.appendChild(list);
        return panel;
    }

    function orderCard(order) {
        const card = document.createElement("article");
        card.className = "admin-order-card";
        const createdAt = new Date(order.createdAt);
        const items = order.items || [];

        const head = document.createElement("div");
        head.className = "admin-order-head";
        head.innerHTML = `<div><strong>Masa ${order.tableNumber}</strong><p class="muted">${createdAt.toLocaleString("tr-TR")}</p></div><div class="order-right"><span class="order-status ${orderStatusClass(order.status)}">${orderStatusText(order.status)}</span><strong>${money(order.totalAmount)}</strong></div>`;
        card.appendChild(head);

        const meta = document.createElement("div");
        meta.className = "order-code";
        meta.textContent = `Siparis ID: ${String(order.id).slice(0, 8)}`;
        card.appendChild(meta);

        if (items.length === 0) {
            card.appendChild(empty("Siparis kalemi yok."));
            return card;
        }

        const list = document.createElement("ul");
        list.className = "admin-order-items";

        for (const item of items) {
            const li = document.createElement("li");
            const note = item.itemNote ? `<small>${escapeHtml(item.itemNote)}</small>` : "";
            li.innerHTML = `<span><strong>${escapeHtml(item.productNameSnapshot)}</strong>${note}</span><b>x${item.quantity}</b>`;
            list.appendChild(li);
        }

        card.appendChild(list);
        return card;
    }

    function serviceCallReportPanel(state) {
        const panel = panelShell("Servis Cagrilari Raporu", "Garson cagirma kullanimi ve acik kalan talepleri takip edin.");
        const body = panel.querySelector(".panel-body");
        const todayCalls = getTodayServiceCalls(state.serviceCalls);
        const openCalls = state.serviceCalls.filter(x => Number(x.status) === 0);
        const completedCalls = state.serviceCalls.filter(x => Number(x.status) === 1);
        const completedWithDuration = completedCalls
            .filter(x => x.completedAt)
            .map(x => Math.max(0, new Date(x.completedAt).getTime() - new Date(x.createdAt).getTime()));
        const averageMinutes = completedWithDuration.length === 0
            ? "-"
            : `${Math.max(1, Math.round((completedWithDuration.reduce((sum, value) => sum + value, 0) / completedWithDuration.length) / 60000))} dk`;

        const cards = document.createElement("div");
        cards.className = "summary-grid report-grid";
        cards.appendChild(summaryCard("Bugunku Cagri", todayCalls.length));
        cards.appendChild(summaryCard("Acik Cagri", openCalls.length));
        cards.appendChild(summaryCard("Tamamlanan", completedCalls.length));
        cards.appendChild(summaryCard("Toplam Cagri", state.serviceCalls.length));
        cards.appendChild(summaryCard("Ortalama Kapanis", averageMinutes));
        cards.appendChild(summaryCard("Cagri Gelen Masa", new Set(state.serviceCalls.map(x => x.tableNumber)).size));
        body.appendChild(cards);

        return panel;
    }

    function serviceCallListPanel(state) {
        const panel = panelShell("Servis Cagrisi Gecmisi", "Acik ve tamamlanan garson cagrilarini birlikte gosterir.");
        const body = panel.querySelector(".panel-body");

        if (state.serviceCalls.length === 0) {
            body.appendChild(empty("Servis cagrisi bulunamadi."));
            return panel;
        }

        const list = document.createElement("div");
        list.className = "admin-order-list";

        for (const call of [...state.serviceCalls].sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt))) {
            list.appendChild(serviceCallCard(call));
        }

        body.appendChild(list);
        return panel;
    }

    function serviceCallCard(call) {
        const card = document.createElement("article");
        card.className = "admin-order-card service-call-admin-card";
        const createdAt = new Date(call.createdAt);
        const completedAt = call.completedAt ? new Date(call.completedAt) : null;

        const head = document.createElement("div");
        head.className = "admin-order-head";
        head.innerHTML = `<div><strong>Masa ${call.tableNumber}</strong><p class="muted">${createdAt.toLocaleString("tr-TR")}</p></div><div class="order-right"><span class="order-status ${serviceCallStatusClass(call.status)}">${serviceCallStatusText(call.status)}</span><strong>${serviceCallDuration(call)}</strong></div>`;
        card.appendChild(head);

        const meta = document.createElement("div");
        meta.className = "order-code";
        meta.textContent = `Cagri ID: ${String(call.id).slice(0, 8)}`;
        card.appendChild(meta);

        if (completedAt) {
            const done = document.createElement("div");
            done.className = "order-code";
            done.textContent = `Tamamlanma: ${completedAt.toLocaleString("tr-TR")}`;
            card.appendChild(done);
        }

        if (call.message) {
            const note = document.createElement("div");
            note.className = "service-call-note";
            note.textContent = call.message;
            card.appendChild(note);
        }

        return card;
    }

    function tableFormPanel(state) {
        const panel = panelShell("Yeni Masa", "Her masa icin benzersiz QR linki olusturulur.");
        const body = panel.querySelector(".panel-body");

        body.appendChild(field("Masa numarasi", input(state.tableForm.displayNumber, value => state.tableForm.displayNumber = value, "number")));
        body.appendChild(button("Masa Olustur", "button primary", () => createTable(state)));
        return panel;
    }

    function tableListPanel(state) {
        const panel = panelShell("Masalar ve QR Linkleri", "QR linkini acabilir veya PNG olarak indirebilirsiniz.");
        const body = panel.querySelector(".panel-body");

        if (state.tables.length === 0) {
            body.appendChild(empty("Masa bulunamadi."));
            return panel;
        }

        const search = input(state.tableSearch, value => {
            state.tableSearch = value;
            render(state);
        });
        search.placeholder = "Masa numarasi veya QR link ara";
        body.appendChild(field("Masa ara", search));

        const query = String(state.tableSearch || "").trim().toLocaleLowerCase("tr-TR");
        const visibleTables = state.tables.filter(item => {
            if (!query) return true;
            return String(item.displayNumber).includes(query)
                || String(item.qrUrl || "").toLocaleLowerCase("tr-TR").includes(query);
        });

        if (visibleTables.length === 0) {
            body.appendChild(empty("Aramaya uygun masa bulunamadi."));
            return panel;
        }

        const table = tableShell(["Masa", "Durum", "Siparis", "QR Link", ""]);
        for (const item of visibleTables) {
            const tr = document.createElement("tr");
            tr.innerHTML = `<td><strong>Masa ${item.displayNumber}</strong></td><td>${status(item.isActive)}</td><td>${activeOrderStatus(item.hasActiveOrder)}</td><td>${item.qrUrl ? `<a class="qr-link" href="${item.qrUrl}" target="_blank" rel="noreferrer">${item.qrUrl}</a>` : "<span class=\"muted\">QR link alinamadi</span>"}</td>`;
            const actions = document.createElement("td");
            actions.className = "row-actions";
            if (item.qrUrl) actions.appendChild(linkButton("Ac", item.qrUrl));
            if (item.qrUrl) actions.appendChild(button("Link Kopyala", "button", () => copyText(state, item.qrUrl, `Masa ${item.displayNumber} QR linki kopyalandi.`)));
            actions.appendChild(button("Numara Duzenle", "button", () => promptTableDisplayNumber(state, item)));
            actions.appendChild(button("QR Gor", "button", () => openQrPng(state, item)));
            actions.appendChild(button("QR Indir", "button primary", () => downloadQrPng(state, item)));
            if (item.isActive) {
                const deactivate = button("Pasife Al", "button danger", () => setTableStatus(state, item.id, false));
                if (item.hasActiveOrder) {
                    deactivate.disabled = true;
                    deactivate.title = "Aktif siparis varken masa pasife alinamaz.";
                }
                actions.appendChild(deactivate);
            } else {
                actions.appendChild(button("Aktife Al", "button primary", () => setTableStatus(state, item.id, true)));
            }
            tr.appendChild(actions);
            table.querySelector("tbody").appendChild(tr);
        }

        body.appendChild(wrapTable(table));
        return panel;
    }

    function appendNotices(parent, state) {
        if (state.error) {
            const error = document.createElement("div");
            error.className = "notice error";
            error.textContent = state.error;
            parent.appendChild(error);
        }

        if (state.success) {
            const success = document.createElement("div");
            success.className = "notice success";
            success.textContent = state.success;
            parent.appendChild(success);
        }
    }

    function panelShell(title, subtitle) {
        const panel = document.createElement("section");
        panel.className = "panel";
        panel.innerHTML = `<div class="panel-head"><div><h2>${title}</h2><p class="muted">${subtitle}</p></div></div><div class="panel-body"></div>`;
        return panel;
    }

    function summaryCard(label, value) {
        const card = document.createElement("article");
        card.className = "summary-card";
        card.innerHTML = `<span>${label}</span><strong>${value}</strong>`;
        return card;
    }

    function field(label, control) {
        const wrapper = document.createElement("label");
        wrapper.className = "field";
        const span = document.createElement("span");
        span.textContent = label;
        wrapper.appendChild(span);
        wrapper.appendChild(control);
        return wrapper;
    }

    function input(value, onInput, type, step) {
        const el = document.createElement("input");
        el.type = type || "text";
        if (step) el.step = step;
        el.value = value || "";
        el.addEventListener("input", () => onInput(el.value));
        return el;
    }

    function textarea(value, onInput) {
        const el = document.createElement("textarea");
        el.value = value || "";
        el.addEventListener("input", () => onInput(el.value));
        return el;
    }

    function select(value, options, onChange) {
        const el = document.createElement("select");
        const placeholder = document.createElement("option");
        placeholder.value = "";
        placeholder.textContent = "Seciniz";
        el.appendChild(placeholder);

        for (const option of options) {
            const item = document.createElement("option");
            item.value = option[0];
            item.textContent = option[1];
            el.appendChild(item);
        }

        el.value = value || "";
        el.addEventListener("change", () => onChange(el.value));
        return el;
    }

    function button(text, className, onClick) {
        const el = document.createElement("button");
        el.type = "button";
        el.className = className;
        el.textContent = text;
        el.addEventListener("click", onClick);
        return el;
    }

    function linkButton(text, href) {
        const link = document.createElement("a");
        link.className = "button";
        link.href = href;
        link.target = "_blank";
        link.rel = "noreferrer";
        link.textContent = text;
        return link;
    }

    function tableShell(headers) {
        const table = document.createElement("table");
        table.className = "data-table";
        table.innerHTML = `<thead><tr>${headers.map(x => `<th>${x}</th>`).join("")}</tr></thead><tbody></tbody>`;
        return table;
    }

    function wrapTable(table) {
        const wrap = document.createElement("div");
        wrap.className = "table-wrap";
        wrap.appendChild(table);
        return wrap;
    }

    function status(isActive) {
        return `<span class="status ${isActive ? "active" : "passive"}">${isActive ? "Aktif" : "Pasif"}</span>`;
    }

    function stockStatus(isAvailable) {
        return `<span class="status ${isAvailable ? "active" : "warning"}">${isAvailable ? "Stokta Var" : "Stokta Yok"}</span>`;
    }

    function activeOrderStatus(hasActiveOrder) {
        return hasActiveOrder
            ? `<span class="status warning">Aktif Siparis Var</span>`
            : `<span class="muted">Yok</span>`;
    }

    function roleBadge(role) {
        const className = role === "Admin" ? "warning" : "active";
        return `<span class="status ${className}">${escapeHtml(role)}</span>`;
    }

    function empty(text) {
        const el = document.createElement("div");
        el.className = "empty";
        el.textContent = text;
        return el;
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
        const root = document.getElementById("admin-app");
        if (!root || root.dataset.initialized === "true") return;

        root.dataset.initialized = "true";
        const state = createState(root);
        render(state);

        if (state.token) {
            loadAll(state);
        }
    }

    document.addEventListener("DOMContentLoaded", init);
    window.addEventListener("load", init);
})();
