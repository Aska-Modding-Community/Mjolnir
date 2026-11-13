using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using SandSailorStudio.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using Il2CppInterop.Runtime;
using GameContextMenu = SSSGame.UI.ContextMenu;
using Object = UnityEngine.Object;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

// A window's Unity objects: its chrome in the game's two styles, the header, the scroll view and the footer, and the drag.
public partial class UiWindow
{
    // A drag on the panel itself (its frame, its title, the gaps between its parts) moves the window: the pointer's move
    // in screen pixels over the canvas's scale. A press on a part that takes drags itself never reaches the panel.
    private void MakeDraggable(GameObject panel, Canvas canvas)
    {
        var trigger = panel.AddComponent<EventTrigger>();
        On(EventTriggerType.Drag, data =>
        {
            if (data.TryCast<PointerEventData>() is not { } pointer || pointer.button != PointerEventData.InputButton.Left) return;
            float scale = canvas.scaleFactor > 0 ? canvas.scaleFactor : 1;
            _offset += pointer.delta / scale;
            Resize();
        });
        On(EventTriggerType.EndDrag, _ => DragEnded?.Invoke());

        void On(EventTriggerType type, Action<BaseEventData> handler)
        {
            var entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(DelegateSupport.ConvertDelegate<UnityAction<BaseEventData>>(
                new Action<BaseEventData>(data => GameUi.Guard($"{this} drag", () => handler(data)))));
            trigger.triggers.Add(entry);
        }
    }

