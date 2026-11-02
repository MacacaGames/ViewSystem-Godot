using Godot;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
namespace MacacaGames.ViewSystem;

public sealed partial class ViewInstance
{
    private readonly Dictionary<Control,CancellationTokenSource> elementOwnership=new();
    private CancellationToken ElementOwnershipToken(Control element)
        =>elementOwnership.TryGetValue(element,out var scope)?scope.Token:lifetimeToken;
    private void RenewElementOwnership(Control element)
    {
        SuspendElement(element);elementOwnership[element]=new();
    }
    internal void SuspendElement(Control element)
    {
        if(!elementOwnership.Remove(element,out var scope))return;
        try{scope.Cancel();}finally{scope.Dispose();}
    }
    private async Task AnimateOwnedAsync(Control element,CancellationToken token,Func<CancellationToken,Task> animate,bool allowLoan=true)
    {
        if(!Owns(element))return;
        var ownership=ElementOwnershipToken(element);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(token,ownership,lifetimeToken);
        try{await animate(linked.Token);}
        // A view's entry may finish after one of its shared elements was lent
        // elsewhere. Explicit manual operations include ownership in their own
        // token and still report cancellation to their caller.
        catch(OperationCanceledException)when(allowLoan&&ownership.IsCancellationRequested&&!token.IsCancellationRequested&&!lifetimeToken.IsCancellationRequested){}
    }
}
