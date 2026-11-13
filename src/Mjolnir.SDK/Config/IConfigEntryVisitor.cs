namespace Mjolnir.SDK.Config;

internal interface IConfigEntryVisitor<out TResult>
{
    TResult Visit<T>(ConfigEntry<T> entry);
}
