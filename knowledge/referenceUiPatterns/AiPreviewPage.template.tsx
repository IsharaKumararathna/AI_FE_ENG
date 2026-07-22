// @aife-version: 7
import React, { useState, useEffect, useMemo } from 'react';

// ── Auto-discover all AI preview pages ──
// Each sub-folder under _AiPreview/ is a preview page. This require.context
// runs at build time and picks up every index.tsx automatically — no route
// wiring needed when a new component is added via save_ai_preview.
const previewModules = require.context('./', true, /\/index\.tsx$/);

interface PreviewEntry {
  slug: string;
  Component: React.ComponentType<any>;
}

function buildPreviewEntries(): PreviewEntry[] {
  return previewModules
    .keys()
    .map((key: string) => {
      // key looks like './customer-register/index.tsx'
      const slug = key.replace(/^\.\//, '').replace(/\/index\.tsx$/, '');
      const module = previewModules(key);
      const Component = module.default || module;
      return { slug, Component };
    })
    .filter((entry) => entry.slug !== ''); // exclude the top-level index.tsx itself
}

// ── Types ──
interface MetaData {
  files: string[];
  sources: Record<string, string>;
  analysis: {
    layout: string;
    elements: Array<{
      kind: string;
      text?: string;
      bounds?: string;
      matchedComponentId?: string;
      confidence?: number;
    }>;
  } | null;
  review: {
    score: number;
    outcome: string;
    violations?: Array<{
      ruleId: string;
      category?: string;
      severity?: string;
      message: string;
      location?: string;
    }>;
    suggestions?: string[];
  } | null;
  generatedAt: string;
}

// ── Tab names ──
type TabId = 'preview' | 'review' | 'code' | 'analysis';

interface Tab {
  id: TabId;
  label: string;
  icon: string;
}

const TABS: Tab[] = [
  { id: 'preview', label: 'Live Preview', icon: '👁️' },
  { id: 'review', label: 'Review Report', icon: '✅' },
  { id: 'code', label: 'React Code', icon: '📄' },
  { id: 'analysis', label: 'Analysis', icon: '🔍' },
];

// ── Helpers ──
async function loadMeta(slug: string): Promise<MetaData | null> {
  try {
    const meta = await import(`./${slug}/meta.json`);
    return meta.default || meta;
  } catch {
    return null;
  }
}

function escapeHtml(text: string): string {
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}

// ── Sub-components ──

const LivePreviewTab: React.FC<{ entry: PreviewEntry }> = ({ entry }) => {
  const { slug, Component } = entry;
  // The key on this wrapper div forces React to unmount and remount the
  // inner <Component /> when the slug changes. Without this, React may
  // reuse the DOM subtree even when the Component reference changes,
  // which means the preview always shows the first-loaded component.
  return (
    <div key={slug} style={{ padding: '16px', background: '#fff', borderRadius: '8px', border: '1px solid #e5e7eb' }}>
      <Component />
    </div>
  );
};

const ReviewReportTab: React.FC<{ meta: MetaData | null }> = ({ meta }) => {
  if (!meta?.review) {
    return (
      <div style={{ padding: '24px', textAlign: 'center', color: '#6b7280' }}>
        <p>No review report available for this preview.</p>
        <p style={{ fontSize: '13px', marginTop: '8px' }}>
          The AI reviewer stage may not have been run, or its results were not saved.
        </p>
      </div>
    );
  }

  const { score, outcome, violations, suggestions } = meta.review;
  const scoreColor = score >= 80 ? '#057a55' : score >= 60 ? '#ff5a1f' : '#c81e1e';
  const outcomeClass =
    outcome === 'Passed'
      ? { bg: '#f3faf7', color: '#057a55' }
      : outcome === 'PassedWithWarnings'
        ? { bg: '#fff8f1', color: '#ff5a1f' }
        : { bg: '#fdf2f2', color: '#c81e1e' };

  return (
    <div style={{ padding: '16px' }}>
      {/* Score */}
      <div style={{ display: 'flex', alignItems: 'center', gap: '24px', marginBottom: '24px' }}>
        <div>
          <div style={{ fontSize: '48px', fontWeight: 700, color: scoreColor }}>{score}</div>
          <div style={{ fontSize: '13px', color: '#6b7280' }}>Review Score</div>
        </div>
        <div>
          <span
            style={{
              padding: '4px 12px',
              borderRadius: '12px',
              fontSize: '12px',
              fontWeight: 600,
              background: outcomeClass.bg,
              color: outcomeClass.color,
            }}
          >
            {outcome || 'Unknown'}
          </span>
        </div>
      </div>

      {/* Violations */}
      {violations && violations.length > 0 ? (
        <>
          <h3 style={{ marginBottom: '8px', fontSize: '15px', fontWeight: 700, color: '#1f2a37' }}>
            Violations ({violations.length})
          </h3>
          {violations.map((v, i) => {
            const sevBg = v.severity === 'Blocking' ? '#fdf2f2' : '#fff8f1';
            const sevColor = v.severity === 'Blocking' ? '#c81e1e' : '#ff5a1f';
            const sevLabel = v.severity || 'Warning';
            return (
              <div
                key={i}
                style={{
                  display: 'flex',
                  gap: '12px',
                  padding: '8px 0',
                  borderBottom: '1px solid #f3f4f6',
                }}
              >
                <span
                  style={{
                    padding: '2px 8px',
                    borderRadius: '4px',
                    fontSize: '11px',
                    fontWeight: 600,
                    background: sevBg,
                    color: sevColor,
                    flexShrink: 0,
                  }}
                >
                  {sevLabel}
                </span>
                <span style={{ fontWeight: 600, fontSize: '12px', minWidth: '160px', color: '#6b7280' }}>
                  {v.ruleId || v.category || '—'}
                </span>
                <span style={{ fontSize: '13px', color: '#1f2a37' }}>{v.message}</span>
              </div>
            );
          })}
        </>
      ) : (
        <p style={{ color: '#057a55', fontWeight: 600, fontSize: '14px' }}>✅ No violations found.</p>
      )}

      {/* Suggestions */}
      {suggestions && suggestions.length > 0 && (
        <>
          <h3 style={{ margin: '16px 0 8px', fontSize: '15px', fontWeight: 700, color: '#1f2a37' }}>
            Suggestions
          </h3>
          <ul style={{ paddingLeft: '20px' }}>
            {suggestions.map((s, i) => (
              <li key={i} style={{ fontSize: '13px', color: '#4b5563', marginBottom: '4px' }}>
                {s}
              </li>
            ))}
          </ul>
        </>
      )}
    </div>
  );
};

const ReactCodeTab: React.FC<{ meta: MetaData | null }> = ({ meta }) => {
  if (!meta?.sources || Object.keys(meta.sources).length === 0) {
    return (
      <div style={{ padding: '24px', textAlign: 'center', color: '#6b7280' }}>
        <p>No source code available for this preview.</p>
      </div>
    );
  }

  const fileNames = Object.keys(meta.sources).filter((f) => f !== 'meta.json');

  if (fileNames.length === 0) {
    return (
      <div style={{ padding: '24px', textAlign: 'center', color: '#6b7280' }}>
        <p>No source files found.</p>
      </div>
    );
  }

  return (
    <div style={{ padding: '16px' }}>
      {fileNames.map((fileName) => (
        <div key={fileName} style={{ marginBottom: '16px' }}>
          <div
            style={{
              fontSize: '13px',
              fontWeight: 600,
              color: '#4b5563',
              marginBottom: '4px',
              padding: '8px 12px',
              background: '#f3f4f6',
              borderRadius: '8px 8px 0 0',
              fontFamily: 'Consolas, monospace',
            }}
          >
            📁 {fileName}
          </div>
          <pre
            style={{
              background: '#1e1e1e',
              color: '#d4d4d4',
              padding: '16px',
              margin: 0,
              borderRadius: '0 0 8px 8px',
              fontFamily: '"Consolas", "Fira Code", monospace',
              fontSize: '13px',
              lineHeight: 1.5,
              overflowX: 'auto',
              whiteSpace: 'pre-wrap',
              wordBreak: 'break-word',
            }}
          >
            {meta.sources[fileName]}
          </pre>
        </div>
      ))}
    </div>
  );
};

const AnalysisTab: React.FC<{ meta: MetaData | null }> = ({ meta }) => {
  if (!meta?.analysis) {
    return (
      <div style={{ padding: '24px', textAlign: 'center', color: '#6b7280' }}>
        <p>No analysis data available for this preview.</p>
        <p style={{ fontSize: '13px', marginTop: '8px' }}>
          The prototype analyzer stage may not have been run, or its results were not saved.
        </p>
      </div>
    );
  }

  const { layout, elements } = meta.analysis;

  return (
    <div style={{ padding: '16px' }}>
      {/* Layout */}
      <div style={{ marginBottom: '20px' }}>
        <h3 style={{ fontSize: '15px', fontWeight: 700, color: '#1f2a37', marginBottom: '8px' }}>
          Detected Layout
        </h3>
        <div
          style={{
            display: 'inline-block',
            padding: '6px 14px',
            background: '#ebf5ff',
            color: '#1548be',
            borderRadius: '20px',
            fontSize: '14px',
            fontWeight: 600,
          }}
        >
          {layout || 'Unknown'}
        </div>
      </div>

      {/* Elements */}
      <h3 style={{ fontSize: '15px', fontWeight: 700, color: '#1f2a37', marginBottom: '8px' }}>
        Detected Elements ({elements?.length || 0})
      </h3>

      {elements && elements.length > 0 ? (
        <div style={{ display: 'grid', gridTemplateColumns: '1fr 1fr', gap: '12px' }}>
          {elements.map((el, i) => (
            <div
              key={i}
              style={{
                background: '#fff',
                border: '1px solid #e5e7eb',
                borderRadius: '8px',
                padding: '12px',
              }}
            >
              <div style={{ fontWeight: 600, fontSize: '13px', color: '#1f2a37', marginBottom: '4px' }}>
                {el.kind}
                {el.text && (
                  <span style={{ fontWeight: 400, color: '#6b7280', marginLeft: '8px' }}>
                    &quot;{el.text}&quot;
                  </span>
                )}
              </div>
              {el.matchedComponentId && (
                <div style={{ fontSize: '12px', color: '#057a55', marginBottom: '2px' }}>
                  ✅ Matched: <strong>{el.matchedComponentId}</strong>
                  {el.confidence !== undefined && (
                    <span style={{ color: '#6b7280', marginLeft: '6px' }}>
                      ({(el.confidence * 100).toFixed(0)}%)
                    </span>
                  )}
                </div>
              )}
              {!el.matchedComponentId && (
                <div style={{ fontSize: '12px', color: '#c81e1e' }}>⚠️ No match found</div>
              )}
              {el.bounds && (
                <div style={{ fontSize: '11px', color: '#9ca3af', marginTop: '4px' }}>{el.bounds}</div>
              )}
            </div>
          ))}
        </div>
      ) : (
        <p style={{ fontSize: '13px', color: '#6b7280' }}>No elements detected.</p>
      )}

      {/* Summary */}
      <div style={{ marginTop: '24px' }}>
        <h3 style={{ fontSize: '15px', fontWeight: 700, color: '#1f2a37', marginBottom: '8px' }}>
          Summary
        </h3>
        <table
          style={{
            width: '100%',
            borderCollapse: 'collapse',
            fontSize: '13px',
          }}
        >
          <tbody>
            <tr>
              <td style={{ padding: '8px', borderBottom: '1px solid #f3f4f6', fontWeight: 600, color: '#6b7280' }}>
                Generated At
              </td>
              <td style={{ padding: '8px', borderBottom: '1px solid #f3f4f6' }}>
                {meta.generatedAt ? new Date(meta.generatedAt).toLocaleString() : '—'}
              </td>
            </tr>
            <tr>
              <td style={{ padding: '8px', borderBottom: '1px solid #f3f4f6', fontWeight: 600, color: '#6b7280' }}>
                Source Files
              </td>
              <td style={{ padding: '8px', borderBottom: '1px solid #f3f4f6' }}>
                {meta.files?.length || 0}
              </td>
            </tr>
            <tr>
              <td style={{ padding: '8px', fontWeight: 600, color: '#6b7280' }}>Review Score</td>
              <td style={{ padding: '8px' }}>{meta.review?.score ?? '—'}/100</td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  );
};

// ── Main AiPreviewPage ──

const AiPreviewPage: React.FC = () => {
  const entries = buildPreviewEntries();

  const [activeTab, setActiveTab] = useState<TabId>('preview');
  const [meta, setMeta] = useState<MetaData | null>(null);
  const [metaLoading, setMetaLoading] = useState(false);

  // Simple index-based selection. No string comparison, no useMemo, no
  // complex state initialization races. Just pick entry by index.
  const [selectedIndex, setSelectedIndex] = useState(0);

  const selectedEntry = entries[selectedIndex] || null;

  // Trigger the dropdown to track selectedIndex changes.
  // This ref + onChange pattern is an uncontrolled <select> — simpler,
  // avoids all React controlled-input reconciliation quirks.
  const selectRef = React.useRef<HTMLSelectElement>(null);

  // Load meta.json whenever selectedIndex changes
  useEffect(() => {
    const slug = selectedEntry?.slug;
    if (!slug) {
      setMeta(null);
      return;
    }
    setMetaLoading(true);
    loadMeta(slug).then((m) => {
      setMeta(m);
      setMetaLoading(false);
    });
  }, [selectedIndex]); // eslint-disable-line react-hooks/exhaustive-deps

  // Reset tab to 'preview' when switching previews
  useEffect(() => {
    setActiveTab('preview');
  }, [selectedIndex]);

  // ── No previews available ──
  if (entries.length === 0) {
    return (
      <div style={{ padding: '48px', textAlign: 'center' }}>
        <div style={{ fontSize: '48px', marginBottom: '16px' }}>📋</div>
        <h2 style={{ fontSize: '20px', fontWeight: 700, color: '#1f2a37', marginBottom: '8px' }}>
          No AI Previews Yet
        </h2>
        <p style={{ color: '#6b7280', fontSize: '14px', maxWidth: '480px', margin: '0 auto' }}>
          Generated components will appear here automatically. Use the AI Frontend Generator to create
          your first preview — no manual route wiring needed.
        </p>
      </div>
    );
  }

  return (
    <div style={{ padding: '24px' }}>
      {/* Slug selector — only show if multiple previews exist */}
      {entries.length > 1 && (
        <div style={{ marginBottom: '16px', display: 'flex', alignItems: 'center', gap: '12px' }}>
          <span style={{ fontSize: '14px', fontWeight: 600, color: '#1f2a37' }}>Preview:</span>
          <select
            ref={selectRef}
            defaultValue={entries[0]?.slug ?? ''}
            onChange={() => {
              const idx = selectRef.current?.selectedIndex ?? 0;
              setSelectedIndex(idx);
            }}
            style={{
              padding: '8px 12px',
              border: '1px solid #d7dce5',
              borderRadius: '8px',
              fontSize: '14px',
              background: '#fff',
              minWidth: '200px',
            }}
          >
            {entries.map((e) => (
              <option key={e.slug} value={e.slug}>
                {e.slug}
              </option>
            ))}
          </select>
        </div>
      )}

      {/* 4-tab layout — plain HTML/CSS, no Design System dependency */}
      <div style={{ display: 'flex', gap: '1px', marginBottom: '0', borderBottom: '2px solid #d7dce5' }}>
        {TABS.map((t) => (
          <button
            key={t.id}
            onClick={() => setActiveTab(t.id)}
            style={{
              padding: '10px 20px',
              background: activeTab === t.id ? '#fff' : '#f3f4f6',
              color: activeTab === t.id ? '#1a56db' : '#6b7280',
              border: activeTab === t.id ? '2px solid #1a56db' : '2px solid transparent',
              borderBottom: activeTab === t.id ? '2px solid #fff' : '2px solid transparent',
              borderRadius: '8px 8px 0 0',
              fontWeight: activeTab === t.id ? 600 : 400,
              fontSize: '14px',
              cursor: 'pointer',
              marginBottom: '-2px',
              position: 'relative' as const,
            }}
          >
            {t.icon} {t.label}
          </button>
        ))}
      </div>

      {/* Tab content */}
      <div style={{ marginTop: '16px' }}>
        {activeTab === 'preview' && selectedEntry && <LivePreviewTab entry={selectedEntry} />}

        {activeTab === 'review' && (
          <>
            {metaLoading ? (
              <div style={{ padding: '24px', textAlign: 'center', color: '#6b7280' }}>
                Loading review data...
              </div>
            ) : (
              <ReviewReportTab meta={meta} />
            )}
          </>
        )}

        {activeTab === 'code' && (
          <>
            {metaLoading ? (
              <div style={{ padding: '24px', textAlign: 'center', color: '#6b7280' }}>
                Loading source code...
              </div>
            ) : (
              <ReactCodeTab meta={meta} />
            )}
          </>
        )}

        {activeTab === 'analysis' && (
          <>
            {metaLoading ? (
              <div style={{ padding: '24px', textAlign: 'center', color: '#6b7280' }}>
                Loading analysis data...
              </div>
            ) : (
              <AnalysisTab meta={meta} />
            )}
          </>
        )}
      </div>
    </div>
  );
};

export default AiPreviewPage;
