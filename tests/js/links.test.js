import { describe, expect, it } from 'vitest';
import { classifyLink } from '../../src/MarkdownBlaze/wwwroot/js/links.js';

// The list is the one .NET hands over in config.linkSchemes (ExternalLinks.AllowedSchemes).
const schemes = ['ftp', 'http', 'https', 'mailto', 'tel'];

describe('classifyLink', () => {
  it.each([
    ['https://example.com/a?b=1#c', 'ext'],
    ['HTTP://EXAMPLE.COM', 'ext'],
    ['mailto:someone@example.com', 'ext'],
    ['tel:+3212345678', 'ext'],
    ['ftp://example.com/pub', 'ext'],
  ])('%s opens in the operating system', (href, kind) => expect(classifyLink(href, schemes).kind).toBe(kind));

  it('treats //host as https', () => expect(classifyLink('//example.com/x', schemes)).toEqual({ kind: 'ext', url: 'https://example.com/x' }));

  it.each(['javascript:alert(1)', 'file:///C:/x.exe', 'data:text/html,hi', 'vbscript:x', 'ms-msdt:/id', 'search-ms:query=x', 'C:/x.exe', 'C:\\x.exe'])(
    '%s does nothing', href => expect(classifyLink(href, schemes).kind).toBe('none'));

  it.each(['', '#', '#section', undefined, null])('%s is left to the browser to scroll', href =>
    expect(classifyLink(href, schemes).kind).toBe('page'));

  it('swallows a relative path that is not an in-app target', () => expect(classifyLink('other/page.html', schemes).kind).toBe('none'));

  it('uses the list it is given, not one of its own', () => expect(classifyLink('mailto:a@b.c', ['https']).kind).toBe('none'));
});
