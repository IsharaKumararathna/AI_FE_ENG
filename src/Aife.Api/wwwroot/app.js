// AI Frontend Generator — Dashboard
const API = window.location.origin + '/api/v1';

let currentSessionId = null;
let currentPrototypeId = null;

// ── Panel toggles (collapsible detail sections) ──
document.querySelectorAll('.panel-toggle').forEach(btn => {
  btn.addEventListener('click', () => {
    btn.classList.toggle('active');
    document.getElementById(btn.dataset.panel).classList.toggle('active');
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

  const content = mainFile.content;

  // Extract meaningful info from the generated TSX
  const nameMatch = content.match(/(?:const|function)\s+([A-Z]\w*)/);
  const compName = nameMatch ? nameMatch[1] : 'Generated';

  const imports = (content.match(/import\s+\{([^}]+)\}\s+from/g) || [''])[0] || '';
  const comps = imports.replace(/import\s*\{|\}\s*from.*/g, '').trim();
  const compList = comps.split(',').map(c => c.trim()).filter(Boolean);

  // Extract column headers if DataGrid is used
  const colMatch = content.match(/columns\s*=\s*\[([^\]]+)\]/);
  const cols = colMatch ? colMatch[1].replace(/["']/g, '').split(',').map(c => c.trim()) : [];

  // Extract row data if present (from generated code)
  const rowMatch = content.match(/rows\s*=\s*(\[[\s\S]*?\])\s*\]/);
  let dataRows = [];
  if (rowMatch) {
    try {
      // Try to parse as JSON-like
      const rowStr = rowMatch[1].replace(/'/g, '"');
      if (rowStr.startsWith('[') && rowStr.endsWith(']')) {
        dataRows = JSON.parse(rowStr);
      }
    } catch { /* use sample data fallback */ }
  }

  // Determine page structure from the generated content
  const hasLayout = content.includes('<AppLayout');
  const hasSidebar = hasLayout || content.includes('sidebar');
  const hasTabs = compList.includes('BUSTabStrip');
  const hasTable = compList.includes('DataGrid');
  const hasButtons = compList.includes('BUSButton');
  const hasLabels = compList.includes('BUSLabel');
  const hasExpansion = compList.includes('BUSExpansionPanel');
  const hasCheckbox = compList.includes('BUSCheckbox');
  const hasSwitch = compList.includes('BUSSwitch');
  const hasInput = compList.includes('BUSInput') || compList.includes('BUSSearch') || compList.includes('BUSFilter');

  // Extract page title from the analysis or content
  const titleMatch = content.match(/(?:<h1|>\s*['"])([^<"']+)(?:<\/h1|['"])/);
  const pageTitle = titleMatch ? titleMatch[1].trim() : compName;

  // Build the preview HTML dynamically based on what was actually generated
  let previewHtml = '<!DOCTYPE html><html><head><meta charset="UTF-8"><style>';
  previewHtml += `
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body { font-family: 'Inter','Segoe UI',sans-serif; background: #f6f8fb; color: #1e293b; font-size: 14px; }
    .layout { display: flex; height: 100vh; }
    .sidebar { width: 260px; background: #fff; border-right: 1px solid #e5e7eb; flex-shrink: 0; overflow-y: auto; }
    .logo { display: flex; align-items: center; gap: 12px; padding: 16px; border-bottom: 1px solid #eee; }
    .menu-btn { width: 36px; height: 36px; border: 1px solid #ddd; background: #fff; border-radius: 8px; cursor: pointer; display: flex; align-items: center; justify-content: center; }
    .brand { font-size: 20px; color: #1155cc; font-weight: 700; }
    nav a { display: flex; align-items: center; gap: 12px; padding: 14px 18px; color: #334155; text-decoration: none; font-size: 14px; }
    nav a.active { background: #e8f2ff; color: #1155cc; font-weight: 600; }
    main { flex: 1; display: flex; flex-direction: column; min-width: 0; }
    header.topbar { background: #fff; height: 60px; border-bottom: 1px solid #e5e7eb; display: flex; justify-content: space-between; align-items: center; padding: 0 20px; flex-shrink: 0; }
    .search { width: 300px; background: #f9fafb; border: 1px solid #e5e7eb; border-radius: 8px; display: flex; align-items: center; padding: 0 12px; }
    .search input { width: 100%; border: none; height: 38px; outline: none; background: transparent; }
    .user { display: flex; align-items: center; gap: 10px; }
    .avatar { width: 34px; height: 34px; background: #dbeafe; border-radius: 50%; display: flex; align-items: center; justify-content: center; color: #1155cc; font-weight: 700; }
    .content { padding: 28px; overflow: auto; flex: 1; }
    .page-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: 24px; }
    h1 { font-size: 32px; color: #111928; font-weight: 700; }
    .primary-btn { background: #0d5bd7; color: #fff; border: none; border-radius: 8px; padding: 12px 20px; font-size: 14px; font-weight: 600; cursor: pointer; }
    .outline-btn { background: #fff; border: 1px solid #d7dce5; padding: 10px 14px; border-radius: 8px; cursor: pointer; font-size: 13px; display: inline-flex; align-items: center; gap: 6px; color: #374151; }
    .tabs { display: flex; gap: 20px; margin-bottom: 20px; border-bottom: 1px solid #e4e7ed; }
    .tab-btn { padding: 12px 2px; border: none; background: none; font-size: 15px; color: #64748b; cursor: pointer; }
    .tab-btn.active { color: #1155cc; border-bottom: 2px solid #1155cc; font-weight: 600; margin-bottom: -1px; }
    .tab-btn .badge-count { background: #eef2f7; padding: 2px 8px; border-radius: 50px; font-size: 12px; margin-left: 4px; }
    .table-wrapper { background: #fff; border: 1px solid #e5e7eb; border-radius: 10px; overflow: hidden; margin-top: 16px; }
    table { width: 100%; border-collapse: collapse; }
    thead { background: #f8fafc; }
    th { text-align: left; padding: 14px; font-size: 13px; color: #64748b; font-weight: 600; }
    td { padding: 16px 14px; border-top: 1px solid #f1f3f6; font-size: 14px; }
    .status-badge { display: inline-block; padding: 5px 10px; border-radius: 20px; font-size: 12px; font-weight: 600; }
    .status-started { background: #e7f0ff; color: #0d5bd7; }
    .status-progress { background: #fff8e1; color: #b45309; }
    .status-completed { background: #e6f7ed; color: #057a55; }
    .comp-label { display: block; padding: 8px 0; font-size: 13px; color: #6b7280; }
    .comp-card { background: #fff; border: 1px solid #e5e7eb; border-radius: 8px; padding: 14px; margin-bottom: 10px; }
    .comp-card h3 { font-size: 14px; margin-bottom: 4px; }
    .comp-card p { font-size: 13px; color: #6b7280; }
    .expansion-panel { background: #fff; border: 1px solid #e5e7eb; border-radius: 8px; padding: 12px 16px; margin-bottom: 8px; cursor: pointer; }
    .simple-page { max-width: 960px; margin: 0 auto; }
  `;
  previewHtml += '</style></head><body>';

  // Decide layout: sidebar layout or simple single-column
  if (hasSidebar) {
    previewHtml += '<div class="layout">';
    previewHtml += '<aside class="sidebar">';
    previewHtml += '<div class="logo"><div class="menu-btn">☰</div><div class="brand">📋 BUSpek</div></div>';
    previewHtml += '<nav>';
    if (hasTabs) previewHtml += '<a class="active">📋 ' + pageTitle + '</a>';
    else previewHtml += '<a class="active">📋 Overview</a>';
    previewHtml += '<a>📄 Control register</a>';
    previewHtml += '<a>🔔 Follow-up</a>';
    previewHtml += '<a>👤 Customers</a>';
    previewHtml += '<a>⚙️ Settings</a>';
    previewHtml += '</nav></aside>';
    previewHtml += '<main>';
    // Header
    previewHtml += '<header class="topbar"><div class="search">🔍 <input type="text" placeholder="Search..."></div><div class="user"><div class="avatar">U</div><span>User</span></div></header>';
  } else {
    // Simple page — no sidebar
    previewHtml += '<div class="simple-page">';
  }

  previewHtml += '<section class="content">';

  // Page header
  previewHtml += '<div class="page-header"><h1>' + pageTitle + '</h1>';
  if (hasButtons) previewHtml += '<button class="primary-btn">+ New</button>';
  previewHtml += '</div>';

  // Render components in order based on what was generated
  if (hasLabels) {
    previewHtml += '<div class="comp-label">📝 Text content region</div>';
    previewHtml += '<div class="comp-label" style="font-size:16px;font-weight:500;color:#1e293b;margin-bottom:12px;">' + pageTitle + '</div>';
  }

  if (hasTabs) {
    previewHtml += '<div class="tabs">';
    previewHtml += '<button class="tab-btn active">Tab 1 <span class="badge-count">5</span></button>';
    previewHtml += '<button class="tab-btn">Tab 2 <span class="badge-count">3</span></button>';
    previewHtml += '<button class="tab-btn">Tab 3 <span class="badge-count">8</span></button>';
    previewHtml += '</div>';
  }

  if (hasExpansion) {
    previewHtml += '<div class="expansion-panel"><strong>▶ Expandable section</strong></div>';
  }

  if (hasInput) {
    previewHtml += '<div style="margin-bottom:16px;">';
    previewHtml += '<input type="text" placeholder="' + (compList.includes('BUSSearch') ? 'Search...' : 'Enter text...') + '" style="width:100%;max-width:400px;padding:10px 14px;border:1px solid #d7dce5;border-radius:8px;font-size:14px;outline:none;">';
    previewHtml += '</div>';
  }

  if (hasCheckbox) {
    previewHtml += '<div style="margin-bottom:12px;display:flex;align-items:center;gap:8px;">';
    previewHtml += '<input type="checkbox" id="cb1"><label for="cb1" style="font-size:14px;">Checkbox option</label>';
    previewHtml += '</div>';
  }

  if (hasSwitch) {
    previewHtml += '<div style="margin-bottom:12px;display:flex;align-items:center;gap:8px;">';
    previewHtml += '<div style="width:44px;height:24px;background:#0d5bd7;border-radius:12px;position:relative;cursor:pointer;"><div style="width:20px;height:20px;background:#fff;border-radius:50%;position:absolute;top:2px;right:2px;"></div></div>';
    previewHtml += '<span style="font-size:14px;">Toggle switch</span>';
    previewHtml += '</div>';
  }

  if (hasButtons) {
    previewHtml += '<div style="margin:16px 0;display:flex;gap:10px;flex-wrap:wrap;">';
    if (compList.includes('BUSButton')) {
      previewHtml += '<button class="primary-btn">' + pageTitle + ' action</button>';
      previewHtml += '<button class="outline-btn">Cancel</button>';
    }
    previewHtml += '</div>';
  }

  if (hasTable) {
    const allColumns = ['Reg.no', 'Insp.#', 'Type', 'Make / model', 'Insp.date', 'Remaining', 'Sev', 'Inspector', 'Status', ''];
    const headers = cols.length >= 2 ? cols : allColumns;
    previewHtml += '<div class="table-wrapper"><table><thead><tr>';
    headers.forEach(h => { previewHtml += '<th>' + h + '</th>'; });
    previewHtml += '</tr></thead><tbody>';

    const sampleRows = dataRows.length > 0 ? dataRows : [
      { 'Column 1': 'Sample A', 'Column 2': 'Value 1', 'Column 3': 'Active' },
      { 'Column 1': 'Sample B', 'Column 2': 'Value 2', 'Column 3': 'Pending' },
      { 'Column 1': 'Sample C', 'Column 2': 'Value 3', 'Column 3': 'Done' },
    ];

    sampleRows.forEach(row => {
      previewHtml += '<tr>';
      headers.forEach(h => {
        let val = row[h] || '—';
        if (h === 'Status' || (typeof val === 'string' && (val === 'Active' || val === 'Pending' || val === 'Done' || val === 'Started' || val === 'Completed'))) {
          const cls = val === 'Completed' || val === 'Done' ? 'status-completed' : val === 'Active' ? 'status-started' : 'status-progress';
          val = '<span class="status-badge ' + cls + '">' + val + '</span>';
        }
        previewHtml += '<td>' + val + '</td>';
      });
      previewHtml += '</tr>';
    });
    previewHtml += '</tbody></table></div>';
  }

  // If nothing specific was generated, show a summary card
  if (!hasTable && !hasTabs && !hasButtons && !hasExpansion && !hasCheckbox && !hasSwitch && !hasInput) {
    previewHtml += '<div class="comp-card"><h3>' + pageTitle + '</h3><p>Generated component layout for this page.</p></div>';
    compList.forEach(c => {
      previewHtml += '<div class="comp-label">📦 Component: <strong>' + c + '</strong></div>';
    });
  }

  previewHtml += '</section>';
  previewHtml += hasSidebar ? '</main></div>' : '</div>';
  previewHtml += '</body></html>';

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
