/* ==========================================================================
   Before / after slider: drag the squeegee (pointer or keys), switch scenes.
   ========================================================================== */
(function () {
  LWC.onReady(({ gsap, ScrollTrigger, reducedMotion }) => {
    const section = document.querySelector('#results');
    if (!section) return;

    const stage = section.querySelector('.results__stage');
    const handle = section.querySelector('.results__handle');
    const tabs = [...section.querySelectorAll('.results__tab')];
    const pill = section.querySelector('.results__tab-pill');
    const scenes = [...section.querySelectorAll('.results__scene')];
    if (!stage || !handle) return;

    const state = { pos: 50 };

    function setPos(value) {
      state.pos = Math.max(0, Math.min(100, value));
      stage.style.setProperty('--pos', `${state.pos}%`);
      const v = Math.round(state.pos);
      handle.setAttribute('aria-valuenow', v);
      handle.setAttribute('aria-valuetext', `${v}% before, ${100 - v}% after`);
    }

    /* ---------- Pointer drag (anywhere on the stage) ---------- */
    let dragging = false;
    const fromEvent = e => {
      const rect = stage.getBoundingClientRect();
      return ((e.clientX - rect.left) / rect.width) * 100;
    };
    stage.addEventListener('pointerdown', e => {
      if (e.button !== 0) return;
      dragging = true;
      gsap?.killTweensOf(state);
      stage.setPointerCapture(e.pointerId);
      setPos(fromEvent(e));
    });
    stage.addEventListener('pointermove', e => { if (dragging) setPos(fromEvent(e)); });
    const stop = () => { dragging = false; };
    stage.addEventListener('pointerup', stop);
    stage.addEventListener('pointercancel', stop);

    /* ---------- Keyboard ---------- */
    handle.addEventListener('keydown', e => {
      const step = e.shiftKey ? 10 : 5;
      const map = { ArrowLeft: -step, ArrowDown: -step, ArrowRight: step, ArrowUp: step };
      if (e.key in map) { e.preventDefault(); setPos(state.pos + map[e.key]); }
      else if (e.key === 'Home') { e.preventDefault(); setPos(0); }
      else if (e.key === 'End') { e.preventDefault(); setPos(100); }
    });

    /* ---------- Scene tabs ---------- */
    function movePill(tab) {
      if (!pill || !tab) return;
      pill.style.width = `${tab.offsetWidth}px`;
      pill.style.transform = `translateX(${tab.offsetLeft - 5}px)`;
    }
    tabs.forEach(tab => {
      tab.addEventListener('click', () => {
        tabs.forEach(t => {
          const on = t === tab;
          t.classList.toggle('is-active', on);
          t.setAttribute('aria-selected', String(on));
        });
        scenes.forEach(s => s.classList.toggle('is-active', s.dataset.scene === tab.dataset.scene));
        movePill(tab);
        if (!reducedMotion && gsap) {
          gsap.fromTo(state, { pos: 85 }, { pos: 50, duration: 1, ease: 'power3.inOut', onUpdate: () => setPos(state.pos) });
        }
      });
      tab.addEventListener('keydown', e => {
        if (e.key !== 'ArrowRight' && e.key !== 'ArrowLeft') return;
        const next = tabs[(tabs.indexOf(tab) + (e.key === 'ArrowRight' ? 1 : tabs.length - 1)) % tabs.length];
        next.focus(); next.click();
      });
    });
    const syncPill = () => movePill(tabs.find(t => t.classList.contains('is-active')));
    syncPill();
    window.addEventListener('resize', syncPill);
    document.fonts?.ready.then(syncPill);

    setPos(50);

    /* ---------- Teach the interaction once ---------- */
    if (!reducedMotion && gsap && ScrollTrigger) {
      ScrollTrigger.create({
        trigger: stage,
        start: 'top 70%',
        once: true,
        onEnter: () => {
          if (dragging) return;
          gsap.timeline({ onUpdate: () => setPos(state.pos) })
            .to(state, { pos: 82, duration: 0.9, ease: 'power2.inOut' })
            .to(state, { pos: 22, duration: 1.3, ease: 'power2.inOut' })
            .to(state, { pos: 50, duration: 0.9, ease: 'power2.out' });
        },
      });
    }
  });
})();
