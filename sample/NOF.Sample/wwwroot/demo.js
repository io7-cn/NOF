"use strict";
const el = (id) => document.getElementById(id);
const pretty = (value) => JSON.stringify(value, null, 2);
let csrf;
function notice(text, error = false) {
    el("notice").textContent = text;
    el("notice").classList.toggle("error", error);
}
async function request(url, body, method) {
    const response = await fetch(url, {
        method: method || (body === undefined ? "GET" : "POST"),
        headers: {
            "Content-Type": "application/json",
            ...(csrf ? { "X-CSRF-TOKEN": csrf } : {}),
        },
        ...(body === undefined ? {} : { body: JSON.stringify(body) }),
    });
    const text = await response.text();
    let data;
    try {
        data = JSON.parse(text);
    } catch {
        data = text;
    }
    if (!response.ok) {
        const error = new Error(
            `${response.status}: ${typeof data === "string" ? data : data.detail || data.title || pretty(data)}`,
        );
        error.status = response.status;
        throw error;
    }
    return data;
}
function action(button, fn) {
    button.addEventListener("click", async () => {
        button.disabled = true;
        try {
            await fn();
        } catch (error) {
            notice(error.message, true);
        } finally {
            button.disabled = false;
        }
    });
}
function cell(row, text) {
    const td = document.createElement("td");
    td.textContent = text ?? "—";
    row.append(td);
}
function rows(id, values, render, empty) {
    el(id).replaceChildren();
    if (!values.length) {
        const tr = document.createElement("tr");
        cell(tr, empty);
        el(id).append(tr);
    } else
        for (const value of values) {
            const tr = document.createElement("tr");
            render(tr, value);
            el(id).append(tr);
        }
}
const input = (value, tenant = "host") => ({ value, tenant });
async function refreshMessages() {
    const id = el("orderId").value.trim();
    const query = id ? `?orderId=${encodeURIComponent(id)}` : "";
    const [outbox, receipts] = await Promise.all([
        request(`/api/demo/outbox${query}`),
        request(`/api/demo/receipts${query}`),
    ]);
    el("deliveryBadge").textContent = outbox.paused
        ? "示例发件已暂停"
        : "发件运行中";
    el("outboxCount").textContent = outbox.items.length;
    el("receiptCount").textContent =
        `${receipts.items.length}${id ? " / 3" : ""}`;
    rows(
        "outboxRows",
        outbox.items,
        (tr, row) => {
            cell(tr, row.messageType.split(".").at(-1));
            cell(
                tr,
                { Pending: "待投递", Sent: "已发送", Failed: "失败" }[
                    row.status
                ] || row.status,
            );
            cell(tr, row.retryCount);
            cell(
                tr,
                row.errorMessage
                    ? `上次失败：${row.errorMessage}`
                    : row.id.slice(0, 8),
            );
        },
        "没有匹配的发件意图",
    );
    rows(
        "receiptRows",
        receipts.items,
        (tr, row) => {
            cell(
                tr,
                ["邮件投影", "订单审计", "订单指标"][row.kind] || row.kind,
            );
            cell(tr, row.summary);
            cell(tr, new Date(row.receivedAtUtc).toLocaleTimeString());
        },
        "尚未收到业务投影",
    );
    el("messageDetails").textContent = pretty({ outbox, receipts });
}
async function refreshInvocations() {
    const result = await request("/api/demo/invocations");
    rows(
        "invocationRows",
        result.items,
        (tr, row) => {
            cell(tr, row.operation);
            cell(tr, row.origin);
            cell(tr, row.tenant);
            cell(tr, `${row.milliseconds.toFixed(1)} ms`);
            cell(tr, `${row.outcome} · ${row.traceId || "无采样 Trace"}`);
        },
        "执行一项业务操作后刷新",
    );
}
action(el("submitOrder"), async () => {
    const data = await request("/api/messages/orders", {
        email: el("email").value,
    });
    el("orderId").value = data.orderId;
    notice(`订单 ${data.orderId} 已提交。两条意图已保存到 Outbox。`);
    await refreshMessages();
});
action(el("refreshMessages"), async () => {
    await refreshMessages();
    notice("已刷新 Outbox 与跨服务投影。");
});
for (const button of document.querySelectorAll("[data-delivery]"))
    action(button, async () => {
        await request(`/api/demo/delivery/${button.dataset.delivery}`, {});
        notice(
            button.dataset.delivery === "fail-next"
                ? "下一次发件尝试会失败一次，随后按 Outbox 策略重试。"
                : "已更新示例发件状态。",
        );
        await refreshMessages();
    });
