using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Il2CppInterop.Runtime;
using SandSailorStudio.UI;
using SSSGame;
using SSSGame.UI;
using Mjolnir.SDK.Threading;

namespace Mjolnir.SDK.UI;

/// <summary>
/// The game's own dialog box (ConfirmActionMenu): a title, a message and one or two buttons, or a text field
/// (<see cref="Prompt"/>). Texts are shown as given (no localization keys). Needs phase B
/// (<see cref="Mod.OnGameReady"/> or later); called from another thread, it shows on the next frame. The …Async forms
/// give the answer as a task: awaited on the main thread, your code goes on there after the dialog closed, at the start
/// of a later frame (<c>if (await Dialog.ConfirmAsync("Reset", "Sure?")) Reset();</c>).
/// </summary>
public static class Dialog
{
    /// <summary>A question with two buttons. Esc counts as cancel.</summary>
    /// <param name="title">The dialog's title.</param>
    /// <param name="message">The question.</param>
    /// <param name="onConfirm">Called when the player confirms; if it throws, the calling mod is disabled.</param>
    /// <param name="onCancel">Called on cancel or Esc.</param>
    /// <param name="confirmText">The confirm button's text; null for "OK" in the player's language.</param>
    /// <param name="cancelText">The cancel button's text; null for the game's own "Cancel".</param>
    public static void Confirm(string title, string message, Action? onConfirm, Action? onCancel = null,
                               string? confirmText = null, string? cancelText = null) =>
        Show(new Request(title, message, confirmText, cancelText, HideCancel: false), _ => ModCall.InvokeEach(onConfirm, $"Dialog '{title}' confirm"),
             () => ModCall.InvokeEach(onCancel, $"Dialog '{title}' cancel"), notShown: null);

