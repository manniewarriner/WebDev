"""Rebuild the review cards from real, public reviews on the business's Bark profile.
Source: https://www.bark.com/en/gb/b/lincoln-window-cleaner/eQB60/ (checked 7 Oct 2026, 22 reviews, all 5 stars).
Text is verbatim; names are shortened to first name + initial."""
import pathlib, re, html

BARK = "https://www.bark.com/en/gb/b/lincoln-window-cleaner/eQB60/"
REVIEWS = [  # (name, date, text, featured on homepage)
 ("Stefan Bryan", "Aug 2026", "⭐⭐⭐⭐⭐ Highly recommend Lincoln Window Cleaner! Florida provides a brilliant, professional service and always does a fantastic job. The windows are left spotless every time, and the service is reliable, friendly and great value for money.", True),
 ("Amy Zac", "Apr 2025", "Contacted Lincoln Window Cleaner today about a small job at my business premises. Not only did they quote within minutes, they then managed to squeeze us in within a few hours! Amazing fast and efficient service, thank you!", True),
 ("Emma Stevens", "Jul 2024", "Florida came today and managed to get my very difficult to get to dirty windows and frames sparkling clean. Thank you so much. Highly recommend.", True),
 ("Luce Townrow", "Sep 2026", "Excellent customer service, lovely ladies, and high quality window cleaning, highly recommend 😊", True),
 ("Nicky Jelley", "Oct 2024", "A great job completed at very short notice on a Sunday!! Fantastic service and great communication. Prices very competitive too.", True),
 ("Samantha Williams", "May 2026", "Florida and her team not only clean the windows at my place of work, but she will also be cleaning mine. She does a fantastic job and i highly recommend to anyone.", True),
 ("Lai Ma", "Apr 2026", "Florida did an excellent job with our window cleaning. They were punctual, left everything spotless, and offered a very reasonable price.", True),
 ("Kakta Dovidas", "Jun 2026", "Great window cleaning service – very professional, quick, and spotless results. No streaks, highly recommended!", True),
 ("Grigas Mantas", "Apr 2026", "I'm very pleased with the window cleaning service. The team is reliable, professional, and consistently leaves my windows spotless every time.", False),
 ("Sandra Kaktienė", "Jun 2026", "Excellent window cleaning service! Friendly, reliable, and left all my windows sparkling clean. Highly recommend!", False),
 ("Vitalija Bukauskiene", "Apr 2025", "Great service, fast and professional job done on Wednesday. Thank you Florida and your team. Highly recommend to anyone needing windows done.", False),
 ("Magdalena Magda", "Jun 2025", "Great service, professional approach and job well done. Highly recommend to anyone needing windows cleaned.", False),
 ("Egidijus Tarnavicius", "Apr 2026", "Thanks, my windows never been cleaner", False),
 ("Danni Prks", "Sep 2026", "Lovely customer service - would recommend!", False),
 ("Gemma Gazi", "Jul 2026", "Excellent service and very friendly - would definitely recommend.", False),
 ("Dave Kelly", "Oct 2024", "Great service and professional job done today, highly recommend to anyone needing windows done.", False),
 ("Kuniauskytė Simona", "Jun 2024", "polite, friendly, professional and reliable✨️", False),
 ("Rolandas Varzinskas", "Jul 2024", "the best recommendations, fast and serious work", False),
 ("Madalina Tanasa", "Jun 2025", "I strongly recommend. Really good job xx", False),
 ("Lauren Timmis", "Aug 2025", "Did a great job, would definitely recommend", False),
 ("Amanda Wright", "Jul 2024", "Great service would highly recommend", False),
 ("Ricardas Ivaskevicius", "Jul 2024", "Professional, good quality", False),
]
COUNT = len(REVIEWS)

def short(n):
    a = n.split(); return f"{a[0]} {a[-1][0]}." if len(a) > 1 else a[0]

def card(n, d, t):
    init = n[0].upper()
    return f'''<div class="glass-card reviews__card">
              <div class="reviews__card-top"><span class="reviews__card-stars" aria-label="5 out of 5 stars">★★★★★</span><span class="reviews__date">{d}</span></div>
              <p class="reviews__text">{html.escape(t, quote=False)}</p>
              <div class="reviews__who"><span class="reviews__avatar" aria-hidden="true">{init}</span><span><span class="reviews__author">{html.escape(short(n))}</span><span class="reviews__source">Verified review · Bark</span></span></div>
            </div>'''

def slides(cls, items):
    return "\n".join(f'''          <div class="{cls}">
            {card(*r[:3])}
          </div>''' for r in items)

root = pathlib.Path(__file__).resolve().parent.parent

# Homepage carousel
p = root / "parts/social.html"; s = p.read_text(encoding="utf-8")
s = re.sub(r'(<div class="reviews__track">\n).*?(\n        </div>\n      </div>\n\n      <!-- Carousel controls -->)',
           lambda m: m.group(1) + slides("reviews__slide", [r for r in REVIEWS if r[3]]) + m.group(2), s, flags=re.S)
s = re.sub(r'<div class="reviews__meta">.*?</div>',
           f'<div class="reviews__meta">\n        <p><strong>{COUNT} five-star reviews</strong></p>\n        <p class="muted">Every review on <a href="{BARK}" target="_blank" rel="noopener">Bark</a> is 5 stars</p>\n      </div>', s, count=1, flags=re.S)
s = s.replace('<a href="reviews.html" class="btn btn--ghost">Leave a review</a>', f'<a href="reviews.html" class="btn btn--ghost">Read all {COUNT} reviews</a>')
p.write_text(s, encoding="utf-8")

# Reviews page grid (all)
p = root / "reviews.html"; s = p.read_text(encoding="utf-8")
s = re.sub(r'(<div class="reviews__grid" data-reveal-stagger>\n).*?(\n        </div>\n      </div>\n    </section>)',
           lambda m: m.group(1) + slides("reviews__grid-item", REVIEWS) + m.group(2), s, count=1, flags=re.S)
s = re.sub(r'<p class="lead" style="max-width: 600px; margin-top: 1.5rem;">.*?</p>',
           f'<p class="lead" style="max-width: 640px; margin-top: 1.5rem;">{COUNT} reviews, all five stars, from homes and businesses around Lincoln. Copied word for word from our <a href="{BARK}" target="_blank" rel="noopener">Bark profile</a>.</p>', s, count=1, flags=re.S)
p.write_text(s, encoding="utf-8")
print("reviews:", COUNT)
