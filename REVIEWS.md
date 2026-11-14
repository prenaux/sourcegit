# Local reviews

Reviews retain findings and reviewed-file progress without archiving unrelated
code. Open REVIEWS in the repository sidebar, use Reviews in Local changes, or
choose Review changes from the History context menu for a commit or two
comparison endpoints.

## Open a source

Choose a commit, revision comparison, staged changes, unstaged changes, or all
local changes. A commit is compared with its first parent, or the empty tree for
an initial commit. Revision comparisons show the net difference between the
selected endpoints. Unstaged and all-local sources include untracked files that
Git does not ignore.

The file list loads paths and status before file contents. Selecting a file
loads its diff asynchronously. Selecting another file cancels obsolete loading.
Unreviewed local files are live previews and reload when selected again. Opening
or browsing a preview does not create review files.

A comment or explicit reviewed mark records the selected file's identity. Code
already associated with review work does not reload silently. Update snapshot
refreshes the source and migrates existing findings. Source-change checks are
limited to reviewed and selected files, rather than hashing every working file.

## Write the review

General Review uses the commit-message editor. Its first line is the review
subject; a blank line separates the subject from the body. The section can be
collapsed. Commit and comparison subjects supply the initial subject and appear
in the snapshot selector with revision and date information.

Comment on hunk records the current hunk. Comment on lines records the selected
line or range with surrounding context. Add file comment records a finding about
the file without capturing its entire diff. Comment text is top-aligned and
saved automatically. Closing an empty or whitespace-only comment removes it.

Comment states are fixed:

- tofix is the default and identifies work that requires a change.
- comment records an observation or discussion without requesting a change.
- fixed identifies a finding that has been addressed.
- dismissed identifies a finding that requires no action.

Mark file reviewed is independent of comment state. It records the file's
identity and progress, not a complete source backup. Mark remaining comments
fixed affects comment and tofix entries in the selected file. Set all comments
applies the selected state to every comment in that file, including comments
outside the current diff. Undo restores the preceding states.

## Navigate and update

The diff toolbar provides first, previous, next, and last hunk navigation and a
hunk position indicator. Review shortcuts are active only inside Reviews:

- Ctrl+M adds a comment on the current hunk.
- Ctrl+Shift+M adds a comment on the current line or selected range.
- Ctrl+Up and Ctrl+Down navigate hunks when a comment or general-text editor does
  not have focus.
- Enter focuses the selected comment's editor when the comments list has focus.
- Escape closes the comment editor or cancels moving a comment.

The comment shortcuts also accept Command on macOS. Enter inside an editor
continues to insert a newline.

Updating preserves comment IDs and workflow states. Unchanged files retain their
reviewed marks; changed reviewed files show Changed since review. Unambiguous
unchanged ranges migrate using their recorded context. Edited or ambiguous
ranges show Needs attention. Findings without a current diff location show
Outside current diff. Neither condition resolves a finding automatically.

Recovery actions appear only when applicable. Move comment to selected lines
starts relocation: select its destination and choose Attach comment here. Show
original commented code opens the recorded version in which the finding was
added. Each recorded version retains its own comments and placements.

## Storage and copying

Active review data lives in the repository's `_reviews/` directory. Each review
has two files under `<review_id>/`:

- `review.md` presents the saved subject, general notes, findings, states,
  locations, relevant code context, and reviewed-file progress as readable text.
- `review.xml` retains compact metadata, anchors, and history for actual review
  work. The application uses this file when reopening the review.

Untouched files are not persisted. A line comment retains bounded surrounding
context, even inside a large hunk. A hunk comment retains the selected hunk.
General-only reviews do not capture file data. Full source files and per-file
snapshot documents are not written. Saved context remains readable without the
original Git objects or current working files.

Copy Review copies the subject, general notes, and active comment and tofix
findings. Each finding precedes its location and relevant code context. It does
not copy unrelated files or the entire comparison. Code blocks are partial diff
context, not complete patches to apply. Fixed and dismissed findings remain in
the saved Markdown but are omitted from the clipboard text. Uncertain findings
include an explicit warning and their original context.

The application converts the earlier snapshot-directory format when opening an
existing review. It preserves findings, general text, workflow states, and
recorded context, then writes the compact format. After successful conversion,
old generated snapshots and full-source caches move to `_toremove/_reviews/` for
inspection instead of being deleted. The legacy reader is used only for this
one-time conversion; subsequent opens use the compact metadata. Conversion errors
leave the original data available and appear in the review status.

Review capture excludes both active review storage and its conversion archive.
The application does not change ignore rules or stage these files. XML and
Markdown writes use atomic replacement. Binary and metadata-only changes support
file comments. Local sources require a resolved index; edits during a selected
file capture cause a retry or a visible error.
