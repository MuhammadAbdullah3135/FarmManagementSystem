import { afterEach, describe, expect, it, vi } from 'vitest';
import { isPageChangeClick, returnToTop } from './paginationScroll';

/**
 * The two pieces the layout's listener is built from, tested on their own terms.
 *
 * The wiring itself — a click inside the scroll box, arriving at the listener — is asserted in
 * `components/AppLayout.test.tsx`, which renders a real `Pagination` inside the layout. Here the
 * question is only what counts as a page change, and what happens to a box that is already at the
 * top.
 */

/** A detached host holding markup, so the controls can be queried and clicked as they would be. */
const host = (html: string): HTMLElement => {
  const element = document.createElement('div');
  element.innerHTML = html;
  return element;
};

describe('isPageChangeClick', () => {
  it('is true for a numbered page that is not the current one', () => {
    const element = host('<li class="ant-pagination-item ant-pagination-item-2"><a>2</a></li>');

    expect(isPageChangeClick(element.querySelector('li'))).toBe(true);
    // A click lands on whatever is inside the control — the `a` here, an icon in an arrow — so
    // the control is found by walking up from the target.
    expect(isPageChangeClick(element.querySelector('a'))).toBe(true);
  });

  it('is true for the arrows and the five-page hops', () => {
    for (const className of [
      'ant-pagination-prev',
      'ant-pagination-next',
      'ant-pagination-jump-prev',
      'ant-pagination-jump-next',
    ]) {
      const element = host(`<li class="${className}"><button type="button"></button></li>`);
      expect(isPageChangeClick(element.querySelector('button'))).toBe(true);
    }
  });

  it('is false for the page you are already on, which pages nothing', () => {
    const element = host('<li class="ant-pagination-item ant-pagination-item-1 ant-pagination-item-active"><a>1</a></li>');

    expect(isPageChangeClick(element.querySelector('a'))).toBe(false);
  });

  it('is false for a disabled arrow, which pages nothing', () => {
    const element = host(
      '<li class="ant-pagination-prev ant-pagination-disabled"><button type="button" disabled></button></li>',
    );

    expect(isPageChangeClick(element.querySelector('li'))).toBe(false);
    expect(isPageChangeClick(element.querySelector('button'))).toBe(false);
  });

  it('is false for anything else a page can be clicked on', () => {
    const element = host('<button type="button">Refresh</button><th class="ant-table-cell">Tag</th>');

    // Sorting a column, refreshing a list, opening a row: none of these is a page change.
    expect(isPageChangeClick(element.querySelector('button'))).toBe(false);
    expect(isPageChangeClick(element.querySelector('th'))).toBe(false);
    expect(isPageChangeClick(null)).toBe(false);
    // A DOM event's target is not always an element (a text node, for one).
    expect(isPageChangeClick(document.createTextNode('2'))).toBe(false);
  });
});

describe('returnToTop', () => {
  /** A box whose `scrollTop` reads as given — jsdom has no layout to scroll for real. */
  const boxAt = (scrollTop: number): HTMLElement => {
    const box = document.createElement('div');
    Object.defineProperty(box, 'scrollTop', { value: scrollTop, writable: true, configurable: true });
    return box;
  };

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('scrolls a box that is part-way down the page', () => {
    const scrollTo = vi.spyOn(HTMLElement.prototype, 'scrollTo').mockImplementation(() => undefined);

    expect(returnToTop(boxAt(400))).toBe(true);
    expect(scrollTo).toHaveBeenCalledWith({ top: 0 });
  });

  it('leaves a box that is already at the top alone', () => {
    const scrollTo = vi.spyOn(HTMLElement.prototype, 'scrollTo').mockImplementation(() => undefined);

    expect(returnToTop(boxAt(0))).toBe(false);
    expect(scrollTo).not.toHaveBeenCalled();
  });

  it('does nothing at all without a box', () => {
    const scrollTo = vi.spyOn(HTMLElement.prototype, 'scrollTo').mockImplementation(() => undefined);

    expect(returnToTop(null)).toBe(false);
    expect(scrollTo).not.toHaveBeenCalled();
  });
});
