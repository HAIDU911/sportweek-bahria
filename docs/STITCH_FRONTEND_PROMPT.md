# SportsWeek Manager — Google Stitch Frontend Prompt Pack

**How to use:** Paste **PART A** into Stitch first (it sets the design system for the whole project). Then paste each **PART B** screen prompt one at a time, in order. If Stitch drifts, tell it: "Keep the same design system as Part A." Export to Figma/HTML and hand it back; the React + TypeScript build follows these screens 1:1.

---

## PART A — Design system & product brief (paste first)

**Product:** SportsWeek Manager — the official platform for Bahria University's annual sports week. A public scoreboard for students, a workspace for team captains, and a **read-only administrator console** for viewing users and participants. Web app, responsive (desktop 1440, tablet 834, mobile 390). Design desktop and mobile for every screen.

**Personality:** reliable, energetic, transparent. Headlines are direct, never filler. Example copy: "The campus board, live." / "Every result stays on record."

**Visual direction — warm editorial sports board (strict):**
- Background: warm paper off-white `#F4F0E6`. Cards: `#FBF9F3` with a 1.5px ink border `#111111` and a hard 4px offset shadow (`4px 4px 0 #111`) on interactive cards. No soft blurry shadows, no glassmorphism, no gradients.
- Ink: `#111111` for text, borders and all primary buttons.
- Accents: electric lime `#C6F432` (live, success, progress, highlights) and signal orange `#FF6B1A` (alerts, "today", attention). Use accents as flat blocks, tags and underline marks — never as page backgrounds.
- Status styles (always icon + text label, never colour alone): Live = lime fill + pulsing dot; Pending = paper with dashed ink border; Approved = ink fill, white text; Rejected = orange outline; Withdrawn = grey `#8A8678` with strike-through.
- **No dark mode. No yellow input or hover states.** Buttons and inputs are black/ink; on hover/focus/active they stay dark with white text and gain a lime 3px focus ring. Never reduce contrast on interaction. WCAG AA minimum.
- Typography: display = bold condensed grotesque ("Anton" or "Archivo Narrow Bold"), uppercase for hero headings and scores; UI/body = "Inter" or "Space Grotesk"; numerals = "JetBrains Mono" (tabular). Scale: Hero 72/64, H1 48, H2 32, H3 24, body 16, small 13. Scores are huge (48–96px) — the scoreboard is the hero.
- Shape: 6px radius controls, 12px cards; thick 1.5px ink dividers; ticket-stub / scoreboard motifs; diagonal-stripe texture only on empty states.
- Icons: Lucide line icons, 1.75px stroke.
- Motion (as annotations): restrained — 200ms ease-out page entry, card lift on hover (translate -2px), live-dot pulse, filter-chip slide, confirmation drawers slide in from the right. All motion off under reduced-motion.
- Reusable components, defined once: top nav, footer, page header with breadcrumb, filter chips, segmented control, search field, score card, fixture row, bracket node, team card, stat tile, leaderboard row, data table (collapses to cards on mobile), pagination, tabs, side drawer, modal dialog, toast, status badge, empty state, error state with Retry, skeleton loader, form field (label/help/error), role badge, avatar with initials.
- Accessibility: visible focus rings, a label on every input, keyboard-operable tables and drawers, no browser alert/prompt/confirm — use in-page dialogs and drawers.

**States every data screen must show (as separate frames):** loading skeleton · empty ("Nothing here yet" + next action) · error with Retry · dependency-unavailable ("Scoreboard is temporarily offline — results are safe, retry in a moment") · not-found.

**Navigation:**
- Public top nav: logo "SPORTSWEEK / BAHRIA" · Sports · Tournaments · Fixtures · Leaderboards · Teams · [Continue with Google]. A pulsing "LIVE NOW (n)" pill appears only when the API reports live matches.
- Captain: left rail (desktop) / bottom tab bar (mobile) — Overview · My Teams · My Fixtures · Notifications (unread count).
- Admin Console: ink left sidebar (same ink/paper/lime palette — not a dark mode) — Dashboard · Users · Participants · Teams · Tournaments · Departments · Audit Log · Exports. A persistent orange badge reads "READ-ONLY ADMIN".

---

## PART B — Screens (paste one at a time)

### B1. Public home `/`
Hero: huge uppercase headline "THE CAMPUS BOARD, LIVE." with sub-copy "Fixtures, scores and standings for Bahria University Sports Week. No login needed." Right side: stacked "Live now" score-card carousel (2 cards) with team names, giant monospaced score, sport tag, venue, lime pulsing LIVE dot. Below: 4 stat tiles (Sports · Teams approved · Matches played · Departments competing). Then "Today's fixtures" (time, sport icon, teams, venue, status badge), "Latest results" (3 compact score cards), "Department standings" top-5 with lime points bars, and a "Sports" grid (Tug of War, Cricket, Futsal, Chess, Badminton, Table Tennis) with registration-state tags. Footer: "Every result stays on record." Also include the no-live-match state ("No match is live right now. Next kick-off: 14:30 Futsal — Court 2.").

### B2. Sports `/sports` and `/sports/:id`
List: grid of sport cards (icon, name, team count, tournament status chip). Detail: header with sport name, rules-summary accordion, tabs [Tournaments · Leaderboard · Rules], tournament cards with registration state, dates, and team count/limit progress bar.

### B3. Tournaments `/tournaments` and `/tournaments/:id`
List with filter chips (All · Registration open · In progress · Completed) and a sport select. Detail: header (name, sport, dates, timezone, venues, status), tab bar [Overview · Fixtures · Standings · Teams · Statistics]. Overview shows next match, a progress ring "12 of 31 matches complete", and a bracket preview.

