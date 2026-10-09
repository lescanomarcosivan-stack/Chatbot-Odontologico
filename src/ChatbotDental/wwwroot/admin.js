// Panel de recepción: lista solicitudes y conversaciones registradas por el asistente.
(() => {
  "use strict";

  const CLAVE_SESION = "lapacho-admin";
  const TIPOS = { turno: "Turno", consulta: "Consulta", humano: "Para recepción" };

  const $ = id => document.getElementById(id);
  let clave = leerClave();
  let solicitudes = [];
  let filtro = "";

  function leerClave() { try { return sessionStorage.getItem(CLAVE_SESION) || ""; } catch { return ""; } }
  function guardarClave(v) { try { v ? sessionStorage.setItem(CLAVE_SESION, v) : sessionStorage.removeItem(CLAVE_SESION); } catch { } }

  const fmtFechaHora = new Intl.DateTimeFormat("es-AR", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit" });
  const fmtDia = new Intl.DateTimeFormat("es-AR", { weekday: "short", day: "numeric", month: "short", timeZone: "UTC" });

  function texto(t) { const s = document.createElement("span"); s.textContent = t ?? ""; return s.innerHTML; }

  async function api(ruta) {
    const r = await fetch("/api/admin/" + ruta, { headers: { "X-Admin-Key": clave } });
    const datos = await r.json().catch(() => ({}));
    if (!r.ok) throw Object.assign(new Error(datos.error || "Error " + r.status), { estado: r.status });
    return datos;
  }

  // ---------- Ingreso ----------
  $("ingreso").addEventListener("submit", async e => {
    e.preventDefault();
    clave = $("clave").value;
    await entrar();
  });

  async function entrar() {
    $("ingreso-error").textContent = "";
    try {
      await cargarSolicitudes();
      guardarClave(clave);
      $("ingreso").hidden = true;
      $("tablero").hidden = false;
      $("salir").hidden = false;
      cargarConversaciones();
    } catch (err) {
      guardarClave("");
      $("ingreso").hidden = false;
      $("ingreso-error").textContent = err.message;
    }
  }

  $("salir").addEventListener("click", () => {
    guardarClave(""); clave = "";
    $("tablero").hidden = true; $("salir").hidden = true; $("ingreso").hidden = false;
    $("clave").value = ""; $("clave").focus();
  });

  // ---------- Solicitudes ----------
  async function cargarSolicitudes() {
    solicitudes = await api("solicitudes");
    $("cuenta-solicitudes").textContent = solicitudes.length || "";
    pintarSolicitudes();
  }

  function pintarSolicitudes() {
    const lista = filtro ? solicitudes.filter(s => s.tipo === filtro) : solicitudes;
    $("filas-solicitudes").innerHTML = lista.map(s => {
      const urgente = s.detalle.includes("[URGENCIA]");
      const detalle = texto(s.detalle.replace(" [URGENCIA]", ""));
      const dia = s.fechaPreferida ? fmtDia.format(new Date(s.fechaPreferida + "T12:00:00Z")) + (s.franja ? ", " + texto(s.franja) : "") : "—";
      const tel = s.telefono.replace(/[^\d+]/g, "");
      return `<tr>
        <td class="fecha">${fmtFechaHora.format(new Date(s.fecha))}</td>
        <td><span class="etiqueta ${texto(s.tipo)}">${TIPOS[s.tipo] || texto(s.tipo)}</span></td>
        <td class="nombre">${texto(s.nombre)}</td>
        <td><a href="https://wa.me/${tel}" target="_blank" rel="noopener">${texto(s.telefono)}</a></td>
        <td class="detalle">${urgente ? '<span class="urgencia">Urgencia.</span> ' : ""}${detalle}</td>
        <td class="fecha">${dia}</td>
        <td class="ver"><a href="#" data-conv="${texto(s.conversacionId)}">Ver chat</a></td>
      </tr>`;
    }).join("");
    $("vacio-solicitudes").hidden = lista.length > 0;
    document.querySelector(".tabla-envoltura").hidden = lista.length === 0;
  }

  $("filtros").addEventListener("click", e => {
    const b = e.target.closest(".filtro");
    if (!b) return;
    filtro = b.dataset.tipo;
    document.querySelectorAll(".filtro").forEach(x => x.classList.toggle("activo", x === b));
    pintarSolicitudes();
  });

  $("filas-solicitudes").addEventListener("click", e => {
    const a = e.target.closest("[data-conv]");
    if (!a) return;
    e.preventDefault();
    mostrarVista("conversaciones");
    abrirConversacion(a.dataset.conv);
  });

  // ---------- Conversaciones ----------
  async function cargarConversaciones() {
    const convs = await api("conversaciones").catch(() => []);
    $("cuenta-conversaciones").textContent = convs.length || "";
    $("lista-conv").innerHTML = convs.length
      ? convs.map(c => `<li><button type="button" data-id="${texto(c.id)}">
          <span class="primero">${texto(c.primerMensaje || "(sin mensajes)")}</span>
          <span class="meta">${fmtFechaHora.format(new Date(c.ultimoMensaje))}, ${c.cantidadMensajes} mensajes</span>
        </button></li>`).join("")
      : '<li class="vacio">Todavía no hay conversaciones.</li>';
  }

  $("lista-conv").addEventListener("click", e => {
    const b = e.target.closest("button[data-id]");
    if (b) abrirConversacion(b.dataset.id);
  });

  async function abrirConversacion(id) {
    document.querySelectorAll("#lista-conv button").forEach(b =>
      b.setAttribute("aria-current", b.dataset.id === id ? "true" : "false"));
    const $d = $("detalle-conv");
    $d.innerHTML = '<p class="vacio">Cargando…</p>';
    try {
      const mensajes = await api("conversaciones/" + encodeURIComponent(id));
      $d.innerHTML = mensajes.map(m => `<div class="msg ${m.rol === "user" ? "msg-usuario" : "msg-bot"}">${texto(m.texto.replace(/\*\*/g, ""))}<span class="hora">${fmtFechaHora.format(new Date(m.fecha))}</span></div>`).join("")
        || '<p class="vacio">La conversación está vacía.</p>';
    } catch (err) {
      $d.innerHTML = `<p class="vacio">${texto(err.message)}</p>`;
    }
  }

  // ---------- Pestañas ----------
  function mostrarVista(vista) {
    document.querySelectorAll("[role=tab]").forEach(t => t.setAttribute("aria-selected", String(t.dataset.vista === vista)));
    $("vista-solicitudes").hidden = vista !== "solicitudes";
    $("vista-conversaciones").hidden = vista !== "conversaciones";
  }
  document.querySelectorAll("[role=tab]").forEach(t => t.addEventListener("click", () => mostrarVista(t.dataset.vista)));

  $("actualizar").addEventListener("click", () => { cargarSolicitudes().catch(() => {}); cargarConversaciones(); });

  if (clave) entrar();
})();
