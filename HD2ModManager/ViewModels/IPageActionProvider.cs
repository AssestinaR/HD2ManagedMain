using System.Collections.ObjectModel;

namespace HD2ModManager.ViewModels
{
    // 作用：标记可向 Shell 浮动操作区注册页面级动作的页面视图模型。
    public interface IPageActionProvider
    {
        ObservableCollection<PageActionViewModel> PageActions { get; }
    }
}
