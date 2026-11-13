namespace Mjolnir.UiExample.ViewModels;

/// <summary>A row of the tabs window's list: what it is, how much there is, and the text its details show.</summary>
internal sealed record Supply(string Name, int Amount, string Text);
