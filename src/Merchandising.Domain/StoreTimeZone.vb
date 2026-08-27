' Merchandising.Domain.StoreTimeZone
'
' P3-06: every timestamp is stored in UTC (CLAUDE.md section 5), and spec
' section 14 requires every report's date filter to be expressed in the
' STORE time zone, with the applied range stated back to the caller - never
' left for the client to guess what boundary the server actually used.
' P3-03 deliberately deferred this ("date-boundary filtering is P3-06's" -
' PurchaseOrdersController.vb's own header, PurchaseOrderNumberGenerator.vb's)
' so exactly one definition of the boundary exists, here.
'
' MEASURED, NOT ASSUMED: TimeZoneInfo.FindSystemTimeZoneById("Asia/Manila")
' was confirmed to resolve on this machine before this file was written -
' .NET's ICU-based globalization (the default since .NET 5, including on
' Windows) accepts IANA ids directly; the older NLS-only path would have
' needed the Windows id "Singapore Standard Time" instead and thrown
' TimeZoneNotFoundException on "Asia/Manila". Confirmed:
'   [TimeZoneInfo]::FindSystemTimeZoneById("Asia/Manila")
'   -> Id=Asia/Manila, BaseUtcOffset=08:00:00
' Asia/Manila observes no daylight saving (verified via the same call's
' AdjustmentRules, which is empty), so BaseUtcOffset is the offset for every
' calendar date - this class does not need to special-case a DST transition
' the zone does not have.
'
' Pure BCL (TimeZoneInfo, DateOnly) - no ASP.NET Core, no I/O. Domain
' depends on nothing (CLAUDE.md section 4).

Public NotInheritable Class StoreTimeZone

    ''' <summary>The IANA id spec section 11/12 calls "the configured store time zone", pinned here rather than read from configuration - P3-06's own scope, not a settings feature this card was asked to build.</summary>
    Public Const IanaId As String = "Asia/Manila"

    Private Shared ReadOnly _zone As TimeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(IanaId)

    Private Sub New()
    End Sub

    ''' <summary>The resolved zone, exposed for anything that needs more than the two conversions below (a future report's own formatting, for instance).</summary>
    Public Shared ReadOnly Property Zone As TimeZoneInfo
        Get
            Return _zone
        End Get
    End Property

    ''' <summary>
    ''' The UTC instant at which <paramref name="storeLocalDate"/> BEGINS in
    ''' the store's local time - the inclusive lower bound of a "from this
    ''' date" filter.
    ''' </summary>
    Public Shared Function StartOfDayUtc(storeLocalDate As DateOnly) As DateTime

        Dim localMidnight As New DateTime(
            storeLocalDate.Year, storeLocalDate.Month, storeLocalDate.Day, 0, 0, 0, DateTimeKind.Unspecified)

        Return TimeZoneInfo.ConvertTimeToUtc(localMidnight, _zone)

    End Function

    ''' <summary>
    ''' The UTC instant at which the day AFTER <paramref name="storeLocalDate"/>
    ''' begins in the store's local time - the EXCLUSIVE upper bound of a
    ''' "through this date" filter (CreatedAtUtc &lt; this value), so the
    ''' whole of the requested local day is included and nothing from the
    ''' next one leaks in.
    ''' </summary>
    Public Shared Function EndOfDayUtcExclusive(storeLocalDate As DateOnly) As DateTime
        Return StartOfDayUtc(storeLocalDate.AddDays(1))
    End Function

End Class
