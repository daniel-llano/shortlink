const storageKey = "shortlink.mine";
const $ = (id) => document.getElementById(id);

function loadMine() {
  try {
    return JSON.parse(localStorage.getItem(storageKey) ?? "[]");
  } catch {
    return [];
  }
}

function saveMine(links) {
  localStorage.setItem(storageKey, JSON.stringify(links));
}

async function problemMessage(response) {
  try {
    const problem = await response.json();
    const fields = Object.values(problem.errors ?? {}).flat();
    return fields.length ? fields.join(" ") : (problem.detail ?? problem.title);
  } catch {
    return response.status === 429 ? "Too many links in a minute. Wait a bit and try again." : `Request failed (${response.status}).`;
  }
}

function el(tag, props = {}, ...children) {
  const node = Object.assign(document.createElement(tag), props);
  node.append(...children);
  return node;
}

async function renderMine() {
  const mine = loadMine();
  const body = $("links").querySelector("tbody");
  body.replaceChildren();
  $("links").hidden = mine.length === 0;
  $("empty").hidden = mine.length > 0;

  for (const item of mine) {
    const clicks = el("td", { className: "num", textContent: "…" });
    const remove = el("button", { type: "button", className: "link-button", textContent: "Delete" });
    remove.addEventListener("click", () => deleteLink(item));
    body.append(
      el("tr", {},
        el("td", {}, el("a", { href: item.shortUrl, target: "_blank", rel: "noopener", textContent: item.shortUrl.replace(/^https?:\/\//, "") })),
        el("td", { className: "target", title: item.targetUrl, textContent: item.targetUrl }),
        clicks,
        el("td", { className: "num" }, remove)));

    fetch(`/api/links/${encodeURIComponent(item.code)}`)
      .then((r) => (r.ok ? r.json() : null))
      .then((link) => {
        clicks.textContent = link ? link.clickCount.toLocaleString() : "gone";
      })
      .catch(() => (clicks.textContent = "?"));
  }
}

async function deleteLink(item) {
  if (!confirm(`Delete ${item.shortUrl}? It stops working right away.`)) return;
  const response = await fetch(`/api/links/${encodeURIComponent(item.code)}`, {
    method: "DELETE",
    headers: { "X-Owner-Key": item.ownerKey },
  });
  if (response.ok || response.status === 404) {
    saveMine(loadMine().filter((l) => l.code !== item.code));
    renderMine();
  } else {
    alert(await problemMessage(response));
  }
}

$("create").addEventListener("submit", async (event) => {
  event.preventDefault();
  const error = $("error");
  error.hidden = true;

  const url = $("url").value.trim();
  if (!url) {
    error.textContent = "Paste a link to shorten.";
    error.hidden = false;
    return;
  }

  const expires = $("expires").value;
  const button = event.submitter;
  button.disabled = true;
  try {
    const response = await fetch("/api/links", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        url,
        alias: $("alias").value.trim() || null,
        expiresAt: expires ? new Date(expires).toISOString() : null,
      }),
    });
    if (!response.ok) {
      error.textContent = await problemMessage(response);
      error.hidden = false;
      return;
    }

    const link = await response.json();
    $("short-url").href = link.shortUrl;
    $("short-url").textContent = link.shortUrl;
    $("owner-key").textContent = link.ownerKey;
    $("qr").src = `/api/links/${encodeURIComponent(link.code)}/qr`;
    $("result").hidden = false;
    $("create").reset();

    saveMine([{ code: link.code, shortUrl: link.shortUrl, targetUrl: link.targetUrl, ownerKey: link.ownerKey }, ...loadMine()]);
    renderMine();
  } catch {
    error.textContent = "Can't reach the server. Check your connection and try again.";
    error.hidden = false;
  } finally {
    button.disabled = false;
  }
});

$("copy").addEventListener("click", async () => {
  await navigator.clipboard.writeText($("short-url").href);
  $("copy").textContent = "Copied";
  setTimeout(() => ($("copy").textContent = "Copy"), 1500);
});

renderMine();
