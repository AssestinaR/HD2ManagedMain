using HD2ModCore.Application;
using HD2ModCore.Domain;

namespace HD2ModCore.Infrastructure;

// 作用：按 patch 与 entry 构建批量 dry-run 计划，记录编辑、跳过和失败原因。
// Purpose: Builds batch dry-run plans per patch and entry, recording edited, skipped, and failed reasons.
public sealed class PatchArchiveBatchPlanner : IPatchArchiveBatchPlanner
{
	private readonly IPatchTocScanner tocScanner;
	private readonly IPatchArchiveDryWriter dryWriter;

	public PatchArchiveBatchPlanner(IPatchTocScanner tocScanner, IPatchArchiveDryWriter dryWriter)
	{
		this.tocScanner = tocScanner ?? throw new ArgumentNullException(nameof(tocScanner));
		this.dryWriter = dryWriter ?? throw new ArgumentNullException(nameof(dryWriter));
	}

	public async ValueTask<PatchArchiveBatchPlan> BuildBatchPlanAsync(
		IReadOnlyCollection<string> patchTocFilePaths,
		Func<PatchTocEntry, CancellationToken, ValueTask<PatchUnitMeshEditResult?>> editFactory,
		Func<string, IReadOnlyCollection<PatchUnitMeshEditResult>, CancellationToken, ValueTask<IReadOnlyCollection<PatchArchiveAdditionalEntry>>>? additionalEntryFactory = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(patchTocFilePaths);
		ArgumentNullException.ThrowIfNull(editFactory);

		var patchPlans = new List<PatchArchiveBatchPatchPlan>();
		var entryResults = new List<PatchArchiveBatchEntryResult>();
		foreach (var patchTocFilePath in patchTocFilePaths)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (string.IsNullOrWhiteSpace(patchTocFilePath))
			{
				throw new ArgumentException("Patch TOC file path cannot be null or whitespace.", nameof(patchTocFilePaths));
			}

			var entries = await tocScanner.ScanEntriesAsync(patchTocFilePath, cancellationToken).ConfigureAwait(false);
			var edits = new List<PatchUnitMeshEditResult>();
			foreach (var entry in entries)
			{
				cancellationToken.ThrowIfCancellationRequested();
				try
				{
					var edit = await editFactory(entry, cancellationToken).ConfigureAwait(false);
					if (edit is null)
					{
						entryResults.Add(new PatchArchiveBatchEntryResult(entry, PatchArchiveBatchEntryStatus.Skipped, "No edit produced."));
						continue;
					}

					if (!Path.GetFullPath(edit.Entry.SourceFilePath).Equals(Path.GetFullPath(entry.SourceFilePath), StringComparison.OrdinalIgnoreCase))
					{
						throw new InvalidDataException("Edit entry source path does not match the scanned entry source path.");
					}
					if (edit.Entry.EntryIndex != entry.EntryIndex || edit.Entry.AssetKey != entry.AssetKey)
					{
						throw new InvalidDataException("Edit entry identity does not match the scanned entry identity.");
					}

					edits.Add(edit);
					entryResults.Add(new PatchArchiveBatchEntryResult(entry, PatchArchiveBatchEntryStatus.Edited, "Edit produced.", edit));
				}
				catch (Exception ex) when (ex is not OperationCanceledException)
				{
					entryResults.Add(new PatchArchiveBatchEntryResult(entry, PatchArchiveBatchEntryStatus.Failed, ex.Message, Exception: ex));
				}
			}

			var additionalEntries = additionalEntryFactory is null
				? Array.Empty<PatchArchiveAdditionalEntry>()
				: await additionalEntryFactory(patchTocFilePath, edits, cancellationToken).ConfigureAwait(false);
			var writePlan = await dryWriter.BuildWritePlanAsync(patchTocFilePath, edits, additionalEntries: additionalEntries, cancellationToken: cancellationToken).ConfigureAwait(false);
			patchPlans.Add(new PatchArchiveBatchPatchPlan(patchTocFilePath, writePlan, edits));
		}

		return new PatchArchiveBatchPlan(patchPlans, entryResults);
	}
}