    /// <summary>
    /// A question with two buttons, answered by a task: true when the player confirms, false on cancel or Esc (and when
    /// the dialog cannot be shown: before the game is ready, on a dedicated server).
    /// </summary>
    /// <param name="title">The dialog's title.</param>
    /// <param name="message">The question.</param>
    /// <param name="confirmText">The confirm button's text; null for "OK" in the player's language.</param>
    /// <param name="cancelText">The cancel button's text; null for the game's own "Cancel".</param>
    public static Task<bool> ConfirmAsync(string title, string message, string? confirmText = null, string? cancelText = null)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Show(new Request(title, message, confirmText, cancelText, HideCancel: false), _ => answer.TrySetResult(true),
             () => answer.TrySetResult(false), notShown: () => answer.TrySetResult(false));
        return answer.Task;
    }

    /// <summary>A message with one button. <paramref name="onClose"/> is called however the dialog closes.</summary>
    /// <param name="title">The dialog's title.</param>
    /// <param name="message">The message.</param>
    /// <param name="onClose">Called when the dialog closes, by its button or by Esc.</param>
    /// <param name="okText">The button's text; null for "OK" in the player's language.</param>
    public static void Message(string title, string message, Action? onClose = null, string? okText = null) =>
        Show(new Request(title, message, okText, okText, HideCancel: true), _ => ModCall.InvokeEach(onClose, $"Dialog '{title}' close"),
             () => ModCall.InvokeEach(onClose, $"Dialog '{title}' close"), notShown: null);

    /// <summary>A message with one button, as a task that completes when the dialog closes (at once if it cannot be shown).</summary>
    /// <param name="title">The dialog's title.</param>
    /// <param name="message">The message.</param>
    /// <param name="okText">The button's text; null for "OK" in the player's language.</param>
    public static Task MessageAsync(string title, string message, string? okText = null)
    {
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Show(new Request(title, message, okText, okText, HideCancel: true), _ => closed.TrySetResult(), () => closed.TrySetResult(),
             notShown: () => closed.TrySetResult());
        return closed.Task;
    }

    /// <summary>
    /// A question with a text field (the game's own, as it asks for a settlement's name): <paramref name="onConfirm"/>
    /// gets the text the player confirmed. Esc counts as cancel.
    /// </summary>
    /// <param name="title">The dialog's title.</param>
    /// <param name="message">The question.</param>
    /// <param name="onConfirm">Called with the text when the player confirms; if it throws, the calling mod is disabled.</param>
    /// <param name="onCancel">Called on cancel or Esc.</param>
    /// <param name="text">The text the field starts with.</param>
    /// <param name="placeholder">The grey hint shown while the field is empty.</param>
    /// <param name="maxLength">The most characters the player can type; 0 for no limit.</param>
    /// <param name="confirmText">The confirm button's text; null for "OK" in the player's language.</param>
    /// <param name="cancelText">The cancel button's text; null for the game's own "Cancel".</param>
    public static void Prompt(string title, string message, Action<string> onConfirm, Action? onCancel = null, string text = "",
                              string? placeholder = null, int maxLength = 0, string? confirmText = null, string? cancelText = null)
    {
        ArgumentNullException.ThrowIfNull(onConfirm);
        Show(new Request(title, message, confirmText, cancelText, HideCancel: false, Input: new InputRequest(text, placeholder, maxLength)),
             typed => ModCall.InvokeEach(onConfirm, typed ?? "", $"Dialog '{title}' confirm"), () => ModCall.InvokeEach(onCancel, $"Dialog '{title}' cancel"),
             notShown: null);
    }

    /// <summary>
    /// A question with a text field, answered by a task: the text the player confirmed, or null on cancel or Esc (and when
    /// the dialog cannot be shown).
    /// </summary>
    /// <param name="title">The dialog's title.</param>
    /// <param name="message">The question.</param>
    /// <param name="text">The text the field starts with.</param>
    /// <param name="placeholder">The grey hint shown while the field is empty.</param>
    /// <param name="maxLength">The most characters the player can type; 0 for no limit.</param>
    /// <param name="confirmText">The confirm button's text; null for "OK" in the player's language.</param>
    /// <param name="cancelText">The cancel button's text; null for the game's own "Cancel".</param>
    public static Task<string?> PromptAsync(string title, string message, string text = "", string? placeholder = null, int maxLength = 0,
                                            string? confirmText = null, string? cancelText = null)
    {
        var answer = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        Show(new Request(title, message, confirmText, cancelText, HideCancel: false, Input: new InputRequest(text, placeholder, maxLength)),
             typed => answer.TrySetResult(typed ?? ""), () => answer.TrySetResult(null), notShown: () => answer.TrySetResult(null));
        return answer.Task;
    }

    private static void Show(Request request, Action<string?> confirmed, Action cancelled, Action? notShown)
    {
        GameUi.EnsureInstalled();
        Dispatcher.Run(() =>
        {
            bool shown = false;
            if (GameUi.CanUseUnity("Dialog")) GameUi.Guard("Dialog", () => shown = ShowDialog(request, confirmed, cancelled));
            if (!shown) notShown?.Invoke();
        });
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ShowDialog(Request request, Action<string?> confirmed, Action cancelled)
    {
        ConfirmActionMenu.ConfirmActionMenuParameters parameters = request.Input is { } input
            ? new InputFieldConfirmActionMenu.InputFieldConfirmActionMenuParameters
            {
                defaultText = input.Text ?? "",
                placeholderText = input.Placeholder ?? "",
                characterLimit = Math.Max(0, input.MaxLength),
                checkForProfanities = false,
            }
            : new ConfirmActionMenu.ConfirmActionMenuParameters();
        parameters.Title = request.Title;
        parameters.Message = request.Message;
        parameters.ConfirmMessage = request.ConfirmText ?? SdkTexts.Get("dialog.ok");
        parameters.CancelMessage = request.CancelText ?? Theme.Loc("common.cancel", SdkTexts.Get("dialog.cancel"));
        parameters.HideCancelButton = request.HideCancel;
        parameters.CloseOnConfirm = true;
        // The game's callbacks run on the main thread, once each; the dialog is closing then.
        parameters.OnConfirm = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ConfirmActionMenu>>(
            new Action<ConfirmActionMenu>(menu => GameUi.Guard("Dialog confirm", () => confirmed(request.Input == null ? null : TypedText(menu)))))!;
        parameters.OnCancel = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<ConfirmActionMenu>>(
            new Action<ConfirmActionMenu>(_ => GameUi.Guard("Dialog cancel", cancelled)))!;
        if (request.Input != null) Menu.Show<InputFieldConfirmActionMenu>(parameters);
        else Menu.Show<ConfirmActionMenu>(parameters);
        return true;
    }

    // The text field of the game's input dialog (InputFieldConfirmActionMenu.InputFieldHelper.inputField).
    private static string TypedText(ConfirmActionMenu menu) => menu.TryCast<InputFieldConfirmActionMenu>()?.InputFieldHelper?.inputField?.text ?? "";

    private sealed record InputRequest(string? Text, string? Placeholder, int MaxLength);

    private sealed record Request(string Title, string Message, string? ConfirmText, string? CancelText, bool HideCancel, InputRequest? Input = null);
}
