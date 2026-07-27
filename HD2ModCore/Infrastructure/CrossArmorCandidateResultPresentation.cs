using HD2ModCore.Domain;

// Purpose: Maps committed candidate results to a UI-neutral state without treating post-commit warnings as cancellation or failure.
namespace HD2ModCore.Infrastructure;

public sealed record CrossArmorCandidateResultPresentation(bool IsFailure, bool IsWarning, string StatusText);

public static class CrossArmorCandidateResultPresenter
{
	public static CrossArmorCandidateResultPresentation Map(CrossArmorTransferCandidateResult result)
	{
		ArgumentNullException.ThrowIfNull(result);
		if (!result.IsSuccessful)
			return new(true, false, "生成失败");
		return new(false, result.HasWarnings, result.HasWarnings ? "候选已提交，但报告不完整/有告警" : "候选生成完成");
	}
}