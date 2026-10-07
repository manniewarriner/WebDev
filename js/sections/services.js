/* ==========================================================================
   Services section: card hover effects, before/after slider, landmarks marquee
   ========================================================================== */

LWC.onReady(({ gsap, ScrollTrigger, lenis, reducedMotion }) => {
  const root = document.documentElement;
  const servicesSection = document.getElementById('services');
  const landmarksSection = document.querySelector('.landmarks');

  // Guard against missing sections
  if (!servicesSection && !landmarksSection) return;

  /* ========== A. SERVICE PANELS: HOVER EFFECTS ========== */
  if (servicesSection) {
    const panels = servicesSection.querySelectorAll('.services__panel');
    if (panels.length > 0) {
      panels.forEach(panel => {
        // Add smooth transitions and hover tracking
        panel.dataset.hasPanelListeners = 'true';

        // Cleanup function for unmount
        panel._cleanup = () => {
          // No event listeners to clean up currently, but structure for future use
        };
      });
    }
  }

  /* ========== C. LANDMARKS MARQUEE ========== */
  if (landmarksSection && !reducedMotion && ScrollTrigger) {
    const marquee = landmarksSection.querySelector('.landmarks__marquee-track');
    if (marquee) {
      let lastVelocity = 0;
      let tween = null;

      // Initial animation (continuous scroll)
      tween = gsap.to(marquee, {
        x: -marquee.offsetWidth / 2,
        duration: 30,
        ease: 'none',
        repeat: -1,
        modifiers: {
          x: gsap.utils.unitize(x => parseFloat(x) % (marquee.offsetWidth / 2)),
        },
        paused: false,
      });

      // React to scroll velocity
      ScrollTrigger.create({
        trigger: landmarksSection,
        onUpdate(self) {
          if (!tween) return;
          const vel = self.getVelocity() || 0;
          lastVelocity = vel;

          // Modulate animation speed based on scroll velocity
          const speedMultiplier = Math.min(5, 1 + Math.abs(vel) / 600);
          gsap.to(tween, { timeScale: speedMultiplier, duration: 0.2, overwrite: true,
            onComplete: () => gsap.to(tween, { timeScale: 1, duration: 1.2, ease: 'power2.out' }) });
        },
      });

      // Store cleanup
      marquee._cleanup = () => {
        if (tween) tween.kill();
      };
    }
  }

});
