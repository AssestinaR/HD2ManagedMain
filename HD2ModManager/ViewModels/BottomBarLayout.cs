using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace HD2ModManager.ViewModels;

// The registry owns insertion order; the layout engine turns that order into
// compact bottom-to-top rows without any WPF dependency.
public sealed record BottomBarRowDefinition(string Key, object Content, double PreferredWidth = 0d);

public sealed record BottomBarRegistrationRequest(
    string SourceId,
    IReadOnlyList<BottomBarRowDefinition> Rows,
    int? InsertAtRow = null,
    double PreferredWidth = 0d);

public sealed record BottomBarLayoutRow(
    string Key,
    object Content,
    int RowNumber,
    double BottomOffset,
    double PreferredWidth);

public sealed record BottomBarLayoutSnapshot(
    IReadOnlyList<BottomBarLayoutRow> Rows,
    double ContentHeight,
    double PreferredWidth)
{
    public static BottomBarLayoutSnapshot Empty { get; } = new(Array.Empty<BottomBarLayoutRow>(), 0d, 0d);
    public bool HasContent => Rows.Count != 0;
}

public sealed class BottomBarRegistrationToken : IDisposable
{
    private Action? _dispose;

    internal BottomBarRegistrationToken(Action dispose) => _dispose = dispose;

    public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
}

public sealed class BottomBarRegistrationStore
{
    private readonly List<BottomBarRegistrationRequest> _registrations = [];

    public event EventHandler<BottomBarLayoutSnapshot>? LayoutChanged;

    public BottomBarLayoutSnapshot Snapshot { get; private set; } = BottomBarLayoutSnapshot.Empty;

    public BottomBarRegistrationToken Register(BottomBarRegistrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.SourceId)) throw new ArgumentException("SourceId is required.", nameof(request));
        if (request.Rows.Count == 0) throw new ArgumentException("At least one row is required.", nameof(request));
        if (_registrations.Any(item => string.Equals(item.SourceId, request.SourceId, StringComparison.Ordinal)))
            throw new InvalidOperationException($"Bottom bar source '{request.SourceId}' is already registered.");

        var insertIndex = ResolveInsertIndex(request.InsertAtRow);
        _registrations.Insert(insertIndex, request);
        Publish();
        return new BottomBarRegistrationToken(() => Remove(request.SourceId));
    }

    public void Upsert(BottomBarRegistrationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var index = _registrations.FindIndex(item => string.Equals(item.SourceId, request.SourceId, StringComparison.Ordinal));
        if (index < 0)
        {
            Register(request);
            return;
        }

        // InsertAtRow is intentionally ignored for existing sources. It is an
        // insertion hint, not a permanent row lock.
        _registrations[index] = request with { InsertAtRow = null };
        Publish();
    }

    public void Remove(string sourceId)
    {
        var index = _registrations.FindIndex(item => string.Equals(item.SourceId, sourceId, StringComparison.Ordinal));
        if (index < 0) return;
        _registrations.RemoveAt(index);
        Publish();
    }

    private int ResolveInsertIndex(int? requestedRow)
    {
        if (requestedRow is null) return _registrations.Count;
        var rowCount = _registrations.Sum(item => item.Rows.Count);
        if (requestedRow <= 0 || requestedRow > rowCount + 1) return _registrations.Count;

        var requested = Math.Clamp(requestedRow.Value, 1, rowCount + 1);
        var nextRow = 1;
        for (var index = 0; index < _registrations.Count; index++)
        {
            var lastRow = nextRow + _registrations[index].Rows.Count - 1;
            // Keep an existing multi-row source contiguous. A request that targets
            // any row inside it inserts immediately below that source block.
            if (requested <= lastRow) return index;
            nextRow = lastRow + 1;
        }
        return _registrations.Count;
    }

    private void Publish()
    {
        Snapshot = BottomBarLayoutEngine.Build(_registrations);
        LayoutChanged?.Invoke(this, Snapshot);
    }
}

public static class BottomBarLayoutEngine
{
    public const double RowHeight = 40d;
    public const double RowGap = 6d;

    public static BottomBarLayoutSnapshot Build(IReadOnlyList<BottomBarRegistrationRequest> registrations)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        var rows = new List<BottomBarLayoutRow>();
        var bottom = 0d;
        var rowNumber = 1;
        var width = 0d;

        foreach (var registration in registrations)
        {
            foreach (var row in registration.Rows)
            {
                var preferredWidth = Math.Max(registration.PreferredWidth, row.PreferredWidth);
                rows.Add(new BottomBarLayoutRow(
                    $"{registration.SourceId}:{row.Key}",
                    row.Content,
                    rowNumber++,
                    bottom,
                    preferredWidth));
                width = Math.Max(width, preferredWidth);
                bottom += RowHeight + RowGap;
            }
        }

        return rows.Count == 0
            ? BottomBarLayoutSnapshot.Empty
            : new BottomBarLayoutSnapshot(rows, bottom - RowGap, width);
    }
}
