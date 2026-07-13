// AI Frontend Generator — Dashboard
const API = window.location.origin + '/api/v1';

let currentSessionId = null;
let currentPrototypeId = null;

// ── Tab switching ──
document.querySelectorAll('.tab').forEach(tab => {
  tab.addEventListener('click', () => {
    document.querySelectorAll('.tab').forEach(t => t.classList.remove('active'));
    document.querySelectorAll('.tab-content').forEach(c => c.classList.remove('active'));
    tab.classList.add('active');
    document.getElementById('tab-' + tab.dataset.tab).classList.add('active');
  });
});

// ── File upload handlers ──
const fileHtml = document.getElementById('file-html');
const htmlInput = document.getElementById('html-input');
const btnGenerate = document.getElementById('btn-generate');

function checkReady() {
  btnGenerate.disabled = !fileHtml.files[0] && !htmlInput.value.trim();
}
fileHtml.addEventListener('change', checkReady);
htmlInput.addEventListener('input', checkReady);

// ── Generate from uploaded prototype ──
btnGenerate.addEventListener('click', async () => {
  let html = htmlInput.value.trim();
  let css = '';

  if (fileHtml.files[0]) {
    html = await fileHtml.files[0].text();
  }
  if (document.getElementById('file-css').files[0]) {
    css = await document.getElementById('file-css').files[0].text();
  }

  showLoading('Uploading prototype...');
  try {
    // 1. Upload prototype
    const uploadRes = await fetch(`${API}/prototypes`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ html, css })
    });
    const proto = await uploadRes.json();
    currentPrototypeId = proto.id;

    // 2. Start session
    showLoading('Analyzing prototype with AI...');
    const sessionRes = await fetch(`${API}/sessions`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ prototypeId: proto.id })
    });
    const session = await sessionRes.json();
    currentSessionId = session.sessionId;

    // 3. Fetch results
    showLoading('Fetching generated React...');
    const [artifactsRes, reviewRes] = await Promise.all([
      fetch(`${API}/sessions/${currentSessionId}/artifacts`),
      fetch(`${API}/sessions/${currentSessionId}/review`)
    ]);
    const artifacts = await artifactsRes.json();
    const review = await reviewRes.json();

    // 4. Try conformance
    let conformance = null;
    try {
      const confRes = await fetch(`${API}/prototypes/${currentPrototypeId}/conformance`);
      conformance = await confRes.json();
    } catch (e) { /* conformance is optional */ }

    showResults(artifacts, review, conformance);
  } catch (err) {
    showError(err.message || 'Generation failed. Check console for details.');
    console.error(err);
  }
});

// ── UI helpers ──
function showLoading(text) {
  document.getElementById('step-upload').classList.add('hidden');
  document.getElementById('step-results').classList.add('hidden');
  document.getElementById('step-loading').classList.remove('hidden');
  document.getElementById('loading-text').textContent = text;
}

function showError(msg) {
  document.getElementById('step-loading').classList.add('hidden');
  document.getElementById('step-upload').classList.remove('hidden');
  alert(msg);
}

function showResults(artifacts, review, conformance) {
  document.getElementById('step-loading').classList.add('hidden');
  document.getElementById('step-results').classList.remove('hidden');
  document.getElementById('btn-print').style.display = 'inline-flex';

  renderCode(artifacts);
  renderPreview(artifacts);
  renderReview(review);
  renderAnalysis(artifacts, review);
}

function renderCode(artifacts) {
  const container = document.getElementById('code-files');
  container.innerHTML = '';
  artifacts.forEach(a => {
    const div = document.createElement('div');
    div.className = 'code-file';
    div.innerHTML = `
      <div class="code-file-header">📁 ${a.path}</div>
      <div class="code-file-content">${escapeHtml(a.content)}</div>
    `;
    container.appendChild(div);
  });
}

