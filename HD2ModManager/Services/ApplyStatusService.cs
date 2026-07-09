using HD2ModCore.Domain;

namespace HD2ModManager.Services
{
    // 作用：保存最近一次 Profile 应用结果，供状态页展示部署闭环。
    public sealed class ApplyStatusService
    {
        public DateTimeOffset? LastAppliedUtc { get; private set; }
        public ActivationResult? LastActivation { get; private set; }
        public ApplyResult? LastCoreResult => LastActivation?.CoreResult;

        public void Record(ActivationResult result)
        {
            LastAppliedUtc = DateTimeOffset.UtcNow;
            LastActivation = result;
        }

        public string Summary
        {
            get
            {
                if (LastActivation == null) return "尚未应用配置";
                var core = LastActivation.CoreResult;
                if (core == null) return LastActivation.Message;
                var errors = core.Issues.Count(i => i.Severity == CoreIssueSeverity.Error);
                var warnings = core.Issues.Count(i => i.Severity == CoreIssueSeverity.Warning);
                return $"{(core.Success ? "成功" : "失败")} · 操作 {core.Operations.Count} · 错误 {errors} · 警告 {warnings}";
            }
        }

        public IReadOnlyList<string> Details
        {
            get
            {
                var core = LastActivation?.CoreResult;
                if (core == null) return Array.Empty<string>();

                var methodGroups = core.Operations
                    .Where(o => o.Method != null)
                    .GroupBy(o => o.Method!.Value)
                    .Select(g => $"{g.Key}: {g.Count()}");
                var issues = core.Issues.Take(8).Select(i => $"[{i.Severity}] {i.Code}: {i.Message}");
                return methodGroups.Concat(issues).ToList();
            }
        }
    }
}
