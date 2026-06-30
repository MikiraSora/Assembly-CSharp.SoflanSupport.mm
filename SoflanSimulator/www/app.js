// ==================== State ====================
let sessionData = null;
let sessionId = null;
let cursorTime = 0;
let selectedNoteIndex = null;
let selectedNoteCurve = null;
let cursorRequest = null;
let rafScheduled = false;
let dragging = false;

const Y_MIN = 120, Y_MAX = 680;

// ==================== DOM ====================
const $ = id => document.getElementById(id);
const gCanvas = $('global-canvas');
const gCtx = gCanvas.getContext('2d');
const dCanvas = $('detail-canvas');
const dCtx = dCanvas.getContext('2d');

// ==================== Params ====================
function getParams() {
  return {
    defaultMsec: parseFloat($('p-default-msec').value) || 2000,
    maiBugAdjust: parseFloat($('p-mai-bug-adjust').value) || 0,
    startPos: parseFloat($('p-start-pos').value) || 120,
    endPos: parseFloat($('p-end-pos').value) || 400,
    noteSpeed: parseFloat($('p-note-speed').value) || 150,
  };
}
function getStep() { return parseFloat($('p-step').value) || 16; }
function paramStr() {
  const p = getParams();
  return `defaultMsec=${p.defaultMsec}&maiBugAdjust=${p.maiBugAdjust}&startPos=${p.startPos}&endPos=${p.endPos}&noteSpeed=${p.noteSpeed}`;
}

// ==================== Canvas Setup ====================
function resizeCanvases() {
  [gCanvas, dCanvas].forEach(c => {
    const r = c.getBoundingClientRect();
    c.width = r.width;
    c.height = r.height;
  });
}
window.addEventListener('resize', () => { resizeCanvases(); drawGlobal(); drawDetail(); });

// ==================== Load ====================
$('load-btn').addEventListener('click', async () => {
  const filePath = $('file-path').value.trim();
  if (!filePath) return;
  $('status').textContent = 'Loading...';
  try {
    const resp = await fetch('/api/load', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ file: filePath })
    });
    const data = await resp.json();
    if (data.error) { $('status').textContent = 'Error: ' + data.error; return; }
    sessionData = data;
    sessionId = data.sessionId;
    selectedNoteIndex = null;
    selectedNoteCurve = null;
    cursorTime = 0;
    $('status').textContent = `Loaded: ${data.notes.filter(n=>n.active).length} active notes, ${data.sflList.length} SFL, ${data.bpmList.length} BPM, duration=${(data.durationMs/1000).toFixed(1)}s`;
    drawGlobal();
    drawDetail();
  } catch (e) {
    $('status').textContent = 'Error: ' + e.message;
  }
});

// Enter key on file path triggers load
$('file-path').addEventListener('keydown', e => { if (e.key === 'Enter') $('load-btn').click(); });

// ==================== Coordinate Mapping ====================
function timeToX(t, w) { return (t / sessionData.durationMs) * w; }
function xToTime(x, w) { return (x / w) * sessionData.durationMs; }
function yToCanvasY(y, h) { return ((y - Y_MIN) / (Y_MAX - Y_MIN)) * h; }