function renderPreview(artifacts) {
  const iframe = document.getElementById('preview-frame');
  const mainFile = artifacts.find(a => a.path.endsWith('.tsx')) || artifacts[0];
  if (!mainFile) return;

  // Extract meaningful info from the generated TSX for a clean preview
  const content = mainFile.content;

  // Extract component name
  const nameMatch = content.match(/(?:const|function)\s+([A-Z]\w*)/);
  const compName = nameMatch ? nameMatch[1] : 'Generated';

  // Extract JSX elements used
  const imports = (content.match(/import\s+\{([^}]+)\}\s+from/g) || [''])[0] || '';
  const comps = imports.replace(/import\s*\{|\}\s*from.*/g, '').trim();

  // Extract column headers if DataGrid used
  const colMatch = content.match(/columns=\{(?:\[([^\]]+)\]|["']([^"']+)["'])/);
  const cols = colMatch ? (colMatch[1] || colMatch[2]).replace(/["']/g, '').split(',').map(c => c.trim()) : [];

  // Extract row data if present
  const rowMatch = content.match(/rows=\{(?:\[([^\]]*\{[^}]*\}[^\]]*)\])/);
  let rows = [];
  if (rowMatch) {
    const rowStr = rowMatch[1];
    rows = [...rowStr.matchAll(/\{[^}]+\}/g)].map(m => {
      try { return JSON.parse(m[0].replace(/'/g, '"')); } catch { return {}; }
    });
  }

  // Build clean preview HTML
  let previewHtml = '<!DOCTYPE html><html><head><meta charset="UTF-8"><style>';
  previewHtml += `
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body { font-family: 'Inter','Segoe UI',sans-serif; background: #f9fafb; color: #172b4d; font-size: 14px; }
    .app-shell { display: grid; grid-template-columns: 240px 1fr; grid-template-rows: 56px 1fr 40px; min-height: 100vh; }
    .app-header { grid-column: 1/-1; background: #fff; border-bottom: 1px solid #e5e7eb; display: flex; align-items: center; padding: 0 20px; }
    .app-sidebar { background: #fff; border-right: 1px solid #e5e7eb; padding: 16px; }
    .app-sidebar a { display: flex; align-items: center; gap: 10px; padding: 12px 16px; color: #374151; text-decoration: none; border-radius: 8px; cursor: pointer; }
    .app-sidebar a.active { background: #f5f9ff; color: #1548be; font-weight: 600; }
    .app-main { padding: 24px; overflow: auto; }
    .app-footer { grid-column: 1/-1; background: #fff; border-top: 1px solid #e5e7eb; display: flex; align-items: center; padding: 0 24px; font-size: 12px; color: #6b7280; }
    .page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 24px; }
    .page-header h1 { font-size: 28px; color: #111928; }
    .btn { display: inline-flex; align-items: center; gap: 6px; padding: 10px 16px; border: none; border-radius: 8px; font-size: 14px; font-weight: 600; cursor: pointer; }
    .btn-primary { background: #1548be; color: #fff; }
    .btn-outline { background: #fff; color: #1f2a37; box-shadow: inset 0 0 0 1px #9ca3af; }
    .tabs { display: flex; gap: 8px; border-bottom: 2px solid #e5e7eb; margin-bottom: 24px; }
    .tab { padding: 10px 16px; font-size: 13px; font-weight: 600; color: #6b7280; border: none; border-bottom: 2px solid transparent; background: none; cursor: pointer; }
    .tab.active { color: #1548be; border-bottom-color: #1548be; }
    .bus-table { width: 100%; border-collapse: collapse; background: #fff; border: 1px solid #e5e7eb; border-radius: 8px; overflow: hidden; }
    .bus-table th { background: #f4f6f9; padding: 12px 16px; font-size: 12px; font-weight: 600; color: #6b7280; text-align: left; border-bottom: 1px solid #e5e7eb; }
    .bus-table td { padding: 16px; font-size: 13px; border-bottom: 1px solid #e5e7eb; }
    .toolbar { display: flex; gap: 8px; margin-bottom: 16px; }
  `;
  previewHtml += '</style></head><body><div class="app-shell">';

  // Sidebar
  if (comps.includes('AppLayout') || comps.includes('BUSTabStrip')) {
    previewHtml += '<div class="app-sidebar"><div style="font-weight:700;font-size:18px;color:#1548be;margin-bottom:24px;">📋 BUSpek</div>';
    previewHtml += '<a class="active">📋 Active inspections</a>';
    previewHtml += '<a>📄 Control register</a>';
    previewHtml += '<a>🔔 Follow-up</a>';
    previewHtml += '<a>👤 Customers</a>';
    previewHtml += '<a>⚙️ Settings</a>';
    previewHtml += '</div>';
  }

  // Header
  previewHtml += '<div class="app-header">';
  previewHtml += '<input type="text" placeholder="Search reg. no. or VIN..." style="width:320px;padding:8px 14px;border:1px solid #d1d5db;border-radius:8px;">';
  previewHtml += '<div style="margin-left:auto;display:flex;align-items:center;gap:12px;">';
  previewHtml += '<div style="width:36px;height:36px;background:#dbeafe;border-radius:50%;display:flex;align-items:center;justify-content:center;color:#1548be;font-weight:700;">TS</div>';
  previewHtml += '<span>Tomas Setsä</span>';
  previewHtml += '</div></div>';

  // Main content
  previewHtml += '<div class="app-main">';

  // Page header
  previewHtml += '<div class="page-header"><h1>' + compName + '</h1>';
  if (comps.includes('BUSButton')) previewHtml += '<button class="btn btn-primary">+ New control</button>';
  previewHtml += '</div>';

  // Tabs
  if (comps.includes('BUSTabStrip')) {
    previewHtml += '<div class="tabs"><button class="tab active">All <span style="background:#eef2f7;padding:2px 8px;border-radius:50px;margin-left:4px;">23</span></button>';
    previewHtml += '<button class="tab">Started <span style="background:#eef2f7;padding:2px 8px;border-radius:50px;margin-left:4px;">8</span></button>';
    previewHtml += '<button class="tab">Mine <span style="background:#eef2f7;padding:2px 8px;border-radius:50px;margin-left:4px;">5</span></button></div>';
  }

  // Toolbar
  if (content.includes('outline') || content.includes('Filter') || content.includes('Export')) {
    previewHtml += '<div class="toolbar">';
    previewHtml += '<button class="btn btn-outline">Filter ▼</button>';
    previewHtml += '<button class="btn btn-outline">Columns ▼</button>';
    previewHtml += '<button class="btn btn-outline" style="margin-left:auto;">Export ▼</button>';
    previewHtml += '</div>';
  }

  // Table
  if (cols.length > 0 || comps.includes('DataGrid')) {
    const headers = cols.length > 0 ? cols : ['Reg.no', 'Insp.#', 'Type', 'Make/model', 'Insp.date', 'Status'];
    previewHtml += '<table class="bus-table"><thead><tr>';
    headers.forEach(h => { previewHtml += '<th>' + h + '</th>'; });
    previewHtml += '</tr></thead><tbody>';
    for (let i = 0; i < 5; i++) {
      previewHtml += '<tr>';
      headers.forEach((h, j) => {
        const sample = rows[i] ? (rows[i][h] || rows[i][h.replace(/\s/g,'')] || '—') : (j === 0 ? 'AB' + (12345 + i) : '—');
        previewHtml += '<td>' + sample + '</td>';
      });
      previewHtml += '</tr>';
    }
    previewHtml += '</tbody></table>';
  }

  previewHtml += '</div>'; // main

  // Footer
  if (content.includes('footer') || content.includes('Footer')) {
    previewHtml += '<div class="app-footer">BUS Design System · Generated Preview</div>';
  }

  previewHtml += '</div></body></html>';

  iframe.srcdoc = previewHtml;
}


function renderReview(review) {
  const container = document.getElementById('review-content');
  if (!review) {
    container.innerHTML = '<p>No review data available.</p>';
    return;
  }

  const outcomeClass = review.outcome === 'Passed' ? 'badge-success' :
    review.outcome === 'PassedWithWarnings' ? 'badge-warning' : 'badge-error';

  let html = `
    <div style="display:flex;align-items:center;gap:24px;margin-bottom:24px;">
      <div>
        <div class="review-score" style="color:${review.score >= 80 ? 'var(--green-500)' : review.score >= 60 ? 'var(--orange-400)' : 'var(--red-600)'}">${review.score}</div>
        <div style="font-size:13px;color:var(--gray-500);">Review Score</div>
      </div>
      <div>
        <span class="badge ${outcomeClass}">${review.outcome || 'Unknown'}</span>
      </div>
    </div>
  `;

  if (review.violations && review.violations.length > 0) {
    html += '<h3 style="margin-bottom:8px;">Violations</h3>';
    review.violations.forEach(v => {
      const sevClass = v.severity === 'Blocking' ? 'badge-error' : 'badge-warning';
      html += `<div class="finding-row">
        <span class="badge ${sevClass}">${v.severity}</span>
        <span class="finding-rule">${v.ruleId || v.category}</span>
        <span class="finding-msg">${v.message}</span>
      </div>`;
    });
  } else {
    html += '<p style="color:var(--green-500);font-weight:600;">✅ No violations found.</p>';
  }

  if (review.suggestions && review.suggestions.length > 0) {
    html += '<h3 style="margin:16px 0 8px;">Suggestions</h3><ul style="padding-left:20px;">';
    review.suggestions.forEach(s => { html += `<li style="font-size:13px;color:var(--gray-700);margin-bottom:4px;">${s}</li>`; });
    html += '</ul>';
  }

  container.innerHTML = html;
}

function renderAnalysis(artifacts, review) {
  const container = document.getElementById('analysis-content');
  let html = '<h3 style="margin-bottom:12px;">Generated Artifacts</h3>';
  html += '<div style="display:grid;grid-template-columns:1fr 1fr;gap:12px;margin-bottom:24px;">';
  artifacts.forEach(a => {
    html += `<div style="background:white;border:1px solid var(--gray-200);border-radius:8px;padding:12px;">
      <div style="font-weight:600;font-size:13px;">📁 ${a.path}</div>
      <div style="font-size:12px;color:var(--gray-500);">${a.content.length} characters</div>
    </div>`;
  });
  html += '</div>';

  if (review) {
    html += `<h3 style="margin-bottom:12px;">Summary</h3>
    <table style="width:100%;border-collapse:collapse;">
      <tr><td style="padding:8px;border-bottom:1px solid var(--gray-100);font-weight:600;">Files Generated</td><td style="padding:8px;border-bottom:1px solid var(--gray-100);">${artifacts.length}</td></tr>
      <tr><td style="padding:8px;border-bottom:1px solid var(--gray-100);font-weight:600;">Review Score</td><td style="padding:8px;border-bottom:1px solid var(--gray-100);">${review.score}/100</td></tr>
      <tr><td style="padding:8px;border-bottom:1px solid var(--gray-100);font-weight:600;">Outcome</td><td style="padding:8px;border-bottom:1px solid var(--gray-100);">${review.outcome}</td></tr>
      <tr><td style="padding:8px;border-bottom:1px solid var(--gray-100);font-weight:600;">Violations</td><td style="padding:8px;border-bottom:1px solid var(--gray-100);">${(review.violations || []).length}</td></tr>
      <tr><td style="padding:8px;font-weight:600;">Suggestions</td><td style="padding:8px;">${(review.suggestions || []).length}</td></tr>
    </table>`;
  }

  container.innerHTML = html;
}

function escapeHtml(text) {
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}
