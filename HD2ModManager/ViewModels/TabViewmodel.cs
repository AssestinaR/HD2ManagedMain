using System;
using System.Collections.Generic;
using System.Text;

namespace HD2ModManager.ViewModels
{
    public class TabViewModel : BaseViewModel
    {
        private string _header = "Tab";
        public string Header { get => _header; set => SetField(ref _header, value); }

        // 页面根 VM（可为 ModsPageViewModel、SettingsPageViewModel 等）
        private BaseViewModel? _content;
        public BaseViewModel? Content { get => _content; set => SetField(ref _content, value); }
    }
}
