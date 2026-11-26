using Microsoft.Data.SqlClient;

internal static class SqlReport
{
    public static async Task<ReportResult> RunAsync(IConfiguration configuration, bool slow, bool repeat, CancellationToken cancellationToken)
    {
        await using var connection = new SqlConnection(configuration.GetConnectionString("SalesEvents"));
        if (!OperatingSystem.IsWindows() && connection.DataSource.TrimStart().StartsWith("(localdb)\\", StringComparison.OrdinalIgnoreCase))
            throw new LocalDbUnavailableException();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandTimeout = 10;
        // Converting the indexed column usually forces a scan. Comparing it directly to an int lets SQL Server seek the key.
        command.CommandText = slow
            ? "SELECT EventId, CustomerId, AmountCents, Payload FROM dbo.SalesEvents WHERE CONVERT(varchar(12), EventId) = '125001';"
            : "SELECT EventId, CustomerId, AmountCents, Payload FROM dbo.SalesEvents WHERE EventId = @EventId;";
        if (!slow) command.Parameters.Add("@EventId", System.Data.SqlDbType.Int).Value = 125001;
        // Repeat for the lab only when the configured SQL source also looks local.
        return await ReportComparison.RunPassesAsync(async token =>
        {
            await using var reader = await command.ExecuteReaderAsync(token);
            return await reader.ReadAsync(token)
                ? new { EventId = reader.GetInt32(0), CustomerId = reader.GetInt32(1), AmountCents = reader.GetInt32(2), PayloadLength = reader.GetString(3).Length }
                : null;
        }, repeat && IsLocalSqlSource(connection.DataSource), cancellationToken);
    }

    private static bool IsLocalSqlSource(string server)
    {
        server = server.Trim();
        if (server.StartsWith("(localdb)\\", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var protocol in new[] { "tcp:", "lpc:" })
            if (server.StartsWith(protocol, StringComparison.OrdinalIgnoreCase)) server = server[protocol.Length..];
        var host = server.Split('\\', ',')[0].Trim();
        return host is "." || host.Equals("(local)", StringComparison.OrdinalIgnoreCase) || LabAccess.IsLoopbackHost(host);
    }
}

internal sealed class LocalDbUnavailableException() : InvalidOperationException("SQL Server LocalDB requires Windows.") { }
