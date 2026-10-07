/* ==========================================================================
   Lincoln Window Cleaner: shared runtime
   Exposes window.LWC for section scripts:
     LWC.onReady(fn)      run fn({ gsap, ScrollTrigger, lenis, reducedMotion }) once libs are set up
     LWC.reducedMotion    true when the visitor asked for less motion (or libs failed to load)
     LWC.contact          phone / WhatsApp details (single source of truth)
     LWC.whatsapp(text)   wa.me URL with a pre-filled message
   ========================================================================== */
(function () {
  'use strict';

  const root = document.documentElement;
  root.classList.remove('no-js');

  const contact = {
    phoneDisplay: '07397 872353',
    phoneHref: 'tel:+447397872353',
    whatsappNumber: '447397872353',
  };

  const hasLibs = typeof window.gsap !== 'undefined' && typeof window.ScrollTrigger !== 'undefined';
  const prefersReduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  const reducedMotion = prefersReduced || !hasLibs;
  if (reducedMotion) root.classList.add('reduced-motion');

  const queue = [];
  let ctx = null;

  const LWC = {
    contact,
    reducedMotion,
    whatsapp(text) {
      return `https://wa.me/${contact.whatsappNumber}?text=${encodeURIComponent(text || '')}`;
    },
    onReady(fn) {
      if (ctx) safeRun(fn);
      else queue.push(fn);
    },
  };
  window.LWC = LWC;

  function safeRun(fn) {
    try { fn(ctx); } catch (err) { console.error('[LWC section]', err); }
  }

  /* ---------- Smooth scroll + GSAP ---------- */
  function setupMotion() {
    let lenis = null;
    if (hasLibs) {
      gsap.registerPlugin(ScrollTrigger);
      gsap.defaults({ ease: 'power3.out', duration: 0.9 });
    }
    if (!reducedMotion && typeof window.Lenis !== 'undefined') {
      lenis = new Lenis({ duration: 1.15, easing: t => Math.min(1, 1.001 - Math.pow(2, -10 * t)), smoothWheel: true });
      lenis.on('scroll', ScrollTrigger.update);
      gsap.ticker.add(time => lenis.raf(time * 1000));
      gsap.ticker.lagSmoothing(0);
    }
    LWC.lenis = lenis;
    ctx = { gsap: window.gsap, ScrollTrigger: window.ScrollTrigger, lenis, reducedMotion };
  }

  /* ---------- Generic reveals: data-reveal, data-reveal-stagger, data-split ---------- */
  function setupReveals() {
    if (reducedMotion) return;
    gsap.utils.toArray('[data-reveal]').forEach(el => {
      gsap.to(el, {
        opacity: 1, y: 0, scale: 1, duration: 1.1, delay: parseFloat(el.dataset.revealDelay || 0),
        scrollTrigger: { trigger: el, start: 'top 88%', once: true },
      });
    });
    gsap.utils.toArray('[data-reveal-stagger]').forEach(group => {
      const items = group.children;
      gsap.set(items, { opacity: 0, y: 36 });
      gsap.to(items, {
        opacity: 1, y: 0, duration: 1, stagger: 0.12,
        scrollTrigger: { trigger: group, start: 'top 85%', once: true },
      });
    });
    if (typeof window.SplitType !== 'undefined') {
      gsap.utils.toArray('[data-split]').forEach(el => {
        const split = new SplitType(el, { types: 'lines,words', lineClass: 'split-line' });
        gsap.set(el.querySelectorAll('.split-line'), { overflow: 'hidden' });
        gsap.from(split.words, {
          yPercent: 110, duration: 1.1, stagger: 0.035, ease: 'power4.out',
          scrollTrigger: { trigger: el, start: 'top 85%', once: true },
        });
      });
    }
  }

  /* ---------- Nav ---------- */
  function setupNav() {
    const nav = document.querySelector('.nav');
    if (!nav) return;
    const toggle = nav.querySelector('.nav__toggle');
    let lastY = window.scrollY;
    const onScroll = () => {
      const y = window.scrollY;
      nav.classList.toggle('is-scrolled', y > 24);
      nav.classList.toggle('is-hidden', y > lastY && y > 600 && !nav.classList.contains('is-open'));
      lastY = y;
      const cta = document.querySelector('.mobile-cta');
      if (cta) cta.classList.toggle('is-visible', y > window.innerHeight * 0.6);
    };
    window.addEventListener('scroll', onScroll, { passive: true });
    onScroll();

    toggle?.addEventListener('click', () => {
      const open = nav.classList.toggle('is-open');
      toggle.setAttribute('aria-expanded', String(open));
    });
    nav.querySelectorAll('.nav__links a').forEach(a => a.addEventListener('click', () => {
      nav.classList.remove('is-open');
      toggle?.setAttribute('aria-expanded', 'false');
    }));

    // Smooth in-page anchors through Lenis
    document.addEventListener('click', e => {
      const a = e.target.closest('a[href^="#"]');
      if (!a || a.getAttribute('href').length < 2) return;
      const target = document.querySelector(a.getAttribute('href'));
      if (!target) return;
      e.preventDefault();
      if (LWC.lenis) LWC.lenis.scrollTo(target, { offset: -70 });
      else target.scrollIntoView({ behavior: reducedMotion ? 'auto' : 'smooth' });
    });
  }

  /* ---------- Open now (Mon–Sun 08:00–20:00, UK time) ---------- */
  function setupOpenNow() {
    const badges = document.querySelectorAll('[data-open-badge]');
    if (!badges.length) return;
    const update = () => {
      const hour = Number(new Intl.DateTimeFormat('en-GB', { hour: 'numeric', hour12: false, timeZone: 'Europe/London' }).format(new Date()));
      const open = hour >= 8 && hour < 20;
      badges.forEach(b => {
        b.classList.toggle('is-closed', !open);
        b.textContent = open ? 'Open now · until 8pm' : 'Closed · opens 8am';
      });
    };
    update();
    setInterval(update, 60_000);
  }

  /* ---------- Contact links: data-tel / data-whatsapp="message" ---------- */
  function setupContactLinks() {
    document.querySelectorAll('[data-tel]').forEach(a => { a.href = contact.phoneHref; });
    document.querySelectorAll('[data-whatsapp]').forEach(a => {
      a.href = LWC.whatsapp(a.dataset.whatsapp || "Hi Florida, I'd like a free window cleaning quote please.");
      a.target = '_blank'; a.rel = 'noopener';
    });
    document.querySelectorAll('[data-year]').forEach(el => { el.textContent = new Date().getFullYear(); });
  }

  /* ---------- Cookie consent: third-party embeds (Google Maps) load only after "Accept" ---------- */
  const CONSENT_KEY = 'lwc-consent';
  function getConsent() { try { return localStorage.getItem(CONSENT_KEY); } catch (e) { return null; } }
  function setConsent(v) { try { localStorage.setItem(CONSENT_KEY, v); } catch (e) { /* storage blocked: choice lasts this page view */ } }

  function loadEmbeds() {
    document.querySelectorAll('iframe[data-consent-src]').forEach(f => { if (!f.src) f.src = f.dataset.consentSrc; });
    document.querySelectorAll('[data-consent-placeholder]').forEach(el => { el.hidden = true; });
  }

  function setupConsent() {
    const choice = getConsent();
    if (choice === 'accepted') { loadEmbeds(); return; }
    document.querySelectorAll('[data-consent-accept]').forEach(b => b.addEventListener('click', () => {
      setConsent('accepted'); loadEmbeds();
      document.querySelector('.consent-banner')?.remove();
    }));
    if (choice === 'declined') return;

    const banner = document.createElement('div');
    banner.className = 'consent-banner';
    banner.setAttribute('role', 'region');
    banner.setAttribute('aria-label', 'Cookie choices');
    banner.innerHTML =
      '<p>This site sets no tracking cookies of its own. The Google map may set cookies if you load it. ' +
      '<a href="privacy-policy.html#cookies">Privacy policy</a></p>' +
      '<div class="consent-banner__actions">' +
      '<button type="button" class="btn btn--ghost" data-consent="declined">No thanks</button>' +
      '<button type="button" class="btn btn--brass" data-consent="accepted">Accept</button></div>';
    banner.addEventListener('click', e => {
      const b = e.target.closest('[data-consent]');
      if (!b) return;
      setConsent(b.dataset.consent);
      if (b.dataset.consent === 'accepted') loadEmbeds();
      banner.remove();
    });
    document.body.appendChild(banner);
  }

  function init() {
    setupConsent();
    setupContactLinks();
    setupNav();
    setupOpenNow();
    setupMotion();
    queue.splice(0).forEach(safeRun);
    setupReveals();
    if (hasLibs) {
      window.addEventListener('load', () => ScrollTrigger.refresh());
    }
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init);
  else init();
})();
