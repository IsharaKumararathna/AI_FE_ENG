---
id: token-usage
title: Design Token Usage
tags: [tokens, color, spacing, conformance, react]
---

# Design Token Usage

All visual values in generated React MUST come from the BUS Design System token
set (`src/DesignSystem/tokens/*.ts`, mirrored as SCSS vars in
`styles/_tokens.scss`). Hardcoding raw values is the #1 conformance failure.

## Color

Use the SCSS variable (inside `.scss`) or the TS token import (inside `.tsx`).
Never write a hex/rgb literal in component CSS.

```scss
// GOOD
background: $bus-ds-color-primary;
color: $bus-ds-text-body;
border: 1px solid $bus-ds-border-default;

// BAD — hardcoded, will fail check_token_conformance
background: #1548be;
color: #6b7280;
```

| Token | Value | Use for |
|-------|-------|---------|
| `$bus-ds-color-primary` | #1548be | Primary actions, active nav |
| `$bus-ds-color-primary-hover` | #1e429f | Primary button hover |
| `$bus-ds-surface` | #ffffff | Card / panel backgrounds |
| `$bus-ds-surface-muted` | #f3f4f6 | Muted backgrounds, table stripes |
| `$bus-ds-surface-app-bg` | #f9fafb | App background |
| `$bus-ds-text-heading` | #374151 | Headings |
| `$bus-ds-text-body` | #6b7280 | Body text |
| `$bus-ds-text-muted` | #1f2a37 | Secondary text |
| `$bus-ds-border-default` | #d1d5db | Default borders |
| `$bus-ds-color-error` | #f98080 | Error / destructive |
| `$bus-ds-color-success` | #31c48d | Success states |
| `$bus-ds-focus-ring` | #76a9fa | Focus rings |

## Spacing, Radius, Typography

```scss
border-radius: $bus-ds-radius-button;   // 8px — buttons
border-radius: $bus-ds-radius-base;     // 4px — inputs
font-family: $bus-ds-font-family;       // 'Inter', sans-serif
font-size: $bus-ds-font-size-body;      // 14px
font-weight: $bus-ds-font-weight-medium;// 500
```

## Component token contracts

Each component's KB entry lists its `tokensConsumed` (e.g. BUSButton consumes
`colors.focus.ring`, `radiusTokens.radius.button`, `typography.textRole.buttonText.fontSize`).
When generating a component override, only use tokens from that list or a
semantically equivalent one — do not invent new colors.

## Common violations to avoid

- `#fff` / `#ffffff` → use `$bus-ds-surface` or `$bus-ds-color-on-primary`.
- `#eee` / `#ddd` / `#e5e7eb` → use `$bus-ds-border-default` or `$bus-ds-surface-muted`.
- `#1e293b` / `#334155` (slate) → use `$bus-ds-text-muted` / `$bus-ds-text-heading`.
- `#1155cc` (legacy link blue) → use `$bus-ds-color-primary`.
