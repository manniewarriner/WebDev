/* ==========================================================================
   Pure-water story: one scroll-driven value (0 → 1) drives the steps, the
   droplet, the TDS gauge counting 300 → 0 ppm, the filter and the final shine.
   ========================================================================== */
(function () {
  LWC.onReady(({ gsap, ScrollTrigger, reducedMotion }) => {
    const section = document.querySelector('#pure-water');
    if (!section) return;

    const story = section.querySelector('.water__story');
    const steps = [...section.querySelectorAll('.water__step')];
    const svg = section.querySelector('.water__svg');
    const ppmEl = section.querySelector('.water__ppm');
    if (!story || !svg || !ppmEl || steps.length !== 4) return;

    const NS = 'http://www.w3.org/2000/svg';
    const q = sel => svg.querySelector(sel);
    const arc = q('.water__gauge-arc');
    const pure = q('.water__drop-pure');
    const filter = q('.water__filter');
    const win = q('.water__window');
    const shine = q('.water__shine');
    const sparkles = q('.water__sparkles');
    const ticks = q('.water__ticks');
    const particlesGroup = q('.water__particles');
    const badge = section.querySelector('.water__badge');
    const bar = section.querySelector('.water__progress span');

    /* ---------- Build ticks + mineral particles ---------- */
    for (let i = 0; i < 60; i++) {
      const a = (i / 60) * Math.PI * 2;
      const r1 = i % 5 === 0 ? 228 : 232, r2 = 240;
      const line = document.createElementNS(NS, 'line');
      line.setAttribute('x1', 260 + Math.cos(a) * r1);
      line.setAttribute('y1', 270 + Math.sin(a) * r1);
      line.setAttribute('x2', 260 + Math.cos(a) * r2);
      line.setAttribute('y2', 270 + Math.sin(a) * r2);
      ticks.appendChild(line);
    }

    let seed = 42;
    const rand = () => ((seed = (seed * 16807) % 2147483647) - 1) / 2147483646;
    const particles = [];
    for (let i = 0; i < 70; i++) {
      const c = document.createElementNS(NS, 'circle');
      c.setAttribute('cx', 165 + rand() * 190);
      c.setAttribute('cy', 150 + rand() * 255);
      c.setAttribute('r', 1.8 + rand() * 3.4);
      c.dataset.rank = rand().toFixed(3); // removed when purity passes this value
      particlesGroup.appendChild(c);
      particles.push(c);
    }

    /* ---------- Render the story at progress p ---------- */
    const clamp01 = v => Math.max(0, Math.min(1, v));
    const smooth = t => t * t * (3 - 2 * t);
    let lastStep = -1, shineDone = false, lockSteps = false;

    function render(p) {
      const step = Math.min(3, Math.floor(p * 4));
      if (!lockSteps && step !== lastStep) {
        steps.forEach((el, i) => {
          el.classList.toggle('is-active', i === step);
          el.classList.toggle('is-done', i < step);
        });
        lastStep = step;
      }

      // 300 ppm until filtration starts, down to 0 by the end of step 3
      const purity = smooth(clamp01((p - 0.25) / 0.5));
      const ppm = Math.round(300 * (1 - purity));
      ppmEl.textContent = ppm;

      arc.setAttribute('stroke-dashoffset', (100 * (1 - purity)).toFixed(2));
      pure.setAttribute('opacity', purity.toFixed(3));
      particles.forEach(c => c.setAttribute('opacity', purity < +c.dataset.rank ? 1 : 0));

      // Filter cartridges slide in during step 2
      const f = clamp01((p - 0.22) / 0.08) * (1 - clamp01((p - 0.68) / 0.08));
      filter.setAttribute('opacity', f.toFixed(3));
      filter.setAttribute('transform', `translate(0 ${(-30 * (1 - f)).toFixed(1)})`);

      // Step 4: the droplet lands on a clean window
      const w = clamp01((p - 0.76) / 0.12);
      win.setAttribute('opacity', w.toFixed(3));
      sparkles.setAttribute('opacity', w.toFixed(3));
      if (badge) {
        badge.style.opacity = w;
        badge.style.transform = `translate(-50%, ${12 * (1 - w)}px)`;
      }
      if (w >= 1 && !shineDone && !reducedMotion) {
        shineDone = true;
        gsap.fromTo(shine, { opacity: 0.5, x: -160 }, { opacity: 0, x: 220, duration: 1.2, ease: 'power2.out' });
        gsap.fromTo(sparkles.children, { scale: 0, transformOrigin: '50% 50%' }, { scale: 1, duration: 0.6, stagger: 0.08, ease: 'back.out(3)' });
      } else if (w < 0.5) {
        shineDone = false;
      }

      if (bar) bar.style.transform = `scaleX(${p})`;
    }

    if (reducedMotion) {
      render(1);
      steps.forEach(el => el.classList.add('is-active'));
      return;
    }

    render(0);

    // Minerals drift gently inside the tap-water droplet
    particles.forEach(c => {
      gsap.to(c, {
        x: () => (rand() - 0.5) * 18, y: () => (rand() - 0.5) * 18,
        duration: 2 + rand() * 2.5, ease: 'sine.inOut', repeat: -1, yoyo: true,
      });
    });

    const mm = gsap.matchMedia();

    mm.add('(min-width: 960px)', () => {
      const st = ScrollTrigger.create({
        trigger: story,
        start: 'top top',
        end: '+=280%',
        pin: true,
        scrub: 0.5,
        anticipatePin: 1,
        onUpdate: self => render(self.progress),
      });
      return () => st.kill();
    });

    mm.add('(max-width: 959px)', () => {
      // Phones: no pinning. All steps are shown; the visual plays through once.
      lockSteps = true;
      steps.forEach(el => { el.classList.add('is-active'); el.classList.remove('is-done'); });
      const state = { p: 0 };
      const tween = gsap.to(state, {
        p: 1, duration: 4.5, ease: 'power1.inOut', paused: true,
        onUpdate: () => render(state.p),
      });
      const st = ScrollTrigger.create({
        trigger: section.querySelector('.water__stage'),
        start: 'top 70%',
        once: true,
        onEnter: () => tween.play(),
      });
      return () => { st.kill(); tween.kill(); lockSteps = false; lastStep = -1; };
    });
  });
})();
