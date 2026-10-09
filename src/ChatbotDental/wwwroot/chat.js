// Chat de Lapi: envía mensajes al backend y muestra las respuestas.
(() => {
  "use strict";

  const CLAVE = "lapacho-chat-v1";
  const SALUDO = "¡Hola! Soy Lapi, del Estudio Dental Lapacho. ¿En qué te puedo ayudar?";

  const $mensajes = document.getElementById("chat-mensajes");
  const $form = document.getElementById("chat-form");
  const $entrada = document.getElementById("chat-entrada");
  const $enviar = document.getElementById("chat-enviar");
  const $sugerencias = document.getElementById("sugerencias");
  const $nueva = document.getElementById("chat-nueva");
  const $estado = document.getElementById("chat-estado");

  let estado = cargar() || nuevoEstado();
  let enviando = false;

  // ---------- Persistencia (por navegador) ----------
  function nuevoId() {
    if (window.crypto && crypto.randomUUID) return crypto.randomUUID();
    return "c" + Date.now().toString(36) + Math.random().toString(36).slice(2, 12);
  }
  function nuevoEstado() {
    return { id: nuevoId(), items: [] };
  }
  function cargar() {
    try {
      const dato = JSON.parse(localStorage.getItem(CLAVE));
      return dato && dato.id && Array.isArray(dato.items) ? dato : null;
    } catch { return null; }
  }
  function guardar() {
    try { localStorage.setItem(CLAVE, JSON.stringify(estado)); } catch { /* sin almacenamiento */ }
  }

  // ---------- Formato del texto del bot (seguro: se escapa antes) ----------
  function escapar(t) {
    return t.replace(/[&<>"']/g, c => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" }[c]));
  }
  function enLinea(t) {
    return escapar(t)
      .replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>")
      .replace(/(^|[\s(])\*(?!\s)(.+?)\*(?=[\s).,!?]|$)/g, "$1<em>$2</em>");
  }
  function formatear(texto) {
    const lineas = texto.trim().split(/\n/);
    let html = "", lista = null, parrafo = [];
    const cerrarParrafo = () => { if (parrafo.length) { html += "<p>" + parrafo.join("<br>") + "</p>"; parrafo = []; } };
    const cerrarLista = () => { if (lista) { html += "<ul>" + lista.join("") + "</ul>"; lista = null; } };

    for (const linea of lineas) {
      const item = linea.match(/^\s*(?:[-*•]|\d+[.)])\s+(.*)$/);
      if (item) { cerrarParrafo(); (lista ||= []).push("<li>" + enLinea(item[1]) + "</li>"); }
      else if (!linea.trim()) { cerrarParrafo(); cerrarLista(); }
      else { cerrarLista(); parrafo.push(enLinea(linea)); }
    }
    cerrarParrafo(); cerrarLista();
    return html;
  }

  // ---------- Pintado ----------
  const ICONOS = {
    turno: '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="3.5" y="5" width="17" height="15" rx="3"/><path d="M8 3v4M16 3v4M3.5 10h17M9 14.5l2 2 4-4"/></svg>',
    consulta: '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M5 12.5l4.5 4.5L19 7.5"/></svg>',
    humano: '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="8" r="3.5"/><path d="M5 20c1-3.8 3.6-5.5 7-5.5s6 1.7 7 5.5"/></svg>',
  };

  function pintar(item, animar) {
    const el = document.createElement("div");
    if (item.tipo === "accion") {
      el.className = "accion";
      el.innerHTML = (ICONOS[item.accion] || ICONOS.consulta) + "<span>" + escapar(item.texto) + "</span>";
    } else if (item.tipo === "error") {
      el.className = "msg msg-error";
      el.textContent = item.texto;
    } else {
      el.className = "msg " + (item.tipo === "usuario" ? "msg-usuario" : "msg-bot");
      if (item.tipo === "usuario") { const p = document.createElement("p"); p.textContent = item.texto; el.appendChild(p); }
      else el.innerHTML = formatear(item.texto);
    }
    if (animar) el.classList.add("nuevo");
    $mensajes.appendChild(el);
    bajar();
    return el;
  }

  function bajar() { $mensajes.scrollTop = $mensajes.scrollHeight; }

  function pintarTodo() {
    $mensajes.innerHTML = "";
    pintar({ tipo: "bot", texto: SALUDO }, false);
    estado.items.forEach(i => pintar(i, false));
    $sugerencias.hidden = estado.items.some(i => i.tipo === "usuario");
  }

  function agregar(item) {
    if (item.tipo !== "error") { estado.items.push(item); guardar(); }
    return pintar(item, true);
  }

  function mostrarEscribiendo() {
    const el = document.createElement("div");
    el.className = "msg msg-bot escribiendo";
    el.setAttribute("aria-label", "Lapi está escribiendo");
    el.innerHTML = "<span></span><span></span><span></span>";
    $mensajes.appendChild(el);
    bajar();
    return el;
  }

  // ---------- Envío ----------
  async function enviar(texto) {
    texto = texto.trim();
    if (!texto || enviando) return;
    enviando = true;
    $enviar.disabled = true;
    $sugerencias.hidden = true;

    agregar({ tipo: "usuario", texto });
    $entrada.value = "";
    ajustarAlto();
    const $escribiendo = mostrarEscribiendo();

    try {
      const r = await fetch("/api/chat", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ conversacionId: estado.id, mensaje: texto }),
      });
      const datos = await r.json().catch(() => ({}));
      $escribiendo.remove();

      if (!r.ok) {
        agregar({ tipo: "error", texto: datos.error || "No pudimos responder ahora. Probá de nuevo en unos segundos." });
        return;
      }
      agregar({ tipo: "bot", texto: datos.respuesta });
      (datos.acciones || []).forEach(a => agregar({ tipo: "accion", accion: a.tipo, texto: a.texto }));
    } catch {
      $escribiendo.remove();
      agregar({ tipo: "error", texto: "Sin conexión con el servidor. Revisá tu internet y volvé a intentar." });
    } finally {
      enviando = false;
      $enviar.disabled = false;
      $entrada.focus({ preventScroll: true });
    }
  }

  function ajustarAlto() {
    $entrada.style.height = "auto";
    $entrada.style.height = Math.min($entrada.scrollHeight, 120) + "px";
  }

  // ---------- Eventos ----------
  $form.addEventListener("submit", e => { e.preventDefault(); enviar($entrada.value); });

  $entrada.addEventListener("keydown", e => {
    if (e.key === "Enter" && !e.shiftKey && !e.isComposing) { e.preventDefault(); enviar($entrada.value); }
  });
  $entrada.addEventListener("input", ajustarAlto);

  document.querySelectorAll("[data-sugerencia]").forEach(b =>
    b.addEventListener("click", () => {
      document.querySelector(".chat").scrollIntoView({ behavior: "smooth", block: "center" });
      enviar(b.dataset.sugerencia);
    })
  );

  $nueva.addEventListener("click", () => {
    estado = nuevoEstado();
    guardar();
    pintarTodo();
    $entrada.focus();
  });

  // Estado del asistente (si falta la API key se avisa)
  fetch("/api/estado").then(r => r.json()).then(d => {
    if (!d.asistenteDisponible) {
      $estado.textContent = "Asistente no configurado";
      $estado.classList.add("sin-conexion");
    }
  }).catch(() => {});

  pintarTodo();
})();