// ==================== Global Canvas ====================
function drawGlobal() {
  if (!sessionData) return;
  const w = gCanvas.width, h = gCanvas.height;
  gCtx.clearRect(0, 0, w, h);
  gCtx.fillStyle = '#0d1117';
  gCtx.fillRect(0, 0, w, h);

  // SoFlan speed bands
  for (const sfl of sessionData.sflList) {
    const x1 = timeToX(sfl.startMs, w);
    const x2 = timeToX(sfl.endMs, w);
    const speed = sfl.speed;
    let color;
    if (speed < 0) color = 'rgba(255,60,60,0.15)';
    else if (speed > 1) color = `rgba(255,180,40,${Math.min(0.2, 0.05 * speed)})`;
    else color = `rgba(60,140,255,${Math.min(0.2, 0.05 + 0.1 * (1 - speed))})`;
    gCtx.fillStyle = color;
    gCtx.fillRect(x1, 0, Math.max(1, x2 - x1), h);
  }

  // BPM change markers
  gCtx.strokeStyle = 'rgba(100,200,100,0.3)';
  gCtx.setLineDash([3, 3]);
  gCtx.lineWidth = 1;
  for (const bpm of sessionData.bpmList) {
    const x = timeToX(bpm.timeMs, w);
    gCtx.beginPath();
    gCtx.moveTo(x, 0);
    gCtx.lineTo(x, h);
    gCtx.stroke();
  }
  gCtx.setLineDash([]);

  // Note markers (active only)
  const scaleStartTime = 2 * getParams().defaultMsec - getParams().maiBugAdjust;
  for (const note of sessionData.notes) {
    if (!note.active) continue;
    const x = timeToX(note.appearMsec, w);
    // Check if active at cursor
    const diff = note.appearMsec - cursorTime;
    const isActive = Math.abs(diff) <= scaleStartTime;
    gCtx.strokeStyle = note.indexNote === selectedNoteIndex ? '#ffcc00' : (isActive ? '#44aaff' : '#444466');
    gCtx.lineWidth = note.indexNote === selectedNoteIndex ? 2 : 1;
    gCtx.beginPath();
    gCtx.moveTo(x, 0);
    gCtx.lineTo(x, h);
    gCtx.stroke();
  }

  // Selected note's Y(t) curve
  if (selectedNoteCurve) {
    gCtx.strokeStyle = '#ffcc00';
    gCtx.lineWidth = 2;
    gCtx.beginPath();
    let started = false;
    for (const pt of selectedNoteCurve.points) {
      const x = timeToX(pt.t, w);
      const y = yToCanvasY(pt.screenY, h);
      if (!started) { gCtx.moveTo(x, y); started = true; }
      else gCtx.lineTo(x, y);
    }
    gCtx.stroke();
  }

  // Time cursor
  const cx = timeToX(cursorTime, w);
  gCtx.strokeStyle = '#ffffff';
  gCtx.lineWidth = 1;
  gCtx.beginPath();
  gCtx.moveTo(cx, 0);
  gCtx.lineTo(cx, h);
  gCtx.stroke();

  // Time label
  gCtx.fillStyle = '#ffffff';
  gCtx.font = '11px Consolas';
  gCtx.fillText(`${(cursorTime/1000).toFixed(2)}s`, cx + 4, 14);
}

