Unicode text fallback fonts
===========================

Custom Radio Stations can render non-Western wheel text through a generated bitmap texture.
The renderer first checks this Fonts folder for a private font and never installs it into Windows.

Recommended for broad Japanese/CJK coverage:
  NotoSansCJKjp-VF.ttf

Smaller Japanese-focused alternatives are also auto-detected:
  NotoSansJP-Regular.ttf
  NotoSansJP-VF.ttf

The renderer also recognizes the equivalent .otf names, but GDI+ can outline only fonts with
TrueType-compatible outlines. A private font is now probed with the same GraphicsPath operation
used at runtime before it is accepted; unsupported CFF/PostScript-style OpenType fonts are skipped
instead of failing separately for every song title. TrueType (.ttf) remains strongly preferred on
Windows/.NET Framework 4.8.

If no private font is present, the mod tries suitable Windows CJK fonts (Yu Gothic UI, Meiryo,
Microsoft YaHei UI, Malgun Gothic) before falling back to GTA's native text renderer.

GDI+ alternate-font fallback remains enabled inside the generated-bitmap renderer. This means a
whole title still becomes one coherent bitmap even when Windows must obtain an uncommon glyph
from another installed family. The renderer never mixes GTA HUD text with bitmap text inside the
same title.

Noto CJK project:
https://github.com/notofonts/noto-cjk

Noto CJK fonts are distributed under the SIL Open Font License 1.1. If you redistribute a Noto
font with a build, include the corresponding OFL license file from the Noto project.

Renderer sizing notes
---------------------
The Unicode renderer asks GTA for the selected native font slot's line height, then maps the
fallback font's own em/line-spacing metrics onto that height. Each actual string is measured
with its fallback font before the PNG is allocated. Full-width forms, half-width Katakana,
Latin and mixed CJK text therefore use their real glyph advances rather than a fixed character
width. Visual cap-height can differ slightly from GTA's Chalet font because the typefaces are
different, but the line box/scale follows GTA's native text size.

Textures are rasterized according to output height (720p=1x, 1080p=1.5x, 1440p=2x, 4K=3x)
and drawn back through GTA's 720-high scaled UI canvas. Intermediate/windowed heights round up to
the nearest 0.25x density bucket, preventing a new unreclaimable ScriptHookV texture variant for
every single resize pixel while never undersampling the active output. Aspect ratio does not alter
glyph size; 16:9, 21:9 and 32:9 at the same vertical resolution can reuse the same cached texture.
