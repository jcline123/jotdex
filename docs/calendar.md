# Calendar sync

Settings → Calendar copies open todos onto a CalDAV calendar. iCloud is the default (`https://caldav.icloud.com/`). Another CalDAV server works if it accepts the same kind of sign-in.

## Sign-in

Use your Apple ID and an **app-specific password** from [appleid.apple.com](https://appleid.apple.com) (Sign-In and Security → App-Specific Passwords). That is not the password you use to sign in to Apple. Jotdex stores it with the other app secrets (DPAPI, and `secrets-portable.json` in a move kit). It is not written into notes and the settings API never sends it back.

Find calendars, then pick the one that should receive todos. Shared calendars stay unused unless you select them.

## What lands on the calendar

Each open todo is one all-day event titled `todo:` followed by the todo text. That prefix is how Jotdex recognizes its own items and items you add from the phone.

- No due date, due today, or due in the past: the event is on **today**. The next sync after midnight moves it to the new day if the todo is still open.
- A due date in the future stays on that date.
- Completing or deleting the todo removes the event.
- A todo that lives in a note includes a link to that note when **Jotdex address** is set (for example `jotdex.cline.xyz`). The link opens `/?note=` for that note.

## Adding a todo from the phone

Create an **all-day** event on the calendar you selected and title it `todo:` plus the task, such as `todo: Pick up parts`. The next sync adds it to the todo list with a Calendar chip, the same kind of chip a note todo uses for its note. Deleting that event removes the todo. Checking it off in Jotdex removes the event.

Timed events are left alone, even if the title starts with `todo:`.

Jotdex does not write calendar ids into the note body beyond the todo line’s own comment. The same todo always updates the same event, so a later sync replaces it instead of duplicating it.

Sync runs about every ten minutes while it is turned on, and again shortly after a note or todo change. **Sync now** runs immediately.

If CalDAV is slow or unreachable, Jotdex logs a warning (see `data/logs/jotdex-yyyyMMdd.log`), backs off, and keeps serving the app. A failed sync does not stop the server. `/api/health` includes the last calendar sync time and error.
