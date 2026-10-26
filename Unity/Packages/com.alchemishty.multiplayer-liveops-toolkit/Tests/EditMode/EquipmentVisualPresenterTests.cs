using System;
using System.Collections;
using System.Collections.Generic;
using MultiplayerLiveOpsToolkit.Equipment;
using MultiplayerLiveOpsToolkit.EquipmentVisuals;
using NUnit.Framework;

public class EquipmentVisualPresenterTests
{
    private static EquipmentLoadout Look(string itemId) => new EquipmentLoadout(1, new Dictionary<string, string> { { "tail", itemId } });
    [Test]
    public void Bind_PreviewAndConfirmedChanges_RenderWithoutNetworkingAndDisposeUnsubscribes()
    {
        var renderer = new RendererStub();
        var service = new ServiceStub { Presentation = Look("confirmed") };
        var presenter = new EquipmentVisualPresenter(renderer, message => Assert.Fail(message));
        presenter.Bind(service);
        Assert.That(renderer.Displayed, Is.SameAs(service.Presentation));
        service.IsPreviewActive = true; service.Presentation = Look("preview"); service.NotifyPreview();
        Assert.That(renderer.Displayed, Is.SameAs(service.Presentation));
        service.IsPreviewActive = false; service.Presentation = Look("confirmed"); service.NotifyPreview();
        Assert.That(renderer.Displayed, Is.SameAs(service.Presentation));
        presenter.Dispose(); presenter.Dispose();
        int renderCount = renderer.RenderCount;
        service.NotifyPreview(); service.NotifyState();
        Assert.That(renderer.RenderCount, Is.EqualTo(renderCount));
        Assert.That(renderer.Displayed, Is.Null);
    }
    [Test]
    public void ShowPublic_LocalPreviewOverridesPublicUntilRestored_ConfirmedAccountDoesNotBypassPublicApproval()
    {
        var renderer = new RendererStub();
        var service = new ServiceStub { Presentation = Look("account-newer") };
        using (var presenter = new EquipmentVisualPresenter(renderer, message => Assert.Fail(message)))
        {
            presenter.Bind(service);
            var publicLook = Look("public"); presenter.ShowPublic(publicLook);
            service.NotifyState(); Assert.That(renderer.Displayed, Is.SameAs(publicLook));
            service.IsPreviewActive = true; service.NotifyPreview(); Assert.That(renderer.Displayed, Is.SameAs(service.Presentation));
            service.IsPreviewActive = false; service.NotifyPreview(); Assert.That(renderer.Displayed, Is.SameAs(publicLook));
            var nextPublic = Look("new-public"); presenter.ShowPublic(nextPublic);
            Assert.That(renderer.Displayed, Is.SameAs(nextPublic));
        }
    }
    [Test]
    public void Render_MissingConfiguration_ReportsFailureAndAccountTeardownClearsVisuals()
    {
        string diagnostic = null;
        var renderer = new RendererStub { ShouldThrow = true };
        var service = new ServiceStub { Presentation = Look("confirmed") };
        using (var presenter = new EquipmentVisualPresenter(renderer, message => diagnostic = message))
        {
            presenter.Bind(service);
            Assert.That(diagnostic, Does.Contain("binding"));
            renderer.ShouldThrow = false; service.NotifyState();
            service.HasSnapshot = false; service.NotifyState();
            Assert.That(renderer.Displayed, Is.Null);
        }
    }
    private sealed class RendererStub : IEquipmentRenderer
    {
        public EquipmentLoadout Displayed;
        public int RenderCount;
        public bool ShouldThrow;
        public void Render(EquipmentLoadout loadout) { if (ShouldThrow) throw new InvalidOperationException("Missing visual binding"); Displayed = loadout; RenderCount++; }
        public void Clear() { Displayed = null; }
    }
    private sealed class ServiceStub : IEquipmentService
    {
        public bool HasSnapshot { get; set; } = true;
        public bool IsBusy => false;
        public bool RequiresRefresh => false;
        public EquipmentState Snapshot => throw new NotSupportedException();
        public EquipmentLoadout Presentation { get; set; }
        public bool IsPreviewActive { get; set; }
        public event Action StateChanged;
        public event Action PreviewChanged;
        public void NotifyState() => StateChanged?.Invoke();
        public void NotifyPreview() => PreviewChanged?.Invoke();
        public IEnumerator Refresh(Action success, Action<EquipmentError> failure) => throw new NotSupportedException();
        public IEnumerator ApplyItems(IEnumerable<string> itemIds, Action success, Action<EquipmentError> failure) => throw new NotSupportedException();
        public IEnumerator Unequip(IEnumerable<string> slotIds, Action success, Action<EquipmentError> failure) => throw new NotSupportedException();
        public IEnumerator Clear(Action success, Action<EquipmentError> failure) => throw new NotSupportedException();
        public void Preview(IEnumerable<string> itemIds) => throw new NotSupportedException();
        public void ClearPreview() => throw new NotSupportedException();
    }
}
