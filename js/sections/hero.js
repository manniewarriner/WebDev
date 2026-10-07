/* ==========================================================================
   Hero: a grimy window over the Lincoln skyline. A squeegee wipes it clean in
   three S-strokes as you scroll (pinned on desktop, auto-played on mobile).
   ========================================================================== */
(function () {
  LWC.onReady(({ gsap, ScrollTrigger, reducedMotion }) => {
    const hero = document.querySelector('.hero');
    if (!hero || reducedMotion) return;

    const canvas = hero.querySelector('.hero__grime');
    const squeegee = hero.querySelector('.hero__squeegee');
    const sparkle = hero.querySelector('.hero__sparkle');
    const dropsLayer = hero.querySelector('.hero__droplets');
    const hint = hero.querySelector('.hero__hint');
    const layers = hero.querySelectorAll('.hero__layer');
    if (!canvas || !squeegee) return;

    const ctx = canvas.getContext('2d');
    const source = document.createElement('canvas'); // the grime, drawn once per size
    const sctx = source.getContext('2d');
    const AUTO = 0.1; // share of the wipe that plays by itself on load
    const isMobile = window.matchMedia('(max-width: 719px)').matches;

    let W = 0, H = 0, dpr = 1, blade = 0;
    let path = [];      // [{x0,y0,x1,y1,len,dir}]
    let total = 0;
    let shown = -1;     // progress currently drawn
    let isClean = false;
    const state = { auto: 0, scroll: 0 };

    /* ---------- Seeded random + value noise ---------- */
    function rng(seed) {
      return () => {
        seed = (seed * 16807) % 2147483647;
        return (seed - 1) / 2147483646;
      };
    }

    function noiseField(gw, gh, rand) {
      const grid = [];
      for (let i = 0; i < (gw + 1) * (gh + 1); i++) grid.push(rand());
      const at = (x, y) => grid[y * (gw + 1) + x];
      const smooth = t => t * t * (3 - 2 * t);
      return (u, v) => {
        const x = u * gw, y = v * gh;
        const xi = Math.min(gw - 1, Math.floor(x)), yi = Math.min(gh - 1, Math.floor(y));
        const tx = smooth(x - xi), ty = smooth(y - yi);
        const a = at(xi, yi), b = at(xi + 1, yi), c = at(xi, yi + 1), d = at(xi + 1, yi + 1);
        return a + (b - a) * tx + (c - a) * ty + (a - b - c + d) * tx * ty;
      };
    }

    /* ---------- Paint the grime once ---------- */
    function paintGrime() {
      const rand = rng(1337);
      const cw = source.width, ch = source.height;
      sctx.setTransform(1, 0, 0, 1, 0, 0);
      sctx.clearRect(0, 0, cw, ch);

      // 1. Misty film
      sctx.fillStyle = 'rgba(222, 229, 236, 0.66)';
      sctx.fillRect(0, 0, cw, ch);

      // 2. Smudgy dirt clouds: low-res fbm noise, stretched smooth
      const lw = Math.max(64, Math.round(W / 6)), lh = Math.max(40, Math.round(H / 6));
      const low = document.createElement('canvas');
      low.width = lw; low.height = lh;
      const lctx = low.getContext('2d');
      const img = lctx.createImageData(lw, lh);
      const octaves = [noiseField(5, 4, rand), noiseField(12, 9, rand), noiseField(30, 22, rand)];
      for (let y = 0; y < lh; y++) {
        for (let x = 0; x < lw; x++) {
          const u = x / lw, v = y / lh;
          let n = octaves[0](u, v) * 0.55 + octaves[1](u, v) * 0.3 + octaves[2](u, v) * 0.15;
          n = Math.max(0, n - 0.42) / 0.58;          // keep only the darker patches
          const bottom = 0.35 + 0.65 * v;            // dirt settles toward the bottom
          const i = (y * lw + x) * 4;
          img.data[i] = 118; img.data[i + 1] = 108; img.data[i + 2] = 92;
          img.data[i + 3] = Math.round(255 * Math.min(0.42, n * 0.55 * bottom + 0.04));
        }
      }
      lctx.putImageData(img, 0, 0);
      sctx.imageSmoothingEnabled = true;
      sctx.imageSmoothingQuality = 'high';
      sctx.drawImage(low, 0, 0, cw, ch);

      sctx.scale(dpr, dpr);

      // 3. Old lazy wipe arcs
      sctx.lineCap = 'round';
      for (let i = 0; i < 5; i++) {
        sctx.strokeStyle = `rgba(255,255,255,${0.06 + rand() * 0.06})`;
        sctx.lineWidth = 40 + rand() * 50;
        sctx.beginPath();
        const cx = rand() * W, cy = H * (0.4 + rand() * 0.8), r = 200 + rand() * 300;
        sctx.arc(cx, cy, r, Math.PI * 1.1, Math.PI * 1.1 + 0.9 + rand() * 0.6);
        sctx.stroke();
      }

      // 4. Dried water spots (rings), the classic dirty-window look
      const spots = Math.round((W * H) / 5200);
      for (let i = 0; i < spots; i++) {
        const x = rand() * W, y = rand() * H, r = 1.5 + rand() * rand() * 9;
        sctx.beginPath();
        sctx.arc(x, y, r, 0, Math.PI * 2);
        sctx.fillStyle = `rgba(150, 140, 125, ${0.05 + rand() * 0.08})`;
        sctx.fill();
        sctx.lineWidth = 0.8 + rand();
        sctx.strokeStyle = `rgba(250, 250, 248, ${0.25 + rand() * 0.3})`;
        sctx.stroke();
      }

      // 5. Dust speckles
      const specks = Math.round((W * H) / 600);
      for (let i = 0; i < specks; i++) {
        const x = rand() * W, y = rand() * H;
        sctx.fillStyle = `rgba(70, 62, 52, ${0.18 + rand() * 0.35})`;
        sctx.beginPath();
        sctx.arc(x, y, 0.35 + rand() * rand() * 1.6, 0, Math.PI * 2);
        sctx.fill();
      }

      // 6. Rain streaks running down
      for (let i = 0; i < Math.round(W / 45); i++) {
        let x = rand() * W, y = rand() * H * 0.5;
        const len = 80 + rand() * H * 0.5;
        const g = sctx.createLinearGradient(0, y, 0, y + len);
        g.addColorStop(0, 'rgba(110,100,85,0)');
        g.addColorStop(0.7, `rgba(110,100,85,${0.12 + rand() * 0.12})`);
        g.addColorStop(1, 'rgba(110,100,85,0.28)');
        sctx.strokeStyle = g;
        sctx.lineWidth = 1.5 + rand() * 3;
        sctx.beginPath();
        sctx.moveTo(x, y);
        for (let s = 0; s < len; s += 18) { x += (rand() - 0.5) * 3; sctx.lineTo(x, y + s); }
        sctx.stroke();
      }
    }

    /* ---------- The squeegee route: three S-strokes ---------- */
    function buildPath() {
      blade = (H / 3) * 1.16;
      const out = blade / 2 + 40;
      const rows = [H / 6, H / 2, (H * 5) / 6];
      const pts = [
        [-out, rows[0], 1], [W + out, rows[0], 1],
        [W + out, rows[1], 0], [-out, rows[1], -1],
        [-out, rows[2], 0], [W + out, rows[2], 1],
      ];
      path = []; total = 0;
      for (let i = 1; i < pts.length; i++) {
        const [x0, y0] = pts[i - 1], [x1, y1, dir] = pts[i];
        const len = Math.hypot(x1 - x0, y1 - y0);
        path.push({ x0, y0, x1, y1, len, dir: dir || (x1 > x0 ? 1 : -1), moving: dir !== 0 });
        total += len;
      }
    }

    function pointAt(p) {
      let d = p * total;
      for (const seg of path) {
        if (d <= seg.len) {
          const t = seg.len ? d / seg.len : 0;
          return { x: seg.x0 + (seg.x1 - seg.x0) * t, y: seg.y0 + (seg.y1 - seg.y0) * t, seg };
        }
        d -= seg.len;
      }
      const last = path[path.length - 1];
      return { x: last.x1, y: last.y1, seg: last };
    }

    /* ---------- Draw the glass at progress p ---------- */
    function draw(p) {
      p = Math.max(0, Math.min(1, p));
      if (Math.abs(p - shown) < 0.0005) return;
      shown = p;

      ctx.setTransform(1, 0, 0, 1, 0, 0);
      ctx.globalCompositeOperation = 'source-over';
      ctx.clearRect(0, 0, canvas.width, canvas.height);
      ctx.drawImage(source, 0, 0);

      // Erase everything the blade has passed
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
      ctx.globalCompositeOperation = 'destination-out';
      ctx.lineWidth = blade;
      ctx.lineCap = 'butt';
      ctx.lineJoin = 'round';
      ctx.beginPath();
      let d = p * total;
      ctx.moveTo(path[0].x0, path[0].y0);
      for (const seg of path) {
        if (d <= 0) break;
        const t = Math.min(1, d / seg.len);
        ctx.lineTo(seg.x0 + (seg.x1 - seg.x0) * t, seg.y0 + (seg.y1 - seg.y0) * t);
        d -= seg.len;
      }
      ctx.stroke();

      ctx.globalCompositeOperation = 'source-over';
      const at = pointAt(p);

      // Place the squeegee: blade at the point, handle trailing behind it
      const scale = blade / 300;
      const visible = at.seg.moving && p > 0.001 && p < 0.999;
      gsap.set(squeegee, {
        x: at.x - 10, y: at.y - 150,
        scaleX: scale * (at.seg.dir > 0 ? -1 : 1), scaleY: scale,
        rotation: at.seg.dir > 0 ? -4 : 4,
        opacity: visible ? 1 : 0,
      });

      if (p >= 0.995 && !isClean) setClean(true);
      else if (p < 0.98 && isClean) setClean(false);
      if (hint && p > AUTO + 0.05) hint.classList.add('is-gone');
    }

    function setClean(clean) {
      isClean = clean;
      gsap.to(canvas, { opacity: clean ? 0 : 1, duration: clean ? 0.8 : 0.3, overwrite: true });
      if (sparkle) {
        sparkle.classList.remove('is-on');
        if (clean) { void sparkle.offsetWidth; sparkle.classList.add('is-on'); }
      }
      if (clean) startDroplets();
    }

    /* ---------- Droplets on the fresh glass ---------- */
    let dropsTl = null;
    function startDroplets() {
      if (dropsTl || !dropsLayer) return;
      dropsTl = gsap.timeline();
      const n = isMobile ? 4 : 10;
      const rand = rng(99);
      for (let i = 0; i < n; i++) {
        const drop = document.createElement('span');
        drop.className = 'hero__drop';
        const size = 6 + rand() * 9;
        drop.style.setProperty('--s', `${size}px`);
        drop.style.setProperty('--trail', `${40 + rand() * 120}px`);
        drop.style.left = `${5 + rand() * 90}%`;
        dropsLayer.appendChild(drop);
        const dur = 5 + rand() * 7;
        dropsTl.fromTo(drop, { y: -30 - rand() * H * 0.4 }, {
          y: H + 200, duration: dur, ease: 'power1.in', repeat: -1, repeatDelay: rand() * 4,
        }, rand() * 3);
      }
      new IntersectionObserver(([entry]) => {
        if (entry.isIntersecting) dropsTl.play(); else dropsTl.pause();
      }).observe(hero);
    }

    /* ---------- Sizing ---------- */
    function resize() {
      const rect = hero.getBoundingClientRect();
      W = Math.round(rect.width); H = Math.round(rect.height);
      dpr = Math.min(2, window.devicePixelRatio || 1);
      canvas.width = source.width = Math.round(W * dpr);
      canvas.height = source.height = Math.round(H * dpr);
      buildPath();
      paintGrime();
      shown = -1;
      draw(current());
    }

    const current = () => Math.max(state.auto, AUTO + (1 - AUTO) * state.scroll);

    resize();
    let resizeTimer;
    window.addEventListener('resize', () => {
      clearTimeout(resizeTimer);
      resizeTimer = setTimeout(() => {
        if (Math.round(hero.getBoundingClientRect().width) !== W) resize();
      }, 200);
    });

    /* ---------- Intro: copy rises in behind the glass ---------- */
    gsap.from(hero.querySelectorAll('.hero__content > *'), {
      y: 40, opacity: 0, duration: 1.2, stagger: 0.09, ease: 'power4.out', delay: 0.1,
    });

    /* ---------- Drive the wipe ---------- */
    if (isMobile) {
      // No pinning on phones: the whole wipe plays once by itself
      gsap.to(state, {
        auto: 1, duration: 3.4, delay: 0.7, ease: 'power1.inOut',
        onUpdate: () => draw(current()),
      });
    } else {
      gsap.to(state, {
        auto: AUTO, duration: 1.5, delay: 0.6, ease: 'power2.inOut',
        onUpdate: () => draw(current()),
      });

      const tl = gsap.timeline({
        scrollTrigger: {
          trigger: hero,
          start: 'top top',
          end: '+=140%',
          pin: true,
          scrub: 0.6,
          anticipatePin: 1,
        },
      });
      tl.to(state, { scroll: 1, ease: 'none', duration: 1, onUpdate: () => draw(current()) }, 0);
      layers.forEach(layer => {
        const depth = parseFloat(layer.dataset.depth || 0);
        tl.to(layer, { yPercent: -depth * 6, ease: 'none', duration: 1 }, 0);
      });
      // A short hold on the clean view before the pin releases
      tl.to({}, { duration: 0.15 });
    }

    // Skyline drifts as the hero scrolls away
    gsap.to(hero.querySelector('.hero__skyline'), {
      yPercent: 12, ease: 'none',
      scrollTrigger: { trigger: hero, start: isMobile ? 'top top' : 'bottom bottom', end: 'bottom top', scrub: true },
    });
  });
})();
