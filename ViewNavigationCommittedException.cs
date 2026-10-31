using System;
using System.Collections.Generic;
namespace MacacaGames.ViewSystem;

/// <summary>The new view committed successfully; retained-State refresh or outgoing cleanup failed.</summary>
public sealed class ViewNavigationCommittedException : InvalidOperationException
{
    public string ViewId { get; }
    public bool IsOverlay { get; }
    public ViewNavigationCommittedException(string viewId, bool isOverlay, IEnumerable<Exception> errors)
        : base($"View '{viewId}' committed, but State refresh or outgoing cleanup failed.", new AggregateException(errors))
    { ViewId = viewId; IsOverlay = isOverlay; }
}
