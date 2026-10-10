import { describe, expect, it } from 'vitest';
import { comboFor, dropPath, isEditable, isSupportedName, isTyping, keyName } from '../../src/MarkdownBlaze/wwwroot/js/input.js';

const known = new Set(['alt+left', 'alt+right', 'f5', 'ctrl+r', 'ctrl+p', 'ctrl+o', 'ctrl+shift+o', 'f2', 'shift+f2', 'f9', 'shift+f9', '?', 'escape']);
const key = (k, mods = {}) => ({ key: k, ctrlKey: false, altKey: false, shiftKey: false, ...mods });

describe('comboFor', () => {
  it.each([
    [key('ArrowLeft', { altKey: true }), 'alt+left'],
    [key('F2'), 'f2'],
    [key('F2', { shiftKey: true }), 'shift+f2'],
    [key('o', { ctrlKey: true }), 'ctrl+o'],
    [key('O', { ctrlKey: true, shiftKey: true }), 'ctrl+shift+o'],
    [key('P', { ctrlKey: true }), 'ctrl+p'],
    [key('?', { shiftKey: true }), '?'],
    [key('Escape'), 'escape'],
  ])('%j', (e, expected) => expect(comboFor(e, known)).toBe(expected));

  it('overlooks a modifier that makes no difference to a function key', () => {
    expect(comboFor(key('F5', { shiftKey: true }), known)).toBe('f5');
    expect(comboFor(key('F5', { ctrlKey: true }), known)).toBe('f5');
  });

  it('is empty for what the registry does not know', () => {
    expect(comboFor(key('a'), known)).toBe('');
    expect(comboFor(key('b', { ctrlKey: true }), known)).toBe('');
    expect(comboFor(key('ArrowLeft'), known)).toBe('');
  });

  it('keyName drops Arrow and lower-cases', () => {
    expect(keyName('ArrowRight')).toBe('right');
    expect(keyName('F11')).toBe('f11');
    expect(keyName('Q')).toBe('q');
  });
});

describe('isEditable', () => {
  const el = (tag, props = {}) => ({ nodeType: 1, tagName: tag, isContentEditable: false, ...props });

  it.each([
    el('TEXTAREA'),
    el('SELECT'),
    el('INPUT', { type: 'text' }),
    el('INPUT', { type: 'search' }),
    el('INPUT'),
    el('DIV', { isContentEditable: true }),
  ])('%j takes typed text', e => expect(isEditable(e)).toBe(true));

  it.each([
    el('BUTTON'),
    el('DIV'),
    el('INPUT', { type: 'checkbox' }),
    el('INPUT', { type: 'range' }),
    null,
    { nodeType: 3 },
  ])('%j does not', e => expect(isEditable(e)).toBe(false));
});

describe('isTyping', () => {
  const field = { nodeType: 1, tagName: 'INPUT', type: 'text', isContentEditable: false };
  const page = { nodeType: 1, tagName: 'BODY', isContentEditable: false };
  const press = (k, target, mods = {}) => ({ ...key(k, mods), metaKey: false, target, composedPath: () => [target] });

  it('is true for a character typed into a field, so "?" is not the help there', () => {
    expect(isTyping(press('?', field, { shiftKey: true }))).toBe(true);
    expect(isTyping(press('a', field))).toBe(true);
  });

  it('is false for the same key anywhere else', () => expect(isTyping(press('?', page, { shiftKey: true }))).toBe(false));

  it('is false for keys that cannot type, or with Ctrl or Alt held', () => {
    expect(isTyping(press('F5', field))).toBe(false);
    expect(isTyping(press('Escape', field))).toBe(false);
    expect(isTyping(press('o', field, { ctrlKey: true }))).toBe(false);
    expect(isTyping(press('ArrowLeft', field, { altKey: true }))).toBe(false);
  });

  it('looks through a component that wraps its input in a shadow root', () => {
    const host = { nodeType: 1, tagName: 'FLUENT-TEXT-INPUT', isContentEditable: false };
    const e = { ...key('?', { shiftKey: true }), metaKey: false, target: host, composedPath: () => [field, host] };
    expect(isTyping(e)).toBe(true);
  });
});

describe('isSupportedName', () => {
  const extensions = ['.md', '.markdown', '.mdx', '.txt'];
  it.each(['a.md', 'A.MD', 'notes.markdown', 'x.mdx', 'readme.txt'])('%s is opened', n => expect(isSupportedName(n, extensions)).toBe(true));
  it.each(['a.png', 'md', 'a.md.exe', 'a.mdd'])('%s is not', n => expect(isSupportedName(n, extensions)).toBe(false));
});

describe('dropPath', () => {
  const dt = data => ({ getData: type => data[type] ?? '' });

  it('takes a path the engine offers on the file', () => expect(dropPath(dt({}), { path: 'C:\\a.md' })).toBe('C:\\a.md'));

  it('reads a file:// uri from the uri list, skipping comments', () =>
    expect(dropPath(dt({ 'text/uri-list': '# a comment\nfile:///C:/My%20Docs/a.md\n' }), {})).toBe('C:/My Docs/a.md'));

  it('keeps a posix path as it is', () => expect(dropPath(dt({ 'text/plain': 'file:///home/me/a.md' }), {})).toBe('/home/me/a.md'));

  it('is empty when the engine offers nothing', () => expect(dropPath(dt({}), {})).toBe(''));
  it('is empty for anything that is not a file uri', () => expect(dropPath(dt({ 'text/plain': 'https://example.com/a.md' }), {})).toBe(''));
});
