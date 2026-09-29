/**
 * Returning the reader to the top of a page after its page number changes.
 *
 * The problem: the scroll box is the layout's `Content` (see `components/AppLayout.tsx`), and a
 * `Pagination` sits *below* the rows it pages, so clicking page 2 replaces the rows far above
 * the viewport and leaves the reader looking at the bottom of the new page — which reads as
 * "nothing happened".
 *
 * Why this is one listener on the scroll box rather than an `onChange` per screen: the
 * notification centre and the QR-label sheet page through antd's `Pagination`, and the rest of
 * the app pages through antd's `Table` pagination — some of it on the server
 * (`onChange: (p) => load(p)`) and some of it entirely in the browser, where antd owns the page
 * state and the page has no handler to hook at all. Every one of those controls is antd's, and
 * all of them are inside the scroll box, so a single delegated listener covers every paginated
 * screen at once — including the tables whose paging the page never sees.
 *
 * The trigger is deliberately narrow: a click that *changes the page*. Clicking the page you are
 * already on, typing in a filter, sorting a column, or refreshing a list does not scroll: none of
 * those is a page change, and jumping to the top for them is the same disorientation this exists
 * to remove. A control inside a modal is unaffected too — antd renders modals in a portal outside
 * the scroll box, so their clicks never reach this listener and their own scrolling is untouched.
 */

/**
 * antd's pagination controls that move to a different page.
 *
 * `.ant-pagination-item` is the numbered page itself and is filtered below: it is the one control
 * that can be a no-op. `-jump-prev`/`-jump-next` are the five-page hops that appear on long lists,
 * and `-options` (the page-size select and the quick jumper) is deliberately absent: its popup is
 * rendered in a portal outside the scroll box, so a click there cannot be seen from here.
 */
const PAGE_CHANGE_SELECTOR = [
  '.ant-pagination-prev',
  '.ant-pagination-next',
  '.ant-pagination-jump-prev',
  '.ant-pagination-jump-next',
  '.ant-pagination-item',
].join(',');

/**
 * True when this click moves to another page.
 *
 * `target` is whatever the click landed on, which may be a child of the control (the icon inside
 * prev/next, the `a` inside a numbered item), so the control is found by walking up.
 */
export const isPageChangeClick = (target: EventTarget | null): boolean => {
  if (!(target instanceof Element)) return false;

  const control = target.closest(PAGE_CHANGE_SELECTOR);
  if (control === null) return false;

  // A disabled arrow (‹ on the first page, › on the last) pages nothing, and neither does the
  // page you are already on — antd does not re-page for that one either. Neither moves the view.
  if (control.classList.contains('ant-pagination-disabled')) return false;
  if (control.querySelector('button:disabled') !== null) return false;

  return !control.classList.contains('ant-pagination-item-active');
};

/**
 * Puts a scroll box back at the top, and reports whether it actually moved.
 *
 * A box that is already at the top is left alone, so an incidental page change cannot jolt a view
 * that never left the top. Returns whether it scrolled: the tests assert on that, because jsdom
 * has no layout to observe the result in.
 */
export const returnToTop = (scroller: HTMLElement | null): boolean => {
  if (scroller === null || scroller.scrollTop === 0) return false;

  scroller.scrollTo({ top: 0 });
  return true;
};
