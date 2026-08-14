using HD2ModManager.Views;

namespace HD2ModManager.ViewModels;

// Isolated in-memory harness for validating ModListPanel layout behavior.
public sealed class ModListPanelTestPageViewModel : PageViewModel
{
    private int _nextId = 9;
    private string _lastAction = "尚未触发行操作。";

    public BulkObservableCollection<ModListPanelTestItem> Items { get; } = new(item => item.Guid);
    public RelayCommand AddCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand MoveLastToFirstCommand { get; }
    public RelayCommand UseShortListCommand { get; }
    public RelayCommand UseLongListCommand { get; }

    public string Summary => $"{Items.Count} 个占位条目";
    public string LastAction { get => _lastAction; private set => SetField(ref _lastAction, value); }

    public ModListPanelTestPageViewModel()
    {
        Title = "列表组件测试";
        AddCommand = new RelayCommand(Add);
        RemoveCommand = new RelayCommand(Remove);
        MoveLastToFirstCommand = new RelayCommand(MoveLastToFirst);
        UseShortListCommand = new RelayCommand(() => Populate(3));
        UseLongListCommand = new RelayCommand(() => Populate(14));
        Populate(8);
    }

    public void RecordAction(ModListRowAction action, ModListPanelTestItem item)
        => LastAction = $"{item.Name}: {action}";

    public void ApplySelection(IReadOnlyList<string> selectedKeys)
    {
        var selected = selectedKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items) item.IsSelected = selected.Contains(item.Guid);
        LastAction = selected.Count == 0 ? "未选择条目。" : $"已选择 {selected.Count} 个条目。";
    }

    private void Add()
    {
        var items = Items.ToList();
        items.Add(CreateItem(_nextId++));
        Items.ReplaceWith(items);
        NotifyCountChanged();
    }

    private void Remove()
    {
        if (Items.Count == 0) return;
        Items.ReplaceWith(Items.Take(Items.Count - 1).ToList());
        NotifyCountChanged();
    }

    private void MoveLastToFirst()
    {
        if (Items.Count < 2) return;
        var items = Items.ToList();
        var last = items[^1];
        items.RemoveAt(items.Count - 1);
        items.Insert(0, last);
        Items.ReplaceWith(items);
    }

    private void Populate(int count)
    {
        _nextId = count + 1;
        Items.ReplaceWith(Enumerable.Range(1, count).Select(CreateItem));
        NotifyCountChanged();
    }

    private void NotifyCountChanged() => OnPropertyChanged(nameof(Summary));

    private static ModListPanelTestItem CreateItem(int id)
        => new($"test-{id:D3}", $"占位 Mod {id:D3}", "用于验证标准列表的尺寸与过渡效果。");
}

public sealed class ModListPanelTestItem(string guid, string name, string description) : BaseViewModel, IModListSelectable
{
    public string Guid { get; } = guid;
    public string SelectionKey => Guid;
    public string Name { get; } = name;
    public string Description { get; } = description;
    public string AssetSummaryText => "测试条目";
    public string UserStatusTitle => "测试状态";
    public bool IsModelOutdated => false;
    public bool IsVisible => true;
    public string? ImagePath => null;
    private bool _isSelected;
    public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }
}
