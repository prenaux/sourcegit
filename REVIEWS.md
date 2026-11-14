# Local reviews

Reviews retain findings without archiving unrelated code. Open REVIEWS in the
repository sidebar, use Reviews in Local changes, or choose Review changes from
the History context menu for a commit or two comparison endpoints.

## Open a source

Start a commit review or a comparison between two selected commits from History.
In Local changes, the Reviews menu provides Review Staged Changes, Review
Unstaged Changes, and Review All Local Changes. Its button occupies a separate
column to the right of the search field; the clear-search icon remains inside
the search field. Starting a review does not save it by itself.

A commit is compared with its first parent, or the empty tree for an initial
commit. Revision comparisons show the net difference between the selected
endpoints. Unstaged and all-local sources include untracked files that Git does
not ignore. Reviews manages saved reviews and snapshots rather than providing
another source-creation form. Without saved work, it shows an empty state and
does not automatically start a local source.

The file list loads paths and status before file contents. Selecting a file
loads its diff asynchronously. Selecting another file cancels obsolete loading.
Unreviewed local files are live previews and reload when selected again. Opening
or browsing a preview does not create review files.

History, the saved-review selector, and the snapshot selector reconstruct the
same complete available file list. Selecting an annotated file loads its full
source diff, including sections without comments. Browsing diffs are separate
from persisted comment excerpts; loading a complete diff does not save it.

The comparison label uses Staged changes, Unstaged changes, or All local changes
for local sources. Commit sources show selectable revision endpoints. The
selected file's absolute path is selectable above the diff. File-list context
menus provide Copy Path for the repository-relative path and Copy Full Path for
the absolute path. Long list paths truncate from the left and align right to
preserve filenames.

The text filter matches repository-relative paths. All files, Reviewed, and
Unreviewed filters combine with it. Reviewed means at least one nonempty comment,
including fixed or dismissed comments. Unreviewed means no nonempty comments.
The selected file stays visible while its comment editor is active; membership
updates when the editor closes, so adding a first comment or removing the last
comment does not interrupt typing.

A comment records the selected file's identity. Code already associated with
review work does not reload silently. Update snapshot
refreshes the source and migrates existing findings. Source-change checks are
limited to reviewed and selected files, rather than hashing every working file.
If the original Git source is unavailable, or current local contents no longer
match recorded code, the viewer explicitly labels its limited recorded-context
fallback. Existing findings remain readable. An explicit snapshot update loads
current local changes and migrates findings instead of silently changing their
code or attaching them to unverified lines.

## Write the review

General Review uses the commit-message editor. Its first line is the review
subject; a blank line separates the subject from the body. The section can be
collapsed. Commit and comparison subjects supply the initial subject and appear
in the snapshot selector with revision and date information.

Comment records the current line when no text is selected. Comment on selection
records the selected source range with surrounding context. Hovering a commented
line offers Edit comment instead of creating another finding. Add file comment
records a finding about the file without capturing its entire diff. General
review text and comments use the same editor as commit messages. Comments omit
commit-subject decorations. Typing preserves text, blank lines, and caret state.
Closing an empty or whitespace-only comment removes it.

Comment states are fixed:

- tofix is the default and identifies work that requires a change.
- comment records an observation or discussion without requesting a change.
- fixed identifies a finding that has been addressed.
- dismissed identifies a finding that requires no action.

Files have no separate manual reviewed mark or progress state. Removing the last
nonempty comment makes a file unreviewed. Mark remaining comments fixed affects
comment and tofix entries in the selected file. Set all comments applies the
selected state to every comment in that file, including comments outside the
current diff. Undo restores the preceding states.

## Navigate and update

A section is a contiguous block of changed lines highlighted by diff navigation.
One Git patch hunk can contain multiple sections. Reviews provides first, last,
and previous/next boundary controls with a section position indicator. History
and Local changes retain their existing navigation behavior.

Review shortcuts are active only inside Reviews:

- Ctrl+M comments on the selection, or edits the finding on the current line.
  Without a selection or existing finding, it comments on the current line.
- Ctrl+Down moves to the current section's end, then the next section's start,
  then its end. Ctrl+Up follows those boundaries in reverse.
- Adding Shift extends the selection through the same boundaries without moving
  its original anchor. The end boundary includes the section's final line break.
- Enter focuses the selected comment's editor when the comments list has focus.
- Escape closes the comment editor, cancels relocation, and returns focus to the
  diff. Enter inside an editor continues to insert a newline.

The comment shortcut also accepts Command on macOS. Boundary navigation does not
intercept normal text navigation inside a comment or general-review editor.

Comment actions appear at the target's top, clamped to the visible viewport. The
editor remains attached to the commented code as the diff scrolls. It moves
inside the viewport when the target is only partly visible or near the bottom.
Changing the diff selection or scrolling the target completely out of view closes
the editor without discarding nonempty comments. Mixed targets display both old
and new source ranges.

Updating preserves comment IDs and workflow states. Unambiguous unchanged ranges
migrate using their recorded context. Edited or ambiguous ranges show Needs
attention. Findings without a current diff location show
Outside current diff. Neither condition resolves a finding automatically.

Recovery actions appear only when applicable. Move comment to selected lines
starts relocation: select its destination and choose Attach comment here. Show
original commented code opens the recorded version in which the finding was
added. Each recorded version retains its own comments and placements.

## Storage and copying

Active review data lives in the repository's `_reviews/` directory. Each review
has two files under `<review_id>/`:

- `review.md` presents the saved subject, general notes, findings, states,
  locations, and relevant code context as readable text.
- `review.xml` retains compact metadata, anchors, and history for actual review
  work. The application uses this file when reopening the review.

Untouched files are not persisted. A line comment retains bounded surrounding
context, even inside a large patch hunk. Selecting a complete section retains
that section and its surrounding context. General-only reviews do not capture file data. Full source files and per-file
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
inspection instead of being deleted. Obsolete manual file marks are ignored on
load and omitted on save; comments, subjects, and general notes are retained.
The legacy reader is used only for this one-time conversion; subsequent opens
use the compact metadata. Conversion errors
leave the original data available and appear in the review status.

Review capture excludes both active review storage and its conversion archive.
The application does not change ignore rules or stage these files. XML and
Markdown writes use atomic replacement. Binary and metadata-only changes support
file comments. Local sources require a resolved index; edits during a selected
file capture cause a retry or a visible error.
