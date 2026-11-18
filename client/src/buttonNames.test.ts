/// <reference types="node" />
import { readFileSync, readdirSync } from 'node:fs';
import path from 'node:path';
import { describe, expect, it } from 'vitest';
import ts from 'typescript';

/*
 * Every <Button> in the app must be nameable by a screen reader.
 *
 * An icon-only button has no text of its own, and the tooltip or Popconfirm around it is not a
 * name — which is how the list pages' row actions came to announce themselves as "button, button,
 * button" until each was given an aria-label. This is the half that stops the next one arriving
 * unnamed: the scan below fails on any Button with no aria-label, no aria-labelledby, no title and
 * no text of its own.
 *
 * Parsed rather than grepped: a JSX attribute can hold an arrow function with braces and a `/>`
 * inside, and a Button with visible text spans several lines, so a regex over source text would
 * either miss tags or match across them. Same reasoning as `lookupQuickAddCoverage.test.ts`.
 */

/** The attributes that name a control. `title` counts: antd passes it through to the DOM. */
const NAMING = ['aria-label', 'aria-labelledby', 'title'];

interface Violation {
  file: string;
  line: number;
}

/** The Buttons in one file's source that nothing gives an accessible name to. */
const unnamedButtons = (file: string, source: string): Violation[] => {
  const sourceFile = ts.createSourceFile(file, source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  const found: Violation[] = [];

  const attributesOf = (element: ts.JsxOpeningLikeElement) => {
    const names: string[] = [];
    let spread = false;
    for (const property of element.attributes.properties) {
      if (property.kind === ts.SyntaxKind.JsxSpreadAttribute) spread = true;
      else if (property.name) names.push(property.name.getText(sourceFile));
    }
    return { names, spread };
  };

  const check = (node: ts.Node, element: ts.JsxOpeningLikeElement) => {
    const { names, spread } = attributesOf(element);
    // A spread may carry the name in from elsewhere, so it is not a finding.
    if (spread || NAMING.some((attribute) => names.includes(attribute))) return;
    const { line } = sourceFile.getLineAndCharacterOfPosition(node.getStart(sourceFile));
    found.push({ file, line: line + 1 });
  };

  const visit = (node: ts.Node) => {
    if (ts.isJsxSelfClosingElement(node)) {
      if (node.tagName.getText(sourceFile) === 'Button') check(node, node);
    } else if (ts.isJsxElement(node) && node.openingElement.tagName.getText(sourceFile) === 'Button') {
      // Text, or an expression that renders some, is a name of its own.
      const hasOwnName = node.children.some((child) =>
        ts.isJsxExpression(child)
          ? child.expression !== undefined
          : ts.isJsxText(child)
            ? child.text.trim() !== ''
            : true,
      );
      if (!hasOwnName) check(node, node.openingElement);
    }
    ts.forEachChild(node, visit);
  };

  visit(sourceFile);
  return found;
};

const walk = (directory: string): string[] =>
  readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) return walk(full);
    return entry.name.endsWith('.tsx') && !entry.name.includes('.test.') ? [full] : [];
  });

/** vitest runs with the vite root (client/) as its cwd, here and in CI. */
const srcDir = path.resolve(process.cwd(), 'src');
const displayPath = (file: string) => path.relative(srcDir, file).split(path.sep).join('/');

describe('Button accessible names', () => {
  it('reports an icon-only button that nothing names', () => {
    const source = `const A = () => (
  <Button icon={<PlusOutlined />} onClick={open} />
);`;

    expect(unnamedButtons('fixture.tsx', source)).toEqual([{ file: 'fixture.tsx', line: 2 }]);
  });

  it('accepts every way a button can carry a name', () => {
    const source = `const A = () => (
  <>
    <Button aria-label="Add animal" icon={<PlusOutlined />} />
    <Button aria-labelledby="herd-heading" icon={<PlusOutlined />} />
    <Button title="Refresh" icon={<ReloadOutlined />} />
    <Button icon={<PlusOutlined />} onClick={add}>{t('addAnimal')}</Button>
    <Button {...rest} icon={<PlusOutlined />} />
  </>
);`;

    expect(unnamedButtons('fixture.tsx', source)).toEqual([]);
  });

  it('leaves no Button in the app unnamed', () => {
    const violations = walk(srcDir).flatMap((file) =>
      unnamedButtons(displayPath(file), readFileSync(file, 'utf8')),
    );

    // A failure here names the file and line: name the button, do not add it to a list.
    expect(violations.map((violation) => `${violation.file}:${violation.line}`)).toEqual([]);
  });
});
