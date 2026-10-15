# UI design inspiration

The following designs were recreated natively in WPF; design inspiration, no code copied. No React, web runtime, or WebView is included. Provider-logo credits remain in Logos-NOTICE.md.

| Component | Author / handle | Original | Component licence |
| --- | --- | --- | --- |
| Message Thread (Hirael) | Mohammad Shehadeh, @mohammadshehadeh | https://21st.dev/@mohammadshehadeh/components/message-thread | MIT (Hirael, Copyright Mohammad Shehadeh) |
| Animated AI Input (Kokonut UI) | Kokonut Baffier, @kokonutd | https://21st.dev/@kokonutd/components/animated-ai-input | MPL-2.0 (as listed on 21st.dev) |
| Model Selector Prompt | @dqnamo | https://21st.dev/@dqnamo/components/model-selector | MIT |
| Tool Invocation (simple-ai) | @Alwurts | https://21st.dev/@Alwurts/components/tool-invocation | MIT |
| Thinking Orbs | Yogesh, @yogesharc | https://21st.dev/@yogesharc/components/thinking-orbs | MIT |
| Animated Sidebar (Be UI) | Saurabh Chauhan, @starc007 | https://21st.dev/@starc007/components/animated-sidebar | MIT, Copyright 2026 Saurabh Chauhan |

Licence labels are credited to the original designs/libraries. All implementation here is original C#/XAML based on the supplied visual references. Model-profile bars are approximate relative UI hints, not benchmark results or billing quotes.



Native WPF adaptations inspired by these community components (no web runtime):
- Activity Heatmap — Elia Kuratli (kuratlielia), MIT, Copyright (c) 2026 Elia Kuratli: https://21st.dev/@kuratlielia/components/activity-heatmap
- Shiny Button — Hyperiux Vault, MIT: https://21st.dev/@hyperiux/components/shiny-button
- Filters — andrewlu0: https://21st.dev/@andrewlu0/components/filters
- Evervault Card — Manu Arora: https://21st.dev/@manuarora700/components/evervault-card
- Avatar Border — shadcnspace: https://21st.dev/@shadcnspace/components/avatar-border

These implementations were written for OrchDesk using WPF drawing, brushes, controls, and animation. Attribution identifies the visual references; no React source is bundled.


## Redesign v2 references

These are independent native WPF adaptations of supplied screenshots. No web source, React component, or shader source is distributed. The screenshot aliases below identify the supplied reference filenames.

| Screenshot slug | Original reference / author | Licence of reference |
| --- | --- | --- |
| app-shell | [What Saa SApp Shell, Jason Mohab-Ali](https://21st.dev/@jasonmohab-ali/components/whatsaas-app-shell) | Not stated in supplied capture; source not copied |
| command-palette | [Command Palette with Argument Chips, Moumen Soliman](https://21st.dev/@moumensoliman/components/command-palette) | MIT |
| settings-1 | [Profile And Notification Cards, Oliver](https://21st.dev/@olewandowski1/components/settings-1) | MIT |
| integrations-1 | [Connect Integration Cards, Oliver](https://21st.dev/@olewandowski1/components/integrations-1) | MIT-0 |
| toast-gravity-stack | [Toast Gravity Stack, nikolas-sapa](https://21st.dev/@nikolas-sapa/components/toast-gravity-stack) | MIT |
| segmented-control | [Segmented Control, Halaska Studio](https://21st.dev/@halaska-studio/components/segmented-control) | MIT, Copyright Halaska |
| halo-switch | [Halo Switch, cult/ui](https://www.cult-ui.com/docs/components/halo-switch) | MIT (Cult UI) |
| morph-dialog | [Morph Dialog, DHxWhy](https://21st.dev/@dhxwhy/components/morph-dialog) | See original component licence; no source copied |
| amber-aurora | [Light Amber Aurora Shader Background, DHxWhy](https://21st.dev/@dhxwhy/components/light-amber-aurora-shader-background) | MIT |
| skeleton-shimmer | [Skeleton Shimmer, Motion](https://motion.dev/) | MIT (Motion); screenshot-only native adaptation |
| glass-code-block | [Glass Code Block, framecn](https://21st.dev/@framecn/components/glass-code-block) | MIT |
| file-tree | [File Tree, Halaska Studio](https://21st.dev/@halaska-studio/components/file-tree) | MIT, Copyright 2026 Halaska Studio |
| usage-dashboard | [Usage Dashboard, Mohammad Shehadeh](https://21st.dev/@mohammadshehadeh/components/usage-dashboard) | MIT, Copyright 2026 Mohammad Shehadeh |

The `empty-styled`, `bento-01`, and `file-diff` screenshots were used for visual inspiration only. No source or licence is incorporated from those references.

### Bundled fonts

- Geist Mono Regular and Medium: The Geist Project Authors, SIL Open Font License 1.1. Full supplied notice: `Assets/Fonts/OFL.txt`. Original font binaries supplied with the brief are embedded as WPF Resources.
- Inter: Copyright 2016 The Inter Project Authors. SIL Open Font License 1.1: `Assets/Fonts/Inter-OFL.txt`. The complete Inter Variable 4.001 font shipped with Blender 5.2 was decoded from WOFF2 and instantiated at weights 400/500. Static family names and variation metadata were normalized for the WPF resolver; outlines were not redrawn. Files: `Inter-Regular.ttf`, `Inter-Medium.ttf`.
- Zodiak: original bundled Fontshare Free Font Licence retained at `Assets/Fonts/Zodiak-FFL.txt`.
