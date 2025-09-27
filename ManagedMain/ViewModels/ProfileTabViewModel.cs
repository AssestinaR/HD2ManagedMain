using System.ComponentModel;
using System.Runtime.CompilerServices;
using ManagedMain.Models;

namespace ManagedMain.ViewModels
{
    // 代表一个配置文件的标签页（后续扩展为 ManageDeModoCracy 页）
    public class ProfileTabViewModel : INotifyPropertyChanged
    {
        public ProfileEntry Profile { get; }
        public ProfileTabViewModel(ProfileEntry p)
        {
            Profile = p;
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
