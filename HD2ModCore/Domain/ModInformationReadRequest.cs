namespace HD2ModCore.Domain;

// 作用：描述统一读取器针对一个具体 Patch 文件的读取范围和缓存策略。
// Purpose: Describes the source Patch path, selection, revision, and cache policy for one reader request.
public sealed record ModInformationReadRequest(
	string SourcePath,
	ModInformationRequestContext? Context = null,
	ModContentRevision? Revision = null,
	ModInformationSelector? Selector = null,
	ModInformationContentView ContentView = ModInformationContentView.Effective,
	ModNodeId? NodeId = null)
{
	private ModInformationRequestContext? _generatedContext;

	public ModInformationRequestContext EffectiveContext
		=> Context ?? System.Threading.LazyInitializer.EnsureInitialized(ref _generatedContext,
			static () => ModInformationRequestContext.Create(ModInformationCacheScope.Operation))!;
	public ModInformationSelector EffectiveSelector => Selector ?? ModInformationSelector.All;

	public void Validate()
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(SourcePath);
		EffectiveContext.Validate();
	}
}
