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

  let code = mainFile.content
    .replace(/import\s+.*from\s+['"].*['"];?\s*/g, '')
    .replace(/:\s*React\.FC[^=]*/g, '')
    .replace(/\bexport default\b/g, '')
    .replace(/const\s+ActiveInspections/g, 'function ActiveInspections');

  const html = `<!DOCTYPE html>
<html>
<head><meta charset="UTF-8">
<script src="https://unpkg.com/react@18/umd/react.production.min.js"><\/script>
<script src="https://unpkg.com/react-dom@18/umd/react-dom.production.min.js"><\/script>
<script src="https://unpkg.com/@babel/standalone/babel.min.js"><\/script>
<style>
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body { font-family: 'Inter','Segoe UI',sans-serif; background: #f9fafb; color: #111928; font-size: 14px; padding: 24px; }
  .bus-btn { display: inline-flex; align-items: center; gap: 6px; padding: 10px 16px; border: none; border-radius: 8px; font-size: 14px; font-weight: 600; cursor: pointer; white-space: nowrap; }
  .bus-btn-primary { background: #1548be; color: white; }
  .bus-btn-primary:hover { background: #1e429f; }
  .bus-btn-outline-secondary { background: white; color: #1f2a37; box-shadow: inset 0 0 0 1px #9ca3af; border-radius: 8px; }
  .bus-tab-strip { display: flex; gap: 4px; border-bottom: 2px solid #e5e7eb; margin-bottom: 16px; }
  .bus-tab { padding: 8px 16px; font-size: 13px; font-weight: 600; color: #6b7280; border: none; border-bottom: 2px solid transparent; background: none; cursor: pointer; }
  .bus-tab.active { color: #1548be; border-bottom-color: #1548be; }
  .bus-table { width: 100%; border-collapse: collapse; background: white; border: 1px solid #e5e7eb; border-radius: 8px; }
  .bus-table th { background: #f4f6f9; padding: 12px 8px; font-size: 14px; font-weight: 600; color: #6b7280; text-align: left; border-bottom: 1px solid #e5e7eb; }
  .bus-table td { padding: 16px 8px; font-size: 12px; border-bottom: 1px solid #e5e7eb; }
  .app-shell { display: grid; grid-template-columns: 240px 1fr; grid-template-rows: 56px 1fr; min-height: 100vh; }
  .toolbar { display: flex; justify-content: space-between; margin-bottom: 12px; gap: 8px; }
</style></head>
<body>
  <div id="root"><div style="text-align:center;padding:48px;color:#6b7280;">⏳ Loading preview...</div></div>
  <script type="text/babel">
    var e = React.createElement;
    var BUSButton = function(p) {
      var cls = p.variant === 'outlineSecondary' ? 'bus-btn bus-btn-outline-secondary' : 'bus-btn bus-btn-primary';
      return e('button', { className: cls }, p.children || p.label || '');
    };
    var BUSTabStrip = function(p) {
      var items = p.tabs ? p.tabs.split(',') : ['Tab 1', 'Tab 2'];
      return e('div', { className: 'bus-tab-strip' },
        items.map(function(t, i) { return e('button', { key: i, className: 'bus-tab' + (i === (p.activeIndex||0) ? ' active' : '') }, t.trim()); })
      );
    };
    var DataGrid = function(p) {
      var cols = p.columns ? p.columns.split(',') : ['Col 1', 'Col 2'];
      return e('div', {},
        e('table', { className: 'bus-table' },
          e('thead', null, e('tr', null, cols.map(function(c, i) { return e('th', { key: i }, c.trim()); }))),
          e('tbody', null, e('tr', null, cols.map(function(c, i) { return e('td', { key: i }, '—'); })))
        )
      );
    };
    var BUSInput = function(p) {
      return e('input', { placeholder: p.placeholder || '', style: { padding: '8px 12px', border: '1px solid #d1d5db', borderRadius: '8px', fontSize: '13px', width: '200px' } });
    };
    var BUSFormField = function(p) { return e('div', {}, p.children); };
    var BUSCheckbox = function(p) { return e('label', {}, e('input', { type: 'checkbox' }), p.label); };
    var BUSSwitch = function(p) { return e('input', { type: 'checkbox', defaultChecked: p.checked }); };
    var AppLayout = function(p) { return e('div', {}, p.children); };

    ${code}

    function App() {
      var Comp = ActiveInspections || Generated;
      if (!Comp) return e('div', { style: { padding: '24px', color: '#c81e1e' } }, 'No component found in generated code.');
      return e(Comp);
    }

    var root = document.getElementById('root');
    try {
      ReactDOM.createRoot(root).render(e(App));
    } catch(ex) {
      root.innerHTML = '<div style=\"padding:24px;color:#c81e1e;\">Preview render error: ' + ex.message + '<br><pre style=\"font-size:11px;margin-top:8px;color:#374151;\">' + decodeURIComponent(\"%3Ccode%3E\") + mainFile.content.replace(/</g, '&lt;').replace(/>/g, '&gt;').substring(0, 500) + '</pre></div>';
    }
  <\/script>
</body></html>`;

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
