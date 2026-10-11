import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { bridge } from '../../src/MarkdownBlaze/wwwroot/js/bridge.js';
import {
  addressOf,
  classifyLink,
  externalAddressOf,
  hideLinkStatus,
  installLinkClicks,
  installLinkStatus,
} from '../../src/MarkdownBlaze/wwwroot/js/links.js';

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

const XLINK = 'http://www.w3.org/1999/xlink';

/// An anchor inside an SVG, as Mermaid makes it: the address in xlink:href (SVG 1.1) or href (SVG 2).
function svgAnchor(attribute, address) {
  document.body.innerHTML =
    '<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"><a id="a"><text id="t">label</text></a></svg>';
  const anchor = document.getElementById('a');
  if (attribute === 'xlink:href') anchor.setAttributeNS(XLINK, 'xlink:href', address);
  else anchor.setAttributeNS(null, 'href', address);
  return anchor;
}

describe('addressOf', () => {
  afterEach(() => { document.body.innerHTML = ''; });

  it('reads an HTML anchor', () => {
    document.body.innerHTML = '<a id="a" href="https://example.com/x">x</a>';
    expect(addressOf(document.getElementById('a'))).toBe('https://example.com/x');
  });

  it('reads the raw attribute, not the address the browser makes of it', () => {
    document.body.innerHTML = '<a id="a" href="other.md">x</a>';
    expect(addressOf(document.getElementById('a'))).toBe('other.md');
  });

  it('reads an SVG anchor with xlink:href', () => expect(addressOf(svgAnchor('xlink:href', 'https://example.com'))).toBe('https://example.com'));
  it('reads an SVG anchor with href', () => expect(addressOf(svgAnchor('href', 'https://example.com'))).toBe('https://example.com'));

  it('reads the animated href of an SVGAElement (href.baseVal), which a plain getAttribute cannot see', () => {
    const element = {
      getAttribute: () => null,
      getAttributeNS: () => null,
      href: { baseVal: 'https://example.com/svg', animVal: 'https://example.com/svg' },
    };
    expect(addressOf(element)).toBe('https://example.com/svg');
  });

  it('is null for an anchor with no address at all, which is then an in-page anchor to the classification', () => {
    document.body.innerHTML = '<a id="a">x</a>';
    expect(addressOf(document.getElementById('a'))).toBeNull();
    expect(classifyLink(addressOf(document.getElementById('a')), schemes).kind).toBe('page');
  });
});

describe('a click on a link', () => {
  let controller;
  const invoke = vi.fn(() => Promise.resolve());

  beforeEach(() => {
    controller = new AbortController();
    invoke.mockClear();
    bridge.ref = { invokeMethodAsync: invoke };
    bridge.config = { linkSchemes: schemes };
    installLinkClicks(controller.signal);
  });
  afterEach(() => { controller.abort(); bridge.ref = null; bridge.config = null; document.body.innerHTML = ''; });

  const click = target => {
    const event = new MouseEvent('click', { bubbles: true, cancelable: true });
    target.dispatchEvent(event);
    return event;
  };

  it('on an HTML link to the web is handed to .NET and does not navigate', () => {
    document.body.innerHTML = '<a id="a" href="https://example.com/">x</a>';
    const event = click(document.getElementById('a'));
    expect(event.defaultPrevented).toBe(true);
    expect(invoke).toHaveBeenCalledWith('OnLink', 'ext', 'https://example.com/');
  });

  it('on an SVG link that Mermaid made is handed to .NET through the same allow-list, and does not navigate', () => {
    const event = click(svgAnchor('xlink:href', 'https://example.com/diagram').querySelector('text'));
    expect(event.defaultPrevented).toBe(true);
    expect(invoke).toHaveBeenCalledWith('OnLink', 'ext', 'https://example.com/diagram');
  });

  it.each(['javascript:alert(1)', 'file:///C:/Windows/System32/calc.exe', 'data:text/html,x', 'ms-msdt:/id'])(
    'on an SVG link to %s does nothing and does not navigate', address => {
      const event = click(svgAnchor('xlink:href', address));
      expect(event.defaultPrevented).toBe(true);
      expect(invoke).not.toHaveBeenCalled();
    });

  it('on an SVG link with href rather than xlink:href is held to the same rules', () => {
    expect(click(svgAnchor('href', 'javascript:alert(1)')).defaultPrevented).toBe(true);
    expect(invoke).not.toHaveBeenCalled();
  });

  it('on an in-app link reports the target and not the address', () => {
    document.body.innerHTML = '<a id="a" href="#" data-nav="C:\\wiki\\a.md">x</a>';
    click(document.getElementById('a'));
    expect(invoke).toHaveBeenCalledWith('OnLink', 'nav', 'C:\\wiki\\a.md');
  });

  it('on a link the renderer made inert does nothing', () => {
    document.body.innerHTML = '<a id="a" href="#" data-md-blocked="link">x</a>';
    const event = click(document.getElementById('a'));
    expect(event.defaultPrevented).toBe(true);
    expect(invoke).not.toHaveBeenCalled();
  });

  it('on an in-page anchor is left to the browser to scroll', () => {
    document.body.innerHTML = '<a id="a" href="#top">x</a>';
    expect(click(document.getElementById('a')).defaultPrevented).toBe(false);
    expect(invoke).not.toHaveBeenCalled();
  });
});

