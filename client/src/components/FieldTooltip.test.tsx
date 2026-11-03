import { describe, expect, it, vi } from 'vitest';
import { fireEvent, render, waitFor } from '@testing-library/react';
import FieldTooltip from './FieldTooltip';

/** The element `aria-describedby` currently points at, or null while closed. */
const described = (trigger: HTMLElement): HTMLElement | null => {
  const id = trigger.getAttribute('aria-describedby');
  return id ? document.getElementById(id) : null;
};

describe('FieldTooltip', () => {
  it('gives a keyboard-focusable trigger whose aria-describedby resolves to the open tooltip', async () => {
    const { container } = render(
      <FieldTooltip titleKey="tooltip:member.revokeEffect">
        <button type="button">Remove</button>
      </FieldTooltip>,
    );
    const trigger = container.firstElementChild as HTMLElement;

    // The span is the focus target: neither the wrapper's children nor antd add one.
    expect(trigger.tagName).toBe('SPAN');
    expect(trigger.tabIndex).toBe(0);
    // Closed: there is nothing to describe yet, so no dangling id.
    expect(trigger.getAttribute('aria-describedby')).toBeNull();

    fireEvent.focus(trigger);
    await waitFor(() => expect(trigger.getAttribute('aria-describedby')).not.toBeNull());

    // The described element is the popup itself, carrying the resolved copy.
    expect(described(trigger)?.textContent).toContain('Removes their access immediately');
  });

  it('resolves interpolation through t() rather than concatenating strings', async () => {
    const { container } = render(
      <FieldTooltip titleKey="tooltip:sync.pendingCount" values={{ count: 3 }}>
        <span>3</span>
      </FieldTooltip>,
    );
    const trigger = container.firstElementChild as HTMLElement;

    fireEvent.focus(trigger);
    await waitFor(() => expect(trigger.getAttribute('aria-describedby')).not.toBeNull());

    // A bare English key with {{count}}: i18next substitutes without plural forms,
    // the same shape `health:intervalDays` ships in every locale today.
    expect(described(trigger)?.textContent).toContain('3 changes waiting to upload');
  });

  it('is dismissed by Escape, using antd’s own handler rather than a custom one', async () => {
    const { container } = render(
      <FieldTooltip titleKey="tooltip:member.revokeEffect">
        <span>Remove</span>
      </FieldTooltip>,
    );
    const trigger = container.firstElementChild as HTMLElement;

    fireEvent.focus(trigger);
    await waitFor(() => expect(trigger.getAttribute('aria-describedby')).not.toBeNull());

    fireEvent.keyDown(window, { key: 'Escape' });
    await waitFor(() => expect(trigger.getAttribute('aria-describedby')).toBeNull());
    expect(described(trigger)).toBeNull();
  });

  it('wraps a disabled control in the span, since a disabled button fires no events', () => {
    const { container } = render(
      <FieldTooltip titleKey="tooltip:member.revokeEffect">
        <button type="button" disabled>
          Remove
        </button>
      </FieldTooltip>,
    );
    const trigger = container.firstElementChild as HTMLElement;

    expect(trigger.tagName).toBe('SPAN');
    expect(trigger.tabIndex).toBe(0);
    expect(trigger.querySelector('button[disabled]')).not.toBeNull();
  });

  it('warns in development when the resolved copy exceeds 120 characters', () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    try {
      render(
        <FieldTooltip titleKey="tooltip:sync.pendingCount" values={{ count: 'x'.repeat(200) }}>
          <span>over the limit</span>
        </FieldTooltip>,
      );
      expect(warn).toHaveBeenCalledWith(expect.stringContaining('tooltip:sync.pendingCount'));
      expect(warn).toHaveBeenCalledWith(expect.stringContaining('limit 120'));
    } finally {
      warn.mockRestore();
    }
  });
});
