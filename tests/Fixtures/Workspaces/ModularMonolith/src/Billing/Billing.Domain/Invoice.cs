namespace Billing.Domain;

public partial class Invoice<T>
{
    public sealed class Line<TItem>;
    private sealed class PrivateLine;
}

internal class Hidden
{
    public class Nested;
}

public record InvoiceRecord(decimal Total);
public readonly record struct Money(decimal Amount);
public enum InvoiceState { Draft, Paid }
public delegate void InvoiceChanged();
file class LocalHelper
{
    public class Nested;
}
