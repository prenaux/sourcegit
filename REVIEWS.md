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

The file list is the same change tree used by Local changes, with Tree, Grid,
and List modes, a remembered view mode, and remembered folder expansion. The
rounded search field matches repository-relative paths and combines with All
files, Reviewed, Unreviewed, Comment, To fix, Fixed, and Dismissed. A file
matches a comment-state filter when at least one nonempty comment has that state.
The list status icon follows the captured patch, so a deleted file shows the
deleted icon and an added file shows the added icon before its content loads.
Each file row shows one review mark after its name: a warning glyph and the open
count when the file has tofix findings, a check glyph when it has comments and
no tofix findings, and no mark when it has no comments. Folder rows never show a
review mark, and folders do not aggregate their children's marks.

The file tree loads paths and status before file contents. Selecting a file
loads its diff asynchronously. Selecting another file cancels obsolete loading.
Unreviewed local files are live previews and reload when selected again. Opening
or browsing a preview does not create review files.

History, the saved-review selector, and the snapshot selector reconstruct the
same complete available file list. Selecting an annotated file loads its full
source diff, including sections without comments. Browsing diffs are separate
from persisted comment excerpts; loading a complete diff does not save it.

The comparison label uses Staged changes, Unstaged changes, or All local changes
for local sources. Commit sources show selectable revision endpoints. The
diff header also shows the selected file's added and deleted line counts. The
selected file's absolute path is selectable above the diff.

The Reviewed filter means at least one nonempty comment, including fixed or
dismissed comments. Unreviewed means no nonempty comments. The file tree and the
comments list share the left column with a horizontal splitter, and both panes
scroll independently. The comments list header shows the selected file's comment
count.

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

Editing happens in one docked panel below the diff with three tabs: Comment,
File comment, and General review. A draggable splitter between the diff and the
panel changes the height of each. There is no floating editor and no close
button. All three tabs use the same framed editor as the commit message box.
Escape returns focus to the diff, so the whole review is reachable from the
keyboard. Focusing a comment editor places the caret at the end of its text.

The Comment tab follows the diff caret and selection. It shows the finding under
the caret, or an empty editor targeting the selected lines or current line.
Moving the caret retargets the tab; every keystroke is already saved, so nothing
is lost. Ctrl+M or the Comment action gives that tab focus. An empty or
whitespace body removes its finding when the target changes or the editor loses
focus. The File comment tab edits the single file-level comment and omits the
redundant whole-file anchor label.

General review uses the commit-message editor. Its first line is the review
subject; a blank line separates the subject from the body. Commit and comparison
subjects supply the initial subject and appear in the snapshot selector with
revision and date information.

Comment records the current line when no text is selected. Comment on selection
records the selected source range with surrounding context. Hovering a commented
line offers Edit comment instead of creating another finding. General review
text and comments use the same editor as commit messages. Comments omit
commit-subject decorations. Typing preserves text, blank lines, and caret state.
Closing an empty or whitespace-only comment removes it.

Comment states are fixed:

- tofix is the default and identifies work that requires a change.
- comment records an observation or discussion without requesting a change.
- fixed identifies a finding that has been addressed.
- dismissed identifies a finding that requires no action.

Files have no separate manual reviewed mark or progress state. Removing the last
nonempty comment makes a file unreviewed. A file has at most one file-level
comment, edited from the File comment tab or from Edit file comment.

## File context menu

The file tree uses the standard change-list selection, including multi-select.
Review commands come first, then the standard file commands.

- Edit file comment opens the file-level comment. It is available only when one
  file is selected.
- Mark as OK stores one dismissed file comment with the text OK, which makes the
  file reviewed quickly. It is available only when no selected file has a tofix
  comment. Otherwise the item is disabled and its label names the count.
- Set all comments applies comment, tofix, fixed, or dismissed to every comment
  in every selected file, including comments outside the current diff.

State changes are immediate and cannot be undone.

Save as Patch and Copy as Patch use the selected files' complete patches. They
are available only when every selected file has loaded full source, so a
recorded-context-only file is never exported as a patch that would not apply.

Open, Reveal, File History, Blame, and custom file actions are available for
single-file selections in every review source, because they act on the working
file path. Open and Reveal stay disabled when the working file is absent. Staging,
discarding, and stashing are never offered inside Reviews.

Copy Path copies the repository-relative path and Copy Full Path copies the
absolute path, for every selected file.

The diff context menu keeps the same entries as the other diff views: the line
header, Copy, Copy as Patch, Open in External Merge Tool, and the repository's
custom file actions with the clicked line as the target.

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

The comments list sits below the file tree behind a horizontal splitter. Its
header shows the selected file's comment count using the same style as the
Unstaged and Staged section labels. Each row uses two lines: the first nonempty
body line, then a grey line with the state and the range, for example
"tofix - New: 12-13". Attached comments omit the meaningless Attached word;
Needs attention and Outside current diff remain visible. Comments sort the
whole-file finding first, then by the first line of the range, then by range
length. A mixed target displays both old and new source ranges.

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
  work. The application uses this file when reopening the review. It is the
  authoritative file; `review.md` is a presentation, not an input. Editing
  comment text or state in `review.xml` is effective, while changing recorded
  code context also requires updating its capture hash.

Untouched files are not persisted. A line comment retains bounded surrounding
context, even inside a large patch hunk. Selecting a complete section retains
that section and its surrounding context. General-only reviews do not capture
file data. A file marked OK persists one dismissed file comment. Full source
files and per-file snapshot documents are not written. Saved context remains
readable without the original Git objects or current working files.

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
