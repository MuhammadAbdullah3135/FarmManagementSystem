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
 * The app's option lists are short — a farm's employees, its feed types, a handful of
 * statuses — so rendering them all costs nothing measurable.
 *
 * `virtual` is spread last, so a caller that really does have a huge list can still opt
 * back in.
 */
const Select = ((props: ComponentProps<typeof AntSelect>) => (
  <AntSelect virtual={false} {...props} />
)) as typeof AntSelect;

export default Select;
