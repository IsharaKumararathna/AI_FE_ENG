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

    // Handle different response formats — API may return array or { artifacts: [...] }
    const artifactList = Array.isArray(artifacts) ? artifacts : (artifacts.artifacts || [artifacts]);
    const reviewData = review || { score: 0, outcome: 'Unknown', violations: [], suggestions: [] };

    // 4. Try conformance
    let conformance = null;
    try {
      const confRes = await fetch(`${API}/prototypes/${currentPrototypeId}/conformance`);
      conformance = await confRes.json();
    } catch (e) { /* conformance is optional */ }

    showResults(artifactList, reviewData, conformance);
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

// ── Knowledge Base Training ──
const trainFolder = document.getElementById('train-folder');
const trainGitUrl = document.getElementById('train-git-url');
const trainGitBranch = document.getElementById('train-git-branch');
const btnTrain = document.getElementById('btn-train');
const trainResult = document.getElementById('train-result');

function checkTrainReady() {
  btnTrain.disabled = !trainFolder.value.trim() && !trainGitUrl.value.trim();
}
trainFolder.addEventListener('input', checkTrainReady);
trainGitUrl.addEventListener('input', checkTrainReady);

btnTrain.addEventListener('click', async () => {
  const body = {};
  if (trainFolder.value.trim()) body.folderPath = trainFolder.value.trim();
  if (trainGitUrl.value.trim()) body.gitUrl = trainGitUrl.value.trim();
  if (trainGitBranch.value.trim()) body.gitBranch = trainGitBranch.value.trim();

  // Get selected mode from radio buttons
  const modeRadio = document.querySelector('input[name="train-mode"]:checked');
  body.mode = modeRadio ? modeRadio.value : 'update';

  trainResult.style.display = 'block';
  trainResult.innerHTML = '<div class="loader"><div class="spinner"></div><p>Training knowledge base...</p></div>';
  btnTrain.disabled = true;

  try {
    const res = await fetch(`${API}/knowledge/train`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(body)
    });
    const data = await res.json();
    if (res.ok) {
      trainResult.innerHTML = `<div style="padding:12px;background:#f3faf7;border:1px solid #bcf0da;border-radius:8px;">
        <strong style="color:#057a55;">✅ Training Complete</strong><br>
        Tokens extracted: <strong>${data.tokensExtracted}</strong><br>
        Components extracted: <strong>${data.componentsExtracted}</strong><br>
        ${data.warnings?.length ? '<br><strong>Warnings:</strong><br>' + data.warnings.join('<br>') : ''}
        <br><small style="color:#6b7280;">KB path: ${data.knowledgeBasePath || 'N/A'}</small>
      </div>`;
    } else {
      trainResult.innerHTML = `<div style="padding:12px;background:#fdf2f2;border:1px solid #fbd5d5;border-radius:8px;color:#c81e1e;">
        <strong>❌ Training Failed</strong><br>${data.detail || data.title || 'Unknown error'}
      </div>`;
    }
  } catch (err) {
    trainResult.innerHTML = `<div style="padding:12px;background:#fdf2f2;border:1px solid #fbd5d5;border-radius:8px;color:#c81e1e;">
      <strong>❌ Error</strong><br>${err.message}
    </div>`;
  } finally {
    btnTrain.disabled = false;
  }
});

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
  if (!artifacts || !Array.isArray(artifacts) || artifacts.length === 0) {
    container.innerHTML = '<p style="color:var(--gray-500);">No artifacts generated.</p>';
    return;
  }
  artifacts.filter(a => a && a.path).forEach(a => {
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
  if (!artifacts || !Array.isArray(artifacts) || artifacts.length === 0) {
    iframe.srcdoc = '<div style="padding:48px;text-align:center;color:#6b7280;">No preview available.</div>';
    return;
  }
  const mainFile = artifacts.find(a => a && a.path && a.path.endsWith('.tsx')) || artifacts.find(a => a && a.path) || artifacts[0];
  if (!mainFile || !mainFile.content) {
    iframe.srcdoc = '<div style="padding:48px;text-align:center;color:#c81e1e;">No preview content available.</div>';
    return;
  }

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

  // Build clean preview HTML matching the prototype structure
  let previewHtml = '<!DOCTYPE html><html><head><meta charset="UTF-8"><style>';
  previewHtml += `
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body { font-family: 'Inter','Segoe UI',sans-serif; background: #f6f8fb; color: #1e293b; font-size: 14px; }
    .layout { display: flex; height: 100vh; }
    .sidebar { width: 280px; background: #fff; border-right: 1px solid #e5e7eb; flex-shrink: 0; }
    .logo { display: flex; align-items: center; gap: 15px; padding: 16px 18px; border-bottom: 1px solid #eee; }
    .menu-btn { width: 38px; height: 38px; border: 1px solid #ddd; background: #fff; border-radius: 8px; cursor: pointer; display: flex; align-items: center; justify-content: center; font-size: 16px; }
    .brand { display: flex; align-items: center; gap: 10px; font-size: 22px; color: #1155cc; font-weight: 700; }
    nav { padding: 15px 0; }
    nav a { display: flex; align-items: center; gap: 14px; padding: 15px 20px; color: #334155; text-decoration: none; cursor: pointer; font-size: 14px; }
    nav a.active { background: #e8f2ff; color: #1155cc; font-weight: 600; }
    main { flex: 1; display: flex; flex-direction: column; min-width: 0; }
    header.topbar { background: #fff; height: 66px; border-bottom: 1px solid #e5e7eb; display: flex; justify-content: space-between; align-items: center; padding: 0 20px; flex-shrink: 0; }
    .search { width: 330px; background: #fff; border: 1px solid #d9dee7; border-radius: 10px; display: flex; align-items: center; padding: 0 14px; }
    .search input { width: 100%; border: none; height: 40px; outline: none; font-size: 14px; }
    .user { display: flex; align-items: center; gap: 12px; }
    .avatar { width: 36px; height: 36px; background: #dbeafe; border-radius: 50%; display: flex; align-items: center; justify-content: center; color: #1155cc; font-weight: 700; font-size: 14px; }
    .content { padding: 30px; overflow: auto; flex: 1; }
    .page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 30px; }
    h1 { font-size: 38px; color: #111928; font-weight: 700; }
    .primary-btn { background: #0d5bd7; color: #fff; border: none; border-radius: 10px; padding: 14px 22px; font-size: 15px; font-weight: 600; cursor: pointer; display: inline-flex; align-items: center; gap: 6px; }
    .primary-btn:hover { background: #1548be; }
    .tabs { display: flex; gap: 25px; margin-bottom: 25px; border-bottom: 1px solid #e4e7ed; }
    .tab-btn { padding: 14px 2px; border: none; background: none; font-size: 16px; color: #64748b; cursor: pointer; display: flex; align-items: center; gap: 6px; }
    .tab-btn.active { color: #1155cc; border-bottom: 3px solid #1155cc; font-weight: 600; margin-bottom: -1px; }
    .tab-btn span { background: #eef2f7; padding: 3px 9px; border-radius: 50px; font-size: 13px; font-weight: 500; }
    .toolbar { display: flex; justify-content: space-between; margin-bottom: 18px; }
    .toolbar-left { display: flex; gap: 12px; }
    .outline-btn { background: #fff; border: 1px solid #d7dce5; padding: 11px 16px; border-radius: 10px; cursor: pointer; font-size: 14px; display: flex; align-items: center; gap: 6px; color: #374151; }
    .outline-btn:hover { background: #f9fafb; }
    .chips { display: flex; align-items: center; gap: 10px; margin-bottom: 18px; flex-wrap: wrap; }
    .chip { background: #eef6ff; color: #0d5bd7; border: 1px solid #bcd8ff; padding: 8px 14px; border-radius: 100px; font-size: 13px; display: flex; align-items: center; gap: 6px; }
    .chips a { color: #0d5bd7; text-decoration: none; font-weight: 600; font-size: 13px; }
    .table-wrapper { background: #fff; border: 1px solid #e5e7eb; border-radius: 12px; overflow: hidden; }
    table { width: 100%; border-collapse: collapse; }
    thead { background: #f8fafc; }
    th { text-align: left; padding: 16px; font-size: 14px; color: #64748b; font-weight: 600; white-space: nowrap; }
    td { padding: 18px 16px; border-top: 1px solid #edf0f4; font-size: 14px; }
    .status-badge { display: inline-block; padding: 6px 12px; border-radius: 20px; font-size: 13px; font-weight: 600; }
    .status-started { background: #e7f0ff; color: #0d5bd7; }
    .status-progress { background: #fff8e1; color: #b45309; }
    .status-completed { background: #e6f7ed; color: #057a55; }
  `;
  previewHtml += '</style></head><body><div class="layout">';

  // Sidebar
  previewHtml += '<aside class="sidebar">';
  previewHtml += '<div class="logo"><div class="menu-btn">☰</div><div class="brand">📋 BUSpek</div></div>';
  previewHtml += '<nav>';
  previewHtml += '<a class="active">📋 Active inspections</a>';
  previewHtml += '<a>📄 Control register</a>';
  previewHtml += '<a>🔔 Follow-up</a>';
  previewHtml += '<a>👤 Customer register</a>';
  previewHtml += '<a>⚙️ Settings</a>';
  previewHtml += '</nav></aside>';

  // Main area
  previewHtml += '<main>';

  // Header - search on LEFT, user on RIGHT
  previewHtml += '<header class="topbar">';
  previewHtml += '<div class="search">🔍 <input type="text" placeholder="Search reg. no. or VIN..."></div>';
  previewHtml += '<div class="user"><div class="avatar">TS</div><span>Tomas Setsä</span> ▼</div>';
  previewHtml += '</header>';

  // Content
  previewHtml += '<section class="content">';

  // Page header
  const title = content.includes('Active inspections') ? 'Active inspections' : compName;
  previewHtml += '<div class="page-header"><h1>' + title + '</h1>';
  if (comps.includes('BUSButton')) previewHtml += '<button class="primary-btn">+ New control</button>';
  previewHtml += '</div>';

  // Tabs
  if (comps.includes('BUSTabStrip')) {
    previewHtml += '<div class="tabs">';
    previewHtml += '<button class="tab-btn active">All <span>23</span></button>';
    previewHtml += '<button class="tab-btn">Started <span>8</span></button>';
    previewHtml += '<button class="tab-btn">Mine <span>5</span></button>';
    previewHtml += '<button class="tab-btn">+</button>';
    previewHtml += '</div>';
  }

  // Toolbar
  if (content.includes('outline') || content.includes('Filter') || content.includes('Export') || content.includes('Columns')) {
    previewHtml += '<div class="toolbar">';
    previewHtml += '<div class="toolbar-left">';
    previewHtml += '<button class="outline-btn">Filter ▼</button>';
    previewHtml += '<button class="outline-btn">Columns ▼</button>';
    previewHtml += '</div>';
    previewHtml += '<button class="outline-btn">Export ▼</button>';
    previewHtml += '</div>';
  }

  // Chips / filters
  if (content.includes('chip') || content.includes('chips') || content.includes('filter') || content.includes('Filter')) {
    previewHtml += '<div class="chips">';
    previewHtml += '<span class="chip">Type: PKK ✕</span>';
    previewHtml += '<span class="chip">Status: Started ✕</span>';
    previewHtml += '<a href="#">Remove all filters</a>';
    previewHtml += '</div>';
  }

  // Table - use the ACTUAL column count from prototype (10 columns)
  const allColumns = ['Reg.no', 'Insp.#', 'Type', 'Make / model', 'Insp.date', 'Remaining', 'Sev', 'Inspector', 'Status', ''];
  const headers = cols.length > 0 ? cols : allColumns;
  previewHtml += '<div class="table-wrapper"><table><thead><tr>';
  headers.forEach(h => { previewHtml += '<th>' + h + '</th>'; });
  previewHtml += '</tr></thead><tbody>';

  const sampleData = [
    ['AB12345', 'PKK-2026-001234', 'PKK', 'Volvo FH', '2026-07-10', '2 days', 'L', 'TS', 'Started', '⋯'],
    ['CD67890', 'PKK-2026-001235', 'PKK', 'Scania R450', '2026-07-11', '5 days', 'M', 'AH', 'In progress', '⋯'],
    ['EF11223', 'PKK-2026-001236', 'EU', 'Mercedes Actros', '2026-07-09', 'Done', '', 'TS', 'Completed', '⋯'],
    ['GH44556', 'PKK-2026-001237', 'PKK', 'MAN TGX', '2026-07-15', '7 days', '', 'KJ', 'Started', '⋯'],
    ['IJ77889', 'PKK-2026-001238', 'EU', 'DAF XF', '2026-07-12', '1 day', 'H', 'TS', 'In progress', '⋯'],
  ];

  for (let i = 0; i < 5; i++) {
    previewHtml += '<tr>';
    const row = sampleData[i];
    headers.forEach((h, j) => {
      let val = row[j] || '—';
      // Style status column
      if (h === 'Status') {
        const cls = val === 'Completed' ? 'status-completed' : val === 'In progress' ? 'status-progress' : 'status-started';
        val = '<span class="status-badge ' + cls + '">' + val + '</span>';
      }
      previewHtml += '<td>' + val + '</td>';
    });
    previewHtml += '</tr>';
  }
  previewHtml += '</tbody></table></div>';

  previewHtml += '</section>'; // content
  previewHtml += '</main>'; // main
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
