/* ==========================================================================
   Castle Square panorama: depth parallax, labels popping in, windows glinting
   one after another, and the window cleaner's pole working away.
   ========================================================================== */
(function () {
  LWC.onReady(({ gsap, ScrollTrigger, reducedMotion }) => {
    const section = document.querySelector('#lincoln');
    if (!section) return;

    const scroller = section.querySelector('.bailgate__scroller');
    const tags = section.querySelectorAll('.bailgate__tag');
    const glints = section.querySelectorAll('.bg-glint');
    const pole = section.querySelector('.bg-pole');
    const spray = section.querySelector('.bg-spray');
    const flag = section.querySelector('.bg-flag');

    // Phones: start the swipeable panorama on the castle gate
    if (scroller && scroller.scrollWidth > scroller.clientWidth) {
      scroller.scrollLeft = scroller.scrollWidth * 0.18;
    }

    if (reducedMotion) return;

    // Each layer drifts at its own depth as the section passes
    section.querySelectorAll('.bg-layer').forEach(layer => {
      const depth = parseFloat(layer.dataset.depth || 0);
      gsap.fromTo(layer, { y: 30 * depth }, {
        y: -30 * depth, ease: 'none',
        scrollTrigger: { trigger: section, start: 'top bottom', end: 'bottom top', scrub: true },
      });
    });

    // Labels pop in, then windows catch the light one by one
    gsap.set(tags, { opacity: 0, y: 10 });
    const intro = gsap.timeline({ paused: true });
    intro.to(tags, { opacity: 1, y: 0, duration: 0.6, stagger: 0.12, ease: 'back.out(2)' });
    ScrollTrigger.create({ trigger: scroller, start: 'top 75%', once: true, onEnter: () => intro.play() });

    const shimmer = gsap.timeline({ repeat: -1, repeatDelay: 2.5, paused: true });
    glints.forEach((g, i) => {
      shimmer.fromTo(g, { opacity: 0 }, { opacity: 0.75, duration: 0.25, yoyo: true, repeat: 1, ease: 'power1.inOut' }, i * 0.18);
    });
    ScrollTrigger.create({
      trigger: scroller, start: 'top bottom', end: 'bottom top',
      onToggle: self => (self.isActive ? shimmer.play() : shimmer.pause()),
    });

    // The water-fed pole scrubs up and down the window, spraying
    if (pole) {
      gsap.to(pole, { rotation: -4, svgOrigin: '906 596', duration: 1.1, ease: 'sine.inOut', yoyo: true, repeat: -1 });
    }
    if (spray) {
      gsap.to(spray.children, { y: 26, opacity: 0, duration: 0.9, stagger: { each: 0.15, repeat: -1 }, ease: 'power1.in' });
    }
    if (flag) {
      gsap.to(flag, { scaleX: 0.86, transformOrigin: '0% 50%', duration: 0.8, ease: 'sine.inOut', yoyo: true, repeat: -1 });
    }
  });
})();
