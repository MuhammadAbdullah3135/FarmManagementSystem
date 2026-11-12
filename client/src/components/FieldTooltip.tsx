import React from 'react';
import { Tooltip } from 'antd';
import { useTranslation } from 'react-i18next';

/**
 * The style guide's hard limit. Past this a tooltip is not a tooltip: it is inline
 * help text, because a long hover-only string is out of reach for touch and keyboard
 * readers in the first place. `scripts/check-tooltips.mjs` enforces the same number
 * on the locale files; this guard catches a value that only gets long once `t()`
 * interpolates it.
 */
const MAX_CHARS = 120;

/**
 * 300 ms before a hover shows the tooltip — long enough that a pointer crossing the
 * screen never flashes one, short enough to feel immediate. Focus opens at once: a
 * keyboard user asked for it deliberately, and antd exposes no focus delay of its own.
 */
const SHOW_DELAY_SECONDS = 0.3;

export interface FieldTooltipProps {
  /**
   * A purpose-named key in the `tooltip` namespace, written with the namespace
   * prefix this app already uses (`tooltip:sync.pendingCount`). The name says what
   * the tooltip is for, never what it says — the wording lives only in the locale
   * files and can change without touching this call site.
   */
  titleKey: string;
  /** Interpolation values handed straight to `t()`, e.g. `{ count: 3 }` — never concatenated. */
  values?: Record<string, string | number>;
  /**
   * One element: an icon, a badge, status text, a disabled control. The audit's
   * "needs inline help text instead" cases (QR purpose, sire/dam form fields, the
   * birth tag preview, confirm-pregnancy) never belong here at all.
   */
  children: React.ReactNode;
  className?: string;
}

/**
 * Ant Design's Tooltip, keyed by purpose instead of by copy.
 *
 * Behaviour it leans on rather than reimplements, verified against the installed
 * antd 6.6.1 / @rc-component/tooltip 1.5 / @rc-component/portal:
 *
 *   - Escape dismisses: the portal listens on `window` for the key and closes the
 *     topmost popup, so there is no custom key handling here to drift out of sync.
 *   - `aria-describedby`: antd places it on the trigger while open, pointing at the
 *     popup's own id — the wiring this component exists to make reachable.
 *   - `autoAdjustOverflow` (antd's default, stated for clarity) flips `placement`
 *     when the viewport would clip the tooltip.
 *
 * The child is always wrapped in a focusable span. An icon or a badge cannot take
 * focus by itself, and a disabled button takes neither focus nor mouse events — the
 * span is what the keyboard and the pointer actually land on, in both cases. The
 * trade-off is one extra tab stop if someone wraps an already-enabled control;
 * Phase 1 attaches to no controls at all, and the audit's candidates are icons,
 * badges, status text and disabled buttons.
 */
const FieldTooltip: React.FC<FieldTooltipProps> = ({ titleKey, values, children, className }) => {
  const { t } = useTranslation();
  const text = t(titleKey, values);

  if (import.meta.env?.DEV && text.length > MAX_CHARS) {
    console.warn(
      `[FieldTooltip] ${titleKey} resolved to ${text.length} characters (limit ${MAX_CHARS}). ` +
        'A string this long belongs in inline help text, not a tooltip.',
    );
  }

  return (
    <Tooltip
      title={text}
      placement="top"
      mouseEnterDelay={SHOW_DELAY_SECONDS}
      autoAdjustOverflow
      trigger={['hover', 'focus']}
    >
      <span className={className} tabIndex={0}>
        {children}
      </span>
    </Tooltip>
  );
};

export default FieldTooltip;
