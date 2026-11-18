import type { ComponentProps } from 'react';
import { Select as AntSelect } from 'antd';

/**
 * Every select in the app comes from here so its open list is fully exposed to assistive
 * tech.
 *
 * antd virtualises the dropdown by default, and a virtualised popup keeps the `listbox`
 * role on a zero-size mirror holding only the active option and its two neighbours. Arrow
 * keys slide that window, but nothing ever puts the rest of the options in the
 * accessibility tree, so a screen reader cannot enumerate the list. Turning virtualisation
 * off puts `listbox` on the real list and gives every option its own `option` role.
 *
 * Every list in the app is bounded by the API's 100-row lookup page — animals, inventory
 * items, customers, suppliers and employees are the largest — and rendering 100 options in
 * full costs about 15ms more than the three-option window (production build: 24ms against
 * 9ms to open, with the painted frame unchanged at ~48ms either way). The cost only turns
 * into a dragging dropdown in the hundreds: 500 options take ~107ms and 2000 take ~375ms,
 * which no lookup page can return.
 *
 * `virtual` is spread last, so a caller with a genuinely huge list can opt back in, trading
 * the full accessibility tree for the smaller one.
 */
const Select = ((props: ComponentProps<typeof AntSelect>) => (
  <AntSelect virtual={false} {...props} />
)) as typeof AntSelect;

export default Select;
