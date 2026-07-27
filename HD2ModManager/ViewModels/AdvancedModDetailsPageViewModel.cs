using HD2ModManager.Services;

namespace HD2ModManager.ViewModels
{
    // 作用：将 Mod 高级资产表和作者工具作为全宽工作区页面承载。
    public sealed class AdvancedModDetailsPageViewModel : ModDetailsPageViewModel
    {
        public AdvancedModDetailsPageViewModel(ModLibraryService library, ProfileService profiles, DerivedStateCoordinator derivedState, string modId, NotificationService? notifications = null, BackgroundTaskService? backgroundTasks = null)
            : base(library, profiles, derivedState, modId, notifications, backgroundTasks)
        {
            Title = "高级 Mod 详情";
        }

        public override bool RequiresSingleSlot => true;
    }
}