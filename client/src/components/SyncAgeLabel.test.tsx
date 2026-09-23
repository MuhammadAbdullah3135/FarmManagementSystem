import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import SyncAgeLabel from './SyncAgeLabel';

describe('SyncAgeLabel', () => {
  it('states how old the cached rows are', () => {
    const twoDaysAgo = new Date(Date.now() - 2 * 24 * 60 * 60 * 1000).toISOString();

    render(<SyncAgeLabel lastSyncedAt={twoDaysAgo} />);

    expect(screen.getByText('Synced 2 days ago')).toBeInTheDocument();
  });

  it('says nothing at all when the view has no synced data to describe', () => {
    // A filtered or paginated view is deliberately never cached, so a label would be
    // describing nothing — and a wrong "synced" claim is worse than no label.
    const { container } = render(<SyncAgeLabel lastSyncedAt={null} />);

    expect(container).toBeEmptyDOMElement();
  });
});
