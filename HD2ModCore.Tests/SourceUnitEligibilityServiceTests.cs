using HD2ModAdaptation.Analysis;
using HD2ModCore.Domain;
using HD2ModCore.Infrastructure;

namespace HD2ModCore.Tests;

public sealed class SourceUnitEligibilityServiceTests
{
    private static readonly AssetKey EligibleUnit = new(0x10, 0x100);
    private static readonly AssetKey LodOnlyUnit = new(0x10, 0x200);

    [Fact]
    public void Select_ReadableUnitWithTransferableLod0_IsEligible()
    {
        var selection = new SourceUnitEligibilityService().Select([Analysis(Unit(EligibleUnit, Mesh(lodIndex: 0, isTransferable: true)))]);

        Assert.Contains(EligibleUnit, selection.EligibleUnitAssetKeys);
        Assert.Equal("TransferableVisibleLod0", Assert.Single(selection.Units).Reason);
    }

    [Fact]
    public void Select_UnitWithOnlyAuxiliaryLod_IsIneligible()
    {
        var selection = new SourceUnitEligibilityService().Select([Analysis(Unit(LodOnlyUnit, Mesh(lodIndex: 3, isTransferable: true)))]);

        Assert.DoesNotContain(LodOnlyUnit, selection.EligibleUnitAssetKeys);
        Assert.Equal("NoTransferableVisibleLod0", Assert.Single(selection.Units).Reason);
    }

    [Fact]
    public void Select_UnreadableUnit_IsIneligible()
    {
        var selection = new SourceUnitEligibilityService().Select([Analysis(Unit(EligibleUnit, Mesh(0, true), readError: "Unreadable source"))]);

        Assert.DoesNotContain(EligibleUnit, selection.EligibleUnitAssetKeys);
        Assert.Equal("SourceUnitUnreadable", Assert.Single(selection.Units).Reason);
    }

    [Fact]
    public void Select_DuplicateUnitAcrossAnalyses_IsEligibleWhenAnyReadablePreparationHasTransferableLod0()
    {
        var selection = new SourceUnitEligibilityService().Select(
        [
            Analysis(Unit(EligibleUnit, Mesh(3, true))),
            Analysis(Unit(EligibleUnit, Mesh(0, true)))
        ]);

        Assert.Contains(EligibleUnit, selection.EligibleUnitAssetKeys);
        Assert.Single(selection.Units);
    }

    private static PatchGroupAnalysis Analysis(params SourceUnitPreparation[] units)
        => new(new PatchGroupInput("source.patch_0"), [], [], [], DateTimeOffset.UtcNow, "test", SourceUnits: units);

    private static SourceUnitPreparation Unit(AssetKey key, SourceMeshPreparation mesh, string? readError = null)
        => new(
            new HD2ModAdaptation.PatchReconstruction.PatchTocEntry(
                new HD2ModAdaptation.PatchReconstruction.AssetKey(key.TypeId, key.FileId),
                "source.patch_0",
                "source.patch_0"),
            null,
            [mesh],
            readError);

    private static SourceMeshPreparation Mesh(int lodIndex, bool isTransferable)
        => new(0, 1, lodIndex, true, isTransferable, "armor", "Slim", "Torso", "Armor", 4, 2, 1, 32, [], []);
}