// ==================== Detail Canvas ====================
function drawDetail() {
  if (!selectedNoteCurve) {
    dCtx.clearRect(0, 0, dCanvas.width, dCanvas.height);
    dCtx.fillStyle = '#1a1a2e';
    dCtx.fillRect(0, 0, dCanvas.width, dCanvas.height);
    dCtx.fillStyle = '#445566';
    dCtx.font = '12px Consolas';
    dCtx.fillText('Click a note marker to view detail', 10, 20);
    return;
  }

  const w = dCanvas.width, h = dCanvas.height;
  dCtx.clearRect(0, 0, w, h);
  dCtx.fillStyle = '#0d1117';
  dCtx.fillRect(0, 0, w, h);

  const points = selectedNoteCurve.points;
  if (points.length < 2) return;
  const tMin = points[0].t, tMax = points[points.length - 1].t;
  const tRange = tMax - tMin || 1;

  function dx(t) { return ((t - tMin) / tRange) * w; }
  function dy(y) { return ((y - Y_MIN) / (Y_MAX - Y_MIN)) * h; }

  // Zone lines
  const p = getParams();
  const moveStartTime = p.defaultMsec - p.maiBugAdjust;
  const scaleStartTime = 2 * p.defaultMsec - p.maiBugAdjust;
  const appearMsec = selectedNoteCurve.appearMsec;

  dCtx.strokeStyle = 'rgba(255,255,255,0.1)';
  dCtx.lineWidth = 1;
  [Y_MIN, p.endPos, Y_MAX].forEach(y => {
    const cy = dy(y);
    dCtx.beginPath(); dCtx.moveTo(0, cy); dCtx.lineTo(w, cy); dCtx.stroke();
  });

  // Zone labels
  dCtx.fillStyle = '#334455';
  dCtx.font = '10px Consolas';
  dCtx.fillText(`y=${Y_MIN} (StartPos)`, 4, dy(Y_MIN) + 12);
  dCtx.fillText(`y=${p.endPos} (EndPos)`, 4, dy(p.endPos) - 3);
  dCtx.fillText(`y=${Y_MAX} (outsideY)`, 4, dy(Y_MAX) - 3);

  // Y(t) curve
  dCtx.strokeStyle = '#ffcc00';
  dCtx.lineWidth = 2;
  dCtx.beginPath();
  for (let i = 0; i < points.length; i++) {
    const x = dx(points[i].t);
    const y = dy(points[i].screenY);
    if (i === 0) dCtx.moveTo(x, y);
    else dCtx.lineTo(x, y);
  }
  dCtx.stroke();

  // Speed curve (secondary axis)
  if (sessionData.soflanGroups && sessionData.soflanGroups[selectedNoteCurve.soflanGroup]) {
    const speedPoints = sessionData.soflanGroups[selectedNoteCurve.soflanGroup];
    dCtx.strokeStyle = 'rgba(100,200,255,0.5)';
    dCtx.lineWidth = 1;
    dCtx.beginPath();
    let started = false;
    for (const sp of speedPoints) {
      if (sp.timeMs < tMin || sp.timeMs > tMax) continue;
      const x = dx(sp.timeMs);
      const y = h - (sp.speed / 3.0) * h; // scale speed to 0-3 range
      if (!started) { dCtx.moveTo(x, y); started = true; }
      else dCtx.lineTo(x, y);
    }
    // extend to end
    if (started) {
      const lastSp = speedPoints[speedPoints.length - 1];
      if (lastSp.timeMs < tMax) dCtx.lineTo(w, h - (lastSp.speed / 3.0) * h);
    }
    dCtx.stroke();
  }

  // Cursor line
  const cx = dx(cursorTime);
  if (cx >= 0 && cx <= w) {
    dCtx.strokeStyle = '#ffffff';
    dCtx.lineWidth = 1;
    dCtx.beginPath();
    dCtx.moveTo(cx, 0); dCtx.lineTo(cx, h);
    dCtx.stroke();

    // Dot on curve at cursor
    const pt = findClosestPoint(points, cursorTime);
    if (pt) {
      dCtx.fillStyle = '#ff4444';
      dCtx.beginPath();
      dCtx.arc(dx(pt.t), dy(pt.screenY), 4, 0, Math.PI * 2);
      dCtx.fill();
    }
  }
}

function findClosestPoint(points, t) {
  let best = null, bestDist = Infinity;
  for (const p of points) {
    const d = Math.abs(p.t - t);
    if (d < bestDist) { bestDist = d; best = p; }
  }
  return best;
}

// ==================== Cursor Interaction ====================
gCanvas.addEventListener('mousedown', e => {
  if (!sessionData) return;
  dragging = true;
  updateCursorFromEvent(e);
});

window.addEventListener('mousemove', e => {
  if (!dragging || !sessionData) return;
  updateCursorFromEvent(e);
});

window.addEventListener('mouseup', () => { dragging = false; });

function updateCursorFromEvent(e) {
  const rect = gCanvas.getBoundingClientRect();
  const x = e.clientX - rect.left;
  cursorTime = Math.max(0, Math.min(sessionData.durationMs, xToTime(x, gCanvas.width)));
  drawGlobal();

  // Check for note click (select)
  if (!dragging) {
    // This is a click, try to select a note
    trySelectNote(x);
  }

  scheduleCursorUpdate();
}

gCanvas.addEventListener('click', e => {
  if (!sessionData) return;
  const rect = gCanvas.getBoundingClientRect();
  const x = e.clientX - rect.left;
  trySelectNote(x);
});

function trySelectNote(clickX) {
  if (!sessionData) return;
  let bestNote = null, bestDist = 10; // pixel threshold
  for (const note of sessionData.notes) {
    if (!note.active) continue;
    const nx = timeToX(note.appearMsec, gCanvas.width);
    const d = Math.abs(nx - clickX);
    if (d < bestDist) { bestDist = d; bestNote = note; }
  }
  if (bestNote) {
    selectNote(bestNote.indexNote);
  }
}

