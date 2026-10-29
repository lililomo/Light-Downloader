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
   - Add the **privacy policy URL** (host `PRIVACY.md`).
   - State the single purpose.
5. Add assets (you provide these):
   - At least one **screenshot** 1280×800 or 640×400.
   - Store icon 128×128 (already in the package).
   - Optional promo tile 440×280.
6. Submit for review. Broad host access + cookies means it may get a longer review;
   the justifications in `PERMISSIONS.md` cover it.

## Firefox Add-ons (AMO)

1. One-time: create an account at https://addons.mozilla.org/developers/ (free).
2. **Submit a New Add-on** → *On this site* → upload `dist/light-downloader-firefox.zip`.
3. Choose self-distribution or listed (listed = public on AMO).
4. Fill the listing from `STORE-LISTING.md`; add the privacy policy URL.
5. Paste `PERMISSIONS.md` into **Notes to reviewer**.
6. Submit. AMO runs automated validation first (see `web-ext lint`), then human review.

### Notes
- The add-on id is set in `manifest.json`
  (`browser_specific_settings.gecko.id`). Keep it stable across versions.
- Bump `version` in **both** manifests for every update.
- The extensions are useless without the desktop app running; mention that in the
  listing so reviewers/users aren't confused when "nothing happens".
