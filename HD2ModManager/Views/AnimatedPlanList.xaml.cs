using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using HD2ModManager.ViewModels;

namespace HD2ModManager.Views;

// Purpose: Reuses the Mod-list transition controller for richer, card-based plan rows.
public partial class AnimatedPlanList : UserControl
{
    private readonly BulkObservableCollection<object> _presentedItems = new();
    private readonly ModListTransitionController _transitions;

    public AnimatedPlanList()
    {
        InitializeComponent();
        _transitions = new ModListTransitionController(this, ItemsList, TransitionOverlay, _presentedItems);
        Loaded += (_, _) => _transitions.Attach(ItemsSource);
        Unloaded += (_, _) => _transitions.Detach();
    }

    public static readonly DependencyProperty ItemsSourceProperty = DependencyProperty.Register(nameof(ItemsSource), typeof(IEnumerable), typeof(AnimatedPlanList), new PropertyMetadata(null, OnItemsSourceChanged));
    public static readonly DependencyProperty ItemTemplateProperty = DependencyProperty.Register(nameof(ItemTemplate), typeof(DataTemplate), typeof(AnimatedPlanList), new PropertyMetadata(null));

    public IEnumerable? ItemsSource { get => (IEnumerable?)GetValue(ItemsSourceProperty); set => SetValue(ItemsSourceProperty, value); }
    public DataTemplate? ItemTemplate { get => (DataTemplate?)GetValue(ItemTemplateProperty); set => SetValue(ItemTemplateProperty, value); }
    public IEnumerable PresentedItems => _presentedItems;

    private static void OnItemsSourceChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs args)
    {
        if (dependencyObject is AnimatedPlanList list && list.IsLoaded)
            list._transitions.Attach(args.NewValue as IEnumerable);
    }
}
