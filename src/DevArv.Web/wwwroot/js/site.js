// DevArv · interacción del tablero, modales, adjuntos y formularios. Sin dependencias externas.
(() => {
  'use strict';

  const $ = (sel, root = document) => root.querySelector(sel);
  const $$ = (sel, root = document) => Array.from(root.querySelectorAll(sel));
  const modal = $('#modal');
  const modalBody = $('#modal-body');
  const board = $('#board');
  let liveTimer = null;
  let dragging = false;
  const TAB_STORAGE_PREFIX = 'devarv-tab:';

  // ------------------------------------------------------------------ modal

  async function openModal(url, { pushTask, resetTabs = false } = {}) {
    try {
      const res = await fetch(url, { headers: { 'X-Requested-With': 'fetch' }, credentials: 'same-origin' });
      if (res.redirected && res.url.includes('/Identity/Account/Login')) { location.href = res.url; return; }
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      modalBody.innerHTML = await res.text();
    } catch (err) {
      modalBody.innerHTML = `<div class="panel"><p class="alert error">No se pudo cargar: ${escapeHtml(err.message)}</p>
        <button class="btn" data-close-modal>Cerrar</button></div>`;
    }
    if (!modal.open) modal.showModal();
    initDynamic(modalBody, { resetTabs });
    if (pushTask) setTaskParam(pushTask);
    scheduleLive();
  }

  function closeModal() {
    if (modal.open) modal.close();
  }

  modal?.addEventListener('close', () => {
    modalBody.innerHTML = '';
    clearTimeout(liveTimer);
    setTaskParam(null);
  });

  modal?.addEventListener('click', (e) => {
    if (e.target === modal) closeModal(); // clic en el fondo
  });

  function setTaskParam(id) {
    if (!board) return;
    const url = new URL(location.href);
    if (id) url.searchParams.set('task', id); else url.searchParams.delete('task');
    history.replaceState(null, '', url);
  }

  // Refresca el detalle de una tarea mientras su ejecución está en curso.
  function scheduleLive() {
    clearTimeout(liveTimer);
    const details = $('.details[data-live="true"]', modalBody);
    if (!details || !modal.open) return;
    liveTimer = setTimeout(async () => {
      if (!modal.open) return;
      const active = document.activeElement;
      if (active && modalBody.contains(active) && /INPUT|TEXTAREA|SELECT/.test(active.tagName)) { scheduleLive(); return; }
      const openKeys = $$('details[data-key]', modalBody).filter(d => d.open).map(d => d.dataset.key);
      const log = $('pre[data-autoscroll]', modalBody);
      const atBottom = log ? log.scrollTop + log.clientHeight >= log.scrollHeight - 30 : true;
      const scroll = modal.scrollTop;
      try {
        const res = await fetch(details.dataset.self, { credentials: 'same-origin' });
        if (res.ok) {
          modalBody.innerHTML = await res.text();
          $$('details[data-key]', modalBody).forEach(d => { if (openKeys.includes(d.dataset.key)) d.open = true; });
          initDynamic(modalBody);
          const newLog = $('pre[data-autoscroll]', modalBody);
          if (newLog && atBottom) newLog.scrollTop = newLog.scrollHeight;
          modal.scrollTop = scroll;
        }
      } catch { /* reintenta en el próximo ciclo */ }
      scheduleLive();
    }, 4000);
  }

  document.addEventListener('click', (e) => {
    const close = e.target.closest('[data-close-modal]');
    if (close) { e.preventDefault(); closeModal(); return; }

    const opener = e.target.closest('[data-modal]');
    if (opener) { e.preventDefault(); openModal(opener.dataset.modal); return; }

    const inner = e.target.closest('a[data-modal-link]');
    // Navegación explícita a otra ejecución: se abre en la pestaña Log, no en el informe recordado.
    if (inner && modal.open) { e.preventDefault(); openModal(inner.getAttribute('href'), { resetTabs: true }); return; }

    const fix = e.target.closest('[data-resolve-conflicts]');
    if (fix) { e.preventDefault(); resolveConflicts(fix); return; }

    const card = e.target.closest('.card[data-task-id]');
    if (card && !e.target.closest('a, button')) {
      const id = card.dataset.taskId;
      openModal(`/Tasks/Details/${id}?modal=true`, { pushTask: id });
      return;
    }

    // Botón que copia el texto de otro elemento (p. ej. «Copiar log» copia el log completo aunque esté oculto).
    const copyButton = e.target.closest('[data-copy-target]');
    if (copyButton) {
      const source = $(copyButton.dataset.copyTarget);
      if (source && navigator.clipboard) {
        // La etiqueta original se guarda una sola vez: un segundo clic antes de que se restaure no debe tomar «✓ Copiado».
        copyButton.dataset.label ??= copyButton.textContent;
        const label = copyButton.dataset.label;
        clearTimeout(copyButton.restoreTimer);
        navigator.clipboard.writeText(source.textContent).then(
          () => { copyButton.textContent = '✓ Copiado'; },
          () => { copyButton.textContent = 'No se pudo copiar'; });
        copyButton.restoreTimer = setTimeout(() => { copyButton.textContent = label; }, 1500);
      }
      return;
    }

    const copy = e.target.closest('[data-copy]');
    if (copy && navigator.clipboard) {
      navigator.clipboard.writeText(copy.textContent.trim());
      copy.classList.add('copied');
      setTimeout(() => copy.classList.remove('copied'), 1200);
    }
  });

  document.addEventListener('submit', (e) => {
    const form = e.target;
    if (form.dataset.confirm && !confirm(form.dataset.confirm)) { e.preventDefault(); return; }
    $$('input[name="returnQuery"]', form).forEach(i => { i.value = location.search; });
    const fileInput = $('input[type=file][data-max-files]', form);
    if (fileInput && fileInput._error) { e.preventDefault(); alert(fileInput._error); }
  });

  // ------------------------------------------------------------------ tablero

  // Botón 🔧 de la tarjeta: la tarea regresa al desarrollador para arreglar los conflictos de su pull request.
  async function resolveConflicts(button) {
    if (!confirm('La tarea regresará a «Desarrollador»: el agente integrará la rama base y resolverá los conflictos del pull request. ¿Continuar?')) return;
    button.disabled = true;
    const body = new FormData();
    body.set('taskId', button.dataset.resolveConflicts);
    body.set('__RequestVerificationToken', $('input[name="__RequestVerificationToken"]')?.value ?? '');
    try {
      const res = await fetch('/?handler=ResolveConflicts', { method: 'POST', body, credentials: 'same-origin' });
      if (!res.ok) {
        let message = `No se pudo pedir el arreglo de conflictos (HTTP ${res.status}).`;
        try { message = (await res.json()).error ?? message; } catch { /* sin cuerpo JSON */ }
        alert(message);
      }
    } catch (err) {
      alert(`No se pudo pedir el arreglo de conflictos: ${err.message}`);
    }
    button.disabled = false;
    refreshBoard();
  }

  async function refreshBoard() {
    if (!board || dragging || document.hidden) return;
    const params = new URLSearchParams(location.search);
    params.delete('task');
    params.set('handler', 'Board');
    try {
      const res = await fetch('/?' + params.toString(), { credentials: 'same-origin' });
      if (res.redirected && res.url.includes('/Identity/Account/Login')) { location.href = res.url; return; }
      if (!res.ok || dragging) return;
      const scrollers = $$('.dropzone', board).map(z => [z.closest('.column')?.dataset.stageId, z.scrollTop]);
      const left = $('.columns', board)?.scrollLeft ?? 0;
      const origins = new Map($$('.card[data-task-id] .dev-timer', board)
        .filter(el => el._origin !== undefined)
        .map(el => [el.closest('.card').dataset.taskId, el._origin]));
      board.innerHTML = await res.text();
      keepTimerOrigins(origins);
      const cols = $('.columns', board);
      if (cols) cols.scrollLeft = left;
      scrollers.forEach(([id, top]) => {
        const zone = $(`.column[data-stage-id="${id}"] .dropzone`, board);
        if (zone) zone.scrollTop = top;
      });
      const generated = $('.columns', board)?.dataset.generated;
      const label = $('#board-updated');
      if (generated && label) label.textContent = `Actualizado ${generated}`;
      const developing = $('.columns', board)?.dataset.developing;
      const running = $('#running-count');
      if (developing !== undefined && running) running.textContent = developing;
    } catch { /* sin conexión: reintenta luego */ }
  }

  // Contador del desarrollador: el servidor manda los segundos transcurridos al generar el tablero
  // (data-elapsed) y aquí se suman los del reloj del navegador, sin depender de que ambos relojes coincidan.
  // El formato debe coincidir con Labels.Stopwatch.
  function stopwatch(totalSeconds) {
    const total = Math.max(0, Math.floor(totalSeconds));
    const h = Math.floor(total / 3600);
    const m = String(Math.floor(total / 60) % 60).padStart(2, '0');
    const s = String(total % 60).padStart(2, '0');
    return h > 0 ? `${h}:${m}:${s}` : `${m}:${s}`;
  }

  function tickTimers() {
    const now = Date.now();
    $$('.dev-timer[data-elapsed]', board).forEach(el => {
      if (el._origin === undefined) el._origin = now - Number(el.dataset.elapsed) * 1000;
      const text = $('[data-timer-text]', el);
      if (text) text.textContent = stopwatch((now - el._origin) / 1000);
    });
  }

  // Tras refrescar el tablero conserva el origen de cada contador para que no retroceda por la latencia;
  // si difiere mucho (otra ejecución, PC suspendida) manda el valor del servidor.
  function keepTimerOrigins(origins) {
    const now = Date.now();
    $$('.card[data-task-id] .dev-timer[data-elapsed]', board).forEach(el => {
      const fresh = now - Number(el.dataset.elapsed) * 1000;
      const previous = origins.get(el.closest('.card').dataset.taskId);
      el._origin = previous !== undefined && Math.abs(previous - fresh) < 5000 ? previous : fresh;
    });
    tickTimers();
  }

  if (board) {
    setInterval(refreshBoard, 5000);
    setInterval(tickTimers, 1000);

    board.addEventListener('dragstart', (e) => {
      const card = e.target.closest('.card[data-task-id]');
      if (!card) return;
      dragging = true;
      card.classList.add('dragging');
      e.dataTransfer.setData('text/plain', card.dataset.taskId);
      e.dataTransfer.effectAllowed = 'move';
    });
    board.addEventListener('dragend', (e) => {
      dragging = false;
      e.target.closest('.card')?.classList.remove('dragging');
      $$('.column.drop-target', board).forEach(c => c.classList.remove('drop-target'));
    });
    board.addEventListener('dragover', (e) => {
      const col = e.target.closest('.column');
      if (!col) return;
      e.preventDefault();
      $$('.column.drop-target', board).forEach(c => { if (c !== col) c.classList.remove('drop-target'); });
      col.classList.add('drop-target');
    });
    board.addEventListener('drop', async (e) => {
      const col = e.target.closest('.column');
      if (!col) return;
      e.preventDefault();
      dragging = false;
      col.classList.remove('drop-target');
      const taskId = e.dataTransfer.getData('text/plain');
      if (!taskId) return;
      const body = new FormData();
      body.set('taskId', taskId);
      body.set('stageId', col.dataset.stageId);
      body.set('__RequestVerificationToken', $('input[name="__RequestVerificationToken"]')?.value ?? '');
      const res = await fetch('/?handler=Move', { method: 'POST', body, credentials: 'same-origin' });
      if (!res.ok) {
        let message = `No se pudo mover (HTTP ${res.status}).`;
        try { message = (await res.json()).error ?? message; } catch { /* sin cuerpo JSON */ }
        alert(message);
      }
      refreshBoard();
    });

    const open = board.dataset.openTask;
    if (open) openModal(`/Tasks/Details/${open}?modal=true`, { pushTask: open });
  }

  // ------------------------------------------------------------------ contenido dinámico

  function initDynamic(root, { resetTabs = false } = {}) {
    // Pestañas. Sin nombre (data-tabs) controlan los paneles del mismo contenedor; con nombre (data-tabs="grupo")
    // controlan los paneles con data-tab-group="grupo" (permite pestañas anidadas) y recuerdan la pestaña elegida
    // en la sesión del navegador, para que no se pierda al refrescar el detalle o al volver tras un formulario.
    // Con resetTabs (navegación explícita) se olvida la pestaña recordada y cada grupo abre en su pestaña inicial.
    $$('[data-tabs]', root).forEach(tabs => {
      const group = tabs.dataset.tabs;
      const panels = () => group
        ? $$('[data-tab-panel]', root).filter(p => p.dataset.tabGroup === group)
        : $$('[data-tab-panel]', tabs.parentElement);
      const activate = (name, remember) => {
        const btn = $$('[data-tab]', tabs).find(b => b.dataset.tab === name);
        if (!btn) return;
        $$('[data-tab]', tabs).forEach(b => b.classList.toggle('on', b === btn));
        panels().forEach(p => { p.hidden = p.dataset.tabPanel !== name; });
        if (group && remember) {
          try { sessionStorage.setItem(TAB_STORAGE_PREFIX + group, name); } catch { /* sin almacenamiento */ }
        }
      };
      tabs.addEventListener('click', (e) => {
        const btn = e.target.closest('[data-tab]');
        if (btn) activate(btn.dataset.tab, true);
      });
      if (group) {
        let saved = null;
        try {
          if (resetTabs) sessionStorage.removeItem(TAB_STORAGE_PREFIX + group);
          else saved = sessionStorage.getItem(TAB_STORAGE_PREFIX + group);
        } catch { /* sin almacenamiento */ }
        if (saved) activate(saved, false);
      }
    });

    // Rama base según el proyecto elegido
    $$('select[data-branch-source]', root).forEach(sel => {
      const target = $('[data-branch-target]', sel.form);
      sel.addEventListener('change', () => {
        const branch = sel.selectedOptions[0]?.dataset.defaultBranch;
        if (branch && target) target.value = branch;
      });
    });

    // Adjuntos: arrastrar, pegar, quitar y validar en el navegador (el servidor valida de nuevo)
    $$('[data-dropfiles]', root).forEach(initDropFiles);

    // Mostrar campos según el tipo de columna
    $$('[data-stage-form]', root).forEach(form => {
      const select = $('[data-kind-select]', form);
      const apply = () => $$('[data-kind-show]', form).forEach(f => { f.hidden = f.dataset.kindShow !== select.value; });
      select.addEventListener('change', apply);
      apply();
    });

    // Log al final
    $$('pre[data-autoscroll]', root).forEach(pre => { pre.scrollTop = pre.scrollHeight; });
  }

  const allowedExt = ['.png', '.jpg', '.jpeg', '.gif', '.webp', '.pdf', '.html', '.htm'];

  function initDropFiles(zone) {
    const input = $('input[type=file]', zone);
    const list = $('[data-file-list]', zone);
    const form = zone.closest('form');
    const badge = form ? $('[data-file-count]', form) : null;
    const maxFiles = Number(input.dataset.maxFiles || 15);
    const maxBytes = Number(input.dataset.maxBytes || 20 * 1024 * 1024);
    let files = [];

    const render = () => {
      const dt = new DataTransfer();
      files.forEach(f => dt.items.add(f));
      input.files = dt.files;
      if (badge) badge.textContent = String(files.length);
      list.innerHTML = '';
      const problems = [];
      if (files.length > maxFiles) problems.push(`Máximo ${maxFiles} archivos.`);
      files.forEach((f, i) => {
        const ext = (f.name.match(/\.[^.]+$/)?.[0] ?? '').toLowerCase();
        let problem = '';
        if (!allowedExt.includes(ext)) problem = 'formato no permitido';
        else if (f.size > maxBytes) problem = `supera ${Math.round(maxBytes / 1048576)} MB`;
        if (problem) problems.push(`«${f.name}»: ${problem}.`);
        const li = document.createElement('li');
        li.className = problem ? 'bad' : '';
        li.innerHTML = `<span></span><small>${(f.size / 1024).toFixed(0)} KB ${problem ? '· ' + problem : ''}</small>
          <button type="button" class="btn btn-ghost small" title="Quitar">✕</button>`;
        li.firstElementChild.textContent = f.name;
        li.querySelector('button').addEventListener('click', () => { files.splice(i, 1); render(); });
        list.appendChild(li);
      });
      input._error = problems.length ? problems.join('\n') : null;
    };

    const add = (incoming) => {
      for (const f of incoming) {
        if (!files.some(x => x.name === f.name && x.size === f.size)) files.push(f);
      }
      render();
    };

    input.addEventListener('change', () => add(Array.from(input.files)));
    zone.addEventListener('dragover', (e) => { e.preventDefault(); zone.classList.add('over'); });
    zone.addEventListener('dragleave', () => zone.classList.remove('over'));
    zone.addEventListener('drop', (e) => {
      e.preventDefault();
      zone.classList.remove('over');
      add(Array.from(e.dataTransfer.files));
    });
    // Pegar capturas de pantalla (Ctrl+V) en cualquier parte del formulario.
    form?.addEventListener('paste', (e) => {
      const pasted = Array.from(e.clipboardData?.files ?? []);
      if (!pasted.length) return;
      e.preventDefault();
      const stamp = new Date().toISOString().replace(/[:.]/g, '-');
      add(pasted.map((f, i) => f.name && f.name !== 'image.png'
        ? f
        : new File([f], `captura-${stamp}-${i + 1}.${(f.type.split('/')[1] || 'png').replace('jpeg', 'jpg')}`, { type: f.type })));
    });
  }

  function escapeHtml(text) {
    const div = document.createElement('div');
    div.textContent = text;
    return div.innerHTML;
  }

  initDynamic(document);
})();
