namespace HD2ModManager.ViewModels;

// Purpose: Keeps an explicit Mod-library restart decision visible until the user commits or cancels it.
public sealed class ModLibrarySwitchBottomBarViewModel : BaseViewModel
{
	public ModLibrarySwitchBottomBarViewModel(Action restart, Action cancel)
	{
		RestartCommand = new RelayCommand(restart);
		CancelCommand = new RelayCommand(cancel);
	}

	public RelayCommand RestartCommand { get; }
	public RelayCommand CancelCommand { get; }
}
