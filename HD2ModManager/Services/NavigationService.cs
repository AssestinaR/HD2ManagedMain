using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace HD2ModManager.Services
{
    public interface INavigationService
    {
        ReadOnlyObservableCollection<string> Breadcrumbs { get; }
        void GoTo(string pageKey);
        void GoBackTo(int index);
        event Action<string>? Navigated;
    }

    public class NavigationService : INavigationService
    {
        private readonly ObservableCollection<string> _breadcrumbs = new();
        public ReadOnlyObservableCollection<string> Breadcrumbs { get; }
        private readonly List<string> _stack = new();
        public event Action<string>? Navigated;

        public NavigationService()
        {
            Breadcrumbs = new ReadOnlyObservableCollection<string>(_breadcrumbs);
            _stack.Add("home");
            _breadcrumbs.Add("Home");
        }

        public void GoTo(string pageKey)
        {
            _stack.Add(pageKey);
            _breadcrumbs.Add(KeyToTitle(pageKey));
            Navigated?.Invoke(pageKey);
        }

        public void GoBackTo(int index)
        {
            if (index < 0 || index >= _stack.Count) return;
            while (_stack.Count - 1 > index)
            {
                _stack.RemoveAt(_stack.Count - 1);
                _breadcrumbs.RemoveAt(_breadcrumbs.Count - 1);
            }
            Navigated?.Invoke(_stack[^1]);
        }

        private static string KeyToTitle(string key)
        {
            return key switch
            {
                "home" => HD2ModManager.Resources.Strings.Breadcrumb_Home,
                "settings" => HD2ModManager.Resources.Strings.Home_Settings,
                "profiles" => "Profiles",
                _ => key
            };
        }
    }
}
