---
id: react-conventions
title: React & TypeScript Conventions
tags: [react, typescript, imports, conventions]
---

# React & TypeScript Conventions

## Imports

Import components exclusively from the DesignSystem public API barrel, never
from internal paths or third-party UI libs.

```tsx
// GOOD
import { Button, DataTable, AppShell } from '../DesignSystem';

// BAD — internal path, breaks on refactor
import { Button } from '../DesignSystem/components/Button/Button';

// BAD — forbidden third-party UI lib
import { Button } from '@progress/kendo-react-buttons';
import { Button } from 'react-bootstrap';
```

## Component authoring

- Functional components only. No class components.
- Props via a typed `interface XProps` (the KB `get_component` tool returns the
  exact prop names/types/enums — use them verbatim, do not invent props).
- Use the exact `variant`/`size` enum values the KB reports. For BUSButton the
  allowed `variant` values are `primary | secondary | subtle | ghost |
  destructive`; `size` is `xs | sm | md | lg`. Never write `variant="danger"`
  or `variant="warn"`.
- Spread inherited handlers (`onClick`, `disabled`, etc.) via the component's
  own props — do not wrap in an extra `<button>`.

## Styling

- Co-locate a `.scss` file per component (same name, same folder).
- Reference tokens via `$bus-ds-*` SCSS vars (see token-usage best practice).
- Use the component's BEM modifier classes (`bus-ds-btn--primary`,
  `bus-ds-btn--lg`) for variant styling — do not override internals from
  outside.

## State & data

- Lift page state to the page component; pass data down via props.
- For tables, feed `DataTable` a rows array + column config — do not build a
  `<table>` by hand.
- Forms use `Form` / `FormField` / `TextInput` / `SelectField`; do not mix raw
  `<input>` elements.