async function selectNote(noteIndex) {
  selectedNoteIndex = noteIndex;
  $('detail-note-label').textContent = `(note #${noteIndex})`;
  $('status').textContent = `Loading curve for note ${noteIndex}...`;
  try {
    const resp = await fetch(`/api/computeCurve?sessionId=${sessionId}&noteIndex=${noteIndex}&${paramStr()}&step=${getStep()}`);
    const data = await resp.json();
    if (data.error) { $('status').textContent = 'Error: ' + data.error; return; }
    selectedNoteCurve = data;
    $('status').textContent = `Note ${noteIndex}: appearMsec=${data.appearMsec.toFixed(1)}ms, ${data.points.length} points`;
    drawGlobal();
    drawDetail();
    updateValuesTable();
  } catch (e) {
    $('status').textContent = 'Error: ' + e.message;
  }
}

// ==================== Cursor Update (rAF + abort) ====================
function scheduleCursorUpdate() {
  if (rafScheduled) return;
  rafScheduled = true;
  requestAnimationFrame(() => {
    rafScheduled = false;
    if (cursorRequest) { cursorRequest.abort(); cursorRequest = null; }
    cursorRequest = new XMLHttpRequest();
    cursorRequest.open('GET', `/api/computeAt?sessionId=${sessionId}&time=${cursorTime}&${paramStr()}`);
    cursorRequest.onload = () => {
      if (cursorRequest.status !== 200) return;
      try {
        const data = JSON.parse(cursorRequest.responseText);
        updateValuesTable(data);
        drawGlobal();
        drawDetail();
      } catch (e) {}
    };
    cursorRequest.onerror = () => {};
    cursorRequest.send();
  });
}

// ==================== Values Table ====================
function updateValuesTable(cursorData) {
  if (!selectedNoteCurve) {
    if (cursorData) {
      // Show all active notes at cursor
      const rows = cursorData.notes.map(n => ({
        note: n.noteIndex,
        diffTime: n.diffTime,
        screenY: n.screenY,
        soflanY: n.soflanY,
      }));
      renderTable(rows, ['note', 'diffTime', 'screenY', 'soflanY']);
    } else {
      $('values-table').innerHTML = '';
    }
    return;
  }

  // Show selected note's detail at cursor
  const pt = findClosestPoint(selectedNoteCurve.points, cursorTime);
  if (pt) {
    const rows = [
      { key: 'currentMsec', val: pt.t.toFixed(2) },
      { key: 'diffTime', val: pt.diffTime.toFixed(4) },
      { key: 'absDiffTime', val: pt.absDiffTime.toFixed(4) },
      { key: 'noteSoflanTime', val: pt.noteSoflanTime.toFixed(4) },
      { key: 'currentSoflanTime', val: pt.currentSoflanTime.toFixed(4) },
      { key: 'soflanY', val: pt.soflanY.toFixed(2) },
      { key: 'screenY', val: pt.screenY.toFixed(2) },
      { key: 'guideScale', val: pt.guideScale.toFixed(4) },
      { key: 'finalScale', val: pt.finalScale.toFixed(4) },
    ];
    renderTable(rows, ['key', 'val']);
  }

  // Also show active notes count
  if (cursorData) {
    const count = cursorData.notes.length;
    $('detail-note-label').textContent = `(note #${selectedNoteIndex}) | ${count} active at cursor`;
  }
}

function renderTable(rows, headers) {
  let html = '<table><tr>';
  for (const h of headers) html += `<th>${h}</th>`;
  html += '</tr>';
  for (const r of rows) {
    html += '<tr>';
    for (const h of headers) html += `<td>${r[h] !== undefined ? r[h] : ''}</td>`;
    html += '</tr>';
  }
  html += '</table>';
  $('values-table').innerHTML = html;
}

// ==================== Param Change ====================
document.querySelectorAll('#toolbar input[type="number"]').forEach(input => {
  input.addEventListener('change', () => {
    if (!sessionData) return;
    // Re-fetch curve if note selected
    if (selectedNoteIndex !== null) {
      selectNote(selectedNoteIndex);
    } else {
      drawGlobal();
    }
  });
});

// ==================== Init ====================
resizeCanvases();
drawGlobal();
drawDetail();