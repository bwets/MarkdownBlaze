---
title: Syntax Showcase
tags:
  - sample
  - reference
---

# Syntax Showcase

A single page that exercises the **Obsidian / GitHub flavoured** syntax MarkdownBlaze understands,
using the sci-fi library as its playground. Open the **Headers** tab in the sidebar to see the
outline this page produces. :rocket:

## Callouts (three dialects, one look)

The same styled callout is spelled three ways. Use whichever your notes already use.

> [!info] GitHub / Obsidian
> Written as `> [!info] Title`. The `[!TYPE]` is case-insensitive, and the title is optional.

:::tip[Docusaurus]
Written as `:::tip[Title] … :::`.
:::

!!! warning "MkDocs"
    Written as `!!! warning "Title"` with an indented body.

Add `+` or `-` after an Obsidian type to make it collapsible:

> [!tip]- Spoiler: who is the Kwisatz Haderach? (click to expand)
> Paul Atreides. Collapsed by default because of the trailing `-`.

> [!danger]+ Expanded by default
> A trailing `+` opens the callout on load.

## Wiki links

Obsidian `[[double-bracket]]` links navigate **in-app**, just like normal relative links:

- Plain: [[dune/houses]] resolves to the Great Houses page.
- Aliased: [[asimov/foundation|the Foundation saga]] shows custom text.
- Heading: [[dune/index#The great Houses]] targets a section.
- Image embed: `![[arrakis.png]]` inlines an image; a note embed like `![[dune/houses]]`
  (no image extension) becomes a link instead.

Inside a table the alias pipe is escaped as `\|`:

| Saga | Jump in |
|---|---|
| Dune | [[dune/houses\|Great Houses of the Landsraad]] |
| Foundation | [[asimov/foundation\|Seldon's plan]] |

## Tags

Inline tags render as pills: #dune #sci-fi/space-opera #reread.
Ordinary text is left alone — `C#`, a bare `#`, and issue numbers like `#42` are **not** tagged.

## Math (KaTeX)

Inline: the Tyrell relativity of spice-time, $E = mc^2$, and Euler's identity
$e^{i\pi} + 1 = 0$.

Display block:

$$
\Psi(\text{prescience}) = \sum_{t=0}^{\infty} \frac{\text{spice}^t}{t!}
$$

A GitHub ```` ```math ```` fenced block renders the same way:

```math
\oint_{\partial \Sigma} \mathbf{B} \cdot d\boldsymbol{\ell} = \mu_0 I_{\text{enc}}
```

## Emphasis extras

You can ==highlight melange==, ~~strike Muad'Dib~~, write water as H~2~O, and energy as
E = mc^2^ with sub/superscripts.

## Hidden comments

Obsidian comments never render. There is an inline comment right here %%(this note is private)%%
that you should not see, and an entire block below is omitted:

%%
Editor's note: rewrite this section before publishing.
None of these lines appear in the rendered page.
%%

The paragraph after the comment block continues normally.

---

Back to the [library home](index), or explore the [[dune/index|Dune wing]].