    // The game's main windows (the settings menu's parts): the world blurred and darkened behind
    // the window over the whole screen, the frame with its corner ornaments, the X in its diamond, the header on the top edge.
    private void BuildMenuChrome(Transform root, RectTransform panel)
    {
        foreach (var part in new[] { Theme.MenuVignette, Theme.MenuBlur })
        {
            if (part == null) continue;
            var behind = Object.Instantiate(part, root, false);
            behind.SetActive(true);
            behind.transform.SetAsFirstSibling(); // the blur first, the vignette over it, the window over both
            foreach (var graphic in behind.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        }
        var chrome = Object.Instantiate(Theme.MenuChrome!, panel, false);
        chrome.name = "Chrome";
        chrome.SetActive(true);
        chrome.transform.SetAsFirstSibling();
        foreach (var graphic in chrome.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        if (Theme.MenuCloseButton is { } template)
        {
            var close = Object.Instantiate(template, panel, false);
            close.name = "Close";
            close.SetActive(true);
            close.GetComponent<Button>()?.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityAction>(new Action(() => GameUi.Guard($"{this}.Close", Close))));
        }
        BuildHeader(panel);
    }

    // On the top edge, centred: the title on the inventory's plate between the settings menu's ornaments, or the content's
    // tabs in the plate's place with the selected tab's name under them.
    private void BuildHeader(RectTransform panel)
    {
        var header = ViewFactory.NewRect(panel, "Header");
        header.anchorMin = header.anchorMax = new Vector2(0.5f, 1);
        header.pivot = new Vector2(0.5f, 0.5f);
        header.anchoredPosition = Vector2.zero;
        ViewFactory.Row(header, 3, TextAnchor.MiddleCenter);
        var fitter = header.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        HeaderPart(Theme.HeaderLeft, header);
        _plate = Theme.HeaderPlate != null ? Object.Instantiate(Theme.HeaderPlate, header, false).GetComponent<RectTransform>() : ViewFactory.NewRect(header, "Plate");
        _plate.name = "Plate";
        _plate.gameObject.SetActive(true);
        if (_plate.GetComponent<Image>() == null) _plate.gameObject.AddComponent<Image>().color = new Color(0.12f, 0.13f, 0.13f, 0.95f);
        ViewFactory.Row(_plate, align: TextAnchor.MiddleCenter).padding = new RectOffset(28, 28, 0, 0);
        var plateSize = _plate.gameObject.GetComponent<LayoutElement>() ?? _plate.gameObject.AddComponent<LayoutElement>();
        plateSize.minHeight = plateSize.preferredHeight = HeaderHeight;
        _titleLabel = ViewFactory.NewText(_plate, "Title", Title, Theme.TitleFont, 20);
        _titleLabel.color = TitleColor;
        _titleLabel.textWrappingMode = TextWrappingModes.NoWrap;
        _titleLabel.raycastTarget = false;

        _tabsRect = ViewFactory.NewRect(header, "Tabs");
        ViewFactory.Row(_tabsRect, align: TextAnchor.MiddleCenter);
        _tabsView = new ContainerView(_tabsRect);
        HeaderPart(Theme.HeaderRight, header);

        _subtitle = ViewFactory.NewText(panel, "Subtitle", "", Theme.TextFont, 14);
        _subtitle.color = SubtitleColor;
        _subtitle.raycastTarget = false;
        var subtitle = _subtitle.rectTransform;
        subtitle.anchorMin = new Vector2(0, 1);
        subtitle.anchorMax = new Vector2(1, 1);
        subtitle.pivot = new Vector2(0.5f, 1);
        subtitle.sizeDelta = new Vector2(-48, SubtitleHeight);
        subtitle.anchoredPosition = new Vector2(0, -(TabsHeight / 2 + 4));
        ShowHeader();
    }

    // An ornament of the header at its own size.
    private static void HeaderPart(GameObject? template, RectTransform header)
    {
        if (template == null) return;
        var part = Object.Instantiate(template, header, false);
        part.SetActive(true);
        var rect = part.GetComponent<RectTransform>();
        var size = part.GetComponent<LayoutElement>() ?? part.AddComponent<LayoutElement>();
        size.preferredWidth = rect.sizeDelta.x;
        size.preferredHeight = rect.sizeDelta.y;
        foreach (var graphic in part.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
    }

    // The game's dialogs (its Window_Dark kept whole, a little larger than the panel as in the dialog), the title inside.
    private void BuildDialogChrome(RectTransform panel, Image catcher)
    {
        if (Theme.DialogChrome is { } template)
        {
            var chrome = Object.Instantiate(template, panel, false);
            chrome.name = "Chrome";
            chrome.SetActive(true);
            chrome.transform.SetAsFirstSibling();
            foreach (var graphic in chrome.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
        }
        else
        {
            catcher.color = new Color(0.08f, 0.09f, 0.09f, 0.95f);
        }
        _titleLabel = ViewFactory.NewText(panel, "Title", Title, Theme.TitleFont, 30);
        _titleLabel.fontStyle = FontStyles.Bold;
        _titleLabel.raycastTarget = false;
        var titleRect = _titleLabel.rectTransform;
        titleRect.anchorMin = new Vector2(0, 1);
        titleRect.anchorMax = new Vector2(1, 1);
        titleRect.pivot = new Vector2(0.5f, 1);
        titleRect.sizeDelta = new Vector2(-48, TitleHeight);
        titleRect.anchoredPosition = new Vector2(0, -TitleTop);
    }

    // The settings menu's scroll view (ViewFactory.NewScrollView), stretched over the window below the title.
    private RectTransform BuildScrollView(RectTransform panel)
    {
        var (area, scroll, content) = ViewFactory.NewScrollView(panel, "Scroll", (int)Margin, spacing: 10);
        _scroll = scroll;
        _scrollArea = area;
        ViewFactory.Stretch(area);
        area.offsetMin = new Vector2(24, ScrollBottom);
        area.offsetMax = new Vector2(-24, -ScrollTop);
        return content;
    }

    // The footer: a column along the window's bottom, as wide as the content, as tall as what it holds.
    private RectTransform BuildFooter(RectTransform panel)
    {
        var footer = ViewFactory.NewRect(panel, "Footer");
        _footerRect = footer;
        footer.anchorMin = new Vector2(0, 0);
        footer.anchorMax = new Vector2(1, 0);
        footer.pivot = new Vector2(0.5f, 0);
        footer.sizeDelta = new Vector2(-48, 0);
        footer.anchoredPosition = new Vector2(0, ScrollBottom);
        ViewFactory.Column(footer, align: TextAnchor.UpperCenter).padding = new RectOffset((int)Margin, (int)Margin, 0, 0);
        footer.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return footer;
    }
}
