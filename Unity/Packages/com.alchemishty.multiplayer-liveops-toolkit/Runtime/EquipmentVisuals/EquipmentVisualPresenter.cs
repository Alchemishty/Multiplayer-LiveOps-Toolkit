using System;
using MultiplayerLiveOpsToolkit.Equipment;

namespace MultiplayerLiveOpsToolkit.EquipmentVisuals
{
    /// <summary>Owns local preview subscriptions; an optional public loadout remains the network display authority.</summary>
    public sealed class EquipmentVisualPresenter : IDisposable
    {
        private readonly IEquipmentRenderer renderer;
        private readonly Action<string> onConfigurationFailure;
        private IEquipmentService service;
        private EquipmentLoadout publicLoadout;
        private bool isDisposed;

        public EquipmentVisualPresenter(IEquipmentRenderer renderer, Action<string> onConfigurationFailure)
        {
            this.renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            this.onConfigurationFailure = onConfigurationFailure ?? throw new ArgumentNullException(nameof(onConfigurationFailure));
        }
        public void Bind(IEquipmentService service)
        {
            EnsureActive();
            if (service == null) throw new ArgumentNullException(nameof(service));
            Unsubscribe();
            this.service = service;
            service.StateChanged += Render;
            service.PreviewChanged += Render;
            Render();
        }
        public void ShowPublic(EquipmentLoadout loadout)
        {
            EnsureActive();
            publicLoadout = loadout ?? throw new ArgumentNullException(nameof(loadout));
            Render();
        }
        private void Render()
        {
            if (isDisposed) return; // A copied notification can arrive during owner teardown.
            var displayed = service != null && service.HasSnapshot && service.IsPreviewActive ? service.Presentation
                : publicLoadout ?? (service != null && service.HasSnapshot ? service.Presentation : null);
            try { if (displayed == null) renderer.Clear(); else renderer.Render(displayed); }
            catch (InvalidOperationException exception) { onConfigurationFailure(exception.Message); }
        }
        private void Unsubscribe()
        {
            if (service == null) return; // Public-only rendering has no private service subscriptions.
            service.StateChanged -= Render;
            service.PreviewChanged -= Render;
            service = null;
        }
        public void Dispose()
        {
            if (isDisposed) return;
            isDisposed = true;
            Unsubscribe();
            publicLoadout = null;
            renderer.Clear();
        }
        private void EnsureActive() { if (isDisposed) throw new ObjectDisposedException(nameof(EquipmentVisualPresenter)); }
    }
}
