# Bundled UI fonts

Drop the font files (`.ttf` or `.otf`) for the families below into **this folder**. The build embeds
every `Fonts\*.ttf` / `Fonts\*.otf` as a WPF `Resource` (see `WaffleMeter.App.Wpf.csproj`), and
`FontFamilyConverter` resolves the user's chosen family from the bundle first, then an installed system
font, then `Malgun Gothic` / `Segoe UI`. So a font renders as soon as its file is here, and the overlay
degrades gracefully (Korean stays readable via Malgun Gothic) until then — no system install needed.

The file name does **not** matter; what matters is the font's **internal family name**, which must equal
the value the app stores (the settings `fontFamily` value, shown in the 표시 settings tab). If a bundled
font doesn't apply, its internal family name probably differs — check it (e.g. right‑click → Properties,
or any font viewer) and, if needed, tell me the exact name so the dropdown value can match.

| Settings value (family name) | License | Where to get the .ttf/.otf |
|---|---|---|
| `Pretendard` | SIL OFL 1.1 | https://github.com/orioncactus/pretendard (releases → `Pretendard-*.ttf`/`.otf`) |
| `Spoqa Han Sans Neo` | SIL OFL 1.1 | https://github.com/spoqa/spoqa-han-sans (releases → TTF) |
| `Freesentation` | SIL OFL 1.1 | https://github.com/yangheeryu/Freesentation (or the official 국립국어원/release TTF) |
| `NEXON Lv2 Gothic` | NEXON free font license (free for commercial use) | NEXON 폰트 공식 페이지 |
| `Tmoney Round Wind` (티머니 둥근바람) | Tmoney custom free license (free incl. commercial; no resale/modification) | 티머니 공식 폰트 페이지 |

Extra-bold "(EX)" weights — four families ship a heavier extra-bold, exposed in the dropdown as
`<name> (EX)`. The settings value is the name WPF resolves the bundled face by (verified per file):

| Settings value | File | Resolves via |
|---|---|---|
| `NEXON Lv2 Gothic Bold` | `NEXON Lv2 Gothic Bold.ttf` | Win32 family name |
| `Spoqa Han Sans Neo Bold` | `SpoqaHanSansNeo-Bold.ttf` | Win32 family name |
| `Freesentation 7 Bold` | `Freesentation-7Bold.ttf` | Win32 family name |
| `Pretendard Bold` | `Pretendard-Bold.otf` | family+face (its Win32 family is the shared `Pretendard`) |

Notes
- A single regular weight per family is enough for the overlay; add more weights only if needed.
- `Malgun Gothic` is always available on Windows and is the safe fallback — it is also offered in the
  dropdown as the no-bundle default.
- Licenses: the three OFL 1.1 fonts may be embedded/redistributed freely. NEXON Lv2 Gothic and Tmoney
  Round Wind are free (including commercial) under their own licenses; keep their license text with the
  app if you redistribute. These notices are why the files are not auto-downloaded into the repo.
