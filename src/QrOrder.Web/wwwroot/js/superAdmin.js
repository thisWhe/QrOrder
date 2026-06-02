(function () {
    const storageKey = "staff:super-admin";

    function createState(root) {
        const saved = JSON.parse(localStorage.getItem(storageKey) || "{}");
        return {
            root,
            tenantSlug: saved.tenantSlug || "platform",
            email: saved.email || "superadmin@demo.com",
            password: "",
            token: saved.token || "",
            tenants: [],
            form: emptyTenantForm(),
            loading: false,
            error: "",
            success: ""
        };
    }

    function saveSession(state) {
        localStorage.setItem(storageKey, JSON.stringify({
            tenantSlug: state.tenantSlug,
            email: state.email,
            token: state.token
        }));
    }

    function emptyTenantForm() {
        return {
            name: "",
            slug: "",
            adminEmail: "",
            adminPassword: "",
            tableCount: 0,
            kitchenEmail: "",
            kitchenPassword: "",
            serviceEmail: "",
            servicePassword: ""
        };
    }

    function clearSession(state) {
        state.token = "";
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
        if (status === 409) {
            if (/slug/i.test(text)) return "Bu isletme slug degeri zaten kullaniliyor.";
            if (/email|user/i.test(text)) return "Bu kullanici e-postasi zaten kullaniliyor.";
            return "Bu kayit zaten var.";
        }
        if (status === 400) return "Bilgileri kontrol edip tekrar deneyin.";
        if (status >= 500 || /^System\.|Microsoft\.| at |HEADERS|Exception/i.test(text)) return "Sunucuda beklenmeyen bir hata olustu.";
        return text.length > 160 ? "Islem tamamlanamadi. Lutfen tekrar deneyin." : text || "Islem tamamlanamadi.";
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
            await loadTenants(state);
        } catch {
            state.error = "Giris basarisiz. Isletme, e-posta veya sifre hatali.";
        } finally {
            state.loading = false;
            render(state);
        }
    }

    async function loadTenants(state) {
        if (!state.token) return;

        state.error = "";
        state.loading = true;

        try {
            state.tenants = await authorizedJson(state, "/super-admin/tenants", {});
        } catch (error) {
            if (/oturum gecersiz|unauthorized|staff token/i.test(error.message)) {
                clearSession(state);
                state.error = "Oturum gecersiz. Tekrar giris yapin.";
            } else {
                state.error = `Isletmeler alinamadi: ${error.message}`;
            }
        } finally {
            state.loading = false;
            render(state);
        }
    }

    async function createTenant(state) {
        state.error = "";
        state.success = "";

        if (!state.form.name.trim() || !state.form.slug.trim() || !state.form.adminEmail.trim() || !state.form.adminPassword.trim()) {
            state.error = "Isletme adi, slug, admin e-posta ve sifre zorunlu.";
            render(state);
            return;
        }

        const tableCount = Number(state.form.tableCount || 0);
        if (!Number.isInteger(tableCount) || tableCount < 0 || tableCount > 300) {
            state.error = "Masa sayisi 0 ile 300 arasinda olmali.";
            render(state);
            return;
        }

        try {
            await authorizedJson(state, "/super-admin/tenants", {
                method: "POST",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify(Object.assign({}, state.form, { tableCount }))
            });

            state.success = "Isletme, masa kayitlari ve secilen personel kullanicilari olusturuldu.";
            state.form = emptyTenantForm();
            await loadTenants(state);
        } catch (error) {
            state.error = `Isletme olusturulamadi: ${error.message}`;
            render(state);
        }
    }

    async function setTenantStatus(state, tenant, isActive) {
        state.error = "";
        state.success = "";

        try {
            await authorizedJson(state, `/super-admin/tenants/${encodeURIComponent(tenant.id)}/status`, {
                method: "PATCH",
                headers: { "Content-Type": "application/json" },
                body: JSON.stringify({ isActive })
            });

            state.success = isActive ? "Isletme aktif edildi." : "Isletme pasife alindi.";
            await loadTenants(state);
        } catch (error) {
            state.error = `Isletme durumu guncellenemedi: ${error.message}`;
            render(state);
        }
    }

    function renderLogin(state) {
        state.root.innerHTML = "";

        const panel = document.createElement("section");
        panel.className = "login-panel";
        panel.innerHTML = "<h1>Super Admin</h1><p>Isletme yonetimi</p>";

        panel.appendChild(field("Platform", input(state.tenantSlug, value => state.tenantSlug = value)));
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

        const header = document.createElement("header");
        header.className = "admin-header";
        header.innerHTML = `<div class="brand"><h1>Super Admin</h1><p>Platform isletme yonetimi</p></div>`;

        const toolbar = document.createElement("div");
        toolbar.className = "toolbar";
        toolbar.appendChild(button(state.loading ? "Yukleniyor" : "Yenile", "button", () => loadTenants(state)));
        toolbar.appendChild(button("Cikis", "button", () => {
            clearSession(state);
            render(state);
        }));
        header.appendChild(toolbar);
        state.root.appendChild(header);

        const content = document.createElement("section");
        content.className = "content";
        appendNotices(content, state);

        const summary = document.createElement("section");
        summary.className = "summary-grid";
        summary.appendChild(summaryCard("Toplam Isletme", state.tenants.length));
        summary.appendChild(summaryCard("Aktif Isletme", state.tenants.filter(x => x.isActive).length));
        summary.appendChild(summaryCard("Pasif Isletme", state.tenants.filter(x => !x.isActive).length));
        summary.appendChild(summaryCard("Toplam Kullanici", state.tenants.reduce((sum, x) => sum + Number(x.userCount || 0), 0)));
        content.appendChild(summary);

        const wrap = document.createElement("section");
        wrap.className = "grid-two";
        wrap.appendChild(createTenantPanel(state));
        wrap.appendChild(tenantListPanel(state));
        content.appendChild(wrap);

        state.root.appendChild(content);
    }

    function createTenantPanel(state) {
        const panel = panelShell("Yeni Isletme", "Isletme, ilk admin, masalar ve opsiyonel personel hesaplarini olusturun.");
        const body = panel.querySelector(".panel-body");

        body.appendChild(field("Isletme adi", input(state.form.name, value => state.form.name = value)));
        body.appendChild(field("Slug", input(state.form.slug, value => state.form.slug = value)));
        body.appendChild(field("Admin e-posta", input(state.form.adminEmail, value => state.form.adminEmail = value)));
        body.appendChild(field("Admin sifre", input(state.form.adminPassword, value => state.form.adminPassword = value, "password")));
        body.appendChild(field("Masa sayisi", input(state.form.tableCount, value => state.form.tableCount = value, "number")));

        const staffTitle = document.createElement("div");
        staffTitle.className = "form-section-title";
        staffTitle.innerHTML = "<strong>Opsiyonel personel hesaplari</strong><p class=\"muted\">Bos birakirsaniz isletme admini sonra kendi panelinden olusturabilir.</p>";
        body.appendChild(staffTitle);

        body.appendChild(field("Mutfak e-posta", input(state.form.kitchenEmail, value => state.form.kitchenEmail = value)));
        body.appendChild(field("Mutfak sifre", input(state.form.kitchenPassword, value => state.form.kitchenPassword = value, "password")));
        body.appendChild(field("Servis e-posta", input(state.form.serviceEmail, value => state.form.serviceEmail = value)));
        body.appendChild(field("Servis sifre", input(state.form.servicePassword, value => state.form.servicePassword = value, "password")));
        body.appendChild(button("Isletme Olustur", "button primary", () => createTenant(state)));
        return panel;
    }

    function tenantListPanel(state) {
        const panel = panelShell("Isletmeler", "Tum tenant kayitlarini buradan takip edin.");
        const body = panel.querySelector(".panel-body");

        if (state.tenants.length === 0) {
            body.appendChild(empty("Isletme bulunamadi."));
            return panel;
        }

        const table = tableShell(["Isletme", "Slug", "Durum", "Masa", "Kullanici", "Admin", ""]);
        for (const tenant of state.tenants) {
            const tr = document.createElement("tr");
            tr.innerHTML = `<td><strong>${escapeHtml(tenant.name)}</strong><div class="muted">${new Date(tenant.createdAt).toLocaleDateString("tr-TR")}</div></td><td>${escapeHtml(tenant.slug)}</td><td>${status(tenant.isActive)}</td><td>${tenant.tableCount || 0}</td><td>${tenant.userCount}</td><td>${(tenant.adminEmails || []).map(escapeHtml).join("<br>") || "<span class=\"muted\">Yok</span>"}</td>`;

            const actions = document.createElement("td");
            actions.className = "row-actions";
            if (tenant.slug !== "platform") {
                actions.appendChild(button(tenant.isActive ? "Pasife Al" : "Aktife Al", tenant.isActive ? "button danger" : "button primary", () => {
                    setTenantStatus(state, tenant, !tenant.isActive);
                }));
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

    function input(value, onInput, type) {
        const el = document.createElement("input");
        el.type = type || "text";
        el.value = value || "";
        el.addEventListener("input", () => onInput(el.value));
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
        const root = document.getElementById("super-admin-app");
        if (!root || root.dataset.initialized === "true") return;

        root.dataset.initialized = "true";
        const state = createState(root);
        render(state);

        if (state.token) {
            loadTenants(state);
        }
    }

    document.addEventListener("DOMContentLoaded", init);
    window.addEventListener("load", init);
})();