### B4. Fixtures & bracket `/tournaments/:id/fixtures`
Segmented control **List / Bracket**. List: grouped by date → round; fixture rows with time, teams, score, venue, status badge. Bracket: horizontal single-elimination tree with round labels ("Round of 16 / Quarter-final / Semi-final / Final"), bye nodes drawn as dashed "BYE — advances", winner underlined in lime, third-place match separated below. Mobile: bracket scrolls horizontally with sticky round headers. Filters: round, date, status, team search.

### B5. Standings & leaderboards `/tournaments/:id/standings`, `/leaderboards`
Overall department leaderboard (rank, department, points, medals, wins) with bar visuals, plus per-sport tabs. Table on desktop, cards on mobile.

### B6. Public teams `/teams`
Searchable directory of **approved** teams only: team name, department, sport, tournament. No contact info, no enrollment numbers, no rosters — small note "Player details are private." Pagination.

### B7. Login / Register `/login`, `/register`, `/auth/callback`, `/verify-email`
Split screen: left editorial panel with scoreboard collage; right a single large black "Continue with Google" button, helper text "We use Google to confirm you're a real person. Gmail accounts are accepted." **No password fields anywhere.** Include: callback loading ("Confirming your Google account…"), failure states (account not allowed / email not verified, with a clear next step), and verification-pending with a resend button that has a 60-second cooldown and truthful states: Queued · Sent · Failed · Unavailable.

### B8. Captain overview `/captain`
Greeting with Google avatar and verification badge. Cards: "Open tournaments you can register for", "Your teams" with status badges, "Next fixture" countdown, "Unread notifications". Rejection reasons shown inline in orange on rejected teams.

### B9. Captain — register a team `/captain/teams`
Stepper: 1 Pick tournament → 2 Team details (name, department) → 3 Roster (searchable list; counter "7 / 5–11") → 4 Review & submit. Field-level validation, a duplicate-player error message, busy state on submit (spinner, disabled to block double-click), values preserved after a recoverable error. Success state: "Pending approval" badge.

### B10. Captain — fixtures & notifications `/captain/fixtures`, `/captain/notifications`
Fixtures: only the captain's own teams — schedule, venue, countdown, result. Notifications: timeline with type icons (approved, rejected, upcoming match, reschedule, result, withdrawal), unread dot, "Mark all read".

### B11. Admin — Dashboard `/workspace/dashboard`
**Read-only.** Top row of stat tiles: Total users · Active captains · Teams (Pending / Approved / Rejected / Withdrawn) · Total participants · Tournaments · Email failures (orange if > 0). Flat ink + lime charts: registrations per day (bars), participants per department (horizontal bars), team-status donut, users by role. "Recent sign-ins" and "Recent audit events" feeds. Alerts strip ("3 teams pending > 48h", "2 email deliveries failed"). Every number labelled with freshness ("Updated 14:02:11").

### B12. Admin — Users `/workspace/users`
Table: avatar, name, **masked email** (`ha***@gmail.com`), role badge (Captain / Scorer / Stakeholder / Admin), verified status, teams owned, last sign-in, created. Toolbar: search, role chips, verified filter, date range, "Export CSV" (opens a confirm drawer explaining the export is audited). Row click → right-side **User detail drawer**: identity (Google subject truncated), role and **how it was assigned** ("Server allowlist"), teams owned, recent activity, and a "Reveal full email" button that opens a dialog "This view is recorded in the audit log." Mobile: rows become cards. **No edit or delete buttons anywhere** — muted note "Read-only console."

### B13. Admin — Participants `/workspace/participants`
Player-level directory: name, department, enrollment (masked `****-1234`), team, sport, tournament, team status, captain. Filters: tournament, sport, department, team status, search. Summary strip: "412 participants · 38 teams · 9 departments". Row → participant drawer (team, tournament, fixtures played). "Export CSV" (audited).

### B14. Admin — Teams `/workspace/teams` and detail
Table with status tabs (All · Pending · Approved · Rejected · Withdrawn), captain, department, roster count, submitted date, queue-age indicator (orange after 48h). Detail: roster table, status timeline, rejection reason, fixtures. Read-only.

### B15. Admin — Tournaments `/workspace/tournaments`
Cards/table: sport, name, lifecycle, registration state, dates, teams n/limit progress, fixtures-complete %. Detail: read-only bracket and schedule by venue (timeline lanes).

### B16. Admin — Departments `/workspace/departments`
Participation analytics: per-department participants, teams, wins, points, plus a registrations sparkline. Compare toggle for two departments.

### B17. Admin — Audit log `/workspace/audit`
Append-only table: timestamp (UTC + campus time), actor (name + role), action (e.g. `admin.user.viewed`, `admin.export.participants`), entity, correlation ID, result. Filters: actor, action, date, entity. Row expands to sanitized metadata. Banner: "Audit events cannot be edited or deleted."

### B18. Admin — Exports `/workspace/exports`
History of exports (who, what, when, row count, masked/unmasked) and a "New export" form: dataset (Users / Participants / Teams / Audit), filters, and an "Include sensitive fields" toggle that requires a reason and shows a warning dialog.

### B19. Cross-cutting frames
Forbidden (403) page; session-expired dialog; rate-limited (429) toast with retry-after; dependency-unavailable banner; 404; mobile nav drawer; confirmation-drawer pattern; toast variants (success, error, info).

**Deliverables from Stitch:** every screen above in desktop + mobile, a component sheet, and the colour/typography tokens exactly as specified.
