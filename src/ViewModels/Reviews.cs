using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Avalonia.Collections;
using Avalonia.Threading;

using CommunityToolkit.Mvvm.ComponentModel;

namespace SourceGit.ViewModels
{
    public sealed class ReviewListItem(Models.Review review)
    {
        public Models.Review Review { get; } = review;
        public string Title => Review.Title;
        public string Details => $"{App.Text("Reviews.Source." + Review.Source)} - {Review.CreatedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
        public string Counts => App.Text("Reviews.Counts",
            Review.CountComments(Models.ReviewCommentState.ToFix),
            Review.CountComments(Models.ReviewCommentState.Comment),
            Review.CountComments(Models.ReviewCommentState.Fixed),
            Review.CountComments(Models.ReviewCommentState.Dismissed));
    }

    // Owns the saved reviews of one repository. The REVIEWS page lists them, and each review session takes its review from here.
    public sealed class Reviews : ObservableObject, IDisposable
    {
        private static readonly TimeSpan s_saveDelay = TimeSpan.FromMilliseconds(500);

        private readonly Repository _owner;
        private readonly string _repository;
        private readonly Models.ReviewStore _store;
        private readonly DispatcherTimer _saveTimer;
        private readonly List<Models.Review> _saved = [];
        private readonly HashSet<Models.Review> _pending = [];
        private Models.Review _activeLocalReview;
        private string _activeLocalReviewId = string.Empty;
        private string _lastSaveError = string.Empty;
        private string _status = string.Empty;
        private ReviewListItem _selectedItem;
        private bool _isLoaded;

        public event EventHandler<Models.Review> ReviewRemoved;

        public AvaloniaList<ReviewListItem> Items { get; } = [];
        public bool HasSelection => _selectedItem != null;
        public string SelectedText => _selectedItem == null ? string.Empty : Models.ReviewExport.CreateDocument(_selectedItem.Review);
        public string Status { get => _status; private set => SetProperty(ref _status, value); }

        public ReviewListItem SelectedItem
        {
            get => _selectedItem;
            set
            {
                if (!SetProperty(ref _selectedItem, value))
                    return;

                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectedText));
            }
        }

        public Reviews(Repository repository) : this(repository.FullPath)
        {
            _owner = repository;
            _activeLocalReviewId = repository.UIStates.ActiveLocalReviewId;
        }

        public Reviews(string repositoryPath)
        {
            _repository = repositoryPath;
            _store = new Models.ReviewStore(repositoryPath);
            _saveTimer = new DispatcherTimer { Interval = s_saveDelay };
            _saveTimer.Tick += (_, _) => Flush();
        }

        public void Dispose()
        {
            Flush();
        }

        public void RefreshItems()
        {
            LoadSavedReviews();
            var selected = _selectedItem?.Review;
            Items.Clear();
            foreach (var review in _saved)
                Items.Add(new ReviewListItem(review));

            SelectedItem = null;
            foreach (var item in Items)
            {
                if (ReferenceEquals(item.Review, selected))
                    SelectedItem = item;
            }
            if (_selectedItem == null && Items.Count > 0)
                SelectedItem = Items[0];
        }

        public Models.Review FindOrCreateReview(Models.ReviewSourceKind source, string baseRevision, string targetRevision, string title)
        {
            LoadSavedReviews();
            var saved = _saved.Find(x => x.Source == source && x.TargetRevision == targetRevision &&
                (source == Models.ReviewSourceKind.Commit || x.BaseRevision == baseRevision));
            return saved ?? new Models.Review
            {
                Source = source,
                BaseRevision = baseRevision,
                TargetRevision = targetRevision,
                Title = title,
            };
        }

        public Models.Review FindOrCreateLocalReview()
        {
            if (_activeLocalReview != null)
                return _activeLocalReview;

            LoadSavedReviews();
            var local = _saved.FindAll(x => x.Source == Models.ReviewSourceKind.Local);
            var active = local.Find(x => x.Id == _activeLocalReviewId);

            // No review was chosen for this repository yet. The latest saved one continues.
            if (active == null && _activeLocalReviewId.Length == 0 && local.Count > 0)
                active = local[0];
            if (active == null)
                return CreateLocalReview();

            SetActiveLocalReview(active);
            return active;
        }

        public Models.Review CreateLocalReview()
        {
            var review = new Models.Review
            {
                Source = Models.ReviewSourceKind.Local,
                Title = App.Text("Reviews.DefaultTitle", App.Text("Reviews.Source.Local"), DateTime.Now.ToString("yyyy-MM-dd HH:mm")),
            };
            SetActiveLocalReview(review);
            return review;
        }

        public void SetActiveLocalReview(Models.Review review)
        {
            _activeLocalReview = review;
            _activeLocalReviewId = review.Id;
            if (_owner != null)
                _owner.UIStates.ActiveLocalReviewId = review.Id;
        }

        // A review without a comment or general text is not written.
        public void QueueSave(Models.Review review)
        {
            if (!review.IsSaved && !review.HasContent)
                return;

            if (!_saved.Contains(review))
                _saved.Insert(0, review);
            _pending.Add(review);
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        public void Flush()
        {
            _saveTimer.Stop();
            foreach (var review in _pending)
            {
                try
                {
                    _store.Save(review);
                    _lastSaveError = string.Empty;
                }
                catch (Exception exception)
                {
                    Status = App.Text("Reviews.SaveFailed", exception.Message);
                    if (_lastSaveError != exception.Message)
                        App.RaiseException(_repository, Status);
                    _lastSaveError = exception.Message;
                }
            }
            _pending.Clear();
        }

        public async Task CopySelectedAsync()
        {
            if (_selectedItem != null)
                await App.CopyTextAsync(Models.ReviewExport.CreateText(_selectedItem.Review));
        }

        public void DeleteSelected()
        {
            var review = _selectedItem?.Review;
            if (review == null)
                return;

            try
            {
                _pending.Remove(review);
                _store.Archive(review);
            }
            catch (Exception exception)
            {
                Status = exception.Message;
                return;
            }

            _saved.Remove(review);
            if (ReferenceEquals(review, _activeLocalReview))
                _activeLocalReview = null;
            ReviewRemoved?.Invoke(this, review);
            RefreshItems();
        }

        public async Task OpenSelectedAsync()
        {
            var review = _selectedItem?.Review;
            if (review == null || _owner == null)
                return;

            if (review.Source == Models.ReviewSourceKind.Local)
                _owner.ShowLocalReview(review);
            else if (review.Source == Models.ReviewSourceKind.Commit)
                _owner.NavigateToCommit(review.TargetRevision);
            else
                await _owner.ShowRevisionCompareAsync(review.BaseRevision, review.TargetRevision);
        }

        private void LoadSavedReviews()
        {
            if (_isLoaded)
                return;

            _isLoaded = true;
            try
            {
                _saved.AddRange(_store.LoadReviews());
                Status = string.Join("\n", _store.LoadErrors);
            }
            catch (Exception exception)
            {
                Status = exception.Message;
            }
        }
    }
}
