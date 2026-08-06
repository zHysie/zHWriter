# Architecture

`zHWriter.Core` contains models, service interfaces, safe path formatting and template expansion. `zHWriter.Infrastructure` provides LocalAppData settings, exclusive file creation, atomic saves, attachment copies, and a debounced `FileSystemWatcher` calendar index. `zHWriter.App` is the WPF shell: floating editor, calendar, settings and tray integration.

All business writes use a path calculated by `IJournalPathService`; it rejects absolute child patterns and `.`/`..` segments, so normal journal operations cannot leave `DiaryRoot`.
