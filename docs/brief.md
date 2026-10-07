# Build brief: Lincoln Window Cleaner (for every helper)

Project root: `C:\Users\Student\Projects\lincoln-window-cleaner\`
Static HTML/CSS/JS, no build step. **Read `css/base.css`, `js/main.js` and `index.html` before writing anything.**

## Goal
A premium, professional site for a local window cleaner that makes people want to book. It should be obvious within a second that this is a window cleaning business (glass, water, squeegees, shine, reflections, droplets). Scroll animations should feel high-end and smooth, never cheap or gimmicky.

## Real business details (use exactly)
- Name: **Lincoln Window Cleaner** · Owner: **Florida** (first-person tone is fine: "I", or "we" for the business)
- Phone: **07397 872353** (`tel:+447397872353`) · WhatsApp: `https://wa.me/447397872353`
- Area: Lincoln and surrounding areas · Hours: **Mon–Sun 08:00–20:00** (open 7 days)
- Residential **and** commercial
- Services:
  1. **Window cleaning**: water-fed pole system with **ultra-pure water** for a spotless, streak-free finish; **frames, sills and doors included**
  2. **Fascia cleaning**
  3. **Gutter vacuuming**: **before & after photos/video from a 4K camera** as proof
- Notable job: **Bishop's Palace windows** (Lincoln landmark)
- Claims you may use: "5-star reviews", "competitive prices", "free quotes", "experienced"
- No email address. All contact goes through phone, WhatsApp or the quote form (which opens WhatsApp).

## Placeholders
Anything invented (reviews, reviewer names, prices, towns list, stats like "1,200 homes") **must** have an HTML comment right next to it: `<!-- PLACEHOLDER: confirm with client -->` (in JS: `// PLACEHOLDER: confirm with client`). Keep invented content realistic and modest. Never claim specific accreditations.

## Design system (in css/base.css; use the tokens, never hard-code colours)
- Colours: `--navy-900` primary dark, `--glass` page background, `--grad-sky`, `--aqua` accent, `--brass` premium accent (CTAs, stars), `--grad-night` dark sections
- Type: `--font-display` (Fraunces) for headings, `--font-ui` (Inter). Use the `.h2`, `.h3`, `.lead`, `.eyebrow` and `.muted` helpers; `<em>` inside `.h2` gives an aqua italic accent word.
- Components: `.container`, `.section`, `.section--dark`, `.section--sky`, `.section-head(--center)`, `.btn` + `.btn--brass|--aqua|--ghost|--glass|--whatsapp|--lg`, `.glass-card`, `.stars`, `.shine` (light sweep across text)
- Radii `--radius*`, shadows `--shadow-soft|lift|glow`, easing `--ease-out`
- Mobile first. It must look perfect at 390px, 768px and 1440px. No horizontal scroll, ever.

## Motion rules
- Section JS goes in an IIFE that calls `LWC.onReady(({ gsap, ScrollTrigger, lenis, reducedMotion }) => { ... })`.
- If `reducedMotion` is true: **no pinning, no scrubbing, no canvas animation loops**. Show the final state statically, and the content must stay fully usable.
- Generic reveals are free: put `data-reveal` (or `="fade"` / `="scale"`, plus optional `data-reveal-delay="0.2"`) on elements, `data-reveal-stagger` on a parent to stagger its children, and `data-split` on headings for a word-by-word reveal. main.js wires these up, so don't duplicate them.
- Use GSAP ScrollTrigger for anything bespoke. Scope selectors to your section's root element. Use `ScrollTrigger.matchMedia` / `gsap.matchMedia()` to simplify on mobile.
- Animate only `transform` / `opacity` (and canvas). Pause canvas loops when off-screen (IntersectionObserver) and handle resize/DPR.
- Use `LWC.whatsapp(text)` for WhatsApp URLs. `data-tel` and `data-whatsapp="message"` attributes are wired automatically.

## File ownership: only write YOUR files
- `parts/<name>.html`: a fragment containing only your `<section>` element(s), with no `<html>`/`<head>`. It gets pasted into `index.html` at `<!-- @include <name> -->`.
- `css/sections/<name>.css`: all selectors prefixed by your section's class/id
- `js/sections/<name>.js`
- `assets/svg/<name>-*.svg` if you need separate SVG files (inline SVG is preferred)
- Do **not** edit base.css, main.js, index.html or another helper's files. If you need something shared, say so in your report.

## Accessibility
Semantic HTML, alt text / `aria-hidden` on decoration, labelled form controls, visible focus, AA contrast, keyboard-operable widgets (sliders, carousels, accordions), `aria-live` for dynamic results.

## Report back
List the files you wrote, the section ids and classes, any placeholders you added, anything you need from base.css/main.js, and known limitations.
