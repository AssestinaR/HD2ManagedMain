using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

// 作用：验证高级分析服务通过信息中心请求派生缓存，而不是直接读取缓存存储。
public sealed class AdvancedModAnalysisServiceTests
{
	[Fact]
	public async Task GetStateAsync_RequestsAdvancedFactsFromInformationCenter()
	{
		var node = new ModNode(ModNodeId.New(), "Test", new ModNodeMetadata("Test", null, DateTimeOffset.UtcNow, null), [], []);
		var facts = new HD2ModCore.Application.AdvancedUnitAnalysisFacts(node.Id, node.RelativePath, "generation", DateTimeOffset.UtcNow, [], []);
		var service = new AdvancedModAnalysisService(new FakeInformationCenter(facts));

		var state = await service.GetStateAsync(node, "mods");

		Assert.True(state.IsReady);
		Assert.True(state.IsCurrent);
	}
}