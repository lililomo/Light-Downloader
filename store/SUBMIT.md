# How to submit the extensions

Packages are produced by `build-extensions.ps1` into `dist/`:
- `dist/light-downloader-chrome.zip`  → Chrome Web Store
- `dist/light-downloader-firefox.zip` → Firefox AMO

Listing text, permission justifications and the privacy policy are in this folder
(`STORE-LISTING.md`, `PERMISSIONS.md`, `PRIVACY.md`).

---

## Chrome Web Store

1. One-time: register a developer account at
   https://chrome.google.com/webstore/devconsole (US$5 one-time fee).
2. **New item** → upload `dist/light-downloader-chrome.zip`.
3. Fill the listing from `STORE-LISTING.md` (name, summary, description, category).
4. **Privacy practices** tab:
   - Paste each permission justification from `PERMISSIONS.md`.
   - Set the data-use disclosures (collects cookies etc., not sold/shared).
   - Add the **privacy policy URL**: https://beliaja.id/light-downloader/privacy/
   - State the single purpose.
5. Add the ready-made assets:
   - **Store icon:** `store/icons/chrome-store-icon-128.png` (128×128 with the recommended padding).
   - **Screenshots (1280×800):** `store/screenshots/store-1280x800-1-main.png`,
     `-2-progress.png`, `-3-save.png`.
   - **Small promo tile (440×280):** `store/icons/promo-small-440x280.png`.
   - **Marquee (1400×560, optional):** `store/icons/promo-marquee-1400x560.png`.
6. Submit for review. Broad host access + cookies means it may get a longer review;
   the justifications in `PERMISSIONS.md` cover it.

## Firefox Add-ons (AMO)

1. One-time: create an account at https://addons.mozilla.org/developers/ (free).
2. **Submit a New Add-on** → *On this site* → upload `dist/light-downloader-firefox.zip`.
3. Choose self-distribution or listed (listed = public on AMO).
4. Fill the listing from `STORE-LISTING.md`; privacy policy URL
   https://beliaja.id/light-downloader/privacy/, homepage
   https://beliaja.id/light-downloader/extension/.
   Icon: `store/icons/icon-128.png` (or `icon-64.png`); screenshots: `store/screenshots/store-1280x800-*.png`.
5. Paste `PERMISSIONS.md` into **Notes to reviewer**.
6. Submit. AMO runs automated validation first (see `web-ext lint`), then human review.

### Notes
- The add-on id is set in `manifest.json`
  (`browser_specific_settings.gecko.id`). Keep it stable across versions.
- Bump `version` in **both** manifests for every update.
- The extensions are useless without the desktop app running; mention that in the
  listing so reviewers/users aren't confused when "nothing happens".
- Once a listing is live, put its store URL in `public/light-downloader/assets/toko.js`
  on beliaja.id (green-1); the sales and support pages show "segera hadir" until then.
