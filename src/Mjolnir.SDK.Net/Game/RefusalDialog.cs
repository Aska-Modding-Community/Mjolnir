using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Il2CppInterop.Runtime;
using Mjolnir.SDK.Threading;
using SandSailorStudio.UI;
using SSSGame.UI;
using UnityEngine;

namespace Mjolnir.SDK.Net;

internal static partial class GameNetwork
{
    /// <summary>The refusal in the game's own message dialog.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void ShowRefusal(CompatibilityReport report)
    {
        var (title, message) = NetTexts.Refusal(report);
        var parameters = new ConfirmActionMenu.ConfirmActionMenuParameters
        {
            Title = title,
            Message = message,
            ConfirmMessage = NetTexts.Get("refusal.ok"),
            CancelMessage = NetTexts.Get("refusal.ok"),
            HideCancelButton = true,
            CloseOnConfirm = true,
        };
        var fit = new DialogFit();
        parameters.OnConfirm = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ConfirmActionMenu>>(new Action<ConfirmActionMenu>(_ => fit.Restore()))!;
        parameters.OnCancel = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ConfirmActionMenu>>(new Action<ConfirmActionMenu>(_ => fit.Restore()))!;
        Menu.Show<ConfirmActionMenu>(parameters);
        Dispatcher.Enqueue(() => fit.Apply(message, frames: 120), "Net refusal fit");
        SdkLog.Info(NetTexts.Source, $"Refusal shown: {report.Summary}");
    }

    /// <summary>
    /// The game's dialog is laid out for a sentence or two (a 435 × 135 panel): a list of mods needs it wider and as tall
    /// as its message. Applied once the dialog shows the message. The game destroys a dialog when it closes (seen in game,
    /// 2026-11-07); should it ever keep one for its next question, closing gives it its own size back.
    /// </summary>
    private sealed class DialogFit
    {
        private const float Width = 640;
        private RectTransform? _panel, _title;
        private Vector2 _panelSize, _titleSize;
        private (RectTransform Rect, Vector2 Position)[] _icons = [];

        public void Apply(string message, int frames)
        {
            var menu = Menu.GetInstance<ConfirmActionMenu>();
            var text = menu?.MessageRect?.GetComponent<TMPro.TMP_Text>();
            if (menu == null || text == null || text.text != message)
            {
                if (frames > 0) Dispatcher.Enqueue(() => Apply(message, frames - 1), "Net refusal fit");
                return;
            }
            try
            {
                var panel = menu.MessageRect.parent.TryCast<RectTransform>();
                if (panel == null) return;
                _panel = panel;
                _panelSize = panel.sizeDelta;
                float width = Math.Max(Width, _panelSize.x);
                float messageWidth = width + menu.MessageRect.sizeDelta.x;
                float messageHeight = _panelSize.y + menu.MessageRect.sizeDelta.y;
                float needed = text.GetPreferredValues(message, messageWidth, 0).y + 8;
                panel.sizeDelta = new Vector2(width, _panelSize.y + Math.Max(0, needed - messageHeight));
                if (menu.TitleRect != null)
                {
                    _title = menu.TitleRect;
                    _titleSize = _title.sizeDelta;
                    _title.sizeDelta = new Vector2(width - 40, _titleSize.y);
                }
                // The decorations stand at fixed places for the game's width: moved in proportion.
                _icons = [.. Enumerable.Range(0, panel.childCount).Select(i => panel.GetChild(i).TryCast<RectTransform>())
                    .Where(r => r != null && r.anchorMin.x == 0 && r.anchorMax.x == 0 && r.anchoredPosition.x > 0)
                    .Select(r => (r!, r!.anchoredPosition))];
                foreach (var (rect, position) in _icons) rect.anchoredPosition = new Vector2(position.x * width / _panelSize.x, position.y);
            }
            catch (Exception e)
            {
                SdkLog.Warn(NetTexts.Source, $"The refusal's dialog was not fitted to its message: {e.Message}");
            }
        }

        public void Restore()
        {
            try
            {
                if (_panel != null) _panel.sizeDelta = _panelSize;
                if (_title != null) _title.sizeDelta = _titleSize;
                foreach (var (rect, position) in _icons) rect.anchoredPosition = position;
            }
            catch (Exception e)
            {
                SdkLog.Warn(NetTexts.Source, $"The dialog's own size was not restored: {e.Message}");
            }
            _panel = _title = null;
            _icons = [];
        }
    }
}
