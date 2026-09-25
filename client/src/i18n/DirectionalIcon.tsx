import { ArrowLeftOutlined, ArrowRightOutlined, LoginOutlined, LogoutOutlined } from '@ant-design/icons';
import type { CSSProperties } from 'react';
import { useTranslation } from 'react-i18next';
import { DEFAULT_LOCALE, directionOf, normalizeLocale, type Direction } from './locale';

/**
 * The icons whose *meaning* is a direction, rendered for the language's reading direction.
 *
 * This exists because antd mirrors its own components and flips nothing else. An arrow that
 * means "back" points left in a left-to-right language and right in a right-to-left one, and
 * `ConfigProvider direction="rtl"` does not touch it — a back button that still points left
 * in Arabic is pointing *forward*, which is worse than an untranslated word because it is
 * invisible to a translation review.
 *
 * Only the roles below are listed. Everything else stays a plain icon on purpose: a plus, a
 * bin, a floppy disk or a chart carries no direction, and mirroring those would be churn
 * that makes the real cases harder to spot. A generated check
 * (`tools/i18n/check-physical-directions.mjs`) looks for directional icon names used
 * outside this file, so a new "next" arrow fails the build instead of being forgotten.
 *
 * Two mechanisms, because there are two kinds of icon:
 *
 *   - `back` swaps one arrow for the other. A mirrored arrow is the same arrow, and antd
 *     ships both, so the glyph itself changes.
 *   - `enter` / `leave` keep their glyph, which is an arrow meeting a wall, and are flipped
 *     with `scaleX(-1)`. Which wall the arrow meets is the whole meaning, so the arrow has
 *     to move rather than be replaced by a different shape.
 */
export type DirectionalRole =
  /** Go back: points toward the start of a line, so it swaps side in a right-to-left language. */
  | 'back'
  /** Going in: "check in", "sign in" — the arrow enters the wall, from whichever side it reads. */
  | 'enter'
  /** Going out: "check out", "sign out" — the arrow leaves the wall, to whichever side it reads. */
  | 'leave';

/** The glyphs that carry a direction in their shape rather than in a word. */
export type DirectionalGlyphMark = 'tree-branch';

const GLYPHS: Record<DirectionalGlyphMark, { ltr: string; rtl: string }> = {
  'tree-branch': { ltr: '\u21B3', rtl: '\u21B2' },
};

/**
 * A text glyph whose shape is a direction, for the marks that are not antd icons.
 *
 * `tree-branch` is the elbow drawn in front of a nested configuration row: it points
 * rightwards and down in English (`↳`) and leftwards and down in Arabic (`↲`), which is the
 * direction the nesting itself runs. It lives here rather than inline on the page because a
 * glyph that points the wrong way looks like a deliberate arrow rather than a missed string —
 * nothing in the translation files would ever show it.
 */
export const DirectionalGlyph = ({ mark }: { mark: DirectionalGlyphMark }) => {
  const rtl = useDirection() === 'rtl';
  const glyph = GLYPHS[mark];
  return <>{rtl ? glyph.rtl : glyph.ltr}</>;
};

/** The direction i18next is using right now, as a React subscription. */
export const useDirection = (): Direction => {
  const { i18n } = useTranslation();
  return directionOf(normalizeLocale(i18n.language) ?? DEFAULT_LOCALE);
};

/**
 * Which physical side a thing that anchors to the *start* of a line ends up on.
 *
 * For the components that cannot express their side logically and are not antd's to mirror:
 * antd's `Drawer` positions itself with a physical `left`/`right`, so the nav drawer asks
 * this rather than hardcoding a side. Exported separately from the icon rendering so the
 * decision can be asserted without opening a drawer.
 */
export const startSide = (direction: Direction): 'left' | 'right' =>
  direction === 'rtl' ? 'right' : 'left';

export const DirectionalIcon = ({
  role,
  style,
}: {
  role: DirectionalRole;
  style?: CSSProperties;
}) => {
  const rtl = useDirection() === 'rtl';

  if (role === 'back') {
    return rtl ? <ArrowRightOutlined style={style} /> : <ArrowLeftOutlined style={style} />;
  }

  const mirrored: CSSProperties | undefined = rtl
    ? { ...style, transform: 'scaleX(-1)' }
    : style;
  return role === 'enter' ? <LoginOutlined style={mirrored} /> : <LogoutOutlined style={mirrored} />;
};

export default DirectionalIcon;
