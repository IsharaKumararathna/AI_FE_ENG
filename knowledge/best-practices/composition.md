---
id: composition
title: Layout Composition
tags: [layout, composition, appshell, react]
---

# Layout Composition

Generated pages compose the four approved layout components. Never hand-roll a
header/sidebar shell with raw `<div>`s and flexbox — use the DesignSystem
layout primitives so navigation, responsive collapse, and focus trapping stay
consistent.

## AppShell — the standard application frame

```tsx
import { AppShell, Header, SideNav, PageHeader, Button } from '../DesignSystem';

<AppShell
  headerProps={{ title: 'Inspections', user: { name: 'Asha' } }}
  activeNavId="inspections"
  navItems={[{ id: 'inspections', label: 'Inspections' }, { id: 'reports', label: 'Reports' }]}
  onNavSelect={(id) => setActive(id)}
>
  <PageHeader title="Active Inspections" subtitle="3 open" actions={<Button variant="primary">New</Button>} />
  {/* page content here */}
</AppShell>
```

Slots: `header`, `sidebar`, `main`, `children`.

## Layout selection guide

| Page type | Layout | Composition |
|-----------|--------|-------------|
| Standard app page | `AppShell` | Header + SideNav + PageHeader + content |
| Detail / form page | `AppShell` + `PageHeader` | PageHeader with back action + form content |
| Dashboard | `AppShell` + grid of `InfoCard`s | PageHeader + responsive card grid |
| List + detail | `AppShell` + `DataTable` + side drawer | Master list with a detail panel |

## Rules

- `PageHeader` is the first child inside `AppShell` content for any page that
  has a title — it renders the title, subtitle, and `actions` slot.
- Do NOT nest an `AppShell` inside another `AppShell`. One shell per route.
- SideNav items come from data (`navItems` / `navGroups`), never hardcoded JSX.
- The main content area must not set its own max-width or centering —
  `AppShell` owns the responsive container.
