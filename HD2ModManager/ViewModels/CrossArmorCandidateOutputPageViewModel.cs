using System.IO;
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
        private string _outputDirectory = Path.Combine(AppContext.BaseDirectory, "Output");
        private bool _isGenerating;
        private string _state = "先在左侧确认计划；候选将写入输出目录下自动命名的子文件夹。";

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
            var candidateDirectory = BuildCandidateDirectory(plan);

            IsGenerating = true;
            State = "正在重建 current target Unit 壳并写出验证候选。";
            _plan.CandidateGenerationRunning = true;
            try
            {
                var result = await Task.Run(() => CoreServices.CreateCrossArmorTransferCandidateService().GenerateCandidateAsync(
                    new CrossArmorTransferCandidateRequest(_plan.SourcePatchTocPath, _plan.GameDataDirectory, candidateDirectory, plan, CrossArmorMaterialBindingMode.PreserveSourceReferences, _plan.PreparedSourceEntries)).AsTask());
                if (!result.IsSuccessful)
                {
                    State = $"生成失败：{string.Join("；", result.Issues.Select(issue => issue.Message).Take(3))}";
                    _notifications.Show(State, NotificationLevel.Error, TimeSpan.FromSeconds(12));
                    return;
                }

                State = $"验证候选已生成到：{candidateDirectory}。Unit {result.OutputUnitCount}；替换 mesh {result.ReplacementMeshCount}；极小化 mesh {result.MinifiedMeshCount}。报告：{result.ReportPath}";
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

        private string BuildCandidateDirectory(CrossArmorTransferPlan plan)
        {
            var sourceName = SanitizeDirectoryName(plan.SelectedSource?.DisplayName ?? "未知来源");
            var armorTargets = plan.SelectedTargets.Where(target => string.Equals(target.Category, "Armor", StringComparison.OrdinalIgnoreCase)).ToArray();
            var targetName = armorTargets.Length == 0
                ? "无护甲目标"
                : SanitizeDirectoryName(armorTargets[0].DisplayName) + (armorTargets.Length > 1 ? $"以及更多的{armorTargets.Length - 1}个" : string.Empty);
            return Path.Combine(OutputDirectory, $"{sourceName}+{DateTime.Now:yyyyMMdd-HHmmss}+替换{targetName}");
        }

        private static string SanitizeDirectoryName(string value)
        {
            var invalidCharacters = Path.GetInvalidFileNameChars();
            var safe = new string(value.Select(character => invalidCharacters.Contains(character) ? '_' : character).ToArray()).Trim();
            return string.IsNullOrWhiteSpace(safe) ? "未命名" : safe;
        }
    }
}