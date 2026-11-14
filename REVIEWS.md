# Local reviews

Reviews capture code and retain comments while work changes. Open Reviews in the
repository sidebar, use Reviews in Local changes, or choose Review changes from
the History context menu for one commit or two comparison endpoints.

## Capture a review

Select a commit, revision comparison, staged changes, unstaged changes, or all
local changes. A commit is compared with its first parent, or the empty tree for
an initial commit. A revision comparison shows the net difference from its base
to its target. Unstaged and all-local captures include untracked files that Git
does not ignore.

A matching saved snapshot reopens its existing review. Otherwise, the captured
preview remains in memory until a comment, general note, or file-review mark is
entered. The application saves those changes automatically. The save indicator
reports pending writes and errors.

The file list includes comment counts and review progress. Select a saved review
and a snapshot above the file list to return to earlier work. General review
text and the title belong to the review, rather than an individual snapshot.

## Add and edit comments

Select one or several source lines and click Add comment. The comment editor
remains open when the pointer leaves the selection. Gutter markers reopen inline
comments and highlight their source ranges. The comments list also opens
existing comments. Add file comment creates a comment without a line anchor.

Comment states are fixed:

- toreview: The finding needs evaluation.
- todo: The finding requires action.
- resolved: The finding has been addressed.
- dismissed: The finding requires no action.

Resolve remaining comments affects toreview and todo comments in the selected
file. Set all comments applies the selected state to every comment in that file,
including comments outside the current diff. Undo restores the previous states.

Mark file reviewed records examination of the code without resolving findings.
Review mode does not expose staging, unstaging, or discarding actions.

## Update a snapshot

The displayed code never updates automatically. For staged and local reviews,
the source-change indicator offers Update snapshot. Existing snapshot IDs are
reused; new code creates another snapshot within the same review.

Updates copy comments and retain their IDs, original context, and workflow
states. Unchanged files retain review progress. Changed reviewed files show
Changed since review. Unique unchanged source ranges migrate automatically;
ambiguous or edited ranges show Needs attention. Comments without a current diff
location remain under their file as Outside current diff.

Reattach preserves a comment's identity and original anchor. Select its new file
and lines, then click Attach comment here. Original snapshot opens its initial
code context. Each snapshot retains its own comment text, states, and placements;
editing an older snapshot does not rewrite a newer snapshot's comments.

## Storage and AI export

All review data lives in the repository's `_reviews/` directory:

- `<review_id>/review.xml` contains the title, general text, and snapshot index.
- `<review_id>/snapshots/<snapshot_id>/` contains the file index and one XML
  record per file, including its patch and comments.
- `_content/` contains captured text identified by content hashes. Identical
  text is stored once and supports migration without current files or Git
  objects.

The application does not change ignore rules or stage or commit these files.
Review capture excludes `_reviews/`, including staged review files. XML records
are replaced atomically. Captured code is checked against its snapshot identity,
and captured text is checked against its content hash when read.

Copy review for AI exports general text, standard unified patches, and toreview
and todo comments. Resolved and dismissed comments are omitted. Uncertain
attachments include their original context instead of claiming current line
coordinates.

Binary and metadata-only changes support file comments rather than inline text
comments. Local captures require a resolved index. Source changes during capture
cause a retry or a visible capture error instead of replacing an existing review.
