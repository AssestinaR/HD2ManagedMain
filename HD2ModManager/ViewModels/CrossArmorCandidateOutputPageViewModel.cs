using System.IO;
using HD2ModAdaptation.Analysis;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;
using HD2ModManager.Services;
using HD2ModManager.Views;

namespace HD2ModManager.ViewModels
{
    // 作用：在跨护甲双槽工作区右侧收集候选输出参数并执行隔离写出。
    public sealed class CrossArmorCandidateOutputPageViewModel : PageViewModel
    {
        private readonly CrossArmorTransferPlanWindowViewModel _plan;
        private readonly NotificationService _notifications;
        private string _outputDirectory = string.Empty;
        private CrossArmorMaterialBindingMode _materialBindingMode = CrossArmorMaterialBindingMode.PreserveSourceReferences;
        private bool _isGenerating;
        private string _state = "先在左侧确认计划，再选择空的输出文件夹。";

        public IReadOnlyList<CrossArmorMaterialBindingMode> MaterialBindingModes { get; } = Enum.GetValues<CrossArmorMaterialBindingMode>();
        public string OutputDirectory
        {
            get => _outputDirectory;
            set
            {
                if (!SetField(ref _outputDirectory, value)) return;
                OnPropertyChanged(nameof(CanGenerate));
                GenerateCommand.RaiseCanExecuteChanged();
            }
        }
        public CrossArmorMaterialBindingMode MaterialBindingMode { get => _materialBindingMode; set => SetField(ref _materialBindingMode, value); }
        public bool IsGenerating { get => _isGenerating; private set { if (SetField(ref _isGenerating, value)) GenerateCommand.RaiseCanExecuteChanged(); } }
        public string State { get => _state; private set => SetField(ref _state, value); }
        public bool CanGenerate => !IsGenerating && _plan.CanGenerateCandidate && !string.IsNullOrWhiteSpace(OutputDirectory);
        public RelayCommand GenerateCommand { get; }

        public CrossArmorCandidateOutputPageViewModel(CrossArmorTransferPlanWindowViewModel plan, NotificationService notifications)
        {
            Title = "验证候选输出";
            _plan = plan;
            _notifications = notifications;
            GenerateCommand = new RelayCommand(async _ => await GenerateAsync(), _ => CanGenerate);
            _plan.PropertyChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(CanGenerate));
                GenerateCommand.RaiseCanExecuteChanged();
            };
        }

        public void SetOutputDirectory(string directory)
        {
            OutputDirectory = directory;
            OnPropertyChanged(nameof(CanGenerate));
            GenerateCommand.RaiseCanExecuteChanged();
        }

        private async Task GenerateAsync()
        {
            var plan = _plan.GetCurrentPlan();
            if (plan is null || !plan.CanContinue || string.IsNullOrWhiteSpace(OutputDirectory)) return;
            if (Directory.Exists(OutputDirectory) && Directory.EnumerateFileSystemEntries(OutputDirectory).Any())
            {
                State = "输出根文件夹必须为空。";
                return;
            }

            IsGenerating = true;
            State = "正在重建 current target Unit 壳并写出验证候选。";
            _plan.CandidateGenerationRunning = true;
            try
            {
                var result = await Task.Run(() => CoreServices.CreateCrossArmorTransferCandidateService().GenerateCandidateAsync(
                    new CrossArmorTransferCandidateRequest(_plan.SourcePatchTocPath, _plan.GameDataDirectory, OutputDirectory, plan, MaterialBindingMode)).AsTask());
                if (!result.IsSuccessful)
                {
                    State = $"生成失败：{string.Join("；", result.Issues.Select(issue => issue.Message).Take(3))}";
                    _notifications.Show(State, NotificationLevel.Error, TimeSpan.FromSeconds(12));
                    return;
                }

                State = $"验证候选已生成：Unit {result.OutputUnitCount}；替换 mesh {result.ReplacementMeshCount}；极小化 mesh {result.MinifiedMeshCount}。报告：{result.ReportPath}";
                _notifications.Show("跨护甲验证候选已生成；请按输出目录中的报告完成验收。", NotificationLevel.Info, TimeSpan.FromSeconds(10));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException)
            {
                State = $"生成候选失败：{exception.Message}";
                _notifications.Show(State, NotificationLevel.Error, TimeSpan.FromSeconds(12));
            }
            finally
            {
                _plan.CandidateGenerationRunning = false;
                IsGenerating = false;
            }
        }
    }
}