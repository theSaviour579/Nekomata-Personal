using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Nekomata.UI.Controls.Common;
using Nekomata.UI.ViewModels;

namespace Nekomata.UI.Views;

// The Guardian HUD home page. One layout that adapts to its width: on the vertical monitor and the laptop the core
// sits between "now" and "needs you"; on a narrow window those stack above and below the core, and the operations
// and projects columns stack too. The core shrinks a little on smaller screens.
public partial class HomeView : UserControl
{
    private MainViewModel? _viewModel;
    private bool? _narrow;
    private bool? _stackOps;
    private readonly DispatcherTimer _typer = new() { Interval = TimeSpan.FromMilliseconds(22) };
    private string _thoughtTarget = "";

    public HomeView()
    {
        InitializeComponent();
        Loaded += (_, _) => { ApplyLayout(); HookConversation(); AnimateDots(); };
        _typer.Tick += (_, _) => TypeNext();
        SizeChanged += (_, _) => ApplyLayout();
        DataContextChanged += (_, _) => HookConversation();
    }

    private void HookConversation()
    {
        if (DataContext is not MainViewModel vm || ReferenceEquals(vm, _viewModel)) return;
        if (_viewModel is not null)
        {
            if (_viewModel.ChatHistory is INotifyCollectionChanged oldChat) oldChat.CollectionChanged -= OnChatChanged;
            _viewModel.PropertyChanged -= OnViewModelChanged;
        }
        _viewModel = vm;
        if (vm.ChatHistory is INotifyCollectionChanged chat) chat.CollectionChanged += OnChatChanged;
        vm.PropertyChanged += OnViewModelChanged;
        ScrollChatToEnd();
        TypeThought(vm.GuardianThought);
        UpdateDots();
    }

    private void OnChatChanged(object? sender, NotifyCollectionChangedEventArgs e) => ScrollChatToEnd();

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.GuardianPanelExpanded)) ScrollChatToEnd();
        if (e.PropertyName == nameof(MainViewModel.GuardianThought) && _viewModel is not null) Dispatcher.BeginInvoke(() => TypeThought(_viewModel.GuardianThought));
        if (e.PropertyName is nameof(MainViewModel.GuardianPresenceState) or nameof(MainViewModel.GuardianBusy)) Dispatcher.BeginInvoke(UpdateDots);
    }

    private void ScrollChatToEnd() =>
        Dispatcher.BeginInvoke(() => HomeChatScroll.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Background);

    private void ApplyLayout()
    {
        var width = ActualWidth;
        if (width <= 0) return;

        var core = Math.Clamp(width * 0.4, 420, 560);
        CoreHost.Width = core;
        CoreHost.Height = core;

        var narrow = width < 1150;
        if (_narrow != narrow)
        {
            _narrow = narrow;
            if (narrow)
            {
                Place(NowPane, 0, 0, 3);
                Place(CoreHost, 1, 0, 3);
                Place(NeedsPane, 2, 0, 3);
                NowPane.HorizontalAlignment = HorizontalAlignment.Center;
                NeedsPane.HorizontalAlignment = HorizontalAlignment.Center;
                NowPane.Margin = new Thickness(0, 0, 0, 20);
                NeedsPane.Margin = new Thickness(0, 20, 0, 0);
            }
            else
            {
                Place(NowPane, 0, 0, 1);
                Place(CoreHost, 0, 1, 1);
                Place(NeedsPane, 0, 2, 1);
                NowPane.HorizontalAlignment = HorizontalAlignment.Right;
                NeedsPane.HorizontalAlignment = HorizontalAlignment.Left;
                NowPane.Margin = new Thickness(0, 0, 24, 0);
                NeedsPane.Margin = new Thickness(24, 0, 0, 0);
            }
        }

        var stackOps = width < 1000;
        if (_stackOps != stackOps)
        {
            _stackOps = stackOps;
            Place(ProjectsPane, 0, 0, stackOps ? 3 : 1);
            Place(CommitPane, stackOps ? 1 : 0, stackOps ? 0 : 2, stackOps ? 3 : 1);
            CommitPane.Margin = stackOps ? new Thickness(0, 30, 0, 0) : new Thickness(0);
            Place(TeamsAsksPane, 0, 0, stackOps ? 3 : 1);
            Place(TeamsUpdatesPane, stackOps ? 1 : 0, stackOps ? 0 : 2, stackOps ? 3 : 1);
            TeamsUpdatesPane.Margin = stackOps ? new Thickness(0, 30, 0, 0) : new Thickness(0);
            SignalsGrid.Columns = stackOps ? 2 : 4;
        }
    }

    private static void Place(FrameworkElement element, int row, int column, int columnSpan)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        Grid.SetColumnSpan(element, columnSpan);
    }

    // The thought line types itself out, a character at a time, with a blinking caret, so a new thought reads as
    // Guardian saying something rather than a label changing.
    private void TypeThought(string? text)
    {
        _thoughtTarget = text ?? "";
        ThoughtRun.Text = "";
        _typer.Start();
    }

    private void TypeNext()
    {
        var shown = ThoughtRun.Text.Length;
        if (shown >= _thoughtTarget.Length) { _typer.Stop(); return; }
        ThoughtRun.Text = _thoughtTarget[..Math.Min(_thoughtTarget.Length, shown + 2)];
    }

    // Dots appear while Guardian is thinking or busy, each fading in turn.
    private void UpdateDots()
    {
        var busy = _viewModel is { GuardianBusy: true } || _viewModel?.GuardianPresenceState == GuardianPresenceState.Thinking;
        ThinkingDots.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AnimateDots()
    {
        var i = 0;
        foreach (var dot in ThinkingDots.Children.OfType<Ellipse>())
        {
            dot.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(420))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(140 * i++)
            });
        }
        var caret = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(530)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        var brush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x42, 0xD9, 0xFF));
        CaretRun.Foreground = brush;
        brush.BeginAnimation(System.Windows.Media.Brush.OpacityProperty, caret);
    }
}
