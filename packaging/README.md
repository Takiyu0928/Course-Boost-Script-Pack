# Clean full bundle

`build_full_pack.py` downloads fixed official Autovisor 3.18.4, UnipusAI 3.6, the 学习通 2026-10-25 package and Firefox 158.0.1. Executable archives are checked against pinned SHA256 hashes. It builds a fresh directory, clears account/course settings, and includes upstream source archives and license notices.

`domooc-overlay.zip` is the portable-script overlay from the user-provided domooc package: two signed extensions, their script storage, local Python/Node scripts and documentation. The owner confirmed the extension script storage contains no personal data. Website origins, cookies, passwords, history, sessions and caches were not copied; the local question bank is empty. Its Profile is rebuilt with extension UUIDs and minimal portable preferences; Firefox security browsing/certificate/update defaults are retained. See the enclosed `CLEAN-PACK.md` for first-run checks.

The build never starts a third-party tool, logs in, controls a browser or submits coursework. First-run extension activation and real platform operation still require manual verification.

Run from a Windows checkout with Python 3 and 7-Zip available, after building the launcher:

```powershell
python packaging/build_full_pack.py dist
```

Use a fresh output directory on each build. No local installed tool directories are read by this script. The generated ZIP and checksum live in `dist/`.
