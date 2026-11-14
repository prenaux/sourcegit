using System;

using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace SourceGit.Views
{
    public partial class Reviews : UserControl
    {
        private readonly DispatcherTimer _sourceTimer = new() { Interval = TimeSpan.FromSeconds(2) };
        private bool _isActive;

        public Reviews()
        {
            InitializeComponent();
            _sourceTimer.Tick += async (_, _) =>
            {
                if (DataContext is ViewModels.Reviews model)
                    await model.CheckSourceAsync();
            };
        }

        protected override async void OnLoaded(RoutedEventArgs e)
        {
            base.OnLoaded(e);
            _isActive = true;
            if (DataContext is ViewModels.Reviews model)
            {
                await model.InitializeAsync();
                await model.CheckSourceAsync();
            }
            if (_isActive)
                _sourceTimer.Start();
        }

        protected override async void OnUnloaded(RoutedEventArgs e)
        {
            _isActive = false;
            _sourceTimer.Stop();
            if (DataContext is ViewModels.Reviews model)
                await model.FlushAsync();
            base.OnUnloaded(e);
        }
    }
}
