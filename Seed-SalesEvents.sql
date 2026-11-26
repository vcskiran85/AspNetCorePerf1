-- Connect directly to an already provisioned, dedicated AspNetPerfDB database.
-- For SQL Server Express service or Azure SQL Database; no database DDL.
IF DB_NAME() <> N'AspNetPerfDB'
BEGIN
    RAISERROR ('Connect directly to the dedicated AspNetPerfDB sample database.', 16, 1);
    RETURN;
END;
IF @@TRANCOUNT <> 0 OR (@@OPTIONS & 2) = 2
BEGIN
    RAISERROR ('Use a fresh connection with no active or implicit transaction.', 16, 1);
    RETURN;
END;
SET NOCOUNT ON;
SET XACT_ABORT ON;

BEGIN TRY
    BEGIN TRANSACTION;
    DECLARE @lockResult int, @created bit = 0;
    EXEC @lockResult = sys.sp_getapplock
        @Resource = N'DOTNET.Sample.SalesEvents.Seed', @LockMode = 'Exclusive',
        @LockOwner = 'Transaction', @LockTimeout = 15000;
    IF @lockResult < 0
        THROW 51000, 'Could not acquire the seed lock; retry after the other setup finishes.', 1;

    IF OBJECT_ID(N'dbo.SalesEvents') IS NULL
    BEGIN
        CREATE TABLE dbo.SalesEvents
        (
            EventId     int NOT NULL PRIMARY KEY CLUSTERED,
            CustomerId  int NOT NULL,
            AmountCents int NOT NULL,
            Payload     varchar(400) NOT NULL
        );
        SET @created = 1;

        -- The decimal digits enumerate IDs 1 through 250000 without catalog dependencies.
        EXEC sys.sp_executesql N'
            ;WITH Digits(n) AS
            (
                SELECT n FROM (VALUES (0),(1),(2),(3),(4),(5),(6),(7),(8),(9)) AS d(n)
            ), EventIds(EventId) AS
            (
                SELECT 1 + a.n + 10*b.n + 100*c.n + 1000*d.n + 10000*e.n + 100000*f.n
                FROM Digits AS a CROSS JOIN Digits AS b CROSS JOIN Digits AS c
                CROSS JOIN Digits AS d CROSS JOIN Digits AS e
                CROSS JOIN (VALUES (0),(1),(2)) AS f(n)
            )
            INSERT dbo.SalesEvents (EventId, CustomerId, AmountCents, Payload)
            SELECT EventId, 1 + ((EventId - 1) % 10000), 100 + ((EventId * 37) % 100000),
                   RIGHT(''000000'' + CONVERT(varchar(6), EventId), 6)
                       + REPLICATE(CHAR(65 + EventId % 26), 394)
            FROM EventIds
            WHERE EventId <= 250000;';
    END;

    DECLARE @tableId int = OBJECT_ID(N'dbo.SalesEvents', N'U');
    IF @tableId IS NULL
        OR (SELECT COUNT(*) FROM sys.columns WHERE object_id = @tableId) <> 4
        OR (SELECT COUNT(*) FROM sys.columns
            WHERE object_id = @tableId AND is_nullable = 0
              AND is_identity = 0 AND is_computed = 0
              AND ((name IN (N'EventId', N'CustomerId', N'AmountCents') AND user_type_id = TYPE_ID(N'int'))
                OR (name = N'Payload' AND user_type_id = TYPE_ID(N'varchar') AND max_length = 400))) <> 4
        OR NOT EXISTS
        (
            SELECT 1
            FROM sys.indexes AS i
            JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
            JOIN sys.columns AS c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE i.object_id = @tableId AND i.is_primary_key = 1 AND i.type = 1 AND i.is_disabled = 0
              AND ic.key_ordinal = 1 AND c.name = N'EventId'
              AND NOT EXISTS (SELECT 1 FROM sys.index_columns AS k
                              WHERE k.object_id = i.object_id AND k.index_id = i.index_id AND k.key_ordinal > 1)
        )
        THROW 51001, 'Existing dbo.SalesEvents has a different schema; it was not changed.', 1;

    -- With a unique key, the count and endpoints prove density. Check values without overwriting them.
    DECLARE @verified bit;
    EXEC sys.sp_executesql N'
        SELECT @verified = CASE WHEN COUNT_BIG(*) = 250000
            AND MIN(EventId) = 1 AND MAX(EventId) = 250000
            AND MAX(CASE WHEN CustomerId <> 1 + ((CONVERT(bigint, EventId) - 1) % 10000)
                          OR AmountCents <> 100 + ((CONVERT(bigint, EventId) * 37) % 100000)
                          OR DATALENGTH(Payload) <> 400
                          OR CONVERT(varbinary(400), Payload) <> CONVERT(varbinary(400),
                              RIGHT(''000000'' + CONVERT(varchar(6), EventId), 6)
                                  + REPLICATE(CHAR(65 + EventId % 26), 394))
                         THEN 1 ELSE 0 END) = 0
            THEN 1 ELSE 0 END
        FROM dbo.SalesEvents;', N'@verified bit OUTPUT', @verified = @verified OUTPUT;
    IF @verified <> 1
        THROW 51002, 'dbo.SalesEvents differs from the sample seed; no existing data was changed.', 1;

    COMMIT TRANSACTION;
    SELECT DB_NAME() AS DatabaseName, @created AS CreatedAndSeeded,
           250000 AS VerifiedRows, 125001 AS ReportEventId;
END TRY
BEGIN CATCH
    IF XACT_STATE() <> 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
GO
