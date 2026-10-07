(function () {
  'use strict';

  LWC.onReady(({ gsap, ScrollTrigger, lenis, reducedMotion }) => {
    const section = document.querySelector('#quote');
    if (!section) return;

    /* ===== Pricing model (PLACEHOLDER: confirm prices with client) ===== */
    const PRICING = {
      // PLACEHOLDER: confirm prices with client
      windows: {
        flat: 12,
        terraced: 15,
        semi: 18,
        detached: 24,
        large: 32,
      },
      // Typical window counts per property
      typicalWindows: {
        flat: 6,
        terraced: 8,
        semi: 10,
        detached: 14,
        large: 20,
      },
      // PLACEHOLDER: confirm prices with client
      gutters: {
        flat: 45,
        terraced: 50,
        semi: 60,
        detached: 80,
        large: 110,
      },
      // PLACEHOLDER: confirm prices with client (flat, terraced, semi, detached, large)
      fascia: {
        flat: 50,
        terraced: 60,
        semi: 70,
        detached: 90,
        large: 120,
      },
      // PLACEHOLDER: confirm prices with client
      conservatory: 40,
      // Frequency multipliers apply to window cleaning only: PLACEHOLDER: confirm with client
      frequency: {
        oneoff: 1.6,
        '4weekly': 1.0,
        '8weekly': 1.1,
      },
      perWindowOverTypical: 0.75, // PLACEHOLDER: confirm with client
    };

    /* ===== Form elements ===== */
    const form = section.querySelector('#quote-form');
    const propertyRadios = form.querySelectorAll('input[name="property"]');
    const windowsStepper = form.querySelectorAll('.quote-form__step-btn');
    const windowsSlider = form.querySelector('input[name="windows"]');
    const windowsCount = form.querySelector('.quote-form__window-count');
    const serviceCheckboxes = form.querySelectorAll('input[name="services"]');
    const frequencyRadios = form.querySelectorAll('input[name="frequency"]');
    const nameInput = form.querySelector('input[name="name"]');
    const postcodeInput = form.querySelector('input[name="postcode"]');
    const contactRadios = form.querySelectorAll('input[name="contact"]');

    const estimatePrice = section.querySelector('.quote__estimate-price');
    const breakdownList = section.querySelector('.quote__breakdown-list');
    const whatsappLink = section.querySelector('.quote__cta-whatsapp');
    const serviceError = section.querySelector('.quote-form__error');

    // Guard against missing elements
    if (!form || !estimatePrice || !breakdownList) return;

    /* ===== State ===== */
    let currentPrice = 25; // Default display
    let priceAnimation = null;

    /* ===== Helper: parse form values ===== */
    function getFormValues() {
      const property = form.querySelector('input[name="property"]:checked')?.value || 'flat';
      const windows = Math.max(4, Math.min(40, parseInt(windowsSlider.value) || 10));
      const services = Array.from(serviceCheckboxes)
        .filter(cb => cb.checked)
        .map(cb => cb.value);
      const frequency = form.querySelector('input[name="frequency"]:checked')?.value || 'oneoff';
      const name = nameInput?.value?.trim() || '';
      const postcode = postcodeInput?.value?.trim() || '';
      const contact = form.querySelector('input[name="contact"]:checked')?.value || 'whatsapp';

      return { property, windows, services, frequency, name, postcode, contact };
    }

    /* ===== Pricing calculation ===== */
    function calculatePrice() {
      const { property, windows, services, frequency } = getFormValues();

      // Validate services
      if (services.length === 0) {
        serviceError.style.display = 'block';
        return null;
      }
      serviceError.style.display = 'none';

      let basePrice = 0;

      // Windows: base + extra windows, scaled by how often
      if (services.includes('windows')) {
        basePrice += windowsPriceFor(property, windows, frequency);
      }

      // Other services
      if (services.includes('gutters')) {
        basePrice += PRICING.gutters[property] || 45;
      }
      if (services.includes('fascia')) {
        basePrice += PRICING.fascia[property] || 60;
      }
      if (services.includes('conservatory')) {
        basePrice += PRICING.conservatory;
      }

      return {
        basePrice,
        finalPrice: basePrice,
        frequency,
      };
    }

    function windowsPriceFor(property, windows, frequency) {
      const windowsBase = PRICING.windows[property] || 12;
      const typicalCount = PRICING.typicalWindows[property] || 6;
      const extra = Math.max(0, windows - typicalCount);
      const mult = PRICING.frequency[frequency] || 1.0;
      return (windowsBase + extra * PRICING.perWindowOverTypical) * mult;
    }

    const money = n => `£${Math.round(n)}`;

    /* ===== Update estimate display ===== */
    function updateEstimate() {
      const { property, services } = getFormValues();

      // Commercial properties show "Bespoke quote"
      if (property === 'commercial') {
        estimatePrice.textContent = 'Bespoke quote';
        breakdownList.innerHTML = '<li>Contact for custom pricing</li>';
        updateWhatsAppMessage(null, null);
        return;
      }

      const result = calculatePrice();
      if (!result) {
        estimatePrice.textContent = 'From £25';
        breakdownList.innerHTML = '<li style="opacity: 0.5;">Add services to see estimate</li>';
        return;
      }

      const { finalPrice } = result;

      // Calculate range (±10%, rounded to £5)
      const low = Math.round((finalPrice * 0.9) / 5) * 5;
      const high = Math.round((finalPrice * 1.1) / 5) * 5;

      const newPrice = finalPrice;

      // Display final range immediately, but trigger shine animation if available
      estimatePrice.textContent = `£${low}–£${high}`;

      // Trigger shine sweep on summary card if available
      if (!reducedMotion && gsap) {
        const summaryCard = section.querySelector('.quote__summary');
        if (summaryCard) {
          gsap.fromTo(summaryCard,
            { boxShadow: 'inset 0 0 0 1px rgba(34,195,238,0.6), 0 0 40px -10px rgba(34,195,238,0.6)' },
            { boxShadow: 'inset 0 0 0 1px rgba(255,255,255,0.14), 0 0 0 0 rgba(34,195,238,0)', duration: 0.9, ease: 'power2.out', clearProps: 'boxShadow' });
        }
      }

      currentPrice = newPrice;

      // Update breakdown
      const breakdownItems = [];
      if (services.includes('windows')) {
        const { windows: windowCount, frequency: freq } = getFormValues();
        const windowsPrice = windowsPriceFor(property, windowCount, freq);
        breakdownItems.push({
          label: `Windows (${windowCount})`,
          price: windowsPrice,
        });
      }
      if (services.includes('gutters')) {
        breakdownItems.push({
          label: 'Gutter vacuuming',
          price: PRICING.gutters[property],
        });
      }
      if (services.includes('fascia')) {
        breakdownItems.push({
          label: 'Fascia & soffit',
          price: PRICING.fascia[property],
        });
      }
      if (services.includes('conservatory')) {
        breakdownItems.push({
          label: 'Conservatory roof',
          price: PRICING.conservatory,
        });
      }

      // Render breakdown
      breakdownList.innerHTML = breakdownItems
        .map(
          item =>
            `<li><span>${item.label}</span><span class="quote__breakdown-price">${money(item.price)}</span></li>`
        )
        .join('');

      // Update frequency note
      const { frequency } = result;
      let freqLabel = '';
      if (frequency === '4weekly') freqLabel = ' (every 4 weeks)';
      else if (frequency === '8weekly') freqLabel = ' (every 8 weeks)';

      if (freqLabel) {
        const li = document.createElement('li');
        li.innerHTML = `<span style="font-size: 0.8rem; opacity: 0.7;">Frequency: ${frequency === '4weekly' ? 'Every 4 weeks' : 'Every 8 weeks'}</span>`;
        breakdownList.appendChild(li);
      }

      // Update WhatsApp button with new price
      updateWhatsAppMessage(low, high);
    }

    /* ===== Generate WhatsApp message ===== */
    function updateWhatsAppMessage(low, high) {
      const { property, windows, services, frequency, name, postcode, contact } = getFormValues();

      const propertyLabels = {
        flat: 'Flat/Apartment',
        terraced: 'Terraced',
        semi: 'Semi-detached',
        detached: 'Detached',
        large: 'Large detached',
        commercial: 'Commercial',
      };

      const serviceLabels = {
        windows: 'Window cleaning',
        gutters: 'Gutter vacuuming',
        fascia: 'Fascia & soffit',
        conservatory: 'Conservatory roof',
      };

      const frequencyLabels = {
        oneoff: 'One-off',
        '4weekly': 'Every 4 weeks',
        '8weekly': 'Every 8 weeks',
      };

      const serviceList = services.map(s => serviceLabels[s]).join(' + ');
      const priceText = low === null || high === null ? 'Bespoke quote' : `£${low}–£${high}`;

      let message = `Hi Florida! I'd like a quote:\n`;
      message += `${propertyLabels[property]}, ${windows} windows\n`;
      message += `${serviceList}\n`;
      message += `${frequencyLabels[frequency]}\n`;
      message += `Estimate shown: ${priceText}`;

      if (name) message += `\nName: ${name}`;
      if (postcode) message += `\nArea: ${postcode}`;

      if (whatsappLink) {
        whatsappLink.href = LWC.whatsapp(message);
      }
    }

    /* ===== Event listeners ===== */

    // Property type changes
    propertyRadios.forEach(radio => {
      radio.addEventListener('change', () => {
        // Micro-interaction: scale + glow on selected card
        propertyRadios.forEach(r => {
          const card = r.closest('.quote-form__radio-card');
          if (r.checked && card) {
            if (!reducedMotion && gsap) {
              gsap.to(card.querySelector('.quote-form__card-inner'), {
                scale: 1.04,
                duration: 0.3,
                yoyo: true,
                repeat: 1,
                ease: 'back.out',
              });
            }
          }
        });
        updateEstimate();
      });
    });

    // Windows stepper
    windowsStepper.forEach(btn => {
      btn.addEventListener('click', e => {
        e.preventDefault();
        const currentValue = parseInt(windowsSlider.value) || 10;
        const isMinus = btn.classList.contains('quote-form__step-minus');
        const newValue = Math.max(4, Math.min(40, isMinus ? currentValue - 1 : currentValue + 1));
        windowsSlider.value = newValue;
        windowsCount.textContent = newValue;
        // Update slider background
        updateSliderBackground();
        updateEstimate();
      });
    });

    // Windows slider
    if (windowsSlider) {
      windowsSlider.addEventListener('input', e => {
        let value = parseInt(e.target.value) || 10;
        value = Math.max(4, Math.min(40, value));
        e.target.value = value;
        windowsCount.textContent = value;
        updateSliderBackground();
        updateEstimate();
      });
    }

    // Services checkboxes
    serviceCheckboxes.forEach(checkbox => {
      checkbox.addEventListener('change', () => {
        // Micro-interaction on checkbox select
        const card = checkbox.closest('.quote-form__checkbox-card');
        if (checkbox.checked && card && !reducedMotion && gsap) {
          gsap.to(card.querySelector('.quote-form__card-inner'), {
            scale: 1.04,
            duration: 0.3,
            yoyo: true,
            repeat: 1,
            ease: 'back.out',
          });
        }
        updateEstimate();
      });
    });

    // Frequency changes
    frequencyRadios.forEach(radio => {
      radio.addEventListener('change', updateEstimate);
    });

    // Name and postcode inputs (for WhatsApp message)
    if (nameInput) nameInput.addEventListener('input', () => updateEstimate());
    if (postcodeInput) postcodeInput.addEventListener('input', () => updateEstimate());

    /* ===== Update slider background gradient ===== */
    function updateSliderBackground() {
      if (!windowsSlider) return;
      const value = (windowsSlider.value - 4) / (40 - 4) * 100;
      windowsSlider.style.setProperty('--value', `${value}%`);
    }

    /* ===== Ripple effect on service card click (optional animation) ===== */
    if (!reducedMotion && gsap) {
      serviceCheckboxes.forEach(checkbox => {
        const card = checkbox.closest('.quote-form__checkbox-card');
        if (card) {
          card.addEventListener('click', e => {
            const rect = card.getBoundingClientRect();
            const x = e.clientX - rect.left;
            const y = e.clientY - rect.top;

            const ripple = document.createElement('span');
            ripple.style.position = 'absolute';
            ripple.style.width = '8px';
            ripple.style.height = '8px';
            ripple.style.left = x + 'px';
            ripple.style.top = y + 'px';
            ripple.style.borderRadius = '50%';
            ripple.style.background = 'rgba(34, 195, 238, 0.6)';
            ripple.style.pointerEvents = 'none';
            ripple.style.zIndex = 10;
            card.style.position = 'relative';
            card.style.overflow = 'hidden';
            card.appendChild(ripple);

            gsap.to(ripple, {
              width: '60px',
              height: '60px',
              left: x - 30 + 'px',
              top: y - 30 + 'px',
              opacity: 0,
              duration: 0.6,
              ease: 'power2.out',
              onComplete() {
                ripple.remove();
              },
            });
          });
        }
      });
    }

    /* ===== Panel reveal animation ===== */
    const panel = section.querySelector('.quote__panel');
    if (panel && !reducedMotion && gsap && ScrollTrigger) {
      // Stagger the calculator and summary
      const calculator = panel.querySelector('.quote__calculator');
      const summary = panel.querySelector('.quote__summary');

      if (calculator && summary) {
        gsap.set([calculator, summary], { opacity: 0, y: 32, scale: 0.96 });
        gsap.to([calculator, summary], {
          opacity: 1,
          y: 0,
          scale: 1,
          duration: 0.8,
          stagger: 0.15,
          ease: 'power3.out',
          scrollTrigger: {
            trigger: panel,
            start: 'top 75%',
            once: true,
          },
        });
      }
    }

    /* ===== Validation: at least one service ===== */
    form.addEventListener('submit', e => {
      if (e.target === form) {
        const values = getFormValues();
        if (values.services.length === 0) {
          e.preventDefault();
          serviceError.style.display = 'block';
          serviceError.setAttribute('role', 'alert');
        }
      }
    });

    /* ===== Initial state ===== */
    updateSliderBackground();
    updateEstimate();
  });
})();
