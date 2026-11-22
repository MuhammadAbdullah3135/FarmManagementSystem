import { describe, expect, it } from 'vitest';
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { MemoryRouter, useNavigate } from 'react-router-dom';
import RouteAnnouncer from './RouteAnnouncer';

/*
 * Two signals per navigation, and both are asserted here: the document title (the tab, and what
 * a screen reader says when it lands on the page) and the polite live region (what it says when
 * the router swaps the view without a load). A route the menu names and one it does not are both
 * covered, because the second kind is the one that quietly falls back to the parent's name.
 */
const GoTo = ({ to }: { to: string }) => {
  const navigate = useNavigate();
  return <button onClick={() => navigate(to)}>go</button>;
};

const renderAt = (entry: string) =>
  render(
    <MemoryRouter initialEntries={[entry]}>
      <RouteAnnouncer />
      <GoTo to="/dashboard/animals" />
    </MemoryRouter>,
  );

describe('RouteAnnouncer', () => {
  it('titles the arrival without announcing a navigation that did not happen', () => {
    renderAt('/dashboard/animals');

    expect(document.title).toBe('Animal List · Farm Management System');
    // The region exists on arrival — that is what lets a later change be announced — but is
    // empty: the load itself is the browser's announcement, not ours.
    const region = screen.getByRole('status');
    expect(region).toHaveAttribute('aria-live', 'polite');
    expect(region).toBeEmptyDOMElement();
  });

  it('retitles and announces each route the user moves to', async () => {
    const user = userEvent.setup();
    renderAt('/dashboard/breeding/records');

    expect(document.title).toBe('Breeding Records · Farm Management System');

    await user.click(screen.getByRole('button', { name: 'go' }));
    expect(document.title).toBe('Animal List · Farm Management System');
    expect(screen.getByRole('status')).toHaveTextContent('Animal List');
  });

  it('names a screen that has no menu entry instead of leaving the parent name', () => {
    renderAt('/dashboard/sync');

    // "Offline & sync" is a page heading with no `nav:` key, so this route carries its own.
    expect(document.title).toBe('Sync Status · Farm Management System');
  });

  it('names a single animal by its section, as its own breadcrumb does', () => {
    renderAt('/dashboard/animals/TAG-0079');

    expect(document.title).toBe('Animals · Farm Management System');
  });

  it('prefers a static screen over the animal matcher', () => {
    // Both patterns describe this path; the specific one has to win, as it does in the router.
    renderAt('/dashboard/animals/import');

    expect(document.title).toBe('Import animals · Farm Management System');
  });

  it('resolves a deployed path, which still carries the base path', () => {
    // In a deployed build the router leaves the base on `location.pathname`; a title that only
    // matched `/dashboard/...` would fall back to the app name on every page.
    renderAt(`${import.meta.env.BASE_URL.replace(/\/+$/, '')}/dashboard/animals`);

    expect(document.title).toBe('Animal List · Farm Management System');
  });

  it('falls back to the nearest section, then to the app name', () => {
    // A deeper path with no entry of its own still belongs to a section.
    renderAt('/dashboard/what-even-is-this');
    expect(document.title).toBe('Dashboard · Farm Management System');

    // Outside the dashboard the title is still a name, never empty: a blank tab is worse than
    // the app's own.
    renderAt('/somewhere-else');
    expect(document.title).toBe('Farm Management System');
  });
});
