# Redesign pass, 7 Oct 2026 (read this, then docs/brief.md)

Project root: `C:\Users\Student\OneDrive\LincolnWC Site\` (ignore the old path in brief.md).
Backup exists at `C:\Users\Student\OneDrive\LincolnWC Site.backup-2026-10-07\`. Never touch the backup.

## Client feedback
"Make it look more professional, with less plain-looking formats, and maybe some different fonts."
Today many sections are flat: a centred heading, then a grid of identical rounded white cards. The goal is an **editorial, heritage-premium** look, like a boutique Lincoln trade with a cathedral-city identity, not a SaaS template.

## New type system (already live in css/base.css + every page's <head>)
- `--font-display`: **Instrument Serif** (only weight 400 + italic; never set 500–700 on it, because faux-bold looks cheap). Use big sizes and italics for accent words.
- `--font-ui`: **Geist** (300–700).
- `--font-mono`: **Geist Mono**, used for eyebrows, labels, numerals, small meta text (uppercase, letter-spacing .12–.18em).
- Replace any hard-coded `font-weight: 500/600` on display-font elements in YOUR files with 400.

## Design language to apply (use the tokens in base.css)
1. **Section index markers**: each major section's eyebrow gets a mono numeral, e.g. `<span class="eyebrow">01 — Pure water</span>`. Order: water 01, services 02, results 03, Bishop's Palace 04, reviews 05, areas 06, FAQ 07, quote 08.
2. **Hairline structure instead of plain boxes**: 1px rules (`var(--line)` / `var(--line-light)`), thin brass hairlines (`var(--brass)` at ~40–60% opacity), corner ticks, and numbered list rows. Fewer identical floating white cards. Where cards stay, give them depth: layered border plus an inner highlight, subtle texture, and a real hover state.
3. **Asymmetric editorial layouts**: left-aligned heads with a short intro paragraph beside them (a 2-column head at ≥960px), large display numerals, pull-quotes, and an occasional oversized italic word.
4. **Material texture**: real CC0 textures are in `assets/img/tex/` (brick_wall_001, castle_wall_varriation = limestone, ceramic_roof_01, concrete_wall_003; .webp). Use them sparingly as low-opacity backgrounds or masks (e.g. limestone at 6–10% behind dark sections). A fine film grain via an inline SVG `feTurbulence` data URI is fine too.
5. **Colour**: navy/glass/aqua/brass stay. Lean more on brass for premium details (rules, numerals, stars, icons) and keep aqua for interactive/water cues. AA contrast must hold.
6. **Motion**: keep existing GSAP behaviour working. Don't break `data-reveal` / `data-split`. Respect reducedMotion.
7. **Responsive**: must look right at 390, 768, 1440 **and 3440px wide (ultrawide monitors)**. No horizontal scroll.

## Workflow rules
- Only edit the files you own (listed in your task). Shared files (`css/base.css`, `js/main.js`, `index.html` outside include markers) belong to the "Global" helper only.
- Edit `parts/*.html`, then run `python tools/assemble.py` from the project root to paste parts into index.html. Running it is safe and idempotent; never hand-edit index.html between `<!-- @include x -->` and `<!-- @end x -->`.
- Keep every existing `PLACEHOLDER` comment and all real business content (reviews are verbatim Bark reviews; never alter their text).
- Keep the IDs used by nav links: #top, #pure-water, #services, #results, #reviews, #areas, #faq, #quote.
- Check JS with `node --check file.js` if you edit JS.
- Report: the files changed, what changed visually, and any risks.
