internal static class AppReport
{
    private const int CustomerCount = 1_000;
    private static readonly Lazy<Sale[]> Sales = new(() => Enumerable.Range(1, 200_000)
        .Select(id => new Sale((id * 17 + id / 31) % CustomerCount, 100 + id * 37 % 100_000)).ToArray());

    public static object Calculate(bool slow, CancellationToken cancellationToken)
    {
        var totals = new long[CustomerCount];
        var sales = Sales.Value;
        // Both versions calculate the same totals: scan all sales once per customer, or bucket each sale in one pass.
        if (slow)
        {
            for (var customer = 0; customer < totals.Length; customer++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var sale in sales)
                    if (sale.Customer == customer) totals[customer] += sale.AmountCents;
            }
        }
        else
        {
            for (var i = 0; i < sales.Length; i++)
            {
                if ((i & 4095) == 0) cancellationToken.ThrowIfCancellationRequested();
                var sale = sales[i];
                totals[sale.Customer] += sale.AmountCents;
            }
        }
        return new
        {
            Customers = totals.Length,
            Events = sales.Length,
            AmountCents = totals.Sum(),
            TopCustomer = Array.IndexOf(totals, totals.Max()),
            TopCustomerAmountCents = totals.Max()
        };
    }

    private readonly record struct Sale(int Customer, int AmountCents);
}
