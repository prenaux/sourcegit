# Local reviews

A review is a set of comments on code changes. You write the comments in
Local changes and in History, on the standard diff view. SourceGit saves each
review in the `_reviews/` folder of the repository. The REVIEWS page lists the
saved reviews.

## Which review is open

- Local changes has one local review. The files of the Unstaged list and of
  the Staged list share it.
- A selected commit in History has its own review. The diff is the commit
  against its first parent.
- Two selected commits in History have the review of their compare.
- More than two selected commits show the compare from the oldest selected
  commit to the newest one. This compare has its own review. The diff does not
  contain the changes of the oldest commit itself.
- A compare with the worktree has no review, because its content changes with
  each edit.

SourceGit writes a review when it gets its first comment or general text.
Opening a page or selecting a file writes nothing.

The local review continues until you choose New Review in the review actions.
The previous local review stays in the REVIEWS page.

## Review panel

Local changes shows four tabs below the diff: Commit, Comment, File comment
and General review. The Commit tab holds the commit message and the commit
buttons. The commit shortcuts work while the Commit tab is open.

History shows the three review tabs below the detail of the selection. A
splitter between the detail and the tabs changes their heights.

- The Comment tab follows the caret and the selection of the diff. It shows
  the comment on the caret line, or an empty editor for the selected lines.
  Text that you type there creates the comment.
- The File comment tab edits the one file comment of the selected file.
- The General review tab edits the review subject and the general notes. The
  first line is the subject. A blank line separates it from the notes.

Each change is saved automatically. A comment without text is removed when
its editor loses the focus or gets another target. Escape in an editor returns
the focus to the diff.

The button at the right of the tab headers shows or hides the comment list.
The list shows the comments of the selected file: the file comment first, then
the line comments by line. A click on a row moves the diff to that comment.
Enter on a row moves the focus to its editor.

## Comment in the diff

The diff is the standard diff view. All its toolbar buttons work: more or
fewer context lines, the entire file, whitespace, side by side and the
external tool.

- Hover a block of changes to get its floating actions. Comment stands beside
  Stage, Unstage and Discard, and it targets the changed lines of the block.
- Select lines, then use Comment, to comment exactly these lines.
- A blue dot in the left margin marks a commented line. A click on the dot
  opens the comment.
- The review button in the diff toolbar lists every review action with its
  shortcut.

Shortcuts:

- Ctrl+M comments on the selection, or on the caret line.
- Ctrl+Down moves the caret to the end of the current block of changes, then
  to the start of the next block. Ctrl+Up does the reverse. These two
  shortcuts work in every diff view.
- Shift with Ctrl+Up or Ctrl+Down extends the selection to that position. The
  diff must have the keyboard focus.
- Ctrl+Alt+Down and Ctrl+Alt+Up go to the next and to the previous comment of
  the diff.

## Comment states

- tofix is the default. The finding requires a change.
- comment records an observation that requires no change.
- fixed marks a finding that was addressed.
- dismissed marks a finding that requires no action.

## Files

Each file row shows one review mark after its name: a warning sign with the
number of tofix comments, or a check mark when the file has comments and no
tofix comment. The marks show in the Unstaged list, the Staged list, the
changes of a commit and the changes of a compare.

The filter box beside the search box keeps the files that match: All files,
Reviewed, Unreviewed, Comment, To fix, Fixed or Dismissed. Reviewed means at
least one comment with text. The list updates when you change the filter, mark
files as OK, or set all comments.

The context menu of the file lists starts with the review actions:

- Edit file comment opens the File comment tab. It needs one selected file.
- Mark as OK stores a file comment with the text OK and the state comment.
  It is disabled while a selected file has a tofix comment.
- Set all comments applies one state to every comment of the selected files.

## How comments follow the code

A comment belongs to a file path. Staging or unstaging a file does not change
its path, so its comments stay. When git reports a rename, the comments move
to the new path.

A line comment records its lines and up to four lines around them. Each time
a diff loads, SourceGit looks for this code in the lines of the diff:

- The code is at the recorded lines: the comment stays attached.
- The code is at one other place: the comment moves there.
- The recorded lines show other code: the comment shows Needs attention.
- The diff does not show the recorded lines: the comment shows Outside current
  diff.

The last two states are set only in Local changes, for a file that is in one
list only. A commit does not change, so a hidden comment there is still valid.
A file with staged and unstaged parts has two diffs, and each one shows only a
part of the code.

A comment that is not attached keeps its last known lines and code. Move
comment to selected lines attaches it again: select the lines, then use the
Comment action, which reads Attach comment here.

Limit: the search reads only the lines that the diff shows. Show the entire
file to see a comment on a line outside the changed blocks.

## REVIEWS page

The page lists the saved reviews with their source, date and number of
comments per state. The selected review shows as Markdown source with syntax
colors.

- Open goes to Local changes for a local review, and makes it the active one.
  For the other reviews it goes to the commit or to the compare in History.
- Copy Review copies the review text to the clipboard.
- Delete moves the review folder to `_toremove/_reviews/`. SourceGit does not
  erase it, so you can restore or erase the folder by hand.

## Storage and copying

Each review has two files under `_reviews/<review_id>/`:

- `review.xml` holds the comments, their states, their lines and the recorded
  code. SourceGit reads only this file. You can edit comment text and states
  in it by hand.
- `review.md` shows the same review as readable text. SourceGit writes it
  again with each save.

Files without a comment are not stored. SourceGit writes both files to a
temporary name first and then renames them, so a failed write keeps the
previous version.

Copy Review copies the subject, the general notes, and the comment and tofix
findings. Each finding comes with its location and a short diff of its code.
These diffs are for reading. They are not patches to apply. Fixed and
dismissed findings stay in `review.md` only. A finding that is not attached
says so.

SourceGit does not change the ignore rules. The `_reviews/` files show in
Local changes unless the repository ignores them.

SourceGit rejects a `review.xml` with a document type declaration, and it
does not follow symbolic links in the storage path. These two rules stop a
repository from making SourceGit read or write files outside `_reviews/`.

Version 2 of `review.xml` stored one comment list per snapshot of the code.
SourceGit still reads these files and keeps the comments of the last snapshot.
It writes version 3 at the next change of the review. The version 2 reader in
`src/Models/ReviewStore.cs` can be removed when no repository holds such a
file.
