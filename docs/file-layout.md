# File layout

Periodic notes live under three top-level folders inside `DiaryRoot`:

- `Daily/MM/yyyy-MM-dd.md` — daily notes, grouped by month (e.g. `Daily/08/2026-08-07.md`)
- `Weekly/yyyy-wwW.md` — weekly notes, ISO 8601 week number (e.g. `Weekly/2026-32W.md`)
- `Monthly/yyyy-MM.md` — monthly notes (e.g. `Monthly/2026-08.md`)

Default templates live in `Templates/Daily.md`, `Templates/Weekly.md` and `Templates/Monthly.md`, and are only created when missing. Attachments live in each note folder's `assets` directory and Markdown references use forward slashes.
