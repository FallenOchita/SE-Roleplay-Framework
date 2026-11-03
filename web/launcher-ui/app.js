(() => {
  "use strict";
  const state = { servers: [] };
  const list = document.getElementById("servers");
  const search = document.getElementById("search");
  const status = document.getElementById("status");

  function send(message) {
    const bridge = window.chrome && window.chrome.webview;
    if (!bridge) {
      status.textContent = "Desktop bridge unavailable. Open this page inside the SE1 RP launcher.";
      return;
    }
    bridge.postMessage({ version: 1, ...message });
  }

  function render() {
    const term = search.value.trim().toLowerCase();
    const filtered = state.servers.filter(server =>
      [server.name, server.description, server.region, server.address]
        .filter(Boolean).join(" ").toLowerCase().includes(term));
    list.replaceChildren();
    if (filtered.length === 0) {
      const empty = document.createElement("p");
      empty.className = "empty";
      empty.textContent = state.servers.length
        ? "No communities match your search."
        : "No servers received yet. Check the directory connection in the launcher.";
      list.append(empty);
      return;
    }

    for (const server of filtered) {
      const card = document.createElement("article");
      card.className = "server-card";
      const heading = document.createElement("h2");
      heading.textContent = server.name || "Unnamed community";
      const description = document.createElement("p");
      description.textContent = server.description || "No description provided.";
      const details = document.createElement("p");
      details.className = "details";
      details.textContent = [server.region, server.status,
        Number.isFinite(server.players) ? server.players + " players" : null]
        .filter(Boolean).join(" · ");
      const connect = document.createElement("button");
      connect.type = "button";
      connect.textContent = "Connect";
      connect.addEventListener("click", () => send({ type: "server.connect", serverId: String(server.id) }));
      card.append(heading, description, details, connect);
      list.append(card);
    }
  }

  const bridge = window.chrome && window.chrome.webview;
  if (bridge) bridge.addEventListener("message", event => {
    const message = event.data;
    if (!message || message.version !== 1 || message.type !== "servers.updated" ||
        !Array.isArray(message.servers)) return;
    state.servers = message.servers.filter(server =>
      server && typeof server.id === "string" && typeof server.name === "string");
    status.textContent = state.servers.length + " communities available.";
    render();
  });

  search.addEventListener("input", render);
  document.getElementById("refresh").addEventListener("click", () => send({ type: "servers.refresh" }));
  render();
  send({ type: "servers.refresh" });
})();