action(el("redeliver"), async () => {
    const result = await request("/api/demo/redeliver", {
        orderId: el("orderId").value,
        email: el("email").value,
    });
    notice(result.message);
    await refreshMessages();
});
for (const button of document.querySelectorAll("[data-permission]"))
    action(button, async () => {
        const manage = button.dataset.permission === "manage";
        try {
            const result = await request(
                manage
                    ? "/api/permissions/manage"
                    : "/api/permissions/overview",
                manage ? {} : undefined,
            );
            el("permissionResult").textContent =
                `200 · ${manage ? "管理" : "读取"}操作已通过 NOF 权限检查。\n${pretty(result)}`;
            notice("业务操作已授权。");
        } catch (error) {
            el("permissionResult").textContent =
                `${error.status} · ${error.status === 401 ? "当前尚未登录" : error.status === 403 ? "当前身份没有所需权限" : error.message}`;
            notice(el("permissionResult").textContent, true);
        }
    });
for (const button of document.querySelectorAll("[data-cache]"))
    action(button, async () => {
        const result = await request(
            `/api/demo/cache/${button.dataset.cache}`,
            input(el("cacheValue").value, el("cacheTenant").value),
        );
        el("cacheResult").textContent = pretty(result);
        notice("缓存操作完成，可切换租户比较结果。");
    });
for (const button of document.querySelectorAll("[data-storage]"))
    action(button, async () => {
        el("storageResult").textContent = pretty(
            await request(
                `/api/demo/storage/${button.dataset.storage}`,
                input(el("storageValue").value),
            ),
        );
        notice("对象存储操作完成。");
    });
action(el("validateValue"), async () => {
    try {
        el("valueResult").textContent = pretty(
            await request(
                "/api/demo/value-object",
                input(el("valueObjectInput").value),
            ),
        );
        notice("值对象有效。");
    } catch (error) {
        el("valueResult").textContent = error.message;
        throw error;
    }
});
action(el("generateIds"), async () => {
    el("valueResult").textContent = pretty(await request("/api/demo/ids", {}));
    notice("已生成五个 Snowflake ID。");
});
action(el("publishEvent"), async () => {
    el("eventResult").textContent = pretty(
        await request("/api/demo/events", input(el("eventValue").value)),
    );
    notice("本地事件已被 Handler 处理。");
});
action(el("refreshInvocations"), refreshInvocations);
action(el("logout"), async () => {
    const form = document.createElement("form");
    form.method = "POST";
    form.action = "/auth/logout";
    const token = document.createElement("input");
    token.type = "hidden";
    token.name = "__RequestVerificationToken";
    token.value = csrf;
    form.append(token);
    document.body.append(form);
    form.submit();
});
async function initialize() {
    csrf = (await request("/api/demo/security-token")).token;
    const identity = await request("/auth/status");
    el("identityBadge").textContent = identity.authenticated
        ? `已登录 · ${identity.name}`
        : "当前为匿名身份";
    el("identityDetails").replaceChildren();
    for (const [label, value] of [
        ["当前身份", identity.name || "匿名"],
        ["权限声明", identity.permissions.join(", ") || "无"],
        ["外部身份提供者", identity.authority],
    ]) {
        const dt = document.createElement("dt");
        dt.textContent = label;
        const dd = document.createElement("dd");
        dd.textContent = value;
        el("identityDetails").append(dt, dd);
    }
    await refreshMessages();
    await refreshInvocations();
}
initialize().catch((error) => notice(error.message, true));
setInterval(() => {
    if (!document.hidden)
        refreshMessages().catch((error) =>
            notice(`接收服务暂不可用：${error.message}`, true),
        );
}, 2500);
