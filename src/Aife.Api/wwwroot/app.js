// BUSpek AI Frontend Generator — Dashboard
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
const btnTreeDemo = document.getElementById('btn-tree-demo');

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

// ── Quick demo: direct tree generation ──
btnTreeDemo.addEventListener('click', async () => {
  showLoading('Generating Active Inspections view...');
  try {
    // Upload a simple prototype
    const demoHtml = '<html><body><button>New control</button><table><tr><th>Reg.no</th></tr></table></body></html>';
    const uploadRes = await fetch(`${API}/prototypes`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ html: demoHtml, css: '' })
    });
    const proto = await uploadRes.json();
    currentPrototypeId = proto.id;

    const sessionRes = await fetch(`${API}/sessions`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ prototypeId: proto.id })
    });
    const session = await sessionRes.json();
    currentSessionId = session.sessionId;

    const [artifactsRes, reviewRes] = await Promise.all([
      fetch(`${API}/sessions/${currentSessionId}/artifacts`),
      fetch(`${API}/sessions/${currentSessionId}/review`)
    ]);
    const artifacts = await artifactsRes.json();
    const review = await reviewRes.json();

    showResults(artifacts, review, null);
  } catch (err) {
    showError(err.message);
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

  // Transform the TSX: remove imports, replace with mock component definitions
  let code = mainFile.content
    // Remove import lines
    .replace(/import\s+.*from\s+['"].*['"];?\s*/g, '')
    // Remove TypeScript types
    .replace(/:\s*React\.FC[^=]*/g, '')
    .replace(/:\s*string/g, '')
    .replace(/:\s*boolean/g, '')
    .replace(/:\s*number/g, '')
    .replace(/<[^>]+>/g, m => m.replace(/\s+as\s+[^>]+/g, ''));

  const html = `<!DOCTYPE html>
<html>
<head>
  <meta charset="UTF-8">
  <script crossorigin src="https://unpkg.com/react@18/umd/react.production.min.js"><\/script>
  <script crossorigin src="https://unpkg.com/react-dom@18/umd/react-dom.production.min.js"><\/script>
  <script src="https://unpkg.com/@babel/standalone/babel.min.js"><\/script>
  <style>
    * { box-sizing: border-box; margin: 0; padding: 0; }
    body { font-family: 'Segoe UI', sans-serif; background: #f9fafb; color: #111928; font-size: 14px; }
    .bus-btn { display: inline-flex; align-items: center; gap: 6px; padding: 10px 16px; border: none; border-radius: 8px; font-size: 14px; font-weight: 600; cursor: pointer; }
    .bus-btn-primary { background: #1548be; color: white; }
    .bus-btn-primary:hover { background: #1e429f; }
    .bus-btn-outline-secondary { background: white; color: #1f2a37; box-shadow: inset 0 0 0 1px #9ca3af; border-radius: 8px; }
    .bus-tab-strip { display: flex; gap: 4px; border-bottom: 2px solid #e5e7eb; }
    .bus-tab { padding: 8px 16px; font-size: 13px; font-weight: 600; color: #6b7280; border: none; border-bottom: 2px solid transparent; background: none; cursor: pointer; }
    .bus-tab.active { color: #1548be; border-bottom-color: #1548be; }
    .bus-table { width: 100%; border-collapse: collapse; background: white; border: 1px solid #e5e7eb; border-radius: 8px; overflow: hidden; }
    .bus-table th { background: #f4f6f9; padding: 12px 8px; font-size: 14px; font-weight: 600; color: #6b7280; text-align: left; }
    .bus-table td { padding: 16px 8px; font-size: 12px; border-bottom: 1px solid #e5e7eb; }
    .app-shell { display: grid; grid-template-columns: 240px 1fr; grid-template-rows: 56px 1fr; min-height: 100vh; }
    .topbar { grid-column: 1/-1; display: flex; align-items: center; gap: 12px; padding: 0 16px; background: white; border-bottom: 1px solid #e5e7eb; }
    .sidebar { background: white; border-right: 1px solid #e5e7eb; padding: 12px 0; }
    .sidebar a { display: block; padding: 10px 16px; color: #374151; text-decoration: none; font-size: 13px; }
    .sidebar a.active { background: #ebf5ff; color: #1548be; font-weight: 600; }
    .main-content { padding: 24px; overflow-y: auto; }
    .page-title { font-size: 20px; font-weight: 700; margin-bottom: 16px; }
    .toolbar { display: flex; justify-content: space-between; margin-bottom: 12px; gap: 8px; }
    .data-grid-container { overflow-x: auto; margin-bottom: 16px; }
    .pagination { display: flex; align-items: center; gap: 12px; padding: 8px 0; }
  </style>
</head>
<body>
  <div id="root"></div>
  <script type="text/babel" data-presets="react">
    // Mock BUSKvalitet Design System components
    const BUSButton = ({ children, variant, ...props }) => {
      const cls = variant === 'outlineSecondary' ? 'bus-btn bus-btn-outline-secondary' : 'bus-btn bus-btn-primary';
      return React.createElement('button', { className: cls, ...props }, children);
    };
    const BUSTabStrip = ({ tabs, activeIndex }) => {
      const items = typeof tabs === 'string' ? tabs.split(',') : (tabs || ['Tab 1', 'Tab 2']);
      return React.createElement('div', { className: 'bus-tab-strip' },
        items.map((t, i) => React.createElement('button', { key: i, className: 'bus-tab' + (i === (activeIndex||0) ? ' active' : '') }, t))
      );
    };
    const DataGrid = ({ columns, sortable }) => {
      const cols = typeof columns === 'string' ? columns.split(',') : (columns || ['Col 1', 'Col 2']);
      return React.createElement('div', { className: 'data-grid-container' },
        React.createElement('table', { className: 'bus-table' },
          React.createElement('thead', null,
            React.createElement('tr', null, cols.map((c, i) => React.createElement('th', { key: i }, c)))
          ),
          React.createElement('tbody', null,
            React.createElement('tr', null, cols.map((c, i) => React.createElement('td', { key: i }, '—')))
          )
        )
      );
    };
    const BUSInput = ({ placeholder, ...props }) =>
      React.createElement('input', { placeholder: placeholder || 'Enter text...', style: { padding: '8px 12px', border: '1px solid #d1d5db', borderRadius: '8px', fontSize: '13px' }, ...props });
    const BUSCheckbox = ({ label, ...props }) =>
      React.createElement('label', { style: { display: 'flex', gap: '8px', alignItems: 'center', fontSize: '13px' } },
        React.createElement('input', { type: 'checkbox', ...props }), label);
    const BUSSwitch = ({ checked, ...props }) =>
      React.createElement('input', { type: 'checkbox', defaultChecked: checked, ...props });
    const AppLayout = ({ children }) => children;

    try {
      ${code}
      const root = ReactDOM.createRoot(document.getElementById('root'));
      root.render(React.createElement(ActiveInspections || Generated));
    } catch(e) {
      document.getElementById('root').innerHTML = '<div style="padding:24px;color:#c81e1e;">Preview error: ' + e.message + '<br><br>Generated code:<br><pre style="font-size:11px;color:#374151;">' + ${JSON.stringify(mainFile.content)}.replace(/</g, '&lt;') + '</pre></div>';
    }
  <\/script>
</body>
</html>`;

  iframe.srcdoc = html;
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
