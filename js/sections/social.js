/* ==========================================================================
   Lincoln Window Cleaner: reviews carousel + areas map + FAQ accordion
   ========================================================================== */
LWC.onReady(({ gsap, ScrollTrigger, lenis, reducedMotion }) => {
  const sectionRoot = document.querySelector('section.reviews, section.areas, section.faq');
  if (!sectionRoot) return; // section not present on this page

  /* ===== CAROUSEL (Reviews) ===== */
  const carousel = document.querySelector('.reviews__carousel');
  const track = carousel?.querySelector('.reviews__track');
  const slides = carousel?.querySelectorAll('.reviews__slide');
  const prevBtn = carousel?.querySelector('.reviews__btn--prev');
  const nextBtn = carousel?.querySelector('.reviews__btn--next');

  if (carousel && track && slides.length > 0) {
    let currentIndex = 0;
    let autoPlayTimer = null;
    let isPaused = false;

    const scrollToSlide = (index) => {
      currentIndex = (index + slides.length) % slides.length;
      const slide = slides[currentIndex];
      if (track.scrollTo && !reducedMotion) {
        track.scrollTo({ left: slide.offsetLeft - track.offsetLeft, behavior: 'smooth' });
      } else {
        track.scrollLeft = slide.offsetLeft - track.offsetLeft;
      }
      updateButtons();
    };

    const updateButtons = () => {
      prevBtn?.setAttribute('aria-label', `Previous review (${currentIndex + 1} of ${slides.length})`);
      nextBtn?.setAttribute('aria-label', `Next review (${currentIndex + 2 > slides.length ? 1 : currentIndex + 2} of ${slides.length})`);
    };

    const startAutoPlay = () => {
      if (reducedMotion || isPaused) return;
      autoPlayTimer = setInterval(() => {
        scrollToSlide(currentIndex + 1);
      }, 6000);
    };

    const stopAutoPlay = () => {
      clearInterval(autoPlayTimer);
      autoPlayTimer = null;
    };

    const pauseAutoPlay = () => {
      isPaused = true;
      stopAutoPlay();
    };

    const resumeAutoPlay = () => {
      isPaused = false;
      startAutoPlay();
    };

    // Event listeners
    prevBtn?.addEventListener('click', () => {
      pauseAutoPlay();
      scrollToSlide(currentIndex - 1);
    });
    nextBtn?.addEventListener('click', () => {
      pauseAutoPlay();
      scrollToSlide(currentIndex + 1);
    });

    // Keyboard navigation
    carousel.addEventListener('keydown', (e) => {
      if (e.key === 'ArrowLeft') {
        pauseAutoPlay();
        scrollToSlide(currentIndex - 1);
        e.preventDefault();
      }
      if (e.key === 'ArrowRight') {
        pauseAutoPlay();
        scrollToSlide(currentIndex + 1);
        e.preventDefault();
      }
    });

    // Pause on hover/focus
    carousel.addEventListener('mouseenter', pauseAutoPlay);
    carousel.addEventListener('mouseleave', resumeAutoPlay);
    carousel.addEventListener('focusin', pauseAutoPlay);
    carousel.addEventListener('focusout', resumeAutoPlay);

    // Intersection observer to pause when off-screen
    const observer = new IntersectionObserver((entries) => {
      if (entries[0].isIntersecting && !isPaused) startAutoPlay();
      else stopAutoPlay();
    }, { threshold: 0.5 });
    observer.observe(carousel);

    updateButtons();
    if (!reducedMotion) startAutoPlay();
  }

  /* ===== AREAS MAP ANIMATION ===== */
  const mapWrap = document.querySelector('.areas__map-wrap');
  const svg = mapWrap?.querySelector('.areas__map');
  const pinLincoln = svg?.querySelector('.areas__pin--lincoln');
  const dotsGroup = svg?.querySelector('.areas__dots');
  const pulse = svg?.querySelector('.areas__pulse');

  if (mapWrap && svg && pinLincoln && dotsGroup && pulse && !reducedMotion) {
    // Pop in dots with stagger
    const dots = dotsGroup.querySelectorAll('.areas__dot');
    gsap.set(dots, { opacity: 0, scale: 0, transformOrigin: '50% 50%' });
    gsap.to(dots, {
      opacity: 1,
      scale: 1,
      duration: 0.6,
      stagger: 0.1,
      scrollTrigger: { trigger: mapWrap, start: 'top 70%', once: true },
    });

    // Pulse ring expanding from Lincoln
    gsap.set(pulse, { opacity: 0 });
    ScrollTrigger.create({
      trigger: mapWrap,
      start: 'top 70%',
      once: true,
      onEnter: () => {
        // Repeat pulse animation
        const tl = gsap.timeline({ repeat: -1 });
        tl.fromTo(pulse, { attr: { r: 20 }, opacity: 0.9 }, { attr: { r: 190 }, opacity: 0, duration: 2.4, ease: 'power2.out' });
      },
    });
  }

  /* ===== FAQ ACCORDION ===== */
  const faqItems = document.querySelectorAll('.faq__item');

  if (faqItems.length > 0) {
    faqItems.forEach((item) => {
      const toggle = item.querySelector('.faq__toggle');
      const content = item.querySelector('.faq__content');

      if (toggle && content) {
        toggle.addEventListener('click', () => {
          const isOpen = item.classList.contains('is-open');

          if (isOpen) {
            // Close
            item.classList.remove('is-open');
            content.hidden = true;
            toggle.setAttribute('aria-expanded', 'false');
          } else {
            // Close all others
            faqItems.forEach((otherItem) => {
              otherItem.classList.remove('is-open');
              otherItem.querySelector('.faq__content').hidden = true;
              otherItem.querySelector('.faq__toggle').setAttribute('aria-expanded', 'false');
            });

            // Open this one
            item.classList.add('is-open');
            content.hidden = false;
            toggle.setAttribute('aria-expanded', 'true');
            toggle.focus();
          }
        });

        // Keyboard support
        toggle.addEventListener('keydown', (e) => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            toggle.click();
          }

          // Arrow navigation
          if (e.key === 'ArrowDown') {
            e.preventDefault();
            const nextItem = item.nextElementSibling;
            if (nextItem) {
              nextItem.querySelector('.faq__toggle')?.focus();
            }
          }

          if (e.key === 'ArrowUp') {
            e.preventDefault();
            const prevItem = item.previousElementSibling;
            if (prevItem) {
              prevItem.querySelector('.faq__toggle')?.focus();
            }
          }
        });
      }
    });
  }
});