// SEC-21: the address a link would open is shown before it is clicked.
describe('externalAddressOf', () => {
  const anchor = html => {
    document.body.innerHTML = html;
    return document.getElementById('a');
  };
  afterEach(() => { document.body.innerHTML = ''; });

  it('is the address that would be opened, from the allow-list classification', () =>
    expect(externalAddressOf(anchor('<a id="a" href="https://example.com/a?b=1#c">x</a>'), schemes)).toBe('https://example.com/a?b=1#c'));

  it('is not the text of the link or its title, which the author writes', () => {
    const lying = anchor('<a id="a" href="https://evil.invalid/login" title="https://good.example/">https://good.example</a>');
    expect(externalAddressOf(lying, schemes)).toBe('https://evil.invalid/login');
  });

  it('writes a protocol-relative address the way it will be opened, as https', () =>
    expect(externalAddressOf(anchor('<a id="a" href="//example.com/x">x</a>'), schemes)).toBe('https://example.com/x'));

  it('writes a host made of look-alike letters as the ASCII it really is', () => {
    // the first letter of the host is a Cyrillic "a", which looks like a Latin one
    const homoglyph = anchor(`<a id="a" href="https://${String.fromCodePoint(0x0430)}pple.com/">https://apple.com/</a>`);
    expect(externalAddressOf(homoglyph, schemes)).toBe('https://xn--pple-43d.com/');
  });

  it('is the address of a mail link too', () =>
    expect(externalAddressOf(anchor('<a id="a" href="mailto:a@example.com">x</a>'), schemes)).toBe('mailto:a@example.com'));

  it.each([
    ['an in-app link', '<a id="a" href="#" data-nav="C:\\wiki\\a.md">x</a>'],
    ['an in-page anchor', '<a id="a" href="#top">x</a>'],
    ['an inert link', '<a id="a" href="#" data-md-blocked="link">x</a>'],
    ['a script link', '<a id="a" href="javascript:alert(1)">x</a>'],
    ['a file link', '<a id="a" href="file:///C:/x.exe">x</a>'],
    ['a relative link that goes nowhere', '<a id="a" href="page.html">x</a>'],
    ['an anchor without an address', '<a id="a">x</a>'],
  ])('is null for %s', (_, html) => expect(externalAddressOf(anchor(html), schemes)).toBeNull());

  it('reads the address of an SVG anchor too', () =>
    expect(externalAddressOf(svgAnchor('xlink:href', 'https://example.com/diagram'), schemes)).toBe('https://example.com/diagram'));

  it('is null when there is no anchor', () => expect(externalAddressOf(null, schemes)).toBeNull());
});

describe('the link status strip', () => {
  let controller;
  const strip = () => document.querySelector('.link-status');

  beforeEach(() => {
    controller = new AbortController();
    bridge.config = { linkSchemes: schemes };
    installLinkStatus(controller.signal);
  });
  afterEach(() => {
    controller.abort();
    hideLinkStatus();
    bridge.config = null;
    document.body.innerHTML = '';
  });

  const fire = (type, target, init = {}) => target.dispatchEvent(new MouseEvent(type, { bubbles: true, ...init }));

  it('shows the real address while the pointer is over a link that opens one, whatever the text says', () => {
    document.body.innerHTML = '<p><a id="a" href="https://evil.invalid/x" title="https://good.example">https://good.example</a></p>';

    fire('mouseover', document.getElementById('a'));

    expect(strip().hidden).toBe(false);
    expect(strip().textContent).toBe('https://evil.invalid/x');
  });

  it('shows nothing for a link that stays in the app', () => {
    document.body.innerHTML = '<p><a id="a" href="#" data-nav="C:\\wiki\\a.md">x</a></p>';

    fire('mouseover', document.getElementById('a'));

    expect(strip()?.hidden ?? true).toBe(true);
    expect(strip()?.textContent ?? '').toBe('');
  });

  it('goes when the pointer moves on to something that is not a link, and when it leaves the window', () => {
    document.body.innerHTML = '<p id="p"><a id="a" href="https://example.com/">x</a> and text</p>';
    fire('mouseover', document.getElementById('a'));
    expect(strip().hidden).toBe(false);

    fire('mouseover', document.getElementById('p'));
    expect(strip().hidden).toBe(true);

    fire('mouseover', document.getElementById('a'));
    expect(strip().hidden).toBe(false);
    fire('mouseout', document.getElementById('a'), { relatedTarget: null });
    expect(strip().hidden).toBe(true);
  });

  it('shows the address of a link that has the keyboard focus, and goes when it loses it', () => {
    document.body.innerHTML = '<p><a id="a" href="https://example.com/focus">x</a></p>';
    const link = document.getElementById('a');

    link.focus();
    expect(strip().textContent).toBe('https://example.com/focus');
    expect(strip().hidden).toBe(false);

    link.blur();
    expect(strip().hidden).toBe(true);
  });

  it('shows the address of a link around an element, found from whatever is under the pointer', () => {
    document.body.innerHTML = '<p><a href="https://example.com/inner"><em id="e">emphasised</em></a></p>';

    fire('mouseover', document.getElementById('e'));

    expect(strip().textContent).toBe('https://example.com/inner');
  });

  it('is app chrome: it has the class the stylesheet gives it and is hidden from assistive technology', () => {
    document.body.innerHTML = '<p><a id="a" href="https://example.com/">x</a></p>';
    fire('mouseover', document.getElementById('a'));

    expect(strip().className).toBe('link-status');
    expect(strip().getAttribute('aria-hidden')).toBe('true');
  });

  it('goes at a click', () => {
    document.body.innerHTML = '<p><a id="a" href="https://example.com/">x</a></p>';
    fire('mouseover', document.getElementById('a'));
    document.getElementById('a').addEventListener('click', e => e.preventDefault()); // there is no click handler here; keep jsdom from navigating
    document.getElementById('a').dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }));

    expect(strip().hidden).toBe(true);
  });
});
